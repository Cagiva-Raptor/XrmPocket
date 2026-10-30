import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// De API draait lokaal op 4108 (zie backend/XrmPocket.Api/appsettings.json).
// In productie serveert de backend de gebouwde frontend uit wwwroot, dus dan is er
// geen proxy nodig en draait alles achter dezelfde origin.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://127.0.0.1:4108',
    },
  },
  build: {
    outDir: '../backend/XrmPocket.Api/wwwroot',
    emptyOutDir: true,
  },
})
