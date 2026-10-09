import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Dev server proxies the API (docker stack on 18080, or SH_API_URL) so the browser stays same-origin
const apiTarget = process.env.SH_API_URL ?? 'http://localhost:18080'

export default defineConfig({
  plugins: [react()],
  build: {
    rollupOptions: {
      output: {
        // Every icon in one chunk: split per page, the home page waited on ~20 tiny icon files one after the other on a
        // phone (G3 C11)
        manualChunks: (id) => (id.includes('node_modules/lucide-react') ? 'icons' : undefined),
      },
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': apiTarget,
      '/health': apiTarget,
      '/hubs': { target: apiTarget, ws: true },
    },
  },
})
