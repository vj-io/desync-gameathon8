// Browser acceptance tests E2E-01..05 from the spec, against the page hook the build exposes:
//   window.__DESYNC__ = { ready, state: 'title'|'playing'|'paused'|'recap'|'results'|'ending',
//                         metrics: { lastRestartMs, fps }, run: { room, seed }, debug: { kill(cause), cmd(c), state() },
//                         autoplay: { state: 'idle'|'running'|'done'|'failed', phase, rooms, won, act } }   // autoplay: ?autoplay=1 only
// The hook refreshes ~4x a second, so every check polls instead of reading once. The page also keeps the analytics events it handed
// to Vercel Web Analytics in window.__desyncEvents (a test hook, last 200).
//
// E2E-01 ready <= 8 s | E2E-02 click requests pointer lock, state 'playing' | E2E-03 restart after a death < 300 ms
// E2E-04 ?autoplay=1&seed=2026&timescale=4&act=1 clears Act 1 | E2E-05 bench: p95 frame < 20 ms, draw calls <= 150
//
// Environment: BASE_URL (test a deployment instead of the local build), E2E_STRICT=1 (a build without the autoplay hook or the
// analytics hook FAILS instead of skipping), E2E_READY_MS / E2E_AUTOPLAY_MS (budgets), E2E_BENCH=1 (run the frame-bound tests E2E-03 and
// E2E-05 even on software GL; they skip themselves there because a software rasteriser cannot hold a frame budget).
import { test, expect } from '@playwright/test';

const READY_BUDGET_MS = Number(process.env.E2E_READY_MS || 8000);
const RESTART_BUDGET_MS = 300;
const AUTOPLAY_BUDGET_MS = Number(process.env.E2E_AUTOPLAY_MS || 240_000);
const FRAME_P95_BUDGET_MS = 20;
const DRAWS_BUDGET = 150;
const STRICT = !!process.env.E2E_STRICT;

// "SKIP · JUST PLAY" on the first-time story prompt (1920x1080 design: 285 px right of centre, 60 px below it).
const STORY_SKIP = { x: 0.5 + 285 / 1920, y: 0.5 + 60 / 1080 };

/** Count pointer-lock requests, open the page, wait for the hook; resolves with the time to ready in ms. */
async function openGame(page, query = '') {
  await page.addInitScript(() => {
    window.__lockRequests = 0;
    const real = Element.prototype.requestPointerLock;
    Element.prototype.requestPointerLock = function (...args) { window.__lockRequests++; return real.apply(this, args); };
  });
  const t0 = Date.now();
  await page.goto('/' + query, { waitUntil: 'commit' });
  await page.waitForFunction(() => window.__DESYNC__ && window.__DESYNC__.ready === true, null, { timeout: 45_000, polling: 100 });
  return Date.now() - t0;
}

/** The WebGL renderer string; software rasterisers (SwiftShader, llvmpipe) run this game at 2-5 fps, so frame-bound budgets cannot hold. */
async function glRenderer(page) {
  return page.evaluate(() => {
    const gl = document.createElement('canvas').getContext('webgl2');
    const e = gl && gl.getExtension('WEBGL_debug_renderer_info');
    return e ? String(gl.getParameter(e.UNMASKED_RENDERER_WEBGL)) : 'unknown';
  });
}
const isSoftware = r => /swiftshader|llvmpipe|software/i.test(r) && !process.env.E2E_BENCH;

/**
 * Click a point of the canvas given as fractions of its size. The button is held for 120 ms like a person's click: Unity asks the
 * browser for pointer lock on the mouse-up that follows the click it acted on, which a zero-length synthetic click does not give it.
 */
async function clickAt(page, fx, fy) {
  const box = await page.locator('#unity-canvas').boundingBox();
  await page.mouse.click(box.x + box.width * fx, box.y + box.height * fy, { delay: 120 });
}

/**
 * Start the first classic room. A fresh browser profile meets the first-time story prompt over the title: answer it with SKIP (the
 * click that lands there starts nothing), then click the title (any click starts the highlighted mode, CLASSIC).
 */
