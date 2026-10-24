using System;
using System.Collections.Generic;
using Desync.Net;
using Desync.Rules;

namespace Desync.Flow
{
    // Cosmetics earned in the Deep Layer (D-014, M4): a title or a flip effect per level from DL 11 to DL 20, and the SYNC GOLD
    // flip trail at DL 30. They change how a flip looks or what the profile is called, nothing else: no stat, no threat hue
    // (non-negotiable 5: every effect draws in the palette's own hues, or gold). Engine-free: the tables and the SaveV3 reads.
    //
    // Save keys (SaveV3.unlocks): "dl_reward_<level>" once the level's reward is earned, "cosmetic_<id>" for the item itself.
    // The equipped item per slot lives in SaveV3.settings["cosmetic_flip"] / ["cosmetic_title"] (an id, absent = none).

    public enum CosmeticSlot { Flip, Title }

    public sealed class Cosmetic
    {
        public readonly string id, name, desc;
        public readonly CosmeticSlot slot;
        public readonly int level;   // the Deep Layer whose first win earns it
        public Cosmetic(string id, CosmeticSlot slot, string name, string desc, int level) { this.id = id; this.slot = slot; this.name = name; this.desc = desc; this.level = level; Key = "cosmetic_" + id; }
        public readonly string Key;   // "cosmetic_<id>", built once: the flip effect checks ownership on every flip
        public string RewardKey => DeepRewards.Key(level);
    }

    public static class Cosmetics
    {
        public static readonly Cosmetic[] All =
        {
            new Cosmetic("deep_diver", CosmeticSlot.Title, "DEEP DIVER", "Title: cleared Deep Layer 11.", 11),
            new Cosmetic("ripple", CosmeticSlot.Flip, "RIPPLE", "Flip effect: a thin ring spreads from the centre.", 12),
            new Cosmetic("heat_walker", CosmeticSlot.Title, "HEAT WALKER", "Title: cleared Deep Layer 13.", 13),
            new Cosmetic("shards", CosmeticSlot.Flip, "SHARDS", "Flip effect: eight shards fly outward.", 14),
            new Cosmetic("redliner", CosmeticSlot.Title, "REDLINER", "Title: cleared Deep Layer 15.", 15),
            new Cosmetic("afterglow", CosmeticSlot.Flip, "AFTERGLOW", "Flip effect: two soft rings trail the flip.", 16),
            new Cosmetic("phase_runner", CosmeticSlot.Title, "PHASE RUNNER", "Title: cleared Deep Layer 17.", 17),
            new Cosmetic("streak", CosmeticSlot.Flip, "STREAK", "Flip effect: a light streak crosses the screen.", 18),
            new Cosmetic("null_entity", CosmeticSlot.Title, "NULL ENTITY", "Title: cleared Deep Layer 19.", 19),
            new Cosmetic("layer_native", CosmeticSlot.Title, "LAYER NATIVE", "Title: cleared Deep Layer 20.", 20),
            new Cosmetic("sync_gold", CosmeticSlot.Flip, "SYNC GOLD", "Flip trail in SYNC gold. Cleared Deep Layer 30.", 30),
        };

        public static Cosmetic Find(string id)
        {
            if (!string.IsNullOrEmpty(id)) foreach (var c in All) if (c.id == id) return c;
            return null;
        }

        public static string SettingKey(CosmeticSlot slot) => slot == CosmeticSlot.Flip ? "cosmetic_flip" : "cosmetic_title";

        public static bool Owned(SaveV3 save, Cosmetic c) => c != null && save.unlocks.Contains(c.Key);

        /// The item worn in `slot`, or null (none chosen, or not owned).
        public static Cosmetic Equipped(SaveV3 save, CosmeticSlot slot)
        {
            if (!save.settings.TryGetValue(SettingKey(slot), out var id) || string.IsNullOrEmpty(id)) return null;
            var c = Find(id);
            return c != null && c.slot == slot && Owned(save, c) ? c : null;
        }

        /// Wears `id` in `slot` ("" or null = none). False, nothing changed, when the id is unknown, not owned or of another slot.
        public static bool Equip(SaveV3 save, CosmeticSlot slot, string id)
        {
            if (string.IsNullOrEmpty(id)) { save.settings.Remove(SettingKey(slot)); return true; }
            var c = Find(id);
            if (c == null || c.slot != slot || !Owned(save, c)) return false;
            save.settings[SettingKey(slot)] = id;
            return true;
        }

        /// The owned items of a slot, in table order.
        public static void OwnedIn(SaveV3 save, CosmeticSlot slot, List<Cosmetic> into)
        {
            into.Clear();
            foreach (var c in All) if (c.slot == slot && Owned(save, c)) into.Add(c);
        }

        /// What the slot wears after one more step: the next owned item after the current one, then none, then the first again.
        public static Cosmetic Next(SaveV3 save, CosmeticSlot slot)
        {
            var cur = Equipped(save, slot);
            bool seen = cur == null, first = true;
            Cosmetic firstOwned = null;
            foreach (var c in All)
            {
                if (c.slot != slot || !Owned(save, c)) continue;
                if (first) { firstOwned = c; first = false; }
                if (seen) return c;
                if (c == cur) seen = true;
            }
            return cur == null ? firstOwned : null;   // past the last: none; from none: the first
        }

        // ---------------------------------------------------------------- the open profile
        public static Cosmetic Equipped(CosmeticSlot slot) => Equipped(SaveService.Data, slot);

        /// Equips on the open profile and writes it.
        public static bool Equip(CosmeticSlot slot, string id)
        {
            if (!Equip(SaveService.Data, slot, id)) return false;
            SaveService.Save();
            return true;
        }
    }

    /// The Deep Layer rewards: one per level from DL 11 to DL 20 (the Fragments bonus stops at DL 20 too), the SYNC GOLD trail at DL 30,
    /// nothing in between (DL 21-29 are played for the DL 30 cosmetic only).
    public static class DeepRewards
    {
        public const int FirstLevel = 11, LastLevel = 20, CosmeticLevel = 30;

        public static string Key(int level) => "dl_reward_" + level;

        /// What winning `level` for the first time earns, or null.
        public static Cosmetic ForLevel(int level)
        {
            foreach (var c in Cosmetics.All) if (c.level == level) return c;
            return null;
        }

        public static bool Earned(SaveV3 save, int level) => save.unlocks.Contains(Key(level));

        /// Grants the rewards of every level in (from, to]: "dl_reward_<n>" + "cosmetic_<id>", and wears the item when its slot is
        /// empty (a first flip effect shows at once). Returns what was newly granted, in level order.
        public static List<Cosmetic> Grant(SaveV3 save, int from, int to)
        {
            var got = new List<Cosmetic>();
            for (int level = Math.Max(0, from) + 1; level <= to; level++)
            {
                var c = ForLevel(level);
                if (c == null || Earned(save, level)) continue;
                save.unlocks.Add(Key(level));
                if (!save.unlocks.Contains(c.Key)) save.unlocks.Add(c.Key);
                if (Cosmetics.Equipped(save, c.slot) == null) Cosmetics.Equip(save, c.slot, c.id);
                got.Add(c);
            }
            return got;
        }
    }
}
