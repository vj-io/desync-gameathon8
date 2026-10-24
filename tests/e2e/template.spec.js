// The WebGL page template (DESYNC/Assets/WebGLTemplates/Desync/index.html), checked without a build: the analytics bridge the game
// calls through (window.__desyncTrack), the Do Not Track switch, the Vercel script injection rules, and the page text.
import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const template = fs.readFileSync(path.join(here, '../../DESYNC/Assets/WebGLTemplates/Desync/index.html'), 'utf8');

const snippet = (() => {
  const blocks = [...template.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]);
  const b = blocks.find(s => s.includes('__desyncTrack'));
  if (!b) throw new Error('the template has no analytics block (window.__desyncTrack)');
  return b;
})();

const HOST = 'https://desync-test.example';
const page_html = `<!doctype html><html><head><script>
  window.__vaCalls = [];
  window.va = function () { window.__vaCalls.push([].slice.call(arguments)); };   // stand-in for the Vercel queue stub
</script><script>${snippet}</script></head><body></body></html>`;

async function openFake(page, { dnt = false } = {}) {
  const asked = [];
  await page.route(`${HOST}/**`, route => {
    const url = new URL(route.request().url());
    asked.push(url.pathname);
    if (url.pathname === '/') return route.fulfill({ contentType: 'text/html', body: page_html });
    return route.fulfill({ contentType: 'application/javascript', body: '/* insights stub */' });
  });
  if (dnt) await page.addInitScript(() => Object.defineProperty(navigator, 'doNotTrack', { get: () => '1' }));
  await page.goto(HOST + '/');
  return asked;
}

test('analytics: an event is forwarded to window.va as a custom event, and kept for tests', async ({ page }) => {
  await openFake(page);
  await page.evaluate(() => window.__desyncTrack('run_start', '{"mode":"Quick","assist":false,"pid":"0123456789ab"}'));
  const calls = await page.evaluate(() => window.__vaCalls);
  expect(calls).toEqual([['event', { name: 'run_start', data: { mode: 'Quick', assist: false, pid: '0123456789ab' } }]]);
  expect(await page.evaluate(() => window.__desyncEvents)).toEqual([{ name: 'run_start', data: { mode: 'Quick', assist: false, pid: '0123456789ab' } }]);
});

test('analytics: bad JSON is dropped, a throwing va never reaches the game, the log is bounded', async ({ page }) => {
  await openFake(page);
  const out = await page.evaluate(() => {
    window.__desyncTrack('x', 'not json');
    window.va = () => { throw new Error('blocked'); };
    window.__desyncTrack('y', '{"a":1}');   // must not throw
    for (let i = 0; i < 300; i++) window.__desyncTrack('e' + i, '{}');
    return { n: window.__desyncEvents.length, first: window.__desyncEvents[0].name, last: window.__desyncEvents[window.__desyncEvents.length - 1].name };
  });
  expect(out).toEqual({ n: 200, first: 'e100', last: 'e299' });
});

test('analytics: Do Not Track records nothing and loads no analytics script', async ({ page }) => {
  const asked = await openFake(page, { dnt: true });
  await page.evaluate(() => window.__desyncTrack('run_start', '{}'));
  expect(await page.evaluate(() => window.__vaCalls)).toEqual([]);
  expect(await page.evaluate(() => window.__desyncEvents)).toEqual([]);
  expect(asked).not.toContain('/_vercel/insights/script.js');
});

test('analytics: the Vercel script is requested on a real host, not on localhost', async ({ page }) => {
  const asked = await openFake(page);
  await page.waitForFunction(() => !!document.querySelector('script[src="/_vercel/insights/script.js"]'));
  expect(await page.evaluate(() => document.querySelector('script[src="/_vercel/insights/script.js"]').defer)).toBe(true);
  await expect.poll(() => asked.includes('/_vercel/insights/script.js')).toBe(true);

  // the same page on localhost: no script element, so no 404 in the console and no test noise
  await page.route('http://127.0.0.1:9/**', route => route.fulfill({ contentType: 'text/html', body: page_html }));
  await page.goto('http://127.0.0.1:9/');
  expect(await page.evaluate(() => !!document.querySelector('script[src*="_vercel"]'))).toBe(false);
  await page.evaluate(() => window.__desyncTrack('local', '{}'));
  expect((await page.evaluate(() => window.__desyncEvents)).map(e => e.name)).toEqual(['local']);   // still recorded for tests
});

test('page text describes the current game', () => {
  expect(template).not.toMatch(/~8 min/);
  expect(template).toMatch(/<div class="meta">[^<]*BREACH runs[^<]*story mode[^<]*classic rooms[^<]*headphones recommended<\/div>|<div class="meta">[^<]*story mode[^<]*BREACH runs[^<]*classic rooms[^<]*headphones recommended<\/div>/);
  const desc = template.match(/<meta name="description" content="([^"]*)"/)[1];
  expect(desc).toMatch(/BREACH/);
  expect(desc).toMatch(/story/i);
  expect(template).toMatch(/<meta property="og:description" content="[^"]*BREACH/);
  expect(template).toMatch(/<b>TAB<\/b> run map/);
});
