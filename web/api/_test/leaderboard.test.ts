// node --test web/api/_test/leaderboard.test.ts   (Node 23.6+ runs .ts directly)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { GET, POST, dayKey, fnv1a, roomScore, runScore, weekKey } from '../leaderboard.ts';

const fx = JSON.parse(readFileSync(new URL('./score-cases.json', import.meta.url), 'utf8'));

test('shared score cases (the C# tests read the same file)', () => {
  for (const c of fx.cases) {
    assert.deepEqual(c.rooms.map(roomScore), c.roomScores, c.name);
    assert.equal(runScore(c.rooms), c.expected, c.name);
  }
});

test('board seeds and ISO week keys', () => {
  for (const [board, seed] of fx.seeds) assert.equal(fnv1a(board), seed, board);
  for (const [day, week] of fx.weeks) assert.equal(weekKey(new Date(day + 'T12:00:00Z')), week, day);
});

// ---------------------------------------------------------------- the handlers against an in-memory Redis

const env = (globalThis as any).process.env;
const sets = new Map<string, Map<string, number>>();
const set = (k: string) => sets.get(k) ?? sets.set(k, new Map()).get(k)!;
const desc = (k: string) => [...set(k)].sort((a, b) => b[1] - a[1] || (a[0] < b[0] ? 1 : -1));

function fakeRedis(cmd: (string | number)[]): unknown {
  const [op, key] = cmd as string[];
  const s = set(key);
  switch (op) {
    case 'ZADD': {
      const [, , flag, score, member] = cmd, old = s.get(String(member)), v = Number(score);
      if (old === undefined) { s.set(String(member), v); return 1; }
      if (flag === 'GT' && v > old) s.set(String(member), v);
      return 0;
    }
    case 'ZREVRANK': { const i = desc(key).findIndex(([m]) => m === cmd[2]); return i < 0 ? null : i; }
    case 'ZSCORE': { const v = s.get(String(cmd[2])); return v === undefined ? null : String(v); }
    case 'ZCARD': return s.size;
    case 'ZRANGE': return desc(key).slice(Number(cmd[2]), Number(cmd[3]) + 1).flatMap(([m, v]) => [m, String(v)]);
  }
  throw new Error('unexpected ' + op);
}

(globalThis as any).fetch = async (url: string, init: { body: string; headers: Record<string, string> }) => {
  assert.equal(url, 'https://kv.test/pipeline');
  assert.equal(init.headers.Authorization, 'Bearer t0ken');
  return new Response(JSON.stringify(JSON.parse(init.body).map((c: (string | number)[]) => ({ result: fakeRedis(c) }))));
};

const today = dayKey(new Date());
const player = (n: number) => n.toString(16).padStart(32, '0');
const runOf = (c: any, extra: object = {}) =>
  ({ mode: 'daily', board: today, seed: fnv1a(today), buildHash: 'abc1234', playerId: player(1), score: c.expected, rooms: c.rooms, ...extra });
const post = (body: object) => POST(new Request('https://x/api/leaderboard', { method: 'POST', body: JSON.stringify(body) }));
const get = (q: string) => GET(new Request('https://x/api/leaderboard?' + q));

test('503 without storage', async () => {
  delete env.KV_REST_API_URL; delete env.KV_REST_API_TOKEN; delete env.UPSTASH_REDIS_REST_URL; delete env.UPSTASH_REDIS_REST_TOKEN;
  assert.equal((await get('board=' + today)).status, 503);
  assert.equal((await post(runOf(fx.cases[0]))).status, 503);
});

test('submit: recomputed or rejected, first daily stands, weekly keeps the best, board + rank', async () => {
  env.UPSTASH_REDIS_REST_URL = 'https://kv.test/'; env.UPSTASH_REDIS_REST_TOKEN = 't0ken';
  const [a, b] = fx.cases;

  const lie = await post(runOf(a, { score: a.expected + 1 }));
  assert.equal(lie.status, 422);
  assert.equal((await lie.json()).expected, a.expected);
  assert.equal((await post(runOf(a, { seed: 1 }))).status, 400, 'seed is the board hash');
  assert.equal((await post(runOf(a, { board: '2001-01-01', seed: fnv1a('2001-01-01') }))).status, 400, 'only the current day');
  const doubled = structuredClone(a.rooms);
  doubled[0].events.push([20, 'RoomClear', null, 0]);
  assert.equal((await post(runOf(a, { rooms: doubled, score: a.expected + 2000 }))).status, 400, 'one RoomClear per room');
  const zeroDay = structuredClone(a.rooms);
  zeroDay[0].mods = [[0, 1, 5, 90, 0]];
  assert.equal((await post(runOf(a, { rooms: zeroDay }))).status, 400, 'no Zero-Day cap in Daily');

  const first = await post(runOf(b));
  assert.equal(first.status, 200);
  assert.deepEqual(await first.json(), { ok: true, score: b.expected, rank: 1, best: b.expected });
  assert.equal((await post(runOf(a))).status, 409, 'one scored attempt a day');
  assert.equal((await post(runOf(a, { playerId: player(2) }))).status, 200);

  const board = await (await get('board=' + today + '&player=' + player(1))).json();
  assert.equal(board.total, 2);
  assert.deepEqual(board.top.map((r: any) => r.score), [a.expected, b.expected]);
  assert.deepEqual(board.you, { rank: 2, score: b.expected });
  assert.equal(board.top[0].tag, '000000');

  const week = weekKey(new Date()), weekly = (c: any) => runOf(c, { mode: 'weekly', board: week, seed: fnv1a(week) });
  assert.equal((await post(weekly(b))).status, 200);
  assert.equal((await (await post(weekly(a))).json()).best, a.expected, 'a better weekly run replaces the old one');
  assert.equal((await (await post(weekly(b))).json()).best, a.expected, 'a worse one does not');
  assert.equal((await get('board=nope')).status, 400);
});
