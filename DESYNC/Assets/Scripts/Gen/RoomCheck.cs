using System;
using System.Collections.Generic;
using Desync.Rules;

namespace Desync.Gen
{
    /// Room validator (spec F2):
    ///  (a) reachability over (tile, layer) — flips are allowed on any RENDER floor, so this is SOURCE connectivity;
    ///  (b) fastest route spawn -> every terminal (in list order, hacked in RENDER for HackSeconds) -> exit, as an A*
    ///      over (tile, layer, 5 % melt bucket) at 5 m/s with Rules melt rates, never reaching 85 % melt and never
    ///      standing in RENDER inside a camera cone (cones are crossed in SOURCE);
    ///  (c) continuous camera exposure measured along that route; (d) no ICE spawn within 4 m of the spawn.
    internal static class RoomCheck
    {
        internal const float Tile = 0.5f, Speed = 5f, MeltCap = 85f, MeltBucket = 5f, IceMinDist = 4f, MaxExposure = 0.4f,
                             HackSeconds = 2f;   // time standing at a terminal (RENDER, melt drains meanwhile)
        internal const float FixedHalf = 30f, FixedRange = 12f, SweepHalf = 45f, SweepRange = 10f;
        const int Buckets = 17;                         // melt < 85 -> buckets 0..16
        const byte WalkR = 1, WalkS = 2;
        static readonly int[] MR = { -1, 0, 1, 0, -1, -1, 1, 1 }, MC = { 0, 1, 0, -1, -1, 1, 1, -1 };

        internal static bool BlocksRender(char ch) => ch == '#' || ch == 'X' || ch == 'A' || ch == ' ';
        internal static bool WalksSource(char ch) => ch != 'X' && ch != ' ';
        internal static bool IsIce(EntityKind k) =>
            k == EntityKind.IceVent || k == EntityKind.TrackerIce || k == EntityKind.Sentinel || k == EntityKind.Splitter || k == EntityKind.Daemon;
        internal static float X(int tile, int w) => (tile % w + 0.5f) * Tile;
        internal static float Z(int tile, int w) => -(tile / w + 0.5f) * Tile;

        static int TileAt(float x, float z, int w, int h)
        {
            int c = (int)Math.Floor(x / Tile), r = (int)Math.Floor(-z / Tile);
            return r < 0 || r >= h || c < 0 || c >= w ? -1 : r * w + c;
        }

        /// No RENDER-blocking tile strictly between the two tiles (Bresenham line).
        internal static bool Clear(char[] g, int w, int a, int b)
        {
            int r0 = a / w, c0 = a % w, r1 = b / w, c1 = b % w;
            int dr = Math.Abs(r1 - r0), dc = Math.Abs(c1 - c0), sr = r0 < r1 ? 1 : -1, sc = c0 < c1 ? 1 : -1, err = dc - dr;
            while (true)
            {
                int e2 = 2 * err;
                if (e2 > -dr) { err -= dr; c0 += sc; }
                if (e2 < dc) { err += dc; r0 += sr; }
                if (r0 == r1 && c0 == c1) return true;
                if (BlocksRender(g[r0 * w + c0])) return false;
            }
        }

        /// Compass steps for yaw k*45 (0 = north, 90 = east): east and north components.
        internal static readonly int[] DirE = { 0, 1, 1, 1, 0, -1, -1, -1 }, DirN = { 1, 1, 0, -1, -1, -1, 0, 1 };

