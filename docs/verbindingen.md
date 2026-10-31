# Verbindingen — zoals XrmToolBox, maar web-only

**Datum:** 2026-10-31 · **Status:** geïmplementeerd

De vraag was: *hoe gebruikt de originele XrmToolBox verbindingen, en doe het op dezelfde
manier.* Hieronder eerst de feiten uit de bron, dan wat XrmPocket overneemt en waar het
bewust afwijkt.

> **Licentie:** XrmToolBox is GPL-3.0 en het connection-controls-pakket
> (`MscrmTools.Xrm.Connection`, repo `MscrmTools/MscrmTools.Xrm.Connection`) is dat ook.
> We hebben de code **gelezen om het gedrag te begrijpen** en niets overgenomen; de
> implementatie hieronder is zelf geschreven. Zo blijft de licentie van XrmPocket vrij
> (zie `THIRD-PARTY-NOTICES.md`).

## Hoe XrmToolBox het doet (bron: `XrmToolBox` + `MscrmTools.Xrm.Connection`)

1. **Eén lijst met verbindingen**, bewaard als XML:
   `XrmToolBox/New/NewForm.cs` zet
   `ConnectionsList.ConnectionsListFilePath = <AppData>\MscrmTools\XrmToolBox\Connections\MscrmTools.ConnectionsList.xml`.
   Daarbinnen zit `ConnectionsList → Files (ConnectionFile) → CrmConnections → Connections`.
   Meerdere "connection files" kunnen naast elkaar bestaan (`MergeConnectionFiles`).
2. **`ConnectionDetail` = één verbinding**: `ConnectionId` (Guid), `ConnectionName`,
   `OrganizationServiceUrl` / `OrganizationDataServiceUrl` / `OrganizationUrl`,
   `OrganizationVersion`, `OrganizationFriendlyName`, `EnvironmentId`, `AuthType`
   (`AuthenticationProviderType`: OAuth/Office365, client secret, certificaat, on-prem),
   `TenantId`, `AzureAdAppId`, `UserName`, `UseMfa`, `UseIfd`, `SavePassword`, `Timeout`,
   `LastUsedOn`, `EnvironmentHighlighting` (kleur/tekst per omgeving), een
   **metadata-cache per verbinding**, en `ClientSecretEncrypted` / `RefreshToken`.
3. **Secrets worden versleuteld bewaard** met `CryptoManager` (Rijndael, sleutel afgeleid
   van constanten in de app) — alleen als `SavePassword` aan staat; anders wordt bij het
   verbinden via de `RequestPassword`-event om het wachtwoord/secret gevraagd. Er is ook
   een Azure Key Vault-route (`AzureKeyVaultName`) en een
   `SensitiveDataNotFoundReason` (geweigerd/niet bereikbaar).
4. **Verbinden** gaat via `ConnectionManager.ConnectToServer(List<ConnectionDetail>)`:
   per verbinding wordt een `CrmServiceClient` gemaakt (of hergebruikt als
   `ReuseConnections` aan staat), met `StepChanged`-voortgang en `ConnectionSucceed`
   (geeft `ConnectionDetail` + `OrganizationService` terug).
5. **Bij het starten** kan XrmToolBox automatisch verbinden met een ingestelde
   *initial connection* (`initialConnectionName`); anders kies je in de connection
   selector/manager. De lijst kan op **meest recent gebruikt** gesorteerd worden
   (`NumberOfRecentConnectionsToDisplay`, `ShowMostRecentConnections`).
6. **Per tool-tab** hangt één `ConnectionDetail`; je kunt meerdere verbindingen
   tegelijk open hebben en een tab van verbinding wisselen (`TabConnectionUpdater`).
   De statusbalk toont omgeving + gebruiker, met **impersonatie** als de verbinding dat
   toelaat (`CanImpersonate`).

## Wat XrmPocket overneemt

