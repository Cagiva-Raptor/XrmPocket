# HANDOVER — XrmPocket, overdracht naar een andere OpenCode-instance

**Datum:** 2026-11-10 · **Bedoeld voor:** een OpenCode-instance (eventueel met een
**lokaal model**) die dit project overneemt.

> **Lees dit bestand eerst, in deze volgorde:**
> 1. dit `HANDOVER.md` (status, regels, wat te doen)
> 2. [`docs/digest-xrmpocket.md`](docs/digest-xrmpocket.md) (1 pagina, voor kleine context)
> 3. [`docs/architectuur.md`](docs/architectuur.md) + [`docs/verbindingen.md`](docs/verbindingen.md)
> 4. `afspraken/decisions.yaml` (alle beslissingen met rationale)
>
> De gespreksgeschiedenis zelf wordt **niet** overgedragen; deze bestanden zijn de
> overdracht. Ze staan in de repo en zijn gepusht, dus `git clone`/`git pull` volstaat.

---

## 1. Wat XrmPocket is

**SQL 4 CDS voor Dataverse — in de browser en op de iPhone.** Geïnspireerd door
XrmToolBox (dat is Windows/WinForms en draait niet op iOS), maar een eigen web-app.
De motor is de MIT-gelicenseerde **SQL 4 CDS-engine** van Mark Carrington
(`MarkMpn.Sql4Cds.Engine`), die **inprocess** in de backend draait.

Repo: `git@github.com-xrmpocket:Cagiva-Raptor/XrmPocket.git` (branch `main`)

## 2. Status in één blok

| Fase | Status |
|---|---|
| 0 — Fundament (stack, engine-spike, structuur) | ✅ klaar (2026-10-31) |
| 1 — Verbinding (echte Dataverse-verbinding) | ⛔ **wacht op portal-acties gebruiker** (zie §5) |
| 2 — Kern (SQL → engine → API → grid) | 🚧 werkt volledig tegen de **mock**; echte omgeving wacht op fase 1 |
| 3 — UI (editor, grid, verbindingen, PWA) | 🚧 staat en werkt lokaal; nog niet op de iPhone getest |
| 4 — Andere XrmToolBox-tools | ⏳ pas na fase 2-3; volgorde kiest de gebruiker |

**Bewezen met commando's** (zie §7): SQL → FetchXML, uitvoering, JOINs, GROUP BY,
verbindingen beheren, WhoAmI + rollen, secret bewaren, interactieve aanmeld-URL bouwen.

**Nog niet bewezen**: een *geslaagde* Dataverse-verbinding (secret ontbreekt / portal
nog niet ingericht) en de app op een echte iPhone.

## 3. Structuur van de repo

```
backend/XrmPocket.Api/           ASP.NET Core (.NET 10) — API + engine
  Program.cs                     endpoints, DI, statische bestanden
  Query/QueryService.cs          SQL → plan/FetchXML → uitvoering → JSON
  Query/SqlSafetyCheck.cs        alleen leesqueries (ScriptDom)
  Query/QueryModels.cs           request/response-contracten
  Connections/ConnectionProfile.cs   model (SavedConnection, ConnectionTestResult)
  Connections/ConnectionStore.cs     verbindingen in ~/.config/xrmpocket/connections.json
  Connections/ConnectionFactory.cs   client/DataSource per verbinding + tests
  Connections/InteractiveAuthService.cs  OAuth+PKCE, tokens, refresh
  Environments/EnvironmentTemplateCatalog.cs  sjablonen dev/test/acc/productie
  Mock/                          synthetische dataset + FetchXML-evaluator + metadata
web/                             React 19 + TypeScript + Vite (PWA)
  src/App.tsx                    editor, resultaatgrid, statusbalk
  src/ConnectionManager.tsx      verbindingen beheren (zoals XrmToolBox)
  src/ConnectionTestSummary.tsx  WhoAmI + rollen weergave
  src/api.ts                     API-types en aanroepen
  public/manifest.webmanifest, public/sw.js   PWA
docs/                            spike, architectuur, verbindingen, digest
afspraken/decisions.yaml         8 beslissingen (1 superseded, 2 amended)
CONTEXT.md / START.md            projectoverzicht en oorspronkelijke overdracht
THIRD-PARTY-NOTICES.md           MIT-vermelding SQL 4 CDS
```