        /// Is tile t inside the cone (half-angle, range in metres) of a camera on tile cam facing yaw (0 = north, 90 = east)?
        /// Generated yaws are multiples of 45 and half-angles 30/45, which are tested in exact integer arithmetic so every
        /// platform builds the same room from a seed; any other yaw falls back to trigonometry.
        internal static bool Sees(char[] g, int w, int cam, float yaw, float half, float range, int t)
        {
            if (t == cam) return true;
            int dx = t % w - cam % w, dy = cam / w - t / w;   // east, north (tiles)
            int rt = (int)(range / Tile);
            if (dx * dx + dy * dy > rt * rt) return false;
            float q = yaw / 45f;
            int k = (int)Math.Round(q);
            bool inside;
            if (Math.Abs(q - k) < 1e-4f && (half == 30f || half == 45f))
            {
                k = (k % 8 + 8) % 8;
                long dot = (long)dx * DirE[k] + (long)dy * DirN[k], u2 = DirE[k] * DirE[k] + DirN[k] * DirN[k];
                inside = dot >= 0 && 4 * dot * dot >= (long)(dx * dx + dy * dy) * u2 * (half == 30f ? 3 : 2);   // cos^2: 3/4, 1/2
            }
            else
            {
                double diff = Math.Atan2(dx, dy) * (180.0 / Math.PI) - yaw;
                diff -= 360.0 * Math.Floor((diff + 180.0) / 360.0);
                inside = Math.Abs(diff) <= half + 1e-6;
            }
            return inside && Clear(g, w, cam, t);
        }

        internal static void ConeOf(EntityKind k, out float half, out float range)
        {
            if (k == EntityKind.CameraSweep) { half = SweepHalf; range = SweepRange; }
            else { half = FixedHalf; range = FixedRange; }
        }

        static void MarkCone(char[] g, int w, int h, int cam, float yaw, float half, float range, bool[] seen)
        {
            int rt = (int)(range / Tile), cr = cam / w, cc = cam % w;
            for (int r = Math.Max(0, cr - rt); r <= Math.Min(h - 1, cr + rt); r++)
                for (int c = Math.Max(0, cc - rt); c <= Math.Min(w - 1, cc + rt); c++)
                {
                    int t = r * w + c;
                    if (!seen[t] && !BlocksRender(g[t]) && Sees(g, w, cam, yaw, half, range, t)) seen[t] = true;
                }
        }

        internal static Validation Validate(RoomSpec room)
        {
            var v = new Validation { reasons = new List<string>(), minMeltPeak = 100f };
            if (room == null || room.tiles == null || room.tiles.Length == 0) { v.reasons.Add("no_tiles"); return v; }
            int h = room.tiles.Length, w = room.tiles[0].Length, n = w * h;
            var g = new char[n];
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++) g[r * w + c] = c < room.tiles[r].Length ? room.tiles[r][c] : ' ';

            int spawn = -1, exit = -1;
            var targets = new List<int>();   // terminals in list order, then the exit
            var seen = new bool[n];
            foreach (var e in room.entities)
            {
                int t = TileAt(e.x, e.z, w, h);
                if (t < 0) continue;
                if (e.kind == EntityKind.Spawn) spawn = t;
                else if (e.kind == EntityKind.Exit) exit = t;
                else if (e.kind == EntityKind.Terminal) targets.Add(t);
                else if (e.kind == EntityKind.CameraFixed || e.kind == EntityKind.CameraSweep)
                {
                    ConeOf(e.kind, out float half, out float range);
                    MarkCone(g, w, h, t, e.yaw, half, range, seen);
                }
            }
            if (spawn < 0) v.reasons.Add("no_spawn");
            if (exit < 0) v.reasons.Add("no_exit");
            if (v.reasons.Count > 0) return v;
            int terminals = targets.Count;
            targets.Add(exit);

            float sx = X(spawn, w), sz = Z(spawn, w);
            foreach (var e in room.entities)
                if (IsIce(e.kind) && (e.x - sx) * (e.x - sx) + (e.z - sz) * (e.z - sz) < IceMinDist * IceMinDist)
                { v.reasons.Add("ice_near_entry"); break; }
            if (seen[spawn]) v.reasons.Add("spawn_in_cone");
            for (int i = 0; i < terminals; i++)
                if (AnySeenAround(seen, w, h, targets[i])) { v.reasons.Add("terminal_in_cone"); break; }

            var walk = new byte[n];
            for (int t = 0; t < n; t++)
                walk[t] = (byte)((BlocksRender(g[t]) ? 0 : WalkR) | (WalksSource(g[t]) ? WalkS : 0));

