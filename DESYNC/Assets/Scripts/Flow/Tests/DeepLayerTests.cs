using System.Collections.Generic;
using System.IO;
using System.Linq;
using Desync.Content;
using Desync.Rules;
using NUnit.Framework;

namespace Desync.Flow.Tests
{
    /// Deep Layer rules (DeepRules, RunUnlocks) on the shipped mutators. Engine-free: the catalog is read from disk.
    public class DeepLayerTests
    {
        static Catalog cat;
        static Catalog Cat => cat ??= Catalog.FromJson(File.ReadAllText("Assets/Resources/Content/exploits.json"), File.ReadAllText("Assets/Resources/Content/mutators.json"));

        static List<MutatorPick> P(params (string id, int rank)[] picks) => picks.Select(p => new MutatorPick(p.id, p.rank)).ToList();

        [Test]
        public void Presets_AddUpToTheirLevel_AndAreLegal()
        {
            for (int level = 1; level <= DeepRules.MaxLevel; level++)
            {
                var preset = DeepRules.Preset(level);
                Assert.AreEqual(level, DeepRules.Heat(preset, Cat), "DL " + level);
                CollectionAssert.IsEmpty(DeepRules.Check(level, preset, Cat, DeepRules.MaxLevel), "DL " + level);
                CollectionAssert.AreEqual(DeepRules.Canonical(preset).Select(p => p.id), preset.Select(p => p.id));
            }
            CollectionAssert.AreEquivalent(new[] { "tight_deadline" }, DeepRules.Preset(1).Select(p => p.id));
        }

        [Test]
        public void Check_RejectsLockedLevels_WrongHeat_BadPicks()
        {
            CollectionAssert.Contains(DeepRules.Check(0, DeepRules.Preset(1), Cat, 10), "locked:0");
            CollectionAssert.Contains(DeepRules.Check(4, DeepRules.Preset(4), Cat, 3), "locked:4", "one above the best won");
            CollectionAssert.Contains(DeepRules.Check(11, DeepRules.Preset(10), Cat, 20), "locked:11", "M3 stops at DL 10");
            CollectionAssert.Contains(DeepRules.Check(3, DeepRules.Preset(2), Cat, 10), "heat:2/3");
            CollectionAssert.Contains(DeepRules.Check(4, P(("hardened_ice", 1), ("hardened_ice", 3)), Cat, 10), "duplicate:hardened_ice");
            CollectionAssert.Contains(DeepRules.Check(1, P(("double_melt", 1)), Cat, 10), "rank:double_melt:1", "a run mutator is not a heat pick");
            CollectionAssert.Contains(DeepRules.Check(2, P(("hot_silicon", 4)), Cat, 10), "rank:hot_silicon:4");
            CollectionAssert.Contains(DeepRules.Check(1, P(("no_such", 1)), Cat, 10), "unknown:no_such");
            CollectionAssert.IsEmpty(DeepRules.Check(7, P(("gaze_everywhere", 1), ("double_patrol", 1), ("tracker_swarm", 1)), Cat, 10));
        }

        /// The same picks in any order fold into the same Modifiers and flags; heat stacks with room mutators (adds, then muls).
        [Test]
        public void Picks_FoldIntoModsAndFlags_Deterministically()
        {
            var picks = P(("tracker_swarm", 1), ("hot_silicon", 2), ("double_patrol", 1), ("hair_trigger", 2), ("fewer_choices", 1));
            var room = new List<Mutator> { Cat.mutators.Find(m => m.id == "double_melt") };
            var held = new List<Exploit> { Cat.Exploit("burn_rate") };
            var a = DeepRules.Build(held, room, 10, picks, Cat, null);
            for (int k = 0; k < 2 * picks.Count; k++)   // every rotation, forwards and reversed
            {
                var order = picks.Skip(k % picks.Count).Concat(picks.Take(k % picks.Count)).ToList();
                if (k >= picks.Count) order.Reverse();
                var b = DeepRules.Build(held, room, 10, order, Cat, null);
                Assert.AreEqual(a.mods, b.mods);
                CollectionAssert.AreEquivalent(a.flags, b.flags);
            }
            Assert.AreEqual((12f + 2f) * 2f, a.mods.SourceRise, 1e-4f, "Hot Silicon adds before DOUBLE_MELT multiplies");
            Assert.AreEqual(0.3f, a.mods.TraceWindow, 1e-4f, "Hair Trigger 2");
            Assert.AreEqual(3f, a.mods.FlipCost, 1e-4f, "Burn Rate held");
            foreach (var f in new[] { "mut:double_melt", "mut:tracker_swarm", "mut:hot_silicon", "mut:double_patrol", "mut:hair_trigger", "mut:fewer_choices",
                                      "flag:tracker_swarm", "flag:double_patrol", "flag:fewer_choices", DeepRules.BossHeatFlag })
                CollectionAssert.Contains(a.flags, f);

            var low = DeepRules.Build(held, room, 5, DeepRules.Preset(5), Cat, null);
            CollectionAssert.DoesNotContain(low.flags, DeepRules.BossHeatFlag, "boss heat from DL 6");
            CollectionAssert.Contains(DeepRules.Build(held, room, 6, DeepRules.Preset(6), Cat, null).flags, DeepRules.BossHeatFlag);
            var none = DeepRules.Build(held, room, 0, picks, Cat, null);
            CollectionAssert.DoesNotContain(none.flags, "mut:hot_silicon", "picks are ignored outside a Deep Layer run");
            Assert.AreEqual(24f, none.mods.SourceRise, 1e-4f);
        }

