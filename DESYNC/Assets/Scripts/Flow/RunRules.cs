using System;
using System.Collections.Generic;
using Desync.Content;
using Desync.Gen;
using Desync.Rules;
using UnityEngine;

namespace Desync.Flow
{
    /// The run's numbers and deterministic rolls (no scene, no state): Fragments, Cycles, prices, market stock, room
    /// mutators by node type, forced mirror, dead-zone strips. RunManager applies them; tests call them directly.
    public static class RunRules
    {
        public const int CampaignActs = 3;                     // AUDITOR, COMPILER, ARCHITECT
        public const int MaxBackups = 2, StartBackups = 0;
        public const float MeltCarryShare = 0.5f;              // melt carried from a room's exit into the next room
        public const int FragClear = 10, FragGoldPlus = 10, FragS = 15, FragDeath = 3;
        public const int CyclesClear = 10, CyclesPerGrade = 5, CyclesElite = 10;
        public const int PriceCommon = 25, PriceRare = 40, PriceEpic = 60, RemovalPrice = 20, BackupPrice = 45, RerollPrice = 15;
        public const float Tile = 0.5f;                        // generator tile size, metres

        /// The M2 draft (and market) pool.
        public static readonly string[] M2Exploits =
        {
            "clean_handshake", "packet_loss", "slipstream", "brinkmanship", "thermal_throttle", "burn_rate", "cold_boot", "heat_sink",
            "fever_code", "coolant_leak", "liquid_cooling", "heat_exchange", "log_wipe", "rootkit", "signal_jam", "audit_trail",
        };

        /// Fragments one cleared room earns: clear 10, gold medal or better +10, grade S +15.
        public static int RoomFragments(Medal medal, char grade) =>
            FragClear + (medal >= Medal.Gold ? FragGoldPlus : 0) + (grade == 'S' ? FragS : 0);

        /// What a run banks from `earned`: all of it on a win or a cash-out; on a death 40 % (rounded up) plus the death's own 3.
        public static int Bank(int earned, bool died) => died ? (2 * earned + 4) / 5 + FragDeath : earned;   // ceil(0.4 x), exact

        /// Cycles one cleared room earns: 10, +5 per grade above C, +10 on an Elite node.
        public static int RoomCycles(char grade, bool elite) =>
            CyclesClear + Math.Max(0, "CBAS".IndexOf(grade)) * CyclesPerGrade + (elite ? CyclesElite : 0);

        public static int Price(Rarity r) => r == Rarity.C ? PriceCommon : r == Rarity.R ? PriceRare : PriceEpic;

        /// The full catalog restricted to the M2 exploits (mutators kept).
        public static Catalog DraftPool(Catalog full)
        {
            var c = new Catalog { mutators = full.mutators };
            foreach (var id in M2Exploits)
            {
                var e = full.Exploit(id);
                if (e != null) c.exploits.Add(e);
            }
            return c;
        }

