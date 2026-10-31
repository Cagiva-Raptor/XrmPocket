namespace XrmPocket.Api.Query;

/// <summary>Wat de client vraagt: SQL, een verbinding en een rijlimiet.</summary>
public sealed class QueryRequest
{
    public string Sql { get; set; } = "";

    /// <summary>Id van een opgeslagen verbinding. Leeg = de standaardverbinding.</summary>
    public string? ConnectionId { get; set; }

    /// <summary>
    /// Verouderd: naam of omgevingssjabloon van een verbinding. Wordt gebruikt als
    /// <see cref="ConnectionId"/> leeg is (oude clients/curl).
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>Aantal rijen dat de client wil; de backend kapt af op een harde grens.</summary>
    public int? MaxRows { get; set; }
}

/// <summary>Eén kolom in het resultaat.</summary>
public sealed record ResultColumn(string Name, string Type);

/// <summary>Foutmelding met een korte hint (in het Nederlands, voor in de UI).</summary>
public sealed record QueryError(string Code, string Message, string? Hint);

/// <summary>Alles wat de UI nodig heeft na één query.</summary>
public sealed class QueryResponse
{
    public bool Ok { get; init; }

    /// <summary>Verbinding die daadwerkelijk gebruikt is.</summary>
    public string? ConnectionId { get; init; }

    public string? ConnectionName { get; init; }

    public IReadOnlyList<ResultColumn> Columns { get; init; } = Array.Empty<ResultColumn>();

    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; init; } = Array.Empty<IReadOnlyList<object?>>();

    public int RowCount => Rows.Count;

    /// <summary>Waar: de rijlimiet is bereikt, er waren meer rijen.</summary>
    public bool Truncated { get; init; }

    /// <summary>De FetchXML die de engine van deze SQL maakte.</summary>
    public IReadOnlyList<string> FetchXml { get; init; } = Array.Empty<string>();

    public double DurationMs { get; init; }

    public QueryError? Error { get; init; }
}
