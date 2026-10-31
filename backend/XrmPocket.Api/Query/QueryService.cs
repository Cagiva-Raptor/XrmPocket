using System.Diagnostics;
using System.Globalization;
using MarkMpn.Sql4Cds.Engine;
using MarkMpn.Sql4Cds.Engine.ExecutionPlan;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using XrmPocket.Api.Configuration;
using XrmPocket.Api.Connections;
using XrmPocket.Api.Mock;

namespace XrmPocket.Api.Query;

/// <summary>
/// De kern: SQL in, resultaat uit. De SQL 4 CDS-engine zet de SQL om naar FetchXML
/// en voert die uit tegen de gekozen verbinding (mock of Dataverse).
/// </summary>
public sealed class QueryService
{
    private const int MaxSqlLength = 20_000;

    private readonly ConnectionStore _connections;
    private readonly ConnectionFactory _factory;
    private readonly XrmPocketOptions _options;
    private readonly ILogger<QueryService> _logger;

    public QueryService(
        ConnectionStore connections,
        ConnectionFactory factory,
        IOptions<XrmPocketOptions> options,
        ILogger<QueryService> logger)
    {
        _connections = connections;
        _factory = factory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<QueryResponse> ExecuteAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        var sql = request.Sql?.Trim();

        if (string.IsNullOrEmpty(sql))
            return Fail(null, null, new QueryError("empty_sql", "De query is leeg.", "Typ een SELECT-statement en probeer opnieuw."));

        if (sql.Length > MaxSqlLength)
            return Fail(null, null, new QueryError("sql_too_long", $"De query is te lang ({sql.Length} tekens, max {MaxSqlLength}).", null));

        var profile = ResolveConnection(request);

        if (profile == null)
        {
            return Fail(null, null, new QueryError(
                "unknown_connection",
                "Deze verbinding bestaat niet (meer).",
                "Kies een verbinding in de lijst of maak een nieuwe aan."));
        }

        var missing = _factory.DescribeMissingConfiguration(profile);

        if (missing != null)
        {
            return Fail(profile.Id, profile.Name, new QueryError(
                "connection_not_configured",
                $"Verbinding '{profile.Name}' is nog niet bruikbaar.",
                missing));
        }

        if (!SqlSafetyCheck.IsReadOnly(sql, out var unsafeReason))
        {
            return Fail(profile.Id, profile.Name, new QueryError(
                "write_not_allowed",
                "Deze versie voert enkel leesqueries uit.",
                unsafeReason));
        }

        var maxRows = Math.Clamp(request.MaxRows ?? _options.DefaultMaxRows, 1, _options.HardMaxRows);

        try
        {
            var dataSource = _factory.CreateDataSource(profile);
            var dataSources = new Dictionary<string, DataSource>(StringComparer.OrdinalIgnoreCase)
            {
                [dataSource.Name] = dataSource
            };

            using var connection = new Sql4CdsConnection(dataSources);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = profile.TimeoutSeconds;

            var stopwatch = Stopwatch.StartNew();

            // Plan (en dus de FetchXML) maken zonder uit te voeren: zo kan de UI de
            // vertaling tonen, ook als de query daarna faalt.
            var plan = command.GeneratePlan(compileForExecution: false);
            var fetchXml = plan
                .SelectMany(Walk)
                .OfType<IFetchXmlExecutionPlanNode>()
                .Select(node => node.FetchXmlString)
                .ToList();

            var columns = new List<ResultColumn>();
            var rows = new List<IReadOnlyList<object?>>();
            var truncated = false;

            using (var reader = command.ExecuteReader())
            {
                for (var i = 0; i < reader.FieldCount; i++)
                    columns.Add(new ResultColumn(reader.GetName(i), reader.GetDataTypeName(i)));

                while (reader.Read())
                {
                    if (rows.Count >= maxRows)
                    {
                        truncated = true;
                        break;
                    }

                    var values = new object?[reader.FieldCount];

                    for (var i = 0; i < reader.FieldCount; i++)
                        values[i] = ToJsonValue(reader.IsDBNull(i) ? null : reader.GetValue(i));

                    rows.Add(values);
                }
            }

            stopwatch.Stop();
            _connections.Touch(profile.Id);

            return new QueryResponse
            {
                Ok = true,
                ConnectionId = profile.Id,
                ConnectionName = profile.Name,
                Columns = columns,
                Rows = rows,
                Truncated = truncated,
                FetchXml = fetchXml,
                DurationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query op verbinding {Connection} mislukt", profile.Name);
            return Fail(profile.Id, profile.Name, MapError(ex, profile));
        }
    }

    /// <summary>
    /// Zoekt de verbinding: expliciet id, anders de oude omgevingsnaam, anders de
    /// standaardverbinding (zoals XrmToolBox bij het starten de ingestelde
    /// beginverbinding gebruikt).
    /// </summary>
    private ConnectionProfile? ResolveConnection(QueryRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ConnectionId))
            return _connections.Get(request.ConnectionId.Trim());

        if (!string.IsNullOrWhiteSpace(request.Environment))
        {
            var wanted = request.Environment.Trim();
            var match = _connections.List().FirstOrDefault(profile =>
                string.Equals(profile.Environment, wanted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile.Name, wanted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile.Id, wanted, StringComparison.OrdinalIgnoreCase));

            if (match != null)
                return match;
        }

        return _connections.GetDefault();
    }

    private static QueryError MapError(Exception ex, ConnectionProfile profile)
    {
        var message = ex.GetBaseException().Message;

        if (ex is ConnectionNotConfiguredException)
        {
            return new QueryError(
                "connection_not_configured",
                $"Verbinding '{profile.Name}' is niet bruikbaar.",
                message);
        }

        if (ex is NotSupportedException)
        {
            return new QueryError("not_supported", message.Trim(),
                "Mock ondersteunt momenteel: " + FetchXmlEvaluator.SupportedOperators);
        }

        if (ex is Sql4CdsException)
        {
            return new QueryError(
                "sql_error",
                message.Trim(),
                profile.IsMock
                    ? "Controleer tabel- en kolomnamen. In mock-modus bestaan: account, contact, incident."
                    : "Controleer tabel- en kolomnamen en je rechten in deze omgeving.");
        }

        return new QueryError("unexpected", message.Trim(), "Zie de backend-log voor details.");
    }

    private static QueryResponse Fail(string? connectionId, string? connectionName, QueryError error) => new()
    {
        Ok = false,
        ConnectionId = connectionId,
        ConnectionName = connectionName,
        Error = error
    };

    private static IEnumerable<IExecutionPlanNode> Walk(IExecutionPlanNode node)
    {
        yield return node;

        foreach (var source in node.GetSources())
            foreach (var child in Walk(source))
                yield return child;
    }

    private static object? ToJsonValue(object? value) => value switch
    {
        null or DBNull => null,
        Money money => money.Value,
        OptionSetValue option => option.Value,
        EntityReference reference => new Dictionary<string, object?>
        {
            ["id"] = reference.Id.ToString(),
            ["name"] = reference.Name,
            ["logicalName"] = reference.LogicalName
        },
        DateTime dateTime => dateTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
        Guid guid => guid.ToString(),
        byte[] bytes => Convert.ToBase64String(bytes),
        _ => value
    };
}
