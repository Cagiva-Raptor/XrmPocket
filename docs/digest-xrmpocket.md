# XrmPocket — digest (1 pagina, voor kleine context)

> Lees dit als eerste; `HANDOVER.md` heeft de volledige overdracht, `docs/` de details.
> Laatst bijgewerkt: 2026-11-10.

## Wat het is

Web-app die **SQL 4 CDS** (SQL over Dataverse) bruikbaar maakt in de browser en op de
iPhone. Geïnspireerd door XrmToolBox, maar eigen code: de MIT-engine
`MarkMpn.Sql4Cds.Engine` draait **inprocess** in een ASP.NET Core-backend (.NET 10);
de frontend is React + TypeScript + Vite (PWA).

## Status

- **Werkt volledig tegen de mock** (synthetische `account`/`contact`/`incident`):
  SQL → FetchXML → rijen, JOINs, GROUP BY, verbindingen beheren, WhoAmI + rollen.
- **Echte Dataverse-verbinding nog niet groen**: wacht op portal-acties van de
  gebruiker (redirect-URI + gedelegeerde `user_impersonation` voor interactief, of een
  nieuw client secret + application user voor app-only).
- Schrijfacties zijn **bewust uit** (alleen leesqueries).

## Draaien

```bash
cd backend/XrmPocket.Api && dotnet build && dotnet run   # poort 4108
cd web && npm install && npm run dev                     # poort 5173 (proxy → 4108)
cd web && npm run build                                  # → backend/.../wwwroot
```

macOS: `export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"`.
Windows (Savaco-laptop): `winget install Microsoft.DotNet.SDK.10`, dan
`cd backend\XrmPocket.Api; dotnet run` — volledige stappen in `HANDOVER.md` §7.6.

## Verifiëren

```bash
curl -s http://127.0.0.1:4108/api/health
curl -s http://127.0.0.1:4108/api/connections
curl -s -X POST http://127.0.0.1:4108/api/query -H 'content-type: application/json' \
  -d '{"sql":"SELECT name, revenue FROM account ORDER BY revenue DESC"}'
```

## Kaart van de code

| Pad | Rol |
|---|---|
| `backend/XrmPocket.Api/Query/` | SQL → plan/FetchXML → uitvoering → JSON |
| `backend/XrmPocket.Api/Connections/` | verbindingen, secret, WhoAmI, OAuth |
| `backend/XrmPocket.Api/Mock/` | synthetische dataset + FetchXML-evaluator |
| `web/src/App.tsx` | editor, grid, statusbalk |
| `web/src/ConnectionManager.tsx` | verbindingen beheren |
| `docs/` | architectuur, verbindingen, spike, deze digest |
| `afspraken/decisions.yaml` | 8 beslissingen (bron van waarheid) |

## Harde regels

1. **Geen secrets** in repo, commits, logs, API-antwoorden of chat. Alleen paden.
2. **Alleen leesqueries**; schrijven pas na een nieuwe beslissing.
3. **Poort 4108** voor de app; 4101-4107 nooit aanraken (Sauron/openCode).
4. **Kleine Nederlandse commits** + `git push`; nooit ongecommit achterblijven.
5. **Beslissing = `afspraken/decisions.yaml` + `CONTEXT.md` + deze digest** in één commit.
6. **Geen GPL-code** van XrmToolBox overnemen (gedrag bestuderen mag).
7. **Testdata synthetisch**; nooit echte klantgegevens.
8. **Verifieer met commando's**, niet met aannames. Vastgelopen? Vraag de gebruiker.

## Top-valkuilen

- Engine-metadata mist `EntityLogicalName`/`ColumnNumber`/relaties → NRE of JOIN faalt.
- Lookups/aggregaten moeten als `AliasedValue` terugkomen, anders cast-fout.
- Poort 4107 is bezet door de Sauron-sessie van dit project → app op 4108.
- iOS: PWA-installatie en interactief aanmelden vereisen HTTPS (behalve localhost).

## Volgende stap

Portal-actie gebruiker → `POST /api/connections/{id}/test` groen → echte query op
SAV-TEST. Daarna: HTTPS op de iPhone, tests toevoegen, UI-verfijning, dan pas andere
XrmToolBox-tools.
