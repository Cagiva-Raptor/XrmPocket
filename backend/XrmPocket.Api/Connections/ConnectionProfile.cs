namespace XrmPocket.Api.Connections;

/// <summary>
/// Eén opgeslagen verbinding — het equivalent van XrmToolBox' <c>ConnectionDetail</c>.
/// Belangrijk verschil: hier staat <b>nooit</b> een secret in, alleen een verwijzing
/// naar het secret-bestand (chmod 600) buiten de repo.
/// </summary>
public sealed class ConnectionProfile
{
    /// <summary>Uniek id (Guid als string), zoals ConnectionId in XrmToolBox.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Naam zoals de gebruiker die ziet (XrmToolBox: ConnectionName).</summary>
    public string Name { get; set; } = "";

    /// <summary>mock of dataverse.</summary>
    public string Kind { get; set; } = "dataverse";

    /// <summary>
    /// app-only (client credentials) of interactive. Interactive is bewust nog niet
    /// geïmplementeerd: CA blokkeert dat op onbeheerde apparaten (AADSTS530033).
    /// </summary>
    public string AuthType { get; set; } = "app-only";

    /// <summary>Sjabloon waar deze verbinding op gebaseerd is: dev, test, acc, production, custom.</summary>
    public string Environment { get; set; } = "test";

    /// <summary>Volledige URL van de omgeving (productie heeft bewust geen alias).</summary>
    public string? Url { get; set; }

    /// <summary>Entra-tenant (openbaar id, geen secret).</summary>
    public string? TenantId { get; set; }

    /// <summary>Client-ID van de app registration (openbaar id, geen secret).</summary>
    public string? ClientId { get; set; }

    /// <summary>Pad naar het bestand met het client secret (chmod 600).</summary>
    public string? SecretFile { get; set; }

    /// <summary>Time-out per aanroep, zoals de Timeout-instelling in XrmToolBox.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Kleurmarkering per omgeving (XrmToolBox: EnvironmentHighlighting), zodat je op
    /// het scherm ziet of je tegen test dan wel productie werkt.
    /// </summary>
    public string Highlight { get; set; } = "none";

    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastUsedOn { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsMock => string.Equals(Kind, "mock", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Wat de API over een verbinding toont (nooit het secret).</summary>
public sealed record SavedConnection(
    string Id,
    string Name,
    string Kind,
    string AuthType,
    string Environment,
    string? Url,
    string? TenantId,
    string? ClientId,
    string Highlight,
    int TimeoutSeconds,
    bool IsDefault,
    bool Configured,
    bool SecretPresent,
    string? Note,
    DateTimeOffset? LastUsedOn);

/// <summary>
/// Resultaat van een verbindingstest — precies wat XrmToolBox in zijn statusbalk
/// toont (omgeving, versie, gebruiker), aangevuld met de application user-check:
/// bestaat er een systemuser voor deze app, is die actief en welke rollen heeft hij?
/// </summary>
public sealed class ConnectionTestResult
{
    public bool Ok { get; init; }

    public string? OrganizationName { get; init; }
    public string? OrganizationUniqueName { get; init; }
    public string? OrganizationVersion { get; init; }

    /// <summary>Id van de gebruiker waarmee verbonden is (WhoAmI).</summary>
    public Guid? UserId { get; init; }

    public string? UserFullName { get; init; }
    public string? UserDomainName { get; init; }

    /// <summary>applicationid van de systemuser; hoort de client-ID van de app te zijn.</summary>
    public string? ApplicationId { get; init; }

    public bool? UserDisabled { get; init; }

    /// <summary>Rollen van de application user (namen).</summary>
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    /// <summary>Waar als een van de rollen Systeembeheerder is.</summary>
    public bool IsSystemAdministrator { get; init; }

    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public string? Hint { get; init; }

    public static ConnectionTestResult Failure(string code, string message, string? hint) => new()
    {
        Ok = false,
        ErrorCode = code,
        Message = message,
        Hint = hint
    };
}

/// <summary>Body om een verbinding aan te maken of te wijzigen.</summary>
public sealed class SaveConnectionRequest
{
    public string? Id { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "dataverse";
    public string AuthType { get; set; } = "app-only";
    public string Environment { get; set; } = "test";
    public string? Url { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? SecretFile { get; set; }
    public int? TimeoutSeconds { get; set; }
    public string Highlight { get; set; } = "none";
}

/// <summary>Body om een secret te bewaren (analoog aan XrmToolBox' RequestPassword).</summary>
public sealed class SaveSecretRequest
{
    public string Secret { get; set; } = "";
}
