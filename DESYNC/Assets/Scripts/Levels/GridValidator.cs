using System;
using System.Collections.Generic;
using System.Text;

namespace Desync.Levels
{
    /// Beatability check over a RoomDef grid (pure C#, no UnityEngine, so it also runs outside Unity).
    /// Model: Dijkstra over (cell, level, layer, melt). RENDER walks free cells only and drains melt; SOURCE may enter
    /// walls/props (never void) and raises melt; each flip spikes melt; returning to RENDER inside a solid cell is
    /// rejected (PRD 5.4). Legs: spawn -> every terminal (best order, hacked in RENDER) -> exit.
    /// Two runs: "fastest" (cameras ignored) and "ghost" (cells a camera/drone could watch are SOURCE-only).
    public static class GridValidator
    {
        // Rates mirror GameConfig defaults (PRD Appendix A); passed in so Unity can feed GameConfig.I.
        public struct Rates { public float move, rise, drain, spike, hack; }
        public static readonly Rates Default = new Rates { move = 5f, rise = 12f, drain = 4f, spike = 5f, hack = 3f };

        public sealed class Result
        {
            public bool ok, ghostOk, renderOnly;
            public float time, peakMelt, ghostTime, ghostPeak;
            public int flips, sourceCells, exposure, ghostFlips;
            public string route = "", ghostRoute = "", note = "";
            public override string ToString() =>
                (ok ? "BEATABLE" : "NOT BEATABLE") +
                $" fastest {time:0.0}s peakMelt {peakMelt:0}% flips {flips} sourceCells {sourceCells} camExposureCells {exposure}" +
                $" | ghost {(ghostOk ? $"{ghostTime:0.0}s peakMelt {ghostPeak:0}% flips {ghostFlips}" : "none")}" +
                $" | renderOnly {(renderOnly ? "yes" : "no")}{(note.Length > 0 ? " | " + note : "")}";
        }

        struct Node { public int cell, lvl, layer; public float melt, time, cost; }

        public static Result Validate(RoomDef room) => Validate(room, Default);

        public static Result Validate(RoomDef room, Rates k)
        {
            var g = new Grid(room);
            var watched = Watched(room, g);
            var res = new Result();
            var fast = Route(room, g, k, null, true, out res.route);
            var ghost = Route(room, g, k, watched, true, out res.ghostRoute);
            res.renderOnly = Route(room, g, k, null, false, out _) != null;
            if (fast != null)
            {
                res.ok = true; res.time = fast[fast.Count - 1].time;
                foreach (var n in fast) res.peakMelt = Math.Max(res.peakMelt, n.melt);
                Stats(fast, watched, out res.flips, out res.sourceCells, out res.exposure);
            }
            if (ghost != null)
            {
                res.ghostOk = true; res.ghostTime = ghost[ghost.Count - 1].time;
                foreach (var n in ghost) res.ghostPeak = Math.Max(res.ghostPeak, n.melt);
                Stats(ghost, watched, out res.ghostFlips, out _, out _);
            }
            if (room.traceCountdown > 0 && res.ok && res.time > room.traceCountdown) res.note = "SLOWER THAN TRACE COUNTDOWN";
            return res;
        }

        static void Stats(List<Node> path, bool[] watched, out int flips, out int src, out int exposure)
        {
            flips = src = exposure = 0;
            for (int i = 1; i < path.Count; i++)
            {
                if (path[i].layer != path[i - 1].layer) flips++;
                else if (path[i].cell != path[i - 1].cell) { if (path[i].layer == 1) src++; else if (watched[path[i].cell]) exposure++; }
            }
        }

