using System.IO.Compression;
using System.Globalization;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using XrmPocket.Api.Connections;

namespace XrmPocket.Api.Tools;

/// <summary>
/// Gereedschap naast de SQL-editor: FetchXML Builder, Ribbon Workbench (lezen),
/// Data Transporter (importeren) en User Security Manager (rollen). Eigen implementatie
/// met de Dataverse-SDK; geen code uit XrmToolBox (GPL-3.0) overgenomen.
/// </summary>
public static class ToolsEndpoints
{
    public sealed record FetchXmlRequest(string? ConnectionId, string FetchXml, int? MaxRows);

    public sealed record RoleAssignRequest(string? ConnectionId, string RoleId, bool Assign);

    public sealed record ImportRequest(string? ConnectionId, string Entity, List<Dictionary<string, string?>> Rows, bool DryRun);

    public static void MapToolsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/tools");

        group.MapGet("/roles", (string? connectionId, ConnectionStore store, ConnectionFactory factory)
            => Run(store, factory, connectionId, client =>
            {
                var rows = RetrieveAll(client, RoleQuery()).Select(ToRole).ToList();
                return Results.Ok(new { roles = rows });
            }));

        group.MapGet("/users", (string? connectionId, ConnectionStore store, ConnectionFactory factory)
            => Run(store, factory, connectionId, client =>
            {
                var query = new QueryExpression("systemuser")
                {
                    ColumnSet = new ColumnSet("systemuserid", "fullname", "domainname", "isdisabled", "applicationid", "businessunitid")
                };
                query.Orders.Add(new OrderExpression("fullname", OrderType.Ascending));
                var rows = RetrieveAll(client, query)
                    .Select(entity => new
                    {
                        id = entity.Id.ToString(),
                        fullName = entity.GetAttributeValue<string>("fullname"),
                        domainName = entity.GetAttributeValue<string>("domainname"),
                        isDisabled = entity.GetAttributeValue<bool>("isdisabled"),
                        applicationId = entity.GetAttributeValue<Guid?>("applicationid")?.ToString(),
                        businessUnitId = entity.GetAttributeValue<EntityReference>("businessunitid")?.Id.ToString()
                    })
                    .ToList();
                return Results.Ok(new { users = rows });
            }));

