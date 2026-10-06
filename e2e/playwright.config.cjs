const { defineConfig } = require('@playwright/test');

const BASE_URL = process.env.SH_E2E_BASE_URL || 'http://localhost:5173';

module.exports = defineConfig({
  testDir: '.',
  testMatch: ['*.spec.cjs', '*.spec.ts'],
  globalSetup: require.resolve('./global-setup.cjs'),
  use: {
    baseURL: BASE_URL,
    storageState: require('./global-setup.cjs').STATE,
    headless: true,
    viewport: { width: 1280, height: 720 },
    ignoreHTTPSErrors: true,
  },
  // Start the buyer site dev server unless a running stack URL is given
  webServer: process.env.SH_E2E_BASE_URL
    ? undefined
    : {
        command: 'npm run dev',
        cwd: '../web',
        port: 5173,
        timeout: 60000,
        reuseExistingServer: true,
      },
  timeout: 30000,
  retries: 1,
  // On the Docker stack every request goes through the gateway's production rate limit (30 req/s per IP):
  // two workers stay under it, more would turn the suite into a load test
  workers: process.env.SH_E2E_BASE_URL ? 2 : undefined,
});
