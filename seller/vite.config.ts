import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Served behind Nginx at /seller/
export default defineConfig({
  base: '/seller/',
  plugins: [react()],
  server: {
    port: 5174,
    proxy: {
      '/api': 'http://localhost:18080',
      '/health': 'http://localhost:18080',
    },
  },
})
