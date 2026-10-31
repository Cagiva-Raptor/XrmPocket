using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using XrmPocket.Api.Configuration;

namespace XrmPocket.Api.Connections;

/// <summary>
/// Interactieve aanmelding met je eigen Microsoft-account (OAuth authorization code
/// met PKCE), zoals XrmToolBox dat met "Office365"-verbindingen doet. De aanmelding
/// gebeurt in de browser van de gebruiker; de backend bewaart alleen het
/// refresh-token (chmod 600) en haalt daarmee access tokens op.
/// </summary>
public sealed class InteractiveAuthService
{
    private static readonly HttpClient Http = new();
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(15);

    private readonly XrmPocketOptions _options;
    private readonly ConnectionStore _store;
    private readonly ILogger<InteractiveAuthService> _logger;
    private readonly ConcurrentDictionary<string, PendingSignIn> _pending = new(StringComparer.OrdinalIgnoreCase);

    public InteractiveAuthService(IOptions<XrmPocketOptions> options, ConnectionStore store, ILogger<InteractiveAuthService> logger)
    {
        _options = options.Value;
        _store = store;
        _logger = logger;
    }

    public string RedirectUri => _options.InteractiveRedirectUri;
    public string AppBaseUrl => _options.AppBaseUrl;

    /// <summary>Waar: er is een refresh-token voor deze verbinding bewaard.</summary>
    public bool IsSignedIn(ConnectionProfile profile) => LoadTokens(profile)?.RefreshToken != null;

    public string? Account(ConnectionProfile profile) => LoadTokens(profile)?.Account;

    public DateTimeOffset? ExpiresOn(ConnectionProfile profile) => LoadTokens(profile)?.ExpiresOn;

    /// <summary>
    /// Bouwt de autorisatie-URL. De gebruiker opent die in de browser en meldt zich
    /// daar aan; Entra stuurt daarna terug naar de redirect-URI.
    /// </summary>
    /// <summary>
    /// Start de aanmelding. <paramref name="requestedOrigin"/> is het adres waarop de
    /// gebruiker de app opende (localhost, LAN of Tailscale); het moet in de
    /// allow-list staan, want de callback-URL moet exact overeenkomen met wat er in de
    /// app registration staat.
    /// </summary>
    public SignInStart StartSignIn(ConnectionProfile profile, string? requestedOrigin = null)
    {
        if (string.IsNullOrWhiteSpace(profile.ClientId))
            throw new InvalidOperationException("Deze verbinding heeft geen client-ID.");
        if (string.IsNullOrWhiteSpace(profile.TenantId))
            throw new InvalidOperationException("Deze verbinding heeft geen tenant-ID.");
        if (string.IsNullOrWhiteSpace(profile.Url))
            throw new InvalidOperationException("Deze verbinding heeft geen URL.");

        CleanupPending();

        var origin = ResolveOrigin(requestedOrigin);
        var redirectUri = origin.TrimEnd('/') + "/api/auth/callback";

        var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        _pending[state] = new PendingSignIn(profile.Id, verifier, redirectUri, origin, DateTimeOffset.UtcNow.Add(PendingLifetime));

        var query = new Dictionary<string, string>
        {
            ["client_id"] = profile.ClientId!,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["response_mode"] = "query",
            ["scope"] = $"{ScopeFor(profile)} offline_access openid profile",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["prompt"] = "select_account"
        };

        var url = $"https://login.microsoftonline.com/{profile.TenantId}/oauth2/v2.0/authorize?" +
                  string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return new SignInStart(profile.Id, url, state, redirectUri);
    }

    /// <summary>
    /// Verwerkt de terugkeer van Entra: code inwisselen voor tokens en bewaren.
    /// Geeft de verbinding-id terug (of een foutmelding voor de UI).
    /// </summary>
    public async Task<SignInResult> HandleCallbackAsync(string? code, string? state, string? error, string? errorDescription, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(error))
        {
            return SignInResult.Failed(null, error, TranslateError(error, errorDescription));
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return SignInResult.Failed(null, "invalid_request", "De aanmelding kwam terug zonder code of state.");

        if (!_pending.TryRemove(state, out var pending) || pending.ExpiresOn < DateTimeOffset.UtcNow)
            return SignInResult.Failed(null, "invalid_state", "De aanmelding is verlopen of hoort bij een andere sessie. Probeer opnieuw.");

        var profile = _store.Get(pending.ConnectionId);

        if (profile == null)
            return SignInResult.Failed(pending.ConnectionId, "unknown_connection", "De verbinding bestaat niet meer.");

        var tokens = await ExchangeCodeAsync(profile, code, pending.CodeVerifier, pending.RedirectUri, cancellationToken);

        if (tokens == null)
            return SignInResult.Failed(profile.Id, "token_failed", "Tokens ophalen mislukte.");

        SaveTokens(profile, tokens);
        _logger.LogInformation("Interactieve aanmelding gelukt voor verbinding {Connection} ({Account})", profile.Name, tokens.Account);

        return SignInResult.Succeeded(profile.Id, tokens.Account);
    }