## 4. Architectuur in het kort

```
browser/PWA  ──HTTP──▶  XrmPocket.Api (poort 4108)
                          ├─ /api/connections (lijst, standaard, test, secret, signin)
                          ├─ /api/environments (sjablonen)
                          ├─ /api/query (SQL → FetchXML → rijen)
                          └─ wwwroot (gebouwde frontend)
                                ├─ mock: synthetische dataset
                                └─ Dataverse: ServiceClient (app-only óf interactief)
```

- **Poorten:** app **4108**; Vite-devserver 5173 (proxy `/api` → 4108).
  Poorten 4101-4107 zijn van Sauron/openCode-sessies — **niet aanraken**.
- **Secrets** (nooit in repo/UI/logs):
  - client secret: `~/.config/opencode/secrets/sapphire_bot_test_secret.txt` (chmod 600)
  - verbindingen: `~/.config/xrmpocket/connections.json` (chmod 600, **zonder** secret)
  - interactieve tokens: `~/.config/xrmpocket/tokens/<id>.json` (chmod 600)
- **Dataverse-omgevingen:** dev/test/acc via `operations-sav-{dev,test,acc}.crm4.dynamics.com`;
  productie **alleen** via `https://sapphire.crm4.dynamics.com/` (geen alias) en nooit
  ongevraagd. Standaard = SAV-TEST.

## 5. Wat de gebruiker nog moet doen (blokkers)

Er zijn twee routes; de app ondersteunt beide.

**A — interactief (eigen account, zoals XrmToolBox)**
1. Entra ID → App registrations → *Sapphire AI Bot - Test* → **Authentication** →
   platform **Web** → redirect-URI's toevoegen:
   `http://localhost:4108/api/auth/callback` en het adres dat je echt gebruikt
   (LAN/Tailscale, **https** buiten localhost).
2. **API permissions** → Dynamics CRM (Dataverse) → *Delegated* →
   `user_impersonation` → **admin consent**.
3. In de app: *Verbindingen…* → `SAV-TEST (mijn account)` → **Aanmelden met Microsoft**.

**B — app-only (client secret)**
1. Nieuw client secret in de portal (huidige bestand is een onbruikbaar fragment).
2. Application user in SAV-TEST voor client-ID
   `a52e36b0-b5ec-41d4-aae2-49e26747b2c9` + beveiligingsrol.
3. Secret instellen: in de app (*Verbindingen…* → SAV-TEST → secretveld) of met
   `afspraken/scripts/dataverse-whoami.py check` (andere repo).

⚠️ **CA-risico:** `AADSTS530033` betekent dat het apparaat niet compliant is. Niet
omzeilen; melden aan de gebruiker. Interactief aanmelden vanaf een beheerd apparaat of
app-only blijven gebruiken.

## 6. Werkafspraken (gelden ook op de nieuwe instance)

1. **Lees eerst, codeer daarna.** `HANDOVER.md` → `docs/digest-xrmpocket.md` → de
   relevante doc. Lees niet de hele repo in je context; open alleen wat je nodig hebt.
2. **Secrets nooit** in de repo, in een commit, in een API-antwoord, in een log of in
   een chatbericht. Alleen paden noemen. Nieuwe machine = secret daar opnieuw instellen.
3. **Alleen leesqueries.** Schrijfacties (INSERT/UPDATE/DELETE) blijven uit tot een
   expliciete nieuwe beslissing.
4. **Kleine Nederlandse commits** (`backend: …`, `web: …`, `docs: …`, `afspraken: …`),
   met `git push` naar `main`. Werk nooit ongecommit achter.
