using System.Collections.Generic;
using System.IO;
using System.Linq;
using Desync.Content;
using Desync.Rules;
using NUnit.Framework;

namespace Desync.Flow.Tests
{
    /// Deep Layer 11-30 (M4): the preset ladder past DL 10, the cap, the Fragments bonus that stops at DL 20, and the cosmetic rewards
    /// (DL 11-20 one each, DL 30 the SYNC GOLD trail). Engine-free: the catalog is read from disk.
    public class DeepLayerM4Tests
    {
        static Catalog cat;
        static Catalog Cat => cat ??= Catalog.FromJson(File.ReadAllText("Assets/Resources/Content/exploits.json"), File.ReadAllText("Assets/Resources/Content/mutators.json"));

        static SaveV3 Opened(int best = 0)
        {
            var s = SaveMigrator.Default();
            s.unlocks.Add("kill_architect");
            s.deepLayerMax = best;
            return s;
        }

        // ================================================================== levels and presets
        [Test]
        public void TheCapIsThirty_OneLevelOpensPerWin()
        {
            Assert.AreEqual(30, DeepRules.MaxLevel);
            Assert.AreEqual(1, RunUnlocks.MaxDeepLayer(Opened(0)));
            Assert.AreEqual(11, RunUnlocks.MaxDeepLayer(Opened(10)), "DL 11 opens after a DL 10 win");
            Assert.AreEqual(30, RunUnlocks.MaxDeepLayer(Opened(29)));
            Assert.AreEqual(30, RunUnlocks.MaxDeepLayer(Opened(30)), "capped: DL 30 stays the deepest");
            Assert.AreEqual(0, RunUnlocks.MaxDeepLayer(SaveMigrator.Default()), "closed before the first ARCHITECT kill");
            CollectionAssert.Contains(DeepRules.Check(31, DeepRules.Preset(30), Cat, 40), "locked:31");
            CollectionAssert.IsEmpty(DeepRules.Check(30, DeepRules.Preset(30), Cat, 30));
            CollectionAssert.Contains(DeepRules.Check(15, DeepRules.Preset(15), Cat, 14), "locked:15", "one above the best won");
        }

        [Test]
        public void Presets_OneToThirty_AreLegal_AndEachAddsExactlyOneHeat()
        {
            int prev = 0;
            for (int level = 1; level <= DeepRules.MaxLevel; level++)
            {
                var p = DeepRules.Preset(level);
                int heat = DeepRules.Heat(p, Cat);
                Assert.AreEqual(level, heat, "DL " + level);
                Assert.AreEqual(prev + 1, heat);
                CollectionAssert.IsEmpty(DeepRules.Check(level, p, Cat, DeepRules.MaxLevel), "DL " + level + " preset is legal");
                CollectionAssert.AreEqual(p.Select(x => x.id).OrderBy(x => x, System.StringComparer.Ordinal), p.Select(x => x.id), "canonical order");
                Assert.IsTrue(p.All(x => !Cat.mutators.Find(m => m.id == x.id).runMutator), "heat picks only");
                prev = heat;
            }
        }

        [Test]
        public void Presets_KeepTheM3Ladder_AndNeverRepeatAMutator()
        {
            var m3 = new[] { "tight_deadline", "hardened_ice", "poor_market", "long_sequences", "unstable_reality", "persistent_melt", "wider_lens", "hot_silicon" };
            CollectionAssert.AreEquivalent(m3, DeepRules.Preset(8).Select(p => p.id), "DL 1-8 are unchanged");
            Assert.AreEqual(2, DeepRules.Preset(10).First(p => p.id == "tight_deadline").rank);
            Assert.AreEqual(2, DeepRules.Preset(10).First(p => p.id == "hardened_ice").rank);
            for (int level = 1; level <= DeepRules.MaxLevel; level++)
                Assert.AreEqual(DeepRules.Preset(level).Count, DeepRules.Preset(level).Select(p => p.id).Distinct().Count(), "DL " + level);
        }

        [Test]
        public void Presets_OnTheDeepEnd_BringTheHeatThreatsAndTheRuleBreakers()
        {
            var top = DeepRules.Preset(30).Select(p => p.id).ToList();
            foreach (var id in new[] { "double_patrol", "tracker_swarm", "no_backups", "gaze_everywhere", "fewer_choices", "scrambled_prompts" })
                CollectionAssert.Contains(top, id, "DL 30 carries " + id);
            CollectionAssert.DoesNotContain(DeepRules.Preset(20).Select(p => p.id), "no_backups", "not before the swap-ins");
            Assert.IsFalse(DeepRules.Preset(30).Any(p => p.id == "boss_overclock"), "DL 6 and up already run the bosses hot");
        }

