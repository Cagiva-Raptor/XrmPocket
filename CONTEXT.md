# xrmpocket — projectoverzicht (om mee te nemen)

Laatst bijgewerkt: 2026-10-30. Lees dit bij de start van elke xrmpocket-sessie.

## Omgeving

- **Mac Mini M4** (lokaal, 192.168.9.9), OpenCode v2
- Pad: `/Users/jurgendictus/opencode/xrmpocket`
- GitHub-repo: **`Cagiva-Raptor/XrmPocket`** (leeg, geen commits)
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

## Stand van zaken (2026-10-30)

**Wat nu bestaat**

- Map + `git` (aangemaakt 2026-10-30) + `.gitignore`
- **SSH deploy key** klaar: `~/.ssh/gh-xrmtoolbox-ios-deploy` (ed25519, geen
  passphrase, `chmod 600`) + host-alias `github.com-xrmpocket` in `~/.ssh/config`
  (deze key raakt enkel dit repo; de persoonlijke sleutel blijft buiten beeld).
  **Getest:** sleutelpaar ✔, authenticatie ✔ (`Hi Cagiva-Raptor/XrmPocket!`),
  leestoegang via `git ls-remote` ✔. Remote-URL wordt straks:
  `git@github.com-xrmpocket:Cagiva-Raptor/XrmPocket.git`
- `afspraken/` in het project met `afspraken/decisions.yaml`
- Sauron-geregistreerd: id `xrmpocket`, poort **4107**, sessie `Sauron - XrmPocket`

**Wat nog open is**

- **Stack/keuze**: geen enkel stack-bestand toegevoegd — dat is een aparte
  beslissing (zie `afspraken/decisions.yaml`)
- **Scope**: welke functionaliteit eerst (query's draaien? schema verkennen? alle
  XrmToolBox-tools?) en voor wie (jij alleen, collega's, publiek in de App Store?)
- **Platformkeuze**: native Swift/SwiftUI vs. cross-platform — volgt uit de scope,
  niet omgekeerd
- **Licentie van het eigen project**: kiezen vóór de eerste publicatie (GPL-3.0
  als je XrmToolBox/andere GPL-code meeneemt, anders vrij te kiezen)
- App Store-deelname vraagt een Apple Developer-account (staat of valt met die
  keuze; TestFlight is jullie standaardkanaal voor eigen apps)

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

1. Elke sessie begint met **`CONTEXT.md`** + **`afspraken/decisions.yaml`**
2. Nieuwe afspraak = toevoegen aan `decisions.yaml` **én** dit overzicht bijwerken
3. Geen secrets (wachtwoorden, tokens, sleutels) in deze bestanden of in
   commando's/logs — de deploy key blijft in `~/.ssh/`
4. Kleine, Nederlandse commits
5. Deel-rijke wijzigingen aan de gedeelde afspraken-repo: commit **én push**, andere
   machines pullen

## Open vragen

1. **Welke functionaliteit eerst?** (bv. alleen query's zoals SQL 4 CDS, of bredere
   toolbox zoals XrmToolBox zelf)
2. **Voor wie** is de app: jijzelf, Savaco-collega's, of publiek in de App Store?
3. Welke **platformkeuze** (native vs. cross-platform) volgt uit die scope?
4. Welke **license** krijgt het eigen project — en komt er GPL-code van XrmToolBox in?
5. Is er behoefte aan **inlog op meerdere omgevingen** (dev/test/acc), en zo ja via
   welke authenticatie — rekening houdend met de CA-eis dat alleen
   Intune-beheerde apparaten interactief mogen aanmelden?
