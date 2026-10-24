using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Desync.Content;
using Desync.Gen;
using Desync.Rules;
using NUnit.Framework;

namespace Desync.Flow.Tests
{
    /// The unlock tree (Unlocks.cs, Content/unlocks.json): its shape against the spec's cadence, buying, the exploit pool, starters, the
    /// Black Market gate, and the balance check that the whole tree is affordable in about 8 hours. Engine-free: data is read from disk.
    public class UnlocksTests
    {
        static Catalog cat;
        static Catalog Cat => cat ??= Catalog.FromJson(File.ReadAllText("Assets/Resources/Content/exploits.json"), File.ReadAllText("Assets/Resources/Content/mutators.json"));
        static UnlockData data;
        static UnlockData Tree => data ??= UnlockData.FromJson(File.ReadAllText("Assets/Resources/Content/unlocks.json"));

        static SaveV3 Fresh(int fragments = 0) { var s = SaveMigrator.Default(); s.fragments = fragments; return s; }

        static readonly string[] Pools = { "pool_a", "pool_b", "pool_c", "pool_d", "pool_e", "pool_f" };

        // ================================================================== shape
        [Test]
        public void Tree_IsSound_Acyclic_AndEveryExploitAndStarterExists()
        {
            CollectionAssert.IsEmpty(Tree.Validate(Cat));
            Assert.AreEqual(Tree.nodes.Count, Tree.Ordered().Count);
        }

        [Test]
        public void Validate_CatchesBrokenData()
        {
            var bad = UnlockData.FromJson(@"{ ""nodes"": [
              { ""id"": ""a"", ""cost"": 5, ""requires"": [""b""], ""grants"": [""unlock_a""] },
              { ""id"": ""b"", ""cost"": 5, ""requires"": [""a""], ""grants"": [""unlock_a""], ""exploits"": [""no_such""] },
              { ""id"": ""c"", ""cost"": 5, ""requires"": [""kill_architect""], ""grants"": [] },
              { ""id"": ""a"", ""cost"": 0, ""grants"": [""oops""] } ] }");
            var e = bad.Validate(Cat);
            foreach (var want in new[] { "loop:a", "loop:b", "dupgrant:unlock_a", "exploit:b:no_such", "nogrant:c", "requires:c:kill_architect", "notDL0:c:kill_architect", "id:a", "grant:a:oops" })
                CollectionAssert.Contains(e, want);
        }

        [Test]
        public void Pools_PartitionTheFortyEightBaseExploits_AndDuosTheTen()
        {
            var seen = new List<string>();
            foreach (var id in Pools)
            {
                var n = Tree.Find(id);
                Assert.AreEqual(8, n.exploits.Count, id);
                seen.AddRange(n.exploits);
            }
            var bases = Cat.exploits.Where(e => e.archetype != Archetype.Duo).Select(e => e.id).ToList();
            Assert.AreEqual(48, bases.Count);
            CollectionAssert.AreEquivalent(bases, seen, "every base exploit sits in exactly one pool");
            var duos = Cat.exploits.Where(e => e.archetype == Archetype.Duo).Select(e => e.id);
            CollectionAssert.AreEquivalent(duos, Tree.Find("duos").exploits);
        }

        [Test]
        public void EveryPool_HasFourArchetypesAtLeast_SoAnOfferCanBeBuilt()
        {
            var open = new List<Exploit>();
            foreach (var id in Pools)
            {
                open.AddRange(Tree.Find(id).exploits.Select(x => Cat.Exploit(x)));
                Assert.GreaterOrEqual(open.Select(e => e.archetype).Distinct().Count(), 4, "cumulative through " + id);
            }
            var first = Tree.Find("pool_a").exploits.Select(x => Cat.Exploit(x)).ToList();
            Assert.GreaterOrEqual(first.Count(e => e.rarity == Rarity.R), 2, "an Elite offer needs Rare exploits from the first pool");
        }

