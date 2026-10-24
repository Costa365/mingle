import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

const proxy = {
  '/api': { target: process.env.API_PROXY_TARGET ?? 'http://127.0.0.1:5100' },
  '/hubs': { target: process.env.API_PROXY_TARGET ?? 'http://127.0.0.1:5100', ws: true },
}

export default defineConfig({
  plugins: [react()],
  server: { port: 5173, strictPort: true, proxy },
  preview: { port: 4173, strictPort: true, proxy },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    clearMocks: true,
    restoreMocks: true,
  },
})
