using System;
using System.Collections.Generic;
using Desync.Rules;

namespace Desync.Gen
{
    /// Run map: per act 4 rows of 2-3 lane nodes + a boss. Edges only go to the next row; every node offers at least
    /// two forward choices and is reachable from row 0. Node ids are "a{act}n{k}" (k = reading order, boss last).
    internal static class RunGen
    {
        static readonly NodeType[] Types = { NodeType.Infil, NodeType.Elite, NodeType.Market, NodeType.Cooldown, NodeType.Anomaly };
        static readonly int[] Weights = { 60, 15, 10, 10, 5 };

        internal static RunGraph Generate(uint seed, int heat)
        {
            var graph = new RunGraph { seed = seed };
            var rng = Rng.Substream(seed, "run");
            for (int act = 1; act <= 3; act++)
            {
                var nodes = new List<RunNode>();
                var rows = new List<RunNode>[4];
                bool anomaly = false;
                for (int row = 0; row < 4; row++)
                {
                    rows[row] = new List<RunNode>();
                    int skip = rng.NextDouble() < 0.5 ? rng.Range(0, 3) : -1;   // 2-node rows leave one lane out
                    for (int lane = 0; lane < 3; lane++)
                    {
                        if (lane == skip) continue;
                        var type = act == 1 && row == 0 ? NodeType.Infil : Roll(rng, ref anomaly);
                        var n = new RunNode { id = "a" + act + "n" + nodes.Count, act = act, index = row, lane = lane, type = type };
                        rows[row].Add(n);
                        nodes.Add(n);
                    }
                }
                var boss = new RunNode { id = "a" + act + "n" + nodes.Count, act = act, index = 4, lane = 1, type = NodeType.Boss };
                for (int row = 0; row < 3; row++) Link(rows[row], rows[row + 1]);
                foreach (var n in rows[3]) n.next.Add(boss.id);
                nodes.Add(boss);
                foreach (var n in nodes) n.roomSeed = Rng.Substream(seed, n.id).NextUInt();
                graph.acts.Add(nodes);
            }
            return graph;
        }

        /// Weighted node type; at most one Anomaly per act (spec: <= 1 per act per run).
        static NodeType Roll(Mulberry32 rng, ref bool anomaly)
        {
            while (true)
            {
                int x = rng.Range(0, 100), i = 0;
                while (x >= Weights[i]) x -= Weights[i++];
                if (Types[i] != NodeType.Anomaly) return Types[i];
                if (!anomaly) { anomaly = true; return NodeType.Anomaly; }
            }
        }

        /// Lane neighbours (|dLane| <= 1), topped up with the nearest lanes to two choices; every next node gets a parent.
        static void Link(List<RunNode> from, List<RunNode> to)
        {
            foreach (var u in from)
            {
                foreach (var v in to) if (Math.Abs(u.lane - v.lane) <= 1) u.next.Add(v.id);
                while (u.next.Count < 2)
                {
                    RunNode best = null;
                    foreach (var v in to)
                        if (!u.next.Contains(v.id) && (best == null || Math.Abs(u.lane - v.lane) < Math.Abs(u.lane - best.lane))) best = v;
                    u.next.Add(best.id);
                }
            }
            foreach (var v in to)
            {
                bool fed = false;
                foreach (var u in from) fed |= u.next.Contains(v.id);
                if (fed) continue;
                RunNode best = null;
                foreach (var u in from) if (best == null || Math.Abs(u.lane - v.lane) < Math.Abs(best.lane - v.lane)) best = u;
                best.next.Add(v.id);
            }
        }
    }
}
