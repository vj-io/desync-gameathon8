using System;
using System.Collections.Generic;
using Desync.Rules;

namespace Desync.Gen
{
    /// Room generation (spec F2): Spelunky-style solution path over the act grid, socket-matched chunks (any of 8
    /// orientations), room mirror, spawn/exit/terminals, budgeted threats on chunk markers, then RoomCheck.Validate.
    /// A rejected room is rebuilt from hash(seed, attempt); after 20 failed attempts it falls back to an original room.
    internal static class RoomGen
    {
        internal const int N = 16, DoorLo = 6, DoorHi = 9, Attempts = 20;
        const int Free = -1, Wall = (int)SocketKind.Wall, Door = (int)SocketKind.Door, Src = (int)SocketKind.SourceOnly;
        static readonly int[] DR = { -1, 0, 1, 0 }, DC = { 0, 1, 0, -1 };   // N E S W

        struct Threat
        {
            public EntityKind kind; public char marker; public int cost, act, node; public string prefix; public bool enemy;
            public Threat(EntityKind kind, char marker, int cost, int act, int node, string prefix, bool enemy = true)
            { this.kind = kind; this.marker = marker; this.cost = cost; this.act = act; this.node = node; this.prefix = prefix; this.enemy = enemy; }
        }

        /// Costs from the spec; (act, node) = first room the threat may appear in (enemy intro table, r1 = node 0).
        /// Glitch zones are a hazard, not an enemy: first met in the AUDITOR fight, placed from Act 2 on.
        static readonly Threat[] Threats =
        {
            new Threat(EntityKind.CameraFixed, 'C', 2, 1, 0, "CAM"),
            new Threat(EntityKind.CameraSweep, 'C', 3, 1, 1, "CAM"),
            new Threat(EntityKind.Drone,       'P', 4, 1, 2, "DRN"),
            new Threat(EntityKind.IceVent,     'I', 4, 1, 0, "ICE"),
            new Threat(EntityKind.TrackerIce,  'I', 5, 1, 3, "ICE"),
            new Threat(EntityKind.Lantern,     'P', 4, 2, 0, "DRN"),
            new Threat(EntityKind.Sentinel,    'I', 4, 2, 1, "ICE"),
            new Threat(EntityKind.Splitter,    'I', 5, 2, 3, "ICE"),
            new Threat(EntityKind.Glitch,      'G', 2, 2, 0, "GLT", false),
            new Threat(EntityKind.Daemon,      'I', 7, 3, 0, "DMN"),
            new Threat(EntityKind.Watcher,     'C', 6, 3, 1, "EYE"),
            new Threat(EntityKind.Guard,       'P', 4, 1, 2, "GRD"),   // HALCYON officer: walks a patrol loop like a drone
        };

        /// Guards: at most 2 per room (3 in act 3), every waypoint >= GuardSpawnDist m from the spawn, the loop never through
        /// a terminal's hacking spot (its tile and the 8 around it).
        internal const float GuardSpawnDist = 6f;
        internal static int MaxGuards(int act) => act >= 3 ? 3 : 2;

        /// Attempts used by the last GenerateRoom on this thread (tests, tuning).
        [ThreadStatic] internal static int LastAttempts;

        internal static int Budget(int act, int node, int heat) => 10 + 6 * act + 2 * node + heat / 2;

        internal static RoomSpec Generate(uint seed, int act, int node, int heat, ChunkLibrary lib, HeatPlacement placement)
        {
            act = Math.Max(1, Math.Min(3, act));
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                uint s = attempt == 0 ? seed : Rng.Substream(seed, "attempt" + attempt).NextUInt();
                var room = new Builder(lib, act, node, heat, s, placement).Build();
                if (room == null) continue;
                room.seed = seed;
                var v = RoomCheck.Validate(room);
                if (v.reasons.Count > 0) continue;
                room.par = Par(v.bestPathSeconds, act);
                LastAttempts = attempt + 1;
                return room;
            }
            LastAttempts = Attempts;
            int pick = (act - 1) * 2 + 1 + (int)(seed & 1);
            return new RoomSpec { seed = seed, act = act, node = node, heat = heat, par = 60f, fallbackRoom = "Room_0" + pick };
        }

        /// best route x 1.6 rounded to 5 s; at least 45 s; at most 75 s in acts 1-2 (act 3 rooms are larger).
        internal static float Par(float best, int act)
        {
            float par = (float)Math.Round(best * 1.6f / 5f, MidpointRounding.AwayFromZero) * 5f;
            par = Math.Max(45f, par);
            return act <= 2 ? Math.Min(75f, par) : par;
        }

