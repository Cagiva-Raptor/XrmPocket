# xrmpocket — projectoverzicht (om mee te nemen)

Laatst bijgewerkt: 2026-10-31. Lees dit bij de start van elke xrmpocket-sessie.

## Omgeving

- **Mac Mini M4** (lokaal, 192.168.9.9), OpenCode v2
- Pad: `/Users/jurgendictus/opencode/xrmpocket`
- GitHub-repo: **`Cagiva-Raptor/XrmPocket`** (remote via host-alias
  `github.com-xrmpocket`, enkel de deploy key)
- Gedeelde afspraken-repo: `/Users/jurgendictus/opencode/afspraken/` (git → LXC),
  geladen via `instructions` (`AGENTS.md` + `digest.md`)

## Doel

Een nieuw GitHub-project waarin **XrmToolBox beschikbaar gemaakt wordt op iOS**
(woordelijke opdracht van de gebruiker, 2026-10-30).

Bij context (gecheckt die dag, geen veronderstellingen):
- XrmToolBox (`MscrmTools/XrmToolBox`) is **GPL-3.0**, Windows/.NET WinForms — het
  draait niet native op iOS; het gaat dus om een app die zich **laat inspireren**
  door XrmToolBox, niet om een port van dezelfde code.
- De meest gebruikte tool binnen XrmToolBox is **SQL 4 CDS**
  (`MarkMpn/Sql4Cds`, **MIT**) — SQL-syntax over Dataverse.
- **Merknaam: XrmPocket** (gebruikerskeuze 2026-10-30, vrij in App Store en op
  GitHub). De naam "XrmToolBox" hoort bij Tanguy Touré/MscrmTools en wordt bewust
  **niet** als app-naam gebruikt (tenzij er ooit samenwerking komt).
- Wie GPL-3.0-code letterlijk hergebruikt, maakt de eigen app GPL-3.0 (copyleft);
  MIT-code (zoals SQL 4 CDS) mag vrij hergebruikt worden met naamsvermelding.

## Stand van zaken (2026-10-31)

**Wat nu bestaat**

- Map + `git` + `.gitignore`; commits gepusht naar `Cagiva-Raptor/XrmPocket` (`main`)
- **Richting beslist: web-based eerst** (zie `afspraken/decisions.yaml`
  `2026-10-30-web-based-eerst-geen-native-start`) — overdrachtsdocument voor de
  bouwsessie: **`START.md`** (lezen bij de start van elke sessie, samen met dit bestand)
- **Stack beslist** (`2026-10-30-stack-aspnetcore-react`): ASP.NET Core (.NET 10, C#) met
  de SQL 4 CDS-engine inprocess + React/TypeScript/Vite, PWA-klaar. Vervangt de oude
  "nog geen stack"-beslissing.
- **Engine werkt** (spike 2026-10-30, verslag in
  `docs/spike-2026-10-30-sql-naar-fetchxml.md`): SQL → FetchXML én uitvoering, zonder
  XrmToolBox en zonder Dataverse-verbinding.
- **Draaiende app** (poort **4108**, zie `2026-10-31-app-poort-4108`):
  - `backend/XrmPocket.Api` — `GET /api/environments`, `POST /api/query`,
    serveert de gebouwde frontend; alleen leesqueries, rijlimiet, omgevingswhitelist
  - `web/` — SQL-editor, omgevingskiezer, resultaatgrid, FetchXML-paneel, PWA-manifest
  - **mock-connector**: synthetische `account`/`contact`/`incident` met metadata,
    relaties, filters, JOINs, sortering, TOP en GROUP BY
  - Dataverse-route via `ServiceClient` (app-only) is **geïmplementeerd maar ongetest**
- **Toolchain**: .NET 10.0.403 (`brew install dotnet`,
  `export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"`), Node 26, npm 11
- **SSH deploy key** klaar: `~/.ssh/gh-xrmtoolbox-ios-deploy` (ed25519, geen
  passphrase, `chmod 600`) + host-alias `github.com-xrmpocket` in `~/.ssh/config`
  (deze key raakt enkel dit repo; de persoonlijke sleutel blijft buiten beeld).
  **Getest:** sleutelpaar ✔, authenticatie ✔ (`Hi Cagiva-Raptor/XrmPocket!`),
  leestoegang én push ✔. Remote-URL:
  `git@github.com-xrmpocket:Cagiva-Raptor/XrmPocket.git`
- Sauron-geregistreerd: id `xrmpocket`, poort **4107** (sessie), sessie
  `Sauron - XrmPocket`

**Wat nog open is**

- **Blokkers op de gebruiker**: nieuw client secret + application user in SAV-TEST
  (zie `START.md` §4). Zonder die twee blijft het bij de mock-omgeving.
- **Scope na SQL 4 CDS**: welke andere tools, en voor wie
- **Licentie van het eigen project**: kiezen vóór de eerste publicatie (GPL-3.0 als je
  GPL-code meeneemt, anders vrij te kiezen; MIT-code mag met naamsvermelding)
- App Store-deelname vraagt een Apple Developer-account (TestFlight is jullie
  standaardkanaal) — relevant pas als er ooit een native wrapper komt

## Sauron-koppeling

Toegevoegd aan `bridge/projects.json` (live `~/Projects/Sauron` + git-repo
`~/opencode/Sauron`):

```json
{
  "id": "xrmpocket",
  "name": "XrmPocket",
  "path": "/Users/jurgendictus/opencode/xrmpocket",
  "port": 4107
}
```

Afspraken:

- **Volgende vrije poort** — nooit hergebruiken; bestaande poorten (4101-4106) nooit wijzigen
- Sessietitel **`Sauron - XrmPocket`**; **nooit** twee sessies met dezelfde titel
- De brug **herstarten** na een wijziging aan `projects.json`
  (`launchctl kickstart -k gui/$(id -u)/be.dictus.sauron.bridge`)
- Bij een **padwijziging** de dubbele sessietitels nakijken
- De app toont enkel wat in `projects.json` staat: een bestaande map zonder
  registratie is **niet zichtbaar**

## Werkwijze

1. Elke sessie begint met **`START.md`** + **`CONTEXT.md`** + **`afspraken/decisions.yaml`**
2. Nieuwe afspraak = toevoegen aan `decisions.yaml` **én** dit overzicht bijwerken
3. Geen secrets (wachtwoorden, tokens, sleutels) in deze bestanden of in
   commando's/logs — de deploy key blijft in `~/.ssh/`, het client secret in
   `~/.config/opencode/secrets/`
4. Kleine, Nederlandse commits
5. Deel-rijke wijzigingen aan de gedeelde afspraken-repo: commit **én push**, andere
   machines pullen

## Open vragen

1. **Concrete stack** (backend-taal + frontend): keuze in fase 0 van `START.md`, daarna
   als beslissing vastleggen
2. **Welke functionaliteit eerst?** — zie `START.md`: SQL 4 CDS volledig goed
   **vóór** de andere tools (doel van de gebruiker)
3. **Voor wie** is de app: jijzelf, Savaco-collega's, of publiek (App Store/TestFlight)?
4. Welke **license** krijgt het eigen project — en komt er GPL-code van XrmToolBox in?
5. Is er behoefte aan **inlog op meerdere omgevingen** (dev/test/acc), en zo ja via
   welke authenticatie — rekening houdend met de CA-eis dat alleen
   Intune-beheerde apparaten interactief mogen aanmelden? (Web-based = app-only,
   dus dit speelt hoogstens bij een latere native wrapper.)
