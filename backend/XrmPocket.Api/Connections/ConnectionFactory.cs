using System.Collections.Concurrent;
using System.Globalization;
using MarkMpn.Sql4Cds.Engine;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Options;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
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
    /// Verbindingstest met WhoAmI + application user-check — precies wat XrmToolBox in
    /// zijn statusbalk toont, aangevuld met de vraag of de app in deze omgeving een
    /// systemuser met rollen heeft.
    /// </summary>
    public ConnectionTestResult Test(ConnectionProfile profile)
    {
        if (profile.IsMock)
        {
            // Ook de mock levert een (duidelijk synthetische) gebruiker + rollen, zodat
            // de statusbalk en de rollenweergave testbaar zijn zonder Dataverse.
            return new ConnectionTestResult
            {
                Ok = true,
                OrganizationName = "Mock (synthetische data)",
                OrganizationUniqueName = "mock",
                OrganizationVersion = typeof(Sql4CdsConnection).Assembly.GetName().Version?.ToString(),
                UserId = MockUserId,
                UserFullName = "Mock-gebruiker",
                UserDomainName = "mock@xrmpocket.local",
                ApplicationId = profile.ClientId ?? "mock-app-id",
                UserDisabled = false,
                Roles = new[] { "Systeembeheerder (mock)", "Salesperson (mock)" },
                IsSystemAdministrator = true,
                Message = "Geen Dataverse-verbinding: gebruiker en rollen zijn verzonnen."
            };
        }

        var note = DescribeMissingConfiguration(profile);

        if (note != null)
        {
            return ConnectionTestResult.Failure("connection_not_configured", note,
                "Stel een client secret in (knop 'Secret instellen') en geef de app een application user in Dataverse.");
        }

        try
        {
            var client = GetClient(profile);

            if (!client.IsReady)
            {
                return ConnectionTestResult.Failure("connect_failed",
                    client.LastError ?? "Verbinding niet gereed.", MapHint(client.LastError));
            }

            var whoAmI = (WhoAmIResponse)client.Execute(new WhoAmIRequest());
            var version = TryGetVersion(client);
            var user = DescribeUser(client, profile, whoAmI.UserId, out var userNote);

            return new ConnectionTestResult
            {
                Ok = true,
                OrganizationName = client.ConnectedOrgFriendlyName,
                OrganizationUniqueName = client.ConnectedOrgUniqueName,
                OrganizationVersion = version,
                UserId = whoAmI.UserId,
                UserFullName = user?.FullName,
                UserDomainName = user?.DomainName,
                ApplicationId = user?.ApplicationId,
                UserDisabled = user?.Disabled,
                Roles = user?.Roles ?? Array.Empty<string>(),
                IsSystemAdministrator = user?.IsSystemAdministrator ?? false,
                Message = userNote,
                Hint = user?.Disabled == true
                    ? "De application user is gedeactiveerd; activeer hem in Dataverse voordat je queries draait."
                    : null
            };
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

            return ConnectionTestResult.Failure(code, message.Trim(), MapHint(message));
        }
    }

    private static string? TryGetVersion(ServiceClient client)
    {
        try
        {
            var response = (RetrieveVersionResponse)client.Execute(new RetrieveVersionRequest());
            return response.Results.TryGetValue("Version", out var value) ? value?.ToString() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Leest de systemuser achter de app-only verbinding: bestaat hij, is hij actief,
    /// hoort zijn applicationid bij deze app en welke rollen heeft hij? Dit is de
    /// "application user"-controle die je in Dataverse anders met de hand doet.
    /// </summary>
    private UserDescription? DescribeUser(ServiceClient client, ConnectionProfile profile, Guid userId, out string? note)
    {
        note = null;

        try
        {
            var entity = client.Retrieve("systemuser", userId,
                new ColumnSet("fullname", "domainname", "isdisabled", "applicationid", "isintegrationuser"));

            var applicationId = entity.GetAttributeValue<Guid?>("applicationid");
            var disabled = entity.GetAttributeValue<bool?>("isdisabled") ?? false;
            var roles = RetrieveRoles(client, userId, out var isSystemAdministrator);

            if (applicationId != null && !string.IsNullOrEmpty(profile.ClientId) &&
                !string.Equals(applicationId.Value.ToString(), profile.ClientId, StringComparison.OrdinalIgnoreCase))
            {
                note = $"Let op: deze gebruiker hoort bij app {applicationId}, niet bij client-ID {profile.ClientId}.";
            }
            else if (roles.Count == 0)
            {
                note = "De application user heeft geen beveiligingsrollen; query's kunnen op rechten stuklopen.";
            }

            return new UserDescription(
                entity.GetAttributeValue<string>("fullname"),
                entity.GetAttributeValue<string>("domainname"),
                applicationId?.ToString(),
                disabled,
                roles,
                isSystemAdministrator);
        }
        catch (Exception ex)
        {
            // Geen leesrecht op systemuser/role is geen verbindingsfout: de app werkt,
            // maar we kunnen de gebruiker niet beschrijven.
            _logger.LogDebug(ex, "Application user-info ophalen mislukt voor {Connection}", profile.Name);
            note = "Kon de application user niet uitlezen (geen leesrecht op systemuser/role).";
            return null;
        }
    }

    private static IReadOnlyList<string> RetrieveRoles(ServiceClient client, Guid userId, out bool isSystemAdministrator)
    {
        // Zelfde aanpak als de engine gebruikt: role + link naar systemuserroles.
        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("name", "roletemplateid")
        };

        var link = query.AddLink("systemuserroles", "roleid", "roleid");
        link.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);

        var roles = client.RetrieveMultiple(query).Entities;
        isSystemAdministrator = roles.Any(role => role.GetAttributeValue<Guid?>("roletemplateid") == SystemAdministratorTemplateId);

        return roles
            .Select(role => role.GetAttributeValue<string>("name"))
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    /// <summary>Vaste, verzonnen gebruiker-id voor de mock-verbinding.</summary>
    private static readonly Guid MockUserId = new("00000000-0000-0000-0000-000000000c04");

    /// <summary>Vaste GUID van de rol "Systeembeheerder" in Dataverse.</summary>
    private static readonly Guid SystemAdministratorTemplateId = new("627090FF-40A3-4053-8790-584EDC5BE201");

    private sealed record CacheEntry(string Fingerprint, Lazy<ServiceClient> Client);

    private sealed record UserDescription(
        string? FullName,
        string? DomainName,
        string? ApplicationId,
        bool Disabled,
        IReadOnlyList<string> Roles,
        bool IsSystemAdministrator);
}

/// <summary>De verbinding is nog niet bruikbaar; de melding zegt wat er ontbreekt.</summary>
public sealed class ConnectionNotConfiguredException : Exception
{
    public ConnectionNotConfiguredException(string message) : base(message)
    {
    }
}
