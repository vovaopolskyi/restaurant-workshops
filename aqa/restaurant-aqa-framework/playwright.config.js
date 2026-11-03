import { defineConfig, devices } from '@playwright/test';
import { loadEnv } from './support/env.js';

loadEnv();
export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  // Each retry gets a new reservation and independent browser context.
  retries: process.env.CI ? 1 : 0,
  workers: process.env.CI ? 2 : undefined,
  timeout: 120_000,
  expect: { timeout: 20_000 },
  globalTimeout: 30 * 60_000,
  maxFailures: 0,
  reporter: [['list'], ['html', { open: 'never' }], ['junit', { outputFile: 'test-results/results.xml' }]],
  use: {
    baseURL: process.env.BASE_URL || undefined,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    actionTimeout: 20_000,
    navigationTimeout: 30_000,
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], channel: process.env.BROWSER_CHANNEL || undefined } }],
});
