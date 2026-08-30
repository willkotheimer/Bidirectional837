import { defineConfig, devices } from '@playwright/test';

/**
 * Screenshot capture, pointed at the deployed application by default.
 *
 * A wide viewport on purpose: the claim table carries nine governed columns and the point of the
 * shot is that a reader can see them. Set CAPTURE_BASE_URL to capture a local build instead.
 */
export default defineConfig({
  testDir: '.',
  fullyParallel: false,
  workers: 1,
  timeout: 120_000,
  use: {
    baseURL: process.env.CAPTURE_BASE_URL ?? 'https://bidirectional837.azurewebsites.net',
    ...devices['Desktop Chrome'],
    viewport: { width: 1500, height: 1000 },
    deviceScaleFactor: 2,
    acceptDownloads: true,
  },
});