        [Test]
        public void FragmentBonus_ContinuesToDL20_ThenStops()
        {
            Assert.AreEqual(110, DeepRules.Bonus(100, 11));
            Assert.AreEqual(150, DeepRules.Bonus(100, 15));
            Assert.AreEqual(200, DeepRules.Bonus(100, 20));
            Assert.AreEqual(200, DeepRules.Bonus(100, 21));
            Assert.AreEqual(200, DeepRules.Bonus(100, 30), "DL 21-30 pay the DL 30 cosmetic only");
        }

        // ================================================================== rewards
        [Test]
        public void Rewards_OnePerLevelFromElevenToTwenty_TheTrailAtThirty_NothingElse()
        {
            for (int level = 1; level <= 30; level++)
            {
                var c = DeepRewards.ForLevel(level);
                if (level >= 11 && level <= 20) Assert.IsNotNull(c, "DL " + level + " pays a reward");
                else if (level == 30) { Assert.AreEqual("sync_gold", c.id); Assert.AreEqual(CosmeticSlot.Flip, c.slot); }
                else Assert.IsNull(c, "DL " + level + " pays none");
            }
            Assert.AreEqual(11, Cosmetics.All.Length);
            Assert.AreEqual(Cosmetics.All.Length, Cosmetics.All.Select(c => c.id).Distinct().Count());
            Assert.AreEqual(Cosmetics.All.Length, Cosmetics.All.Select(c => c.level).Distinct().Count(), "one reward per level");
            Assert.IsTrue(Cosmetics.All.Any(c => c.slot == CosmeticSlot.Title) && Cosmetics.All.Any(c => c.slot == CosmeticSlot.Flip));
            Assert.AreEqual("dl_reward_12", DeepRewards.ForLevel(12).RewardKey);
            Assert.AreEqual("cosmetic_sync_gold", DeepRewards.ForLevel(30).Key);
        }

        [Test]
        public void RecordDeepWin_PaysEachLevelsRewardOnce_AndWearsTheFirstOfEachSlot()
        {
            var s = Opened(10);
            Assert.IsTrue(RunUnlocks.RecordDeepWin(s, 11));
            CollectionAssert.AreEqual(new[] { "deep_diver" }, DeepRun.Rewards.Select(c => c.id));
            CollectionAssert.IsSupersetOf(s.unlocks, new[] { "dl_reward_11", "cosmetic_deep_diver" });
            Assert.AreEqual("deep_diver", Cosmetics.Equipped(s, CosmeticSlot.Title).id, "the first title is worn at once");
            Assert.IsNull(Cosmetics.Equipped(s, CosmeticSlot.Flip));

            Assert.IsTrue(RunUnlocks.RecordDeepWin(s, 12));
            Assert.AreEqual("ripple", Cosmetics.Equipped(s, CosmeticSlot.Flip).id);
            Assert.IsTrue(RunUnlocks.RecordDeepWin(s, 13));
            Assert.AreEqual("deep_diver", Cosmetics.Equipped(s, CosmeticSlot.Title).id, "a later title does not replace the worn one");
            CollectionAssert.Contains(s.unlocks, "cosmetic_heat_walker");

            Assert.IsFalse(RunUnlocks.RecordDeepWin(s, 12), "a lower level raises nothing");
            int count = s.unlocks.Count;
            Assert.IsFalse(RunUnlocks.RecordDeepWin(s, 13));
            Assert.AreEqual(count, s.unlocks.Count, "no duplicate keys");
        }

        [Test]
        public void RecordDeepWin_ASkippedRangePaysEveryLevelItPassed_InOrder()
        {
            var s = Opened(10);
            Assert.IsTrue(RunUnlocks.RecordDeepWin(s, 14));   // a profile whose best was set outside a run
            CollectionAssert.AreEqual(new[] { "deep_diver", "ripple", "heat_walker", "shards" }, DeepRun.Rewards.Select(c => c.id));
            CollectionAssert.AreEqual(new[] { "dl_reward_11", "cosmetic_deep_diver", "dl_reward_12", "cosmetic_ripple", "dl_reward_13", "cosmetic_heat_walker", "dl_reward_14", "cosmetic_shards" },
                                      s.unlocks.Where(k => k.StartsWith("dl_reward_") || k.StartsWith("cosmetic_")).ToArray());
        }

