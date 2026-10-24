using System;
using System.Collections.Generic;
using Desync.Rules;
using Newtonsoft.Json;

// Implementation of ContentContracts.cs (data in Assets/Resources/Content/exploits.json and mutators.json).
//
// CONDITION GRAMMAR (Effect.when)
//   when   := clause ( '&' clause )*            every clause must hold; spaces are ignored
//   clause := var op number
//           | 'layer' '=' ( 'source' | 'render' )
//   var    := 'chain'    GHOST chain (GameState.Chain)
//           | 'melt'     melt 0..100 (GameState.Melt)
//           | 'trace'    TRACE level this room (GameState.TraceCount; 2 is the highest before the alarm, "max TRACE")
//           | 'node'     1-based node in the act; only known when rolling room mutators
//   op     := '>=' | '<=' | '>' | '<' | '='
//   number := digits [ '.' digits ]
//   e.g. "chain>=4", "melt>=75", "melt>70", "trace>=2", "layer=source", "melt>=50&layer=source", "node>=4".
//   Evaluate with Conditions.Eval (no allocations). Catalog.FromJson rejects a malformed condition.
//
// EFFECT SEMANTICS
// - stat = a Modifiers field name, op = add | mul | set. Loadout.Build folds every unconditional one: all adds,
//   then all muls, then all sets, then Modifiers.Clamped(). Same-stat effects therefore stack before the clamp.
// - stat = "flag:<id>" (op omitted): behaviour the game implements. value = its headline number (seconds, %, metres,
//   count) so balance edits stay in data; the rest is in desc. Flags always land in Built.flags; a flag's `when`
//   documents its trigger for the game.
// - A stat effect with a `when` is returned in Built.conditional. Conditions.Apply(built.mods, built.conditional, ctx)
//   gives the live Modifiers for the current state (no allocations).
// - Thermal Runaway ("flag:thermal_runaway", value 2): Build multiplies the stat effects of OVERCLOCK exploits by it
//   (add: value x2, mul: (value - 1) x2 + 1). The game doubles OVERCLOCK flag values itself when the flag is held.
// - Exclusive groups: one held exploit per group. Build skips exploits whose exclusiveGroup equals `mode`
//   (Zero-Day is in group "daily", so mode "daily" disables it).
// - Mutator.ranks[r - 1] = total heat of a deep-layer mutator at rank r; effects[r - 1] = its effects at rank r.
//   Run mutators have no ranks and one effects entry; their `when` is a room-eligibility gate (only `node` is known).
namespace Desync.Content
{
    /// Live values a condition reads.
    public struct ConditionContext
    {
        public int chain, trace, node;
        public float melt;
        public Layer layer;
    }

    public static class Conditions
    {
        /// True when every clause holds; null/empty = always. Malformed = false. No allocations.
        public static bool Eval(string when, in ConditionContext c) => string.IsNullOrEmpty(when) || (Run(when, c, out bool ok) && ok);

        public static bool IsValid(string when)
        {
            if (string.IsNullOrEmpty(when)) return true;
            Run(when, default, out bool ok);
            return ok;
        }

        /// built + every conditional effect that holds now (adds, muls, sets), clamped. No allocations.
        public static Modifiers Apply(in Modifiers built, List<Effect> conditional, in ConditionContext c)
        {
            var m = built;
            ContentImpl.Fold(ref m, conditional, true, c);
            return m.Clamped();
        }

