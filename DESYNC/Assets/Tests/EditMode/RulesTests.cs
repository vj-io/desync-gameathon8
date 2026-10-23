using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Desync.Rules;

// Acceptance tests for the pure rules core (spec IDs kept). Written before the implementation.
public class MeltTests
{
    static MeltState Run(MeltState s, float secs) { int n = (int)Math.Round(secs * 60); for (int i = 0; i < n; i++) s = Melt.Step(s, 1f / 60f); return s; }

    [Test] public void MELT_01_Rises12PerSecondInSource() => Assert.That(Run(new MeltState(0, Layer.Source), 1).Melt, Is.EqualTo(12f).Within(0.01f));
    [Test] public void MELT_02_FlipAdds5() => Assert.That(Melt.Flip(new MeltState(10, Layer.Render)).Melt, Is.EqualTo(15f).Within(1e-5f));
    [Test] public void MELT_02b_FlipSwitchesLayer() => Assert.AreEqual(Layer.Source, Melt.Flip(new MeltState(10, Layer.Render)).Layer);
    [Test] public void MELT_03_DrainsAndClampsAtZero()
    {
        Assert.That(Run(new MeltState(6, Layer.Render), 1).Melt, Is.EqualTo(2f).Within(0.01f));
        Assert.AreEqual(0f, Run(new MeltState(2, Layer.Render), 2).Melt);
    }
    [Test] public void MELT_04_DeathAt100FromZeroInSource()
    {
        Assert.IsFalse(Run(new MeltState(0, Layer.Source), 8.2f).Dead);
        Assert.IsTrue(Run(new MeltState(0, Layer.Source), 8.4f).Dead);
    }
    [Test] public void MELT_05_FlipInto100Kills() => Assert.IsTrue(Melt.Flip(new MeltState(96, Layer.Render)).Dead);
    [Test] public void MELT_06_SourceRiseClampedAtLeast8()
    {
        var m = Modifiers.Default; m.SourceRise = 2f;
        Assert.That(Melt.Step(new MeltState(0, Layer.Source), 1f, m).Melt, Is.EqualTo(8f).Within(1e-5f));
    }
    [Test] public void MELT_07_FrameRateIndependent()
    {
        var a = new MeltState(0, Layer.Source); var b = a;
        for (int i = 0; i < 90; i++) a = Melt.Step(a, 1f / 30f);
        for (int i = 0; i < 432; i++) b = Melt.Step(b, 1f / 144f);
        Assert.Less(Math.Abs(a.Melt - b.Melt), 0.01f);
    }
    [Test] public void MOD_Clamps()
    {
        var m = Modifiers.Default; m.FlipCost = 0; m.TraceWindow = 3; m.MoveSpeedMult = 2; m.ScoreMult = 20;
        var c = m.Clamped();
        Assert.AreEqual(2f, c.FlipCost); Assert.AreEqual(1f, c.TraceWindow); Assert.AreEqual(1.3f, c.MoveSpeedMult, 1e-5f); Assert.AreEqual(8f, c.ScoreMult);
    }
}

public class ScoringTests
{
    [Test] public void SCORE_01_Mult1AtChain0() => Assert.AreEqual(1f, Scoring.GhostMultiplier(0));
    [Test] public void SCORE_02_PlusQuarterPerStep() => Assert.That(Scoring.GhostMultiplier(4), Is.EqualTo(2f).Within(1e-5f));
    [Test] public void SCORE_03_CapsAt4()
    {
        Assert.AreEqual(4f, Scoring.GhostMultiplier(12)); Assert.AreEqual(4f, Scoring.GhostMultiplier(50));
        Assert.AreEqual(5f, Scoring.GhostMultiplier(50, 5f));
    }
    [Test] public void SCORE_04_GradeThresholds()
    {
        Assert.AreEqual('S', Scoring.Grade(1000, 1000)); Assert.AreEqual('A', Scoring.Grade(750, 1000));
        Assert.AreEqual('B', Scoring.Grade(500, 1000)); Assert.AreEqual('C', Scoring.Grade(499, 1000));
    }
    [Test] public void SCORE_05_GrazeCappedAt3PerIcePerRoom()
    {
        var ev = Enumerable.Range(0, 5).Select(i => new RunEvent(i, EventType.Graze, "r1", "ice1")).ToList();
        Assert.AreEqual(450, Scoring.ScoreFromEvents(ev, Modifiers.Default));
        ev.Add(new RunEvent(6, EventType.Graze, "r1", "ice2"));
        ev.Add(new RunEvent(7, EventType.Graze, "r2", "ice1"));
        Assert.AreEqual(750, Scoring.ScoreFromEvents(ev, Modifiers.Default));
    }
    [Test] public void SCORE_06_RedlineOnlyAt90()
    {
        Assert.AreEqual(0, Scoring.ScoreFromEvents(new[] { new RunEvent(1, EventType.Exit, "r1", melt: 89.9f) }, Modifiers.Default));
        Assert.AreEqual(250, Scoring.ScoreFromEvents(new[] { new RunEvent(1, EventType.Exit, "r1", melt: 90f) }, Modifiers.Default));
    }
    [Test] public void SCORE_07_ChainMultipliesAndBreaks()
    {
        var ev = new List<RunEvent>();
        for (int i = 0; i < 4; i++) ev.Add(new RunEvent(i, EventType.CleanFlip, "r1"));       // 50*(1.25+1.5+1.75+2.0) = 325
        ev.Add(new RunEvent(5, EventType.Hack, "r1"));                                         // 120*2.0 = 240
        ev.Add(new RunEvent(6, EventType.Detected, "r1"));                                     // chain -> 0
        ev.Add(new RunEvent(7, EventType.Hack, "r1"));                                         // 120*1 = 120
        Assert.AreEqual(325 + 240 + 120, Scoring.ScoreFromEvents(ev, Modifiers.Default));
    }
    [Test] public void SCORE_08_TimeBonus()
    {
        Assert.AreEqual(400, Scoring.TimeBonus(20f, 30f)); Assert.AreEqual(0, Scoring.TimeBonus(40f, 30f));
    }
    [Test] public void MEDAL_Thresholds()
    {
        Assert.AreEqual(Medal.Sync, Medals.For(40f, 40f)); Assert.AreEqual(Medal.Gold, Medals.For(50f, 40f));
        Assert.AreEqual(Medal.Silver, Medals.For(64f, 40f)); Assert.AreEqual(Medal.Bronze, Medals.For(65f, 40f));
    }
}