        [Test]
        public void Cadence_MatchesTheSpecsHourByHourTable()
        {
            Assert.IsTrue(Tree.Find("pool_a").Free, "first 8 exploits free");
            Assert.IsTrue(Tree.Find("daily").Free, "Daily visible from the start");
            foreach (var id in new[] { "pool_b", "pool_c" }) { var n = Tree.Find(id); Assert.That(n.by, Is.InRange(1f, 2f), id + ": +16 exploits at 1-2 h"); }
            foreach (var id in new[] { "pool_d", "pool_e", "pool_f" }) Assert.That(Tree.Find(id).by, Is.InRange(4f, 8f), id + ": the remainder at 4-8 h");
            Assert.That(Tree.Find("duos").by, Is.InRange(1f, 2f), "duos possible at 1-2 h");
            Assert.That(Tree.Find("timeattack").by, Is.InRange(1f, 2f), "Time-Attack at 1-2 h");
            CollectionAssert.AreEqual(new[] { Unlocks.TimeAttack }, Tree.Find("timeattack").grants);
            foreach (var id in new[] { "starter_ghost", "starter_phantom", "blackmarket" }) Assert.That(Tree.Find(id).by, Is.InRange(0.5f, 1f), id + ": 0.5-1 h");
            foreach (var id in new[] { "starter_redline", "starter_overclock", "starter_tracer" })
                Assert.Greater(Tree.Find(id).by, Tree.Find("starter_phantom").by, id + " comes after GHOST and PHANTOM");
            foreach (var n in Tree.nodes) Assert.IsFalse(n.requires.Any(r => r.StartsWith("kill_") || r.StartsWith("dl_") || r.StartsWith("cosmetic_")), n.id + " is reachable at DL 0");
            foreach (var n in Tree.nodes) Assert.IsFalse(n.grants.Any(g => g.StartsWith("dl_") || g.StartsWith("cosmetic_")), n.id + " grants no Deep Layer key");
        }

        // ================================================================== ownership
        [Test]
        public void FreeNodes_AreOpenWithoutAPurchase_AndNothingIsWrittenForThem()
        {
            var s = Fresh();
            Assert.IsTrue(Unlocks.Has(s, Tree, "unlock_pool_a"));
            Assert.IsTrue(Unlocks.Has(s, Tree, Unlocks.Daily));
            Assert.IsFalse(Unlocks.Has(s, Tree, "unlock_pool_b"));
            Assert.IsFalse(Unlocks.Has(s, Tree, Unlocks.TimeAttack));
            Assert.IsFalse(Unlocks.Has(s, Tree, null));
            Assert.AreEqual(UnlockState.Owned, Unlocks.State(s, Tree, Tree.Find("daily")));
            CollectionAssert.IsEmpty(s.unlocks);
            Assert.IsFalse(Unlocks.Buy(s, Tree, "daily"), "nothing to buy");
            Assert.IsFalse(Unlocks.Buy(s, Tree, "no_such"));
        }

        [Test]
        public void States_Locked_Short_Open_Owned()
        {
            var s = Fresh(95);
            var c = Tree.Find("pool_c"); var b = Tree.Find("pool_b");
            Assert.AreEqual(UnlockState.Locked, Unlocks.State(s, Tree, c), "pool_b first");
            Assert.AreEqual(b, Unlocks.Missing(s, Tree, c));
            Assert.AreEqual(UnlockState.Open, Unlocks.State(s, Tree, b));
            s.fragments = b.cost - 1;
            Assert.AreEqual(UnlockState.Short, Unlocks.State(s, Tree, b));
            s.fragments = b.cost;
            Assert.IsTrue(Unlocks.Buy(s, Tree, "pool_b"));
            Assert.AreEqual(UnlockState.Owned, Unlocks.State(s, Tree, b));
            Assert.AreEqual(UnlockState.Short, Unlocks.State(s, Tree, c), "pool_b owned, no Fragments left");
        }