        /// Sockets [N,E,S,W] of a chunk placed with orientation o (bit 2 = mirror east-west first, bits 0-1 = clockwise quarter turns).
        internal static void Sockets(Chunk c, int o, int[] s)
        {
            s[0] = (int)c.n; s[1] = (int)c.e; s[2] = (int)c.s; s[3] = (int)c.w;
            if (o >= 4) { int t = s[1]; s[1] = s[3]; s[3] = t; }
            for (int k = 0; k < (o & 3); k++) { int t = s[3]; s[3] = s[2]; s[2] = s[1]; s[1] = s[0]; s[0] = t; }
        }

        /// Where source tile (r, c) of a chunk lands under orientation o.
        internal static void Map(int r, int c, int o, out int rr, out int cc)
        {
            if (o >= 4) c = N - 1 - c;
            for (int k = 0; k < (o & 3); k++) { int t = r; r = c; c = N - 1 - t; }
            rr = r; cc = c;
        }

        sealed class Builder
        {
            readonly ChunkLibrary lib; readonly int act, node, heat; readonly uint seed;
            readonly HeatPlacement hp;
            readonly int gw, gh, cells, W, H;
            readonly List<int> pool = new List<int>();
            bool[] sideOnly, hasT;
            int[] req, chunkOf, orientOf, sock, uses, pathIdx, order;
            List<int> path;
            int termCell, steps;
            char[] g;
            Mulberry32 rng;
            readonly int[] tmp = new int[4], tmp2 = new int[4];

            internal Builder(ChunkLibrary lib, int act, int node, int heat, uint seed, HeatPlacement hp)
            {
                this.lib = lib; this.act = act; this.node = node; this.heat = heat; this.seed = seed; this.hp = hp;
                gw = act == 1 ? 3 : 4; gh = act == 3 ? 4 : 3; cells = gw * gh; W = gw * N; H = gh * N;
            }

            int Neighbour(int cell, int d)
            {
                int r = cell / gw + DR[d], c = cell % gw + DC[d];
                return r < 0 || r >= gh || c < 0 || c >= gw ? -1 : r * gw + c;
            }

            internal RoomSpec Build()
            {
                if (lib == null) return null;
                sideOnly = new bool[lib.chunks.Count]; hasT = new bool[lib.chunks.Count];
                for (int i = 0; i < lib.chunks.Count; i++)
                {
                    var c = lib.chunks[i];
                    if (c?.tiles == null || c.tiles.Length != N || (c.act != act && c.act != 0)) continue;
                    sideOnly[i] = c.tags != null && c.tags.Contains("side");
                    foreach (var row in c.tiles) hasT[i] |= row.IndexOf('T') >= 0;
                    pool.Add(i);
                }
                if (pool.Count == 0 || !MakePath()) return null;

                rng = Rng.Substream(seed, "chunks");
                chunkOf = new int[cells]; orientOf = new int[cells]; sock = new int[cells * 4]; uses = new int[lib.chunks.Count];
                for (int k = 0; k < cells; k++) chunkOf[k] = -1;
                if (!Place(0)) return null;
                DropIsolated();
                Stamp();

                bool mirror = Rng.Substream(seed, "mirror").NextDouble() < 0.5;
                if (mirror) Mirror();
                var room = new RoomSpec { act = act, node = node, heat = heat, mirror = mirror, gridW = gw, gridH = gh };
                room.chunks = new string[gh][];
                for (int r = 0; r < gh; r++)
                {
                    room.chunks[r] = new string[gw];
                    for (int c = 0; c < gw; c++) { int k = chunkOf[r * gw + c]; room.chunks[r][c] = k < 0 ? null : lib.chunks[k].id; }
                }
                if (!Populate(room)) return null;
                room.tiles = new string[H];
                for (int r = 0; r < H; r++) room.tiles[r] = new string(g, r * W, W);
                return room;
            }

            // ---------- solution path ----------

            bool MakePath()
            {
                var r = Rng.Substream(seed, "path");
                req = new int[cells * 4];
                for (int tries = 0; tries < 16; tries++)
                {
                    path = Walk(r);
                    for (int i = 0; i < req.Length; i++) req[i] = Free;
                    for (int i = 1; i < path.Count; i++) Require(path[i - 1], path[i], Door);
                    if (act >= 2)
                    {
                        // SOURCE-only shortcut: a source_only edge between two path cells that are adjacent but >= 3 steps apart
                        var pairs = new List<int>();
                        for (int i = 0; i < path.Count; i++)
                            for (int j = i + 3; j < path.Count; j++)
                                if (Dir(path[i], path[j]) >= 0) pairs.Add(i * 64 + j);
                        if (pairs.Count == 0) continue;
                        int p = pairs[r.Range(0, pairs.Count)];
                        Require(path[p / 64], path[p % 64], Src);
                    }
                    pathIdx = new int[cells];
                    for (int k = 0; k < cells; k++) pathIdx[k] = -1;
                    for (int i = 0; i < path.Count; i++) pathIdx[path[i]] = i;
                    // fail fast: every cell needs a chunk for its fixed sockets, and one path cell a chunk with a terminal
                    bool ok = true;
                    for (int k = 0; k < cells && ok; k++) ok = Feasible(k, false);
                    var withT = new List<int>();
                    for (int i = 1; i < path.Count; i++) if (Feasible(path[i], true)) withT.Add(path[i]);
                    if (!ok || withT.Count == 0) continue;
                    termCell = withT[r.Range(0, withT.Count)];
                    order = new int[cells];
                    int n = 0;
                    foreach (int k in path) order[n++] = k;
                    for (int k = 0; k < cells; k++) if (pathIdx[k] < 0) order[n++] = k;
                    return true;
                }
                return false;
            }

