using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using Desync.Gen;

// Generator rules beyond the GEN acceptance tests: chunk authoring, intro pacing, budgets, sockets, validator cases.
public class GenRulesTests
{
    static ChunkLibrary Lib => _lib ??= ChunkLibrary.FromFolder("Assets/Resources/Content/chunks");
    static ChunkLibrary _lib;
    static readonly uint[] Seeds = Enumerable.Range(0, 120).Select(i => (uint)(i * 104729 + 7)).ToArray();

    static readonly Dictionary<EntityKind, int> Cost = new Dictionary<EntityKind, int>
    {
        { EntityKind.CameraFixed, 2 }, { EntityKind.CameraSweep, 3 }, { EntityKind.Drone, 4 }, { EntityKind.IceVent, 4 },
        { EntityKind.TrackerIce, 5 }, { EntityKind.Lantern, 4 }, { EntityKind.Sentinel, 4 }, { EntityKind.Splitter, 5 },
        { EntityKind.Daemon, 7 }, { EntityKind.Watcher, 6 }, { EntityKind.Glitch, 2 },
    };

    [Test] public void Chunks_PassAuthoringRules()
    {
        foreach (var c in Lib.chunks)
            CollectionAssert.IsEmpty(ChunkLint.Check(c), c.id);
        for (int act = 1; act <= 3; act++)
        {
            Assert.GreaterOrEqual(Lib.chunks.Count(c => c.act == act), 10, $"act {act} chunks");
            Assert.IsTrue(Lib.chunks.Any(c => c.act == act && c.tags.Contains("side") && c.tiles.Any(r => r.Contains('A'))), $"act {act} anomaly wall");
        }
    }

    [Test] public void Orientation_MovesSocketsWithTiles()
    {
        var c = new Chunk { n = SocketKind.Door, e = SocketKind.SourceOnly, s = SocketKind.Wall, w = SocketKind.Wall };
        var s = new int[4];
        for (int o = 0; o < 8; o++)
        {
            RoomGen.Sockets(c, o, s);
            // the north door opening (row 0, col 7) and east gate (row 7, col 15) must land on the sides Sockets reports
            RoomGen.Map(0, 7, o, out int r1, out int c1);
            RoomGen.Map(7, 15, o, out int r2, out int c2);
            Assert.AreEqual((int)SocketKind.Door, s[Side(r1, c1)], $"o={o}");
            Assert.AreEqual((int)SocketKind.SourceOnly, s[Side(r2, c2)], $"o={o}");
        }
    }

    static int Side(int r, int c) => r == 0 ? 0 : c == 15 ? 1 : r == 15 ? 2 : 3;

    [Test] public void Act1_FollowsEnemyIntroTable()
    {
        var allowed = new[]
        {
            new[] { EntityKind.CameraFixed, EntityKind.IceVent },
            new[] { EntityKind.CameraFixed, EntityKind.IceVent, EntityKind.CameraSweep },
            new[] { EntityKind.CameraFixed, EntityKind.IceVent, EntityKind.CameraSweep, EntityKind.Drone },
            new[] { EntityKind.CameraFixed, EntityKind.IceVent, EntityKind.CameraSweep, EntityKind.Drone, EntityKind.TrackerIce },
        };
        for (int node = 0; node < 4; node++)
            foreach (var seed in Seeds.Take(60))
            {
                var kinds = Threats(Generator.GenerateRoom(seed, 1, node, 0, Lib)).Select(e => e.kind).Distinct().ToList();
                foreach (var k in kinds) CollectionAssert.Contains(allowed[node], k, $"node {node} seed {seed}");
                Assert.LessOrEqual(kinds.Count, 3, "act 1: <= 3 enemy types per room");
            }
    }

