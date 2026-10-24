using System;
using System.Collections.Generic;
using Desync.Content;
using Desync.Gen;
using Desync.Rules;

namespace Desync.Flow
{
    // Deep Layer (heat) and boss-kill unlocks. Engine-free on purpose: the rules below are checked without the editor.
    //
    // DESIGN (the spec leaves the choice open): a Deep Layer run is a CAMPAIGN run (3 acts) played under heat mutators the
    // player picks with their ranks (Content/mutators.json, ranks[r-1] = heat at rank r). The run's Deep Layer IS the total
    // heat of the picks, so "DL 7" means exactly 7 heat. Every level has a preset (DeepRules.Preset) the select screen
    // offers as its default, so picking a level alone is enough. Levels open one at a time: DL 1 after the first ARCHITECT
    // kill, then up to one above the best Deep Layer won (SaveV3.deepLayerMax), capped at DeepRules.MaxLevel (10 in M3, 30 in M4).
    // M4: DL 11-20 each pay a cosmetic reward and a bigger Fragments bonus up to DL 20; DL 21-30 are played for the DL 30 cosmetic
    // only (Cosmetics.cs: DeepRewards).

    /// The Deep Layer of the run in progress (writer: RunManager at run start; reader: DL select, HUD, results).
    public static class DeepRun
    {
        public static int Level;                                                 // 0 = not a Deep Layer run
        public static readonly List<MutatorPick> Picks = new List<MutatorPick>();   // heat mutators, canonical order (by id)
        public static readonly List<Cosmetic> Rewards = new List<Cosmetic>();       // what the last Deep Layer win earned (RecordDeepWin); cleared with the run
        /// UI (DL select) -> run: start a Deep Layer CAMPAIGN at `level` with these picks (null = DeepRules.Preset(level)).
        public static Action<int, IReadOnlyList<MutatorPick>> StartRequested;
    }

    /// Boss ids per act and the first-kill flags they leave in SaveV3.unlocks ("kill_auditor", ...).
    public static class RunUnlocks
    {
        public static readonly string[] Bosses = { "auditor", "compiler", "architect" };   // act 1, 2, 3

        public static string Boss(int act) => Bosses[Math.Max(1, Math.Min(Bosses.Length, act)) - 1];
        public static string KillFlag(int act) => "kill_" + Boss(act);
        public static bool Killed(SaveV3 save, int act) => save.unlocks.Contains(KillFlag(act));

        /// Records the act's boss as killed; true on the first kill.
        public static bool RecordKill(SaveV3 save, int act)
        {
            if (Killed(save, act)) return false;
            save.unlocks.Add(KillFlag(act));
            return true;
        }

        /// An ARCHITECT ending (BossEvents.Ending: "Exposed" | "Merged") as "ending_exposed" / "ending_merged"; true when new.
        public static bool RecordEnding(SaveV3 save, string ending)
        {
            if (string.IsNullOrEmpty(ending)) return false;
            string flag = "ending_" + ending.ToLowerInvariant();
            if (save.unlocks.Contains(flag)) return false;
            save.unlocks.Add(flag);
            return true;
        }

        /// Deep Layer opens with the first ARCHITECT kill.
        public static bool DeepLayerOpen(SaveV3 save) => Killed(save, 3);

        /// Highest Deep Layer the player may start: one above the best won, 0 while Deep Layer is closed.
        public static int MaxDeepLayer(SaveV3 save) => DeepLayerOpen(save) ? Math.Min(DeepRules.MaxLevel, Math.Max(0, save.deepLayerMax) + 1) : 0;

        /// A won Deep Layer run; true when it raised the best. Every level the win moves past pays its reward (DeepRewards.Grant,
        /// DeepRun.Rewards): the first win of each level, DL 11-20 and DL 30.
        public static bool RecordDeepWin(SaveV3 save, int level)
        {
            if (level <= save.deepLayerMax) return false;
            var got = DeepRewards.Grant(save, save.deepLayerMax, level);
            DeepRun.Rewards.Clear();
            DeepRun.Rewards.AddRange(got);
            save.deepLayerMax = level;
            return true;
        }
    }

