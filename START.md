# START — overdracht voor de XrmPocket-sessie

Laatst bijgewerkt: 2026-10-31 (fase 0 klaar en fase 2/3 in opbouw — zie §5). **Lees vóór je iets doet:** `CONTEXT.md` +
`afspraken/decisions.yaml` (zelfde map). Daarna kun je direct aan de slag.

## 1. Het doel (letterlijk, gebruiker 2026-10-30)

> XrmToolBox beschikbaar maken op iOS — **starten met SQL 4 CDS**; als die
> **volledig goed** werkt, gaan we over naar de andere XrmToolBox-tools.

XrmToolBox is GPL-3.0 Windows/WinForms en draait niet op iOS: dit wordt een **eigen
app die zich laat inspireren** door XrmToolBox, geen port. Merknaam: **XrmPocket**
(geen "XrmToolBox for iOS" — zie beslissing `2026-10-30-naam-en-merkkader`).

## 2. Beslist: web-based eerst (gebruiker, 2026-10-30)

**Geen native Xcode-start.** Architectuurrichting:

```
Browser / PWA (je iPhone, laptop)
        │ HTTP (lokaal netwerk / Tailscale)
        ▼
Backend (machine hier) ──── Sql4Cds.Engine (MIT, lokaal proces)
        │ app-only (client credentials)        SQL → FetchXML
        ▼
Dataverse: dev / test / acc   (standaard = SAV-TEST)
```

Waarom web en niet Xcode (de drie argumenten die de keuze maakten):

1. **CA-blokkade**: interactieve Entra-login werkt alleen op Intune-beheerde
   apparaten (bewezen op LXC én Mac Mini, `AADSTS530033`). Web-backend gebruikt
   **app-only** — geen gebruiker, geen MFA, geen device-eis.
2. **Secret-veiligheid**: het client secret leeft **server-side** in
   `~/.config/opencode/secrets/` (chmod 600); nooit in een app-binary, nooit in de
   repo. In een native app zou het uitneembaar zijn.
3. **Engine-hergebruik**: `MarkMpn/Sql4Cds` (MIT) heeft een **losse engine**
   (`MarkMpn.Sql4Cds.Engine` + `.Engine.Tests.NetCore` = cross-platform bewijs).
   Web-backend kan die rechtstreeks inprocess gebruiken; native zou een dure
   herimplementatie of zware MAUI-port vragen.

Native (Xcode/TestFlight, jullie standaardkanaal) blijft **mogelijk als latere
wrapper rond dezelfde backend** — dat is geen herschrijving, enkel een tweede
client. Beslis dat niet nu.

## 3. Wat er al klaarligt (niet opnieuw doen)

> **2026-10-31:** fase 0 is af. Stack = ASP.NET Core + React/TS/Vite
> (`2026-10-30-stack-aspnetcore-react`), app op poort **4108**
> (`2026-10-31-app-poort-4108`). De engine draait inprocess; `backend/` + `web/` +
> `docs/` bestaan en de mock-keten werkt end-to-end. Zie `README.md` om te starten.

- **Repo**: `Cagiva-Raptor/XrmPocket`, remote
  `git@github.com-xrmpocket:Cagiva-Raptor/XrmPocket.git` (host-alias → **enkel** de
  deploy key `~/.ssh/gh-xrmtoolbox-ios-deploy`; `main` bestaat en is gepusht)
- **Sauron**: id `xrmpocket`, poort **4107**, sessie `Sauron - XrmPocket` — geregistreerd en geverifieerd
- **Gecheckte feiten** (2026-10-30): engine los van XTB-UI, MIT-licensed, NetCore-tests aanwezig; toolchains: **Xcode 27.1 / Swift 6.4** ✔, **Node v26** (`/opt/homebrew/bin/node`) ✔, **.NET niet geïnstalleerd**
- **Skills** (laden automatisch): `d365-verbinding` (omgevingen + foutenkaart), `entra-users`, `savaco-sop`
- **Bruikbare scripts** uit de gedeelde repo:
  `afspraken/scripts/dataverse-whoami.py` (app-only verbindingstest) en `entra-users.py`

## 4. Blokkerend vs. niet-blokkerend

### Twee routes naar een echte verbinding (2026-11-01)

Sinds `2026-11-01-interactieve-login-als-tweede-authvorm` zijn er twee manieren:

- **A — interactief (jouw account):** app registration voorzien van een redirect-URI
  (platform *Web*, bv. `http://localhost:4108/api/auth/callback`), de gedelegeerde
  toestemming `Dataverse > user_impersonation` + admin consent, daarna in XrmPocket
  *Verbindingen…* > authenticatie **Interactief** > **Aanmelden met Microsoft**.
  Geen client secret nodig. CA kan alsnog weigeren (`AADSTS530033`) als het apparaat
  niet compliant is; buiten localhost is HTTPS verplicht (zie `docs/verbindingen.md`).
- **B — app-only (client secret):** onderstaande blokkers.

### ⛔ Wacht op de gebruiker (voor de app-only-route)

