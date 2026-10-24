using System.Collections.Generic;
using System.Linq;
using Desync.Content;
using Desync.Gen;
using Desync.Rules;
using NUnit.Framework;
using UnityEngine;

namespace Desync.Flow.Tests
{
    /// The run's numbers and deterministic rolls (RunRules), on the shipped content.
    public class RunRulesTests
    {
        static Catalog cat;
        static ChunkLibrary lib;
        static Catalog Cat => cat ??= Catalog.FromJson(Resources.Load<TextAsset>("Content/exploits").text, Resources.Load<TextAsset>("Content/mutators").text);
        static ChunkLibrary Lib => lib ??= ChunkLibrary.FromFolder("Assets/Resources/Content/chunks");

        static RunNode Node(NodeType type, int act, int index, uint seed) =>
            new RunNode { id = "a" + act + "n" + index, type = type, act = act, index = index, roomSeed = seed };

        [Test]
        public void DraftPool_IsTheSixteenM2Exploits_AndOffersComeFromIt()
        {
            var pool = RunRules.DraftPool(Cat);
            CollectionAssert.AreEquivalent(RunRules.M2Exploits, pool.exploits.Select(e => e.id));
            var held = new List<Exploit>();
            for (uint s = 1; s <= 200; s++)
            {
                var offer = Loadout.RollOffer(new Mulberry32(s), pool, held, s % 3 == 0);
                Assert.AreEqual(3, offer.Count);
                Assert.AreEqual(3, offer.Select(e => e.id).Distinct().Count());
                Assert.IsTrue(offer.All(e => RunRules.M2Exploits.Contains(e.id)));
                if (s % 3 == 0) Assert.IsTrue(offer.All(e => e.rarity != Rarity.C), "elite / boss drafts are Rare+");
            }
        }

        [Test]
        public void Fragments_PerRoom_AndADeathBanksAtLeast40PercentOfAWin()
        {
            Assert.AreEqual(10, RunRules.RoomFragments(Medal.Bronze, 'C'));
            Assert.AreEqual(10, RunRules.RoomFragments(Medal.Silver, 'A'));
            Assert.AreEqual(20, RunRules.RoomFragments(Medal.Gold, 'B'));
            Assert.AreEqual(35, RunRules.RoomFragments(Medal.Sync, 'S'));
            foreach (int earned in new[] { 0, 1, 10, 35, 99, 250 })
            {
                Assert.AreEqual(earned, RunRules.Bank(earned, false));
                Assert.GreaterOrEqual(RunRules.Bank(earned, true), 0.4 * earned + RunRules.FragDeath);
                Assert.LessOrEqual(RunRules.Bank(earned, true), earned + RunRules.FragDeath);
            }
            Assert.AreEqual(43, RunRules.Bank(100, true));
        }

        [Test]
        public void Cycles_ByGrade_AndElite()
        {
            Assert.AreEqual(10, RunRules.RoomCycles('C', false));
            Assert.AreEqual(25, RunRules.RoomCycles('S', false));
            Assert.AreEqual(30, RunRules.RoomCycles('A', true));
        }

        [Test]
        public void RoomMutators_EliteGetsTwoDistinctTags_BossNone_FirstRoomNone_GazeOnlyFromNode4()
        {
            for (uint s = 1; s <= 300; s++)
            {
                int index = (int)(s % 4);
                var elite = RunRules.RoomMutators(new Mulberry32(s), Cat, Node(NodeType.Elite, 1, index, s));
                Assert.AreEqual(2, elite.Count);
                Assert.AreNotEqual(elite[0].tag, elite[1].tag);
                Assert.IsTrue(elite.All(m => m.runMutator));
                if (index < 3) Assert.IsFalse(elite.Any(m => m.id == "gaze"), "GAZE needs node >= 4");
                Assert.AreEqual(0, RunRules.RoomMutators(new Mulberry32(s), Cat, Node(NodeType.Boss, 1, 4, s)).Count);
                Assert.AreEqual(0, RunRules.RoomMutators(new Mulberry32(s), Cat, Node(NodeType.Infil, 1, 0, s)).Count);
                var infil = RunRules.RoomMutators(new Mulberry32(s), Cat, Node(NodeType.Infil, 1, index, s));
                Assert.LessOrEqual(infil.Count, 2);
                Assert.AreEqual(infil.Count, infil.Select(m => m.tag).Distinct().Count());
            }
        }