    /// Deep Layer numbers, presets, validation and how the picks fold into a room (Modifiers, flags, generator placement).
    public static class DeepRules
    {
        public const int MaxLevel = 30;          // DL 1-30 (M3 shipped 1-10)
        public const int RewardCap = 20;         // the Fragments bonus grows to DL 20 and stops there; DL 21-30 pay the DL 30 cosmetic only
        public const int RewardPercent = 10;     // Fragments bonus per level, % of what the run earned
        public const int BossHeatLevel = 6;      // DL >= 6: boss heat variants on every boss
        public const string BossHeatFlag = "flag:boss_overclock";

        /// One step of the preset ladder: `id` at `rank`, after dropping the picks in `drop`. Every step adds exactly one heat.
        readonly struct Step
        {
            public readonly string id; public readonly int rank; public readonly string[] drop;
            public Step(string id, int rank, params string[] drop) { this.id = id; this.rank = rank; this.drop = drop; }
        }

        /// Presets: each level adds one heat to the one below (a new rank-1 mutator, a rank raised, or - from DL 24, once every single
        /// heat is spent - a heavier mutator swapped in for lighter ones). DL 1-10 are the M3 ladder, DL 11-23 raise the pressure on melt,
        /// hacks, TRACE and cameras, DL 24-30 bring the heat threats and the rule-breakers (No Backups, Gaze Everywhere). Boss Overclock is
        /// not a step: DL 6 and up already run the bosses hot.
        static readonly Step[] Ladder =
        {
            new Step("tight_deadline", 1), new Step("hardened_ice", 1), new Step("poor_market", 1),
            new Step("long_sequences", 1), new Step("unstable_reality", 1), new Step("persistent_melt", 1),
            new Step("wider_lens", 1), new Step("hot_silicon", 1), new Step("hardened_ice", 2),
            new Step("tight_deadline", 2),
            // 11-20
            new Step("hair_trigger", 1), new Step("expensive_flip", 1), new Step("long_sequences", 2), new Step("wider_lens", 2),
            new Step("hot_silicon", 2), new Step("auditor_protocol", 1), new Step("slow_cooling", 1), new Step("tight_deadline", 3),
            new Step("hardened_ice", 3), new Step("blackout", 1),
            // 21-30
            new Step("hair_trigger", 2), new Step("slow_cooling", 2), new Step("hot_silicon", 3),
            new Step("double_patrol", 1, "poor_market"), new Step("tracker_swarm", 1, "expensive_flip"),
            new Step("scrambled_prompts", 1, "blackout"), new Step("fewer_choices", 1, "persistent_melt"),
            new Step("no_backups", 1, "unstable_reality", "auditor_protocol"), new Step("gaze_everywhere", 1, "hair_trigger"),
            new Step("hair_trigger", 1),
        };

        /// The default picks of a level (total heat = level), canonical order.
        public static List<MutatorPick> Preset(int level)
        {
            var picks = new List<MutatorPick>();
            for (int i = 0; i < Math.Min(level, Ladder.Length); i++)
            {
                var step = Ladder[i];
                foreach (var d in step.drop) picks.RemoveAll(p => p.id == d);
                int at = picks.FindIndex(p => p.id == step.id);
                var pick = new MutatorPick(step.id, step.rank);
                if (at >= 0) picks[at] = pick; else picks.Add(pick);
            }
            return Canonical(picks);
        }

        /// Picks sorted by id: the order they were chosen in never changes the run.
        public static List<MutatorPick> Canonical(IReadOnlyList<MutatorPick> picks)
        {
            var c = new List<MutatorPick>(picks ?? Array.Empty<MutatorPick>());
            c.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return c;
        }

        /// Total heat (unknown ids and bad ranks count 0; Check reports them).
        public static int Heat(IReadOnlyList<MutatorPick> picks, Catalog cat) => picks == null ? 0 : Loadout.ValidateHeat(picks, Array.Empty<Exploit>(), cat).heat;