        static bool Run(string s, in ConditionContext c, out bool ok)
        {
            ok = false;
            bool all = true;
            int i = 0;
            while (true)
            {
                i = Skip(s, i);
                int v0 = i;
                while (i < s.Length && s[i] >= 'a' && s[i] <= 'z') i++;
                int vLen = i - v0;
                i = Skip(s, i);
                if (i >= s.Length) return false;
                int op;   // 0 >=, 1 <=, 2 >, 3 <, 4 =
                if (s[i] == '>' || s[i] == '<')
                {
                    bool gt = s[i] == '>';
                    i++;
                    if (i < s.Length && s[i] == '=') { op = gt ? 0 : 1; i++; } else op = gt ? 2 : 3;
                }
                else if (s[i] == '=') { op = 4; i++; }
                else return false;
                i = Skip(s, i);

                bool hold;
                if (Is(s, v0, vLen, "layer"))
                {
                    int w0 = i;
                    while (i < s.Length && s[i] >= 'a' && s[i] <= 'z') i++;
                    bool src = Is(s, w0, i - w0, "source");
                    if (op != 4 || (!src && !Is(s, w0, i - w0, "render"))) return false;
                    hold = (c.layer == Layer.Source) == src;
                }
                else
                {
                    float lhs;
                    if (Is(s, v0, vLen, "chain")) lhs = c.chain;
                    else if (Is(s, v0, vLen, "melt")) lhs = c.melt;
                    else if (Is(s, v0, vLen, "trace")) lhs = c.trace;
                    else if (Is(s, v0, vLen, "node")) lhs = c.node;
                    else return false;
                    if (!Number(s, ref i, out float rhs)) return false;
                    hold = op == 0 ? lhs >= rhs : op == 1 ? lhs <= rhs : op == 2 ? lhs > rhs : op == 3 ? lhs < rhs : lhs == rhs;
                }
                all &= hold;

                i = Skip(s, i);
                if (i == s.Length) { ok = true; return all; }
                if (s[i] != '&') return false;
                i++;
            }
        }

        static int Skip(string s, int i) { while (i < s.Length && s[i] == ' ') i++; return i; }

        static bool Is(string s, int start, int len, string word) => len == word.Length && string.CompareOrdinal(s, start, word, 0, len) == 0;

