// Browser acceptance tests. BASE_URL points at any deployment (preview or local); without it the local static
// server (serve.mjs, Builds/WebRelease) is started for the run. E2E_GPU=1 uses the machine's GPU instead of software WebGL (the
// frame-bound tests E2E-03 and E2E-05 only mean something there); E2E_RETRIES=n retries a failed test (timing budgets on a loaded machine);
// E2E_BROWSER=firefox|webkit runs another engine (the build's browser matrix; the budgets are only meaningful on a GPU).
import { defineConfig } from '@playwright/test';

const GPU = process.env.E2E_GPU === '1';
const browserName = process.env.E2E_BROWSER || 'chromium';   // chromium | firefox | webkit (the browser flags below are Chromium's)
const port = process.env.PORT || 8080;
const baseURL = process.env.BASE_URL || `http://127.0.0.1:${port}`;

export default defineConfig({
  testDir: '.',
  testMatch: '*.spec.js',
  timeout: 90_000,
  workers: 1,
  retries: Number(process.env.E2E_RETRIES || 0),
  reporter: [['list']],
  use: {
    baseURL,
    headless: true,
    viewport: { width: 1280, height: 720 },
    // software WebGL2 so the build also runs on machines without a GPU; E2E_GPU=1 runs the full Chromium on the real GPU
    // (E2E_ANGLE picks the backend: metal on macOS, d3d11 on Windows, vulkan on Linux)
    browserName,
    channel: GPU && browserName === 'chromium' ? 'chromium' : undefined,
    launchOptions: browserName !== 'chromium' ? {} : { args: GPU
      ? ['--ignore-gpu-blocklist', `--use-angle=${process.env.E2E_ANGLE || 'metal'}`]
      : ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] },
  },
  webServer: process.env.BASE_URL ? undefined : { command: 'node serve.mjs', url: baseURL, reuseExistingServer: true, timeout: 15_000 },
});