        /// Room mutators for a node: none on a boss, two forced on an Elite (same eligibility and one-per-tag rule as
        /// Loadout.RollRoomMutators), otherwise Loadout.RollRoomMutators by node.
        public static List<Mutator> RoomMutators(Mulberry32 rng, Catalog cat, RunNode node)
        {
            if (node.type == NodeType.Boss) return new List<Mutator>();
            if (node.type != NodeType.Elite) return Loadout.RollRoomMutators(rng, cat, node.index);
            var ctx = new ConditionContext { node = node.index + 1 };
            var picks = new List<Mutator>(2);
            var pool = new List<Mutator>();
            while (picks.Count < 2)
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

        public const string AuditorDronesFlag = "flag:auditor_drones";

        /// Auditor Drones (dispatched at TRACE 2) in generated rooms from act 2's third room on (enemy intro: A2 r3) and in all
        /// of act 3; not in boss arenas or an original room a failed generation fell back to.
        public static bool AuditorDrones(RunNode node, RoomSpec spec) =>
            node.type != NodeType.Boss && spec != null && spec.fallbackRoom == null && (node.act == 2 ? node.index >= 2 : node.act >= 3);

        /// The node's room; with MIRROR the first mirrored room among reseeds of the node seed (the generator mirrors p 0.5).
        public static RoomSpec Room(RunNode node, bool mirror, ChunkLibrary lib) => Room(node, mirror, lib, 0, default);

        /// The same under Deep Layer heat: `heat` raises the threat budget (generator), `placement` adds the heat threats.
        public static RoomSpec Room(RunNode node, bool mirror, ChunkLibrary lib, int heat, HeatPlacement placement)
        {
            var spec = Generator.GenerateRoom(node.roomSeed, node.act, node.index, heat, lib, placement);
            for (int k = 1; mirror && !spec.mirror && spec.fallbackRoom == null && k <= 8; k++)
                spec = Generator.GenerateRoom(Rng.Substream(node.roomSeed, "mirror" + k).NextUInt(), node.act, node.index, heat, lib, placement);
            return spec;
        }

        /// Black Market stock: 3 exploits (a draft roll, priced by rarity), a removal when something is held, a Backup when
        /// one may be bought, a reroll token.
        public static List<MarketItem> MarketStock(Mulberry32 rng, Catalog pool, IReadOnlyList<Exploit> held, bool backupOnSale)
        {
            var items = new List<MarketItem>();
            foreach (var e in Loadout.RollOffer(rng, pool, held, false))
                items.Add(new MarketItem { kind = "exploit", exploitId = e.id, price = Price(e.rarity) });
            if (held.Count > 0) items.Add(new MarketItem { kind = "removal", price = RemovalPrice });
            if (backupOnSale) items.Add(new MarketItem { kind = "backup", price = BackupPrice });
            items.Add(new MarketItem { kind = "reroll", price = RerollPrice });
            return items;
        }

        // ---------- DEAD_ZONES ----------

        public const int StripLong = 8, StripShort = 2, StripGap = 8;   // tiles: 4 m x 1 m strips, >= 4 m apart
        public const float StripClearance = 3f;                          // metres from spawn, exit and terminals

        /// 1..max dead-zone strips (4 m x 1 m, either orientation) on plain floor ('.') whose 0.5 m margin is walkable too
        /// (never across a doorway or inside a SOURCE passage), >= 3 m from spawn, exit and terminals, >= 4 m apart.
        /// Tile coordinates: x = column, y = row (row 0 = north). Deterministic for a given rng state.
        public static List<RectInt> DeadZoneStrips(RoomSpec spec, Mulberry32 rng, int max)
        {
            var strips = new List<RectInt>();
            var t = spec.tiles;
            if (t == null || t.Length == 0 || max <= 0) return strips;
            int rows = t.Length, cols = t[0].Length;
            var cands = new List<RectInt>();
            for (int o = 0; o < 2; o++)
            {
                int w = o == 0 ? StripLong : StripShort, h = o == 0 ? StripShort : StripLong;
                for (int r = 1; r + h < rows; r++)
                    for (int c = 1; c + w < cols; c++)
                    {
                        var rect = new RectInt(c, r, w, h);
                        if (Fits(t, rect) && Clear(spec, rect)) cands.Add(rect);
                    }
            }
            int n = rng.Range(1, max + 1);
            while (strips.Count < n && cands.Count > 0)
            {
                var pick = cands[rng.Range(0, cands.Count)];
                strips.Add(pick);
                cands.RemoveAll(q => Gap(q, pick) < StripGap);   // also removes the pick itself
            }
            return strips;
        }

        /// Room-space metres of a strip: x east, z = -south (GenContracts).
        public static void StripMetres(RectInt s, out float x0, out float x1, out float z0, out float z1)
        {
            x0 = s.xMin * Tile; x1 = s.xMax * Tile;
            z0 = -s.yMax * Tile; z1 = -s.yMin * Tile;
        }

        static bool Fits(string[] t, RectInt s)
        {
            for (int r = s.yMin - 1; r <= s.yMax; r++)
                for (int c = s.xMin - 1; c <= s.xMax; c++)
                {
                    if (c >= t[r].Length) return false;
                    char ch = t[r][c];
                    bool inside = r >= s.yMin && r < s.yMax && c >= s.xMin && c < s.xMax;
                    if (inside ? ch != '.' : ch == '#' || ch == 'X' || ch == 'A' || ch == ' ') return false;
                }
            return true;
        }

        static bool Clear(RoomSpec spec, RectInt s)
        {
            StripMetres(s, out float x0, out float x1, out float z0, out float z1);
            foreach (var e in spec.entities)
            {
                if (e.kind != EntityKind.Spawn && e.kind != EntityKind.Exit && e.kind != EntityKind.Terminal) continue;
                float dx = Math.Max(0f, Math.Max(x0 - e.x, e.x - x1)), dz = Math.Max(0f, Math.Max(z0 - e.z, e.z - z1));
                if (dx * dx + dz * dz < StripClearance * StripClearance) return false;
            }
            return true;
        }

        /// Tiles of empty space between two rects along the axis that separates them most (negative when they overlap).
        static int Gap(RectInt a, RectInt b) =>
            Math.Max(Math.Max(a.xMin - b.xMax, b.xMin - a.xMax), Math.Max(a.yMin - b.yMax, b.yMin - a.yMax));
    }
}