        static bool Number(string s, ref int i, out float v)
        {
            v = 0f;
            int start = i;
            float scale = 0f;   // 0 = integer part, else weight of the next fraction digit
            for (; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch >= '0' && ch <= '9')
                {
                    if (scale == 0f) v = v * 10f + (ch - '0');
                    else { v += (ch - '0') * scale; scale *= 0.1f; }
                }
                else if (ch == '.' && scale == 0f) scale = 0.1f;
                else break;
            }
            return i > start && s[i - 1] != '.';
        }
    }

    static class ContentImpl
    {
        const string Flag = "flag:";
        static readonly string[] Passes = { "add", "mul", "set" };
        static readonly int[] Weights = { 60, 30, 10 }, EliteWeights = { 0, 70, 30 };   // C, R, E

        sealed class Doc { public List<Exploit> exploits = new List<Exploit>(); public List<Mutator> mutators = new List<Mutator>(); }

        internal static Catalog FromJson(string[] docs)
        {
            var cat = new Catalog();
            foreach (var json in docs)
            {
                var d = JsonConvert.DeserializeObject<Doc>(json);
                if (d == null) continue;
                cat.exploits.AddRange(d.exploits);
                cat.mutators.AddRange(d.mutators);
            }
            foreach (var e in cat.exploits)
            {
                foreach (var f in e.effects) Check(e.id, f);
                foreach (var r in e.requires)
                    if (!Enum.TryParse(r, true, out Archetype _)) throw new FormatException("content: " + e.id + " requires unknown archetype " + r);
            }
            foreach (var m in cat.mutators)
                foreach (var rank in m.effects)
                    foreach (var f in rank) Check(m.id, f);
            return cat;
        }

        static void Check(string owner, Effect f)
        {
            bool ok = f.stat != null && (IsFlag(f) || (IsStat(f.stat) && Array.IndexOf(Passes, f.op) >= 0));
            if (!ok || !Conditions.IsValid(f.when))
                throw new FormatException("content: bad effect on " + owner + ": " + f.stat + " " + f.op + " " + f.when);
        }

        static bool IsFlag(Effect f) => f.stat.StartsWith(Flag, StringComparison.Ordinal);

        static bool IsStat(string stat) { var m = Modifiers.Default; return ApplyOne(ref m, stat, "add", 0f); }

        static bool ApplyOne(ref Modifiers m, string stat, string op, float v)
        {
            switch (stat)
            {
                case "FlipCost": m.FlipCost = Op(m.FlipCost, op, v); return true;
                case "SourceRise": m.SourceRise = Op(m.SourceRise, op, v); return true;
                case "RenderDrain": m.RenderDrain = Op(m.RenderDrain, op, v); return true;
                case "TraceWindow": m.TraceWindow = Op(m.TraceWindow, op, v); return true;
                case "ChainWindow": m.ChainWindow = Op(m.ChainWindow, op, v); return true;
                case "ChainCap": m.ChainCap = Op(m.ChainCap, op, v); return true;
                case "ScoreMult": m.ScoreMult = Op(m.ScoreMult, op, v); return true;
                case "MoveSpeedMult": m.MoveSpeedMult = Op(m.MoveSpeedMult, op, v); return true;
                case "IceSpeedMult": m.IceSpeedMult = Op(m.IceSpeedMult, op, v); return true;
                case "RedlineThreshold": m.RedlineThreshold = Op(m.RedlineThreshold, op, v); return true;
                case "CameraRangeMult": m.CameraRangeMult = Op(m.CameraRangeMult, op, v); return true;
                case "HackLengthDelta": m.HackLengthDelta = (int)Math.Round(Op(m.HackLengthDelta, op, v)); return true;
                default: return false;
            }
        }

        static float Op(float x, string op, float v) => op == "add" ? x + v : op == "mul" ? x * v : v;

        /// Applies stat effects in pass order (adds, muls, sets); onlyActive skips effects whose `when` does not hold.
        internal static void Fold(ref Modifiers m, List<Effect> fx, bool onlyActive, in ConditionContext c)
        {
            foreach (var pass in Passes)
                foreach (var f in fx)
                    if (f.op == pass && (!onlyActive || Conditions.Eval(f.when, c))) ApplyOne(ref m, f.stat, f.op, f.value);
        }

        internal static Built Build(IReadOnlyList<Exploit> held, IReadOnlyList<MutatorPick> picks, Catalog cat, string mode)
        {
            var b = new Built();
            var fixedFx = new List<Effect>();
            float ocScale = 1f;
            foreach (var e in held)
                foreach (var f in e.effects)
                    if (f.stat == "flag:thermal_runaway") ocScale = f.value;

            foreach (var e in held)
            {
                if (mode != null && e.exclusiveGroup == mode) continue;
                float k = e.archetype == Archetype.Overclock ? ocScale : 1f;
                foreach (var f in e.effects) Route(b, fixedFx, k == 1f ? f : Scaled(f, k));
            }
            foreach (var p in picks)
            {
                var m = cat.mutators.Find(x => x.id == p.id);
                if (m == null || m.effects.Count == 0) continue;
                foreach (var f in m.effects[Math.Min(Math.Max(p.rank, 1), m.effects.Count) - 1]) Route(b, fixedFx, f);
            }

            var mods = Modifiers.Default;
            Fold(ref mods, fixedFx, false, default);
            b.mods = mods.Clamped();
            return b;
        }

        static void Route(Built b, List<Effect> fixedFx, Effect f)
        {
            if (IsFlag(f)) b.flags.Add(f.stat);
            else if (!string.IsNullOrEmpty(f.when)) b.conditional.Add(f);
            else fixedFx.Add(f);
        }

        static Effect Scaled(Effect f, float k) => IsFlag(f) || f.op == "set" ? f
            : new Effect { stat = f.stat, op = f.op, when = f.when, value = f.op == "mul" ? 1f + (f.value - 1f) * k : f.value * k };

        internal static bool CanHold(IReadOnlyList<Exploit> held, Exploit c)
        {
            if (held.Count >= Loadout.MaxHeld) return false;
            int duos = 0;
            foreach (var h in held)
            {
                if (h.id == c.id) return false;
                if (c.exclusiveGroup != null && h.exclusiveGroup == c.exclusiveGroup) return false;
                if (h.archetype == Archetype.Duo) duos++;
            }
            if (c.archetype != Archetype.Duo) return true;
            if (duos >= Loadout.MaxDuos) return false;
            foreach (var r in c.requires)
            {
                Enum.TryParse(r, true, out Archetype need);
                bool found = false;
                foreach (var h in held) if (h.archetype == need) found = true;
                if (!found) return false;
            }
            return true;
        }

        internal static List<Exploit> RollOffer(Mulberry32 rng, Catalog cat, IReadOnlyList<Exploit> held, bool elite, bool fewerChoices)
        {
            int n = fewerChoices ? 2 : 3;
            var w = elite ? EliteWeights : Weights;
            var offer = new List<Exploit>(n);
            var pool = new List<Exploit>();
            for (int slot = 0; slot < n; slot++)
            {
                // the last slot brings a second archetype when every earlier pick shares one (unless none is left)
                bool needOther = slot == n - 1 && slot > 0 && offer.TrueForAll(e => e.archetype == offer[0].archetype);
                Collect(pool, cat, held, offer, needOther);
                if (pool.Count == 0 && needOther) Collect(pool, cat, held, offer, false);
                if (pool.Count == 0) break;
                offer.Add(PickWeighted(rng, pool, w));
            }
            return offer;
        }

        static void Collect(List<Exploit> pool, Catalog cat, IReadOnlyList<Exploit> held, List<Exploit> offer, bool needOther)
        {
            pool.Clear();
            foreach (var e in cat.exploits)
                if (!offer.Contains(e) && CanHold(held, e) && !(needOther && e.archetype == offer[0].archetype)) pool.Add(e);
        }

        /// Rolls a rarity by weight among the rarities present in the pool, then a uniform exploit of that rarity.
        static Exploit PickWeighted(Mulberry32 rng, List<Exploit> pool, int[] w)
        {
            var count = new int[3];
            foreach (var e in pool) count[(int)e.rarity]++;
            int total = 0;
            for (int r = 0; r < 3; r++) if (count[r] > 0) total += w[r];
            if (total == 0) return pool[rng.Range(0, pool.Count)];   // only zero-weight rarities left

            int roll = rng.Range(0, total), tier = 0;
            for (int r = 0; r < 3; r++)
            {
                if (count[r] == 0 || w[r] == 0) continue;
                if (roll < w[r]) { tier = r; break; }
                roll -= w[r];
            }
            int k = rng.Range(0, count[tier]);
            foreach (var e in pool)
                if ((int)e.rarity == tier && k-- == 0) return e;
            throw new InvalidOperationException("unreachable");
        }

        internal static (int heat, List<string> errors) ValidateHeat(IReadOnlyList<MutatorPick> picks, IReadOnlyList<Exploit> held, Catalog cat)
        {
            int heat = 0;
            var errors = new List<string>();
            for (int i = 0; i < picks.Count; i++)
            {
                var p = picks[i];
                var m = cat.mutators.Find(x => x.id == p.id);
                if (m == null) { errors.Add("unknown:" + p.id); continue; }
                if (p.rank < 1 || p.rank > m.ranks.Count) { errors.Add("rank:" + p.id + ":" + p.rank); continue; }
                bool dup = false;
                for (int j = 0; j < i; j++) dup |= picks[j].id == p.id;
                if (dup) { errors.Add("duplicate:" + p.id); continue; }
                heat += m.ranks[p.rank - 1];
                foreach (var c in m.conflicts)
                {
                    bool hit = false;
                    foreach (var e in held) hit |= e.id == c;
                    foreach (var q in picks) hit |= q.id == c;
                    if (hit) errors.Add("conflict:" + m.id + ":" + c);
                }
            }
            return (heat, errors);
        }

        internal static List<Mutator> RollRoomMutators(Mulberry32 rng, Catalog cat, int nodeIndex)
        {
            int node = nodeIndex + 1;
            int count = node <= 1 ? 0 : node == 2 ? rng.Range(0, 2) : node == 3 ? 1 : node == 4 ? rng.Range(1, 3) : 2;
            var ctx = new ConditionContext { node = node };
            var picks = new List<Mutator>(count);
            var pool = new List<Mutator>();
            while (picks.Count < count)
            {
                pool.Clear();
                foreach (var m in cat.mutators)
                    if (m.runMutator && Eligible(m, ctx) && !picks.Exists(p => p.tag == m.tag)) pool.Add(m);
                if (pool.Count == 0) break;
                picks.Add(pool[rng.Range(0, pool.Count)]);
            }
            return picks;
        }

        static bool Eligible(Mutator m, in ConditionContext c)
        {
            foreach (var rank in m.effects)
                foreach (var f in rank)
                    if (!Conditions.Eval(f.when, c)) return false;
            return true;
        }
    }
}
