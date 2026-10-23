using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Desync.Content;
using Desync.Rules;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

// Condition grammar, loadout folding and roll distributions (see the grammar block in ContentImpl.cs).
public class ContentGrammarTests
{
    static ConditionContext Ctx(int chain = 0, float melt = 0f, int trace = 0, Layer layer = Layer.Render, int node = 0)
        => new ConditionContext { chain = chain, melt = melt, trace = trace, layer = layer, node = node };

    [Test] public void Comparisons()
    {
        Assert.IsTrue(Conditions.Eval("chain>=4", Ctx(chain: 4)));
        Assert.IsFalse(Conditions.Eval("chain>=4", Ctx(chain: 3)));
        Assert.IsFalse(Conditions.Eval("melt>70", Ctx(melt: 70f)));
        Assert.IsTrue(Conditions.Eval("melt>70", Ctx(melt: 70.5f)));
        Assert.IsTrue(Conditions.Eval("melt<=7.5", Ctx(melt: 7.5f)));
        Assert.IsTrue(Conditions.Eval("trace<1", Ctx(trace: 0)));
        Assert.IsTrue(Conditions.Eval("trace=2", Ctx(trace: 2)));
        Assert.IsFalse(Conditions.Eval("node>=4", Ctx(node: 3)));
        Assert.IsTrue(Conditions.Eval("node>=4", Ctx(node: 4)));
    }

    [Test] public void LayerAndConjunction()
    {
        Assert.IsTrue(Conditions.Eval("layer=source", Ctx(layer: Layer.Source)));
        Assert.IsFalse(Conditions.Eval("layer=source", Ctx()));
        Assert.IsTrue(Conditions.Eval("layer=render", Ctx()));
        Assert.IsTrue(Conditions.Eval(" melt >= 50 & layer = source ", Ctx(melt: 50f, layer: Layer.Source)));
        Assert.IsFalse(Conditions.Eval("melt>=50&layer=source", Ctx(melt: 49f, layer: Layer.Source)));
        Assert.IsFalse(Conditions.Eval("melt>=50&layer=source", Ctx(melt: 60f)));
    }

    [Test] public void EmptyAlwaysHolds()
    {
        Assert.IsTrue(Conditions.Eval(null, Ctx()));
        Assert.IsTrue(Conditions.Eval("", Ctx()));
        Assert.IsTrue(Conditions.IsValid(null));
    }

    [TestCase("chain>>4")] [TestCase("speed>1")] [TestCase("Chain>=4")] [TestCase("chain 4")] [TestCase("layer>=source")]
    [TestCase("layer=up")] [TestCase("melt>=")] [TestCase("melt>=1.")] [TestCase("melt>=7x")] [TestCase("chain>=4&")]
    public void MalformedIsInvalidAndNeverHolds(string when)
    {
        Assert.IsFalse(Conditions.IsValid(when));
        Assert.IsFalse(Conditions.Eval(when, Ctx(chain: 99, melt: 99f, trace: 9, layer: Layer.Source, node: 9)));
    }

    [Test] public void FromJsonRejectsBadEffects()
    {
        Assert.Throws<FormatException>(() => Catalog.FromJson("{\"exploits\":[{\"id\":\"x\",\"effects\":[{\"stat\":\"FlipCosts\",\"op\":\"add\",\"value\":1}]}]}"));
        Assert.Throws<FormatException>(() => Catalog.FromJson("{\"exploits\":[{\"id\":\"x\",\"effects\":[{\"stat\":\"FlipCost\",\"op\":\"plus\",\"value\":1}]}]}"));
        Assert.Throws<FormatException>(() => Catalog.FromJson("{\"exploits\":[{\"id\":\"x\",\"effects\":[{\"stat\":\"FlipCost\",\"op\":\"add\",\"value\":1,\"when\":\"heat>=1\"}]}]}"));
        Assert.Throws<FormatException>(() => Catalog.FromJson("{\"mutators\":[{\"id\":\"m\",\"effects\":[[{\"stat\":\"flag:m\",\"when\":\"node>4&\"}]]}]}"));
    }

    [Test] public void EvalDoesNotAllocate()
    {
        var c = Ctx(melt: 60f, layer: Layer.Source);
        TestDelegate eval = () => { Conditions.Eval("melt>=50&layer=source", c); };
        eval();
        Assert.That(eval, Is.Not.AllocatingGCMemory());
    }
}

public class ContentLoadoutTests
{
    static Catalog Cat => _cat ??= Catalog.FromJson(File.ReadAllText("Assets/Resources/Content/exploits.json"), File.ReadAllText("Assets/Resources/Content/mutators.json"));
    static Catalog _cat;
    static List<Exploit> Held(params string[] ids) => ids.Select(id => { var e = Cat.Exploit(id); Assert.IsNotNull(e, id); return e; }).ToList();
    static readonly List<MutatorPick> NoMut = new List<MutatorPick>();
    static ConditionContext Ctx(int chain = 0, float melt = 0f, int trace = 0, Layer layer = Layer.Render)
        => new ConditionContext { chain = chain, melt = melt, trace = trace, layer = layer };

