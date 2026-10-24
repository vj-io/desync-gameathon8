using System;
using System.Collections.Generic;
using Desync.Gen;
using Desync.Rules;

namespace Desync.Flow
{
    /// Where a generated room's story shard goes (pure; seeded by the room). At most one per room:
    ///   - always on an Anomaly node, and in act 1 rooms until the player holds EarlyShards (the first half hour);
    ///   - otherwise with probability Chance on the room's "shard" substream.
    /// The spot: a SOURCE-only vault behind the room's anomaly wall when it has one (the secret the flicker marks); else a
    /// floor tile, reachable from the spawn, out of every camera cone (fixed 30 deg x 12 m, sweep 45 deg x 10 m, Watcher
    /// as fixed, Lantern 7 m all round; walls block sight), SpawnClear m from the spawn and EntityClear m from any entity,
    /// picked on the room's "shardspot" substream. Boss arenas take no pickup: a boss kill pays the shard instead (ShardRun).
    public static class ShardPlacement
    {
        public const float Chance = 0.4f, Tile = 0.5f, SpawnClear = 4f, EntityClear = 1.5f;
        public const int EarlyShards = 3;

        public static bool Wants(RoomSpec spec, NodeType type, int act, int collected)
        {
            if (spec?.tiles == null || type == NodeType.Boss) return false;
            if (type == NodeType.Anomaly || (act == 1 && collected < EarlyShards)) return true;
            return Rng.Substream(spec.seed, "shard").NextDouble() < Chance;
        }

        /// Room-space metres of the shard (x east, z = -south, tile centre); false when no tile qualifies.
        public static bool Spot(RoomSpec spec, out float x, out float z)
        {
            x = z = 0f;
            if (spec?.tiles == null || spec.tiles.Length == 0) return false;
            int h = spec.tiles.Length, w = spec.tiles[0].Length, n = w * h;
            var g = new char[n];
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++) g[r * w + c] = c < spec.tiles[r].Length ? spec.tiles[r][c] : ' ';

            int spawn = -1;
            var seen = new bool[n];
            var near = new bool[n];   // within EntityClear of an entity
            foreach (var e in spec.entities)
            {
                int t = TileAt(e.x, e.z, w, h);
                if (t < 0) continue;
                if (e.kind == EntityKind.Spawn) spawn = t;
                if (e.kind != EntityKind.AnomalyWall) Mark(near, w, h, t, EntityClear);   // the wall marker is not a thing to avoid
                if (ConeOf(e.kind, out float half, out float range)) MarkCone(g, w, h, t, e.yaw, half, range, seen);
            }
            if (spawn < 0) return false;

            var render = Flood(g, w, h, spawn, false);
            var source = Flood(g, w, h, spawn, true);

            int vault = VaultTile(g, w, h, render, source, seen, near);
            if (vault >= 0) { x = X(vault, w); z = Z(vault, w); return true; }

            var cands = new List<int>();
            float sx = X(spawn, w), sz = Z(spawn, w);
            for (int t = 0; t < n; t++)
            {
                if (g[t] != '.' || !source[t] || seen[t] || near[t]) continue;
                float dx = X(t, w) - sx, dz = Z(t, w) - sz;
                if (dx * dx + dz * dz < SpawnClear * SpawnClear) continue;
                cands.Add(t);
            }
            if (cands.Count == 0) return false;
            int pick = cands[Rng.Substream(spec.seed, "shardspot").Range(0, cands.Count)];
            x = X(pick, w); z = Z(pick, w);
            return true;
        }

        /// The middle free tile of the first SOURCE-only floor pocket (not reachable in RENDER) that touches an anomaly wall 'A'.
        static int VaultTile(char[] g, int w, int h, bool[] render, bool[] source, bool[] seen, bool[] near)
        {
            int n = w * h;
            var done = new bool[n];
            var stack = new Stack<int>();
            var pocket = new List<int>();
            for (int t = 0; t < n; t++)
            {
                if (done[t] || render[t] || !source[t] || BlocksRender(g[t])) continue;
                pocket.Clear();
                bool anomaly = false;
                stack.Push(t); done[t] = true;
                while (stack.Count > 0)
                {
                    int u = stack.Pop();
                    pocket.Add(u);
                    for (int d = 0; d < 4; d++)
                    {
                        int v = Step(u, d, w, h);
                        if (v < 0) continue;
                        if (g[v] == 'A') anomaly = true;
                        if (!done[v] && !render[v] && source[v] && !BlocksRender(g[v])) { done[v] = true; stack.Push(v); }
                    }
                }
                if (!anomaly) continue;
                pocket.Sort();
                for (int k = 0; k < pocket.Count; k++)
                {
                    int m = pocket[(pocket.Count / 2 + k) % pocket.Count];   // the middle first, then onwards
                    if (g[m] == '.' && !seen[m] && !near[m]) return m;
                }
            }
            return -1;
        }