        // ---------------------------------------------------------------- grid queries
        sealed class Grid
        {
            public readonly int C, R; readonly RoomDef d;
            public Grid(RoomDef room) { d = room; C = room.Cols; R = room.Rows; }
            public int Id(int c, int r) => r * C + c;
            public char Ch(int c, int r) => d.At(c, r);
            public bool Void(int c, int r) => Legend.IsVoid(Ch(c, r));
            public bool Wall(int c, int r) => Ch(c, r) == Legend.Wall;
            public bool Raised(int c, int r) => !Wall(c, r) && d.LevelAt(c, r) == Legend.Upper;
            public int Ramp(int c, int r) => Wall(c, r) ? 0 : Legend.RampDir(d.LevelAt(c, r));   // +1 rises east, -1 rises west
            public int EntLevel(int c, int r) => Raised(c, r) || Ramp(c, r) != 0 ? 1 : 0;
            public bool Surface(int c, int r, int l) => !Void(c, r) && (l == 0 ? Ramp(c, r) == 0 : Raised(c, r) || Ramp(c, r) != 0);
            public bool Solid(int c, int r, int l) => Wall(c, r) || (Legend.Blocks(Ch(c, r)) && EntLevel(c, r) == l);
            public bool RenderOk(int c, int r, int l) => Surface(c, r, l) && !Solid(c, r, l);
        }

        static readonly int[] DX = { 0, 1, 0, -1, 1, 1, -1, -1 }, DY = { -1, 0, 1, 0, -1, 1, 1, -1 };

        /// Target level after stepping from (c,r,l) by (dx,dy); -1 = impossible in any layer; sets railing when only SOURCE may pass.
        static int Step(Grid g, int c, int r, int l, int dx, int dy, out bool railing)
        {
            railing = false;
            int c2 = c + dx, r2 = r + dy;
            if (g.Void(c2, r2)) return -1;
            int ramp = g.Ramp(c, r), ramp2 = g.Ramp(c2, r2);
            if (l == 0)
            {
                if (ramp2 != 0) return dy == 0 && dx == ramp2 ? 1 : -1;     // walk onto a ramp from its low end only
                return 0;
            }
            if (ramp != 0)
            {
                if (dy == 0 && dx == -ramp) return ramp2 == ramp ? 1 : 0;   // walking down the ramp
                if (dy == 0 && dx == ramp) return ramp2 == ramp || g.Raised(c2, r2) ? 1 : 0;
                return 0;                                                    // off the ramp side: drop
            }
            // on the upper floor
            if (g.Raised(c2, r2)) return 1;
            if (ramp2 != 0 && dy == 0 && dx == -ramp2) return 1;             // onto a ramp from its high end
            if (!g.Wall(c2, r2)) railing = true;                             // railing edge: SOURCE drops through it
            return 0;
        }

        static List<Node> Route(RoomDef room, Grid g, Rates k, bool[] watched, bool allowSource, out string log)
        {
            log = "";
            int sc = -1, sr = -1; var terms = new List<int>(); int ec = -1, er = -1;
            for (int r = 0; r < g.R; r++)
            for (int c = 0; c < g.C; c++)
            {
                char ch = g.Ch(c, r);
                if (ch == Legend.Spawn) { sc = c; sr = r; }
                else if (ch == Legend.Terminal) terms.Add(g.Id(c, r));
                else if (ch == Legend.Exit) { ec = c; er = r; }
            }
            if (sc < 0) { log = "no spawn"; return null; }
            List<Node> best = null;
            foreach (var order in Permutations(terms))
            {
                var path = new List<Node> { new Node { cell = g.Id(sc, sr), lvl = g.EntLevel(sc, sr), layer = 0 } };
                bool fail = false;
                foreach (int t in order)
                {
                    int tc = t % g.C, tr = t / g.C, tl = g.EntLevel(tc, tr);
                    // hack standing next to it: RENDER drains melt, SOURCE (allowed, prompts scrambled) must survive the hack time
                    // (a SOURCE hack is only the fallback when no RENDER spot is reachable).
                    var from = path[path.Count - 1];
                    var leg = Leg(g, k, watched, allowSource, from, (c, r, n) =>
                        n.lvl == tl && n.layer == 0 && Math.Abs(c - tc) + Math.Abs(r - tr) == 1)
                        ?? Leg(g, k, watched, allowSource, from, (c, r, n) =>
                        n.lvl == tl && Math.Abs(c - tc) + Math.Abs(r - tr) == 1 && n.melt + k.rise * k.hack < 100f);
                    if (leg == null) { fail = true; break; }
                    var end = leg[leg.Count - 1];
                    end.time += k.hack; end.cost += k.hack * (end.layer == 1 ? 2.5f : 1f);
                    end.melt = end.layer == 0 ? Math.Max(0f, end.melt - k.drain * k.hack) : end.melt + k.rise * k.hack;
                    leg[leg.Count - 1] = end;
                    path.AddRange(leg.GetRange(1, leg.Count - 1));
                }
                if (!fail && ec >= 0)
                {
                    var leg = Leg(g, k, watched, allowSource, path[path.Count - 1], (c, r, n) => c == ec && r == er);
                    if (leg == null) fail = true; else path.AddRange(leg.GetRange(1, leg.Count - 1));
                }
                if (!fail && (best == null || path[path.Count - 1].cost < best[best.Count - 1].cost)) best = path;
            }
            if (best != null) log = Overlay(room, g, best);
            return best;
        }