            var inSource = Flood(walk, null, w, h, spawn, WalkS);
            v.reachable = true;
            for (int i = 0; i < targets.Count; i++)
                if (!inSource[targets[i]])
                {
                    v.reachable = false;
                    v.reasons.Add(i == terminals ? "unreachable_exit" : "unreachable_terminal");
                    break;
                }
            var inRender = Flood(walk, seen, w, h, spawn, WalkR);
            v.flipFreePathExists = true;
            foreach (var t in targets) v.flipFreePathExists &= inRender[t];

            if (v.reachable)
            {
                if (Route(walk, seen, w, h, spawn, targets, out float time, out float peak, out float exposure))
                {
                    v.bestPathSeconds = time;
                    v.minMeltPeak = peak;
                    v.maxCameraExposureOnBestPath = exposure;
                    if (exposure >= MaxExposure) v.reasons.Add("camera_exposure");
                }
                else v.reasons.Add("melt_budget");
            }
            return v;
        }

        static bool AnySeenAround(bool[] seen, int w, int h, int t)
        {
            int r = t / w, c = t % w;
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    int rr = r + dr, cc = c + dc;
                    if (rr >= 0 && rr < h && cc >= 0 && cc < w && seen[rr * w + cc]) return true;
                }
            return false;
        }

        /// 4-connected flood over tiles carrying `flag` (and not seen, when a cone mask is given).
        static bool[] Flood(byte[] walk, bool[] seen, int w, int h, int start, byte flag)
        {
            var mark = new bool[walk.Length];
            var queue = new int[walk.Length];
            int head = 0, tail = 0;
            mark[start] = true; queue[tail++] = start;
            while (head < tail)
            {
                int t = queue[head++], r = t / w, c = t % w;
                for (int d = 0; d < 4; d++)
                {
                    int rr = r + MR[d], cc = c + MC[d];
                    if (rr < 0 || rr >= h || cc < 0 || cc >= w) continue;
                    int u = rr * w + cc;
                    if (mark[u] || (walk[u] & flag) == 0 || (seen != null && seen[u])) continue;
                    mark[u] = true; queue[tail++] = u;
                }
            }
            return mark;
        }

        static bool Route(byte[] walk, bool[] seen, int w, int h, int spawn, List<int> targets,
                          out float time, out float peak, out float exposure)
        {
            time = 0f; peak = 0f; exposure = 0f;
            var s = search ??= new Search();
            int goal = s.Run(walk, seen, w, h, spawn, targets);
            if (goal < 0) return false;
            int top = 0, run = 0, worst = 0;
            for (int l = goal; l >= 0; l = s.parent[l])
            {
                top = Math.Max(top, s.m[l]);
                int p = s.parent[l];
                if (p < 0) continue;
                run = s.ExposedAt(l) ? run + s.g[l] - s.g[p] : 0;
                worst = Math.Max(worst, run);
            }
            time = s.g[goal] / Search.TimeUnit; peak = top / Search.MeltUnit; exposure = worst / Search.TimeUnit;
            return true;
        }

        [ThreadStatic] static Search search;

        /// A* over (next target, tile, layer, melt bucket). Hacking a terminal (RENDER, on its tile) takes HackSeconds and
        /// drains melt, then the next target becomes active, so melt is traded off across the whole route, not per leg.
        /// A label is kept only if it lowers the best settled melt bucket of its state; with a consistent heuristic
        /// same-state labels pop in time order, so the first goal pop is the fastest route within the 5 % discretisation.
        /// Time and melt are fixed-point integers so the result (and the room it accepts) is identical on every platform.
        /// The grid is padded by one closed tile so moves need no bounds checks.
        sealed class Search
        {
            internal const float TimeUnit = 100000f, MeltUnit = 10000f;   // 1e-5 s, 1e-4 %
            static readonly Modifiers Mods = Modifiers.Default;
            static readonly int Step = Fix(Tile / Speed * TimeUnit), Diag = Fix(Tile / Speed * 1.41421356 * TimeUnit),
                HackTime = Fix(HackSeconds * TimeUnit), Cap = Fix(MeltCap * MeltUnit), Bucket = Fix(MeltBucket * MeltUnit),
                Flip = Fix(Mods.FlipCost * MeltUnit), HackDrain = Fix(Mods.RenderDrain * HackSeconds * MeltUnit),
                RiseStep = Fix(Mods.SourceRise * (Tile / Speed) * MeltUnit), RiseDiag = Fix(Mods.SourceRise * (Tile / Speed) * 1.41421356 * MeltUnit),
                DrainStep = Fix(Mods.RenderDrain * (Tile / Speed) * MeltUnit), DrainDiag = Fix(Mods.RenderDrain * (Tile / Speed) * 1.41421356 * MeltUnit);
            const byte OpenR = 1, OpenS = 2, Seen = 4;
            static int Fix(double x) => (int)Math.Round(x);

            // labels
            internal int[] g = new int[1024], m = new int[1024], parent = new int[1024];
            int[] f = new int[1024], state = new int[1024], tileOf = new int[1024], kOf = new int[1024], heap = new int[1024];
            int count, heapCount;
            // per state / per (state, bucket)
            int[] settledStamp = new int[0], gStamp = new int[0], bestG = new int[0];
            byte[] settled = new byte[0];
            int stamp;
            // padded grid
            byte[] open = new byte[0];
            int[] heur = new int[0], goal = new int[8];
            int pw, np, kLast;
            readonly int[] off = new int[8], diagA = new int[8], diagB = new int[8];

            internal bool ExposedAt(int l) => (state[l] & 1) == 0 && (open[tileOf[l]] & Seen) != 0;

            internal int Run(byte[] walk, bool[] seen, int w, int h, int start, List<int> targets)
            {
                pw = w + 2; np = pw * (h + 2);
                int kn = targets.Count, states = np * 2 * kn;
                if (open.Length < np) open = new byte[np];
                Array.Clear(open, 0, np);
                for (int r = 0; r < h; r++)
                    for (int c = 0; c < w; c++)
                    {
                        int t = r * w + c, p = (r + 1) * pw + c + 1;
                        open[p] = (byte)(((walk[t] & WalkR) != 0 && !seen[t] ? OpenR : 0) | ((walk[t] & WalkS) != 0 ? OpenS : 0) | (seen[t] ? Seen : 0));
                    }
                if (settled.Length < states)
                {
                    settled = new byte[states]; settledStamp = new int[states];
                    bestG = new int[states * Buckets]; gStamp = new int[states * Buckets];
                }
                stamp++;
                for (int d = 0; d < 8; d++)
                {
                    off[d] = MR[d] * pw + MC[d];
                    diagA[d] = MR[d] * pw; diagB[d] = MC[d];   // the two orthogonal tiles a diagonal move passes
                }

                // heuristic per (target, tile): octile to the target + octile chain through the remaining targets
                if (goal.Length < kn) goal = new int[kn];
                for (int k = 0; k < kn; k++) goal[k] = (targets[k] / w + 1) * pw + targets[k] % w + 1;
                kLast = kn - 1;
                if (heur.Length < np * kn) heur = new int[np * kn];
                int rest = 0;
                for (int k = kn - 1; k >= 0; k--)
                {
                    if (k < kn - 1) rest += Octile(goal[k], goal[k + 1]) + HackTime;
                    int gr = goal[k] / pw, gc = goal[k] % pw, b = k * np;
                    for (int p = 0; p < np; p++)
                    {
                        int dr = Math.Abs(p / pw - gr), dc = Math.Abs(p % pw - gc);
                        heur[b + p] = (dr > dc ? dr * Step + dc * (Diag - Step) : dc * Step + dr * (Diag - Step)) + rest;
                    }
                }

                count = 0; heapCount = 0;
                Push(-1, 0, (start / w + 1) * pw + start % w + 1, 0, 0, 0);
                while (heapCount > 0)
                {
                    int l = Pop(), st = state[l], k = kOf[l], p = tileOf[l], layer = st & 1, melt = m[l];
                    int b = melt / Bucket;
                    if (settledStamp[st] == stamp && b >= settled[st]) continue;
                    settledStamp[st] = stamp; settled[st] = (byte)b;
                    if (p == goal[k])
                    {
                        if (k == kLast) return l;   // exit, either layer
                        if (layer == 0)             // hack in RENDER, then head for the next target
                            Relax(l, k + 1, p, 0, HackTime, Math.Max(0, melt - HackDrain));
                    }

                    // flip in place: +FlipCost before the change; RENDER only on floor outside camera cones
                    if (layer == 0 || (open[p] & OpenR) != 0) Relax(l, k, p, layer ^ 1, 0, melt + Flip);

                    byte need = layer == 1 ? OpenS : OpenR;
                    int ms = layer == 1 ? melt + RiseStep : Math.Max(0, melt - DrainStep);
                    int md = layer == 1 ? melt + RiseDiag : Math.Max(0, melt - DrainDiag);
                    for (int d = 0; d < 4; d++)
                        if ((open[p + off[d]] & need) != 0) Relax(l, k, p + off[d], layer, Step, ms);
                    for (int d = 4; d < 8; d++)   // diagonals: no corner cutting
                        if ((open[p + off[d]] & need) != 0 && (open[p + diagA[d]] & need) != 0 && (open[p + diagB[d]] & need) != 0)
                            Relax(l, k, p + off[d], layer, Diag, md);
                }
                return -1;
            }

            int Octile(int a, int b)
            {
                int dr = Math.Abs(a / pw - b / pw), dc = Math.Abs(a % pw - b % pw);
                return Math.Max(dr, dc) * Step + Math.Min(dr, dc) * (Diag - Step);
            }

            void Relax(int from, int k, int p, int layer, int dt, int melt)
            {
                if (melt >= Cap) return;
                int st = ((k * np + p) << 1) | layer, b = melt / Bucket;
                if (settledStamp[st] == stamp && b >= settled[st]) return;
                int ng = g[from] + dt, key = st * Buckets + b;
                if (gStamp[key] == stamp && bestG[key] <= ng) return;
                gStamp[key] = stamp; bestG[key] = ng;
                Push(from, k, p, layer, ng, melt);
            }

            void Push(int from, int k, int p, int layer, int time, int melt)
            {
                if (count == g.Length)
                {
                    int cap = count * 2;
                    Array.Resize(ref g, cap); Array.Resize(ref m, cap); Array.Resize(ref f, cap); Array.Resize(ref parent, cap);
                    Array.Resize(ref state, cap); Array.Resize(ref tileOf, cap); Array.Resize(ref kOf, cap);
                }
                int l = count++;
                g[l] = time; m[l] = melt; parent[l] = from; tileOf[l] = p; kOf[l] = k;
                state[l] = ((k * np + p) << 1) | layer;
                f[l] = time + heur[k * np + p];   // admissible and consistent
                if (heapCount == heap.Length) Array.Resize(ref heap, heapCount * 2);
                int i = heapCount++;
                while (i > 0)
                {
                    int q = (i - 1) >> 1;
                    if (!Before(l, heap[q])) break;
                    heap[i] = heap[q]; i = q;
                }
                heap[i] = l;
            }

            /// Heap order: estimated total time, then lower melt (equal-time routes keep the cooler one), then age.
            bool Before(int a, int b) => f[a] != f[b] ? f[a] < f[b] : m[a] != m[b] ? m[a] < m[b] : a < b;

            int Pop()
            {
                int top = heap[0], last = heap[--heapCount], i = 0;
                while (true)
                {
                    int a = 2 * i + 1;
                    if (a >= heapCount) break;
                    if (a + 1 < heapCount && Before(heap[a + 1], heap[a])) a++;
                    if (!Before(heap[a], last)) break;
                    heap[i] = heap[a]; i = a;
                }
                if (heapCount > 0) heap[i] = last;
                return top;
            }
        }
    }
}
