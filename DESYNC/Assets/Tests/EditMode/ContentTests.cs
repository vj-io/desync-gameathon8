using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Desync.Content;
using Desync.Rules;

// Exploit / mutator acceptance tests (spec EXP-01..08, MUT-01..02). Data in Assets/Resources/Content/{exploits,mutators}.json.
public class ContentTests
{
    static Catalog Cat => _cat ??= Catalog.FromJson(File.ReadAllText("Assets/Resources/Content/exploits.json"), File.ReadAllText("Assets/Resources/Content/mutators.json"));
    static Catalog _cat;
    static Exploit X(string id) { var e = Cat.Exploit(id); Assert.IsNotNull(e, id); return e; }

    [Test] public void EXP_01_48BasePlus10Duos()
    {
        var ids = Cat.exploits.Select(e => e.id).ToList();
        Assert.AreEqual(ids.Count, ids.Distinct().Count());
        Assert.AreEqual(48, Cat.exploits.Count(e => e.archetype != Archetype.Duo));
        Assert.AreEqual(10, Cat.exploits.Count(e => e.archetype == Archetype.Duo));
    }
    [Test] public void EXP_02_Max5Held()
    {
        var five = new[] { "clean_handshake", "echo_flip", "packet_loss", "slipstream", "null_trail" }.Select(X).ToList();
        Assert.IsFalse(Loadout.CanHold(five, X("log_wipe")));
        Assert.IsTrue(Loadout.CanHold(five.Take(4).ToList(), X("log_wipe")));
    }
    [Test] public void EXP_03_FlipCostNeverBelow2()
    {
        var b = Loadout.Build(new[] { "burn_rate", "thermal_paste" }.Select(X).ToList(), new List<MutatorPick>(), Cat);
        Assert.GreaterOrEqual(b.mods.FlipCost, 2f);
    }
    [Test] public void EXP_04_ScoreMultCappedAt8()
    {
        var b = Loadout.Build(new[] { "fever_code", "audit_trail", "zero_day" }.Select(X).ToList(), new List<MutatorPick>(), Cat);
        Assert.LessOrEqual(b.mods.ScoreMult, 8f);
    }
    [Test] public void EXP_05_ColdBootBlocksHeatSink() => Assert.IsFalse(Loadout.CanHold(new[] { X("cold_boot") }, X("heat_sink")));
    [Test] public void EXP_06_DuoOnlyWithBothArchetypes()
    {
        var rng = new Mulberry32(5);
        for (int i = 0; i < 200; i++)
            Assert.IsFalse(Loadout.RollOffer(rng, Cat, new[] { X("clean_handshake") }, false).Any(e => e.id == "spectral_fever"));
    }
    [Test] public void EXP_07_OffersDistinctMixedDeterministic()
    {
        var a = Loadout.RollOffer(new Mulberry32(9), Cat, new List<Exploit>(), false);
        var b = Loadout.RollOffer(new Mulberry32(9), Cat, new List<Exploit>(), false);
        CollectionAssert.AreEqual(a.Select(e => e.id), b.Select(e => e.id));
        Assert.AreEqual(3, a.Select(e => e.id).Distinct().Count());
        Assert.GreaterOrEqual(a.Select(e => e.archetype).Distinct().Count(), 2);
    }
    [Test] public void EXP_08_ZeroDayDisabledInDaily()
    {
        Assert.AreEqual(4f, Loadout.Build(new[] { X("zero_day") }, new List<MutatorPick>(), Cat, "daily").mods.ChainCap);
        Assert.AreEqual(5f, Loadout.Build(new[] { X("zero_day") }, new List<MutatorPick>(), Cat).mods.ChainCap);
    }
    [Test] public void MUT_01_TotalMaxHeat40()
    {
        var all = Cat.mutators.Where(m => !m.runMutator).Select(m => new MutatorPick(m.id, m.ranks.Count)).ToList();
        Assert.AreEqual(20, all.Count);
        Assert.AreEqual(40, Loadout.ValidateHeat(all, new List<Exploit>(), Cat).heat);
    }
    [Test] public void MUT_02_PersistentMeltConflictsColdBoot()
        => CollectionAssert.Contains(Loadout.ValidateHeat(new[] { new MutatorPick("persistent_melt", 1) }, new[] { X("cold_boot") }, Cat).errors, "conflict:persistent_melt:cold_boot");
    [Test] public void MUT_03_RoomMutatorsNeverShareTag()
    {
        Assert.AreEqual(10, Cat.mutators.Count(m => m.runMutator));
        var rng = new Mulberry32(3);
        for (int i = 0; i < 300; i++)
        {
            var picks = Loadout.RollRoomMutators(rng, Cat, 1 + i % 5);
            Assert.AreEqual(picks.Count, picks.Select(m => m.tag).Distinct().Count());
            Assert.LessOrEqual(picks.Count, 2);
        }
        Assert.AreEqual(0, Loadout.RollRoomMutators(new Mulberry32(1), Cat, 0).Count, "node 1 (index 0) has no mutators");
    }
}