    static double ChiSquare(int[] observed, double[] p)
    {
        double total = observed.Sum(), chi = 0;
        for (int i = 0; i < p.Length; i++) chi += Math.Pow(observed[i] - total * p[i], 2) / (total * p[i]);
        return chi;
    }

    [Test] public void RarityWeightsC60R30E10()
    {
        var rng = new Mulberry32(2026);
        var n = new int[3];
        for (int i = 0; i < 10000; i++)
            foreach (var e in Loadout.RollOffer(rng, Cat, new List<Exploit>(), false)) n[(int)e.rarity]++;
        Assert.Less(ChiSquare(n, new[] { 0.6, 0.3, 0.1 }), 13.82, $"C{n[0]} R{n[1]} E{n[2]}");   // 2 dof, p = 0.001
    }

    [Test] public void EliteWeightsR70E30NoCommons()
    {
        var rng = new Mulberry32(77);
        var n = new int[3];
        for (int i = 0; i < 10000; i++)
            foreach (var e in Loadout.RollOffer(rng, Cat, new List<Exploit>(), true)) n[(int)e.rarity]++;
        Assert.AreEqual(0, n[0]);
        Assert.Less(ChiSquare(new[] { n[1], n[2] }, new[] { 0.7, 0.3 }), 10.83, $"R{n[1]} E{n[2]}");   // 1 dof, p = 0.001
    }

    [Test] public void OffersSkipHeldAndBlockedAndStayMixed()
    {
        var rng = new Mulberry32(11);
        var held = Held("cold_boot", "clean_handshake");
        for (int i = 0; i < 500; i++)
        {
            var o = Loadout.RollOffer(rng, Cat, held, false);
            Assert.AreEqual(3, o.Count);
            Assert.IsFalse(o.Any(e => e.id == "cold_boot" || e.id == "clean_handshake" || e.id == "heat_sink"));
            Assert.GreaterOrEqual(o.Select(e => e.archetype).Distinct().Count(), 2);
            var f = Loadout.RollOffer(rng, Cat, held, false, fewerChoices: true);
            Assert.AreEqual(2, f.Count);
            Assert.AreEqual(2, f.Select(e => e.archetype).Distinct().Count());
        }
    }

    [Test] public void DuoOfferedOnlyForHeldPair()
    {
        var rng = new Mulberry32(4);
        var held = Held("clean_handshake", "cold_boot");   // GHOST + REDLINE
        var duos = new HashSet<string>();
        for (int i = 0; i < 3000; i++)
            foreach (var e in Loadout.RollOffer(rng, Cat, held, true))
                if (e.archetype == Archetype.Duo) duos.Add(e.id);
        CollectionAssert.AreEquivalent(new[] { "spectral_fever" }, duos);
    }

    [Test] public void CanHoldRules()
    {
        Assert.IsFalse(Loadout.CanHold(Held("clean_handshake"), Cat.Exploit("clean_handshake")), "duplicate");
        Assert.IsFalse(Loadout.CanHold(Held("kernel_panic"), Cat.Exploit("dead_mans_switch")), "one survive-at-100 effect");
        Assert.IsFalse(Loadout.CanHold(Held("clean_handshake"), Cat.Exploit("spectral_fever")), "duo needs both archetypes");
        Assert.IsTrue(Loadout.CanHold(Held("clean_handshake", "cold_boot"), Cat.Exploit("spectral_fever")));
        Assert.IsFalse(Loadout.CanHold(Held("clean_handshake", "cold_boot", "ghost_ping", "spectral_fever"), Cat.Exploit("poltergeist")), "max 1 duo");
    }

    [Test] public void BuildStacksThenClamps()
    {
        var b = Loadout.Build(Held("burn_rate", "thermal_paste"), NoMut, Cat);
        Assert.AreEqual(2f, b.mods.FlipCost, 1e-4f);
        Assert.AreEqual(4.5f, b.mods.RenderDrain, 1e-4f);

        b = Loadout.Build(Held("rootkit", "ghost_protocol"), NoMut, Cat);
        Assert.AreEqual(0.75f, b.mods.TraceWindow, 1e-4f);
        Assert.AreEqual(1, b.conditional.Count);
        Assert.AreEqual(1f, Conditions.Apply(b.mods, b.conditional, Ctx(chain: 8)).TraceWindow, 1e-4f, "0.5 + 0.25 + 0.3 clamps to 1.0");

        b = Loadout.Build(Held("cold_boot"), new List<MutatorPick> { new MutatorPick("hardened_ice", 2), new MutatorPick("persistent_melt", 1) }, Cat);
        Assert.AreEqual(1.2f, b.mods.IceSpeedMult, 1e-4f);
        CollectionAssert.IsSupersetOf(b.flags, new[] { "flag:cold_boot", "flag:persistent_melt" });

        b = Loadout.Build(new List<Exploit>(), new List<MutatorPick> { new MutatorPick("double_melt", 0) }, Cat);
        Assert.AreEqual(24f, b.mods.SourceRise, 1e-4f);
        Assert.AreEqual(1.3f, b.mods.ScoreMult, 1e-4f);
    }

