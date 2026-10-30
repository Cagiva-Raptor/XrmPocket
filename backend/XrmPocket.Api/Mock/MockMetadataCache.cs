using System.Reflection;
using MarkMpn.Sql4Cds.Engine;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

namespace XrmPocket.Api.Mock;

/// <summary>
/// Metadata voor de synthetische dataset, zodat de SQL 4 CDS-engine zonder
/// Dataverse-verbinding kan bepalen wat een query betekent.
/// </summary>
public sealed class MockMetadataCache : IAttributeMetadataCache
{
    private readonly Dictionary<string, EntityMetadata> _entities;

    public MockMetadataCache(MockDataset dataset)
    {
        _entities = dataset.Tables.ToDictionary(
            table => table.LogicalName,
            BuildEntity,
            StringComparer.OrdinalIgnoreCase);

        AddRelationships(dataset);
    }

    /// <summary>
    /// Lookup-kolommen hebben relaties nodig, anders kan de engine een JOIN niet
    /// vertalen (kolomtypen blijven dan leeg).
    /// </summary>
    private void AddRelationships(MockDataset dataset)
    {
        foreach (var table in dataset.Tables)
        {
            var referencing = _entities[table.LogicalName];

            foreach (var column in table.Columns.Where(c => c.Kind == MockColumnKind.Lookup))
            {
                var targetName = column.LookupTarget ?? "account";

                if (!_entities.TryGetValue(targetName, out var referenced))
                    continue;

                var idAttribute = referenced.PrimaryIdAttribute ?? targetName + "id";

                var relationship = new OneToManyRelationshipMetadata
                {
                    SchemaName = $"{table.LogicalName}_{targetName}_{column.LogicalName}",
                    ReferencedEntity = targetName,
                    ReferencedAttribute = idAttribute,
                    ReferencingEntity = table.LogicalName,
                    ReferencingAttribute = column.LogicalName
                };

                Append(referencing, nameof(EntityMetadata.ManyToOneRelationships), relationship);
                Append(referenced, nameof(EntityMetadata.OneToManyRelationships), relationship);
            }
        }
    }

    private static void Append<T>(EntityMetadata entity, string property, T item)
    {
        var prop = typeof(EntityMetadata).GetProperty(property, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)!;
        var existing = prop.GetValue(entity) as T[] ?? Array.Empty<T>();
        prop.SetValue(entity, existing.Concat(new[] { item }).ToArray());
    }

    public EntityMetadata this[string name] => _entities[name];

    public EntityMetadata this[int otc] => _entities.Values.Single(e => e.ObjectTypeCode == otc);

    public bool TryGetValue(string logicalName, out EntityMetadata metadata)
        => _entities.TryGetValue(logicalName, out metadata!);

    public bool TryGetMinimalData(string logicalName, out EntityMetadata metadata)
        => _entities.TryGetValue(logicalName, out metadata!);

    public string[] RecycleBinEntities => Array.Empty<string>();

    public IEnumerable<EntityMetadata> GetAllEntities() => _entities.Values;

    public string[] TryGetRecycleBinEntities() => Array.Empty<string>();

    private static EntityMetadata BuildEntity(MockTable table)
    {
        var attributes = table.Columns.Select(column => BuildAttribute(table, column)).ToArray();

        var entity = new EntityMetadata
        {
            LogicalName = table.LogicalName,
            SchemaName = table.Label.Replace(" ", string.Empty),
            DisplayName = new Label(table.Label, 1033)
        };

        Set(entity, nameof(EntityMetadata.ObjectTypeCode), table.ObjectTypeCode);
        Set(entity, nameof(EntityMetadata.Attributes), attributes);

        if (table.PrimaryId != null)
            Set(entity, nameof(EntityMetadata.PrimaryIdAttribute), table.PrimaryId.LogicalName);

        if (table.PrimaryName != null)
            Set(entity, nameof(EntityMetadata.PrimaryNameAttribute), table.PrimaryName.LogicalName);

        return entity;
    }

    private static AttributeMetadata BuildAttribute(MockTable table, MockColumn column)
    {
        AttributeMetadata attribute = column.Kind switch
        {
            MockColumnKind.UniqueIdentifier => new UniqueIdentifierAttributeMetadata { LogicalName = column.LogicalName },
            MockColumnKind.String => new StringAttributeMetadata { LogicalName = column.LogicalName, MaxLength = column.MaxLength },
            MockColumnKind.Integer => new IntegerAttributeMetadata { LogicalName = column.LogicalName, Format = IntegerFormat.None },
            MockColumnKind.Decimal => new DecimalAttributeMetadata { LogicalName = column.LogicalName, Precision = 2 },
            MockColumnKind.Money => new MoneyAttributeMetadata { LogicalName = column.LogicalName, Precision = 2 },
            MockColumnKind.DateTime => new DateTimeAttributeMetadata { LogicalName = column.LogicalName, Format = DateTimeFormat.DateAndTime },
            MockColumnKind.Boolean => new BooleanAttributeMetadata { LogicalName = column.LogicalName },
            MockColumnKind.OptionSet => new PicklistAttributeMetadata { LogicalName = column.LogicalName, OptionSet = new OptionSetMetadata() },
            MockColumnKind.Lookup => new LookupAttributeMetadata
            {
                LogicalName = column.LogicalName,
                Targets = new[] { column.LookupTarget ?? "account" }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(column), column.Kind, "Onbekende kolomsoort")
        };

        attribute.DisplayName = new Label(column.Label ?? column.LogicalName, 1033);
        Set(attribute, nameof(AttributeMetadata.EntityLogicalName), table.LogicalName);
        Set(attribute, nameof(AttributeMetadata.IsValidForRead), true);
        Set(attribute, nameof(AttributeMetadata.IsValidForCreate), true);
        Set(attribute, nameof(AttributeMetadata.IsValidForUpdate), true);
        Set(attribute, nameof(AttributeMetadata.IsLogical), false);
        Set(attribute, nameof(AttributeMetadata.IsCustomAttribute), false);
        attribute.RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None);
        Set(attribute, nameof(AttributeMetadata.ColumnNumber), attribute.ColumnNumber ?? 0);

        if (column.IsPrimaryId)
            Set(attribute, nameof(AttributeMetadata.IsPrimaryId), true);

        if (column.IsPrimaryName)
            Set(attribute, nameof(AttributeMetadata.IsPrimaryName), true);

        return attribute;
    }

    /// <summary>
    /// Een aantal metadata-eigenschappen heeft een interne setter; de engine leest ze
    /// wel. Zelfde truc als de NetCore-tests van Sql4Cds gebruiken.
    /// </summary>
    private static void Set(object target, string property, object? value)
    {
        var prop = target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)!;
        prop.SetValue(target, value);
    }
}
