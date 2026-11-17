import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      // The API from `dotnet run`; override when it listens elsewhere.
      '/api': process.env.TEZGAH_API_URL ?? 'http://localhost:5080',
    },
  },
  test: {
    environment: 'node',
  },
})
