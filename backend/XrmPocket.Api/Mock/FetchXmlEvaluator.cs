using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;

namespace XrmPocket.Api.Mock;

/// <summary>
/// Voert de FetchXML die de engine genereert uit op de synthetische dataset.
/// Ondersteunt wat de engine voor gewone SELECT-query's maakt: attributen, filters
/// (ook genest), JOINs via link-entity en sortering. Wat niet kan, geeft een
/// duidelijke fout in plaats van stil een verkeerd resultaat.
/// </summary>
public static class FetchXmlEvaluator
{
    /// <summary>Operatoren die de mock aankan (voor foutmeldingen in de UI).</summary>
    public const string SupportedOperators =
        "eq, ne, gt, ge, lt, le, like, begins-with, ends-with, null, not-null, in, not-in";

    public static EntityCollection Evaluate(MockDataset dataset, string fetchXml)
    {
        XDocument doc;

        try
        {
            doc = XDocument.Parse(fetchXml);
        }
        catch (Exception ex)
        {
            throw new NotSupportedException("mock: FetchXML kon niet gelezen worden: " + ex.Message, ex);
        }

        var fetch = doc.Root ?? throw new NotSupportedException("mock: lege FetchXML");
        var entityElement = fetch.Element("entity")
            ?? throw new NotSupportedException("mock: FetchXML zonder <entity>");

        var aliases = new Dictionary<string, MockTable>(StringComparer.OrdinalIgnoreCase);
        CollectAliases(dataset, entityElement, aliases);

        var mainAlias = AliasOf(entityElement);
        var mainTable = aliases[mainAlias];

        var rows = Expand(dataset, entityElement, aliases);

        if (string.Equals((string?)fetch.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase))
            return Aggregate(fetch, entityElement, mainAlias, mainTable, rows);

        // Filter van de hoofd-entiteit en van elke link-entity.
        foreach (var (filter, defaultAlias) in CollectFilters(entityElement, mainAlias))
        {
            var predicate = BuildFilter(filter, defaultAlias, aliases);
            rows = rows.Where(predicate).ToList();
        }

        rows = ApplyOrder(rows, entityElement, aliases, mainAlias);

        if (int.TryParse((string?)fetch.Attribute("top"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var top) && top > 0)
            rows = rows.Take(top).ToList();

        var result = new EntityCollection();

        foreach (var row in rows)
        {
            var entity = new Entity(mainTable.LogicalName);
            var idName = mainTable.PrimaryId?.LogicalName;

            if (idName != null && row.TryGetValue($"{mainAlias}.{idName}", out var id) && id is Guid guid)
                entity.Id = guid;

            Project(entityElement, row, mainAlias, aliases, entity);
            result.Entities.Add(entity);
        }

        return result;
    }

    /// <summary>
    /// Voert een aggregate-FetchXML uit (GROUP BY + COUNT/SUM/AVG/MIN/MAX) op de
    /// al samengevoegde rijen.
    /// </summary>
    private static EntityCollection Aggregate(
        XElement fetch,
        XElement entityElement,
        string mainAlias,
        MockTable mainTable,
        List<Dictionary<string, object?>> rows)
    {
        var groupBy = new List<(string Key, string OutputName)>();
        var aggregates = new List<(string Function, string SourceKey, string Column, string OutputName)>();

        foreach (var attribute in entityElement.Elements("attribute"))
        {
            var name = (string?)attribute.Attribute("name") ?? throw new NotSupportedException("mock: aggregate zonder kolomnaam");
            var outputName = (string?)attribute.Attribute("alias") ?? name;
            var function = (string?)attribute.Attribute("aggregate");

            if (string.Equals((string?)attribute.Attribute("groupby"), "true", StringComparison.OrdinalIgnoreCase))
                groupBy.Add(($"{mainAlias}.{name}", outputName));
            else if (!string.IsNullOrEmpty(function))
                aggregates.Add((function.ToLowerInvariant(), $"{mainAlias}.{name}", name, outputName));
        }

        var result = new EntityCollection();

        foreach (var group in rows.GroupBy(row => string.Join("|", groupBy.Select(g =>
                     Convert.ToString(row.TryGetValue(g.Key, out var v) ? v : null, CultureInfo.InvariantCulture)))))
        {
            var groupRows = group.ToList();
            var entity = new Entity(mainTable.LogicalName);

            foreach (var (key, outputName) in groupBy)
            {
                var value = groupRows[0].TryGetValue(key, out var raw) ? raw : null;
                var columnName = key[(key.IndexOf('.') + 1)..];
                entity[outputName] = new AliasedValue(mainTable.LogicalName, columnName, value);
            }

            foreach (var (function, sourceKey, columnName, outputName) in aggregates)
            {
                var values = groupRows
                    .Select(row => row.TryGetValue(sourceKey, out var value) ? value : null)
                    .Where(value => value != null)
                    .ToList();

                object? aggregated = function switch
                {
                    "count" or "countcolumn" => groupRows.Count,
                    "sum" => values.Sum(v => Convert.ToDecimal(v, CultureInfo.InvariantCulture)),
                    "avg" or "average" => values.Count == 0 ? null : values.Average(v => Convert.ToDecimal(v, CultureInfo.InvariantCulture)),
                    "min" => values.Count == 0 ? null : values.Min(v => Convert.ToDecimal(v, CultureInfo.InvariantCulture)),
                    "max" => values.Count == 0 ? null : values.Max(v => Convert.ToDecimal(v, CultureInfo.InvariantCulture)),
                    _ => throw new NotSupportedException($"mock: aggregate '{function}' wordt nog niet ondersteund.")
                };

                // Als AliasedValue met de échte kolomnaam: zo blijft de waarde door de
                // engine-opkuis heen (zie RemoveUnknownAttributes in FetchXmlScan).
                entity[outputName] = new AliasedValue(mainTable.LogicalName, columnName, aggregated);
            }

            result.Entities.Add(entity);
        }

        if (int.TryParse((string?)fetch.Attribute("top"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var top) && top > 0)
        {
            var trimmed = result.Entities.Take(top).ToList();
            result.Entities.Clear();
            foreach (var entity in trimmed)
                result.Entities.Add(entity);
        }

        return result;
    }

    private static string AliasOf(XElement entityElement)
        => (string?)entityElement.Attribute("alias")
           ?? (string?)entityElement.Attribute("name")
           ?? throw new NotSupportedException("mock: entiteit zonder naam of alias");

    private static void CollectAliases(MockDataset dataset, XElement entityElement, Dictionary<string, MockTable> aliases)
    {
        var alias = AliasOf(entityElement);
        var name = (string?)entityElement.Attribute("name") ?? alias;

        aliases[alias] = dataset.Find(name)
            ?? throw new NotSupportedException($"mock: tabel '{name}' bestaat niet in de mock-dataset.");

        foreach (var link in entityElement.Elements("link-entity"))
            CollectAliases(dataset, link, aliases);
    }

    /// <summary>
    /// Bouwt de samengevoegde rijen: hoofd-entiteit plus elke link-entity, met
    /// sleutels als "alias.kolom" zodat gelijknamige kolommen niet botsen.
    /// </summary>
    private static List<Dictionary<string, object?>> Expand(
        MockDataset dataset,
        XElement entityElement,
        Dictionary<string, MockTable> aliases)
    {
        var alias = AliasOf(entityElement);
        var table = aliases[alias];

        var rows = table.Rows
            .Select(row => Prefix(alias, row))
            .ToList();

        foreach (var link in entityElement.Elements("link-entity"))
        {
            var linkAlias = AliasOf(link);
            var linkType = ((string?)link.Attribute("link-type") ?? "inner").ToLowerInvariant();
            var fromAttribute = (string?)link.Attribute("from")
                ?? throw new NotSupportedException("mock: link-entity zonder 'from'");
            var toAttribute = (string?)link.Attribute("to")
                ?? throw new NotSupportedException("mock: link-entity zonder 'to'");

            var linkRows = Expand(dataset, link, aliases);
            var joined = new List<Dictionary<string, object?>>();

            foreach (var row in rows)
            {
                row.TryGetValue($"{alias}.{toAttribute}", out var leftValue);

                var matches = linkRows
                    .Where(linkRow =>
                    {
                        linkRow.TryGetValue($"{linkAlias}.{fromAttribute}", out var rightValue);
                        return ValuesEqual(leftValue, rightValue);
                    })
                    .ToList();

                if (matches.Count == 0)
                {
                    if (linkType is "outer" or "outer-left" or "leftouter")
                        joined.Add(row);

                    continue;
                }

                foreach (var match in matches)
                {
                    var merged = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase);

                    foreach (var pair in match)
                        merged[pair.Key] = pair.Value;

                    joined.Add(merged);
                }
            }

            rows = joined;
        }

        return rows;
    }

    private static Dictionary<string, object?> Prefix(string alias, IReadOnlyDictionary<string, object?> row)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in row)
            result[$"{alias}.{pair.Key}"] = pair.Value;