5. **Beslissing nodig?** Voeg een notitie toe aan `afspraken/decisions.yaml`
   (id met datum, `decision` met "Handhaving:", `rationale`, `files`, `status`) en werk
   `CONTEXT.md` + `docs/digest-xrmpocket.md` in **dezelfde commit** bij. Vervang je een
   oudere beslissing, laat die staan met `status: superseded` + `superseded_by`, of
   `amended_by` als maar een deel vervalt.
6. **Poorten:** app 4108, Vite 5173; 4101-4107 nooit hergebruiken of wijzigen.
7. **Geen GPL-code overnemen.** XrmToolBox en `MscrmTools.Xrm.Connection` zijn GPL-3.0
   (gedrag bestuderen mag, code kopiëren niet). MIT-code (SQL 4 CDS) mag met
   naamsvermelding — die staat in `THIRD-PARTY-NOTICES.md`.
8. **Verifieer met commando's**, niet met aannames: bouwen (`dotnet build`,
   `npm run build`) en gedrag testen met `curl` (§7) vóór je iets "klaar" noemt.
9. **Testdata is synthetisch.** Nooit echte klantgegevens als testmateriaal.
10. **Loop je vast in herhaling?** Stop en vraag de gebruiker (afspraak uit de
    gedeelde repo). Zeker met een klein lokaal model: liever één gerichte vraag dan
    drie mislukte pogingen.

### Extra afspraken voor een **lokaal model** (kleine context)

- **Contextbudget:** laad `HANDOVER.md` + `docs/digest-xrmpocket.md`; open daarna per
  taak hooguit 2-3 bestanden. Gebruik `grep`/`rg` in plaats van hele bestanden lezen.
- **Één stap per keer:** wijzig één bestand, bouw, test, commit. Geen grote
  refactors in één keer.
- **Citeer geen aannames:** als je iets niet in de repo of in een test ziet, zeg dat
  en vraag het.
- **Volledige bestandsinhoud teruggeven** bij codewijzigingen (afspraak van de
  gebruiker: nooit losse diffs laten napluizen).
- **Engelse vakjargon-woorden niet vertalen** (commit, deploy, endpoint, token, …);
  de omringende zin blijft Nederlands.

## 7. Zo zet je het op een nieuwe machine

### 7.1 Vereisten

| Onderdeel | Versie | macOS | Linux |
|---|---|---|---|
| .NET SDK | 10.x | `brew install dotnet` + `export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"` | `dotnet-install.sh --channel 10.0` of pakketbeheer |
| Node.js + npm | 26 / 11 | `brew install node` | NodeSource of `nvm` |
| git + SSH-sleutel | — | zie 7.2 | idem |

### 7.2 Repo + SSH (push-toegang)

De remote gebruikt een **host-alias** met een eigen deploy key (niet je persoonlijke
sleutel). **Maak per machine een nieuwe sleutel** en zet de publieke sleutel als
*Deploy key* (met write access) in de GitHub-repo — kopieer nooit de privésleutel van
de andere machine.

```bash
# 1. nieuwe sleutel + publieke sleutel tonen (plak die in GitHub > Settings > Deploy keys)
ssh-keygen -t ed25519 -C "xrmpocket-<machine>" -f ~/.ssh/gh-xrmpocket-<machine> -N ""
cat ~/.ssh/gh-xrmpocket-<machine>.pub

# 2. host-alias in ~/.ssh/config
cat >> ~/.ssh/config <<'EOF'
Host github.com-xrmpocket
  HostName github.com
  User git
  IdentityFile ~/.ssh/gh-xrmpocket-<machine>
  IdentitiesOnly yes
EOF

# 3. clonen
git clone git@github.com-xrmpocket:Cagiva-Raptor/XrmPocket.git xrmpocket
```

### 7.3 Bouwen en starten