async function jackIn(page) {
  await clickAt(page, STORY_SKIP.x, STORY_SKIP.y);
  await page.waitForTimeout(700);
  if (await page.evaluate(() => window.__DESYNC__.state) !== 'playing') await clickAt(page, 0.5, 0.5);
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
  const renderer = await glRenderer(page);
  test.skip(isSoftware(renderer), `software WebGL (${renderer}) cannot return control inside 300 ms: the restart is bound by the frame time. Run with E2E_GPU=1 or force with E2E_BENCH=1`);
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

test('E2E-04 autoplay clears Act 1 on seed 2026', async ({ page }) => {
  test.setTimeout(AUTOPLAY_BUDGET_MS + 90_000);
  const errors = [];
  page.on('pageerror', e => errors.push(String(e)));
  await openGame(page, '?autoplay=1&seed=2026&timescale=4&act=1');
  // the hook appears with the first status the build publishes, a moment after the title is up
  const hooked = await page.waitForFunction(() => !!window.__DESYNC__.autoplay, null, { timeout: 30_000, polling: 250 }).then(() => true, () => false);
  if (!hooked) {
    if (STRICT) throw new Error('this build publishes no window.__DESYNC__.autoplay: it lacks the autoplay hook');
    test.skip(true, 'this build has no autoplay hook (built before the launch pass): rebuild, then run again');
  }
  const t0 = Date.now();
  await page.waitForFunction(() => ['done', 'failed'].includes(window.__DESYNC__.autoplay.state), null, { timeout: AUTOPLAY_BUDGET_MS, polling: 500 });
  const a = await page.evaluate(() => window.__DESYNC__.autoplay);
  const seed = a.seed;   // the run seed (window.__DESYNC__.run.seed is the current room's derived seed)
  console.log(`autoplay ${a.state} after ${Math.round((Date.now() - t0) / 1000)} s: rooms=${a.rooms} won=${a.won} act=${a.act} seed=${seed}`);
  expect(a.state).toBe('done');
  expect(a.won).toBe(true);
  expect(a.rooms).toBeGreaterThanOrEqual(2);
  expect(seed).toBe(2026);
  expect(errors).toEqual([]);
});

test('E2E-05 bench: p95 frame under 20 ms and draw calls within budget', async ({ page }) => {
  test.setTimeout(150_000);
  // Count frames and WebGL draw calls from inside the page, before any game code runs.
  await page.addInitScript(() => {
    const m = window.__bench = { on: false, frames: [], draws: [], count: 0, stamp: 0 };
    for (const C of [window.WebGL2RenderingContext, window.WebGLRenderingContext]) {
      if (!C) continue;
      for (const fn of ['drawArrays', 'drawElements', 'drawArraysInstanced', 'drawElementsInstanced', 'drawRangeElements']) {
        const orig = C.prototype[fn];
        if (!orig) continue;
        C.prototype[fn] = function (...a) { m.count++; return orig.apply(this, a); };
      }
    }
    // A frame is one animation-frame timestamp (the page may register several callbacks for it): draws are counted between two of them.
    const raf = window.requestAnimationFrame.bind(window);
    window.requestAnimationFrame = cb => raf(t => {
      if (t !== m.stamp) {
        if (m.on && m.stamp) { m.frames.push(t - m.stamp); m.draws.push(m.count); }
        m.stamp = t; m.count = 0;
      }
      cb(t);
    });
  });
  await openGame(page);
  const renderer = await glRenderer(page);
  console.log(`renderer: ${renderer}`);
  test.skip(isSoftware(renderer), 'software WebGL cannot hold a frame budget: run on a real GPU (E2E_GPU=1 tools/e2e.sh) or force with E2E_BENCH=1');
  await jackIn(page);
  await page.waitForTimeout(4000);   // warm-up: first-room shaders and allocations
  await page.evaluate(() => { const m = window.__bench; m.frames = []; m.draws = []; m.stamp = 0; m.on = true; });
  await page.waitForTimeout(8000);
  const { frames, draws } = await page.evaluate(() => { window.__bench.on = false; return { frames: window.__bench.frames, draws: window.__bench.draws }; });
  expect(frames.length).toBeGreaterThan(60);
  const pct = (a, p) => [...a].sort((x, y) => x - y)[Math.min(a.length - 1, Math.floor(a.length * p))];
  const p95 = pct(frames, 0.95), draws95 = pct(draws, 0.95);
  console.log(`bench: ${frames.length} frames, frame p50 ${pct(frames, 0.5).toFixed(1)} ms, p95 ${p95.toFixed(1)} ms, max ${Math.max(...frames).toFixed(1)} ms; draw calls p50 ${pct(draws, 0.5)}, p95 ${draws95}, max ${Math.max(...draws)}`);
  expect(p95).toBeLessThan(FRAME_P95_BUDGET_MS);
  expect(draws95).toBeLessThanOrEqual(DRAWS_BUDGET);
});

test('E2E-A1 analytics events reach the page hook', async ({ page }) => {
  await openGame(page);
  const hooked = await page.evaluate(() => Array.isArray(window.__desyncEvents));
  if (!hooked) {
    if (STRICT) throw new Error('this page has no window.__desyncEvents: it lacks the analytics hook');
    test.skip(true, 'this build predates the analytics hook');
  }
  await page.waitForFunction(() => window.__desyncEvents.some(e => e.name === 'game_loaded'), null, { timeout: 15_000, polling: 250 });
  const events = await page.evaluate(() => window.__desyncEvents);
  const loaded = events.find(e => e.name === 'game_loaded');
  expect(loaded.data.build).toBeTruthy();
  expect(loaded.data.pid).toMatch(/^[0-9a-f]{12}$/);                         // hashed profile id, never the raw GUID
  expect(JSON.stringify(events)).not.toMatch(/[0-9a-f]{32}/);                // no raw 32-digit profile id anywhere
  for (const e of events) for (const v of Object.values(e.data)) expect(['string', 'number', 'boolean']).toContain(typeof v);   // flat props only
});
