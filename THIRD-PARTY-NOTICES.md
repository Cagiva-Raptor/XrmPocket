# Third-party vermeldingen

XrmPocket gebruikt code van derden. Deze vermeldingen zijn verplicht (MIT) of beleefd
(open source) — de volledige licentieteksten staan bij de respectievelijke pakketten.

## MarkMpn.Sql4Cds.Engine

- **Wat:** de SQL 4 CDS-engine — zet T-SQL om naar FetchXML en voert die uit tegen
  Dataverse. Wordt inprocess gebruikt in `backend/XrmPocket.Api`.
- **Auteur:** Mark Carrington (MarkMpn)
- **Bron:** https://github.com/MarkMpn/Sql4Cds
- **Licentie:** MIT
- **Versie:** 10.5.1 (NuGet `MarkMpn.Sql4Cds.Engine`)

```
MIT License

Copyright (c) Mark Carrington

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Microsoft.Xrm.Sdk / Microsoft.PowerPlatform.Dataverse.Client

- **Wat:** Dataverse-SDK en de service-client voor app-only verbindingen
  (transitief meegeleverd met de engine).
- **Licentie:** MIT (Microsoft)
- **Bron:** https://www.nuget.org/packages/Microsoft.PowerPlatform.Dataverse.Client

## Inspiratie

XrmPocket is **geïnspireerd door** XrmToolBox (MscrmTools, GPL-3.0), maar bevat geen
code van XrmToolBox en gebruikt de naam niet als productnaam. Zie beslissing
`2026-10-30-naam-en-merkkader` in `afspraken/decisions.yaml`.

## Licentie van dit project

Nog te kiezen (open vraag in `CONTEXT.md`). Zolang er geen GPL-3.0-code van XrmToolBox
zelf in het project zit, is de eigen licentie vrij; MIT-code hierboven mag met
naamsvermelding hergebruikt worden.