```bash
cd xrmpocket

# backend (zet DOTNET_ROOT op macOS)
cd backend/XrmPocket.Api
dotnet build
dotnet run            # → http://0.0.0.0:4108

# frontend (development)
cd ../../web
npm install
npm run dev           # → http://localhost:5173 (proxy naar 4108)

# frontend (productie: de backend serveert ze uit wwwroot)
npm run build
```

### 7.4 Verifiëren (dit moet groen zijn)

```bash
curl -s http://127.0.0.1:4108/api/health
# {"status":"ok","engine":"10.5.1.0",...}

curl -s http://127.0.0.1:4108/api/connections
# mock (standaard) + SAV-TEST (+ eventueel interactieve verbinding)

curl -s -X POST http://127.0.0.1:4108/api/query \
  -H 'content-type: application/json' \
  -d '{"sql":"SELECT name, revenue FROM account ORDER BY revenue DESC"}'
# ok:true, 4 rijen, fetchXml met <condition .../> en <order .../>

curl -s -X POST http://127.0.0.1:4108/api/query \
  -H 'content-type: application/json' \
  -d '{"sql":"DELETE FROM account"}'
# ok:false, error.code = write_not_allowed
```

Verwacht op een verse machine: `connections.json` wordt **automatisch aangemaakt** met
*Mock (synthetisch)* als standaard en *SAV-TEST* (app-only, nog niet bruikbaar zonder
secret). De mock-queries hierboven werken dus direct.

### 7.5 Wat je **niet** mee kopieert via chat

| Bestand | Waarom | Wat te doen |
|---|---|---|
| `~/.config/opencode/secrets/sapphire_bot_test_secret.txt` | secret | op de nieuwe machine opnieuw instellen (portal) of via `scp` kopiëren |
| `~/.config/xrmpocket/connections.json` | bevat paden/URL's (geen secret) | automatisch aangemaakt; of kopiëren als je dezelfde lijst wilt |
| `~/.config/xrmpocket/tokens/*.json` | refresh tokens | **niet** kopiëren; opnieuw aanmelden |
| SSH-deploykey | privésleutel | **nieuwe sleutel per machine** maken en de publieke sleutel in GitHub zetten (zie §7.2); nooit de privésleutel kopiëren of via chat sturen |

### 7.6 Savaco-laptop (Windows 11, PowerShell)

De app is platformonafhankelijk: ASP.NET Core + Node, geen Unix-specifieke API's
(`File.SetUnixFileMode` wordt op Windows overgeslagen). Deze stappen zijn voor een
**verse Windows-machine**; de laptop heeft een **eigen afspraken-repo** (zie §6, regel
over gescheiden werelden) — voeg XrmPocket-beslissingen dus **niet** daar toe, ze leven
in `afspraken/decisions.yaml` van deze projectrepo.

**a. Voorwaarden installeren**

```powershell
winget install --id Git.Git -e --source winget
winget install --id Microsoft.DotNet.SDK.10 -e --source winget
winget install --id OpenJS.NodeJS -e --source winget
# controleer (nieuw PowerShell-venster):
dotnet --version    # 10.x
node --version      # 22 of nieuwer (op de Mac: 26)
npm --version
```

**b. Repo clonen met een eigen deploy key**

```powershell
ssh-keygen -t ed25519 -C "xrmpocket-laptop" -f "$env:USERPROFILE\.ssh\gh-xrmpocket-laptop" -N '""'
Get-Content "$env:USERPROFILE\.ssh\gh-xrmpocket-laptop.pub"
#   -> GitHub > repo XrmPocket > Settings > Deploy keys > Add deploy key
#      (vink "Allow write access" aan)

Add-Content "$env:USERPROFILE\.ssh\config" @"
Host github.com-xrmpocket
  HostName github.com
  User git
  IdentityFile ~/.ssh/gh-xrmpocket-laptop
  IdentitiesOnly yes
"@

git clone git@github.com-xrmpocket:Cagiva-Raptor/XrmPocket.git xrmpocket
```

**c. Bouwen en starten**