| XrmToolBox | XrmPocket |
|---|---|
| `ConnectionDetail` | `ConnectionProfile` (`backend/.../Connections/ConnectionProfile.cs`) |
| `MscrmTools.ConnectionsList.xml` | `~/.config/xrmpocket/connections.json` (chmod 600, buiten de repo) |
| Connection Manager-dialoog | `web/src/ConnectionManager.tsx` (modal: lijst, nieuw, bewerken, standaard, testen, verwijderen) |
| Connection selector + statusbalk | verbindingskiezer in de topbar + statusbalk met omgevingsbadge |
| `initialConnectionName` | `defaultConnectionId` in het bestand; UI opent die verbinding |
| `LastUsedOn` / MRU | `LastUsedOn` per verbinding (wordt bij elke query bijgewerkt) |
| `ReuseConnections` | `ConnectionFactory`-cache per verbinding met vingerafdruk (URL, tenant, client-ID, secret-bestand + wijzigingstijd) |
| `RequestPassword`-event | `POST /api/connections/{id}/secret` (waarde wordt nooit teruggegeven of gelogd) |
| `WhoAmI` in de statusbalk | `POST /api/connections/{id}/test` → omgeving, versie, gebruikers-id |
| `EnvironmentHighlighting` | `highlight` per verbinding → badge (dev/test/acc/productie) |
| Meerdere verbindingen tegelijk | elke query kiest een verbinding; wisselen = andere verbinding in de kiezer |

## Waar XrmPocket bewust afwijkt

| Punt | XrmToolBox | XrmPocket | Waarom |
|---|---|---|---|
| Secretopslag | versleuteld ín het connection-bestand (sleutel zit in de app) | **nooit** in het profiel; apart bestand met chmod 600, profiel bevat enkel het pad | Een sleutel in de app is terug te vinden; een bestand met 600 is op deze machine beter te bewaken, en de repo blijft schoon |
| Authenticatie | OAuth/interactief, client secret, certificaat | **alleen app-only** (client credentials); interactief staat in de UI maar is uitgeschakeld | Conditional Access weigert interactieve login op onbeheerde apparaten (`AADSTS530033`, bewezen op LXC én Mac Mini) |
| Impersonatie | mogelijk (`CanImpersonate`) | niet | app-only heeft één vaste applicatiegebruiker; impersonatie vraagt een gebruikerscontext |
| Proxy-instellingen | per connection file | niet | server-side proces zonder proxy-configuratie |
| Key Vault | optie | niet (kan later: dan leest de backend het secret daar) | nu te veel voor de eerste versie |
| Verbinding per tab | ja | nee, één actieve verbinding per browser | web-UI heeft geen tabs met aparte sessies; per query kiezen dekt het gebruik |

## Aanmelden met je eigen Microsoft-account (interactief)

Naast app-only kan een verbinding **interactief** zijn: je meldt je aan met je eigen
account, precies zoals de "Office365"-verbinding in XrmToolBox. De aanmelding gebeurt
in **jouw browser** (OAuth authorization code met PKCE); de backend bewaart daarna een
refresh token (chmod 600, `~/.config/xrmpocket/tokens/`) en haalt daarmee access tokens
op. Elke query loopt dan als **jouw** gebruiker, met jouw rechten en rollen.

### Eenmalig in de app registration (portal)

1. Entra ID > App registrations > *Sapphire AI Bot - Test* > **Authentication** >
   **Add a platform** > **Web** en voeg de redirect-URI's toe die je gebruikt:
   - `http://localhost:4108/api/auth/callback` (op de Mac zelf)
   - `http://192.168.9.9:4108/api/auth/callback` (LAN, zie de HTTPS-opmerking)
   - de HTTPS-URL als je via Tailscale werkt
2. **API permissions** > *Add a permission* > **Dynamics CRM** (Dataverse) >
   *Delegated permissions* > **user_impersonation** > daarna **Grant admin consent**.
3. Zorg dat je eigen account in de omgeving een systemuser met de juiste rol is
   (voor SAV-TEST: je gewone Savaco-account).

Daarna in XrmPocket: *Verbindingen…* > nieuwe verbinding > authenticatie
**Interactief** > **Bewaren** > **Aanmelden met Microsoft** > testen.

### Wat de backend doet

