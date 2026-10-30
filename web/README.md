# XrmPocket — frontend

React + TypeScript + Vite. De frontend praat alleen met de eigen backend
(`/api/...`); verbindingen en secrets blijven server-side.

```bash
npm install
npm run dev     # http://localhost:5173, proxy /api → http://127.0.0.1:4108
npm run build   # bouwt naar ../backend/XrmPocket.Api/wwwroot
```

Bestanden:

| Bestand | Inhoud |
|---|---|
| `src/App.tsx` | editor, omgevingskiezer, resultaatgrid, FetchXML-paneel |
| `src/api.ts` | API-types en fetch-aanroepen |
| `public/manifest.webmanifest` | PWA-manifest (installeerbaar op de iPhone) |
| `public/sw.js` | service worker: app-shell cache, `/api` altijd via netwerk |

De PWA is pas installeerbaar via de backend-origin (poort 4108), niet via de
Vite-devserver (5173): daar ontbreekt de service worker bewust.