    [TestCase(1, 0, 0), TestCase(1, 3, 10), TestCase(2, 2, 0), TestCase(3, 3, 20)]
    public void Threats_StayWithinBudgetAndTypeCap(int act, int node, int heat)
    {
        foreach (var seed in Seeds.Take(60))
        {
            var room = Generator.GenerateRoom(seed, act, node, heat, Lib);
            var threats = Threats(room).ToList();
            Assert.LessOrEqual(threats.Sum(e => Cost[e.kind]), 10 + 6 * act + 2 * node + heat / 2, $"seed {seed}");
            Assert.LessOrEqual(threats.Where(e => e.kind != EntityKind.Glitch).Select(e => e.kind).Distinct().Count(), act + 2, $"seed {seed}");
            Assert.Greater(threats.Count, 0, $"seed {seed}");
        }
    }

    [TestCase(2), TestCase(3)]
    public void Act2Plus_HasSourceOnlyGate(int act)
    {
        foreach (var seed in Seeds)
        {
            var room = Generator.GenerateRoom(seed, act, 1, 0, Lib);
            Assert.Greater(Gates(room), 0, $"seed {seed}");
        }
    }

    /// '#' socket openings on chunk boundaries (both sides of the boundary are render wall).
    static int Gates(RoomSpec room)
    {
        int n = 0;
        for (int r = 0; r < room.gridH; r++)
            for (int c = 0; c < room.gridW; c++)
            {
                if (c + 1 < room.gridW && Enumerable.Range(6, 4).All(i => room.tiles[r * 16 + i][c * 16 + 15] == '#' && room.tiles[r * 16 + i][c * 16 + 16] == '#')) n++;
                if (r + 1 < room.gridH && Enumerable.Range(6, 4).All(i => room.tiles[r * 16 + 15][c * 16 + i] == '#' && room.tiles[r * 16 + 16][c * 16 + i] == '#')) n++;
            }
        return n;
    }

    [Test] public void Entities_HaveUniqueIdsAndSitOnTheirMarkers()
    {
        foreach (var seed in Seeds.Take(60))
            for (int act = 1; act <= 3; act++)
            {
                var room = Generator.GenerateRoom(seed, act, 3, 10, Lib);
                Assert.AreEqual(room.gridH * 16, room.tiles.Length);
                Assert.AreEqual(room.entities.Count, room.entities.Select(e => e.id).Distinct().Count(), $"seed {seed}");
                Assert.AreEqual(1, room.entities.Count(e => e.kind == EntityKind.Spawn));
                Assert.AreEqual(1, room.entities.Count(e => e.kind == EntityKind.Exit));
                Assert.LessOrEqual(room.entities.Count(e => e.kind == EntityKind.AnomalyWall), 1);
                foreach (var e in room.entities)
                {
                    char ch = room.tiles[(int)(-e.z / 0.5f)][(int)(e.x / 0.5f)];
                    char want = e.kind == EntityKind.Spawn ? 'S' : e.kind == EntityKind.Exit ? 'E' : e.kind == EntityKind.Terminal ? 'T'
                        : e.kind == EntityKind.AnomalyWall ? 'A' : e.kind == EntityKind.Glitch ? 'G'
                        : e.kind == EntityKind.Drone || e.kind == EntityKind.Lantern ? 'P'
                        : e.kind == EntityKind.CameraFixed || e.kind == EntityKind.CameraSweep || e.kind == EntityKind.Watcher ? 'C' : 'I';
                    Assert.AreEqual(want, ch, $"{e.id} seed {seed}");
                }
            }
    }

    [Test] public void Rooms_MirrorBothWays()
    {
        var mirrored = Seeds.Select(s => Generator.GenerateRoom(s, 1, 1, 0, Lib).mirror).ToList();
        Assert.That(mirrored.Count(m => m), Is.InRange(35, 85));
    }

    [Test] public void Par_RoundsAndClamps()
    {
        Assert.AreEqual(45f, RoomGen.Par(10f, 1));
        Assert.AreEqual(75f, RoomGen.Par(50f, 2));    // 80 -> clamped
        Assert.AreEqual(65f, RoomGen.Par(40f, 3));    // 64 -> 65
        Assert.AreEqual(90f, RoomGen.Par(56.25f, 3)); // act 3 has no upper clamp
        Assert.AreEqual(45f, RoomGen.Par(20f, 3));
    }

