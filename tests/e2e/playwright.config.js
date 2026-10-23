// Browser acceptance tests. BASE_URL points at any deployment (preview or local); without it the local static
// server (serve.mjs, Builds/WebRelease) is started for the run.
import { defineConfig } from '@playwright/test';

const port = process.env.PORT || 8080;
const baseURL = process.env.BASE_URL || `http://127.0.0.1:${port}`;

export default defineConfig({
  testDir: '.',
  testMatch: '*.spec.js',
  timeout: 90_000,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL,
    headless: true,
    viewport: { width: 1280, height: 720 },
    // software WebGL2 so the build also runs on machines without a GPU
    launchOptions: { args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] },
  },
  webServer: process.env.BASE_URL ? undefined : { command: 'node serve.mjs', url: baseURL, reuseExistingServer: true, timeout: 15_000 },
});
