// E2E-01..03 from the spec, against the page hook the build exposes:
//   window.__DESYNC__ = { ready, state: 'title'|'playing'|'paused'|'recap'|'results'|'ending',
//                         metrics: { lastRestartMs, fps }, run: { room, seed }, debug: { kill(cause), cmd(c), state() } }
// The hook refreshes ~4x a second, so every check polls instead of reading once.
import { test, expect } from '@playwright/test';

const READY_BUDGET_MS = 8000;
const RESTART_BUDGET_MS = 300;

/** Count pointer-lock requests, open the page, wait for the hook; resolves with the time to ready in ms. */
async function openGame(page) {
  await page.addInitScript(() => {
    window.__lockRequests = 0;
    const real = Element.prototype.requestPointerLock;
    Element.prototype.requestPointerLock = function (...args) { window.__lockRequests++; return real.apply(this, args); };
  });
  const t0 = Date.now();
  await page.goto('/', { waitUntil: 'commit' });
  await page.waitForFunction(() => window.__DESYNC__ && window.__DESYNC__.ready === true, null, { timeout: 45_000, polling: 100 });
  return Date.now() - t0;
}

/** Click the middle of the canvas (the title accepts a click anywhere) and wait for the first room. */
async function jackIn(page) {
  const box = await page.locator('#unity-canvas').boundingBox();
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
  await page.waitForFunction(() => window.__DESYNC__.state === 'playing', null, { timeout: 30_000, polling: 100 });
}

test('E2E-01 the game is ready within 8 s', async ({ page }) => {
  const ms = await openGame(page);
  console.log(`ready after ${ms} ms`);
  expect(ms).toBeLessThanOrEqual(READY_BUDGET_MS);
  expect(await page.evaluate(() => window.__DESYNC__.state)).toBe('title');
});

test('E2E-02 a click requests pointer lock and starts the run', async ({ page }) => {
  await openGame(page);
  expect(await page.evaluate(() => window.__lockRequests)).toBe(0);
  await jackIn(page);
  expect(await page.evaluate(() => window.__lockRequests)).toBeGreaterThan(0);
  expect(await page.evaluate(() => window.__DESYNC__.state)).toBe('playing');
});

test('E2E-03 restart after a death takes under 300 ms', async ({ page }) => {
  await openGame(page);
  await jackIn(page);
  await page.waitForTimeout(500);   // let the first room finish spawning the player
  await page.evaluate(() => window.__DESYNC__.debug.kill('melt'));
  await page.waitForFunction(() => window.__DESYNC__.state === 'recap', null, { timeout: 10_000, polling: 50 });
  await page.keyboard.press('r');
  await page.waitForFunction(() => window.__DESYNC__.state === 'playing' && window.__DESYNC__.metrics.lastRestartMs > 0, null, { timeout: 10_000, polling: 100 });
  const ms = await page.evaluate(() => window.__DESYNC__.metrics.lastRestartMs);
  console.log(`lastRestartMs = ${ms}`);
  expect(ms).toBeLessThan(RESTART_BUDGET_MS);
});
