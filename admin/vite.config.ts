import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Served behind Nginx at /admin/; the dev server proxies the API (SH_API_URL overrides, e.g. a running local stack)
const api = process.env.SH_API_URL ?? 'http://localhost:18080'

export default defineConfig({
  base: '/admin/',
  plugins: [react()],
  server: {
    port: 5175,
    proxy: {
      '/api': api,
      '/health': api,
    },
  },
})