            /// Start on row 0; left/right p 0.4 each, down 0.2; a blocked sideways step drops down; ends on the last row.
            List<int> Walk(Mulberry32 r)
            {
                var p = new List<int>();
                var visited = new bool[cells];
                int row = 0, col = r.Range(0, gw);
                p.Add(col); visited[col] = true;
                while (true)
                {
                    double x = r.NextDouble();
                    int dir = x < 0.4 ? -1 : x < 0.8 ? 1 : 0, nc = col + dir;
                    if (dir != 0 && nc >= 0 && nc < gw && !visited[row * gw + nc]) col = nc;
                    else if (row == gh - 1) break;
                    else row++;
                    p.Add(row * gw + col); visited[row * gw + col] = true;
                }
                return p;
            }

            int Dir(int a, int b)
            {
                for (int d = 0; d < 4; d++) if (Neighbour(a, d) == b) return d;
                return -1;
            }

            bool Feasible(int k, bool needT)
            {
                foreach (int ci in pool)
                {
                    if ((pathIdx[k] >= 0 && sideOnly[ci]) || (needT && !hasT[ci])) continue;
                    for (int o = 0; o < 8; o++)
                    {
                        Sockets(lib.chunks[ci], o, tmp);
                        bool fits = true;
                        for (int d = 0; d < 4 && fits; d++)
                            fits = Neighbour(k, d) < 0 || req[k * 4 + d] == Free || req[k * 4 + d] == tmp[d];
                        if (fits) return true;
                    }
                }
                return false;
            }

            void Require(int a, int b, int kind)
            {
                int d = Dir(a, b);
                req[a * 4 + d] = kind; req[b * 4 + (d + 2) % 4] = kind;
            }

            // ---------- chunk placement (backtracking; path cells first, then the rest row-major) ----------

            bool Place(int i)
            {
                if (i == cells) return true;
                if (++steps > 2000) return false;
                int k = order[i];
                var cands = Candidates(k);
                foreach (var cand in cands)
                {
                    int ci = cand.Item3 >> 3, o = cand.Item3 & 7;
                    chunkOf[k] = ci; orientOf[k] = o;
                    Sockets(lib.chunks[ci], o, tmp);
                    for (int d = 0; d < 4; d++) sock[k * 4 + d] = tmp[d];
                    uses[ci]++;
                    bool live = true;   // forward check: every open neighbour must still have a chunk that fits
                    for (int d = 0; d < 4 && live; d++)
                    {
                        int nb = Neighbour(k, d);
                        live = nb < 0 || chunkOf[nb] >= 0 || HasCandidate(nb);
                    }
                    if (live && Place(i + 1)) return true;
                    uses[ci]--;
                }
                chunkOf[k] = -1;
                return false;
            }

