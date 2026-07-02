import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/verify',
  timeout: 60000,
  use: {
    baseURL: 'http://localhost:5099',
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
    headless: true,
  },
  reporter: 'line',
});
