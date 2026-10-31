using System.Collections.Concurrent;
using System.Globalization;
using MarkMpn.Sql4Cds.Engine;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Options;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using XrmPocket.Api.Configuration;
using XrmPocket.Api.Mock;

namespace XrmPocket.Api.Connections;

/// <summary>
/// Maakt van een <see cref="ConnectionProfile"/> een echte verbinding, en test die.
/// Het equivalent van XrmToolBox' <c>ConnectionManager.ConnectToServer</c>: per
/// verbinding wordt de client hergebruikt (XrmToolBox: "Reuse connections") tot de
/// instellingen of het secret-bestand wijzigen.
/// </summary>
public sealed class ConnectionFactory
{
    private readonly XrmPocketOptions _options;
    private readonly MockDataset _mockDataset;
    private readonly ILogger<ConnectionFactory> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ConnectionFactory(IOptions<XrmPocketOptions> options, MockDataset mockDataset, ILogger<ConnectionFactory> logger)
    {
        _options = options.Value;
        _mockDataset = mockDataset;
        _logger = logger;
    }

    /// <summary>
    /// Geeft de data source voor deze verbinding, of een duidelijke fout als de
    /// verbinding nog niet bruikbaar is (secret ontbreekt, app-only niet geconfigureerd).
    /// </summary>
    public DataSource CreateDataSource(ConnectionProfile profile)
    {
        if (profile.IsMock)
            return MockOrganizationService.CreateDataSource(_mockDataset);

        var note = DescribeMissingConfiguration(profile);

        if (note != null)
            throw new ConnectionNotConfiguredException(note);

        var client = GetClient(profile);

        if (!client.IsReady)
        {
            throw new ConnectionNotConfiguredException(
                client.LastError ?? "De verbinding is niet gereed. Test de verbinding om de oorzaak te zien.");
        }

        return new DataSource(client);
    }

    /// <summary>Haalt (of maakt) de ServiceClient voor deze verbinding.</summary>
    public ServiceClient GetClient(ConnectionProfile profile)
    {
        var fingerprint = Fingerprint(profile);
        var entry = _cache.GetOrAdd(profile.Id, _ => new CacheEntry(fingerprint, new Lazy<ServiceClient>(() => CreateClient(profile))));

        if (entry.Fingerprint != fingerprint)
        {
            // Instellingen of secret gewijzigd: oude client loslaten en opnieuw opbouwen.
            _cache[profile.Id] = new CacheEntry(fingerprint, new Lazy<ServiceClient>(() => CreateClient(profile)));
            entry = _cache[profile.Id];
        }

        return entry.Client.Value;
    }

    public void Invalidate(string connectionId) => _cache.TryRemove(connectionId, out _);

