namespace XrmPocket.Api.Configuration;

/// <summary>
/// Instellingen voor de XrmPocket-backend. Geen secrets in appsettings: het client
/// secret staat in een bestand buiten de repo (zie <see cref="ClientSecretFile"/>).
/// </summary>
public sealed class XrmPocketOptions
{
    public const string SectionName = "XrmPocket";

    /// <summary>Client-ID van de app registration (openbaar, geen secret).</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Tenant-ID van de Entra-tenant (openbaar, geen secret).</summary>
    public string TenantId { get; set; } = "";

    /// <summary>
    /// Pad naar het bestand met het client secret (chmod 600). In het bestand staat
    /// enkel de waarde; het pad is het enige dat in configuratie terechtkomt.
    /// </summary>
    public string ClientSecretFile { get; set; } = "~/.config/opencode/secrets/sapphire_bot_test_secret.txt";

    /// <summary>
    /// Pad naar het bestand met de opgeslagen verbindingen (buiten de repo, chmod 600).
    /// Het equivalent van XrmToolBox' MscrmTools.ConnectionsList.xml.
    /// </summary>
    public string ConnectionsFile { get; set; } = "~/.config/xrmpocket/connections.json";

    /// <summary>
    /// Callback-URL voor interactieve aanmelding (OAuth). Moet exact gelijk zijn aan
    /// wat er in de app registration onder platform "Web" staat.
    /// </summary>
    public string InteractiveRedirectUri { get; set; } = "http://localhost:4108/api/auth/callback";

    /// <summary>
    /// Toegestane origins voor interactieve aanmelding (bijv. http://localhost:4108,
    /// http://192.168.9.9:4108 of de HTTPS-Tailscale-URL). De UI stuurt zijn eigen
    /// origin mee; alleen deze lijst wordt aanvaard (geen open redirect).
    /// </summary>
    public string[] InteractiveAllowedOrigins { get; set; } = Array.Empty<string>();

    /// <summary>Waar de browser naartoe gaat na een geslaagde aanmelding.</summary>
    public string AppBaseUrl { get; set; } = "/";

    /// <summary>
    /// Map met tokens per verbinding (refresh tokens, chmod 600, buiten de repo).
    /// </summary>
    public string TokenDirectory { get; set; } = "~/.config/xrmpocket/tokens";

    /// <summary>Standaard aantal rijen dat een query teruggeeft.</summary>
    public int DefaultMaxRows { get; set; } = 200;

    /// <summary>Bovengrens: de backend kapt resultaten hierop af.</summary>
    public int HardMaxRows { get; set; } = 5000;

    public Dictionary<string, EnvironmentOptions> Environments { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Zet een pad met een tilde om naar het thuispad van de huidige gebruiker.
    /// </summary>
    public static string ExpandPath(string path)
    {
        if (path.StartsWith("~/", StringComparison.Ordinal))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);

        return path;
    }
}

/// <summary>Eén Dataverse-omgeving (of de mock) zoals de backend die kent.</summary>
public sealed class EnvironmentOptions
{
    public string Label { get; set; } = "";

    /// <summary>Volledige URL; leeg voor de mock. Productie heeft bewust geen alias.</summary>
    public string? Url { get; set; }

    /// <summary>mock of dataverse.</summary>
    public string Kind { get; set; } = "dataverse";

    /// <summary>Uitgeschakeld in configuratie = nooit aanbieden in de UI.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Duidt de productie-omgeving aan. De backend weigert die tenzij de client
    /// expliciet de volledige URL meestuurt (zie beslissing 2026-10-30).
    /// </summary>
    public bool Production { get; set; }
}