        group.MapGet("/users/{userId}/roles", (string userId, string? connectionId, ConnectionStore store, ConnectionFactory factory)
            => Run(store, factory, connectionId, client =>
            {
                var query = RoleQuery();
                var link = query.AddLink("systemuserroles", "roleid", "roleid", JoinOperator.Inner);
                link.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, Guid.Parse(userId));
                var rows = RetrieveAll(client, query).Select(ToRole).OrderBy(role => role.Name).ThenBy(role => role.BusinessUnit).ToList();
                return Results.Ok(new { roles = rows });
            }));

        group.MapPost("/users/{userId}/roles", (string userId, RoleAssignRequest request, ConnectionStore store, ConnectionFactory factory)
            => Run(store, factory, request.ConnectionId, client =>
            {
                var collection = new EntityReferenceCollection { new EntityReference("role", Guid.Parse(request.RoleId)) };
                var relationship = new Relationship("systemuserroles_association");

                if (request.Assign)
                    client.Associate("systemuser", Guid.Parse(userId), relationship, collection);
                else
                    client.Disassociate("systemuser", Guid.Parse(userId), relationship, collection);

                return Results.Ok(new { ok = true, assigned = request.Assign });
            }));

        group.MapPost("/fetchxml", (FetchXmlRequest request, ConnectionStore store, ConnectionFactory factory)
            => Run(store, factory, request.ConnectionId, client =>
            {
                var maxRows = Math.Clamp(request.MaxRows ?? 200, 1, 5000);
                var result = client.RetrieveMultiple(new FetchExpression(request.FetchXml));
                var entities = result.Entities.Take(maxRows).ToList();

                var columns = entities.SelectMany(entity => entity.Attributes.Keys).Distinct().ToList();
                var rows = entities
                    .Select(entity => columns.Select(column => ToValue(entity, column)).ToList())
                    .ToList();

                return Results.Ok(new
                {
                    columns,
                    rows,
                    truncated = result.MoreRecords || result.Entities.Count > maxRows
                });
            }));

        group.MapGet("/ribbon", (string entity, string? connectionId, ConnectionStore store, ConnectionFactory factory)
            => Run(store, factory, connectionId, client =>
            {
                var request = new RetrieveEntityRibbonRequest { EntityName = entity };
                var response = (RetrieveEntityRibbonResponse)client.Execute(request);

                var xml = Decompress(response.CompressedEntityXml);
                return Results.Ok(new { entity, xml });
            }));

        group.MapPost("/import", (ImportRequest request, ConnectionStore store, ConnectionFactory factory)
            => Run(store, factory, request.ConnectionId, client =>
            {
                if (request.DryRun)
                {
                    return Results.Ok(new
                    {
                        dryRun = true,
                        entity = request.Entity,
                        rows = request.Rows.Count,
                        columns = request.Rows.SelectMany(row => row.Keys).Distinct().ToList()
                    });
                }

                var created = 0;
                var errors = new List<string>();

                foreach (var (row, index) in request.Rows.Select((row, index) => (row, index)))
                {
                    var entity = new Entity(request.Entity);

                    foreach (var (key, value) in row)
                    {
                        if (string.IsNullOrWhiteSpace(key) || value == null)
                            continue;

                        entity[key] = value;
                    }

                    try
                    {
                        client.Create(entity);
                        created++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Rij {index + 1}: {ex.GetBaseException().Message}");
                    }
                }

                return Results.Ok(new { dryRun = false, entity = request.Entity, created, errors });
            }));
    }

    /// <summary>Rollen met de naam van hun business unit (via een left join op businessunit).</summary>
    private static QueryExpression RoleQuery()
    {
        var query = new QueryExpression("role") { ColumnSet = new ColumnSet("roleid", "name", "businessunitid") };
        query.Orders.Add(new OrderExpression("name", OrderType.Ascending));

        var businessUnit = query.AddLink("businessunit", "businessunitid", "businessunitid", JoinOperator.LeftOuter);
        businessUnit.EntityAlias = "bu";
        businessUnit.Columns = new ColumnSet("name");

        return query;
    }

    private static RoleInfo ToRole(Entity entity)
    {
        var businessUnitName = entity.GetAttributeValue<AliasedValue>("bu.name")?.Value as string;
        var businessUnitId = entity.GetAttributeValue<EntityReference>("businessunitid")?.Id.ToString();

        return new RoleInfo(entity.Id.ToString(), entity.GetAttributeValue<string>("name"), businessUnitId, businessUnitName);
    }

    private sealed record RoleInfo(string Id, string? Name, string? BusinessUnitId, string? BusinessUnit);

    private static List<Entity> RetrieveAll(ServiceClient client, QueryExpression query)
    {
        var all = new List<Entity>();
        query.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };

        while (true)
        {
            var page = client.RetrieveMultiple(query);
            all.AddRange(page.Entities);

            if (!page.MoreRecords)
                break;

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }

        return all;
    }

    private static IResult Run(ConnectionStore store, ConnectionFactory factory, string? connectionId, Func<ServiceClient, IResult> action)
    {
        var profile = string.IsNullOrWhiteSpace(connectionId) ? store.GetDefault() : store.Get(connectionId.Trim());

        if (profile == null)
            return Results.NotFound(new { error = "Verbinding niet gevonden." });

        if (profile.IsMock)
            return Results.BadRequest(new { error = "Dit gereedschap werkt alleen op een Dataverse-verbinding, niet op de demo." });

        try
        {
            var client = factory.GetClient(profile);
            return action(client);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.GetBaseException().Message });
        }
    }

    private static object? ToValue(Entity entity, string column)
    {
        if (!entity.Attributes.TryGetValue(column, out var value) || value == null)
            return null;

        return value switch
        {
            AliasedValue aliased => ToValue(aliased.Value),
            EntityReference reference => reference.Name ?? reference.Id.ToString(),
            OptionSetValue option => option.Value,
            Money money => money.Value,
            DateTime dateTime => dateTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            Guid guid => guid.ToString(),
            _ => value
        };
    }

    private static object? ToValue(object? value) => value switch
    {
        null => null,
        EntityReference reference => reference.Name ?? reference.Id.ToString(),
        OptionSetValue option => option.Value,
        Money money => money.Value,
        DateTime dateTime => dateTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
        Guid guid => guid.ToString(),
        _ => value
    };

    private static string Decompress(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return "";

        try
        {
            using var input = new MemoryStream(bytes);
            using var archive = new ZipArchive(input, ZipArchiveMode.Read);
            var entry = archive.Entries.FirstOrDefault();

            if (entry == null)
                return "";

            using var reader = new StreamReader(entry.Open(), System.Text.Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (InvalidDataException)
        {
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }
}