    [Test] public void EmptyLibrary_FallsBackToAnOriginalRoom()
    {
        var room = Generator.GenerateRoom(5, 2, 1, 0, new ChunkLibrary());
        Assert.AreEqual("Room_04", room.fallbackRoom);
        Assert.AreEqual(RoomGen.Attempts, RoomGen.LastAttempts);
        Assert.AreEqual("Room_01", Generator.GenerateRoom(4, 1, 1, 0, new ChunkLibrary()).fallbackRoom);
    }

    [Test] public void ChunkJson_ParsesSocketNames()
    {
        var lib = ChunkLibrary.FromJson("{\"chunks\":[{\"id\":\"x\",\"act\":2,\"n\":\"door\",\"e\":\"source_only\",\"s\":\"wall\",\"w\":\"door\",\"budget\":3,\"tags\":[\"side\"],\"tiles\":[]}]}");
        var c = lib.chunks.Single();
        Assert.AreEqual(SocketKind.SourceOnly, c.e);
        Assert.AreEqual(SocketKind.Wall, c.s);
        Assert.AreEqual(3, c.budget);
        CollectionAssert.AreEqual(new[] { "side" }, c.tags);
    }

    // ---------- validator on hand-made rooms ----------

    static RoomSpec Hand(string[] rows, params (EntityKind kind, int r, int c, float yaw)[] ents)
    {
        var room = new RoomSpec { tiles = rows };
        foreach (var e in ents)
            room.entities.Add(new EntitySpawn { kind = e.kind, x = (e.c + 0.5f) * 0.5f, z = -(e.r + 0.5f) * 0.5f, yaw = e.yaw, id = e.kind.ToString() });
        return room;
    }

    static string[] Corridor(int len, string middle)
    {
        string wall = new string('X', len + 2);
        string row = "X" + middle.PadRight(len, '.') + "X";
        return new[] { wall, row, row, row, wall };
    }

    [Test] public void Validate_OpenCorridor_IsFastAndFlipFree()
    {
        var v = Generator.Validate(Hand(Corridor(20, ""), (EntityKind.Spawn, 2, 1, 90f), (EntityKind.Exit, 2, 20, 0f)));
        CollectionAssert.IsEmpty(v.reasons);
        Assert.IsTrue(v.reachable && v.flipFreePathExists);
        Assert.AreEqual(1.9f, v.bestPathSeconds, 0.01f);   // 19 tiles at 0.1 s
        Assert.AreEqual(0f, v.minMeltPeak);
    }

    [Test] public void Validate_ThinWall_CostsTwoFlips()
    {
        var v = Generator.Validate(Hand(Corridor(20, "..........#"), (EntityKind.Spawn, 2, 1, 90f), (EntityKind.Exit, 2, 20, 0f)));
        CollectionAssert.IsEmpty(v.reasons);
        Assert.IsFalse(v.flipFreePathExists);
        Assert.Greater(v.minMeltPeak, 10f);   // in: +5, through: +1.2 per tile, out: +5
        Assert.Less(v.minMeltPeak, 15f);
    }

    [Test] public void Validate_LongWall_RunsOutOfMelt()
    {
        var v = Generator.Validate(Hand(Corridor(100, "..." + new string('#', 94)), (EntityKind.Spawn, 2, 1, 90f), (EntityKind.Exit, 2, 99, 0f)));
        Assert.IsTrue(v.reachable);
        CollectionAssert.Contains(v.reasons, "melt_budget");
    }

    [Test] public void Validate_SealedExit_IsUnreachable()
    {
        var v = Generator.Validate(Hand(Corridor(20, "..........X"), (EntityKind.Spawn, 2, 1, 90f), (EntityKind.Exit, 2, 20, 0f)));
        Assert.IsFalse(v.reachable);
        CollectionAssert.Contains(v.reasons, "unreachable_exit");
    }