        /// Sight cones that count, by entity: (half-angle deg, range m); 180 = all round.
        static bool ConeOf(EntityKind k, out float half, out float range)
        {
            switch (k)
            {
                case EntityKind.CameraFixed: case EntityKind.Watcher: half = 30f; range = 12f; return true;
                case EntityKind.CameraSweep: half = 45f; range = 10f; return true;
                case EntityKind.Lantern: half = 180f; range = 7f; return true;
                default: half = range = 0f; return false;
            }
        }

        static void MarkCone(char[] g, int w, int h, int cam, float yaw, float half, float range, bool[] seen)
        {
            int rt = (int)Math.Ceiling(range / Tile), cr = cam / w, cc = cam % w;
            for (int r = Math.Max(0, cr - rt); r <= Math.Min(h - 1, cr + rt); r++)
                for (int c = Math.Max(0, cc - rt); c <= Math.Min(w - 1, cc + rt); c++)
                {
                    int t = r * w + c;
                    if (!seen[t] && Sees(g, w, cam, yaw, half, range, t)) seen[t] = true;
                }
        }

        /// Tile t inside the cone (yaw 0 = north, 90 = east) with nothing RENDER-solid between.
        static bool Sees(char[] g, int w, int cam, float yaw, float half, float range, int t)
        {
            if (t == cam) return true;
            int dx = t % w - cam % w, dy = cam / w - t / w;   // east, north (tiles)
            if ((dx * dx + dy * dy) * Tile * Tile > range * range) return false;
            if (half < 180f)
            {
                double diff = Math.Atan2(dx, dy) * (180.0 / Math.PI) - yaw;
                diff -= 360.0 * Math.Floor((diff + 180.0) / 360.0);
                if (Math.Abs(diff) > half + 1e-3) return false;
            }
            return Clear(g, w, cam, t);
        }

        /// No RENDER-blocking tile strictly between two tiles (Bresenham), as the generator's validator draws sight lines.
        static bool Clear(char[] g, int w, int a, int b)
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

        /// 4-connected walk from the spawn: RENDER stops at '#', 'X', 'A' and void; SOURCE only at 'X' and void.
        static bool[] Flood(char[] g, int w, int h, int start, bool sourceLayer)
        {
            var seen = new bool[w * h];
            var stack = new Stack<int>();
            seen[start] = true; stack.Push(start);
            while (stack.Count > 0)
            {
                int u = stack.Pop();
                for (int d = 0; d < 4; d++)
                {
                    int v = Step(u, d, w, h);
                    if (v < 0 || seen[v]) continue;
                    char ch = g[v];
                    if (sourceLayer ? ch == 'X' || ch == ' ' : BlocksRender(ch)) continue;
                    seen[v] = true; stack.Push(v);
                }
            }
            return seen;
        }

        static void Mark(bool[] near, int w, int h, int t, float radius)
        {
            int rt = (int)Math.Ceiling(radius / Tile), cr = t / w, cc = t % w;
            for (int r = Math.Max(0, cr - rt); r <= Math.Min(h - 1, cr + rt); r++)
                for (int c = Math.Max(0, cc - rt); c <= Math.Min(w - 1, cc + rt); c++)
                    if (((r - cr) * (r - cr) + (c - cc) * (c - cc)) * Tile * Tile < radius * radius) near[r * w + c] = true;
        }

        static readonly int[] DR = { -1, 0, 1, 0 }, DC = { 0, 1, 0, -1 };

        static int Step(int u, int d, int w, int h)
        {
            int r = u / w + DR[d], c = u % w + DC[d];
            return r < 0 || r >= h || c < 0 || c >= w ? -1 : r * w + c;
        }

        static bool BlocksRender(char ch) => ch == '#' || ch == 'X' || ch == 'A' || ch == ' ';
        static float X(int t, int w) => (t % w + 0.5f) * Tile;
        static float Z(int t, int w) => -(t / w + 0.5f) * Tile;

        static int TileAt(float x, float z, int w, int h)
        {
            int c = (int)Math.Floor(x / Tile), r = (int)Math.Floor(-z / Tile);
            return r < 0 || r >= h || c < 0 || c >= w ? -1 : r * w + c;
        }
    }
}