        static List<Node> Leg(Grid g, Rates k, bool[] watched, bool allowSource, Node start, Func<int, int, Node, bool> goal)
        {
            // Cost = time, with SOURCE seconds weighted x2.5 and flips +0.3 s, so routes flip back once clear of a wall
            // (as a player would) instead of idling in SOURCE. States pop in cost order, so a state whose
            // (cell,lvl,layer) was already finalised with <= melt is dominated (Pareto prune).
            var minMelt = new float[g.C * g.R * 4];
            for (int i = 0; i < minMelt.Length; i++) minMelt[i] = float.MaxValue;
            var open = new SortedSet<(float t, int seq)>();
            var nodes = new List<Node>(); var parent = new List<int>();
            int seq = 0;
            void Push(Node n, int from) { nodes.Add(n); parent.Add(from); open.Add((n.cost, seq++)); }
            Push(start, -1);
            float dStep = RoomDef.Cell / k.move;
            while (open.Count > 0)
            {
                var top = open.Min; open.Remove(top);
                int i = top.seq; var n = nodes[i];
                int key = n.cell * 4 + n.lvl * 2 + n.layer;
                if (minMelt[key] <= n.melt + 0.01f) continue;
                minMelt[key] = n.melt;
                int c = n.cell % g.C, r = n.cell / g.C;
                if (goal(c, r, n))
                {
                    var path = new List<Node>();
                    for (int p = i; p >= 0; p = parent[p]) path.Add(nodes[p]);
                    path.Reverse(); return path;
                }
                // flip
                if (allowSource)
                {
                    int to = 1 - n.layer;
                    bool can = to == 1 || (g.RenderOk(c, r, n.lvl) && (watched == null || !watched[n.cell]));
                    float m = n.melt + k.spike;
                    if (can && m < 100f) Push(new Node { cell = n.cell, lvl = n.lvl, layer = to, melt = m, time = n.time + 0.15f, cost = n.cost + 0.45f }, i);
                }
                for (int d = 0; d < 8; d++)
                {
                    int dx = DX[d], dy = DY[d], c2 = c + dx, r2 = r + dy;
                    int l2 = Step(g, c, r, n.lvl, dx, dy, out bool railing);
                    if (l2 < 0) continue;
                    bool diag = dx != 0 && dy != 0;
                    if (diag && (l2 != n.lvl || g.Ramp(c, r) != 0 || g.Ramp(c2, r2) != 0)) continue;
                    float dt = dStep * (diag ? 1.4142f : 1f);
                    float m;
                    if (n.layer == 0)
                    {
                        if (railing || !g.RenderOk(c2, r2, l2)) continue;
                        if (watched != null && watched[g.Id(c2, r2)]) continue;
                        if (diag && (!g.RenderOk(c + dx, r, n.lvl) || !g.RenderOk(c, r + dy, n.lvl))) continue;
                        m = Math.Max(0f, n.melt - k.drain * dt);
                    }
                    else
                    {
                        if (diag && (g.Void(c + dx, r) || g.Void(c, r + dy))) continue;
                        m = n.melt + k.rise * dt;
                        if (m >= 100f) continue;
                    }
                    Push(new Node { cell = g.Id(c2, r2), lvl = l2, layer = n.layer, melt = m, time = n.time + dt, cost = n.cost + dt * (n.layer == 1 ? 2.5f : 1f) }, i);
                }
            }
            return null;
        }