    /// <summary>
    /// Verbindingstest met WhoAmI — precies wat XrmToolBox in zijn statusbalk toont:
    /// omgeving, versie en de gebruiker waarmee je verbonden bent.
    /// </summary>
    public ConnectionTestResult Test(ConnectionProfile profile)
    {
        if (profile.IsMock)
        {
            return new ConnectionTestResult(
                true,
                "Mock (synthetische data)",
                "mock",
                typeof(Sql4CdsConnection).Assembly.GetName().Version?.ToString(),
                null,
                null,
                "Geen Dataverse-verbinding: alle rijen komen uit de mock-dataset.",
                null);
        }

        var note = DescribeMissingConfiguration(profile);

        if (note != null)
        {
            return new ConnectionTestResult(false, null, null, null, null, "connection_not_configured", note,
                "Maak een client secret aan (knop 'Secret instellen') en geef de app een application user in Dataverse.");
        }

        try
        {
            var client = GetClient(profile);

            if (!client.IsReady)
            {
                return new ConnectionTestResult(false, null, null, null, null, "connect_failed",
                    client.LastError ?? "Verbinding niet gereed.", MapHint(client.LastError));
            }

            var whoAmI = (WhoAmIResponse)client.Execute(new WhoAmIRequest());
            string? version = null;

            try
            {
                var versionResponse = (RetrieveVersionResponse)client.Execute(new RetrieveVersionRequest());
                version = versionResponse.Results.TryGetValue("Version", out var value) ? value?.ToString() : null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Versie ophalen mislukt voor verbinding {Connection}", profile.Name);
            }

            return new ConnectionTestResult(
                true,
                client.ConnectedOrgFriendlyName,
                client.ConnectedOrgUniqueName,
                version,
                whoAmI.UserId,
                null,
                null,
                null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Verbindingstest voor {Connection} mislukt", profile.Name);

            var message = ex.GetBaseException().Message;
            var code = IsInvalidClient(message) ? "invalid_client"
                : message.Contains("AADSTS65001", StringComparison.OrdinalIgnoreCase) ? "consent_required"
                : message.Contains("AADSTS700016", StringComparison.OrdinalIgnoreCase) ? "app_not_found"
                : message.Contains("403", StringComparison.OrdinalIgnoreCase) ? "no_application_user"
                : "connect_failed";

            return new ConnectionTestResult(false, null, null, null, null, code, message.Trim(), MapHint(message));
        }
    }

    /// <summary>Waarom een verbinding (nog) niet bruikbaar is, of null als ze bruikbaar is.</summary>
    public string? DescribeMissingConfiguration(ConnectionProfile profile)
    {
        if (profile.IsMock)
            return null;

        if (!string.Equals(profile.AuthType, "app-only", StringComparison.OrdinalIgnoreCase))
            return "Deze authenticatievorm is nog niet ondersteund (interactieve login wordt door CA geblokkeerd).";

        if (string.IsNullOrWhiteSpace(profile.Url))
            return "Geen URL ingesteld.";

        if (string.IsNullOrWhiteSpace(profile.ClientId))
            return "Geen client-ID ingesteld.";

        var secretPath = ResolveSecretPath(profile);
        var secretLength = SecretLength(secretPath);

        if (!File.Exists(secretPath))
            return $"Client secret-bestand ontbreekt ({secretPath}).";

        if (secretLength < 20)
            return "Client secret is onbruikbaar (te kort). Stel het opnieuw in.";

        return null;
    }

    public bool HasSecret(ConnectionProfile profile) => profile.IsMock || SecretLength(ResolveSecretPath(profile)) >= 20;

    /// <summary>Schrijft een nieuw client secret weg (chmod 600) en geeft het pad terug.</summary>
    public string SaveSecret(ConnectionProfile profile, string secret)
    {
        var value = secret?.Trim() ?? "";

        if (value.Length < 20)
            throw new ArgumentException("Het client secret lijkt onvolledig (te kort).", nameof(secret));

        var path = ResolveSecretPath(profile);
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        File.WriteAllText(temp, value);

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        File.Move(temp, path, overwrite: true);
        Invalidate(profile.Id);

        return path;
    }

    public string ResolveSecretPath(ConnectionProfile profile)
        => XrmPocketOptions.ExpandPath(profile.SecretFile ?? _options.ClientSecretFile);

    private ServiceClient CreateClient(ConnectionProfile profile)
    {
        var secretPath = ResolveSecretPath(profile);
        var secret = File.ReadAllText(secretPath).Trim();

        // Het secret blijft in dit proces; het komt nooit in een antwoord of log terecht.
        var client = new ServiceClient(
            new Uri(profile.Url!),
            profile.ClientId!,
            secret,
            useUniqueInstance: true,
            logger: null);

        return client;
    }

    private static long SecretLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Vingerafdruk van alles wat de client bepaalt; wijzigt er iets, dan bouwen we
    /// opnieuw op (vergelijkbaar met XrmToolBox' forceNewService).
    /// </summary>
    private string Fingerprint(ConnectionProfile profile)
    {
        var secretPath = ResolveSecretPath(profile);
        var lastWrite = File.Exists(secretPath) ? File.GetLastWriteTimeUtc(secretPath).Ticks.ToString(CultureInfo.InvariantCulture) : "0";

        return string.Join('|',
            profile.Url ?? "",
            profile.TenantId ?? "",
            profile.ClientId ?? "",
            secretPath,
            lastWrite,
            SecretLength(secretPath).ToString(CultureInfo.InvariantCulture),
            profile.TimeoutSeconds.ToString(CultureInfo.InvariantCulture));
    }

    private static string? MapHint(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return "Test de verbinding opnieuw of controleer het client secret.";

        if (IsInvalidClient(message))
            return "Het client secret klopt niet: gebruik de Value van het secret (niet de Secret ID), of maak een nieuw secret aan als het verlopen is.";

        if (message.Contains("AADSTS65001", StringComparison.OrdinalIgnoreCase))
            return "Er ontbreekt toestemming: voeg 'Dataverse > user_impersonation' toe aan de app registration en geef admin consent.";

        if (message.Contains("AADSTS700016", StringComparison.OrdinalIgnoreCase))
            return "De app registration bestaat niet (meer) in deze tenant.";

        if (message.Contains("AADSTS530033", StringComparison.OrdinalIgnoreCase))
            return "Interactieve login is geblokkeerd door Conditional Access; gebruik app-only.";

        if (message.Contains("403", StringComparison.OrdinalIgnoreCase))
            return "De app is bekend, maar heeft geen application user met de juiste beveiligingsrol in deze omgeving.";

        return "Controleer URL, client-ID en het client secret.";
    }

    /// <summary>
    /// Herkent de fouten van Microsoft die op een ongeldig/verlopen secret wijzen:
    /// invalid_client, AADSTS7000215 (waarde fout of Secret ID gebruikt) en
    /// AADSTS7000222 (verlopen).
    /// </summary>
    private static bool IsInvalidClient(string message) =>
        message.Contains("invalid_client", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("AADSTS7000215", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("AADSTS7000222", StringComparison.OrdinalIgnoreCase);

    private sealed record CacheEntry(string Fingerprint, Lazy<ServiceClient> Client);
}

/// <summary>De verbinding is nog niet bruikbaar; de melding zegt wat er ontbreekt.</summary>
public sealed class ConnectionNotConfiguredException : Exception
{
    public ConnectionNotConfiguredException(string message) : base(message)
    {
    }
}