            List<(int, uint, int)> Candidates(int k)
            {
                var list = new List<(int, uint, int)>();
                bool onPath = pathIdx[k] >= 0;
                foreach (int ci in pool)
                {
                    if (onPath && sideOnly[ci]) continue;
                    if (k == termCell && !hasT[ci]) continue;
                    var c = lib.chunks[ci];
                    for (int o = 0; o < 8; o++)
                    {
                        Sockets(c, o, tmp);
                        if (!Fits(k, tmp, out bool opens)) continue;
                        // prefer act chunks over shared connectors, unused chunks, and side cells that open onto a neighbour
                        int pri = uses[ci] * 2 + (c.act == 0 ? 3 : 0) + (!onPath && !opens ? 4 : 0);
                        list.Add((pri, rng.NextUInt(), ci * 8 + o));
                    }
                }
                list.Sort((a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item2 != b.Item2 ? a.Item2.CompareTo(b.Item2) : a.Item3.CompareTo(b.Item3));
                return list;
            }

            bool HasCandidate(int k)
            {
                var s = tmp2;
                foreach (int ci in pool)
                {
                    if ((pathIdx[k] >= 0 && sideOnly[ci]) || (k == termCell && !hasT[ci])) continue;
                    for (int o = 0; o < 8; o++)
                    {
                        Sockets(lib.chunks[ci], o, s);
                        if (Fits(k, s, out _)) return true;
                    }
                }
                return false;
            }

            bool Fits(int k, int[] s, out bool opens)
            {
                opens = false;
                for (int d = 0; d < 4; d++)
                {
                    int nb = Neighbour(k, d);
                    if (nb < 0) continue;
                    int rq = req[k * 4 + d];
                    if (rq != Free) { if (s[d] != rq) return false; }
                    else if (chunkOf[nb] >= 0 && s[d] != sock[nb * 4 + (d + 2) % 4]) return false;
                    opens |= s[d] != Wall;
                }
                return true;
            }

            /// Off-path cells that no door or source_only edge connects to the path become void.
            void DropIsolated()
            {
                var reached = new bool[cells];
                var stack = new Stack<int>();
                stack.Push(path[0]); reached[path[0]] = true;
                while (stack.Count > 0)
                {
                    int k = stack.Pop();
                    for (int d = 0; d < 4; d++)
                    {
                        int nb = Neighbour(k, d);
                        if (nb >= 0 && !reached[nb] && sock[k * 4 + d] != Wall) { reached[nb] = true; stack.Push(nb); }
                    }
                }
                for (int k = 0; k < cells; k++) if (!reached[k]) chunkOf[k] = -1;
            }

            // ---------- tiles ----------

            void Stamp()
            {
                g = new char[W * H];
                for (int i = 0; i < g.Length; i++) g[i] = ' ';
                for (int k = 0; k < cells; k++)
                {
                    if (chunkOf[k] < 0) continue;
                    var t = lib.chunks[chunkOf[k]].tiles;
                    int r0 = k / gw * N, c0 = k % gw * N, o = orientOf[k];
                    for (int r = 0; r < N; r++)
                        for (int c = 0; c < N; c++)
                        {
                            Map(r, c, o, out int rr, out int cc);
                            g[(r0 + rr) * W + c0 + cc] = c < t[r].Length ? t[r][c] : 'X';
                        }
                    for (int d = 0; d < 4; d++)
                    {
                        int nb = Neighbour(k, d);
                        Edge(r0, c0, d, nb < 0 || chunkOf[nb] < 0 ? Wall : sock[k * 4 + d]);
                    }
                }
            }

            /// Chunk borders are structural: 'X' except the 2 m socket opening ('.' door, '#' source_only).
            void Edge(int r0, int c0, int d, int kind)
            {
                char open = kind == Door ? '.' : kind == Src ? '#' : 'X';
                for (int i = 0; i < N; i++)
                {
                    int r = d == 0 ? 0 : d == 2 ? N - 1 : i, c = d == 3 ? 0 : d == 1 ? N - 1 : i;
                    g[(r0 + r) * W + c0 + c] = i >= DoorLo && i <= DoorHi ? open : 'X';
                }
            }

            void Mirror()
            {
                for (int r = 0; r < H; r++) Array.Reverse(g, r * W, W);
                int M(int k) => k / gw * gw + gw - 1 - k % gw;
                var chunks = new int[cells];
                for (int k = 0; k < cells; k++) chunks[M(k)] = chunkOf[k];
                chunkOf = chunks;
                for (int i = 0; i < path.Count; i++) path[i] = M(path[i]);
                for (int k = 0; k < cells; k++) pathIdx[k] = -1;
                for (int i = 0; i < path.Count; i++) pathIdx[path[i]] = i;
            }

            int CellOf(int t) => t / W / N * gw + t % W / N;

            bool Walkable(int t) => !RoomCheck.BlocksRender(g[t]);

            /// Marker in the cell (reading order), else the clear floor tile nearest the cell centre.
            int PickTile(int cell, char marker)
            {
                int r0 = cell / gw * N, c0 = cell % gw * N, best = -1;
                float bd = float.MaxValue;
                for (int pass = 0; pass < 3 && best < 0; pass++)
                    for (int r = 1; r < N - 1; r++)
                        for (int c = 1; c < N - 1; c++)
                        {
                            int t = (r0 + r) * W + c0 + c;
                            if (pass == 0) { if (g[t] == marker) return t; continue; }
                            if (pass == 1 ? g[t] != '.' || !ClearAround(t) : !Walkable(t)) continue;
                            float d = (r - 7.5f) * (r - 7.5f) + (c - 7.5f) * (c - 7.5f);
                            if (d < bd) { bd = d; best = t; }
                        }
                return best;
            }

            bool ClearAround(int t)
            {
                for (int dr = -1; dr <= 1; dr++)
                    for (int dc = -1; dc <= 1; dc++) if (!Walkable(t + dr * W + dc)) return false;
                return true;
            }

            /// Compass direction (every `step`-th of the 8, so 1 = 45 deg, 2 = 90 deg) with the longest clear RENDER view, as a
            /// yaw (0 = north, 90 = east); integer only, so every platform picks the same facing.
            float OpenYaw(int t, int step)
            {
                int best = 0, bestLen = -1;
                for (int k = 0; k < 8; k += step)
                {
                    int r = t / W, c = t % W, len = 0;
                    while (len < 24)
                    {
                        r -= RoomCheck.DirN[k]; c += RoomCheck.DirE[k];
                        if (r < 0 || r >= H || c < 0 || c >= W || !Walkable(r * W + c)) break;
                        len++;
                    }
                    int score = len * len * (k % 2 == 1 ? 2 : 1);   // squared length; diagonal steps are sqrt(2) long
                    if (score > bestLen) { bestLen = score; best = k; }
                }
                return best * 45f;
            }

            float YawTo(int a, int b) =>
                (float)((Math.Atan2(b % W - a % W, a / W - b / W) * (180.0 / Math.PI) + 360.0) % 360.0);

            float Dist(int a, int b)
            {
                float dx = (a % W - b % W) * RoomCheck.Tile, dz = (a / W - b / W) * RoomCheck.Tile;
                return (float)Math.Sqrt(dx * dx + dz * dz);
            }

            EntitySpawn Spawn(EntityKind kind, int t, float yaw, string id, float[] waypoints = null) => new EntitySpawn
            {
                kind = kind, x = RoomCheck.X(t, W), z = RoomCheck.Z(t, W), yaw = yaw, id = id, path = waypoints,
            };

            // ---------- entities ----------

            bool Populate(RoomSpec room)
            {
                var r = Rng.Substream(seed, "threats");
                int start = path[0], end = path[path.Count - 1];
                int spawn = PickSpawn(start), exit = PickTile(end, 'E');
                if (spawn < 0 || exit < 0) return false;
                for (int i = 0; i < g.Length; i++) if (g[i] == 'S' || g[i] == 'E') g[i] = '.';
                g[spawn] = 'S'; g[exit] = 'E';
                int toNext = path[1], fromPrev = path[path.Count - 2];
                room.entities.Add(Spawn(EntityKind.Spawn, spawn, SpawnYaw(spawn, Dir(start, toNext)), "SPAWN"));
                room.entities.Add(Spawn(EntityKind.Exit, exit, CellYaw(end, fromPrev), "EXIT"));

                // terminals: T markers on path cells, distinct cells first, visited in path order
                var ts = new List<int>();
                for (int t = 0; t < g.Length; t++)
                    if (g[t] == 'T' && pathIdx[CellOf(t)] >= 0 && Dist(t, spawn) > 2f) ts.Add(t);
                if (ts.Count == 0) return false;
                Shuffle(r, ts);
                int want = act == 1 ? (node >= 2 ? 2 : 1) : act == 2 ? 2 : (node >= 2 ? 3 : 2);
                var chosen = new List<int>();
                for (int pass = 0; pass < 2; pass++)
                    foreach (int t in ts)
                    {
                        if (chosen.Count == want || chosen.Contains(t)) continue;
                        bool cellTaken = false;
                        foreach (int u in chosen) cellTaken |= CellOf(u) == CellOf(t);
                        if (pass == 0 && cellTaken) continue;
                        chosen.Add(t);
                    }
                chosen.Sort((a, b) => pathIdx[CellOf(a)] != pathIdx[CellOf(b)]
                    ? pathIdx[CellOf(a)].CompareTo(pathIdx[CellOf(b)]) : Dist(a, spawn).CompareTo(Dist(b, spawn)));
                for (int i = 0; i < g.Length; i++) if (g[i] == 'T' && !chosen.Contains(i)) g[i] = '.';
                for (int i = 0; i < chosen.Count; i++)
                    room.entities.Add(Spawn(EntityKind.Terminal, chosen[i], OpenYaw(chosen[i], 2), "TRM-" + (i + 1)));

                PlaceThreats(r, room, spawn, chosen);
                PlaceAnomaly(r, room);
                if (hp.Any) PlaceHeat(room, spawn, chosen);
                for (int i = 0; i < g.Length; i++)
                    if ((g[i] == 'C' || g[i] == 'P' || g[i] == 'I' || g[i] == 'G') && !used.Contains(i)) g[i] = '.';
                return true;
            }

            float CellYaw(int from, int to) => Dir(from, to) * 90f;

            const int SpawnView = 8;   // tiles (4 m) of clear RENDER view that keep the spawn facing along the solution path

            /// Spawn facing (axis-aligned): along the solution path (`pathDir`, N E S W) when that view is clear for SpawnView
            /// tiles, else the axis with the longest clear RENDER view (ties keep the path's direction, then N, E, S, W), so the
            /// first frame never shows a wall a metre away.
            float SpawnYaw(int t, int pathDir)
            {
                int best = pathDir, bestLen = View(t, pathDir);
                if (bestLen < SpawnView)
                    for (int d = 0; d < 4; d++)
                    {
                        int len = View(t, d);
                        if (len > bestLen) { best = d; bestLen = len; }
                    }
                return best * 90f;
            }

            /// Spawn tile: an 'S' marker, else the clear floor tile nearest the start cell's centre with SpawnView tiles of straight
            /// RENDER view along some axis (a chunk's centre can be a render-walled vault: starting inside it shows walls in every
            /// direction), else PickTile's nearest clear floor.
            int PickSpawn(int cell)
            {
                int r0 = cell / gw * N, c0 = cell % gw * N, best = -1;
                float bd = float.MaxValue;
                for (int r = 1; r < N - 1; r++)
                    for (int c = 1; c < N - 1; c++)
                    {
                        int t = (r0 + r) * W + c0 + c;
                        if (g[t] == 'S') return t;
                        if (g[t] != '.' || !ClearAround(t)) continue;
                        float d = (r - 7.5f) * (r - 7.5f) + (c - 7.5f) * (c - 7.5f);
                        if (d >= bd) continue;
                        bool open = false;
                        for (int k = 0; k < 4 && !open; k++) open = View(t, k) >= SpawnView;
                        if (open) { bd = d; best = t; }
                    }
                return best >= 0 ? best : PickTile(cell, 'S');
            }

            /// Walkable RENDER tiles in a straight line from t towards d (N E S W), at most 24.
            int View(int t, int d)
            {
                int r = t / W, c = t % W, len = 0;
                while (len < 24)
                {
                    r += DR[d]; c += DC[d];
                    if (r < 0 || r >= H || c < 0 || c >= W || !Walkable(r * W + c)) break;
                    len++;
                }
                return len;
            }

            readonly HashSet<int> used = new HashSet<int>();

            void PlaceThreats(Mulberry32 r, RoomSpec room, int spawn, List<int> terminals)
            {
                int budget = Budget(act, node, heat), typeCap = act + 2;
                var spent = new int[cells];
                var kinds = new List<EntityKind>();
                var counters = new Dictionary<string, int>();
                int guards = 0;

                // slots: every C / I / G marker, plus one patrol per cell holding >= 2 P markers
                var slots = new List<int>();
                var patrols = new List<int>[cells];
                for (int t = 0; t < g.Length; t++)
                {
                    char ch = g[t];
                    if (ch == 'C' || ch == 'I' || ch == 'G') slots.Add(t);
                    else if (ch == 'P') (patrols[CellOf(t)] ??= new List<int>()).Add(t);
                }
                for (int k = 0; k < cells; k++) if (patrols[k] != null && patrols[k].Count >= 2) slots.Add(-1 - k);
                Shuffle(r, slots);

                var options = new List<Threat>();
                foreach (int slot in slots)
                {
                    int t = slot >= 0 ? slot : -1, cell = slot >= 0 ? CellOf(slot) : -1 - slot;
                    char marker = slot >= 0 ? g[slot] : 'P';
                    int cap = lib.chunks[chunkOf[cell]].budget;
                    float[] waypoints = null;
                    float yaw = 0f;
                    if (marker == 'P')
                    {
                        waypoints = Loop(patrols[cell], out t);
                        if (waypoints == null) continue;
                        yaw = YawTo(patrols[cell][0], patrols[cell][1]);
                    }
                    else if (marker == 'C') yaw = OpenYaw(t, 1);

                    options.Clear();
                    foreach (var th in Threats)
                    {
                        if (th.marker != marker || th.cost > budget || spent[cell] + th.cost > cap) continue;
                        if (act < th.act || (act == th.act && node < th.node)) continue;
                        if (th.enemy && !kinds.Contains(th.kind) && Enemies(kinds) >= typeCap) continue;
                        if ((marker == 'I' || marker == 'G') && Dist(t, spawn) < RoomCheck.IceMinDist + 0.5f) continue;
                        if (marker == 'G' && NearAny(t, terminals, 2.5f)) continue;   // no forced flip mid-hack
                        if (marker == 'C' && SeesAny(t, yaw, th.kind, spawn, terminals)) continue;
                        if (th.kind == EntityKind.Guard && (guards >= MaxGuards(act) || !GuardLoop(patrols[cell], spawn, terminals))) continue;
                        options.Add(th);
                    }
                    if (options.Count == 0) continue;
                    var pick = options[r.Range(0, options.Count)];
                    budget -= pick.cost; spent[cell] += pick.cost;
                    if (pick.kind == EntityKind.Guard) guards++;
                    if (!kinds.Contains(pick.kind)) kinds.Add(pick.kind);
                    counters.TryGetValue(pick.prefix, out int n);
                    counters[pick.prefix] = ++n;
                    room.entities.Add(Spawn(pick.kind, t, yaw, pick.prefix + "-" + n, waypoints));
                    if (marker == 'P') foreach (int p in patrols[cell]) used.Add(p);
                    else used.Add(t);
                }
            }

            /// A guard's patrol loop (waypoints in loop order): every waypoint >= GuardSpawnDist from the spawn, and no waypoint or
            /// tile on a leg inside a terminal's hacking spot (the terminal tile and its 8 neighbours).
            bool GuardLoop(List<int> ps, int spawn, List<int> terminals)
            {
                for (int i = 0; i < ps.Count; i++)
                {
                    if (Dist(ps[i], spawn) < GuardSpawnDist) return false;
                    int a = ps[i], b = ps[(i + 1) % ps.Count];
                    int r0 = a / W, c0 = a % W, r1 = b / W, c1 = b % W;
                    int dr = Math.Abs(r1 - r0), dc = Math.Abs(c1 - c0), sr = r0 < r1 ? 1 : -1, sc = c0 < c1 ? 1 : -1, err = dc - dr;
                    while (true)   // Bresenham, both ends included
                    {
                        foreach (int t in terminals) if (Math.Abs(t / W - r0) <= 1 && Math.Abs(t % W - c0) <= 1) return false;
                        if (r0 == r1 && c0 == c1) break;
                        int e2 = 2 * err;
                        if (e2 > -dr) { err -= dr; c0 += sc; }
                        if (e2 < dc) { err += dc; r0 += sr; }
                    }
                }
                return true;
            }

            bool NearAny(int t, List<int> tiles, float metres)
            {
                foreach (int u in tiles) if (Dist(t, u) < metres) return true;
                return false;
            }

            static int Enemies(List<EntityKind> kinds)
            {
                int n = 0;
                foreach (var k in kinds) if (k != EntityKind.Glitch) n++;
                return n;
            }

            /// A camera (or watcher) must never see the spawn or a terminal's hacking spot.
            bool SeesAny(int cam, float yaw, EntityKind kind, int spawn, List<int> terminals)
            {
                RoomCheck.ConeOf(kind, out float half, out float range);
                if (RoomCheck.Sees(g, W, cam, yaw, half, range, spawn)) return true;
                foreach (int t in terminals)
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            int u = t + dr * W + dc;
                            if (Walkable(u) && RoomCheck.Sees(g, W, cam, yaw, half, range, u)) return true;
                        }
                return false;
            }

