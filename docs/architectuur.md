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
   ├─ /api/connections     verbindingen (lijst, standaard, test, secret)
   ├─ /api/environments    sjablonen dev/test/acc/productie
   ├─ /api/query           SQL → FetchXML → rijen
   └─ statische bestanden: de gebouwde frontend (wwwroot)
        │
        ├── MockDataset + MockOrganizationService   (synthetisch, geen verbinding)
        └── ConnectionFactory → ServiceClient (app-only client credentials)
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
  leest het bestand; het pad staat per verbinding in `connections.json`. De repo, de
  UI en de API-antwoorden bevatten nooit de waarde.
- **Verbindingen**: opgeslagen in `~/.config/xrmpocket/connections.json`
  (zie [`verbindingen.md`](verbindingen.md)).

## Poorten

| Poort | Gebruik |
|---|---|
| 4101-4107 | Sauron/openCode-sessies per project (xrmpocket = 4107) — **niet aanraken** |
| **4108** | XrmPocket-API + gebouwde PWA |
| 5173 | Vite dev-server (proxy `/api` → 4108) |

## Verbindingen

Verbindingen werken zoals in XrmToolBox: een **lijst met opgeslagen verbindingen**,
een **standaardverbinding**, per verbinding een test (WhoAmI) en een eigen
kleurmarkering per omgeving. Het volledige model en de vergelijking met XrmToolBox
staan in [`verbindingen.md`](verbindingen.md).

- Bewaard in `~/.config/xrmpocket/connections.json` (chmod 600, buiten de repo).
- Het client secret staat **niet** in dat bestand: enkel het pad naar het
  secret-bestand (chmod 600). Dit is bewust strenger dan XrmToolBox, dat secrets
  versleuteld in het connection-bestand bewaart met een sleutel die in de app zit.
- Bij de eerste start staan **Mock (synthetisch)** (standaard) en **SAV-TEST**
  klaar.

## API

### Verbindingen

| Endpoint | Doel |
|---|---|
| `GET /api/connections` | lijst + `defaultConnectionId`; per verbinding `configured`, `secretPresent`, `note` |
| `POST /api/connections` | aanmaken of wijzigen (naam, soort, omgeving, URL, client-ID, tenant, time-out, highlight) |
| `DELETE /api/connections/{id}` | verwijderen |
| `POST /api/connections/{id}/default` | standaardverbinding kiezen |
| `POST /api/connections/{id}/test` | WhoAmI-test: omgeving, versie, gebruikers-id, of een foutcode met hint |
| `POST /api/connections/{id}/secret` | client secret bewaren (chmod 600); de waarde komt nooit terug |
| `GET /api/environments` | sjablonen (dev/test/acc/productie) voor nieuwe verbindingen |

### `POST /api/query`

```json
{ "sql": "SELECT name FROM account", "connectionId": "…", "maxRows": 200 }
```

`connectionId` leeg = de standaardverbinding. (Voor compatibiliteit werkt ook nog
`environment: "test"`, dat de verbinding met die omgevingsnaam zoekt.)

Antwoord: `columns`, `rows`, `rowCount`, `truncated`, `durationMs`, `fetchXml[]`,
`connectionId`, `connectionName` en `error { code, message, hint }`.

Foutcodes: `empty_sql`, `sql_too_long`, `unknown_connection`,
`connection_not_configured`, `write_not_allowed`, `not_supported`, `sql_error`,
`unexpected`.

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

## Op de iPhone

1. Zorg dat de Mac bereikbaar is (lokaal netwerk of Tailscale): `http://192.168.9.9:4108`.
2. Open die URL in Safari — de app werkt meteen; alle query's lopen via de backend.
3. **Installeren als PWA** (icoon op het beginscherm) vereist **HTTPS**: iOS weigert
   een service worker op een gewone `http://`-LAN-url. Oplossing zodra we zover zijn:
   Tailscale + `tailscale serve`/Caddy met een certificaat vóór poort 4108. Dat is een
   aparte stap, geen wijziging aan de app zelf.

## Teststrategie

- Geen echte klantgegevens als testmateriaal: de mock is volledig synthetisch.
- End-to-end testbaar zonder Dataverse: SQL invoeren via de UI of `curl`.
- Zodra het client secret en de application user er zijn: dezelfde query's tegen
  SAV-TEST, en de FetchXML vergelijken met die van de mock (zelfde SQL, zelfde vorm).

## Wat nog niet bestaat

- Dataverse-route is geïmplementeerd (`ServiceClient` met client credentials) en de
  verbindingstest komt tot bij Microsoft (`AADSTS7000215` bij een nep-secret, dus de
  flow klopt), maar een **geslaagde** verbinding is nog niet mogelijk: de blokkers
  staan in `START.md` §4 (nieuw client secret + application user in SAV-TEST).
- Schrijfacties (INSERT/UPDATE/DELETE) — bewust uit.
- Andere XrmToolBox-tools (fase 4).
