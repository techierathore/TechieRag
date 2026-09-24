import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './tests/verify',
  outputDir: './tests/.artifacts/test-results',
  reporter: 'line',
  use: { baseURL: process.env.BASE_URL, headless: true, screenshot: 'only-on-failure', trace: 'retain-on-failure' },
});
