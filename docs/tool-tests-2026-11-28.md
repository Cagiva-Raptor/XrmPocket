# Tooltests 2026-11-28 (SAV-ACC)

De vier gereedschappen zijn los van XrmToolBox nagebouwd met Dataverse SDK/Web API-gedrag. Er is geen GPL-code overgenomen.

## SQL 4 CDS

- Hoofdservice: poort 4108, engine 10.5.1.
- Vergelijkingsservice: poort 4116, engine 10.6.0.
- 10.6.0-regressie gereproduceerd: `StaticCursorNode.FromQuery` gebruikt `EntityReader.ValidateInsertColumnMapping` voor een tijdelijke cursortabel met ontbrekende metadata en crasht bij scripts met DML/cursors.
- 10.5.1 voert hetzelfde volledige script uit voor CUS-032184 op SAV-ACC (geen technische fout).

## FetchXML Builder

- 3515 entities geladen via metadata.
- 347 account-attributen geladen.
- FetchXML met `top`: 3 resultaten, formatted option label `Active`.
- FetchXML zonder `top`: 3 rijen en `truncated=true`, paging-code werkt.
- Builder gebruikt metadata om entity en attributes te selecteren.

## Ribbon Workbench

- Account-ribbon opgehaald: 439 tekens.
- Bewust alleen-lezen. XrmToolBox Ribbon Workbench is een volledige webapp met solution-import/publish-gedrag; die write-flow is niet blind nagebouwd.

## Data Transporter

- Metadata-gebaseerde validatie voor getallen, decimals, booleans, datums, GUIDs, lookups en picklists.
- Lookupformaat: `logicalname:GUID`.
- Dry-run met een geldig en ongeldig record vindt de verkeerde integer exact op `Rij 2, numberofemployees`.
- Er is nog geen gelijktijdige create/update/upsert-flow; alleen create.
- Een real write-test op `account` werd geblokkeerd door de bestaande Dataverse-pluginregel `Company Id needs to be filled in`; er werd geen record aangemaakt.

## User Security Manager

- 2302 users en 3240 rollen gelezen op SAV-ACC.
- Rollen per user en per business unit zijn beschikbaar.
- Geen roltoewijzing gewijzigd tijdens de test.

## Bewuste beperkingen

- Ribbon publiceren/importeren: niet geïmplementeerd.
- Ribbon Workbench UI zelf: niet overgenomen.
- Data Transporter update/upsert/bulk: niet geïmplementeerd.
- Teams, privileged roles en privileges in User Security: niet geïmplementeerd.
