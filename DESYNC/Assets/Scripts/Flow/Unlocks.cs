using System;
using System.Collections.Generic;
using Desync.Content;
using Desync.Gen;
using Desync.Net;
using Desync.Rules;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Desync.Flow
{
    // The unlock tree (M4): Fragments buy exploit pools, archetype starters, Black Market access and Time-Attack. Content lives in
    // Resources/Content/unlocks.json; what a profile owns lives in SaveV3.unlocks as the keys a node grants ("unlock_pool_b",
    // "unlock_timeattack", ...), so other systems may test a key with Unlocks.Has(key) or read the list directly.
    //
    // Rules (BREACH_SPEC "Hybrid structure" anti-grind, "Unlock cadence by hour"):
    // - A cost-0 node is open from the start and is never written to the save (the first 8 exploits, DAILY).
    // - Every node is reachable at Deep Layer 0: costs are Fragments and requirements are other nodes, never a boss, a Deep Layer or a cosmetic.
    // - Only runs whose seed is the player's own are gated (Campaign, Quick, and Deep Layer runs, which are campaigns): the exploit
    //   pool is what the tree opened, the equipped starter is held at the start, and Black Market nodes play as Infiltration rooms until
    //   bought. Shared-seed modes (Daily, and any mode added later) always see everything, so a leaderboard compares like with like.
    // - Deep Layer runs always keep the Black Market: Poor Market is one of their heat mutators.

    /// One node of the tree. Parsed from unlocks.json.
    public sealed class UnlockNode
    {
        public string id, name, desc, when, starter, tag;
        public int cost, col, row;
        public float by;                                                  // hour of play by which a median player can afford it (balance check)
        public readonly List<string> requires = new List<string>();      // node ids that must be owned first
        public readonly List<string> grants = new List<string>();        // keys written to SaveV3.unlocks
        public readonly List<string> exploits = new List<string>();      // exploit ids this node opens (pool nodes)
        public bool Free => cost <= 0;
    }

    public enum UnlockState { Owned, Open, Short, Locked }   // Open = can be bought now; Short = requirements met, not enough Fragments

    public sealed class UnlockData
    {
        public readonly List<UnlockNode> nodes = new List<UnlockNode>();

        public UnlockNode Find(string id)
        {
            if (id != null) foreach (var n in nodes) if (n.id == id) return n;
            return null;
        }

        /// Reading order (row, then column): the focus order of the screen.
        public List<UnlockNode> Ordered()
        {
            var l = new List<UnlockNode>(nodes);
            l.Sort((a, b) => a.row != b.row ? a.row.CompareTo(b.row) : a.col.CompareTo(b.col));
            return l;
        }

        /// Parses {"nodes":[...]}. Parsed by hand (no reflection) so IL2CPP stripping cannot touch it.
        public static UnlockData FromJson(string json)
        {
            var d = new UnlockData();
            var root = JObject.Parse(json);
            if (!(root["nodes"] is JArray arr)) return d;
            foreach (var t in arr)
            {
                var o = (JObject)t;
                var n = new UnlockNode
                {
                    id = (string)o["id"], name = (string)o["name"] ?? (string)o["id"], desc = (string)o["desc"] ?? "", when = (string)o["when"] ?? "",
                    starter = (string)o["starter"], tag = (string)o["tag"] ?? "", cost = (int?)o["cost"] ?? 0, col = (int?)o["col"] ?? 0, row = (int?)o["row"] ?? 0, by = (float?)o["by"] ?? 0f,
                };
                Add(n.requires, o["requires"]); Add(n.grants, o["grants"]); Add(n.exploits, o["exploits"]);
                d.nodes.Add(n);
            }
            return d;
        }

        static void Add(List<string> into, JToken t)
        {
            if (t is JArray a) foreach (var x in a) into.Add((string)x);
        }

        /// Why the data is unusable, empty when it is sound: duplicate ids / grants / exploits / cells, requirements that do not exist or
        /// loop, paid nodes that grant nothing, grants outside "unlock_", exploits or starters the catalog lacks, requirements on bosses
        /// or Deep Layer keys (every unlock must be reachable at DL 0).
        public List<string> Validate(Catalog cat)
        {
            var e = new List<string>();
            var ids = new HashSet<string>(); var grants = new HashSet<string>(); var exploits = new HashSet<string>(); var cells = new HashSet<string>();
            foreach (var n in nodes)
            {
                if (string.IsNullOrEmpty(n.id) || !ids.Add(n.id)) e.Add("id:" + n.id);
                if (!cells.Add(n.col + "," + n.row)) e.Add("cell:" + n.id);
                if (n.cost < 0) e.Add("cost:" + n.id);
                if (n.cost > 0 && n.grants.Count == 0) e.Add("nogrant:" + n.id);
                foreach (var g in n.grants)
                {
                    if (!g.StartsWith("unlock_", StringComparison.Ordinal)) e.Add("grant:" + n.id + ":" + g);
                    if (!grants.Add(g)) e.Add("dupgrant:" + g);
                }
                foreach (var r in n.requires)
                {
                    if (Find(r) == null) e.Add("requires:" + n.id + ":" + r);
                    if (r.StartsWith("kill_", StringComparison.Ordinal) || r.StartsWith("dl_", StringComparison.Ordinal) || r.StartsWith("cosmetic_", StringComparison.Ordinal))
                        e.Add("notDL0:" + n.id + ":" + r);
                }
                foreach (var x in n.exploits)
                {
                    if (cat != null && cat.Exploit(x) == null) e.Add("exploit:" + n.id + ":" + x);
                    if (!exploits.Add(x)) e.Add("dupexploit:" + x);
                }
                if (n.starter != null && cat != null && cat.Exploit(n.starter) == null) e.Add("starter:" + n.id + ":" + n.starter);
            }
            foreach (var n in nodes) if (Loops(n, new HashSet<string>())) e.Add("loop:" + n.id);
            return e;
        }

        bool Loops(UnlockNode n, HashSet<string> path)
        {
            if (!path.Add(n.id)) return true;
            foreach (var r in n.requires) { var q = Find(r); if (q != null && Loops(q, path)) return true; }
            path.Remove(n.id);
            return false;
        }
    }

    public static class Unlocks
    {
        public const string BlackMarket = "unlock_blackmarket", TimeAttack = "unlock_timeattack", Daily = "unlock_daily", Duos = "unlock_duos";
        public const string StarterSetting = "unlock_starter";   // SaveV3.settings: the id of the equipped starter node

        // ---------------------------------------------------------------- data + profile
        static UnlockData data;

        /// The tree (Resources/Content/unlocks.json). A missing or broken file gives an empty tree, which gates nothing.
        public static UnlockData Data
        {
            get { return data ?? (data = Load()); }
            set { data = value; }   // test seam
        }

        static UnlockData Load()
        {
            var t = Resources.Load<TextAsset>("Content/unlocks");
            try { return UnlockData.FromJson(t ? t.text : "{}"); }
            catch (Exception ex) { Debug.LogWarning("[DESYNC] unlock tree unreadable (" + ex.Message + ")"); return new UnlockData(); }
        }

        public static int Fragments => SaveService.Data.fragments;

        // ---------------------------------------------------------------- ownership (pure: the profile and the tree are arguments)
        public static bool Owned(SaveV3 s, UnlockData d, UnlockNode n)
        {
            if (n == null) return false;
            if (n.Free)
            {
                foreach (var r in n.requires) if (!Owned(s, d, d.Find(r))) return false;
                return true;
            }
            if (n.grants.Count == 0) return false;
            foreach (var g in n.grants) if (!s.unlocks.Contains(g)) return false;
            return true;
        }

        /// A key the profile holds: written by a purchase, or granted by a free node.
        public static bool Has(SaveV3 s, UnlockData d, string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (s.unlocks.Contains(key)) return true;
            foreach (var n in d.nodes) if (n.Free && n.grants.Contains(key) && Owned(s, d, n)) return true;
            return false;
        }

        public static UnlockState State(SaveV3 s, UnlockData d, UnlockNode n)
        {
            if (Owned(s, d, n)) return UnlockState.Owned;
            foreach (var r in n.requires) if (!Owned(s, d, d.Find(r))) return UnlockState.Locked;
            return s.fragments >= n.cost ? UnlockState.Open : UnlockState.Short;
        }

        /// The first requirement still to buy, or null.
        public static UnlockNode Missing(SaveV3 s, UnlockData d, UnlockNode n)
        {
            foreach (var r in n.requires) { var q = d.Find(r); if (!Owned(s, d, q)) return q; }
            return null;
        }

        /// Spends Fragments and writes the node's grants. False, nothing changed, when it is unknown, owned, locked or too dear.
        public static bool Buy(SaveV3 s, UnlockData d, string id)
        {
            var n = d.Find(id);
            if (n == null || State(s, d, n) != UnlockState.Open) return false;
            s.fragments -= n.cost;
            foreach (var g in n.grants) if (!s.unlocks.Contains(g)) s.unlocks.Add(g);
            return true;
        }

        // ---------------------------------------------------------------- starters
        /// The equipped starter node when it is owned, else null.
        public static UnlockNode Starter(SaveV3 s, UnlockData d)
        {
            if (!s.settings.TryGetValue(StarterSetting, out var id)) return null;
            var n = d.Find(id);
            return n != null && n.starter != null && Owned(s, d, n) ? n : null;
        }

        /// Equips a starter node ("" or null = none). False when it is not an owned starter.
        public static bool SetStarter(SaveV3 s, UnlockData d, string id)
        {
            if (string.IsNullOrEmpty(id)) { s.settings.Remove(StarterSetting); return true; }
            var n = d.Find(id);
            if (n == null || n.starter == null || !Owned(s, d, n)) return false;
            s.settings[StarterSetting] = id;
            return true;
        }

        /// The starter exploit added to `held` (a fresh run), or null when none is equipped or it cannot be held.
        public static Exploit GrantStarter(SaveV3 s, UnlockData d, List<Exploit> held, Catalog cat)
        {
            var n = Starter(s, d);
            var e = n != null ? cat.Exploit(n.starter) : null;
            if (e == null || !Loadout.CanHold(held, e)) return null;
            held.Add(e);
            return e;
        }

        // ---------------------------------------------------------------- the exploit pool
        /// An exploit is open when the profile owns the node that lists it; one no node lists is open (new content never hides).
        public static bool ExploitOpen(SaveV3 s, UnlockData d, string exploitId)
        {
            bool listed = false;
            foreach (var n in d.nodes)
            {
                if (!n.exploits.Contains(exploitId)) continue;
                listed = true;
                if (Owned(s, d, n)) return true;
            }
            return !listed;
        }

        /// `full` restricted to the exploits the profile has opened (mutators kept).
        public static Catalog Pool(SaveV3 s, UnlockData d, Catalog full)
        {
            var c = new Catalog { mutators = full.mutators };
            foreach (var e in full.exploits) if (ExploitOpen(s, d, e.id)) c.exploits.Add(e);
            return c;
        }

        // ---------------------------------------------------------------- the run map
        /// Black Market nodes become Infiltration rooms (same id, links and seed) until the profile owns the access. False when nothing changed.
        public static bool GateMarkets(SaveV3 s, UnlockData d, RunGraph g)
        {
            if (g == null || d.Find("blackmarket") == null || Has(s, d, BlackMarket)) return false;
            bool any = false;
            foreach (var act in g.acts)
                foreach (var n in act)
                    if (n.type == NodeType.Market) { n.type = NodeType.Infil; any = true; }
            return any;
        }

        // ---------------------------------------------------------------- the open profile (what the screens and the run manager call)
        public static bool Has(string key) => Has(SaveService.Data, Data, key);
        public static bool Owned(string id) => Owned(SaveService.Data, Data, Data.Find(id));
        public static UnlockState State(string id) { var n = Data.Find(id); return n == null ? UnlockState.Locked : State(SaveService.Data, Data, n); }

        /// Buys node `id` on the open profile and writes the save.
        public static bool Buy(string id)
        {
            if (!Buy(SaveService.Data, Data, id)) return false;
            SaveService.Save();
            return true;
        }

        public static string Starter() { var n = Starter(SaveService.Data, Data); return n != null ? n.id : ""; }

        public static bool SetStarter(string id)
        {
            if (!SetStarter(SaveService.Data, Data, id)) return false;
            SaveService.Save();
            return true;
        }

        /// Runs whose seed is the player's own: Campaign, Quick and Deep Layer (a campaign). Daily and every shared-seed mode are not.
        public static bool Gated(RunMode mode) => mode == RunMode.Campaign || mode == RunMode.Quick;

        /// The run's draft / market pool: the unlocked exploits for a gated mode, everything otherwise.
        public static Catalog Pool(Catalog full, RunMode mode) => Gated(mode) ? Pool(SaveService.Data, Data, full) : full;

        /// Black Market nodes of a fresh run map, until the access is owned (never in a Deep Layer run or a shared-seed mode).
        public static void GateGraph(RunGraph g, RunMode mode, int deepLevel)
        {
            if (Gated(mode) && deepLevel <= 0) GateMarkets(SaveService.Data, Data, g);
        }

        /// The equipped starter, added to a fresh run's held exploits (gated modes only).
        public static void GrantStarter(List<Exploit> held, Catalog cat, RunMode mode)
        {
            if (Gated(mode)) GrantStarter(SaveService.Data, Data, held, cat);
        }
    }
}
