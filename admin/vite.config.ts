import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Served behind Nginx at /admin/
export default defineConfig({
  base: '/admin/',
  plugins: [react()],
  server: {
    port: 5175,
    proxy: {
      '/api': 'http://localhost:18080',
      '/health': 'http://localhost:18080',
    },
  },
})
