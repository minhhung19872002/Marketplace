// Local UI preview against a remote stack (read-only browsing): SH_API_URL=https://… npx vite --config vite.preview-remote.config.ts
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const apiTarget = process.env.SH_API_URL ?? 'http://localhost:18080'
const remote = { target: apiTarget, changeOrigin: true, secure: true }

export default defineConfig({
  plugins: [react()],
  server: { port: 5173, proxy: { '/api': remote, '/health': remote, '/hubs': { ...remote, ws: true } } },
})
