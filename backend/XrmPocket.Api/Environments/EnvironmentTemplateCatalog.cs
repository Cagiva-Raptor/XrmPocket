using Microsoft.Extensions.Options;
using XrmPocket.Api.Configuration;

namespace XrmPocket.Api.Environments;

/// <summary>
/// Eén voorgedefinieerde omgeving om een verbinding mee aan te maken (dev, test, acc,
/// productie). Dit zijn enkel <b>sjablonen</b>: de echte verbindingen staan in
/// <c>connections.json</c> en zijn per stuk bewerkbaar, zoals in XrmToolBox.
/// </summary>
public sealed record EnvironmentTemplate(
    string Id,
    string Label,
    string? Url,
    bool Production);

/// <summary>Lijst met omgevingssjablonen uit de configuratie.</summary>
public sealed class EnvironmentTemplateCatalog
{
    private readonly XrmPocketOptions _options;

    public EnvironmentTemplateCatalog(IOptions<XrmPocketOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyList<EnvironmentTemplate> List()
        => _options.Environments
            .Where(pair => pair.Value.Enabled)
            .Select(pair => new EnvironmentTemplate(pair.Key, pair.Value.Label, pair.Value.Url, pair.Value.Production))
            .ToList();
}