```powershell
cd xrmpocketackend\XrmPocket.Api
dotnet build
dotnet run                     # http://localhost:4108 (luistert op 0.0.0.0)

cd ..\..\web
npm install
npm run build                  # -> backend\XrmPocket.Api\wwwroot
npm run dev                    # optioneel: http://localhost:5173
```

**d. Verifiëren**

```powershell
Invoke-RestMethod http://127.0.0.1:4108/api/health
Invoke-RestMethod http://127.0.0.1:4108/api/connections
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:4108/api/query `
  -ContentType 'application/json' `
  -Body '{"sql":"SELECT name, revenue FROM account ORDER BY revenue DESC"}'
```

**e. Secret instellen (zonder het in de history te zetten)**

```powershell
$dir = "$env:USERPROFILE\.config\opencode\secrets"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$secure = Read-Host -AsSecureString "Client secret"
$plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
           [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
[IO.File]::WriteAllText("$dir\sapphire_bot_test_secret.txt", $plain)
Remove-Variable plain, secure
```

`%USERPROFILE%` is op Windows al per gebruiker afgeschermd (ACL), dus de bestanden
erven veilige rechten. Wil je het expliciet: `icacls` op dat ene bestand met
`/inheritance:r /grant:r "$env:USERNAME:F"` — niet op de hele map (dat breekt backups).

**f. Vanaf de iPhone naar de laptop**

```powershell
# eenmalig, verhoogd PowerShell-venster:
New-NetFirewallRule -DisplayName "XrmPocket 4108" -Direction Inbound `
  -LocalPort 4108 -Protocol TCP -Action Allow -Profile Private
```

Gebruik daarna `http://<laptop-ip>:4108`. PWA-installatie en interactief aanmelden
vereisen HTTPS (zie §5 en `docs/verbindingen.md`); voor de rest volstaat http.

**g. Let op**

- Poort vrij? `Get-NetTCPConnection -LocalPort 4108 -ErrorAction SilentlyContinue`
- Regelafsluitingen: `.gitattributes` zet alles op **LF**; niet handmatig omzetten.
- De laptop heeft géén `~/.config/xrmpocket/connections.json` nodig: dat wordt bij de
  eerste start aangemaakt (mock + SAV-TEST).

### 7.7 Sauron (alleen als die machine Sauron heeft)

Nieuw project toevoegen in `bridge/projects.json` met **de volgende vrije poort**,
sessietitel `Sauron - XrmPocket`, en de brug herstarten. De app zelf blijft op 4108.

## 8. Volgende stappen (prioriteit)

1. **Verbinding groen krijgen** (fase 1): portal-actie gebruiker (§5), dan
   `POST /api/connections/{id}/test` en een echte query op SAV-TEST.
2. **iPhone/PWA**: HTTPS-adres (Tailscale `serve` of Caddy) vóór 4108, manifest testen,
   installeren op het beginscherm.
3. **Tests toevoegen**: xUnit-project voor `SqlSafetyCheck`, `MockMetadataCache`,
   `FetchXmlEvaluator` en de SQL→FetchXML-route (nu alleen met de hand getest).
4. **UI-verfijning**: kolombreedtes/typen, querygeschiedenis, exporteren van resultaten.
5. **Fase 4**: andere XrmToolBox-tools — pas na een volledig goed werkende fase 2-3,
   en de volgorde kiest de gebruiker.

## 9. Bekende valkuilen (kostte eerder tijd)

1. **Engine-metadata**: zonder `EntityLogicalName`, `ColumnNumber`, `IsPrimaryId`,
   `PrimaryIdAttribute`, `PrimaryNameAttribute` én relaties (`ManyToOne`/`OneToMany`)
   faalt de engine met een `NullReferenceException` of kan een JOIN niet vertaald worden.
2. **Lookups/aggregaten** moeten als `AliasedValue` terugkomen (`EntityLogicalName` =
   echte entiteit, `AttributeLogicalName` = echte kolom), anders volgt
   `Unable to cast ... to 'Microsoft.Xrm.Sdk.AliasedValue'`.
