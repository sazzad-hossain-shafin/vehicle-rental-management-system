/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The browser only ever talks to its own origin ("/api/..."). In development this server forwards those requests to
// the API, so the HttpOnly session cookie works exactly as it does behind the production reverse proxy.
// DEV_API_TARGET is read here, on the server side only: it is never bundled into the page.
const apiTarget = process.env.DEV_API_TARGET ?? 'http://localhost:5270'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: false },
    },
  },
  preview: {
    port: 4173,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: false },
    },
  },
  build: {
    sourcemap: false,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    include: ['src/**/*.test.{ts,tsx}'],
  },
})
