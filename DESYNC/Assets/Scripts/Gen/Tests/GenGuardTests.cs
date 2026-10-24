using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Desync.Gen;

// HALCYON guards in generated rooms: from act 1 room 3 on, cost 4, a patrol loop of floor waypoints (x,z pairs) on the
// chunk's 'P' markers, >= 6 m from the spawn, never through a terminal's hacking spot, at most 2 per room (3 in act 3).
public class GenGuardTests
{
    static ChunkLibrary Lib => _lib ??= ChunkLibrary.FromFolder("Assets/Resources/Content/chunks");
    static ChunkLibrary _lib;
    static readonly uint[] Seeds = Enumerable.Range(0, 120).Select(i => (uint)(i * 40503 + 17)).ToArray();

    static readonly Dictionary<EntityKind, int> Cost = new Dictionary<EntityKind, int>
    {
        { EntityKind.CameraFixed, 2 }, { EntityKind.CameraSweep, 3 }, { EntityKind.Drone, 4 }, { EntityKind.IceVent, 4 },
        { EntityKind.TrackerIce, 5 }, { EntityKind.Lantern, 4 }, { EntityKind.Sentinel, 4 }, { EntityKind.Splitter, 5 },
        { EntityKind.Daemon, 7 }, { EntityKind.Watcher, 6 }, { EntityKind.Glitch, 2 }, { EntityKind.Guard, 4 },
    };

    static int Tile(RoomSpec r, float x, float z) => (int)(-z / 0.5f) * r.tiles[0].Length + (int)(x / 0.5f);

    [Test] public void NoGuards_InTheFirstTwoRoomsOfAct1()
    {
        foreach (var seed in Seeds)
            for (int node = 0; node < 2; node++)
                Assert.AreEqual(0, Generator.GenerateRoom(seed, 1, node, 0, Lib).entities.Count(e => e.kind == EntityKind.Guard), $"seed {seed} node {node}");
    }

    [TestCase(1, 2), TestCase(1, 3), TestCase(2, 1), TestCase(2, 3), TestCase(3, 0), TestCase(3, 3)]
    public void Guards_PatrolLegalLoops_WithinCapAndBudget(int act, int node)
    {
        int rooms = 0, withGuards = 0;
        foreach (var seed in Seeds)
        {
            var room = Generator.GenerateRoom(seed, act, node, 0, Lib);
            if (room.fallbackRoom != null) continue;
            rooms++;
            int w = room.tiles[0].Length;
            var guards = room.entities.Where(e => e.kind == EntityKind.Guard).ToList();
            if (guards.Count > 0) withGuards++;
            Assert.LessOrEqual(guards.Count, act == 3 ? 3 : 2, $"seed {seed}");
            Assert.LessOrEqual(room.entities.Where(e => Cost.ContainsKey(e.kind)).Sum(e => Cost[e.kind]), 10 + 6 * act + 2 * node, $"seed {seed}: guards cost 4 of the budget");
            var spawn = room.entities.Single(e => e.kind == EntityKind.Spawn);
            var hack = new HashSet<int>();
            foreach (var t in room.entities.Where(e => e.kind == EntityKind.Terminal))
                for (int dr = -1; dr <= 1; dr++)
                    for (int dc = -1; dc <= 1; dc++) hack.Add(Tile(room, t.x, t.z) + dr * w + dc);
            foreach (var g in guards)
            {
                Assert.IsNotNull(g.path, g.id);
                Assert.GreaterOrEqual(g.path.Length, 4, g.id + ": a loop of >= 2 waypoints");
                Assert.AreEqual(0, g.path.Length % 2, g.id);
                Assert.AreEqual(g.path[0], g.x); Assert.AreEqual(g.path[1], g.z);
                int n = g.path.Length / 2;
                for (int i = 0; i < n; i++)
                {
                    float x = g.path[2 * i], z = g.path[2 * i + 1];
                    Assert.AreEqual('P', room.tiles[(int)(-z / 0.5f)][(int)(x / 0.5f)], $"{g.id} seed {seed}: waypoint on a patrol marker");
                    Assert.GreaterOrEqual(Math.Sqrt((x - spawn.x) * (x - spawn.x) + (z - spawn.z) * (z - spawn.z)), 6.0 - 1e-4, $"{g.id} seed {seed}: >= 6 m from spawn");
                    foreach (int t in Line(Tile(room, x, z), Tile(room, g.path[2 * ((i + 1) % n)], g.path[2 * ((i + 1) % n) + 1]), w))
                    {
                        Assert.IsFalse(hack.Contains(t), $"{g.id} seed {seed}: through a terminal's hacking spot");
                        Assert.IsFalse("#XA ".IndexOf(room.tiles[t / w][t % w]) >= 0, $"{g.id} seed {seed}: leg through a wall");
                    }
                }
            }
        }
        Console.WriteLine($"act {act} node {node}: guards in {withGuards}/{rooms} rooms");
        Assert.Greater(withGuards, rooms / 20, "guards do show up");   // measured: ~20 % of rooms in acts 1-2, ~10 % in act 3
    }

    static IEnumerable<int> Line(int a, int b, int w)
    {
        int r0 = a / w, c0 = a % w, r1 = b / w, c1 = b % w;
        int dr = Math.Abs(r1 - r0), dc = Math.Abs(c1 - c0), sr = r0 < r1 ? 1 : -1, sc = c0 < c1 ? 1 : -1, err = dc - dr;
        while (true)
        {
            yield return r0 * w + c0;
            if (r0 == r1 && c0 == c1) yield break;
            int e2 = 2 * err;
            if (e2 > -dr) { err -= dr; c0 += sc; }
            if (e2 < dc) { err += dc; r0 += sr; }
        }
    }
}