            /// Patrol loop through the cell's P markers, ordered by angle around their centre; null if a leg is blocked.
            float[] Loop(List<int> ps, out int first)
            {
                first = -1;
                long sx = 0, sy = 0, n = ps.Count;
                foreach (int p in ps) { sx += p % W; sy += p / W; }
                ps.Sort((a, b) =>   // by angle around the centroid, in exact integers
                {
                    long ax = a % W * n - sx, ay = a / W * n - sy, bx = b % W * n - sx, by = b / W * n - sy;
                    int ha = ax == 0 && ay == 0 ? -1 : ay < 0 || (ay == 0 && ax < 0) ? 1 : 0;
                    int hb = bx == 0 && by == 0 ? -1 : by < 0 || (by == 0 && bx < 0) ? 1 : 0;
                    if (ha != hb) return ha.CompareTo(hb);
                    long cross = ax * by - ay * bx;
                    return cross != 0 ? (cross > 0 ? -1 : 1) : a.CompareTo(b);
                });
                for (int i = 0; i < ps.Count; i++)
                {
                    int a = ps[i], b = ps[(i + 1) % ps.Count];
                    if (!RoomCheck.Clear(g, W, a, b)) return null;
                }
                var pts = new float[ps.Count * 2];
                for (int i = 0; i < ps.Count; i++) { pts[2 * i] = RoomCheck.X(ps[i], W); pts[2 * i + 1] = RoomCheck.Z(ps[i], W); }
                first = ps[0];
                return pts;
            }

