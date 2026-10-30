using MarkMpn.Sql4Cds.Engine;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace XrmPocket.Api.Mock;

/// <summary>
/// Een <see cref="IOrganizationService"/> die niets met Dataverse te maken heeft:
/// de engine praat ermee alsof het een org is, wij antwoorden uit de synthetische
/// dataset. Schrijfacties worden geweigerd — de mock is read-only.
/// </summary>
public sealed class MockOrganizationService : IOrganizationService
{
    private readonly MockDataset _dataset;

    public MockOrganizationService(MockDataset dataset)
    {
        _dataset = dataset;
    }

    public EntityCollection RetrieveMultiple(QueryBase query)
    {
        if (query is FetchExpression fetch)
            return FetchXmlEvaluator.Evaluate(_dataset, fetch.Query);

        if (query is QueryExpression expression && string.Equals(expression.EntityName, "organization", StringComparison.OrdinalIgnoreCase))
            return OrganizationRecord();

        throw new NotSupportedException("mock: alleen FetchXML- en organization-query's worden ondersteund.");
    }

    public OrganizationResponse Execute(OrganizationRequest request)
    {
        switch (request)
        {
            case RetrieveMultipleRequest retrieveMultiple:
                var response = new RetrieveMultipleResponse();
                response.Results["EntityCollection"] = RetrieveMultiple(retrieveMultiple.Query);
                return response;

            case RetrieveVersionRequest:
                var version = new RetrieveVersionResponse();
                version.Results["Version"] = "9.2.24021.20";
                return version;

            default:
                throw new NotSupportedException($"mock: bericht '{request.RequestName}' wordt niet ondersteund (read-only).");
        }
    }

    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        => throw new NotSupportedException("mock: losse Retrieve wordt niet ondersteund.");

    public Guid Create(Entity entity)
        => throw new NotSupportedException("mock: schrijven is uitgeschakeld.");

    public void Update(Entity entity)
        => throw new NotSupportedException("mock: schrijven is uitgeschakeld.");

    public void Delete(string entityName, Guid id)
        => throw new NotSupportedException("mock: schrijven is uitgeschakeld.");

    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => throw new NotSupportedException("mock: schrijven is uitgeschakeld.");

    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => throw new NotSupportedException("mock: schrijven is uitgeschakeld.");

    private static EntityCollection OrganizationRecord()
    {
        var collection = new EntityCollection();
        var org = new Entity("organization") { Id = Guid.NewGuid() };
        org["organizationid"] = org.Id;
        // 1033 = Engels; bepaalt de standaard-collatie die de engine gebruikt.
        org["localeid"] = 1033;
        collection.Entities.Add(org);
        return collection;
    }

    /// <summary>Bouwt een volledige data source (metadata + verbinding) voor de mock.</summary>
    public static DataSource CreateDataSource(MockDataset dataset) => new()
    {
        Name = MockDataset.DataSourceName,
        Connection = new MockOrganizationService(dataset),
        Metadata = new MockMetadataCache(dataset),
        TableSizeCache = new EmptyTableSizeCache(),
        MessageCache = new EmptyMessageCache()
    };

    private sealed class EmptyTableSizeCache : MarkMpn.Sql4Cds.Engine.ExecutionPlan.ITableSizeCache
    {
        public int this[string logicalName] => 0;
    }

    private sealed class EmptyMessageCache : IMessageCache
    {
        public bool TryGetValue(string name, out Message message)
        {
            message = null!;
            return false;
        }

        public IEnumerable<Message> GetAllMessages(bool lazy) => Array.Empty<Message>();

        public bool IsMessageAvailable(string entityLogicalName, string messageName) => false;
    }
}