        [Test]
        public void Market_ThreeExploitsPricedByRarity_RemovalWhenHolding_BackupWhenOnSale()
        {
            var pool = RunRules.DraftPool(Cat);
            var none = new List<Exploit>();
            var stock = RunRules.MarketStock(new Mulberry32(5), pool, none, true);
            var ex = stock.Where(i => i.kind == "exploit").ToList();
            Assert.AreEqual(3, ex.Count);
            foreach (var i in ex) Assert.AreEqual(RunRules.Price(Cat.Exploit(i.exploitId).rarity), i.price);
            Assert.IsFalse(stock.Any(i => i.kind == "removal"), "nothing held: nothing to remove");
            Assert.AreEqual(1, stock.Count(i => i.kind == "backup"));
            Assert.AreEqual(1, stock.Count(i => i.kind == "reroll"));

            var held = new List<Exploit> { Cat.Exploit("rootkit") };
            stock = RunRules.MarketStock(new Mulberry32(5), pool, held, false);
            Assert.AreEqual(1, stock.Count(i => i.kind == "removal"));
            Assert.IsFalse(stock.Any(i => i.kind == "backup"));
            Assert.IsFalse(stock.Any(i => i.exploitId == "rootkit"), "never sells one already held");
        }

        [Test]
        public void Mirror_ForcesAMirroredRoom()
        {
            for (uint s = 1; s <= 12; s++)
            {
                var spec = RunRules.Room(Node(NodeType.Infil, 1 + (int)(s % 2), 1, s * 7919u), true, Lib);
                Assert.IsTrue(spec.mirror || spec.fallbackRoom != null, "seed " + s);
            }
        }

        [Test]
        public void DeadZones_OnPlainFloor_ClearOfSpawnExitTerminals_ApartAndDeterministic()
        {
            int rooms = 0, withStrips = 0;
            for (uint s = 1; s <= 30; s++)
            {
                var spec = Generator.GenerateRoom(s * 104729u, 1 + (int)(s % 2), (int)(s % 4), 0, Lib);
                if (spec.tiles == null) continue;
                rooms++;
                var strips = RunRules.DeadZoneStrips(spec, Rng.Substream(spec.seed, "deadzones"), 2);
                var again = RunRules.DeadZoneStrips(spec, Rng.Substream(spec.seed, "deadzones"), 2);
                CollectionAssert.AreEqual(strips, again, "deterministic");
                Assert.LessOrEqual(strips.Count, 2);
                if (strips.Count > 0) withStrips++;
                foreach (var r in strips)
                {
                    Assert.IsTrue(r.width * r.height == RunRules.StripLong * RunRules.StripShort);
                    for (int y = r.yMin; y < r.yMax; y++)
                        for (int x = r.xMin; x < r.xMax; x++)
                            Assert.AreEqual('.', spec.tiles[y][x], "strip on plain floor");
                    RunRules.StripMetres(r, out float x0, out float x1, out float z0, out float z1);
                    foreach (var e in spec.entities.Where(e => e.kind == EntityKind.Spawn || e.kind == EntityKind.Exit || e.kind == EntityKind.Terminal))
                    {
                        float dx = Mathf.Max(0f, x0 - e.x, e.x - x1), dz = Mathf.Max(0f, z0 - e.z, e.z - z1);
                        Assert.GreaterOrEqual(Mathf.Sqrt(dx * dx + dz * dz), RunRules.StripClearance - 1e-4f, e.kind + " too close");
                    }
                }
                if (strips.Count == 2)
                {
                    var a = strips[0];
                    var b = strips[1];
                    int gap = Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax, a.yMin - b.yMax, b.yMin - a.yMax);
                    Assert.GreaterOrEqual(gap, RunRules.StripGap);
                }
            }
            Assert.Greater(rooms, 20);
            Assert.Greater(withStrips, rooms * 3 / 4, "most generated rooms have room for a strip");
        }
    }
}
