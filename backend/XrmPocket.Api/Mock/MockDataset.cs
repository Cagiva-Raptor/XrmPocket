using Microsoft.Xrm.Sdk;

namespace XrmPocket.Api.Mock;

/// <summary>Soort kolom in de synthetische dataset.</summary>
public enum MockColumnKind
{
    UniqueIdentifier,
    String,
    Integer,
    Decimal,
    Money,
    DateTime,
    Boolean,
    OptionSet,
    Lookup
}

/// <summary>Eén kolom in een synthetische tabel.</summary>
public sealed class MockColumn
{
    public required string LogicalName { get; init; }
    public required MockColumnKind Kind { get; init; }
    public string? Label { get; init; }
    public int MaxLength { get; init; } = 200;
    public string? LookupTarget { get; init; }
    public bool IsPrimaryId { get; init; }
    public bool IsPrimaryName { get; init; }
}

/// <summary>Eén synthetische tabel: kolommen + rijen.</summary>
public sealed class MockTable
{
    public required string LogicalName { get; init; }
    public required string Label { get; init; }
    public required int ObjectTypeCode { get; init; }
    public required IReadOnlyList<MockColumn> Columns { get; init; }
    public required IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; }

    public MockColumn? PrimaryId => Columns.FirstOrDefault(c => c.IsPrimaryId);
    public MockColumn? PrimaryName => Columns.FirstOrDefault(c => c.IsPrimaryName);
    public MockColumn? Find(string logicalName) =>
        Columns.FirstOrDefault(c => string.Equals(c.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Synthetische dataset waarmee de hele keten (SQL → engine → FetchXML → rijen)
/// zonder Dataverse-verbinding te testen is. Alle namen en waarden zijn verzonnen.
/// </summary>
public sealed class MockDataset
{
    public const string DataSourceName = "mock";

    private static readonly Guid ContosoId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FabrikamId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid NorthwindId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AdventureId = new("44444444-4444-4444-4444-444444444444");

    private readonly List<MockTable> _tables;

    public MockDataset()
    {
        _tables = new List<MockTable> { BuildAccount(), BuildContact(), BuildIncident() };
    }

    public IReadOnlyList<MockTable> Tables => _tables;

    public MockTable? Find(string logicalName) =>
        _tables.FirstOrDefault(t => string.Equals(t.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase));

    private static MockTable BuildAccount() => new()
    {
        LogicalName = "account",
        Label = "Account",
        ObjectTypeCode = 1,
        Columns = new[]
        {
            new MockColumn { LogicalName = "accountid", Kind = MockColumnKind.UniqueIdentifier, Label = "Account", IsPrimaryId = true },
            new MockColumn { LogicalName = "name", Kind = MockColumnKind.String, Label = "Account Name", MaxLength = 160, IsPrimaryName = true },
            new MockColumn { LogicalName = "revenue", Kind = MockColumnKind.Money, Label = "Annual Revenue" },
            new MockColumn { LogicalName = "numberofemployees", Kind = MockColumnKind.Integer, Label = "Number of Employees" },
            new MockColumn { LogicalName = "createdon", Kind = MockColumnKind.DateTime, Label = "Created On" },
            new MockColumn { LogicalName = "statecode", Kind = MockColumnKind.OptionSet, Label = "Status" }
        },
        Rows = new[]
        {
            Row(
                ("accountid", ContosoId),
                ("name", "Contoso"),
                ("revenue", 1_500_000m),
                ("numberofemployees", 120),
                ("createdon", new DateTime(2024, 3, 14, 9, 0, 0, DateTimeKind.Utc)),
                ("statecode", 0)),
            Row(
                ("accountid", FabrikamId),
                ("name", "Fabrikam"),
                ("revenue", 250_000m),
                ("numberofemployees", 18),
                ("createdon", new DateTime(2024, 7, 2, 13, 30, 0, DateTimeKind.Utc)),
                ("statecode", 0)),
            Row(
                ("accountid", NorthwindId),
                ("name", "Northwind Traders"),
                ("revenue", 4_200_000m),
                ("numberofemployees", 340),
                ("createdon", new DateTime(2023, 11, 5, 8, 15, 0, DateTimeKind.Utc)),
                ("statecode", 1)),
            Row(
                ("accountid", AdventureId),
                ("name", "Adventure Works"),
                ("revenue", 890_000m),
                ("numberofemployees", 64),
                ("createdon", new DateTime(2025, 1, 20, 16, 45, 0, DateTimeKind.Utc)),
                ("statecode", 0))
        }
    };

    private static MockTable BuildContact() => new()
    {
        LogicalName = "contact",
        Label = "Contact",
        ObjectTypeCode = 2,
        Columns = new[]
        {
            new MockColumn { LogicalName = "contactid", Kind = MockColumnKind.UniqueIdentifier, Label = "Contact", IsPrimaryId = true },
            new MockColumn { LogicalName = "fullname", Kind = MockColumnKind.String, Label = "Full Name", MaxLength = 160, IsPrimaryName = true },
            new MockColumn { LogicalName = "emailaddress1", Kind = MockColumnKind.String, Label = "Email", MaxLength = 100 },
            new MockColumn { LogicalName = "parentcustomerid", Kind = MockColumnKind.Lookup, Label = "Company Name", LookupTarget = "account" },
            new MockColumn { LogicalName = "createdon", Kind = MockColumnKind.DateTime, Label = "Created On" }
        },
        Rows = new[]
        {
            Row(
                ("contactid", new Guid("aaaaaaaa-0000-0000-0000-000000000001")),
                ("fullname", "Ada Lovelace"),
                ("emailaddress1", "ada@contoso.test"),
                ("parentcustomerid", new EntityReference("account", ContosoId) { Name = "Contoso" }),
                ("createdon", new DateTime(2024, 4, 1, 8, 0, 0, DateTimeKind.Utc))),
            Row(
                ("contactid", new Guid("aaaaaaaa-0000-0000-0000-000000000002")),
                ("fullname", "Grace Hopper"),
                ("emailaddress1", "grace@fabrikam.test"),
                ("parentcustomerid", new EntityReference("account", FabrikamId) { Name = "Fabrikam" }),
                ("createdon", new DateTime(2024, 8, 12, 10, 20, 0, DateTimeKind.Utc))),
            Row(
                ("contactid", new Guid("aaaaaaaa-0000-0000-0000-000000000003")),
                ("fullname", "Alan Turing"),
                ("emailaddress1", "alan@northwind.test"),
                ("parentcustomerid", new EntityReference("account", NorthwindId) { Name = "Northwind Traders" }),
                ("createdon", new DateTime(2023, 12, 24, 11, 0, 0, DateTimeKind.Utc)))
        }
    };

    private static MockTable BuildIncident() => new()
    {
        LogicalName = "incident",
        Label = "Case",
        ObjectTypeCode = 112,
        Columns = new[]
        {
            new MockColumn { LogicalName = "incidentid", Kind = MockColumnKind.UniqueIdentifier, Label = "Case", IsPrimaryId = true },
            new MockColumn { LogicalName = "title", Kind = MockColumnKind.String, Label = "Title", MaxLength = 200, IsPrimaryName = true },
            new MockColumn { LogicalName = "prioritycode", Kind = MockColumnKind.OptionSet, Label = "Priority" },
            new MockColumn { LogicalName = "statecode", Kind = MockColumnKind.OptionSet, Label = "Status" },
            new MockColumn { LogicalName = "createdon", Kind = MockColumnKind.DateTime, Label = "Created On" }
        },
        Rows = new[]
        {
            Row(
                ("incidentid", new Guid("bbbbbbbb-0000-0000-0000-000000000001")),
                ("title", "Factuur niet ontvangen"),
                ("prioritycode", 1),
                ("statecode", 0),
                ("createdon", new DateTime(2025, 2, 3, 9, 10, 0, DateTimeKind.Utc))),
            Row(
                ("incidentid", new Guid("bbbbbbbb-0000-0000-0000-000000000002")),
                ("title", "Levering te laat"),
                ("prioritycode", 2),
                ("statecode", 0),
                ("createdon", new DateTime(2025, 2, 8, 14, 25, 0, DateTimeKind.Utc))),
            Row(
                ("incidentid", new Guid("bbbbbbbb-0000-0000-0000-000000000003")),
                ("title", "Verkeerd artikel geleverd"),
                ("prioritycode", 3),
                ("statecode", 1),
                ("createdon", new DateTime(2025, 1, 15, 7, 45, 0, DateTimeKind.Utc)))
        }
    };

    private static IReadOnlyDictionary<string, object?> Row(params (string Column, object? Value)[] values) =>
        values.ToDictionary(v => v.Column, v => v.Value, StringComparer.OrdinalIgnoreCase);
}
