import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/Games': {
        target: 'http://localhost:5095',
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
