import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end tests run against a REAL stack: the website, the API and PostgreSQL (docker compose up). Nothing is
 * mocked. They need:
 *   E2E_BASE_URL        the website, default http://localhost:8081
 *   E2E_ADMIN_EMAIL     an admin account of that stack (to create the vehicles the tests book)
 *   E2E_ADMIN_PASSWORD
 * Use a disposable database: the tests create accounts, vehicles and reservations and do not remove them.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  workers: process.env.CI ? 2 : undefined,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : [['list']],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:8081',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'] }, testIgnore: /mobile\.spec\.ts/ },
    { name: 'mobile', use: { ...devices['Pixel 7'] }, testMatch: /mobile\.spec\.ts/ },
  ],
})
