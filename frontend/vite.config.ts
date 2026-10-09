/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { existsSync } from 'node:fs'
import type { ServerResponse } from 'node:http'
import { defineConfig, type ProxyOptions } from 'vite'

// The browser only ever talks to its own origin ("/api/..."). In development this server forwards those requests to
// the API, so the HttpOnly session cookie works exactly as it does behind the production reverse proxy.
// DEV_API_TARGET is read here, on the server side only: it is never bundled into the page.
//
// The default is the address of `dotnet run --project src/VehicleRental.Api`. The Docker Compose API listens on
// API_PORT instead (8080 by default), so with Compose run: DEV_API_TARGET=http://localhost:8080 npm run dev
const apiTarget = process.env.DEV_API_TARGET ?? 'http://localhost:5270'

/** Local development only: never open a browser in CI, inside a container, or when the developer opts out. */
function shouldOpenBrowser(): boolean {
  const optedOut = process.env.NO_OPEN === '1' || process.env.NO_OPEN === 'true'
  const inCi = Boolean(process.env.CI)
  const inContainer = process.env.DOCKER === '1' || existsSync('/.dockerenv')
  return !(optedOut || inCi || inContainer)
}

/**
 * When the API is not running the proxy cannot connect. Log one readable line (instead of a stack trace for every
 * request) and answer with a 503 Problem Details body, so the page can say the service is unavailable.
 */
const apiProxy: ProxyOptions = {
  target: apiTarget,
  changeOrigin: false,
  configure: (proxy) => {
    proxy.removeAllListeners('error')
    let lastWarning = 0
    proxy.on('error', (error: NodeJS.ErrnoException & { errors?: NodeJS.ErrnoException[] }, req, res) => {
      const now = Date.now()
      if (now - lastWarning > 10_000) {
        lastWarning = now
        const reason = error.code ?? error.errors?.[0]?.code ?? error.message
        console.warn(
          `\n[api proxy] Cannot reach the API at ${apiTarget} (${reason}). Start it, or set DEV_API_TARGET. See docs/frontend.md.\n`,
        )
      }
      const response = res as ServerResponse
      if (!response.headersSent) {
        response.writeHead(503, { 'Content-Type': 'application/problem+json' })
      }
      response.end(
        JSON.stringify({ status: 503, title: 'The service is unavailable', detail: `Could not reach the API for ${req.url}.` }),
      )
    })
  },
}

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    open: shouldOpenBrowser(),
    proxy: { '/api': apiProxy },
  },
  preview: {
    port: 4173,
    proxy: { '/api': apiProxy },
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
