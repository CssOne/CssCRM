import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    // Permite rodar `npm run dev` isoladamente (sem passar pelo host ASP.NET Core em 5299).
    proxy: {
      '/api': {
        target: 'http://localhost:5299',
        changeOrigin: true,
      },
    },
  },
})