            /// Keep one anomaly wall (4-connected run of 'A'); the rest become plain render walls.
            void PlaceAnomaly(Mulberry32 r, RoomSpec room)
            {
                var groups = new List<List<int>>();
                var seenA = new bool[g.Length];
                for (int t = 0; t < g.Length; t++)
                {
                    if (g[t] != 'A' || seenA[t]) continue;
                    var grp = new List<int>();
                    var stack = new Stack<int>();
                    stack.Push(t); seenA[t] = true;
                    while (stack.Count > 0)
                    {
                        int u = stack.Pop();
                        grp.Add(u);
                        for (int d = 0; d < 4; d++)
                        {
                            int rr = u / W + DR[d], cc = u % W + DC[d], v = rr * W + cc;
                            if (rr >= 0 && rr < H && cc >= 0 && cc < W && g[v] == 'A' && !seenA[v]) { seenA[v] = true; stack.Push(v); }
                        }
                    }
                    groups.Add(grp);
                }
                if (groups.Count == 0) return;
                int keep = r.Range(0, groups.Count);
                for (int i = 0; i < groups.Count; i++)
                {
                    if (i != keep) { foreach (int t in groups[i]) g[t] = '#'; continue; }
                    var grp = groups[i];
                    grp.Sort();
                    int mid = grp[grp.Count / 2];
                    room.entities.Add(Spawn(EntityKind.AnomalyWall, mid, OpenYaw(mid, 2), "ANM-1"));
                }
            }

