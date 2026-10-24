using System;
using System.Collections.Generic;
using Desync.Gen;
using Desync.Rules;
using UnityEngine;

namespace Desync.Flow
{
    /// Time-Attack rooms and their records (no scene, no state beyond caches).
    ///
    /// 42 rooms with stable ids: the six classic rooms ("r1".."r6": their scenes, rooms.json dev times) and 36 generated ones,
    /// 12 per act ("ta_a1_01".."ta_a3_12"): seed = FNV-1a of the id, node = (number - 1) / 3, heat 0, so an id builds the same
    /// room on every machine for as long as the chunks are unchanged. T_dev of a generated room = the validator's best-path
    /// time x 1.3 rounded to 0.5 s (D-009, the rule the classic rooms' dev times use).
    ///
    /// Save (SaveV3 is frozen): the best clear in best[id] for every room; the medal in medals[id] for a classic room (NetHost
    /// records those from the room report) and as ONE unlock key "<id>_<medal>" for a generated room (e.g. "ta_a1_07_gold", the
    /// best medal only: an upgrade replaces the key); ghosts in settings["ta_ghost_<id>"] for the MaxGhosts rooms improved most
    /// recently (their order in settings["ta_ghosts"]). 42 ghosts of up to ~3.2 KB would not fit the 64 KB save (SAVE-05).
    public static class TimeAttackRooms
    {
        public const int Acts = 3, PerAct = 12, MaxGhosts = 10;
        public const string UnlockKey = "unlock_timeattack", GhostOrderKey = "ta_ghosts", GhostPrefix = "ta_ghost_";
        static readonly string[] MedalWords = { "", "bronze", "silver", "gold", "sync" };
        static readonly string[] MedalCodes = { "", "B", "S", "G", "SYNC" };   // SaveV3.medals, as SaveService writes them

        public sealed class Room
        {
            public string id;
            public int act;       // 0 = classic, 1..3
            public int number;    // 1..6 classic, 1..12 in an act
            public int classic;   // scene index 0..5; -1 = generated
            public bool Classic => classic >= 0;
            public int Node => (number - 1) / 3;
            /// "R03 · ICE" / "A1-07".
            public string Label => Classic
                ? "R" + number.ToString("00") + " · " + (GameConfig.I.roomNames != null && classic < GameConfig.I.roomNames.Length ? GameConfig.I.roomNames[classic] : "")
                : "A" + act + "-" + number.ToString("00");
            internal RoomSpec spec;
            internal float tDev = -1f;
        }

        /// Classic r1..r6, then act 1, 2, 3 rooms 1..12.
        public static readonly IReadOnlyList<Room> All = Build();

        static List<Room> Build()
        {
            var list = new List<Room>(SceneFlow.RoomCount + Acts * PerAct);
            for (int i = 0; i < SceneFlow.RoomCount; i++) list.Add(new Room { id = ScoreKeeper.RoomId(i), number = i + 1, classic = i });
            for (int a = 1; a <= Acts; a++)
                for (int n = 1; n <= PerAct; n++) list.Add(new Room { id = "ta_a" + a + "_" + n.ToString("00"), act = a, number = n, classic = -1 });
            return list;
        }

        public static Room Find(string id)
        {
            foreach (var r in All) if (r.id == id) return r;
            return null;
        }

        // ---------- generation ----------

        static ChunkLibrary chunks;
        /// Every chunk under Resources/Content/chunks, in name order (as the run loads them).
        static ChunkLibrary Chunks
        {
            get
            {
                if (chunks != null) return chunks;
                var docs = Resources.LoadAll<TextAsset>("Content/chunks");
                Array.Sort(docs, (a, b) => string.CompareOrdinal(a.name, b.name));
                chunks = new ChunkLibrary();
                foreach (var d in docs) chunks.chunks.AddRange(ChunkLibrary.FromJson(d.text).chunks);
                return chunks;
            }
        }

        /// The generated room's spec (cached), null for a classic room. Should the generator ever fall back to a classic room
        /// for an id, it is reseeded deterministically (id#1, id#2, ...) so the id stays a generated room.
        public static RoomSpec Spec(Room r)
        {
            if (r == null || r.Classic) return null;
            if (r.spec != null) return r.spec;
            var spec = Generator.GenerateRoom(Rng.HashStr(r.id), r.act, r.Node, 0, Chunks);
            for (int k = 1; spec.fallbackRoom != null && k <= 8; k++)
                spec = Generator.GenerateRoom(Rng.HashStr(r.id + "#" + k), r.act, r.Node, 0, Chunks);
            return r.spec = spec;
        }