        [Test]
        public void Buy_SpendsFragments_WritesTheGrants_AndRefusesEverythingElse()
        {
            var s = Fresh(500);
            Assert.IsFalse(Unlocks.Buy(s, Tree, "pool_c"), "locked behind pool_b");
            Assert.AreEqual(500, s.fragments);
            Assert.IsTrue(Unlocks.Buy(s, Tree, "pool_b"));
            Assert.AreEqual(500 - Tree.Find("pool_b").cost, s.fragments);
            CollectionAssert.AreEqual(new[] { "unlock_pool_b" }, s.unlocks);
            Assert.IsTrue(Unlocks.Has(s, Tree, "unlock_pool_b"));
            Assert.IsFalse(Unlocks.Buy(s, Tree, "pool_b"), "owned");
            Assert.AreEqual(500 - Tree.Find("pool_b").cost, s.fragments, "no second charge");
            s.fragments = 1;
            Assert.IsFalse(Unlocks.Buy(s, Tree, "pool_c"), "too dear");
            Assert.AreEqual(1, s.fragments);

            Assert.IsTrue(Unlocks.Buy(Fresh(500), Tree, "timeattack"));
            var t = Fresh(500); Unlocks.Buy(t, Tree, "timeattack");
            Assert.IsTrue(Unlocks.Has(t, Tree, Unlocks.TimeAttack), "other systems test the key literally");
            CollectionAssert.Contains(t.unlocks, "unlock_timeattack");

            var back = SaveMigrator.Migrate(SaveMigrator.Serialize(t));
            Assert.IsTrue(Unlocks.Has(back, Tree, Unlocks.TimeAttack), "survives a save round trip");
            Assert.AreEqual(t.fragments, back.fragments);
        }

        [Test]
        public void TheWholeTree_CanBeBought_InRequirementOrder_WithEnoughFragments()
        {
            var s = Fresh(Tree.nodes.Sum(n => n.cost));
            for (int pass = 0; pass < Tree.nodes.Count; pass++)
                foreach (var n in Tree.Ordered()) Unlocks.Buy(s, Tree, n.id);
            Assert.AreEqual(0, s.fragments, "exactly the sum of the costs");
            foreach (var n in Tree.nodes) Assert.IsTrue(Unlocks.Owned(s, Tree, n), n.id);
            Assert.IsTrue(s.unlocks.Count < 40, "a handful of keys, not one per exploit");
        }

        // ================================================================== the exploit pool
        static List<string> Ids(Catalog c) => c.exploits.Select(e => e.id).ToList();

        [Test]
        public void Pool_FreshProfile_IsTheFirstEight_AndGrowsWithEachPurchase()
        {
            var s = Fresh(10000);
            var pool = Unlocks.Pool(s, Tree, Cat);
            CollectionAssert.AreEquivalent(Tree.Find("pool_a").exploits, Ids(pool));
            Assert.AreSame(Cat.mutators, pool.mutators, "mutators untouched");
            Unlocks.Buy(s, Tree, "pool_b");
            Assert.AreEqual(16, Unlocks.Pool(s, Tree, Cat).exploits.Count);
            Unlocks.Buy(s, Tree, "pool_c");
            Assert.AreEqual(24, Unlocks.Pool(s, Tree, Cat).exploits.Count, "8 free + 16 at 1-2 h");
            foreach (var id in new[] { "pool_d", "pool_e", "pool_f" }) Unlocks.Buy(s, Tree, id);
            Assert.AreEqual(48, Unlocks.Pool(s, Tree, Cat).exploits.Count, "the remainder at 4-8 h");
            Assert.IsFalse(Ids(Unlocks.Pool(s, Tree, Cat)).Any(id => Cat.Exploit(id).archetype == Archetype.Duo), "duos wait for their own node");
            Unlocks.Buy(s, Tree, "duos");
            Assert.AreEqual(58, Unlocks.Pool(s, Tree, Cat).exploits.Count);
        }

        [Test]
        public void Pool_AnExploitNoNodeListsIsOpen_NewContentNeverHides()
        {
            var extra = new Exploit { id = "brand_new", name = "X", archetype = Archetype.Ghost };
            var full = new Catalog { exploits = new List<Exploit>(Cat.exploits) { extra }, mutators = Cat.mutators };
            Assert.IsTrue(Ids(Unlocks.Pool(Fresh(), Tree, full)).Contains("brand_new"));
            Assert.IsTrue(Unlocks.ExploitOpen(Fresh(), new UnlockData(), "clean_handshake"), "an empty tree gates nothing");
        }