    [Test] public void ThermalRunawayDoublesOverclockStats()
    {
        var plain = Loadout.Build(Held("liquid_cooling", "thermal_paste"), NoMut, Cat).mods;
        Assert.AreEqual(10f, plain.SourceRise, 1e-4f);
        Assert.AreEqual(4f, plain.FlipCost, 1e-4f);
        var b = Loadout.Build(Held("liquid_cooling", "thermal_paste", "thermal_runaway"), NoMut, Cat);
        Assert.AreEqual((12f - 4f) * 1.25f, b.mods.SourceRise, 1e-4f);
        Assert.AreEqual(3f, b.mods.FlipCost, 1e-4f);
        Assert.AreEqual(5f, b.mods.RenderDrain, 1e-4f);
        Assert.IsTrue(b.flags.Contains("flag:thermal_runaway"));
    }

    [Test] public void ConditionalEffectsApplyOnlyWhileLive()
    {
        var b = Loadout.Build(Held("slipstream", "fever_code"), NoMut, Cat);
        Assert.AreEqual(1f, b.mods.MoveSpeedMult);
        Assert.AreEqual(1f, b.mods.ScoreMult);
        var on = Conditions.Apply(b.mods, b.conditional, Ctx(chain: 4, melt: 80f));
        Assert.AreEqual(1.08f, on.MoveSpeedMult, 1e-4f);
        Assert.AreEqual(1.5f, on.ScoreMult, 1e-4f);
        var off = Conditions.Apply(b.mods, b.conditional, Ctx(chain: 3, melt: 74f));
        Assert.AreEqual(1f, off.MoveSpeedMult, 1e-4f);
        Assert.AreEqual(1f, off.ScoreMult, 1e-4f);

        b = Loadout.Build(Held("overvolt", "slipstream", "thermal_runaway"), NoMut, Cat);
        Assert.AreEqual(1.3f, Conditions.Apply(b.mods, b.conditional, Ctx(chain: 5, melt: 60f, layer: Layer.Source)).MoveSpeedMult, 1e-4f);

        var c = Ctx(chain: 5, melt: 60f, layer: Layer.Source);
        TestDelegate apply = () => { Conditions.Apply(b.mods, b.conditional, c); };
        apply();
        Assert.That(apply, Is.Not.AllocatingGCMemory());
    }

    [Test] public void ValidateHeatReportsBadPicks()
    {
        Assert.AreEqual(1, Loadout.ValidateHeat(new[] { new MutatorPick("hair_trigger", 1) }, new List<Exploit>(), Cat).heat);
        CollectionAssert.Contains(Loadout.ValidateHeat(new[] { new MutatorPick("hardened_ice", 4) }, new List<Exploit>(), Cat).errors, "rank:hardened_ice:4");
        CollectionAssert.Contains(Loadout.ValidateHeat(new[] { new MutatorPick("nope", 1) }, new List<Exploit>(), Cat).errors, "unknown:nope");
        var dup = Loadout.ValidateHeat(new[] { new MutatorPick("wider_lens", 2), new MutatorPick("wider_lens", 1) }, new List<Exploit>(), Cat);
        Assert.AreEqual(2, dup.heat);
        CollectionAssert.Contains(dup.errors, "duplicate:wider_lens");
        Assert.IsEmpty(Loadout.ValidateHeat(new[] { new MutatorPick("persistent_melt", 1) }, Held("heat_sink"), Cat).errors);
    }

    [Test] public void RoomMutatorCountsAndGazeGate()
    {
        var rng = new Mulberry32(8);
        bool gazeSeen = false;
        for (int i = 0; i < 300; i++)
        {
            Assert.LessOrEqual(Loadout.RollRoomMutators(rng, Cat, 1).Count, 1);
            Assert.AreEqual(1, Loadout.RollRoomMutators(rng, Cat, 2).Count);
            var n4 = Loadout.RollRoomMutators(rng, Cat, 3);
            Assert.That(n4.Count, NUnit.Framework.Is.InRange(1, 2));
            Assert.AreEqual(2, Loadout.RollRoomMutators(rng, Cat, 4).Count);
            foreach (int node in new[] { 1, 2 })
                Assert.IsFalse(Loadout.RollRoomMutators(rng, Cat, node).Any(m => m.id == "gaze"), "gaze only from node 4");
            gazeSeen |= n4.Any(m => m.id == "gaze");
        }
        Assert.IsTrue(gazeSeen);
        CollectionAssert.AreEqual(Loadout.RollRoomMutators(new Mulberry32(42), Cat, 4).Select(m => m.id),
                                  Loadout.RollRoomMutators(new Mulberry32(42), Cat, 4).Select(m => m.id));
    }
}
