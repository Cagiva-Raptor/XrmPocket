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

## API

| Endpoint | Doel |
|---|---|
| `GET /api/connections` | lijst + `defaultConnectionId` (met `configured`, `secretPresent`, `note`) |
| `POST /api/connections` | verbinding aanmaken of wijzigen |
| `DELETE /api/connections/{id}` | verwijderen |
| `POST /api/connections/{id}/default` | standaardverbinding kiezen |
| `POST /api/connections/{id}/test` | WhoAmI-test (omgeving, versie, gebruiker) |
| `POST /api/connections/{id}/secret` | client secret bewaren (chmod 600) |
| `GET /api/environments` | sjablonen (dev/test/acc/productie) om snel een verbinding te maken |
| `POST /api/query` | SQL uitvoeren; `connectionId` leeg = standaardverbinding |

## Eerste start

`ConnectionStore` zet bij de eerste start twee verbindingen klaar: **Mock
(synthetisch)** als standaard en **SAV-TEST** (app-only, met het standaard
client-ID/tenant en het secret-pad uit de configuratie). SAV-TEST blijft
`configured: false` tot er een bruikbaar secret is — precies zoals XrmToolBox een
verbinding toont die nog om een wachtwoord vraagt.