        [Test]
        public void Values_Placement_Carry_Prices_Exclusions()
        {
            var gen = P(("double_patrol", 1), ("tracker_swarm", 1), ("unstable_reality", 1), ("gaze_everywhere", 1));
            var hp = DeepRules.Placement(gen, Cat);
            Assert.AreEqual(1, hp.extraDrones);
            Assert.AreEqual(0.5f, hp.trackerShare, 1e-6f);
            Assert.AreEqual(0.5f, hp.glitchBonus, 1e-6f);
            Assert.IsTrue(hp.watchers);
            Assert.IsFalse(DeepRules.Placement(P(("hot_silicon", 1)), Cat).Any);

            Assert.AreEqual(0.2f, DeepRules.Value(P(("tight_deadline", 2)), Cat, "flag:tight_deadline"), 1e-6f);
            Assert.AreEqual(0f, DeepRules.Value(P(("hot_silicon", 2)), Cat, "flag:tight_deadline"));
            Assert.AreEqual(1f, DeepRules.CarryShare(P(("persistent_melt", 1)), Cat, 0.5f), "Persistent Melt: 100 %");
            Assert.AreEqual(0.5f, DeepRules.CarryShare(P(("hot_silicon", 1)), Cat, 0.5f));
            Assert.AreEqual(60, DeepRules.Price(40, P(("poor_market", 1)), Cat), "Poor Market +50 %");
            Assert.AreEqual(38, DeepRules.Price(25, P(("poor_market", 1)), Cat));
            Assert.AreEqual(25, DeepRules.Price(25, P(("hot_silicon", 1)), Cat));
            Assert.IsTrue(DeepRules.Excludes(P(("persistent_melt", 1)), Cat, "cold_boot"), "Persistent Melt x Cold Boot");
            Assert.IsFalse(DeepRules.Excludes(P(("persistent_melt", 1)), Cat, "heat_sink"));
        }

        [Test]
        public void FragmentBonus_GrowsPerLevel_AndStopsAtDL20()
        {
            Assert.AreEqual(0, DeepRules.Bonus(100, 0));
            Assert.AreEqual(10, DeepRules.Bonus(100, 1));
            Assert.AreEqual(100, DeepRules.Bonus(100, 10));
            Assert.AreEqual(200, DeepRules.Bonus(100, 20));
            Assert.AreEqual(200, DeepRules.Bonus(100, 30), "rewards stop at DL 20");
            Assert.AreEqual(3, DeepRules.Bonus(35, 1), "rounded down");
        }

        [Test]
        public void Unlocks_FirstKills_OpenDeepLayer_OneLevelAtATime_AndSurviveASaveRoundTrip()
        {
            var save = SaveMigrator.Default();
            Assert.IsFalse(RunUnlocks.DeepLayerOpen(save));
            Assert.AreEqual(0, RunUnlocks.MaxDeepLayer(save));
            Assert.IsTrue(RunUnlocks.RecordKill(save, 1));
            Assert.IsFalse(RunUnlocks.RecordKill(save, 1), "first kill only");
            Assert.IsTrue(RunUnlocks.RecordKill(save, 2));
            Assert.AreEqual(0, RunUnlocks.MaxDeepLayer(save), "COMPILER does not open Deep Layer");
            Assert.IsTrue(RunUnlocks.RecordKill(save, 3));
            CollectionAssert.AreEqual(new[] { "kill_auditor", "kill_compiler", "kill_architect" }, save.unlocks);
            Assert.AreEqual(1, RunUnlocks.MaxDeepLayer(save));
            Assert.IsTrue(RunUnlocks.RecordDeepWin(save, 1));
            Assert.IsFalse(RunUnlocks.RecordDeepWin(save, 1));
            Assert.AreEqual(2, RunUnlocks.MaxDeepLayer(save));
            save.deepLayerMax = 10;
            Assert.AreEqual(DeepRules.MaxLevel, RunUnlocks.MaxDeepLayer(save));

            var back = SaveMigrator.Migrate(SaveMigrator.Serialize(save));
            CollectionAssert.AreEqual(save.unlocks, back.unlocks);
            Assert.AreEqual(10, back.deepLayerMax);
        }
    }
}