        [Test]
        public void Offers_FromAFreshPool_AreAlwaysThreeDistinct_AndEliteOffersCarryRares()
        {
            var pool = Unlocks.Pool(Fresh(), Tree, Cat);
            var first = new HashSet<string>(Tree.Find("pool_a").exploits);
            var held = new List<Exploit>();
            for (uint seed = 1; seed <= 300; seed++)
            {
                bool elite = seed % 3 == 0;
                var offer = Loadout.RollOffer(new Mulberry32(seed), pool, held, elite);
                Assert.AreEqual(3, offer.Count, "seed " + seed);
                Assert.AreEqual(3, offer.Select(e => e.id).Distinct().Count());
                Assert.IsTrue(offer.All(e => first.Contains(e.id)), "only unlocked exploits are offered");
                Assert.GreaterOrEqual(offer.Select(e => e.archetype).Distinct().Count(), 2, "two archetypes at least");
                if (elite) Assert.IsTrue(offer.Any(e => e.rarity != Rarity.C), "Elite: a Rare+ exploit");
            }
            // five held: still a full offer from what is left of the pool
            foreach (var id in new[] { "clean_handshake", "brinkmanship", "coolant_leak", "log_wipe" }) held.Add(Cat.Exploit(id));
            Assert.AreEqual(3, Loadout.RollOffer(new Mulberry32(7), pool, held, false).Count);
        }

        [Test]
        public void Duos_AreOfferedOnlyOnceUnlocked_AndOnlyWithBothParents()
        {
            var s = Fresh(10000);
            foreach (var id in Pools.Skip(1)) Unlocks.Buy(s, Tree, id);
            var held = new List<Exploit> { Cat.Exploit("clean_handshake"), Cat.Exploit("brinkmanship") };   // GHOST + REDLINE
            Func<Catalog, bool> sawDuo = pool =>
            {
                for (uint seed = 1; seed <= 400; seed++)
                    if (Loadout.RollOffer(new Mulberry32(seed), pool, held, false).Any(e => e.archetype == Archetype.Duo)) return true;
                return false;
            };
            Assert.IsFalse(sawDuo(Unlocks.Pool(s, Tree, Cat)), "no duos before the node");
            Unlocks.Buy(s, Tree, "duos");
            Assert.IsTrue(sawDuo(Unlocks.Pool(s, Tree, Cat)), "duos after it");
        }

        // ================================================================== starters
        [Test]
        public void Starters_EquipOnlyWhenOwned_OneAtATime_AndAreHeldAtRunStart()
        {
            var s = Fresh(1000);
            Assert.IsFalse(Unlocks.SetStarter(s, Tree, "starter_ghost"), "not bought");
            Assert.IsFalse(Unlocks.SetStarter(s, Tree, "pool_a"), "not a starter node");
            Assert.IsNull(Unlocks.Starter(s, Tree));
            var held = new List<Exploit>();
            Assert.IsNull(Unlocks.GrantStarter(s, Tree, held, Cat));
            CollectionAssert.IsEmpty(held);

            Unlocks.Buy(s, Tree, "starter_ghost"); Unlocks.Buy(s, Tree, "starter_phantom");
            Assert.IsTrue(Unlocks.SetStarter(s, Tree, "starter_phantom"));
            Assert.AreEqual("starter_phantom", Unlocks.Starter(s, Tree).id);
            Assert.IsTrue(Unlocks.SetStarter(s, Tree, "starter_ghost"));
            Assert.AreEqual("starter_ghost", Unlocks.Starter(s, Tree).id, "one at a time");
            var got = Unlocks.GrantStarter(s, Tree, held, Cat);
            Assert.AreEqual("clean_handshake", got.id);
            CollectionAssert.AreEqual(new[] { "clean_handshake" }, held.Select(e => e.id));
            Assert.IsNull(Unlocks.GrantStarter(s, Tree, held, Cat), "already held: not added twice");
            Assert.AreEqual(1, held.Count);

            Assert.IsTrue(Unlocks.SetStarter(s, Tree, ""));
            Assert.IsNull(Unlocks.Starter(s, Tree));

            s.settings[Unlocks.StarterSetting] = "starter_tracer";   // a stale setting for a node the profile does not own
            Assert.IsNull(Unlocks.Starter(s, Tree));
        }