| Stap | Endpoint |
|---|---|
| autorisatie-URL met PKCE bouwen (state + code_challenge) | `POST /api/connections/{id}/signin` |
| code inwisselen voor tokens, refresh token bewaren | `GET /api/auth/callback` (redirect-URI) |
| access token halen/verversen bij elke query | intern (token-provider van de ServiceClient) |
| afmelden (token-bestand verwijderen) | `POST /api/connections/{id}/signout` |
| status tonen (aangemeld? welk account? verloopt wanneer?) | `GET /api/connections/{id}/auth` |

De UI stuurt zijn eigen origin mee; alleen origins uit
`XrmPocket:InteractiveAllowedOrigins` worden aanvaard (geen open redirect). De
callback-URL moet exact overeenkomen met de app registration — vandaar dat de lijst
en de portal hetzelfde moeten zeggen.

### Twee waarschuwingen

1. **Conditional Access.** De eerdere blokkade (`AADSTS530033`) kwam van *device code
   flow op de server* — dat kan geen apparaatcompliance bewijzen. Deze flow meldt aan
   in **jouw browser**: op een beheerd/compliant apparaat kan het wél werken. Krijg je
   `AADSTS530033`, dan weigert het beleid dit apparaat alsnog; gebruik dan app-only of
   meld aan vanaf een beheerd apparaat. De foutmelding in de UI zegt dit letterlijk.
2. **HTTPS voor de iPhone.** Microsoft aanvaardt `http://` alleen voor `localhost`.
   Werk je op de iPhone via `http://192.168.9.9:4108`, dan moet je voor interactief
   aanmelden (en voor PWA-installatie) een HTTPS-adres gebruiken — bijvoorbeeld
   `tailscale serve` of Caddy vóór poort 4108. Zet dat adres in de app registration én
   in `InteractiveAllowedOrigins`.

## Application user-check (wie ben ik echt?)

XrmToolBox toont in zijn statusbalk de omgeving en de gebruiker. Omdat wij app-only
werken, is de "gebruiker" de **applicatiegebruiker** in Dataverse; die moet bestaan,
actief zijn en rollen hebben. `POST /api/connections/{id}/test` doet daarom na de
WhoAmI nog drie dingen:

1. **`systemuser` ophalen** voor de WhoAmI-gebruiker: `fullname`, `domainname`,
   `isdisabled`, `applicationid`.
2. **Rollen ophalen** via `role` met een link naar `systemuserroles`
   (dezelfde query die de engine zelf gebruikt), inclusief de controle op de vaste
   `roletemplateid` van *Systeembeheerder* (`627090FF-40A3-4053-8790-584EDC5BE201`).
3. **Vergelijken**: hoort het `applicationid` bij het client-ID van deze verbinding, is
   de gebruiker gedeactiveerd, en heeft hij rollen? Zo niet, dan zegt de melding wat er
   mist — vóór je een query draait.

De statusbalk toont daarna bijvoorbeeld:

```
✔ Operations SAV Test [operations-sav-test] (9.2.…) · Sapphire AI Bot (a52e…) · 3 rollen
  [systeembeheerder]
```

De mock-verbinding levert een duidelijk verzonnen gebruiker + rollen, zodat de
weergave testbaar is zonder Dataverse.

## API

| Endpoint | Doel |
|---|---|
| `GET /api/connections` | lijst + `defaultConnectionId` (met `configured`, `secretPresent`, `note`) |
| `POST /api/connections` | verbinding aanmaken of wijzigen |
| `DELETE /api/connections/{id}` | verwijderen |
| `POST /api/connections/{id}/default` | standaardverbinding kiezen |
| `POST /api/connections/{id}/test` | WhoAmI + application user-check (omgeving, versie, gebruiker, rollen) |
| `POST /api/connections/{id}/secret` | client secret bewaren (chmod 600) |
| `GET /api/environments` | sjablonen (dev/test/acc/productie) om snel een verbinding te maken |
| `POST /api/query` | SQL uitvoeren; `connectionId` leeg = standaardverbinding |

## Eerste start

`ConnectionStore` zet bij de eerste start twee verbindingen klaar: **Mock
(synthetisch)** als standaard en **SAV-TEST** (app-only, met het standaard
client-ID/tenant en het secret-pad uit de configuratie). SAV-TEST blijft
`configured: false` tot er een bruikbaar secret is — precies zoals XrmToolBox een
verbinding toont die nog om een wachtwoord vraagt.
