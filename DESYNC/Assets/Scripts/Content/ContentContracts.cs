using System;
using System.Collections.Generic;
using Desync.Rules;

namespace Desync.Content
{
    // Data-driven exploits, mutators and the loadout -> Modifiers builder (engine-free). CONTRACT: signatures frozen.

    public enum Archetype { Ghost, Redline, Phantom, Overclock, Tracer, Duo }
    public enum Rarity { C, R, E }

    public sealed class Effect
    {
        public string stat;     // a Modifiers field name (e.g. "FlipCost") or a flag id (e.g. "flag:cold_boot")
        public string op;       // "add" | "mul" | "set"
        public float value;
        public string when;     // optional condition id evaluated in game (e.g. "chain>=4"); null = always
    }

    public sealed class Exploit
    {
        public string id, name, desc;
        public Archetype archetype;
        public Rarity rarity;
        public List<string> requires = new List<string>();   // duos: the two archetypes, e.g. ["Ghost","Redline"]
        public string exclusiveGroup;
        public List<Effect> effects = new List<Effect>();
        public List<string> tags = new List<string>();
    }

    public sealed class Mutator
    {
        public string id, name, desc, tag;
        public List<int> ranks = new List<int>();           // heat per rank (deep layer); empty for run mutators
        public List<List<Effect>> effects = new List<List<Effect>>();   // per rank (run mutators: one entry)
        public List<string> conflicts = new List<string>();
        public bool runMutator;                              // weekend per-room mutator vs deep-layer heat mutator
    }

    public sealed class Catalog
    {
        public List<Exploit> exploits = new List<Exploit>();
        public List<Mutator> mutators = new List<Mutator>();
        /// Parses {"exploits":[...]} and/or {"mutators":[...]} documents (merge several).
        public static Catalog FromJson(params string[] jsonDocs) => throw new NotImplementedException();
        public Exploit Exploit(string id) => exploits.Find(e => e.id == id);
    }

    public struct MutatorPick { public string id; public int rank; public MutatorPick(string id, int rank) { this.id = id; this.rank = rank; } }

    public sealed class Loadout
    {
        public const int MaxHeld = 5, MaxDuos = 1;
        /// Unconditional effects folded into Modifiers (add, then mul, then set, then Clamped()); flags collected;
        /// conditional effects (non-null when) returned for the game to evaluate. mode "daily" disables Zero-Day.
        public static Built Build(IReadOnlyList<Exploit> held, IReadOnlyList<MutatorPick> mutators, Catalog cat, string mode = null) => throw new NotImplementedException();
        /// Max 5 held, max 1 duo, exclusive groups respected, no duplicates.
        public static bool CanHold(IReadOnlyList<Exploit> held, Exploit candidate) => throw new NotImplementedException();
        /// 3 distinct exploits (2 when fewerChoices), >= 2 archetypes, rarity weights C60/R30/E10 (elite 0/70/30),
        /// duos only when both archetypes are held, never one already held or not holdable. Deterministic for a given rng state.
        public static List<Exploit> RollOffer(Mulberry32 rng, Catalog cat, IReadOnlyList<Exploit> held, bool elite, bool fewerChoices = false) => throw new NotImplementedException();
        /// Total heat of deep-layer picks and conflict errors like "conflict:persistent_melt:cold_boot".
        public static (int heat, List<string> errors) ValidateHeat(IReadOnlyList<MutatorPick> picks, IReadOnlyList<Exploit> held, Catalog cat) => throw new NotImplementedException();
        /// Run mutators for a room: count by node (node2: 0-1, node3: 1, node4: 1-2, node5: 2), never two with the same tag.
        public static List<Mutator> RollRoomMutators(Mulberry32 rng, Catalog cat, int nodeIndex) => throw new NotImplementedException();
    }

    public sealed class Built
    {
        public Modifiers mods;
        public HashSet<string> flags = new HashSet<string>();
        public List<Effect> conditional = new List<Effect>();
    }
}