            // ---------- Deep Layer heat placement (unbudgeted; own substream; same marker and safety rules as above) ----------

            void PlaceHeat(RoomSpec room, int spawn, List<int> terminals)
            {
                var r = Rng.Substream(seed, "heat");
                var es = room.entities;

                // Tracker Swarm: a share of the ICE hunters become Tracker ICE on the same vent
                if (hp.trackerShare > 0f)
                {
                    var hunters = new List<int>();
                    for (int i = 0; i < es.Count; i++) if (es[i].kind == EntityKind.IceVent) hunters.Add(i);
                    Shuffle(r, hunters);
                    for (int k = 0, n = Half(hunters.Count * hp.trackerShare); k < n && k < hunters.Count; k++)
                    {
                        var e = es[hunters[k]];
                        e.kind = EntityKind.TrackerIce;
                        es[hunters[k]] = e;
                    }
                }

                // Double Patrol: guard drones on patrol loops nothing walks yet
                if (hp.extraDrones > 0)
                {
                    var loops = new List<int>[cells];
                    for (int t = 0; t < g.Length; t++)
                        if (g[t] == 'P' && !used.Contains(t)) (loops[CellOf(t)] ??= new List<int>()).Add(t);
                    var free = new List<int>();
                    for (int k = 0; k < cells; k++) if (loops[k] != null && loops[k].Count >= 2) free.Add(k);
                    Shuffle(r, free);
                    int added = 0;
                    foreach (int k in free)
                    {
                        if (added == hp.extraDrones) break;
                        var pts = Loop(loops[k], out int t);
                        if (pts == null) continue;
                        es.Add(Spawn(EntityKind.Drone, t, YawTo(loops[k][0], loops[k][1]), "DRN-" + NextId(es, "DRN"), pts));
                        foreach (int p in loops[k]) used.Add(p);
                        added++;
                    }
                }

                // Unstable Reality: more glitch zones, never near the spawn or a terminal
                if (hp.glitchBonus > 0f)
                {
                    int placed = 0;
                    foreach (var e in es) if (e.kind == EntityKind.Glitch) placed++;
                    var spots = new List<int>();
                    for (int t = 0; t < g.Length; t++)
                        if (g[t] == 'G' && !used.Contains(t) && Dist(t, spawn) >= RoomCheck.IceMinDist + 0.5f && !NearAny(t, terminals, 2.5f)) spots.Add(t);
                    Shuffle(r, spots);
                    for (int k = 0, n = Half(placed * hp.glitchBonus); k < n && k < spots.Count; k++)
                    {
                        es.Add(Spawn(EntityKind.Glitch, spots[k], 0f, "GLT-" + NextId(es, "GLT")));
                        used.Add(spots[k]);
                    }
                }

                // Gaze Everywhere: an act 3 room without a Watcher Eye gets one on a free camera mount that sees no spawn/terminal
                if (hp.watchers && act == 3 && !es.Exists(e => e.kind == EntityKind.Watcher))
                {
                    var mounts = new List<int>();
                    for (int t = 0; t < g.Length; t++) if (g[t] == 'C' && !used.Contains(t)) mounts.Add(t);
                    Shuffle(r, mounts);
                    foreach (int t in mounts)
                    {
                        float yaw = OpenYaw(t, 1);
                        if (SeesAny(t, yaw, EntityKind.Watcher, spawn, terminals)) continue;
                        es.Add(Spawn(EntityKind.Watcher, t, yaw, "EYE-" + NextId(es, "EYE")));
                        used.Add(t);
                        break;
                    }
                }
            }

            /// x rounded half up (2.5 -> 3, 0.5 -> 1).
            static int Half(float x) => (int)Math.Floor(x + 0.5f);

            /// 1 + the highest number among ids "<prefix>-n".
            static int NextId(List<EntitySpawn> es, string prefix)
            {
                int n = 0;
                foreach (var e in es)
                    if (e.id != null && e.id.Length > prefix.Length + 1 && e.id.StartsWith(prefix, StringComparison.Ordinal) && e.id[prefix.Length] == '-'
                        && int.TryParse(e.id.Substring(prefix.Length + 1), out int k)) n = Math.Max(n, k);
                return n + 1;
            }

            static void Shuffle(Mulberry32 r, List<int> list)
            {
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = r.Range(0, i + 1);
                    int t = list[i]; list[i] = list[j]; list[j] = t;
                }
            }
        }
    }
}