        [Test]
        public void EveryStarter_IsACommonOfItsArchetype_ThatIsNotARareOrEpicPowerSpike()
        {
            foreach (var n in Tree.nodes.Where(n => n.starter != null))
            {
                var e = Cat.Exploit(n.starter);
                Assert.AreEqual(Rarity.C, e.rarity, n.id);
                StringAssert.Contains(e.archetype.ToString().ToLowerInvariant(), n.id, "the starter belongs to the node's archetype");
            }
        }

        // ================================================================== the run map
        static RunGraph GraphWithMarkets(out int markets)
        {
            for (uint seed = 1; seed < 200; seed++)
            {
                var g = Generator.GenerateRun(seed, 0);
                markets = g.acts.Sum(a => a.Count(n => n.type == NodeType.Market));
                if (markets > 0) return g;
            }
            throw new InconclusiveException("no seed with a market");
        }

        [Test]
        public void BlackMarket_Locked_TurnsMarketNodesIntoInfiltration_KeepingTheMap()
        {
            var g = GraphWithMarkets(out int markets);
            var before = g.acts.SelectMany(a => a).Select(n => n.id + ":" + string.Join(",", n.next) + ":" + n.roomSeed).ToList();
            var s = Fresh(500);
            Assert.IsTrue(Unlocks.GateMarkets(s, Tree, g));
            Assert.AreEqual(0, g.acts.Sum(a => a.Count(n => n.type == NodeType.Market)), "none left (" + markets + " were there)");
            CollectionAssert.AreEqual(before, g.acts.SelectMany(a => a).Select(n => n.id + ":" + string.Join(",", n.next) + ":" + n.roomSeed), "ids, links and seeds unchanged");
            Assert.IsFalse(Unlocks.GateMarkets(s, Tree, g), "nothing left to change");

            var open = GraphWithMarkets(out int m2);
            Unlocks.Buy(s, Tree, "blackmarket");
            Assert.IsFalse(Unlocks.GateMarkets(s, Tree, open));
            Assert.AreEqual(m2, open.acts.Sum(a => a.Count(n => n.type == NodeType.Market)), "owned: the map is untouched");
            Assert.IsFalse(Unlocks.GateMarkets(Fresh(), new UnlockData(), GraphWithMarkets(out _)), "no tree, no gate");
        }

        [Test]
        public void OnlyCampaignAndQuick_AreGated_SharedSeedModesSeeEverything()
        {
            Assert.IsTrue(Unlocks.Gated(RunMode.Campaign));
            Assert.IsTrue(Unlocks.Gated(RunMode.Quick));
            Assert.IsFalse(Unlocks.Gated(RunMode.Daily), "a leaderboard compares like with like");
            Assert.IsFalse(Unlocks.Gated(RunMode.Classic));
        }

        // ================================================================== balance: the tree against the hours
        /// docs/BALANCE.md, "Unlock economy". A back-of-envelope estimate of what a median player banks per hour of play, from RunRules and the spec's
        /// clear rates, so that the cadence in the tree is a number and not a hope.
        static class Economy
        {
            public const double RoomsPerAct = 4.2;      // 4 map nodes x (Infiltration .60 + Elite .15 + Anomaly .05 = .80 rooms; Market and Cooldown pay nothing) + the boss
            public const double FragmentsPerRoom = 12;  // RunRules: clear 10, +10 on Gold or better (~10 % of rooms), +15 on grade S (~7 %)
            public const double MinutesPerRoom = 1.5;   // a 12-room campaign in ~19 min (spec: 18-22), menus and drafts included
            public const double Overhead = 0.9;         // menus, results, retries inside a death: only 90 % of the clock is run time

