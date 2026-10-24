// Leaderboard for Daily Breach and the Weekly Ladder (Vercel Node function).
//   GET  /api/leaderboard?board=2026-10-24&player=<id>   top 50 + the player's rank (board: a UTC day or an ISO week "2026-W43")
//   POST /api/leaderboard   { mode, board, seed, buildHash, playerId, score, rooms: [{ id, time, tDev, mods, events }] }
// A submitted score is never trusted: it is recomputed from the rooms' event logs with a port of the game's scoring
// (Rules.ScoreTally, Scoring.TimeBonus and the gold medal time; C# floats emulated with Math.fround) and must match exactly.
// Daily: the first submission per player stands (one scored attempt a day). Weekly: the best one stands.
// Storage: Upstash Redis over REST (KV_REST_API_URL/_TOKEN or UPSTASH_REDIS_REST_URL/_TOKEN), one sorted set per board.
// Without those env vars every call answers 503 and the game shows its local best only.
// _test/score-cases.json is shared with the C# tests so both sides score identically: node --test web/api/_test/leaderboard.test.ts

export type Mods = [number, number, number, number, number];   // fromEvent, scoreMult, chainCap, redlineThreshold, grazeCapBonus
export type Ev = [number, string, string | null, number];       // t, type, ice, melt
export type Room = { id: string; time: number; tDev: number; mods: Mods[]; events: Ev[] };
type Submission = { mode: 'daily' | 'weekly'; board: string; playerId: string; score: number; rooms: Room[] };

const f32 = Math.fround;
const PTS: Record<string, number> = { CleanFlip: 50, Graze: 150, Exit: 250, Hack: 120, HackPerfect: 300, NoTraceRoom: 500, RoomClear: 1000, PuppetString: 400 };
const RESETS = ['ChainBreak', 'Detected', 'Death'];
const TYPES = new Set(['Flip', 'Redline', ...RESETS, ...Object.keys(PTS)]);   // Rules.EventType
const GRAZE_CAP = 3, TOP = 50, MAX_BODY = 1_000_000;
const DEFAULTS: Mods = [0, 1, 4, 90, 0];
const DAY = /^\d{4}-\d{2}-\d{2}$/, WEEK = /^\d{4}-W\d{2}$/, PLAYER = /^[0-9a-f]{32}$/, BUILD = /^[\w.-]{1,40}$/, ID = /^[\w-]{1,16}$/;

/** Rules.ScoreTally over one room's log, plus Scoring.TimeBonus(time, tDev * 1.25). */
export function roomScore(r: Room): number {
  let chain = 0, sum = 0, k = 0, m = DEFAULTS;
  const grazes = new Map<string, number>();
  for (let i = 0; i < r.events.length; i++) {
    while (k < r.mods.length && r.mods[k][0] <= i) m = r.mods[k++];
    const [, type, ice, melt] = r.events[i];
    if (RESETS.includes(type)) { chain = 0; continue; }
    const pts = Object.prototype.hasOwnProperty.call(PTS, type) ? PTS[type] : 0;
    if (!pts) continue;                                   // Flip; Redline is informational (its Exit pays)
    if (type === 'CleanFlip') chain++;
    else if (type === 'Graze') {
      const key = ice === null ? '' : '#' + ice, n = grazes.get(key) ?? 0;
      if (n >= GRAZE_CAP + m[4]) continue;
      grazes.set(key, n + 1);
    } else if (type === 'Exit' && f32(melt) < f32(m[3])) continue;
    sum += pts * Math.min(f32(m[2]), f32(1 + f32(0.25 * chain))) * Math.min(8, f32(m[1]));
  }
  const gold = f32(f32(r.tDev) * 1.25);
  return Math.round(sum) + Math.round(Math.max(0, f32(gold - f32(r.time))) * 40);   // sums are >= 0: round = away from zero
}

export const runScore = (rooms: Room[]): number => rooms.reduce((s, r) => s + roomScore(r), 0);

/** Rules.Rng.HashStr: FNV-1a 32-bit over UTF-16 code units. Board seeds are HashStr(board). */
export function fnv1a(s: string): number {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 0x01000193);
  return h >>> 0;
}

export const dayKey = (d: Date): string => d.toISOString().slice(0, 10);

/** ISO-8601 week of a UTC date, "2026-W43" (the week's Thursday decides the year). */
export function weekKey(d: Date): string {
  const t = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate()));
  t.setUTCDate(t.getUTCDate() + 4 - (t.getUTCDay() || 7));
  const y = t.getUTCFullYear();
  const week = Math.floor((t.getTime() - Date.UTC(y, 0, 1)) / 86400000 / 7) + 1;
  return y + '-W' + String(week).padStart(2, '0');
}

const num = (v: unknown, lo: number, hi: number): v is number => typeof v === 'number' && Number.isFinite(v) && v >= lo && v <= hi;
const int = (v: unknown, lo: number, hi: number): v is number => num(v, lo, hi) && Number.isInteger(v);