    /// <summary>Geeft een geldig access token voor de omgeving van deze verbinding.</summary>
    public async Task<string> GetAccessTokenAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        var tokens = LoadTokens(profile)
            ?? throw new ConnectionNotConfiguredException("Nog niet aangemeld: meld aan met je Microsoft-account.");

        if (tokens.RefreshToken == null)
            throw new ConnectionNotConfiguredException("Nog niet aangemeld: meld aan met je Microsoft-account.");

        // Ruim voor het verlopen verversen, zodat een query niet halverwege stukloopt.
        if (tokens.AccessToken != null && tokens.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(2))
            return tokens.AccessToken;

        var refreshed = await RefreshAsync(profile, tokens.RefreshToken, cancellationToken);

        if (refreshed == null)
            throw new ConnectionNotConfiguredException("De aanmelding is verlopen. Meld opnieuw aan met je Microsoft-account.");

        SaveTokens(profile, refreshed);
        return refreshed.AccessToken!;
    }

    public void SignOut(ConnectionProfile profile)
    {
        var path = TokenPath(profile);

        if (File.Exists(path))
            File.Delete(path);
    }

    private async Task<TokenSet?> ExchangeCodeAsync(ConnectionProfile profile, string code, string verifier, string redirectUri, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = profile.ClientId!,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = verifier,
            ["scope"] = $"{ScopeFor(profile)} offline_access openid profile"
        };

        return await PostTokenRequestAsync(profile, form, cancellationToken);
    }

    private async Task<TokenSet?> RefreshAsync(ConnectionProfile profile, string refreshToken, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = profile.ClientId!,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = $"{ScopeFor(profile)} offline_access openid profile"
        };

        return await PostTokenRequestAsync(profile, form, cancellationToken);
    }

    private async Task<TokenSet?> PostTokenRequestAsync(ConnectionProfile profile, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var url = $"https://login.microsoftonline.com/{profile.TenantId}/oauth2/v2.0/token";

        using var response = await Http.PostAsync(url, new FormUrlEncodedContent(form), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadError(body);
            _logger.LogWarning("Token-aanvraag mislukt voor {Connection}: {Error}", profile.Name, error);
            throw new InteractiveAuthException(error ?? $"Token-aanvraag mislukt ({(int)response.StatusCode}).", TranslateError(null, error));
        }

        var payload = JsonSerializer.Deserialize<TokenResponse>(body);

        if (payload?.AccessToken == null)
            return null;

        return new TokenSet
        {
            AccessToken = payload.AccessToken,
            RefreshToken = payload.RefreshToken,
            ExpiresOn = DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn ?? 3600),
            Account = ReadAccount(payload.IdToken) ?? LoadTokens(profile)?.Account
        };
    }

    /// <summary>Scope voor Dataverse: alles van de omgeving (delegated user_impersonation).</summary>
    private static string ScopeFor(ConnectionProfile profile)
        => (profile.Url ?? "").TrimEnd('/') + "/.default";

    private string TokenPath(ConnectionProfile profile)
    {
        var directory = XrmPocketOptions.ExpandPath(_options.TokenDirectory);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, profile.Id + ".json");
    }

    private TokenSet? LoadTokens(ConnectionProfile profile)
    {
        try
        {
            var path = TokenPath(profile);

            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize<TokenSet>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tokens voor {Connection} konden niet gelezen worden", profile.Name);
            return null;
        }
    }

    private void SaveTokens(ConnectionProfile profile, TokenSet tokens)
    {
        var path = TokenPath(profile);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(tokens, JsonOptions));

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        File.Move(temp, path, overwrite: true);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Kiest de origin voor de callback: die van de client als hij in de allow-list
    /// staat, anders de geconfigureerde standaard.
    /// </summary>
    private string ResolveOrigin(string? requestedOrigin)
    {
        var allowed = new List<string>(_options.InteractiveAllowedOrigins ?? Array.Empty<string>());

        if (Uri.TryCreate(_options.InteractiveRedirectUri, UriKind.Absolute, out var configured))
            allowed.Add(configured.GetLeftPart(UriPartial.Authority));

        var candidates = allowed
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(requestedOrigin))
        {
            var requested = requestedOrigin.TrimEnd('/');

            if (candidates.Any(candidate => string.Equals(candidate, requested, StringComparison.OrdinalIgnoreCase)))
                return requested;

            throw new InvalidOperationException(
                $"Dit adres ({requested}) is niet toegestaan voor aanmelding. Toegestaan: {string.Join(", ", candidates)}. " +
                "Voeg het adres toe aan XrmPocket:InteractiveAllowedOrigins en aan de redirect-URI's van de app registration.");
        }

        return candidates.FirstOrDefault()
               ?? throw new InvalidOperationException("Geen redirect-URI geconfigureerd voor interactieve aanmelding.");
    }

    /// <summary>Waar de browser naartoe gaat na de callback (het adres waarop je werkt).</summary>
    public string AppBaseUrlFor(string? state)
        => _pending.TryGetValue(state ?? "", out var pending) ? pending.Origin + "/" : AppBaseUrl;

    private void CleanupPending()
    {
        foreach (var pair in _pending.Where(pair => pair.Value.ExpiresOn < DateTimeOffset.UtcNow).ToList())
            _pending.TryRemove(pair.Key, out _);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Leest de UPN uit het id-token (alleen voor weergave; TLS beschermt de respons).</summary>
    private static string? ReadAccount(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken))
            return null;

        var parts = idToken.Split('.');

        if (parts.Length < 2)
            return null;

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            var root = document.RootElement;

            foreach (var claim in new[] { "preferred_username", "upn", "email", "name" })
            {
                if (root.TryGetProperty(claim, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString();
            }
        }
        catch (Exception)
        {
            // Geen geldig id-token: accountnaam is optioneel.
        }

        return null;
    }

    private static string? TryReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var error = root.TryGetProperty("error", out var errorValue) ? errorValue.GetString() : null;
            var description = root.TryGetProperty("error_description", out var descriptionValue) ? descriptionValue.GetString() : null;
            return string.IsNullOrEmpty(description) ? error : $"{error}: {description}";
        }
        catch (Exception)
        {
            return body.Length > 400 ? body[..400] : body;
        }
    }

    /// <summary>Vertaalt AAD-fouten naar iets waar de gebruiker iets aan heeft.</summary>
    public static string TranslateError(string? error, string? description)
    {
        var text = (error ?? "") + " " + (description ?? "");

        if (text.Contains("AADSTS530033", StringComparison.OrdinalIgnoreCase))
            return "Conditional Access weigert de aanmelding: dit apparaat voldoet niet aan het Intune-beleid. " +
                   "Meld aan vanaf een beheerd apparaat, of gebruik een app-only verbinding.";

        if (text.Contains("AADSTS65001", StringComparison.OrdinalIgnoreCase))
            return "Er ontbreekt toestemming: voeg 'Dataverse > user_impersonation' toe aan de app registration en geef admin consent.";

        if (text.Contains("AADSTS50011", StringComparison.OrdinalIgnoreCase))
            return "De redirect-URI klopt niet: registreer de callback-URL van XrmPocket bij de app registration (platform Web).";

        if (text.Contains("AADSTS700016", StringComparison.OrdinalIgnoreCase))
            return "De app registration bestaat niet in deze tenant.";

        if (text.Contains("AADSTS700054", StringComparison.OrdinalIgnoreCase) || text.Contains("AADSTS9002326", StringComparison.OrdinalIgnoreCase))
            return "De app registration staat niet toe dat deze client zo aanmeldt; controleer het platform (Web) en de redirect-URI.";

        return string.IsNullOrEmpty(description) ? "Aanmelden mislukt." : description!;
    }

    private sealed record PendingSignIn(
        string ConnectionId,
        string CodeVerifier,
        string RedirectUri,
        string Origin,
        DateTimeOffset ExpiresOn);

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int? ExpiresIn { get; set; }
        [JsonPropertyName("id_token")] public string? IdToken { get; set; }
    }

    /// <summary>Wat er in het token-bestand staat (chmod 600, buiten de repo).</summary>
    public sealed class TokenSet
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTimeOffset ExpiresOn { get; set; }
        public string? Account { get; set; }
    }
}

/// <summary>Start van een interactieve aanmelding: de URL die de browser moet openen.</summary>
public sealed record SignInStart(string ConnectionId, string AuthorizationUrl, string State, string RedirectUri);

/// <summary>Resultaat van de terugkeer uit Entra.</summary>
public sealed record SignInResult(bool Ok, string? ConnectionId, string? Account, string? ErrorCode, string? Message)
{
    public static SignInResult Succeeded(string connectionId, string? account) => new(true, connectionId, account, null, null);

    public static SignInResult Failed(string? connectionId, string code, string message) => new(false, connectionId, null, code, message);
}

/// <summary>Fout tijdens de interactieve aanmelding (met een Nederlandse uitleg).</summary>
public sealed class InteractiveAuthException : Exception
{
    public InteractiveAuthException(string message, string userMessage) : base(message)
    {
        UserMessage = userMessage;
    }

    public string UserMessage { get; }
}
