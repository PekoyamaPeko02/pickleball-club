import { defineConfig, devices } from '@playwright/test';

// Not the usual 5090 / 9010 / 9011: a developer's own servers live there and must never be reused.
const API_PORT = 5190;
const WEB_PORT = 9110;
const ADMIN_PORT = 9111;
const API = `http://localhost:${API_PORT}`;
const DB_PORT = process.env.E2E_DB_PORT ?? '5434';

/**
 * End-to-end tests run against the real stack: the API on its own database (`pickleball_e2e`, rebuilt from db/init on
 * every run) with the mock payment provider, and both apps pointed at it. They need PostgreSQL up (`pnpm db:up`).
 * Tests share that database and run in parallel, so each one uses its own Court / day / hours.
 */
export default defineConfig({
  testDir: './tests',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['github'], ['list']] : 'list',
  use: {
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    locale: 'en-GB',
    timezoneId: 'Asia/Bangkok',
  },
  projects: [
    { name: 'web', testMatch: /web-.*\.spec\.ts/, use: { ...devices['Desktop Chrome'], baseURL: `http://localhost:${WEB_PORT}` } },
    { name: 'admin', testMatch: /admin-.*\.spec\.ts/, use: { ...devices['Desktop Chrome'], baseURL: `http://localhost:${ADMIN_PORT}` } },
  ],
  webServer: [
    {
      command: 'node e2e/scripts/prepare-db.mjs && dotnet run --project backend/src/PickleballClub.Api --no-launch-profile',
      cwd: '..',
      url: `${API}/health`,
      reuseExistingServer: false,
      timeout: 240_000,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: API,
        ConnectionStrings__Default: `Host=localhost;Port=${DB_PORT};Database=pickleball_e2e;Username=${process.env.E2E_DB_USER ?? 'pickleball'};Password=${process.env.E2E_DB_PASSWORD ?? 'pickleball'}`,
        Cors__Origins__0: `http://localhost:${WEB_PORT}`,
        Cors__Origins__1: `http://localhost:${ADMIN_PORT}`,
        App__WebUrl: `http://localhost:${WEB_PORT}`,
        App__AdminUrl: `http://localhost:${ADMIN_PORT}`,
        // Many sign-ins come from one address in a test run.
        RateLimit__Auth__PermitLimit: '10000',
      },
    },
    {
      command: `pnpm --filter @pbc/web exec quasar dev --port ${WEB_PORT}`,
      cwd: '..',
      url: `http://localhost:${WEB_PORT}`,
      reuseExistingServer: false,
      timeout: 180_000,
      env: { API_MOCK: 'false', API_BASE: API },
    },
    {
      command: `pnpm --filter @pbc/admin exec quasar dev --port ${ADMIN_PORT}`,
      cwd: '..',
      url: `http://localhost:${ADMIN_PORT}`,
      reuseExistingServer: false,
      timeout: 180_000,
      env: { API_MOCK: 'false', API_BASE: API },
    },
  ],
});