        [Test]
        public void RecordDeepWin_DL21To29PayNothing_DL30PaysSyncGold()
        {
            var s = Opened(20);
            for (int level = 21; level <= 29; level++)
            {
                Assert.IsTrue(RunUnlocks.RecordDeepWin(s, level));
                CollectionAssert.IsEmpty(DeepRun.Rewards, "DL " + level);
            }
            Assert.IsFalse(s.unlocks.Any(k => k == "dl_reward_25"));
            Assert.IsTrue(RunUnlocks.RecordDeepWin(s, 30));
            CollectionAssert.AreEqual(new[] { "sync_gold" }, DeepRun.Rewards.Select(c => c.id));
            CollectionAssert.IsSupersetOf(s.unlocks, new[] { "dl_reward_30", "cosmetic_sync_gold" });
            Assert.AreEqual(30, s.deepLayerMax);
        }

        [Test]
        public void WinningLevelsBelowEleven_PaysNoCosmetic()
        {
            var s = Opened(0);
            for (int level = 1; level <= 10; level++) RunUnlocks.RecordDeepWin(s, level);
            CollectionAssert.AreEqual(new[] { "kill_architect" }, s.unlocks, "DL 1-10 pay Fragments only");
        }

        [Test]
        public void Rewards_SurviveASaveRoundTrip_AndAreNotRequiredByAnythingInTheTree()
        {
            var s = Opened(11);
            RunUnlocks.RecordDeepWin(s, 12);
            Cosmetics.Equip(s, CosmeticSlot.Flip, "ripple");
            var back = SaveMigrator.Migrate(SaveMigrator.Serialize(s));
            Assert.AreEqual("ripple", Cosmetics.Equipped(back, CosmeticSlot.Flip).id);
            Assert.IsTrue(DeepRewards.Earned(back, 12));
            Assert.IsFalse(DeepRewards.Earned(back, 13));
            var tree = UnlockData.FromJson(File.ReadAllText("Assets/Resources/Content/unlocks.json"));
            Assert.IsFalse(tree.nodes.Any(n => n.requires.Concat(n.grants).Any(k => k.StartsWith("dl_reward_") || k.StartsWith("cosmetic_"))),
                           "anti-grind: every unlock is reachable at DL 0, only the Deep Layer cosmetics are not");
        }

        // ================================================================== wearing them
        [Test]
        public void Equip_NeedsOwnership_TheRightSlot_AndAnIdThatExists()
        {
            var s = Opened(11);
            RunUnlocks.RecordDeepWin(s, 12);   // deep_diver (title) + ripple (flip) earned and worn
            Assert.IsFalse(Cosmetics.Equip(s, CosmeticSlot.Flip, "shards"), "not owned");
            Assert.IsFalse(Cosmetics.Equip(s, CosmeticSlot.Flip, "deep_diver"), "a title does not fit the flip slot");
            Assert.IsFalse(Cosmetics.Equip(s, CosmeticSlot.Flip, "no_such"));
            Assert.AreEqual("ripple", Cosmetics.Equipped(s, CosmeticSlot.Flip).id, "refusals change nothing");
            Assert.IsTrue(Cosmetics.Equip(s, CosmeticSlot.Flip, ""));
            Assert.IsNull(Cosmetics.Equipped(s, CosmeticSlot.Flip));
            s.settings["cosmetic_flip"] = "sync_gold";   // a stale setting for something the profile does not own
            Assert.IsNull(Cosmetics.Equipped(s, CosmeticSlot.Flip));
        }

        [Test]
        public void Next_StepsThroughTheOwnedItems_ThenNone_ThenBackToTheFirst()
        {
            var s = Opened(10);
            RunUnlocks.RecordDeepWin(s, 18);   // flip: ripple, shards, afterglow, streak
            var seen = new List<string>();
            for (int i = 0; i < 6; i++)
            {
                var next = Cosmetics.Next(s, CosmeticSlot.Flip);
                seen.Add(next?.id ?? "none");
                Cosmetics.Equip(s, CosmeticSlot.Flip, next?.id);
            }
            CollectionAssert.AreEqual(new[] { "shards", "afterglow", "streak", "none", "ripple", "shards" }, seen, "from ripple (worn at DL 12)");
            Assert.IsNull(Cosmetics.Next(Opened(0), CosmeticSlot.Flip), "nothing owned: nothing to step to");
            var owned = new List<Cosmetic>();
            Cosmetics.OwnedIn(s, CosmeticSlot.Title, owned);
            CollectionAssert.AreEqual(new[] { "deep_diver", "heat_walker", "redliner", "phase_runner" }, owned.Select(c => c.id));
        }
    }
}
