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
    private const int MaxSqlLength = 200_000;

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

        if (!SqlSafetyCheck.Check(sql, out var isWrite, out var unsafeReason))
        {
            return Fail(profile.Id, profile.Name, new QueryError(
                "not_supported",
                "Deze instructie wordt niet ondersteund.",
                unsafeReason));
        }

        var maxRows = Math.Clamp(request.MaxRows ?? _options.DefaultMaxRows, 1, _options.HardMaxRows);
        var messages = new List<string>();

        try
        {
            var dataSource = _factory.CreateDataSource(profile);
            var dataSources = new Dictionary<string, DataSource>(StringComparer.OrdinalIgnoreCase)
            {
                [dataSource.Name] = dataSource
            };

            using var connection = new Sql4CdsConnection(dataSources);
            using var command = connection.CreateCommand();
            command.CommandText = CollationCompatibility.Normalize(sql);
            command.CommandTimeout = profile.TimeoutSeconds;
            using var cancelRegistration = cancellationToken.Register(() => command.Cancel());

            connection.InfoMessage += (_, args) => messages.Add(args.Message.Message);

            if (request.DryRun)
            {
                var dryPlan = command.GeneratePlan(compileForExecution: false);
                var dryFetchXml = dryPlan
                    .SelectMany(Walk)
                    .OfType<IFetchXmlExecutionPlanNode>()
                    .Select(node => node.FetchXmlString)
                    .ToList();

                return new QueryResponse
                {
                    Ok = true,
                    ConnectionId = profile.Id,
                    ConnectionName = profile.Name,
                    FetchXml = dryFetchXml,
                    Messages = new[] { "Controle geslaagd: de engine kan dit script plannen. Er is niets uitgevoerd." },
                };
            }

            var stopwatch = Stopwatch.StartNew();
            var fetchXml = new List<string>();
            var resultSets = new List<QueryResultSet>();
            var truncated = false;
            var rowsAffected = 0;
            var hasRowsAffected = false;

            // Dezelfde uitvoeringsroute als XrmToolbox: ExecuteReader voor het hele
            // script, inclusief DML, PRINT en meerdere resultaatsets.
            if (!isWrite)
            {
                var plan = command.GeneratePlan(compileForExecution: false);
                fetchXml.AddRange(plan
                    .SelectMany(Walk)
                    .OfType<IFetchXmlExecutionPlanNode>()
                    .Select(node => node.FetchXmlString));
            }

            command.StatementCompleted += (_, args) =>
            {
                if (args.RecordsAffected >= 0)
                {
                    rowsAffected += args.RecordsAffected;
                    hasRowsAffected = true;
                }
            };

            using (var reader = command.ExecuteReader())
            {
                if (!reader.IsClosed)
                {
                    do
                    {
                        var columns = new List<ResultColumn>();
                        for (var i = 0; i < reader.FieldCount; i++)
                            columns.Add(new ResultColumn(reader.GetName(i), reader.GetDataTypeName(i)));

                        var rows = new List<IReadOnlyList<object?>>();
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

                        if (columns.Count > 0)
                            resultSets.Add(new QueryResultSet(columns, rows, rows.Count >= maxRows));
                    }
                    while (reader.NextResult());
                }
            }

            stopwatch.Stop();
            _connections.Touch(profile.Id);
            var primary = resultSets.FirstOrDefault();

            return new QueryResponse
            {
                Ok = true,
                ConnectionId = profile.Id,
                ConnectionName = profile.Name,
                Columns = primary?.Columns ?? Array.Empty<ResultColumn>(),
                Rows = primary?.Rows ?? Array.Empty<IReadOnlyList<object?>>(),
                ResultSets = resultSets,
                Truncated = truncated,
                FetchXml = fetchXml,
                Messages = messages,
                RowsAffected = hasRowsAffected ? rowsAffected : null,
                DurationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query op verbinding {Connection} mislukt", profile.Name);
            return new QueryResponse
            {
                Ok = false,
                ConnectionId = profile.Id,
                ConnectionName = profile.Name,
                Messages = messages,
                Error = WithLocation(MapError(ex, profile), ex, sql)
            };
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
        var message = CleanMessage(ex.GetBaseException().Message);

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

        return new QueryError("unexpected", message, "Zie de backend-log voor details.");
    }


    private static string CleanMessage(string raw)
    {
        var text = raw.Trim();
        var cut = text.IndexOf("\nRequestId:", StringComparison.Ordinal);

        if (cut >= 0)
            text = text[..cut].Trim();

        if (text.StartsWith('{'))
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(text);
                if (document.RootElement.TryGetProperty("Message", out var message))
                    text = message.GetString() ?? text;
            }
            catch (System.Text.Json.JsonException)
            {
                // Geen geldige JSON: de ruwe tekst blijft staan.
            }
        }

        return text;
    }

    private static QueryError WithLocation(QueryError error, Exception ex, string sql)
    {
        var quoted = System.Text.RegularExpressions.Regex.Match(error.Message, "'([^']+)'");

        if (quoted.Success)
        {
            var pattern = @"(?<![\w@#])" + System.Text.RegularExpressions.Regex.Escape(quoted.Groups[1].Value) + @"(?!\w)";
            var match = System.Text.RegularExpressions.Regex.Match(sql, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (match.Success)
            {
                var (line, column) = PositionOf(sql, match.Index);
                return error with { Line = line, Column = column };
            }
        }

        var (engineLine, engineColumn) = FindLocation(ex, sql);
        return error with { Line = engineLine, Column = engineColumn };
    }

    private static (int Line, int Column) PositionOf(string sql, int offset)
    {
        var before = sql[..offset];
        return (before.Count(c => c == '\n') + 1, offset - (before.LastIndexOf('\n') + 1) + 1);
    }

    /// <summary>
    /// Zoekt regel- en kolomnummer van de fout. De engine-types zijn niet gedocumenteerd,
    /// dus lezen we eigenschappen via reflectie: een regelnummer als de engine dat geeft,
    /// anders de positie (offset) in de SQL, die we zelf omrekenen.
    /// </summary>
    private static (int? Line, int? Column) FindLocation(Exception ex, string sql)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            var found = ReadLocation(current, sql);
            if (found.Line != null)
                return found;

            var errors = current.GetType().GetProperty("Errors")?.GetValue(current) as System.Collections.IEnumerable;

            if (errors != null)
            {
                foreach (var item in errors)
                {
                    if (item == null)
                        continue;

                    found = ReadLocation(item, sql);
                    if (found.Line != null)
                        return found;
                }
            }
        }

        return (null, null);
    }

    private static (int? Line, int? Column) ReadLocation(object target, string sql)
    {
        var offset = ReadInt(target, "Offset") ?? ReadInt(target, "StartOffset") ?? ReadInt(target, "Index");

        if (offset is >= 0 && offset <= sql.Length)
        {
            var before = sql[..offset.Value];
            var lineNumber = before.Count(c => c == '\n') + 1;
            var columnNumber = offset.Value - (before.LastIndexOf('\n') + 1) + 1;
            return (lineNumber, columnNumber);
        }

        var line = ReadInt(target, "LineNumber") ?? ReadInt(target, "ErrorLine") ?? ReadInt(target, "StartLine");
        var column = ReadInt(target, "ColumnNumber") ?? ReadInt(target, "Column");

        if (line is > 0)
            return (line, column is > 0 ? column : null);

        return (null, null);
    }

    private static int? ReadInt(object target, string name)
    {
        var value = target.GetType().GetProperty(name)?.GetValue(target);

        return value switch
        {
            int number => number,
            short number => number,
            long number when number is >= 0 and <= int.MaxValue => (int)number,
            _ => null
        };
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