1. **Nieuw client secret** voor app "Sapphire AI Bot - Test" — de huidige secret-file
   bevat een onbruikbaar 4-byte fragment (`invalid_client`). Aanmaken in de portal →
   meteen verborgen overschrijven in
   `~/.config/opencode/secrets/sapphire_bot_test_secret.txt` (zelfde bestand bedient
   ook `entra-users.py`).
2. **Application user in SAV-TEST** (iemand met D365-beheerdersrechten):
   Instellingen > Beveiliging > Gebruikers > tabblad *Applicatiegebruikers* > Nieuw >
   client-ID `a52e36b0-b5ec-41d4-aae2-49e26747b2c9` > beveiligingsrol geven.
3. Optioneel: `Dataverse > user_impersonation` + admin consent als `AADSTS65001` opduikt.

**Groene check vóór je verder bouwt op echte verbinding:**
`python3 <afspraken>/scripts/dataverse-whoami.py check` → `alles in orde`.

### ✅ Kan nu al, zonder die blokkers

1. **Spike: engine inprocess** — `brew install dotnet` (of officiële dotnet-install),
   `git clone https://github.com/MarkMpn/Sql4Cds`, en bewijs dat
   `MarkMpn.Sql4Cds.Engine` als **library** bouwt en een `SELECT` naar FetchXML
   vertaalt **zonder** XrmToolBox-afhankelijkheden. MIT ⇒ hergebruik mag, maar
   **naamsvermelding is verplicht** (LICENSE-kopie of credits in README).
   Lukt het niet schoon los? Dan pas beraden: Node-variant of engine als subprocess.
2. **Stack-beslissing vastleggen** vóór het eerste stack-bestand: backend-taal
   (ASP.NET Core ligt voor de hand = dezelfde taal als de engine; alternatief Node +
   engine apart) + frontend-aanpak. **Noteer het als nieuwe beslissing in
   `afspraken/decisions.yaml` die `2026-10-30-project-aangemaakt-met-conventies`
   vervangt** — pas dán `dotnet new`, `package.json` e.d. aanmaken.
3. **Mock-connector**: UI bouwen tegen JSON-fixtures (synthetisch, zelfgemaakt) zodat
   editor/grid ook zonder D365 testbaar is.
4. **UI-ontwerp**: SQL-editor + resultaatgrid + omgevingskiezer (dev/test/acc) +
   foutmeldingen die de foutenkaart uit de skill volgen. PWA-klaar (manifest +
   service worker) — dat is jullie "op de iPhone" voor dit project.
5. **Tests**: eigen fixtures rond SQL→FetchXML; nooit echte klantgegevens als
   testmateriaal (afspraken-repo).

## 5. Voorgestelde volgorde (fases)

| Fase | Inhoud | Klaar als… |
|---|---|---|
| **0 — Fundament** ✔ | stack-beslissing + engine-spike + repostructuur | engine vertaalt een SELECT lokaal — **klaar 2026-10-31** (`docs/spike-2026-10-30-sql-naar-fetchxml.md`) |
| **1 — Verbinding** ⛔ | secret + application user + `whoami check` groen | backend raadt SAV-TEST aan — **wacht op de gebruiker** (secret-file is nu 4 bytes) |
| **2 — Kern** 🚧 | SQL → engine → Web API → resultaten (API-laag) | één query levert een grid op via API — **werkt tegen de mock**; echte omgeving wacht op fase 1 |
| **3 — UI** 🚧 | editor, grid, omgevingskiezer, foutkaart, PWA | je draait een echte query vanaf je telefoon — **UI staat** (poort 4108), nog niet op de iPhone getest |
| **4 — Uitbreiding** | andere XrmToolBox-tools (pas ná fase 3) | volgorde ervan kiest de gebruiker |

Pas in fase 4 praten we over meer tools — daarvoor **volledig goed** werkende fase 2-3.

## 6. Regels (kort)

- Geen secrets in repo, argv of logs; paden alleen.
- **Altijd SAV-TEST** (of dev/acc); Productie = alleen de volledige URL
  `https://sapphire.crm4.dynamics.com/` (bewust geen alias), en nooit ongevraagd.
- Stack-bestanden pas ná een beslissing in `decisions.yaml`; **kleine Nederlandse
  commits**.
- Geen "XrmToolBox"-merknaam; GPL-code letterlijk hergebruiken ⇒ eigen project GPL-3.0
  (MIT-code mag, met naamsvermelding).
- Nieuwe afspraak = `decisions.yaml` **én** `CONTEXT.md` bijwerken; dezelfde commit.
- Bestaande projecten/poorten (4101-4107) ongemoeid.

## 7. Open vragen (uit CONTEXT.md, onveranderd)

1. Scope faset 4: welke tools eerst na SQL 4 CDS?
2. Voor wie: jij, collega's, of publiek (App Store / TestFlight)?
3. License van het eigen project (vóór eerste publicatie).
4. Inlog op meerdere omgevingen — altijd binnen de CA-grenzen hierboven.
