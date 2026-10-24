using Desync.Enemies;
using Desync.Gen;
using Desync.Net;
using UnityEngine;

namespace Desync.Flow
{
    /// Story shards during a run. When a generated run room starts (GameEvents.RoomStarted, the room built and RunManager
    /// live), ShardPlacement decides whether it holds a shard and where; the pickup carries the act's lowest missing shard.
    /// A boss kill (GameEvents.RoomCleared on a boss room) pays the act's next shard directly. Collecting saves it in
    /// SaveV3.unlocks, scores PUPPET STRING (pickups only: EnemyEvents.PuppetString, which ScoreKeeper and the audio cue
    /// already hear) and raises ShardEvents.Collected for the toast. CLASSIC and modes with fixed rooms hold no shards.
    public static class ShardRun
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            GameEvents.RoomStarted -= OnRoomStarted;
            GameEvents.RoomStarted += OnRoomStarted;
            GameEvents.RoomCleared -= OnRoomCleared;
            GameEvents.RoomCleared += OnRoomCleared;
        }

        /// Run modes whose rooms hold shards.
        public static bool ModeHolds(RunMode m) => m == RunMode.Quick || m == RunMode.Campaign || m == RunMode.Daily || m == RunMode.Overclock;

        static bool InRun(out RunManager run)
        {
            run = RunManager.I;
            return run && run.RoomLive && run.CurrentNode != null && ModeHolds(RunState.Mode);
        }

        static void OnRoomStarted(int room)
        {
            if (!InRun(out var run) || run.CurrentBoss != null || !RoomInfo.Current) return;
            var spec = run.CurrentSpec;
            var node = run.CurrentNode;
            var save = SaveService.Data;
            string id = ShardBook.Next(save, node.act);
            if (id == null || !ShardPlacement.Wants(spec, node.type, node.act, ShardBook.Count(save))) return;
            if (!ShardPlacement.Spot(spec, out float x, out float z)) return;
            var host = RoomInfo.Current.transform.parent;   // the room handle: the shard goes when the room does
            var local = new Vector3(x, ShardPickup.Height, z);
            ShardPickup.Create(host, host ? host.TransformPoint(local) : local, id);
        }

        static void OnRoomCleared(RoomResult result)
        {
            if (!InRun(out var run) || run.CurrentBoss == null) return;
            string id = ShardBook.Next(SaveService.Data, run.CurrentNode.act);
            if (id != null) Collect(id, false);
        }

        /// Saves `id`; `pickup` = touched in a room (scores PUPPET STRING), else paid by a boss kill. Nothing when already held.
        public static void Collect(string id, bool pickup)
        {
            if (!ShardBook.Collect(SaveService.Data, id)) return;
            SaveService.Save();
            if (pickup) EnemyEvents.PuppetString?.Invoke(id);
            ShardEvents.Collected?.Invoke(id);
        }
    }
}
