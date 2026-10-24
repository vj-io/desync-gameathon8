using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Desync.Gen;
using Desync.Rules;
using NUnit.Framework;

namespace Desync.Flow.Tests
{
    /// Story shards, endings and Endless Overclock: the shipped content (Content/shards.json), the save bookkeeping, where a
    /// room's shard goes (on generated rooms from the shipped chunks, checked with an independent sight test), the first-half-hour
    /// guarantee, the true ending's gate and Overclock's numbers. Engine-free: content and chunks are read from disk.
    public class StoryShardTests
    {
        static ShardContent content;
        static ShardContent Content => content ??= ShardContent.FromJson(File.ReadAllText("Assets/Resources/Content/shards.json"));
        static ChunkLibrary lib;
        static ChunkLibrary Lib => lib ??= ChunkLibrary.FromFolder("Assets/Resources/Content/chunks");

        // ---------------------------------------------------------------- content

        [Test]
        public void Content_ThirtyShards_TenPerAct_InIdOrder_OneToThreeSentences()
        {
            Assert.AreEqual(ShardBook.Total, Content.shards.Count);
            for (int a = 1; a <= ShardBook.Acts; a++)
            {
                var act = Content.shards.Where(s => s.act == a).ToList();
                Assert.AreEqual(ShardBook.PerAct, act.Count, "act " + a);
                for (int n = 1; n <= ShardBook.PerAct; n++) Assert.AreEqual(ShardBook.Id(a, n), act[n - 1].id);
                StringAssert.DoesNotStartWith("ACT ", Content.Act(a), "act " + a + " has a title");
            }
            foreach (var s in Content.shards)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.title), s.id);
                int sentences = Regex.Matches(s.text, @"[.!?](\s|$)").Count;
                Assert.That(sentences, Is.InRange(1, 3), s.id + ": " + s.text);
                Assert.LessOrEqual(s.text.Length, 160, s.id + " fits the reader");
            }
            Assert.AreEqual("shard_a1_03", ShardBook.Id(1, 3));
        }

        [Test]
        public void Content_EachEndingHasItsOwnSequence_EndingInAPrompt()
        {
            foreach (var id in Endings.All)
            {
                var e = Content.Ending(id);
                Assert.IsNotNull(e, id);
                Assert.IsNotNull(Content.Ending(id.ToUpperInvariant()), "BossEvents.Ending names are capitalised");
                var kinds = e.beats.Select(b => b.kind).ToList();
                Assert.AreEqual("wake", kinds.Last(), id + " ends on the prompt");
                CollectionAssert.Contains(kinds, "logo", id);
                CollectionAssert.Contains(kinds, "face", id + ": the camera turns to the face");
                CollectionAssert.Contains(kinds, "push", id + ": and moves");
                CollectionAssert.IsOrdered(e.beats.Select(b => b.t).ToList(), id + " beats in time order");
            }
            CollectionAssert.Contains(Content.Ending(Endings.Desync).beats.Select(b => b.kind).ToList(), "split", "the true ending shows both layers at once");
            var texts = Endings.All.Select(id => string.Join("|", Content.Ending(id).beats.Select(b => b.text))).ToList();
            Assert.AreEqual(3, texts.Distinct().Count(), "three distinct sequences");
        }

        // ---------------------------------------------------------------- save

        [Test]
        public void Book_NextIsTheActsLowestMissing_CollectOnceAndOnlyShards()
        {
            var save = new SaveV3();
            Assert.AreEqual("shard_a2_01", ShardBook.Next(save, 2));
            Assert.IsTrue(ShardBook.Collect(save, "shard_a2_01"));
            Assert.IsFalse(ShardBook.Collect(save, "shard_a2_01"), "once");
            Assert.IsFalse(ShardBook.Collect(save, "shard_a4_01"));
            Assert.IsFalse(ShardBook.Collect(save, "kill_auditor"));
            Assert.IsTrue(ShardBook.Collect(save, "shard_a2_03"));
            Assert.AreEqual("shard_a2_02", ShardBook.Next(save, 2), "the gap first: each act reads in order");
            Assert.AreEqual(2, ShardBook.Count(save, 2));
            Assert.AreEqual(0, ShardBook.Count(save, 1));
            Assert.AreEqual(2, ShardBook.Count(save));
            for (int n = 1; n <= ShardBook.PerAct; n++) ShardBook.Collect(save, ShardBook.Id(2, n));
            Assert.IsNull(ShardBook.Next(save, 2), "act complete");
            Assert.IsNull(ShardBook.Next(save, 0));
            Assert.IsNull(ShardBook.Next(save, 4));
            Assert.AreEqual(ShardBook.PerAct, ShardBook.Count(save));
        }

        // ---------------------------------------------------------------- endings

        [Test]
        public void Endings_DesyncNeedsThreeArchitectKillsAndEveryShard()
        {
            var save = new SaveV3();
            Assert.AreEqual(0, Endings.ArchitectKills(save));
            save.unlocks.Add(RunUnlocks.KillFlag(3));
            Assert.AreEqual(1, Endings.ArchitectKills(save), "a kill from before the count existed");
            Endings.RecordArchitectKill(save);
            Assert.AreEqual(2, Endings.ArchitectKills(save));
            save.settings[Endings.KillsKey] = "0";
            Assert.AreEqual(0, Endings.ArchitectKills(save), "a pinned count wins over the first-kill flag");
            for (int a = 1; a <= 3; a++) for (int n = 1; n <= ShardBook.PerAct; n++) ShardBook.Collect(save, ShardBook.Id(a, n));
            Endings.RecordArchitectKill(save);
            Endings.RecordArchitectKill(save);
            Assert.IsFalse(Endings.DesyncReady(save), "two kills");
            Endings.RecordArchitectKill(save);
            Assert.IsTrue(Endings.DesyncReady(save));
            save.unlocks.Remove(ShardBook.Id(3, 10));
            Assert.IsFalse(Endings.DesyncReady(save), "29 shards");
        }

        [Test]
        public void Endings_FlagsMatchTheRunsRecord_AndDesyncOpensOverclock()
        {
            foreach (var name in new[] { "Exposed", "Merged", "Desync" })
            {
                var save = new SaveV3();
                Assert.IsTrue(RunUnlocks.RecordEnding(save, name));
                Assert.IsTrue(Endings.Seen(save, name.ToLowerInvariant()), name);
                Assert.AreEqual(name == "Desync", OverclockRun.Unlocked(save), name);
            }
        }

        // ---------------------------------------------------------------- placement

        [Test]
        public void Wants_AnomalyAlways_BossNever_ActOneUntilThree_ElseAboutChance()
        {
            var spec = new RoomSpec { seed = 1, tiles = new[] { "." } };
            Assert.IsTrue(ShardPlacement.Wants(spec, NodeType.Anomaly, 3, 29));
            Assert.IsFalse(ShardPlacement.Wants(spec, NodeType.Boss, 1, 0));
            int hits = 0;
            for (uint s = 1; s <= 2000; s++)
            {
                spec.seed = s * 2654435761u;
                Assert.IsTrue(ShardPlacement.Wants(spec, NodeType.Infil, 1, ShardPlacement.EarlyShards - 1));
                bool w = ShardPlacement.Wants(spec, NodeType.Infil, 2, 12);
                Assert.AreEqual(w, ShardPlacement.Wants(spec, NodeType.Elite, 2, 12), "seeded by the room, not the call");
                if (w) hits++;
            }
            Assert.That(hits / 2000f, Is.InRange(ShardPlacement.Chance - 0.05f, ShardPlacement.Chance + 0.05f));
        }

        [Test]
        public void Spot_OnGeneratedRooms_FreeReachableFloor_OutOfSight_Deterministic()
        {
            int placed = 0, rooms = 0;
            for (int act = 1; act <= 3; act++)
                for (uint s = 1; s <= 40; s++)
                {
                    var spec = Generator.GenerateRoom(s * 7919u + (uint)act, act, (int)(s % 4), 0, Lib);
                    if (spec.tiles == null) continue;   // fell back to a classic room: no shard
                    rooms++;
                    if (!ShardPlacement.Spot(spec, out float x, out float z)) continue;
                    placed++;
                    Assert.IsTrue(ShardPlacement.Spot(spec, out float x2, out float z2) && x2 == x && z2 == z, "deterministic");
                    CheckSpot(spec, x, z, "act " + act + " seed " + spec.seed);
                }
            Assert.Greater(rooms, 100);
            Assert.GreaterOrEqual(placed, rooms * 9 / 10, "nearly every room has a legal spot");
        }

        [Test]
        public void Spot_PrefersTheVaultBehindTheAnomalyWall()
        {
            var tiles = new[]
            {
                "XXXXXXXXXXXXXXXX",
                "X.........X....X",
                "X.........A....X",
                "X.........A....X",
                "X.........A....X",
                "X.........X....X",
                "X.........XXXXXX",
                "X..............X",
                "X..............X",
                "X..............X",
                "XXXXXXXXXXXXXXXX",
            };
            for (uint seed = 1; seed <= 30; seed++)
            {
                var spec = new RoomSpec { seed = seed, tiles = tiles, entities = new List<EntitySpawn>
                {
                    new EntitySpawn { kind = EntityKind.Spawn, x = 1.25f, z = -4.25f },
                    new EntitySpawn { kind = EntityKind.AnomalyWall, x = 5.25f, z = -1.75f },
                } };
                Assert.IsTrue(ShardPlacement.Spot(spec, out float x, out float z));
                int c = (int)(x / 0.5f), r = (int)(-z / 0.5f);
                Assert.That(c, Is.InRange(11, 14), "seed " + seed + ": inside the vault (columns 11-14)");
                Assert.That(r, Is.InRange(1, 5), "seed " + seed);
            }
        }

        [Test]
        public void Spot_StaysOutOfACameraCone_AndOffTheSpawn()
        {
            var tiles = Enumerable.Range(0, 24).Select(r => r == 0 || r == 23 ? new string('X', 24) : "X" + new string('.', 22) + "X").ToArray();
            for (uint seed = 1; seed <= 30; seed++)
            {
                var spec = new RoomSpec { seed = seed, tiles = tiles, entities = new List<EntitySpawn>
                {
                    new EntitySpawn { kind = EntityKind.Spawn, x = 6f, z = -6f },
                    new EntitySpawn { kind = EntityKind.CameraFixed, x = 0.75f, z = -6.25f, yaw = 90f },   // west wall, looking east
                    new EntitySpawn { kind = EntityKind.Lantern, x = 9f, z = -3f },
                } };
                Assert.IsTrue(ShardPlacement.Spot(spec, out float x, out float z));
                CheckSpot(spec, x, z, "seed " + seed);
            }
        }

        /// Free floor, SOURCE-reachable from the spawn, SpawnClear from it, EntityClear from every entity (the anomaly marker
        /// aside), and in no camera cone whose sight line is clear by a sampled test with a 0.1 m margin.
        static void CheckSpot(RoomSpec spec, float x, float z, string what)
        {
            var t = spec.tiles;
            int w = t[0].Length, h = t.Length, c = (int)Math.Floor(x / 0.5f), r = (int)Math.Floor(-z / 0.5f);
            Assert.AreEqual('.', t[r][c], what + ": on plain floor");
            var spawn = spec.entities.First(e => e.kind == EntityKind.Spawn);
            Assert.GreaterOrEqual(Dist(spawn.x, spawn.z, x, z), ShardPlacement.SpawnClear - 0.4f, what + ": off the spawn (tile centres)");
            foreach (var e in spec.entities)
                if (e.kind != EntityKind.AnomalyWall) Assert.GreaterOrEqual(Dist(e.x, e.z, x, z), ShardPlacement.EntityClear - 0.75f, what + ": clear of " + e.kind);
            Assert.IsTrue(Reachable(t, (int)(-spawn.z / 0.5f), (int)(spawn.x / 0.5f), r, c), what + ": reachable in SOURCE");
            foreach (var e in spec.entities)
            {
                float half, range;
                if (e.kind == EntityKind.CameraFixed || e.kind == EntityKind.Watcher) { half = 30f; range = 12f; }
                else if (e.kind == EntityKind.CameraSweep) { half = 45f; range = 10f; }
                else if (e.kind == EntityKind.Lantern) { half = 180f; range = 7f; }
                else continue;
                float dx = x - e.x, dn = z - e.z, d = (float)Math.Sqrt(dx * dx + dn * dn);
                if (d > range - 0.5f) continue;
                double ang = Math.Atan2(dx, dn) * 180.0 / Math.PI - e.yaw;
                ang -= 360.0 * Math.Floor((ang + 180.0) / 360.0);
                if (Math.Abs(ang) > half - 2.0) continue;
                Assert.IsFalse(ClearLine(t, e.x, e.z, x, z), what + ": in the sight of " + e.kind + " at " + e.x + "," + e.z);
            }
        }

        static float Dist(float ax, float az, float bx, float bz) => (float)Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

        static bool Solid(string[] t, float x, float z)
        {
            int c = (int)Math.Floor(x / 0.5f), r = (int)Math.Floor(-z / 0.5f);
            if (r < 0 || r >= t.Length || c < 0 || c >= t[0].Length) return true;
            char ch = t[r][c];
            return ch == '#' || ch == 'X' || ch == 'A' || ch == ' ';
        }

        /// Sampled sight line, ends trimmed by a tile, every sample 0.1 m clear of solid tiles in each axis.
        static bool ClearLine(string[] t, float ax, float az, float bx, float bz)
        {
            float len = Dist(ax, az, bx, bz);
            for (float s = 0.5f; s < len - 0.5f; s += 0.05f)
            {
                float px = ax + (bx - ax) * s / len, pz = az + (bz - az) * s / len;
                if (Solid(t, px - 0.1f, pz) || Solid(t, px + 0.1f, pz) || Solid(t, px, pz - 0.1f) || Solid(t, px, pz + 0.1f)) return false;
            }
            return true;
        }

        static bool Reachable(string[] t, int r0, int c0, int r1, int c1)
        {
            int h = t.Length, w = t[0].Length;
            var seen = new bool[h, w];
            var q = new Queue<(int, int)>();
            q.Enqueue((r0, c0)); seen[r0, c0] = true;
            while (q.Count > 0)
            {
                var (r, c) = q.Dequeue();
                if (r == r1 && c == c1) return true;
                foreach (var (dr, dc) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int rr = r + dr, cc = c + dc;
                    if (rr < 0 || rr >= h || cc < 0 || cc >= w || seen[rr, cc] || t[rr][cc] == 'X' || t[rr][cc] == ' ') continue;
                    seen[rr, cc] = true; q.Enqueue((rr, cc));
                }
            }
            return false;
        }

        /// The first half hour: a fresh profile holds the first three shards after at most two act 1 runs (a QUICK run is ~6 min),
        /// taking room nodes over Market / Cooldown when the map offers both, plus the AUDITOR kill's shard.
        [Test]
        public void FirstThreeShards_WithinTwoActOneRuns()
        {
            for (uint seed = 1; seed <= 30; seed++)
            {
                var save = new SaveV3();
                for (int run = 0; run < 2 && ShardBook.Count(save) < ShardPlacement.EarlyShards; run++)
                {
                    var nodes = Generator.GenerateRun(seed + (uint)run * 1000u, 0).acts[0];
                    var cur = nodes.Where(n => n.index == 0).OrderBy(n => n.type == NodeType.Infil ? 0 : 1).First();
                    while (true)
                    {
                        if (cur.type == NodeType.Boss) { ShardBook.Collect(save, ShardBook.Next(save, 1)); break; }
                        if (cur.type != NodeType.Market && cur.type != NodeType.Cooldown)
                        {
                            var spec = RunRules.Room(cur, false, Lib);
                            string id = ShardBook.Next(save, 1);
                            if (id != null && ShardPlacement.Wants(spec, cur.type, 1, ShardBook.Count(save)) && ShardPlacement.Spot(spec, out _, out _))
                                ShardBook.Collect(save, id);
                        }
                        cur = cur.next.Select(id => nodes.First(n => n.id == id))
                                  .OrderBy(n => n.type == NodeType.Market || n.type == NodeType.Cooldown ? 1 : 0).First();
                    }
                }
                Assert.GreaterOrEqual(ShardBook.Count(save, 1), ShardPlacement.EarlyShards, "seed " + seed);
            }
        }

        // ---------------------------------------------------------------- Endless Overclock

        [Test]
        public void Overclock_ActsCycleEveryThreeRooms_HeatAndScoreRise()
        {
            CollectionAssert.AreEqual(new[] { 1, 1, 1, 2, 2, 2, 3, 3, 3, 1, 1, 1, 2 }, Enumerable.Range(0, 13).Select(OverclockRun.ActOf).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 2, 2, 2, 4, 4, 4, 6 }, Enumerable.Range(0, 10).Select(OverclockRun.HeatOf).ToArray());
            Assert.AreEqual(0f, OverclockRun.ScoreBonusOf(0));
            Assert.AreEqual(0.5f, OverclockRun.ScoreBonusOf(5), 1e-5f);
            var a = OverclockRun.NodeFor(77u, 4);
            var b = OverclockRun.NodeFor(77u, 4);
            Assert.AreEqual(a.roomSeed, b.roomSeed);
            Assert.AreNotEqual(a.roomSeed, OverclockRun.NodeFor(77u, 5).roomSeed);
            Assert.AreEqual(NodeType.Infil, a.type);
            Assert.AreEqual(2, a.act);
            Assert.AreEqual(1, a.index);
            var m = new Modifiers { ScoreMult = 1f + OverclockRun.ScoreBonusOf(200) };
            Assert.AreEqual(8f, m.Clamped().ScoreMult, "the global score cap still holds");
        }

        [Test]
        public void Overclock_BestKeepsMoreRoomsThenMoreScore()
        {
            var save = new SaveV3();
            OverclockRun.Best(save, out int r, out int s);
            Assert.AreEqual((0, 0), (r, s));
            Assert.IsFalse(OverclockRun.RecordBest(save, 0, 0));
            Assert.IsTrue(OverclockRun.RecordBest(save, 4, 9000));
            Assert.IsFalse(OverclockRun.RecordBest(save, 3, 50000), "fewer rooms");
            Assert.IsFalse(OverclockRun.RecordBest(save, 4, 9000), "a tie is not a record");
            Assert.IsTrue(OverclockRun.RecordBest(save, 4, 9100));
            Assert.AreEqual("4,9100", save.settings[OverclockRun.BestKey]);
            save.settings[OverclockRun.BestKey] = "garbage";
            OverclockRun.Best(save, out r, out s);
            Assert.AreEqual((0, 0), (r, s), "a broken value reads as no best");
        }

        [Test]
        public void Overclock_LockedUntilDesync_OrTheDebugFlag()
        {
            var save = new SaveV3();
            try
            {
                OverclockRun.DebugUnlock = false;
                Assert.IsFalse(OverclockRun.Unlocked(save));
                OverclockRun.DebugUnlock = true;
                Assert.IsTrue(OverclockRun.Unlocked(save));
                OverclockRun.DebugUnlock = false;
                save.unlocks.Add(Endings.Flag(Endings.Desync));
                Assert.IsTrue(OverclockRun.Unlocked(save));
            }
            finally { OverclockRun.DebugUnlock = false; }
        }
    }
}
