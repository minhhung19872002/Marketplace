const { defineConfig } = require('@playwright/test');

module.exports = defineConfig({
  testDir: '.',
  testMatch: 'e2e.spec.cjs',
  use: {
    baseURL: 'http://localhost:5173',
    headless: true,
    viewport: { width: 1280, height: 720 },
    ignoreHTTPSErrors: true,
  },
  webServer: {
    command: 'npm run dev',
    port: 5173,
    timeout: 60000,
    reuseExistingServer: true,
  },
  timeout: 30000,
  retries: 1,
});
