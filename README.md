# XrmPocket

**SQL 4 CDS voor Dataverse — in de browser en op je iPhone.**

XrmPocket is geïnspireerd door [XrmToolBox](https://www.xrmtoolbox.com/), maar is een
eigen web-app: XrmToolBox zelf is Windows/WinForms en draait niet op iOS. De motor is de
MIT-gelicenseerde [SQL 4 CDS-engine](https://github.com/MarkMpn/Sql4Cds) van Mark
Carrington, die **inprocess** in de backend draait. Naamsvermelding:
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

## Status

- **Fase 0 klaar**: stack gekozen, engine-spike geslaagd
  ([verslag](docs/spike-2026-10-30-sql-naar-fetchxml.md)), repo-structuur staat.
- **Fase 2/3 in opbouw**: SQL → FetchXML → resultaatgrid werkt volledig tegen een
  **synthetische mock-omgeving**. De Dataverse-route (app-only) is geïmplementeerd maar
  nog niet getest: er ontbreekt een geldig client secret en een application user in
  SAV-TEST (zie [`START.md`](START.md) §4).
- Schrijfacties (INSERT/UPDATE/DELETE) zijn bewust uitgeschakeld.

## Snel starten

```bash
# 1. Backend + gebouwde frontend op http://localhost:4108
cd backend/XrmPocket.Api
dotnet run

# 2. Frontend in ontwikkeling (http://localhost:5173, proxy naar 4108)
cd web
npm install
npm run dev

# 3. Frontend bouwen → de backend serveert ze uit wwwroot
cd web
npm run build
```

Vereist: .NET 10 SDK (`brew install dotnet`, `export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"`)
en Node 26.

### Eerste query

```bash
curl -s -X POST http://127.0.0.1:4108/api/query \
  -H 'content-type: application/json' \
  -d '{"sql":"SELECT name, revenue FROM account ORDER BY revenue DESC","environment":"mock"}'
```

Het antwoord bevat de rijen, de duur en **de FetchXML** die de engine van je SQL maakte.

## Structuur

```
backend/XrmPocket.Api/    ASP.NET Core-API + SQL 4 CDS-engine + mock-connector
web/                      React + TypeScript + Vite (PWA)
docs/                     spike-verslagen en architectuur
afspraken/decisions.yaml  projectbeslissingen (met datum, rationale, handhaving)
START.md / CONTEXT.md     overdracht en projectoverzicht
```

## Afspraken die het project bewaken

- Alleen **dev / test / acc** (standaard SAV-TEST); productie staat uit en heeft bewust
  geen alias — enkel de volledige URL.
- Het client secret leeft server-side in `~/.config/opencode/secrets/` (chmod 600);
  nooit in de repo, nooit in de UI, nooit in logs.
- Alleen leesqueries; de backend weigert schrijfacties.
- Geen echte klantgegevens als testmateriaal — de mock-dataset is verzonnen.

Details: [`docs/architectuur.md`](docs/architectuur.md) · beslissingen:
[`afspraken/decisions.yaml`](afspraken/decisions.yaml).