3. **Collatie**: een `DataSource` zonder `Connection` klapt op
   `LoadDefaultCollation` — de mock geeft daarom een `organization`-record met
   `localeid` 1033.
4. **Poort 4107 is bezet** door de Sauron/openCode-sessie van dit project; de app
   gebruikt daarom 4108 (beslissing `2026-10-31-app-poort-4108`).
5. **`dotnet` staat niet in PATH** na Homebrew-install: `DOTNET_ROOT` zetten.
6. **iOS**: PWA installeren en interactief aanmelden vereisen **HTTPS** (behalve
   `localhost`). Kale `http://192.168.x.x:4108` werkt voor query's, niet voor die twee.
7. **`SavePassword`-gedrag van XrmToolBox niet nadoen**: wij bewaren nooit een secret
   in het verbindingsprofiel.

## 10. Checklist voor de nieuwe instance (in deze volgorde)

- [ ] `git clone` of `git pull` — `git log` eindigt op of na `a0ff39a`
- [ ] `HANDOVER.md`, `docs/digest-xrmpocket.md`, `docs/architectuur.md` gelezen
- [ ] `dotnet build` in `backend/XrmPocket.Api` → **Build succeeded**
- [ ] `npm install && npm run build` in `web` → output in `backend/.../wwwroot`
- [ ] backend gestart, `/api/health` = `ok`
- [ ] mock-query via `curl` geeft rijen + FetchXML
- [ ] schrijfquery geweigerd (`write_not_allowed`)
- [ ] afspraken in §6 overgenomen (zeker: geen secrets, kleine commits, alleen lezen)
- [ ] bij twijfel: vraag de gebruiker in plaats van te gokken
- [ ] Windows-laptop: `dotnet --version` = 10.x, `node --version` ≥ 22, poort 4108 vrij
- [ ] Windows-laptop: eigen deploy key in GitHub (geen privésleutel gekopieerd)
- [ ] Windows-laptop: laptop-afsprakenrepo **niet** gemengd met dit project

## 11. Waar de beslissingen staan

`afspraken/decisions.yaml` (10 notities):

| Id | Onderwerp | Status |
|---|---|---|
| `2026-10-30-project-aangemaakt-met-conventies` | start als map met conventies | superseded |
| `2026-10-30-zichtbaar-in-sauron-app` | Sauron-registratie, poort 4107 | active |
| `2026-10-30-naam-en-merkkader` | merknaam XrmPocket, geen XrmToolBox-branding | active |
| `2026-10-30-web-based-eerst-geen-native-start` | web-first, native later als wrapper | active (amended) |
| `2026-10-30-stack-aspnetcore-react` | ASP.NET Core + React/TS/Vite | active |
| `2026-10-31-app-poort-4108` | app-poort 4108 | active |
| `2026-10-31-verbindingen-zoals-xrmtoolbox` | verbindingsmodel, secrets buiten profiel | active (amended) |
| `2026-11-01-interactieve-login-als-tweede-authvorm` | eigen account naast app-only | active |
| `2026-11-10-overdracht-via-docs` | overdracht via HANDOVER + digest, niet via de sessie | active |
| `2026-11-26-windows-laptop-als-tweede-ontwikkelmachine` | Windows-laptop: eigen deploy key, LF, gescheiden afspraken | active |

## 12. Contactpunten / bronnen

- SQL 4 CDS-engine (MIT): https://github.com/MarkMpn/Sql4Cds
- XrmToolBox (GPL-3.0, alleen ter inspiratie): https://github.com/MscrmTools/XrmToolBox
- Connection controls (GPL-3.0, gedrag bestudeerd): https://github.com/MscrmTools/MscrmTools.Xrm.Connection
- Gedeelde afspraken-repo: `/Users/jurgendictus/opencode/afspraken/` (andere machine:
  eigen pad; bevat `AGENTS.md`, `digest.md`, scripts en skills)