/** Shape and range checks; the error text, or the submission. The board must be the current (or just finished) day / week. */
export function validate(b: any, now: Date): string | Submission {
  if (!b || typeof b !== 'object') return 'body';
  const daily = b.mode === 'daily';
  if (!daily && b.mode !== 'weekly') return 'mode';
  const boards = daily ? [dayKey(now), dayKey(new Date(now.getTime() - 86400000))]
                       : [weekKey(now), weekKey(new Date(now.getTime() - 7 * 86400000))];
  if (typeof b.board !== 'string' || !boards.includes(b.board)) return 'board';
  if (b.seed !== fnv1a(b.board)) return 'seed';
  if (typeof b.buildHash !== 'string' || !BUILD.test(b.buildHash)) return 'buildHash';
  if (typeof b.playerId !== 'string' || !PLAYER.test(b.playerId)) return 'playerId';
  if (!int(b.score, 0, 1e9)) return 'score';
  if (!Array.isArray(b.rooms) || b.rooms.length < 1 || b.rooms.length > (daily ? 5 : 15)) return 'rooms';
  for (const r of b.rooms) {
    if (!r || typeof r.id !== 'string' || !ID.test(r.id) || !num(r.time, 0, 3600) || !num(r.tDev, 0, 600)) return 'room';
    if (!Array.isArray(r.mods) || r.mods.length > 64 || !Array.isArray(r.events) || r.events.length < 1 || r.events.length > 5000) return 'room';
    let from = 0;
    for (const m of r.mods) {
      if (!Array.isArray(m) || m.length !== 5 || !int(m[0], from, 5000) || !num(m[1], 0.01, 8) || !num(m[3], 80, 90) || !int(m[4], 0, 20)
          || !(m[2] === 4 || (!daily && m[2] === 5))) return 'mods';   // chain cap 5 is Zero-Day, disabled in Daily
      from = m[0];
    }
    let t = 0, clears = 0, noTrace = 0;
    for (const e of r.events) {
      if (!Array.isArray(e) || e.length !== 4 || !num(e[0], t, r.time + 0.01) || !TYPES.has(e[1]) || !num(e[3], 0, 100)
          || !(e[2] === null || (typeof e[2] === 'string' && e[2].length <= 32))) return 'event';
      t = e[0];
      if (e[1] === 'RoomClear') clears++;
      if (e[1] === 'NoTraceRoom') noTrace++;
    }
    if (clears !== 1 || r.events[r.events.length - 1][1] !== 'RoomClear' || noTrace > 1) return 'clear';   // one clear, logged last
  }
  return { mode: b.mode, board: b.board, playerId: b.playerId, score: b.score, rooms: b.rooms };
}

// ---------------------------------------------------------------- storage

function storage(): { url: string; token: string } | null {
  const env = (globalThis as any).process?.env ?? {};
  const url = env.KV_REST_API_URL || env.UPSTASH_REDIS_REST_URL, token = env.KV_REST_API_TOKEN || env.UPSTASH_REDIS_REST_TOKEN;
  return url && token ? { url: String(url).replace(/\/+$/, ''), token } : null;
}

async function redis(s: { url: string; token: string }, cmds: (string | number)[][]): Promise<any[]> {
  const r = await fetch(s.url + '/pipeline', {
    method: 'POST', headers: { Authorization: 'Bearer ' + s.token, 'Content-Type': 'application/json' }, body: JSON.stringify(cmds),
  });
  if (!r.ok) throw new Error('storage ' + r.status);
  return ((await r.json()) as any[]).map((x: any) => { if (x.error) throw new Error(x.error); return x.result; });
}

const reply = (status: number, body: unknown): Response =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' } });

const boardKey = (mode: string, board: string) => 'lb:' + mode + ':' + board;
const rankOf = (r: unknown) => (typeof r === 'number' ? r + 1 : null);

export async function GET(req: Request): Promise<Response> {
  const s = storage();
  if (!s) return reply(503, { error: 'offline' });
  const q = new URL(req.url).searchParams;
  const board = q.get('board') ?? '', player = q.get('player') ?? '';
  const mode = DAY.test(board) ? 'daily' : WEEK.test(board) ? 'weekly' : null;
  if (!mode) return reply(400, { error: 'board' });
  const key = boardKey(mode, board), me = PLAYER.test(player);
  const cmds: (string | number)[][] = [['ZRANGE', key, 0, TOP - 1, 'REV', 'WITHSCORES'], ['ZCARD', key]];
  if (me) cmds.push(['ZREVRANK', key, player], ['ZSCORE', key, player]);
  try {
    const [flat, total, rank, score] = await redis(s, cmds);
    const top = [];
    for (let i = 0; i + 1 < flat.length; i += 2) top.push({ rank: i / 2 + 1, tag: String(flat[i]).slice(0, 6).toUpperCase(), score: Number(flat[i + 1]) });
    return reply(200, { board, mode, total, top, you: me && rankOf(rank) ? { rank: rankOf(rank), score: Number(score) } : null });
  } catch {
    return reply(502, { error: 'storage' });
  }
}

export async function POST(req: Request): Promise<Response> {
  const s = storage();
  if (!s) return reply(503, { error: 'offline' });
  const text = await req.text();
  if (text.length > MAX_BODY) return reply(413, { error: 'too large' });
  let body: unknown;
  try { body = JSON.parse(text); } catch { return reply(400, { error: 'json' }); }
  const sub = validate(body, new Date());
  if (typeof sub === 'string') return reply(400, { error: sub });
  const total = runScore(sub.rooms);
  if (total !== sub.score) return reply(422, { error: 'score mismatch', expected: total });
  const key = boardKey(sub.mode, sub.board);
  try {
    const [added, rank, best] = await redis(s, [
      ['ZADD', key, sub.mode === 'daily' ? 'NX' : 'GT', total, sub.playerId], ['ZREVRANK', key, sub.playerId], ['ZSCORE', key, sub.playerId],
    ]);
    if (sub.mode === 'daily' && added === 0) return reply(409, { error: 'already submitted', rank: rankOf(rank), best: Number(best) });
    return reply(200, { ok: true, score: total, rank: rankOf(rank), best: Number(best) });
  } catch {
    return reply(502, { error: 'storage' });
  }
}
