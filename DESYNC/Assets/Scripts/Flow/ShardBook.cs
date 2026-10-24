using System;
using System.Collections.Generic;
using Desync.Rules;
using Newtonsoft.Json;
using UnityEngine;

namespace Desync.Flow
{
    /// One story shard (Content/shards.json): HALCYON memos (act 1), board transcripts (act 2), Architect logs (act 3).
    public sealed class Shard { public string id, title, text; public int act; }

    /// One cue of an ending sequence (Content/shards.json "endings"), played by the UI. t = seconds from the start.
    /// kind: head, sys, logo, wake (text, color, hold) · layer (side render|source) · split (value 1 / 0: both layers on screen
    /// at once) · face (turn the camera to the ARCHITECT over dur) · push (move the camera `value` m along its view over dur) ·
    /// flash (color). sfx: an AssetLib clip played with the cue.
    public sealed class EndingBeat { public float t, hold, value, dur; public string kind, text, color, side, sfx; }

    /// One ARCHITECT ending: id = BossEvents.Ending lower-cased ("exposed", "merged", "desync").
    public sealed class EndingScript { public string id, title; public List<EndingBeat> beats = new List<EndingBeat>(); }

    public sealed class ShardContent
    {
        public sealed class ActTitle { public int act; public string title; }

        public List<ActTitle> acts = new List<ActTitle>();
        public List<Shard> shards = new List<Shard>();
        public List<EndingScript> endings = new List<EndingScript>();

        public static ShardContent FromJson(string json) => JsonConvert.DeserializeObject<ShardContent>(json) ?? new ShardContent();

        public Shard Find(string id)
        {
            foreach (var s in shards) if (s.id == id) return s;
            return null;
        }

        /// The ending's script by BossEvents.Ending name, any case; null when the file has none.
        public EndingScript Ending(string id)
        {
            foreach (var e in endings) if (string.Equals(e.id, id, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        public string Act(int act)
        {
            foreach (var a in acts) if (a.act == act) return a.title;
            return "ACT " + act;
        }
    }

    public static class ShardEvents
    {
        /// A shard went into the save (a pickup, or the one a boss kill pays): its id. The UI shows a toast.
        public static Action<string> Collected;
    }

    /// Story shards in the save: SaveV3.unlocks "shard_a<act>_<nn>", 10 per act. Pickups always carry the act's lowest
    /// missing shard, so each act's story reads in order.
    public static class ShardBook
    {
        public const int Acts = 3, PerAct = 10, Total = Acts * PerAct;

        static readonly string[] Ids = new string[Total];
        static ShardContent content;

        static ShardBook()
        {
            for (int a = 1; a <= Acts; a++)
                for (int n = 1; n <= PerAct; n++) Ids[(a - 1) * PerAct + n - 1] = "shard_a" + a + "_" + n.ToString("00");
        }

        /// Content/shards.json, read once (an empty book when it is missing or broken).
        public static ShardContent Content => content ??= Load();

        static ShardContent Load()
        {
            var ta = Resources.Load<TextAsset>("Content/shards");
            if (!ta) { Debug.LogWarning("[DESYNC] Resources/Content/shards.json missing: no story shards"); return new ShardContent(); }
            try { return ShardContent.FromJson(ta.text); }
            catch (Exception e) { Debug.LogWarning("[DESYNC] shards.json unreadable: " + e.Message); return new ShardContent(); }
        }

        /// "shard_a1_03" for act 1, shard 3 (both from 1).
        public static string Id(int act, int n) => Ids[(Mathf.Clamp(act, 1, Acts) - 1) * PerAct + Mathf.Clamp(n, 1, PerAct) - 1];

        public static bool IsId(string id) => Array.IndexOf(Ids, id) >= 0;

        public static bool Has(SaveV3 save, string id) => save.unlocks.Contains(id);

        public static int Count(SaveV3 save)
        {
            int n = 0;
            foreach (var id in Ids) if (save.unlocks.Contains(id)) n++;
            return n;
        }

        public static int Count(SaveV3 save, int act)
        {
            int n = 0;
            for (int i = 1; i <= PerAct; i++) if (save.unlocks.Contains(Id(act, i))) n++;
            return n;
        }

        /// The act's lowest missing shard, or null when the act is complete (or not an act).
        public static string Next(SaveV3 save, int act)
        {
            if (act < 1 || act > Acts) return null;
            for (int i = 1; i <= PerAct; i++)
            {
                string id = Id(act, i);
                if (!save.unlocks.Contains(id)) return id;
            }
            return null;
        }

        /// Adds a shard to the save; true when it is new.
        public static bool Collect(SaveV3 save, string id)
        {
            if (!IsId(id) || save.unlocks.Contains(id)) return false;
            save.unlocks.Add(id);
            return true;
        }
    }
}