        /// Cells a camera cone (range, +-(sweep + half cone), wall-blocked LOS) or a drone patrol (path +-1 cell) can see.
        internal static bool[] WatchedDebug(RoomDef room) => Watched(room, new Grid(room));
        static bool[] Watched(RoomDef room, Grid g)
        {
            var w = new bool[g.C * g.R];
            foreach (var cam in room.cams)
            {
                if (!room.Find(cam.id, out int cc, out int cr)) continue;
                float range = 8f / RoomDef.Cell, half = 45f + 30f;
                for (int r = 0; r < g.R; r++)
                for (int c = 0; c < g.C; c++)
                {
                    float dx = c - cc, dz = cr - r;   // +z = north = row decreasing
                    float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (dist > range) continue;
                    if (dist > 0.01f)
                    {
                        float ang = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
                        float diff = Math.Abs(((ang - cam.yaw) % 360f + 540f) % 360f - 180f);
                        if (diff > half) continue;
                    }
                    if (Los(g, cc, cr, c, r)) w[g.Id(c, r)] = true;
                }
            }
            var wps = room.Waypoints();
            for (int i = 0; i < wps.Count; i++)
            {
                var a = wps[i]; var b = wps[(i + 1) % wps.Count];
                int steps = Math.Max(Math.Abs(b.c - a.c), Math.Abs(b.r - a.r)) * 2 + 1;
                for (int s = 0; s <= steps; s++)
                {
                    float t = (float)s / steps;
                    int pc = (int)Math.Round(a.c + (b.c - a.c) * t), pr = (int)Math.Round(a.r + (b.r - a.r) * t);
                    for (int oy = -1; oy <= 1; oy++) for (int ox = -1; ox <= 1; ox++)
                    {
                        int qc = pc + ox, qr = pr + oy;
                        if (qc >= 0 && qr >= 0 && qc < g.C && qr < g.R) w[g.Id(qc, qr)] = true;
                    }
                }
            }
            return w;
        }

        static bool Los(Grid g, int c0, int r0, int c1, int r1)
        {
            int n = Math.Max(Math.Abs(c1 - c0), Math.Abs(r1 - r0)) * 4;
            for (int i = 1; i < n; i++)
            {
                float t = (float)i / n;
                int c = (int)Math.Round(c0 + (c1 - c0) * t), r = (int)Math.Round(r0 + (r1 - r0) * t);
                if ((c != c1 || r != r1) && g.Wall(c, r)) return false;
            }
            return true;
        }

        static IEnumerable<List<int>> Permutations(List<int> items)
        {
            if (items.Count <= 1) { yield return new List<int>(items); yield break; }
            for (int i = 0; i < items.Count; i++)
            {
                var rest = new List<int>(items); rest.RemoveAt(i);
                foreach (var p in Permutations(rest)) { p.Insert(0, items[i]); yield return p; }
            }
        }

        /// The room grid with the route drawn on it: 'o' walked in RENDER, '*' in SOURCE, '@' a flip.
        static string Overlay(RoomDef room, Grid g, List<Node> path)
        {
            var rows = new char[g.R][];
            for (int r = 0; r < g.R; r++) rows[r] = room.grid[r].ToCharArray();
            for (int i = 1; i < path.Count; i++)
            {
                int c = path[i].cell % g.C, r = path[i].cell / g.C;
                char ch = rows[r][c];
                if (ch != '.' && ch != '#' && !Legend.Blocks(ch)) continue;
                rows[r][c] = path[i].layer != path[i - 1].layer ? '@' : path[i].layer == 1 ? '*' : 'o';
            }
            var sb = new StringBuilder();
            foreach (var row in rows) sb.Append(new string(row)).Append('\n');
            return sb.ToString();
        }
    }
}
