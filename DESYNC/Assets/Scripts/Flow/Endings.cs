using System.Globalization;
using Desync.Enemies;
using Desync.Net;
using Desync.Rules;
using UnityEngine;

namespace Desync.Flow
{
    /// The three ARCHITECT endings. Exposed (sever the final string) and Merged (refuse the final flip) are always there; the
    /// true ending, Desync, is offered in the ARCHITECT's final window once the player has KillsForDesync ARCHITECT kills and
    /// every shard (ArchitectBoss.OfferDesync, set as each ARCHITECT room starts). Seen endings are SaveV3.unlocks
    /// "ending_<name>" (RunManager records them on BossEvents.Ending); the kill count is SaveV3.settings[KillsKey]: every
    /// cleared ARCHITECT room, whichever ending it took.
    public static class Endings
    {
        public const string Exposed = "exposed", Merged = "merged", Desync = "desync";
        public static readonly string[] All = { Exposed, Merged, Desync };
        public const int KillsForDesync = 3;
        public const string KillsKey = "ending_architect_kills";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            GameEvents.RoomStarted -= OnRoomStarted;
            GameEvents.RoomStarted += OnRoomStarted;
            GameEvents.RoomCleared -= OnRoomCleared;
            GameEvents.RoomCleared += OnRoomCleared;
        }

        /// SaveV3.unlocks flag of an ending ("Exposed" -> "ending_exposed"), as RunUnlocks.RecordEnding writes it.
        public static string Flag(string ending) => "ending_" + ending.ToLowerInvariant();

        public static bool Seen(SaveV3 save, string ending) => save.unlocks.Contains(Flag(ending));

        /// ARCHITECT kills so far. A profile from before the count existed has its first-kill flag: that is one.
        public static int ArchitectKills(SaveV3 save) =>
            save.settings.TryGetValue(KillsKey, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? Mathf.Max(0, v) : RunUnlocks.Killed(save, 3) ? 1 : 0;

        public static void RecordArchitectKill(SaveV3 save) =>
            save.settings[KillsKey] = (ArchitectKills(save) + 1).ToString(CultureInfo.InvariantCulture);

        /// The true ending is on offer: KillsForDesync ARCHITECT kills and all ShardBook.Total shards.
        public static bool DesyncReady(SaveV3 save) => ArchitectKills(save) >= KillsForDesync && ShardBook.Count(save) >= ShardBook.Total;

        static bool ArchitectRoom => RunManager.I && RunManager.I.RoomLive && RunManager.I.CurrentBoss == ArchitectBoss.Id;

        static void OnRoomStarted(int room)
        {
            ArchitectBoss.OfferDesync = false;
            if (!ArchitectRoom) return;
            var save = SaveService.Data;
            // pin the count before this fight can add the first-kill flag (which ArchitectKills reads when there is no count)
            if (!save.settings.ContainsKey(KillsKey)) save.settings[KillsKey] = ArchitectKills(save).ToString(CultureInfo.InvariantCulture);
            ArchitectBoss.OfferDesync = DesyncReady(save);
        }

        static void OnRoomCleared(RoomResult result)
        {
            if (!ArchitectRoom) return;
            RecordArchitectKill(SaveService.Data);
            SaveService.Save();
        }
    }
}
