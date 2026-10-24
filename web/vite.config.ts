import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { createLogger } from 'vite'
import { redactProxyLog } from './src/realtime/logs'

const logger = createLogger()
const safeLogger = {
  ...logger,
  info: (message: string) => logger.info(redactProxyLog(message)),
  warn: (message: string) => logger.warn(redactProxyLog(message)),
  warnOnce: (message: string) => logger.warnOnce(redactProxyLog(message)),
  error: (message: string) => logger.error(redactProxyLog(message)),
}

const proxy = {
  '/api': { target: process.env.API_PROXY_TARGET ?? 'http://127.0.0.1:5100' },
  '/hubs': { target: process.env.API_PROXY_TARGET ?? 'http://127.0.0.1:5100', ws: true },
}

export default defineConfig({
  customLogger: safeLogger,
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
