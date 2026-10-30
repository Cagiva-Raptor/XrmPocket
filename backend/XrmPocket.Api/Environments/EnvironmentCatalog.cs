using Microsoft.Extensions.Options;
using XrmPocket.Api.Configuration;

namespace XrmPocket.Api.Environments;

/// <summary>Eén omgeving zoals de API die aan de UI toont.</summary>
public sealed record EnvironmentInfo(
    string Id,
    string Label,
    string Kind,
    string? Url,
    bool Production,
    bool Configured,
    string? Note);

/// <summary>
/// Bepaalt welke omgevingen beschikbaar zijn en of ze bruikbaar zijn. De mock is
/// altijd beschikbaar; een Dataverse-omgeving pas als er een URL is, het client
/// secret-bestand bestaat en gevuld is.
/// </summary>
public sealed class EnvironmentCatalog
{
    private readonly XrmPocketOptions _options;

    public EnvironmentCatalog(IOptions<XrmPocketOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyList<EnvironmentInfo> List()
        => _options.Environments
            .Where(pair => pair.Value.Enabled)
            .Select(pair => Describe(pair.Key, pair.Value))
            .OrderBy(env => env.Kind == "mock" ? 0 : 1)
            .ThenBy(env => env.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public EnvironmentInfo? Find(string id)
    {
        if (!_options.Environments.TryGetValue(id, out var options) || !options.Enabled)
            return null;

        return Describe(id, options);
    }

    private EnvironmentInfo Describe(string id, EnvironmentOptions options)
    {
        if (string.Equals(options.Kind, "mock", StringComparison.OrdinalIgnoreCase))
            return new EnvironmentInfo(id, options.Label, "mock", null, false, true,
                "Synthetische data — geen Dataverse-verbinding.");

        var notes = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Url))
            notes.Add("geen URL in configuratie");

        var secretPath = XrmPocketOptions.ExpandPath(_options.ClientSecretFile);
        var secretLength = ReadSecretLength(secretPath);

        if (!File.Exists(secretPath))
            notes.Add("client secret-bestand ontbreekt");
        else if (secretLength < 20)
            notes.Add("client secret is onbruikbaar (te kort)");

        if (string.IsNullOrWhiteSpace(_options.ClientId))
            notes.Add("client-ID ontbreekt");

        var configured = notes.Count == 0;

        return new EnvironmentInfo(
            id,
            options.Label,
            "dataverse",
            options.Url,
            options.Production,
            configured,
            configured ? null : string.Join("; ", notes));
    }

    /// <summary>
    /// Leest enkel de lengte van het secret-bestand; de inhoud komt nooit in een
    /// log, een antwoord of een uitzondering terecht.
    /// </summary>
    private static long ReadSecretLength(string path)
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
}
