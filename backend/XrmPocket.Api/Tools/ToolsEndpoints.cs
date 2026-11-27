using System.IO.Compression;
using System.Xml.Linq;
using System.Globalization;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
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

        group.MapGet("/entities", (string? connectionId, ConnectionStore store, ConnectionFactory factory)
            => Run(store, factory, connectionId, client =>
            {
                var response = (RetrieveAllEntitiesResponse)client.Execute(new RetrieveAllEntitiesRequest
                {
                    EntityFilters = EntityFilters.Entity,
                    RetrieveAsIfPublished = true
                });
                var entities = response.EntityMetadata
                    .Where(entity => entity.IsValidForAdvancedFind == true)
                    .Select(entity => new
                    {
                        logicalName = entity.LogicalName,
                        displayName = entity.DisplayName?.UserLocalizedLabel?.Label ?? entity.LogicalName,
                        primaryId = entity.PrimaryIdAttribute,
                        primaryName = entity.PrimaryNameAttribute
                    })
                    .OrderBy(entity => entity.displayName)
                    .ToList();
                return Results.Ok(new { entities });
            }));

        group.MapGet("/metadata/{entity}", (string entity, string? connectionId, ConnectionStore store, ConnectionFactory factory)
            => Run(store, factory, connectionId, client =>
            {
                var response = (RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
                {
                    LogicalName = entity,
                    EntityFilters = EntityFilters.Attributes,
                    RetrieveAsIfPublished = true
                });
                var attributes = response.EntityMetadata.Attributes
                    .Where(attribute => attribute.IsValidForAdvancedFind?.Value == true)
                    .Select(attribute => new
                    {
                        logicalName = attribute.LogicalName,
                        displayName = attribute.DisplayName?.UserLocalizedLabel?.Label ?? attribute.LogicalName,
                        type = attribute.AttributeType?.ToString(),
                        targets = attribute is LookupAttributeMetadata lookup ? lookup.Targets : Array.Empty<string>(),
                        options = attribute is EnumAttributeMetadata @enum
                            ? @enum.OptionSet?.Options.Select(option => new
                            {
                                value = option.Value ?? 0,
                                label = option.Label?.UserLocalizedLabel?.Label
                            }).ToList()
                            : null,
                        required = attribute.RequiredLevel?.Value.ToString()
                    })
                    .OrderBy(attribute => attribute.displayName)
                    .ToList();
                return Results.Ok(new { entity, attributes });
            }));

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
                var (entities, moreRecords) = RetrieveFetchXml(client, request.FetchXml, maxRows);

                var columns = entities.SelectMany(entity => entity.Attributes.Keys).Distinct().ToList();
                var rows = entities
                    .Select(entity => columns.Select(column => ToValue(entity, column)).ToList())
                    .ToList();

                return Results.Ok(new
                {
                    columns,
                    rows,
                    truncated = moreRecords || entities.Count >= maxRows && (entities.Count == maxRows)
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
                var metadataResponse = (RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
                {
                    LogicalName = request.Entity,
                    EntityFilters = EntityFilters.Attributes,
                    RetrieveAsIfPublished = true
                });
                var metadata = metadataResponse.EntityMetadata.Attributes.ToDictionary(attribute => attribute.LogicalName, StringComparer.OrdinalIgnoreCase);
                var validationErrors = new List<string>();
                foreach (var (row, index) in request.Rows.Select((row, index) => (row, index)))
                {
                    foreach (var (key, value) in row)
                    {
                        if (string.IsNullOrWhiteSpace(key) || value == null) continue;
                        if (!metadata.TryGetValue(key, out var attribute))
                        {
                            validationErrors.Add($"Rij {index + 1}: onbekende kolom '{key}'.");
                            continue;
                        }
                        try { _ = ConvertImportValue(attribute, value); }
                        catch (Exception ex) { validationErrors.Add($"Rij {index + 1}, {key}: {ex.GetBaseException().Message}"); }
                    }
                }

                if (request.DryRun)
                    return Results.Ok(new
                    {
                        dryRun = true,
                        entity = request.Entity,
                        rows = request.Rows.Count,
                        columns = request.Rows.SelectMany(row => row.Keys).Distinct().ToList(),
                        errors = validationErrors
                    });

                var created = 0;
                var errors = new List<string>(validationErrors);

                foreach (var (row, index) in request.Rows.Select((row, index) => (row, index)))
                {
                    var entity = new Entity(request.Entity);

                    foreach (var (key, value) in row)
                    {
                        if (string.IsNullOrWhiteSpace(key) || value == null)
                            continue;

                        if (!metadata.TryGetValue(key, out var attribute))
                        {
                            errors.Add($"Rij {index + 1}: onbekende kolom '{key}'.");
                            continue;
                        }

                        try
                        {
                            entity[key] = ConvertImportValue(attribute, value);
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Rij {index + 1}, {key}: {ex.GetBaseException().Message}");
                        }
                    }

                    if (errors.Any(error => error.StartsWith($"Rij {index + 1}", StringComparison.Ordinal)))
                        continue;

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

    private static object ConvertImportValue(AttributeMetadata attribute, string value)
    {
        if (attribute is LookupAttributeMetadata lookup)
        {
            var parts = value.Split(':', 2);
            var logicalName = parts.Length == 2 ? parts[0] : lookup.Targets.First();
            var id = parts.Length == 2 ? parts[1] : parts[0];
            return new EntityReference(logicalName, Guid.Parse(id.Trim()));
        }
        if (attribute is PicklistAttributeMetadata picklist)
        {
            var option = picklist.OptionSet.Options.FirstOrDefault(item =>
                string.Equals(item.Label?.UserLocalizedLabel?.Label, value, StringComparison.OrdinalIgnoreCase));
            return new OptionSetValue(option?.Value ?? int.Parse(value, CultureInfo.InvariantCulture));
        }
        if (attribute is BooleanAttributeMetadata boolean)
        {
            var normalized = value.Trim();
            if (bool.TryParse(normalized, out var parsed)) return parsed;
            if (normalized is "1" or "Ja" or "Yes") return true;
            if (normalized is "0" or "Nee" or "No") return false;
            throw new FormatException("Booleaanse waarde verwacht (Ja/Nee, 1/0, true/false).");
        }
        if (attribute is DateTimeAttributeMetadata) return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        if (attribute is MoneyAttributeMetadata money) return new Money(decimal.Parse(value, NumberStyles.Any, CultureInfo.InvariantCulture));
        if (attribute is DecimalAttributeMetadata) return decimal.Parse(value, NumberStyles.Any, CultureInfo.InvariantCulture);
        if (attribute is DoubleAttributeMetadata) return double.Parse(value, NumberStyles.Any, CultureInfo.InvariantCulture);
        if (attribute is IntegerAttributeMetadata) return int.Parse(value, NumberStyles.Any, CultureInfo.InvariantCulture);
        if (attribute is BigIntAttributeMetadata) return long.Parse(value, NumberStyles.Any, CultureInfo.InvariantCulture);
        if (attribute is UniqueIdentifierAttributeMetadata) return Guid.Parse(value);
        return value;
    }

    private static (List<Entity> Entities, bool MoreRecords) RetrieveFetchXml(ServiceClient client, string fetchXml, int maxRows)
    {
        var document = XDocument.Parse(fetchXml, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new ArgumentException("FetchXML heeft geen root-element.");
        var entities = new List<Entity>();
        var page = 1;
        var moreRecords = false;

        var hasTop = root.Attribute("top") is not null;
        while (entities.Count < maxRows)
        {
            if (!hasTop)
            {
                root.SetAttributeValue("page", page);
                root.SetAttributeValue("count", Math.Min(1000, maxRows - entities.Count));
            }
            var result = client.RetrieveMultiple(new FetchExpression(document.ToString(SaveOptions.DisableFormatting)));
            entities.AddRange(result.Entities.Take(maxRows - entities.Count));
            moreRecords = result.MoreRecords;
            if (hasTop || !result.MoreRecords || string.IsNullOrEmpty(result.PagingCookie)) break;

            root.SetAttributeValue("paging-cookie", result.PagingCookie);
            page++;
        }

        return (entities, moreRecords);
    }

    private static object? ToValue(Entity entity, string column)
    {
        if (entity.FormattedValues.TryGetValue(column, out var formatted) && !string.IsNullOrWhiteSpace(formatted))
            return formatted;

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
