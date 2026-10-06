import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Dev server proxies the API (docker stack on 18080, or SH_API_URL) so the browser stays same-origin
const apiTarget = process.env.SH_API_URL ?? 'http://localhost:18080'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': apiTarget,
      '/health': apiTarget,
    },
  },
})
