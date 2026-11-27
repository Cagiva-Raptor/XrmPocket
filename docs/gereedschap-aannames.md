# Gereedschap — aannames en keuzes (nacht-sessie 2026-11-27)

Ter controle door de gebruiker de volgende ochtend. Alles hieronder is zelf
geïmplementeerd met de Dataverse-SDK; er is **geen code van XrmToolBox (GPL-3.0)**
overgenomen, alleen het gedrag is als inspiratie gebruikt.

## Wat er staat

| Tool | Wat het doet | Schrijft? |
|---|---|---|
| FetchXML Builder | FetchXML typen en uitvoeren (`RetrieveMultiple`), tabel met resultaat | nee |
| Ribbon Workbench | ribbon-XML van een tabel ophalen (`RetrieveEntityRibbonRequest`), alleen-lezen | nee |
| Data Transporter | CSV-bestand importeren als nieuwe records (eerst controleren, dan importeren) | **ja** |
| User Security Manager | gebruikers lijsten, rollen tonen, rol toevoegen/verwijderen | **ja** |

De tools zijn bereikbaar via de tab **Gereedschap** naast **SQL**. Alleen voor
Dataverse-verbindingen; op de demo-verbinding krijg je een melding.

## Aannames die je moet controleren

1. **Ribbon Workbench is alleen-lezen.** Het wijzigen van de ribbon (en opslaan
   in een oplossing) is niet gebouwd; dat is te groot voor één nacht en vraagt
   om een beslissing over oplossingen en publiceren.
2. **Ribbon-XML wordt als zip gelezen.** Dataverse levert `CompressedEntityXml`
   als zip met `RibbonXml.xml`. Op SAV-TEST getest: het XML-antwoord komt door.
3. **Ribbon-filter niet meegegeven.** Het veld `RibbonLocationFilters` wordt door
   de server geweigerd ("Unrecognized request parameter"); daarom haalt de tool
   de standaard-ribbon op zonder filter.
4. **Data Transporter importeert alleen tekstwaarden.** Lookups, keuzelijsten,
   datums en getallen worden als tekst meegestuurd en kunnen daardoor fouten
   geven. Er is geen mapping-scherm. Eerste rij = logische kolomnamen.
5. **Import vraagt bevestiging** (`window.confirm`) en maakt records **per rij**;
   fouten per rij worden gemeld, de rest gaat door. Er is geen rollback.
6. **User Security Manager toont max. 500 gebruikers** (`TopCount`). Alle
   systemusers zijn dus nog niet zichtbaar bij grotere omgevingen.
7. **Rol toevoegen/verwijderen** gebruikt de relatie `systemuserroles_association`.
   Niet op SAV-PRD getest om onbedoelde wijzigingen te voorkomen.
8. **Rechten:** de application user van SAV-TEST mag systemuser/role niet
   lezen (zie eerdere melding). Dat betekent dat de gebruikerslijst en rollen
   bij SAV-TEST mogelijk leeg of met een fout komen; dat is een rechtenkwestie,
   geen bug in de tool.
9. **FetchXML-resultaat** toont max. 200 rijen (vast in de UI), de server
   begrenst op 5000.
10. **Geen mock-ondersteuning** voor de gereedschappen; op de demo staat een
    melding.

## Niet gedaan (bewust)

- Ribbon-wijzigingen opslaan en publiceren.
- Export naar CSV vanuit de tools (de Data Transporter doet alleen import).
- Automatische type-conversie bij import.
- Test op SAV-PRD voor rolwijzigingen.