        /// Dev time in seconds (cached; the first call on a generated room generates and validates it, ~10-30 ms).
        public static float TDev(Room r)
        {
            if (r == null) return 0f;
            if (r.tDev >= 0f) return r.tDev;
            if (r.Classic)
            {
                RoomReports.RoomTargets(r.classic, 1, out float t, out _);
                return r.tDev = t;
            }
            var spec = Spec(r);
            return r.tDev = spec.fallbackRoom != null ? 0f : DevTime(Generator.Validate(spec).bestPathSeconds);
        }

        /// TDev is known without generating anything.
        public static bool HasTDev(Room r) => r != null && r.tDev >= 0f;

        /// D-009: validator best path x 1.3, rounded to 0.5 s.
        public static float DevTime(float bestPathSeconds) => (float)Math.Round(bestPathSeconds * 1.3 * 2.0, MidpointRounding.AwayFromZero) / 2f;

        /// FNV-1a over the tile map: a stored ghost is only replayed in the layout it was recorded in.
        public static uint Layout(RoomSpec spec)
        {
            unchecked
            {
                uint h = 2166136261;
                if (spec?.tiles != null)
                    foreach (var row in spec.tiles)
                        foreach (char c in row) { h ^= c; h *= 16777619; }
                return h;
            }
        }

        // ---------- save ----------

        /// Development builds and the editor open Time-Attack without the unlock (tests switch it off to see the lock).
        public static bool DebugOpen = Debug.isDebugBuild;

        /// Time-Attack is open with the meta unlock "unlock_timeattack" (or DebugOpen).
        public static bool Unlocked(SaveV3 save) => Unlocked(save, DebugOpen);
        public static bool Unlocked(SaveV3 save, bool debug) => debug || (save != null && save.unlocks.Contains(UnlockKey));

        public static string MedalKey(string id, Medal m) => id + "_" + MedalWords[(int)m];

        public static Medal MedalOf(SaveV3 save, Room r)
        {
            if (save == null || r == null) return Medal.None;
            if (r.Classic) return save.medals.TryGetValue(r.id, out var code) ? (Medal)Math.Max(0, Array.IndexOf(MedalCodes, code)) : Medal.None;
            for (var m = Medal.Sync; m >= Medal.Bronze; m--) if (save.unlocks.Contains(MedalKey(r.id, m))) return m;
            return Medal.None;
        }

        /// Best clear time in seconds, 0 when none.
        public static float BestTime(SaveV3 save, Room r) =>
            save != null && r != null && save.best.TryGetValue(r.id, out var b) && b != null ? b.time : 0f;

        /// Generated rooms that hold SYNC (0..36): the "SYNC on all 36 rooms" milestone.
        public static int SyncCount(SaveV3 save)
        {
            int n = 0;
            foreach (var r in All) if (!r.Classic && MedalOf(save, r) == Medal.Sync) n++;
            return n;
        }

        /// A generated room's clear: best time, score and grade kept separately in best[id] (as SaveService does for the classic
        /// rooms) and the medal key when the medal is better. Classic rooms are recorded by NetHost from the same report.
        public static void Record(SaveV3 save, Room r, RoomReport rep)
        {
            if (save == null || r == null || r.Classic || rep == null || rep.time <= 0f) return;
            if (!save.best.TryGetValue(r.id, out var b) || b == null) save.best[r.id] = b = new RoomBest { grade = "" };
            if (b.time <= 0f || rep.time < b.time) b.time = rep.time;
            if (rep.total > b.score) b.score = rep.total;
            if ("CBAS".IndexOf(rep.grade) > (string.IsNullOrEmpty(b.grade) ? -1 : "CBAS".IndexOf(b.grade[0]))) b.grade = rep.grade.ToString();
            var had = MedalOf(save, r);
            if (rep.medal <= had) return;
            if (had != Medal.None) save.unlocks.Remove(MedalKey(r.id, had));
            save.unlocks.Add(MedalKey(r.id, rep.medal));
        }

        public static string LoadGhost(SaveV3 save, string id) =>
            save != null && save.settings.TryGetValue(GhostPrefix + id, out var g) ? g : null;

        /// Stores a room's ghost as the most recent; the oldest beyond MaxGhosts are dropped.
        public static void StoreGhost(SaveV3 save, string id, string ghost)
        {
            save.settings.TryGetValue(GhostOrderKey, out var o);
            var order = new List<string>(string.IsNullOrEmpty(o) ? Array.Empty<string>() : o.Split(','));
            order.Remove(id);
            order.Add(id);
            save.settings[GhostPrefix + id] = ghost;
            while (order.Count > MaxGhosts)
            {
                save.settings.Remove(GhostPrefix + order[0]);
                order.RemoveAt(0);
            }
            save.settings[GhostOrderKey] = string.Join(",", order);
        }
    }
}
