using System;
using System.Collections.Generic;
using Desync.Rules;
using NUnit.Framework;
using EventType = Desync.Rules.EventType;

namespace Desync.Flow.Tests
{
    public class RoomReportsTests
    {
        [Test]
        public void ClockIsMinutesSecondsCentiseconds()
        {
            Assert.AreEqual("3:41.27", RoomReports.Clock(221.27f));
            Assert.AreEqual("0:05.00", RoomReports.Clock(5f));
            Assert.AreEqual("0:00.00", RoomReports.Clock(-1f));
            Assert.AreEqual("1:00.00", RoomReports.Clock(59.999f));
        }

        [Test]
        public void ShareTextMatchesTheSpecLine()
        {
            Assert.AreEqual("DESYNC // 2026-10-24 // S A S B S A // 3:41.27 // GHOST x27 // desync-nine.vercel.app",
                RoomReports.ShareText(new DateTime(2026, 10, 24, 23, 0, 0, DateTimeKind.Utc), "S A S B S A", 221.27f, 27));
        }

        [Test]
        public void RunShareTextListsClearedRoomsInOrder_LatestClearWins()
        {
            RoomReports.ClearRun();
            var day = new DateTime(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc);
            RoomReports.AddToRun(new RoomReport { room = 1, grade = 'A', time = 30f }, 4, day);
            RoomReports.AddToRun(new RoomReport { room = 0, grade = 'B', time = 20f }, 9, day);
            string s = RoomReports.AddToRun(new RoomReport { room = 0, grade = 'S', time = 10f }, 2, day);   // retry of room 1
            Assert.AreEqual("DESYNC // 2026-10-24 // S A // 0:40.00 // GHOST x4 // desync-nine.vercel.app", s);
            RoomReports.ClearRun();
        }

        [Test]
        public void ReportAgreesWithRules()
        {
            var r = RoomReports.Build(2, 45f, 1800, 2.5f, 40f, 2500, new RoomBest { time = 52f, score = 1500, grade = "B" }, 0, 1f);
            Assert.AreEqual(Scoring.TimeBonus(45f, 50f), r.timeBonus);
            Assert.AreEqual(200, r.timeBonus);
            Assert.AreEqual(2000, r.total);
            Assert.AreEqual(Medals.For(45f, 40f), r.medal);
            Assert.AreEqual(Medal.Gold, r.medal);
            Assert.AreEqual(Scoring.Grade(2000, 2500), r.grade);
            Assert.AreEqual(52f, r.prevBestTime);
            Assert.AreEqual(1500, r.prevBestScore);
            Assert.AreEqual(2.5f, r.peakMultiplier);
            Assert.AreEqual("5.0 s to SYNC", r.nextTarget);
        }

        [Test]
        public void NextTargetNamesTheNextMedalThenWhatSNeeds()
        {
            Assert.AreEqual("0.8 s to GOLD", RoomReports.NextTarget(50.8f, 40f, 0, 1000, 0, 500));
            Assert.AreEqual("0.1 s to SILVER", RoomReports.NextTarget(64.01f, 40f, 0, 1000, 0, 500));   // never "0.0 s"
            Assert.AreEqual("S needs: no TRACE", RoomReports.NextTarget(30f, 40f, 800, 1000, 1, 500));
            Assert.AreEqual("S needs: +200 pts", RoomReports.NextTarget(30f, 40f, 800, 1000, 0, 500));
            Assert.AreEqual("S needs: +600 pts", RoomReports.NextTarget(30f, 40f, 400, 1000, 2, 500), "no TRACE alone would not reach S");
            Assert.IsNull(RoomReports.NextTarget(30f, 40f, 1000, 1000, 0, 500), "SYNC and S: nothing left");
        }

        [Test]
        public void ReportUsesTheBetterOfThisRunAndTheSavedBestForTheTarget()
        {
            var r = RoomReports.Build(0, 60f, 500, 1f, 40f, 1000, new RoomBest { time = 41f, score = 2000 }, 0, 1f);
            Assert.AreEqual(Medal.Silver, r.medal, "this run's medal");
            Assert.AreEqual("1.0 s to SYNC", r.nextTarget, "target from the 41 s best (gold), not this 60 s run");
        }

        /// The live tally and the replay of its log must agree exactly, whatever the order of events.
        [Test]
        public void LiveTallyEqualsReplayOfItsLog()
        {
            var kinds = (EventType[])Enum.GetValues(typeof(EventType));
            var mods = Modifiers.Default;
            mods.ScoreMult = 1.37f;
            mods.ChainCap = 3.5f;
            for (uint seed = 1; seed <= 50; seed++)
            {
                var rng = new Mulberry32(seed);
                var tally = new ScoreTally();
                var log = new List<RunEvent>();
                for (int i = 0; i < 200; i++)
                {
                    var e = new RunEvent(i * 0.25f, kinds[rng.Range(0, kinds.Length)], "r" + rng.Range(1, 3), "ICE-" + rng.Range(1, 4), rng.Range(80f, 100f));
                    log.Add(e);
                    tally.Apply(e, mods);
                }
                Assert.AreEqual(Scoring.ScoreFromEvents(log, mods), tally.Total, "seed " + seed);
            }
        }

        [Test]
        public void TallyReportsWhatEachEventPaid()
        {
            var t = new ScoreTally();
            var m = Modifiers.Default;
            Assert.AreEqual(62.5, t.Apply(new RunEvent(0, EventType.CleanFlip, "r1"), m), 1e-6);
            Assert.AreEqual(1, t.Chain);
            Assert.AreEqual(0, t.Apply(new RunEvent(1, EventType.Flip, "r1"), m));
            Assert.AreEqual(0, t.Apply(new RunEvent(1, EventType.Redline, "r1", melt: 99f), m), "the Exit record pays REDLINE, not this one");
            Assert.AreEqual(312.5, t.Apply(new RunEvent(2, EventType.Exit, "r1", melt: 95f), m), 1e-6);
            Assert.AreEqual(0, t.Apply(new RunEvent(3, EventType.ChainBreak, "r1"), m));
            Assert.AreEqual(0, t.Chain);
        }
    }
}
