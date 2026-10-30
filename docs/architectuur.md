# Architectuur — XrmPocket

Laatst bijgewerkt: 2026-10-31.

## Doel in één zin

SQL 4 CDS (en later andere XrmToolBox-tools) bruikbaar maken op de iPhone, met de
engine die lokaal draait en Dataverse via **app-only** aanspreekt.

## Onderdelen

```
iPhone / laptop (browser of PWA)
        │  HTTP, dezelfde origin
        ▼
XrmPocket.Api  (ASP.NET Core, .NET 10, poort 4108)
   ├─ /api/environments   welke omgevingen zijn bruikbaar
   ├─ /api/query          SQL → FetchXML → rijen
   └─ statische bestanden: de gebouwde frontend (wwwroot)
        │
        ├── MockDataset + MockOrganizationService   (synthetisch, geen verbinding)
        └── ServiceClient (app-only client credentials)
                 │
                 ▼
        Dataverse: dev / test / acc  (standaard SAV-TEST)
```

- **Engine**: `MarkMpn.Sql4Cds.Engine` (MIT) als NuGet-library, **inprocess**.
  SQL → FetchXML doet de engine; uitvoeren gebeurt via `IOrganizationService`.
- **Frontend**: React + TypeScript + Vite, PWA-klaar (manifest + service worker).
  Praat **uitsluitend** met de eigen API; nooit rechtstreeks met Dataverse.
- **Secrets**: het client secret staat in
  `~/.config/opencode/secrets/sapphire_bot_test_secret.txt` (chmod 600). De backend
  leest het bestand; het pad staat in `appsettings.json`. De repo, de UI en de
  API-antwoorden bevatten nooit de waarde.

## Poorten

| Poort | Gebruik |
|---|---|
| 4101-4107 | Sauron/openCode-sessies per project (xrmpocket = 4107) — **niet aanraken** |
| **4108** | XrmPocket-API + gebouwde PWA |
| 5173 | Vite dev-server (proxy `/api` → 4108) |

## API

### `GET /api/environments`

```json
{ "environments": [ { "id": "mock", "label": "Mock (synthetisch)", "kind": "mock",
  "url": null, "production": false, "configured": true, "note": "…" } ] }
```

`configured: false` + `note` zegt waarom (bv. "client secret is onbruikbaar (te kort)").

### `POST /api/query`

```json
{ "sql": "SELECT name FROM account", "environment": "mock", "maxRows": 200 }
```

Antwoord: `columns`, `rows`, `rowCount`, `truncated`, `durationMs`, `fetchXml[]` en
`error { code, message, hint }`.

Standaardomgeving is `test` (SAV-TEST); de UI begint bij de eerste bruikbare omgeving
(nu: `mock`). Foutcodes: `empty_sql`, `sql_too_long`, `unknown_environment`,
`environment_not_configured`, `write_not_allowed`, `connect_failed`, `not_supported`,
`sql_error`, `unexpected`.

## Veiligheidsregels in de backend

1. **Alleen leesqueries.** `SqlSafetyCheck` parseert met ScriptDom en weigert alles wat
   geen `SELECT`/`SET`/`DECLARE`/`USE` is. Schrijven komt pas na een nieuwe beslissing.
2. **Rijlimiet** per query (standaard 200, harde grens 5000) met `truncated`-vlag.
3. **Omgevingswhitelist** uit configuratie; productie staat uit
   (`Enabled: false`) en vraagt later een expliciete volledige URL.
4. **Geen secrets in antwoorden of logs**: fouten worden gemapt op vaste codes met
   Nederlandse hints.

## Mock-connector

`MockDataset` bevat drie verzonnen tabellen (`account`, `contact`, `incident`) met
metadata en relaties; `MockOrganizationService` voert de FetchXML uit die de engine
maakt. Ondersteund: attributen, filters (ook genest), JOINs (`link-entity`), sortering,
`TOP`, `GROUP BY` met `COUNT/SUM/AVG/MIN/MAX`. Wat niet kan, geeft een expliciete
foutmelding in plaats van een stil verkeerd resultaat.

## Teststrategie

- Geen echte klantgegevens als testmateriaal: de mock is volledig synthetisch.
- End-to-end testbaar zonder Dataverse: SQL invoeren via de UI of `curl`.
- Zodra het client secret en de application user er zijn: dezelfde query's tegen
  SAV-TEST, en de FetchXML vergelijken met die van de mock (zelfde SQL, zelfde vorm).

## Wat nog niet bestaat

- Dataverse-route is geïmplementeerd (`ServiceClient` met client credentials) maar
  **onGETEST**: de blokkers staan in `START.md` §4 (nieuw client secret + application
  user in SAV-TEST).
- Schrijfacties (INSERT/UPDATE/DELETE) — bewust uit.
- Andere XrmToolBox-tools (fase 4).
