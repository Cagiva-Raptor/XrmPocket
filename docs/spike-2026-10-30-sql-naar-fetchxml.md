# Spike — SQL naar FetchXML zonder XrmToolBox

**Datum:** 2026-10-30 · **Status:** geslaagd · **Fase:** 0 (fundament)

## Vraag

Kan de SQL 4 CDS-engine (`MarkMpn.Sql4Cds.Engine`, MIT) als **losse library** gebruikt
worden — dus zonder XrmToolBox-UI en zonder WinForms — en levert die uit gewone SQL
de juiste FetchXML plus uitvoerbare resultaten?

## Antwoord

Ja. Bewezen met een console-app (.NET 10) die de NuGet-package gebruikt, met
**synthetische metadata** en een **zelfgemaakte `IOrganizationService`** als
mock-connector. Geen Dataverse-verbinding, geen klantgegevens.

```xml
<!-- Uitvoer van: SELECT name, revenue FROM account WHERE revenue > 1000 ORDER BY name -->
<fetch xmlns:generator="MarkMpn.SQL4CDS">
  <entity name="account">
    <attribute name="name" />
    <attribute name="revenue" />
    <filter>
      <condition attribute="revenue" operator="gt" value="1000" />
    </filter>
    <order attribute="name" />
  </entity>
</fetch>
```

## Wat de spike aantoonde

| Onderdeel | Bewijs |
|---|---|
| Engine als library | NuGet `MarkMpn.Sql4Cds.Engine` 10.5.1 in een `net10.0`-console, geen XrmToolBox-verwijzing |
| SQL → FetchXML | `Sql4CdsCommand.GeneratePlan(false)` → plan-boom → `IFetchXmlExecutionPlanNode.FetchXmlString` |
| Uitvoering | `command.ExecuteReader()` werkt; de engine vraagt FetchXML op via `IOrganizationService` |
| Metadata | Eigen `IAttributeMetadataCache` met synthetische `EntityMetadata` is genoeg om plannen te bouwen |
| Verbinding | `IOrganizationService`-stub volstaat; enkel `organization.localeid` wordt opgevraagd (collatie) |

## Lessen die het ontwerp bepalen

1. **Metadata bepaalt alles.** Zonder `AttributeMetadata.EntityLogicalName`+/`IsPrimaryId`,
   `PrimaryIdAttribute`, `PrimaryNameAttribute`, `ColumnNumber` en **relaties**
   (`ManyToOneRelationships`/`OneToManyRelationships`) faalt de engine met
   `NullReferenceException` of kan een JOIN niet vertaald worden.
2. **Lookup-kolommen komen terug als `AliasedValue`** (net als aggregate-resultaten);
   de engine leest ze op aliasesnaam en gebruikt `EntityLogicalName` voor de
   metadata-opkuis. Een mock moet dat dus mimicen, anders krijg je
   `Unable to cast object of type 'System.String' to 'Microsoft.Xrm.Sdk.AliasedValue'`.
3. **Plan eerst, dan uitvoeren.** `GeneratePlan(false)` levert de FetchXML op vóór de
   query loopt: de UI kan de vertaling tonen, ook bij een fout of een leeg resultaat.
4. **Collatie komt van het org-record** (`organization.localeid` → 1033 = US-English).
   Een data source zonder `Connection` klapt daarop (`DataSource.LoadDefaultCollation`).

## Reproduceren (huidige repo)

```bash
# backend starten (mock-omgeving, poort 4108)
cd backend/XrmPocket.Api && dotnet run

# SQL → FetchXML + resultaat
curl -s -X POST http://127.0.0.1:4108/api/query \
  -H 'content-type: application/json' \
  -d '{"sql":"SELECT name, revenue FROM account ORDER BY revenue DESC","environment":"mock"}'
```

De mock-dataset (`account`, `contact`, `incident`) is volledig verzonnen en staat in
`backend/XrmPocket.Api/Mock/MockDataset.cs`.

## Licentie

`MarkMpn.Sql4Cds.Engine` is MIT (© Mark Carrington). Naamsvermelding staat in
`THIRD-PARTY-NOTICES.md`; het eigen project mag daardoor vrij gekozen worden
(GPL-3.0 alleen als er ooit GPL-code van XrmToolBox zelf in komt).