        return result;
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        var leftId = left switch
        {
            EntityReference reference => reference.Id,
            Guid guid => guid,
            _ => (Guid?)null
        };

        var rightId = right switch
        {
            EntityReference reference => reference.Id,
            Guid guid => guid,
            _ => (Guid?)null
        };

        if (leftId != null && rightId != null)
            return leftId == rightId;

        return Equals(left, right);
    }

    private static IEnumerable<(XElement Filter, string Alias)> CollectFilters(XElement entityElement, string alias)
    {
        var filter = entityElement.Element("filter");

        if (filter != null)
            yield return (filter, alias);

        foreach (var link in entityElement.Elements("link-entity"))
            foreach (var nested in CollectFilters(link, AliasOf(link)))
                yield return nested;
    }

    private static Func<Dictionary<string, object?>, bool> BuildFilter(
        XElement filter,
        string defaultAlias,
        Dictionary<string, MockTable> aliases)
    {
        var type = ((string?)filter.Attribute("type") ?? "and").ToLowerInvariant();
        var conditions = new List<Func<Dictionary<string, object?>, bool>>();

        foreach (var element in filter.Elements())
        {
            if (element.Name == "condition")
                conditions.Add(BuildCondition(element, defaultAlias, aliases));
            else if (element.Name == "filter")
                conditions.Add(BuildFilter(element, defaultAlias, aliases));
            else
                throw new NotSupportedException($"mock: <{element.Name}> in een filter wordt nog niet ondersteund.");
        }

        return type == "or"
            ? row => conditions.Any(condition => condition(row))
            : row => conditions.All(condition => condition(row));
    }

    private static Func<Dictionary<string, object?>, bool> BuildCondition(
        XElement condition,
        string defaultAlias,
        Dictionary<string, MockTable> aliases)
    {
        var attributeName = (string?)condition.Attribute("attribute")
            ?? throw new NotSupportedException("mock: <condition> zonder attribuut");

        var alias = (string?)condition.Attribute("entityname") ?? defaultAlias;

        if (!aliases.TryGetValue(alias, out var table))
            throw new NotSupportedException($"mock: onbekende entiteit '{alias}' in filter.");

        var column = table.Find(attributeName)
            ?? throw new NotSupportedException($"mock: kolom '{attributeName}' bestaat niet op '{table.LogicalName}'.");

        var key = $"{alias}.{column.LogicalName}";
        var op = ((string?)condition.Attribute("operator") ?? "eq").ToLowerInvariant();
        var rawValue = (string?)condition.Attribute("value");

        object? Get(Dictionary<string, object?> row) => row.TryGetValue(key, out var value) ? value : null;

        return op switch
        {
            "null" => row => Get(row) == null,
            "not-null" => row => Get(row) != null,
            "eq" => row => Compare(Get(row), rawValue, column) == 0,
            "ne" or "neq" => row => Compare(Get(row), rawValue, column) != 0,
            "gt" => row => Compare(Get(row), rawValue, column) > 0,
            "ge" => row => Compare(Get(row), rawValue, column) >= 0,
            "lt" => row => Compare(Get(row), rawValue, column) < 0,
            "le" => row => Compare(Get(row), rawValue, column) <= 0,
            "like" => row => MatchLike(Get(row), rawValue, negate: false),
            "not-like" => row => MatchLike(Get(row), rawValue, negate: true),
            "begins-with" => row => MatchEdge(Get(row), rawValue, begin: true, negate: false),
            "not-begin-with" => row => MatchEdge(Get(row), rawValue, begin: true, negate: true),
            "ends-with" => row => MatchEdge(Get(row), rawValue, begin: false, negate: false),
            "not-end-with" => row => MatchEdge(Get(row), rawValue, begin: false, negate: true),
            "in" => row => InList(Get(row), condition, column, negate: false),
            "not-in" => row => InList(Get(row), condition, column, negate: true),
            _ => throw new NotSupportedException($"mock: operator '{op}' wordt nog niet ondersteund.")
        };
    }

    private static int Compare(object? actual, string? expected, MockColumn column)
    {
        if (actual == null || expected == null)
            return actual == null && expected == null ? 0 : -1;

        switch (column.Kind)
        {
            case MockColumnKind.Money:
            case MockColumnKind.Decimal:
                return Convert.ToDecimal(actual, CultureInfo.InvariantCulture)
                    .CompareTo(decimal.Parse(expected, NumberStyles.Any, CultureInfo.InvariantCulture));
            case MockColumnKind.Integer:
            case MockColumnKind.OptionSet:
                return Convert.ToInt32(actual, CultureInfo.InvariantCulture)
                    .CompareTo(int.Parse(expected, CultureInfo.InvariantCulture));
            case MockColumnKind.DateTime:
                return Convert.ToDateTime(actual, CultureInfo.InvariantCulture)
                    .CompareTo(ParseDate(expected));
            case MockColumnKind.Boolean:
                return Convert.ToBoolean(actual, CultureInfo.InvariantCulture)
                    .CompareTo(expected is "1" or "true");
            case MockColumnKind.UniqueIdentifier:
            case MockColumnKind.Lookup:
                return IdOf(actual).Equals(Guid.Parse(expected)) ? 0 : -1;
            default:
                return string.Compare(Convert.ToString(actual, CultureInfo.InvariantCulture), expected, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static Guid IdOf(object value) => value switch
    {
        EntityReference reference => reference.Id,
        Guid guid => guid,
        _ => Guid.Empty
    };

    private static DateTime ParseDate(string value)
        => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static bool MatchLike(object? actual, string? pattern, bool negate)
    {
        if (actual == null)
            return negate;

        if (pattern == null)
            return !negate;

        var regex = "^" + Regex.Escape(pattern).Replace("%", ".*").Replace("_", ".") + "$";
        var match = Regex.IsMatch(Convert.ToString(actual, CultureInfo.InvariantCulture) ?? "", regex, RegexOptions.IgnoreCase);

        return negate ? !match : match;
    }

    private static bool MatchEdge(object? actual, string? value, bool begin, bool negate)
    {
        if (actual == null || value == null)
            return negate;

        var text = Convert.ToString(actual, CultureInfo.InvariantCulture) ?? "";

        var match = begin
            ? text.StartsWith(value, StringComparison.OrdinalIgnoreCase)
            : text.EndsWith(value, StringComparison.OrdinalIgnoreCase);

        return negate ? !match : match;
    }

    private static bool InList(object? actual, XElement condition, MockColumn column, bool negate)
    {
        var found = condition.Elements("value")
            .Select(value => (string?)value.Attribute("value") ?? value.Value)
            .Any(value => Compare(actual, value, column) == 0);

        return negate ? !found : found;
    }

    private static List<Dictionary<string, object?>> ApplyOrder(
        List<Dictionary<string, object?>> rows,
        XElement entityElement,
        Dictionary<string, MockTable> aliases,
        string mainAlias)
    {
        var orders = new List<(string Key, MockColumn Column, bool Descending)>();

        void Collect(XElement element, string alias)
        {
            foreach (var order in element.Elements("order"))
            {
                var orderAlias = (string?)order.Attribute("alias") ?? alias;
                var attributeName = (string?)order.Attribute("attribute");

                if (attributeName == null || !aliases.TryGetValue(orderAlias, out var table))
                    continue;

                var column = table.Find(attributeName);

                if (column == null)
                    continue;

                orders.Add((
                    $"{orderAlias}.{column.LogicalName}",
                    column,
                    string.Equals((string?)order.Attribute("descending"), "true", StringComparison.OrdinalIgnoreCase)));
            }

            foreach (var link in element.Elements("link-entity"))
                Collect(link, AliasOf(link));
        }

        Collect(entityElement, mainAlias);

        if (orders.Count == 0)
            return rows;

        var sorted = rows;

        for (var i = orders.Count - 1; i >= 0; i--)
        {
            var order = orders[i];
            var comparer = new RowComparer(order.Key, order.Column, order.Descending);
            sorted = sorted.OrderBy(row => row, comparer).ToList();
        }

        return sorted;
    }

    /// <summary>
    /// Zet de gevraagde attributen (met hun alias) in het resultaat, precies zoals
    /// het FetchXML ze vraagt — de engine leest ze daarop terug.
    /// </summary>
    private static void Project(
        XElement entityElement,
        Dictionary<string, object?> row,
        string alias,
        Dictionary<string, MockTable> aliases,
        Entity target)
    {
        var table = aliases[alias];
        var all = entityElement.Element("all-attributes") != null;

        if (all || !entityElement.Elements("attribute").Any())
        {
            foreach (var column in table.Columns)
            {
                if (row.TryGetValue($"{alias}.{column.LogicalName}", out var value))
                    target[column.LogicalName] = value;
            }
        }
        else
        {
            foreach (var attribute in entityElement.Elements("attribute"))
            {
                var name = (string?)attribute.Attribute("name");

                if (name == null || !row.TryGetValue($"{alias}.{name}", out var value))
                    continue;

                var outputName = (string?)attribute.Attribute("alias") ?? name;
                target[outputName] = value;
            }
        }

        foreach (var link in entityElement.Elements("link-entity"))
        {
            var linkAlias = AliasOf(link);
            var linkTable = aliases[linkAlias];

            foreach (var attribute in link.Elements("attribute"))
            {
                var name = (string?)attribute.Attribute("name");

                if (name == null || !row.TryGetValue($"{linkAlias}.{name}", out var value))
                    continue;

                var outputName = (string?)attribute.Attribute("alias") ?? name;

                // Dataverse levert gekoppelde kolommen als AliasedValue; de engine
                // leest ze zo terug.
                target[outputName] = new AliasedValue(linkTable.LogicalName, name, value);
            }
        }
    }

    private sealed class RowComparer : IComparer<Dictionary<string, object?>>
    {
        private readonly string _key;
        private readonly MockColumn _column;
        private readonly bool _descending;

        public RowComparer(string key, MockColumn column, bool descending)
        {
            _key = key;
            _column = column;
            _descending = descending;
        }

        public int Compare(Dictionary<string, object?>? x, Dictionary<string, object?>? y)
        {
            var left = x != null && x.TryGetValue(_key, out var l) ? l : null;
            var right = y != null && y.TryGetValue(_key, out var r) ? r : null;

            int result;

            if (left == null && right == null)
                result = 0;
            else if (left == null)
                result = 1;
            else if (right == null)
                result = -1;
            else
            {
                result = _column.Kind switch
                {
                    MockColumnKind.Money or MockColumnKind.Decimal => Convert.ToDecimal(left, CultureInfo.InvariantCulture)
                        .CompareTo(Convert.ToDecimal(right, CultureInfo.InvariantCulture)),
                    MockColumnKind.Integer or MockColumnKind.OptionSet => Convert.ToInt32(left, CultureInfo.InvariantCulture)
                        .CompareTo(Convert.ToInt32(right, CultureInfo.InvariantCulture)),
                    MockColumnKind.DateTime => Convert.ToDateTime(left, CultureInfo.InvariantCulture)
                        .CompareTo(Convert.ToDateTime(right, CultureInfo.InvariantCulture)),
                    _ => string.Compare(
                        Convert.ToString(left, CultureInfo.InvariantCulture),
                        Convert.ToString(right, CultureInfo.InvariantCulture),
                        StringComparison.OrdinalIgnoreCase)
                };
            }

            return _descending ? -result : result;
        }
    }
}
