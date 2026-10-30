using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using MarkMpn.Sql4Cds.Engine;
using MarkMpn.Sql4Cds.Engine.ExecutionPlan;
using Microsoft.Extensions.Options;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using XrmPocket.Api.Configuration;
using XrmPocket.Api.Environments;
using XrmPocket.Api.Mock;

namespace XrmPocket.Api.Query;

/// <summary>
/// De kern: SQL in, resultaat uit. De SQL 4 CDS-engine zet de SQL om naar FetchXML
/// en voert die uit tegen de gekozen omgeving (mock of Dataverse).
/// </summary>
public sealed class QueryService
{
    private const int MaxSqlLength = 20_000;
    private const string DefaultEnvironment = "test";

    private readonly EnvironmentCatalog _catalog;
    private readonly XrmPocketOptions _options;
    private readonly MockDataset _mockDataset;
    private readonly ConcurrentDictionary<string, Lazy<ServiceClient>> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<QueryService> _logger;

    public QueryService(
        EnvironmentCatalog catalog,
        IOptions<XrmPocketOptions> options,
        MockDataset mockDataset,
        ILogger<QueryService> logger)
    {
        _catalog = catalog;
        _options = options.Value;
        _mockDataset = mockDataset;
        _logger = logger;
    }

    public async Task<QueryResponse> ExecuteAsync(QueryRequest request, CancellationToken cancellationToken)
    {
        var sql = request.Sql?.Trim();

        if (string.IsNullOrEmpty(sql))
            return Fail(null, new QueryError("empty_sql", "De query is leeg.", "Typ een SELECT-statement en probeer opnieuw."));

        if (sql.Length > MaxSqlLength)
            return Fail(null, new QueryError("sql_too_long", $"De query is te lang ({sql.Length} tekens, max {MaxSqlLength}).", null));

        var environmentId = string.IsNullOrWhiteSpace(request.Environment) ? DefaultEnvironment : request.Environment.Trim();
        var environment = _catalog.Find(environmentId);

        if (environment == null)
        {
            return Fail(environmentId, new QueryError(
                "unknown_environment",
                $"Onbekende omgeving '{environmentId}'.",
                "Kies een omgeving uit de lijst; 'mock' werkt altijd."));
        }

        if (!environment.Configured)
        {
            return Fail(environmentId, new QueryError(
                "environment_not_configured",
                $"Omgeving '{environment.Label}' is nog niet bruikbaar.",
                (environment.Note ?? "Configuratie ontbreekt.") + " Gebruik 'mock' om de rest te testen."));
        }

        var maxRows = Math.Clamp(request.MaxRows ?? _options.DefaultMaxRows, 1, _options.HardMaxRows);

        if (!SqlSafetyCheck.IsReadOnly(sql, out var unsafeReason))
        {
            return Fail(environmentId, new QueryError(
                "write_not_allowed",
                "Deze versie voert enkel leesqueries uit.",
                unsafeReason));
        }

        try
        {
            var dataSource = GetDataSource(environment);
            var dataSources = new Dictionary<string, DataSource>(StringComparer.OrdinalIgnoreCase)
            {
                [dataSource.Name] = dataSource
            };

            using var connection = new Sql4CdsConnection(dataSources);
            using var command = connection.CreateCommand();
            command.CommandText = sql;

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

            return new QueryResponse
            {
                Ok = true,
                Environment = environmentId,
                Columns = columns,
                Rows = rows,
                Truncated = truncated,
                FetchXml = fetchXml,
                DurationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query op omgeving {Environment} mislukt", environmentId);
            return Fail(environmentId, MapError(ex, environment));
        }
    }

    private static QueryError MapError(Exception ex, EnvironmentInfo environment)
    {
        var message = ex.GetBaseException().Message;

        if (ex is FailedToConnectToDataverseException || message.Contains("FailedToConnect", StringComparison.OrdinalIgnoreCase))
        {
            return new QueryError(
                "connect_failed",
                $"Kan geen verbinding maken met {environment.Label}.",
                "Controleer het client secret en de application user in Dataverse (zie START.md §4).");
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
                "Controleer tabel- en kolomnamen. In mock-modus bestaan: account, contact, incident.");
        }

        return new QueryError("unexpected", message.Trim(), "Zie de backend-log voor details.");
    }

    private static QueryResponse Fail(string? environmentId, QueryError error, IReadOnlyList<string>? fetchXml = null) => new()
    {
        Ok = false,
        Environment = environmentId,
        Error = error,
        FetchXml = fetchXml ?? Array.Empty<string>()
    };

    private DataSource GetDataSource(EnvironmentInfo environment)
    {
        if (environment.Kind == "mock")
            return MockOrganizationService.CreateDataSource(_mockDataset);

        var client = _clients.GetOrAdd(environment.Id, id => new Lazy<ServiceClient>(() => CreateClient(environment))).Value;

        if (!client.IsReady)
        {
            throw new FailedToConnectToDataverseException(
                client.LastError ?? $"Verbinding met {environment.Label} is niet gereed.");
        }

        return new DataSource(client);
    }

    private ServiceClient CreateClient(EnvironmentInfo environment)
    {
        if (string.IsNullOrWhiteSpace(environment.Url))
            throw new InvalidOperationException("Geen URL voor omgeving " + environment.Label);

        var secretPath = XrmPocketOptions.ExpandPath(_options.ClientSecretFile);
        var secret = File.ReadAllText(secretPath).Trim();

        // Het secret blijft in dit proces; het komt nooit in een antwoord of log.
        return new ServiceClient(new Uri(environment.Url), _options.ClientId, secret, useUniqueInstance: true, logger: null);
    }

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

    /// <summary>Fout bij het opzetten van een Dataverse-verbinding (los type voor de mapper).</summary>
    private sealed class FailedToConnectToDataverseException : Exception
    {
        public FailedToConnectToDataverseException(string message) : base(message)
        {
        }
    }
}