    [Test] public void Validate_FlagsIceNearEntryAndWatchedTerminal()
    {
        var v = Generator.Validate(Hand(Corridor(30, ""), (EntityKind.Spawn, 2, 1, 90f), (EntityKind.Exit, 2, 30, 0f),
            (EntityKind.IceVent, 2, 7, 0f), (EntityKind.Terminal, 2, 20, 0f), (EntityKind.CameraFixed, 2, 26, 270f)));
        CollectionAssert.Contains(v.reasons, "ice_near_entry");     // 3 m away
        CollectionAssert.Contains(v.reasons, "terminal_in_cone");
    }

    [Test] public void Validate_CameraCone_IsCrossedInSource()
    {
        // camera at the far end looks back down the corridor; the route must cross its cone in SOURCE
        var v = Generator.Validate(Hand(Corridor(40, ""), (EntityKind.Spawn, 2, 1, 90f), (EntityKind.Exit, 2, 40, 0f),
            (EntityKind.CameraFixed, 1, 40, 270f)));
        CollectionAssert.IsEmpty(v.reasons);
        Assert.IsFalse(v.flipFreePathExists);
        Assert.AreEqual(0f, v.maxCameraExposureOnBestPath);
        Assert.Greater(v.minMeltPeak, 5f);
    }

    [Test] public void Cone_ExactTestMatchesTrigonometry()
    {
        var g = Enumerable.Repeat('.', 61 * 61).ToArray();
        int cam = 30 * 61 + 30;
        for (int k = 0; k < 8; k++)
            foreach (float half in new[] { 30f, 45f })
                for (int t = 0; t < g.Length; t++)
                {
                    int dx = t % 61 - 30, dy = 30 - t / 61;
                    if (dx * dx + dy * dy > 24 * 24 || t == cam) continue;
                    double diff = Math.Atan2(dx, dy) * 180.0 / Math.PI - k * 45;
                    diff -= 360.0 * Math.Floor((diff + 180.0) / 360.0);
                    if (Math.Abs(Math.Abs(diff) - half) < 1e-6) continue;   // exact boundary: integer test includes it
                    Assert.AreEqual(Math.Abs(diff) < half, RoomCheck.Sees(g, 61, cam, k * 45f, half, 12f, t), $"k={k} half={half} d=({dx},{dy})");
                }
    }

    // ---------- run graph ----------

    [Test] public void RunGraph_OffersChoicesAndAtMostOneAnomalyPerAct()
    {
        foreach (var seed in Seeds)
        {
            var run = Generator.GenerateRun(seed, 0);
            var all = run.acts.SelectMany(a => a).ToList();
            Assert.AreEqual(all.Count, all.Select(n => n.id).Distinct().Count());
            Assert.AreEqual(all.Count, all.Select(n => n.roomSeed).Distinct().Count());
            foreach (var act in run.acts)
            {
                Assert.LessOrEqual(act.Count(n => n.type == NodeType.Anomaly), 1);
                foreach (var n in act)
                {
                    if (n.index < 3) Assert.GreaterOrEqual(n.next.Count, 2, n.id);
                    if (n.index == 3) Assert.AreEqual(act.Single(b => b.type == NodeType.Boss).id, n.next.Single());
                    foreach (var x in n.next) Assert.AreEqual(n.index + 1, act.Single(m => m.id == x).index, "edges go to the next row");
                }
                Assert.IsTrue(act.Where(n => n.index < 4).All(n => n.type != NodeType.Boss));
            }
        }
        Assert.AreEqual(JsonConvert.SerializeObject(Generator.GenerateRun(9, 3)), JsonConvert.SerializeObject(Generator.GenerateRun(9, 3)));
    }

    static IEnumerable<EntitySpawn> Threats(RoomSpec room) => room.entities.Where(e => Cost.ContainsKey(e.kind));
}
