namespace XrmPocket.Api.Query;

/// <summary>Wat de client vraagt: SQL, een verbinding en een rijlimiet.</summary>
public sealed class QueryRequest
{
    public string Sql { get; set; } = "";
    public string? ConnectionId { get; set; }
    public string? Environment { get; set; }
    public int? MaxRows { get; set; }
    public bool DryRun { get; set; }
}

public sealed record ResultColumn(string Name, string Type);
public sealed record QueryResultSet(IReadOnlyList<ResultColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows, bool Truncated);
public sealed record QueryError(string Code, string Message, string? Hint, int? Line = null, int? Column = null);

public sealed class QueryResponse
{
    public bool Ok { get; init; }
    public string? ConnectionId { get; init; }
    public string? ConnectionName { get; init; }
    public IReadOnlyList<ResultColumn> Columns { get; init; } = Array.Empty<ResultColumn>();
    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; init; } = Array.Empty<IReadOnlyList<object?>>();
    public int RowCount => Rows.Count;
    public bool Truncated { get; init; }
    public IReadOnlyList<QueryResultSet> ResultSets { get; init; } = Array.Empty<QueryResultSet>();
    public int? RowsAffected { get; init; }
    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FetchXml { get; init; } = Array.Empty<string>();
    public double DurationMs { get; init; }
    public QueryError? Error { get; init; }
}
