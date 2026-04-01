import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: process.env.CIP_API_PROXY_TARGET ?? 'http://localhost:5180',
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
