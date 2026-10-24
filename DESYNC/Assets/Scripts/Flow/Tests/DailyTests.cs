using System.Collections.Generic;
using System.Linq;
using Desync.Content;
using Desync.Rules;
using NUnit.Framework;
using UnityEngine;

namespace Desync.Flow.Tests
{
    /// Daily Breach / Weekly Ladder rules: the day's mutator pair, the scored-attempt marker, the weekly key, the share block.
    public class DailyTests
    {
        static Catalog cat;
        static Catalog Cat => cat ??= Catalog.FromJson(Resources.Load<TextAsset>("Content/exploits").text, Resources.Load<TextAsset>("Content/mutators").text);

        [Test]
        public void Mutators_ATwoTagPairFixedBySeed_NeverNodeGated()
        {
            var seen = new HashSet<string>();
            for (uint day = 0; day < 60; day++)
            {
                uint seed = Rng.HashStr("2026-" + day);
                var a = DailyRun.Mutators(seed, Cat);
                Assert.AreEqual(2, a.Count, "a pair");
                Assert.AreNotEqual(a[0].tag, a[1].tag, "never two of one tag");
                Assert.IsTrue(a.All(m => m.runMutator), "run mutators only");
                Assert.IsFalse(a.Any(m => m.id == "gaze"), "gaze waits for node 4: not a whole-run mutator");
                CollectionAssert.AreEqual(a.Select(m => m.id).ToList(), DailyRun.Mutators(seed, Cat).Select(m => m.id).ToList(), "same seed, same pair");
                seen.Add(a[0].id + "+" + a[1].id);
            }
            Assert.Greater(seen.Count, 10, "days differ");
        }

        [Test]
        public void ScoredAttempt_IsPerUtcDay()
        {
            var save = SaveMigrator.Default();
            Assert.IsFalse(DailyRun.Scored(save, "2026-10-24"));
            save.settings[DailyRun.ScoredKey] = "2026-10-24";
            Assert.IsTrue(DailyRun.Scored(save, "2026-10-24"));
            Assert.IsFalse(DailyRun.Scored(save, "2026-10-25"), "a new day opens a new scored attempt");
        }

        [Test]
        public void Weekly_KeyBestAndUnlock()
        {
            var save = SaveMigrator.Default();
            Assert.AreEqual("daily_week_2026-43", DailyRun.WeekSaveKey("2026-W43"));
            Assert.AreEqual(0, DailyRun.WeekBest(save, "2026-W43"));
            save.settings["daily_week_2026-43"] = "34000";
            Assert.AreEqual(34000, DailyRun.WeekBest(save, "2026-W43"));
            Assert.IsFalse(DailyRun.WeeklyOpen(save));
            save.unlocks.Add("kill_auditor");
            Assert.IsTrue(DailyRun.WeeklyOpen(save), "opens with the first AUDITOR kill");
        }

        [Test]
        public void ShareBlock_OneLinePerRoom_ThenTotal()
        {
            var text = DailyRun.ShareBlock("2026-10-24", false, new[] { Medal.Gold, Medal.Sync, Medal.Bronze }, new[] { 'S', 'S', 'B' }, 13640, 271.2f);
            var lines = text.Split('\n');
            Assert.AreEqual("DESYNC DAILY // 2026-10-24", lines[0]);
            Assert.AreEqual(DailyRun.MedalGlyphs[(int)Medal.Gold] + " S", lines[1]);
            Assert.AreEqual(DailyRun.MedalGlyphs[(int)Medal.Sync] + " S", lines[2]);
            Assert.AreEqual(DailyRun.MedalGlyphs[(int)Medal.Bronze] + " B", lines[3]);
            Assert.AreEqual("13640 // 4:31.20", lines[4]);
            Assert.AreEqual("desync-nine.vercel.app", lines[5]);
            StringAssert.EndsWith("// PRACTICE", DailyRun.ShareBlock("2026-10-24", true, new Medal[0], new char[0], 0, 0f).Split('\n')[0]);
        }
    }
}
