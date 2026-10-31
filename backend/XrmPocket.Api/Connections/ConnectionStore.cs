using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using XrmPocket.Api.Configuration;

namespace XrmPocket.Api.Connections;

/// <summary>
/// Bewaart de lijst met verbindingen — het equivalent van XrmToolBox'
/// <c>MscrmTools.ConnectionsList.xml</c>, maar als JSON en met de secrets er
/// <b>buiten</b> (XrmToolBox versleutelt ze met een sleutel die in de app zit; wij
/// bewaren enkel een pad naar een bestand met chmod 600).
/// </summary>
public sealed class ConnectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly XrmPocketOptions _options;
    private readonly ILogger<ConnectionStore> _logger;
    private readonly object _gate = new();
    private StoreDocument _document;

    public ConnectionStore(IOptions<XrmPocketOptions> options, ILogger<ConnectionStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        _document = Load();
    }

    public string FilePath => XrmPocketOptions.ExpandPath(_options.ConnectionsFile);

    public IReadOnlyList<ConnectionProfile> List()
    {
        lock (_gate)
            return _document.Connections.OrderBy(ConnectionOrder).ToList();
    }

    public ConnectionProfile? Get(string id)
    {
        lock (_gate)
            return _document.Connections.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public ConnectionProfile? GetDefault()
    {
        lock (_gate)
            return _document.Connections.FirstOrDefault(c => c.Id == _document.DefaultConnectionId)
                   ?? _document.Connections.FirstOrDefault();
    }

    public ConnectionProfile Upsert(SaveConnectionRequest request, out string? error)
    {
        error = null;

        var name = request.Name?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Geef de verbinding een naam.";
            return null!;
        }

        var kind = NormalizeKind(request.Kind);

        if (kind == "dataverse" && string.IsNullOrWhiteSpace(request.Url))
        {
            error = "Een Dataverse-verbinding heeft een URL nodig.";
            return null!;
        }

        lock (_gate)
        {
            var existing = string.IsNullOrWhiteSpace(request.Id)
                ? null
                : _document.Connections.FirstOrDefault(c => string.Equals(c.Id, request.Id, StringComparison.OrdinalIgnoreCase));

            var profile = existing ?? new ConnectionProfile { Id = Guid.NewGuid().ToString() };

            profile.Name = name!;
            profile.Kind = kind;
            profile.AuthType = NormalizeAuthType(request.AuthType);
            profile.Environment = string.IsNullOrWhiteSpace(request.Environment) ? "custom" : request.Environment.Trim().ToLowerInvariant();
            profile.Url = kind == "mock" ? null : request.Url?.Trim();
            profile.TenantId = string.IsNullOrWhiteSpace(request.TenantId) ? _options.TenantId : request.TenantId.Trim();
            profile.ClientId = string.IsNullOrWhiteSpace(request.ClientId) ? _options.ClientId : request.ClientId.Trim();
            profile.SecretFile = string.IsNullOrWhiteSpace(request.SecretFile) ? _options.ClientSecretFile : request.SecretFile.Trim();
            profile.TimeoutSeconds = Math.Clamp(request.TimeoutSeconds ?? 120, 5, 600);
            profile.Highlight = NormalizeHighlight(request.Highlight, profile.Environment);

            if (existing == null)
            {
                _document.Connections.Add(profile);

                if (string.IsNullOrEmpty(_document.DefaultConnectionId))
                    _document.DefaultConnectionId = profile.Id;
            }

            Save();

            return profile;
        }
    }

    public bool Delete(string id)
    {
        lock (_gate)
        {
            var profile = _document.Connections.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

            if (profile == null)
                return false;

            _document.Connections.Remove(profile);

            if (_document.DefaultConnectionId == profile.Id)
                _document.DefaultConnectionId = _document.Connections.FirstOrDefault()?.Id;

            Save();
            return true;
        }
    }

    public bool SetDefault(string id)
    {
        lock (_gate)
        {
            if (_document.Connections.All(c => c.Id != id))
                return false;

            _document.DefaultConnectionId = id;
            Save();
            return true;
        }
    }

    /// <summary>Onthoudt wanneer een verbinding laatst gebruikt is (MRU-lijst).</summary>
    public void Touch(string id)
    {
        lock (_gate)
        {
            var profile = _document.Connections.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

            if (profile == null)
                return;

            profile.LastUsedOn = DateTimeOffset.UtcNow;
            Save();
        }
    }

    private static int ConnectionOrder(ConnectionProfile profile) => profile.LastUsedOn == null ? 1 : 0;

    private StoreDocument Load()
    {
        var path = FilePath;

        if (!File.Exists(path))
        {
            var seeded = Seed();
            Persist(seeded);
            return seeded;
        }

        try
        {
            var json = File.ReadAllText(path);
            var document = JsonSerializer.Deserialize<StoreDocument>(json, JsonOptions) ?? new StoreDocument();
            document.Connections ??= new List<ConnectionProfile>();
            return document;
        }
        catch (Exception ex)
        {
            // Nooit stilzwijgend verbindingen kwijtspelen: bestand hernoemen en opnieuw beginnen.
            _logger.LogError(ex, "Verbindingen-bestand {Path} kon niet gelezen worden; het wordt opzij gezet.", path);
            File.Move(path, path + ".corrupt-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds(), overwrite: true);
            var seeded = Seed();
            Persist(seeded);
            return seeded;
        }
    }

    /// <summary>
    /// Eerste start: twee verbindingen klaarzetten (mock + SAV-TEST), zoals XrmToolBox
    /// een lege lijst toont waarin je zelf verbindingen maakt — alleen vullen wij vast
    /// de omgeving die het project standaard gebruikt.
    /// </summary>
    private StoreDocument Seed()
    {
        var mock = new ConnectionProfile
        {
            Name = "Mock (synthetisch)",
            Kind = "mock",
            AuthType = "none",
            Environment = "mock",
            Highlight = "none",
            TimeoutSeconds = 30
        };

        var test = new ConnectionProfile
        {
            Name = "SAV-TEST",
            Kind = "dataverse",
            AuthType = "app-only",
            Environment = "test",
            Url = "https://operations-sav-test.crm4.dynamics.com/",
            TenantId = _options.TenantId,
            ClientId = _options.ClientId,
            SecretFile = _options.ClientSecretFile,
            Highlight = "test"
        };

        return new StoreDocument
        {
            DefaultConnectionId = mock.Id,
            Connections = new List<ConnectionProfile> { mock, test }
        };
    }

    private void Save()
    {
        Persist(_document);
    }

    private void Persist(StoreDocument document)
    {
        var path = FilePath;
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(document, JsonOptions));

        // Bestand met gevoelige namen en paden: enkel de eigenaar mag het lezen.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        File.Move(temp, path, overwrite: true);
    }

    private static string NormalizeKind(string? kind) =>
        string.Equals(kind, "mock", StringComparison.OrdinalIgnoreCase) ? "mock" : "dataverse";

    /// <summary>
    /// none = mock, app-only = client credentials, interactive = aanmelden met je
    /// eigen Microsoft-account (zoals XrmToolBox' Office365-verbinding).
    /// </summary>
    private static string NormalizeAuthType(string? authType) => authType?.Trim().ToLowerInvariant() switch
    {
        "none" => "none",
        "interactive" => "interactive",
        _ => "app-only"
    };

    private static string NormalizeHighlight(string? highlight, string environment)
    {
        var allowed = new[] { "none", "dev", "test", "acc", "production" };
        var value = highlight?.Trim().ToLowerInvariant();

        if (!string.IsNullOrEmpty(value) && allowed.Contains(value))
            return value;

        return allowed.Contains(environment) ? environment : "none";
    }

    private sealed class StoreDocument
    {
        public int Version { get; set; } = 1;
        public string? DefaultConnectionId { get; set; }
        public List<ConnectionProfile> Connections { get; set; } = new();
    }
}