public class RngTests
{
    [Test] public void RNG_01_SameSeedSameSequence()
    {
        var a = new Mulberry32(1234); var b = new Mulberry32(1234);
        for (int i = 0; i < 1000; i++) Assert.AreEqual(a.NextUInt(), b.NextUInt());
    }
    [Test] public void RNG_01b_MatchesReferenceImplementation()
    {
        // JS reference (spec F9) mulberry32(1234)() first three outputs * 2^32
        var r = new Mulberry32(1234);
        uint Ref(ref uint a) { a += 0x6D2B79F5; uint t = a; t = (t ^ (t >> 15)) * (t | 1); t ^= t + (t ^ (t >> 7)) * (t | 61); return t ^ (t >> 14); }
        uint s = 1234; for (int i = 0; i < 3; i++) Assert.AreEqual(Ref(ref s), r.NextUInt());
    }
    [Test] public void RNG_02_InUnitInterval()
    {
        var r = new Mulberry32(7);
        for (int i = 0; i < 10000; i++) { var x = r.NextDouble(); Assert.GreaterOrEqual(x, 0); Assert.Less(x, 1); }
    }
    [Test] public void RNG_03_DailySeedStablePerUtcDay()
    {
        Assert.AreEqual(Rng.DailySeed(new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc)), Rng.DailySeed(new DateTime(2026, 10, 23, 23, 59, 59, DateTimeKind.Utc)));
        Assert.AreNotEqual(Rng.DailySeed(new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc)), Rng.DailySeed(new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc)));
    }
    [Test] public void RNG_04_NoUnseededRandomInRulesOrGen()
    {
        foreach (var dir in new[] { "Assets/Scripts/Rules", "Assets/Scripts/Gen" })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var src = File.ReadAllText(f);
                StringAssert.DoesNotMatch(@"System\.Random|UnityEngine\.Random|\bRandom\.Range|new Random\(", src, f);
            }
        }
    }
    [Test] public void RNG_05_SubstreamsIndependentOfCallOrder()
    {
        var s1 = Rng.Substream(99, "loot").NextUInt();
        Rng.Substream(99, "enemies").NextUInt();
        var s2 = Rng.Substream(99, "loot").NextUInt();
        Assert.AreEqual(s1, s2);
        Assert.AreNotEqual(s1, Rng.Substream(99, "enemies").NextUInt());
    }
}

public class SaveTests
{
    [Test] public void SAVE_01_V1ToV3KeepsMedalsAndFragments()
    {
        var s = SaveMigrator.Migrate("{\"version\":1,\"best\":{\"r1\":\"G\"},\"frags\":120}");
        Assert.AreEqual(3, s.version); Assert.AreEqual("G", s.medals["r1"]); Assert.AreEqual(120, s.fragments);
    }
    [Test] public void SAVE_02_CorruptGivesDefaultNoThrow()
    {
        Assert.DoesNotThrow(() => SaveMigrator.Migrate("{not json"));
        Assert.AreEqual(3, SaveMigrator.Migrate("{not json").version);
        Assert.AreEqual(3, SaveMigrator.Migrate("").version);
        Assert.AreEqual(3, SaveMigrator.Migrate(null).version);
    }
    [Test] public void SAVE_03_NewerVersionNotDowngraded() => Assert.Throws<NewerSaveException>(() => SaveMigrator.Migrate("{\"version\":99}"));
    [Test] public void SAVE_04_IdempotentOnV3()
    {
        var s = SaveMigrator.Migrate("{\"version\":1,\"frags\":5}");
        var a = SaveMigrator.Serialize(s);
        Assert.AreEqual(a, SaveMigrator.Serialize(SaveMigrator.Migrate(a)));
    }
    [Test] public void SAVE_05_FullUnlockUnder64KB()
    {
        var s = SaveMigrator.Default();
        s.playerId = Guid.NewGuid().ToString();
        for (int i = 0; i < 58; i++) s.unlocks.Add("exploit_" + i);
        for (int i = 0; i < 30; i++) s.shards.Add("shard_" + i);
        for (int i = 0; i < 42; i++) { s.medals["room_" + i] = "SYNC"; s.best["room_" + i] = new RoomBest { time = 41.27f, score = 12345, grade = "S" }; }
        for (int i = 0; i < 365; i++) s.dailyBest[new DateTime(2026, 1, 1).AddDays(i).ToString("yyyy-MM-dd")] = 99999;
        Assert.Less(SaveMigrator.Serialize(s).Length, 65536);
    }
}