            /// Clear rate of each act that is open (the run goes as deep as it can, then cashes out): hour 1 = Act 1 only (the AUDITOR is the
            /// first boss), hour 2 adds Act 2, hours 3-4 Act 3, then the spec's F10 targets (A1 85-97, A2 60-85, A3 40-70 %) at their middles.
            public static readonly (double untilHour, double[] clear)[] Profiles =
            {
                (1, new[] { 0.80 }), (2, new[] { 0.85, 0.60 }), (4, new[] { 0.88, 0.65, 0.45 }), (8, new[] { 0.91, 0.72, 0.55 }),
            };

            /// Expected Fragments banked and minutes played by one attempt: a death mid-act banks RunRules.Bank (40 % + 3), a win or the
            /// cash-out after the last open boss banks all of it.
            public static (double bank, double minutes) Attempt(double[] clear)
            {
                double bank = 0, minutes = 0, reach = 1;
                for (int k = 0; k < clear.Length; k++)
                {
                    double die = reach * (1 - clear[k]), rooms = k * RoomsPerAct + RoomsPerAct / 2;
                    bank += die * RunRules.Bank((int)Math.Round(rooms * FragmentsPerRoom), true);
                    minutes += die * rooms * MinutesPerRoom;
                    reach *= clear[k];
                }
                double win = clear.Length * RoomsPerAct;
                return (bank + reach * win * FragmentsPerRoom, minutes + reach * win * MinutesPerRoom);
            }

            public static double PerHour(double[] clear) { var a = Attempt(clear); return a.bank / a.minutes * 60 * Overhead; }

            /// Fragments banked by hour `h` of play (piecewise linear).
            public static double Earned(double h)
            {
                double total = 0, from = 0;
                foreach (var p in Profiles)
                {
                    total += Math.Max(0, Math.Min(h, p.untilHour) - from) * PerHour(p.clear);
                    from = p.untilHour;
                }
                return total;
            }

            public static double HoursFor(double fragments)
            {
                double h = 0;
                while (Earned(h) < fragments && h < 100) h += 0.01;
                return h;
            }
        }

        [Test]
        public void Economy_TheWholeTreeIsAffordableInAboutEightHours()
        {
            int total = Tree.nodes.Sum(n => n.cost);
            double hours = Economy.HoursFor(total);
            TestContext.WriteLine("unlock economy: " + total + " Fragments; earned by 1/2/4/8 h: " + string.Join(" / ", new[] { 1.0, 2, 4, 8 }.Select(h => Math.Round(Economy.Earned(h)))) +
                                  "; the whole tree at " + Math.Round(hours, 2) + " h");
            Assert.LessOrEqual(hours, 8.0, "reachable within ~8 h of play");
            Assert.GreaterOrEqual(hours, 6.5, "and not hollow: it still takes most of that time");
        }

        [Test]
        public void Economy_EachCadenceTierIsAffordableByItsHour()
        {
            foreach (var by in Tree.nodes.Select(n => n.by).Distinct().OrderBy(x => x))
            {
                int cost = Tree.nodes.Where(n => n.by <= by).Sum(n => n.cost);
                Assert.LessOrEqual(cost, Economy.Earned(by), "everything due by hour " + by + " costs " + cost + ", the estimate banks " + Math.Round(Economy.Earned(by)));
            }
            Assert.AreEqual(0, Tree.nodes.Where(n => n.by <= 0.5).Sum(n => n.cost), "the first half hour is free");
        }

        [Test]
        public void Economy_AFailedRunStillPaysAtLeastFortyPercent()
        {
            // the anti-grind rule: a death banks >= 40 % of what the same rooms would have banked on a win (RunRules.Bank)
            for (int earned = 10; earned <= 400; earned += 5)
                Assert.GreaterOrEqual(RunRules.Bank(earned, true), 0.4 * RunRules.Bank(earned, false), "earned " + earned);
            Assert.Greater(Economy.PerHour(new[] { 0.91, 0.72, 0.55 }), 200, "sanity: hundreds of Fragments an hour");
        }
    }
}