        /// Why a Deep Layer run cannot start, empty when it can: "locked:<level>" (outside 1..maxLevel), "heat:<total>/<level>"
        /// (the picks must add up to the level), plus Loadout.ValidateHeat's unknown / rank / duplicate / conflict errors.
        public static List<string> Check(int level, IReadOnlyList<MutatorPick> picks, Catalog cat, int maxLevel)
        {
            var (heat, errors) = Loadout.ValidateHeat(picks ?? Array.Empty<MutatorPick>(), Array.Empty<Exploit>(), cat);
            if (level < 1 || level > Math.Min(maxLevel, MaxLevel)) errors.Insert(0, "locked:" + level);
            if (heat != level) errors.Add("heat:" + heat + "/" + level);
            return errors;
        }

        /// One room's loadout: held exploits + the room's run mutators (rank 1) + the Deep Layer picks, folded by Loadout.Build;
        /// flags gain "mut:<id>" for every run mutator and heat pick, and the boss heat flag from DL BossHeatLevel on.
        public static Built Build(IReadOnlyList<Exploit> held, IReadOnlyList<Mutator> roomMutators, int level, IReadOnlyList<MutatorPick> deep,
                                  Catalog cat, string mode)
        {
            var picks = new List<MutatorPick>();
            foreach (var m in roomMutators) picks.Add(new MutatorPick(m.id, 1));
            var heat = level > 0 ? Canonical(deep) : new List<MutatorPick>();
            picks.AddRange(heat);
            var b = Loadout.Build(held, picks, cat, mode);
            foreach (var m in roomMutators) b.flags.Add("mut:" + m.id);
            foreach (var p in heat) b.flags.Add("mut:" + p.id);
            if (level >= BossHeatLevel) b.flags.Add(BossHeatFlag);
            return b;
        }

        public static bool Has(IReadOnlyList<MutatorPick> picks, string id)
        {
            if (picks != null) foreach (var p in picks) if (p.id == id) return true;
            return false;
        }

        /// The value of `flag` ("flag:tight_deadline") among the picks at their ranks; 0 when none carries it.
        public static float Value(IReadOnlyList<MutatorPick> picks, Catalog cat, string flag)
        {
            if (picks == null) return 0f;
            foreach (var p in picks)
            {
                var m = cat.mutators.Find(x => x.id == p.id);
                if (m == null || m.effects.Count == 0) continue;
                foreach (var f in m.effects[Math.Min(Math.Max(p.rank, 1), m.effects.Count) - 1])
                    if (f.stat == flag) return f.value;
            }
            return 0f;
        }

        /// What the generator places for the picks: Double Patrol, Tracker Swarm, Unstable Reality, Gaze Everywhere.
        public static HeatPlacement Placement(IReadOnlyList<MutatorPick> picks, Catalog cat) => new HeatPlacement
        {
            extraDrones = (int)Value(picks, cat, "flag:double_patrol"),
            trackerShare = Value(picks, cat, "flag:tracker_swarm"),
            glitchBonus = Value(picks, cat, "flag:unstable_reality"),
            watchers = Has(picks, "gaze_everywhere"),
        };

        /// Share of the exit melt carried into the next room: Persistent Melt's value (100 %), else `normal`.
        public static float CarryShare(IReadOnlyList<MutatorPick> picks, Catalog cat, float normal)
        {
            float v = Value(picks, cat, "flag:persistent_melt");
            return v > 0f ? v : normal;
        }

        /// Market price under Poor Market (+50 %), rounded half up.
        public static int Price(int price, IReadOnlyList<MutatorPick> picks, Catalog cat) =>
            (int)Math.Floor(price * (1f + Value(picks, cat, "flag:poor_market")) + 0.5f);

        /// An exploit the picks conflict with (Persistent Melt x Cold Boot) is never offered or sold in that run.
        public static bool Excludes(IReadOnlyList<MutatorPick> picks, Catalog cat, string exploitId)
        {
            if (picks == null) return false;
            foreach (var p in picks)
            {
                var m = cat.mutators.Find(x => x.id == p.id);
                if (m != null && m.conflicts.Contains(exploitId)) return true;
            }
            return false;
        }

        /// Fragments bonus on top of what a Deep Layer run earned: RewardPercent per level up to RewardCap, rounded down.
        public static int Bonus(int earned, int level) => Math.Max(0, earned) * Math.Min(Math.Max(0, level), RewardCap) * RewardPercent / 100;
    }
}
