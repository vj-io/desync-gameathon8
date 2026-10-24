using System;
using Desync.Rules;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Desync.Net
{
    /// The player's profile: SaveV3 JSON in PlayerPrefs under "desync.save" (IndexedDB on WebGL), D-005.
    /// Loaded once on first use through SaveMigrator. Writes are throttled (>= 2 s); Flush() forces one, which is what
    /// WebGL needs before the tab goes away because PlayerPrefs only reaches IndexedDB on PlayerPrefs.Save().
    public static class SaveService
    {
        public const string DefaultKey = "desync.save";
        public const double MinSaveInterval = 2.0;
        const string LegacyFlag = "legacyBestImported";

        static string key = DefaultKey;
        static SaveV3 data;
        static bool dirty, readOnly;
        static double lastWrite = double.NegativeInfinity;

        // Index = (int)Rules.Medal. Stored in SaveV3.medals as "B" | "S" | "G" | "SYNC".
        static readonly string[] MedalCodes = { "", "B", "S", "G", "SYNC" };

        /// Seconds on a monotonic clock. Tests replace it to step through the throttle.
        public static Func<double> Clock = () => Time.realtimeSinceStartupAsDouble;

        public static string BackupKey => key + ".backup";

        public static SaveV3 Data { get { if (data == null) Load(); return data; } }

        /// Forget the in-memory profile; the next access reloads it from PlayerPrefs under `saveKey`. Test seam.
        public static void Unload(string saveKey = DefaultKey)
        {
            key = saveKey; data = null; dirty = readOnly = false; lastWrite = double.NegativeInfinity;
        }

        public static void Load()
        {
            dirty = readOnly = false;
            string raw = PlayerPrefs.GetString(key, "");
            if (raw.Length == 0) data = SaveMigrator.Default();
            else
            {
                try
                {
                    // Migrate quietly turns anything it can't read into defaults; a save we are about to overwrite deserves a backup first.
                    if (JObject.Parse(raw)["version"]?.Type != JTokenType.Integer) throw new FormatException("no version");
                    data = SaveMigrator.Migrate(raw);
                }
                catch (NewerSaveException e)
                {
                    // Written by a newer build: never downgrade it. Play on defaults and leave the stored text alone.
                    Debug.LogWarning("[DESYNC] " + e.Message + ": progress will not be saved by this build");
                    data = SaveMigrator.Default(); readOnly = true;
                }
                catch (Exception)
                {
                    PlayerPrefs.SetString(BackupKey, raw);
                    Debug.LogWarning("[DESYNC] save is corrupt: raw text kept under '" + BackupKey + "', starting fresh");
                    data = SaveMigrator.Default(); dirty = true;
                }
            }
            if (string.IsNullOrEmpty(data.playerId)) { data.playerId = Guid.NewGuid().ToString("N"); dirty = true; }
            ImportLegacy(data);
            if (dirty) Save();
        }

        /// Ask for a write. Done now if >= MinSaveInterval since the last one, else Tick() does it when due. True when written now.
        public static bool Save()
        {
            if (data == null) Load();
            dirty = true;
            return TryWrite(false);
        }

        /// Call every frame: performs the write a throttled Save() postponed.
        public static void Tick() { if (dirty) TryWrite(false); }

        /// Write now, ignoring the throttle, and persist every PlayerPrefs value (WebGL).
        public static void Flush() { if (!TryWrite(true)) PlayerPrefs.Save(); }

        static bool TryWrite(bool force)
        {
            if (!dirty || readOnly || data == null) return false;
            double now = Clock();
            if (!force && now - lastWrite < MinSaveInterval) return false;
            PlayerPrefs.SetString(key, SaveMigrator.Serialize(data));
            PlayerPrefs.Save();
            dirty = false; lastWrite = now;
            return true;
        }

        /// Fold a finished room into the profile: best time, best score, best grade and the better medal each keep their own record.
        public static void RecordRoom(RoomReport r, string roomId)
        {
            if (r == null || string.IsNullOrEmpty(roomId) || r.time <= 0f) return;
            var d = Data;
            MergeBest(d, roomId, r.time, r.total, r.grade == '\0' ? "" : r.grade.ToString());
            int m = (int)r.medal;
            if (m > 0 && m < MedalCodes.Length)
            {
                d.medals.TryGetValue(roomId, out var cur);
                if (m > Array.IndexOf(MedalCodes, cur)) d.medals[roomId] = MedalCodes[m];
            }
            Save();
        }

        /// Best clear of a room ("r1".."r6"), or null.
        public static RoomBest GetBest(string roomId) => Data.best.TryGetValue(roomId, out var b) ? b : null;

        public static int MedalCount()
        {
            int n = 0;
            foreach (var v in Data.medals.Values) if (Array.IndexOf(MedalCodes, v) > 0) n++;
            return n;
        }

        /// Sum of the best times over every room cleared so far.
        public static float TotalBestTime()
        {
            float t = 0f;
            foreach (var b in Data.best.Values) if (b.time > 0f) t += b.time;
            return t;
        }

        static void MergeBest(SaveV3 d, string roomId, float time, int score, string grade)
        {
            if (!d.best.TryGetValue(roomId, out var b)) d.best[roomId] = b = new RoomBest { grade = "" };
            if (time > 0f && (b.time <= 0f || time < b.time)) b.time = time;
            if (score > b.score) b.score = score;
            if (GradeRank(grade) > GradeRank(b.grade)) b.grade = grade;
        }

        static int GradeRank(string g) => string.IsNullOrEmpty(g) ? -1 : "CBAS".IndexOf(g[0]);

        // Pre-SaveV3 builds kept bestT{i} (seconds) + bestG{i} (grade char) per room. Copied once; the old keys stay because
        // UIKit.BestLabel still reads them.
        static void ImportLegacy(SaveV3 d)
        {
            if (d.settings.ContainsKey(LegacyFlag)) return;
            for (int i = 0; i < SceneFlow.RoomCount; i++)
            {
                int g = PlayerPrefs.GetInt("bestG" + i, 0);
                float t = PlayerPrefs.GetFloat("bestT" + i, 0f);
                if (g != 0 && t > 0f) MergeBest(d, "r" + (i + 1), t, 0, ((char)g).ToString());
            }
            d.settings[LegacyFlag] = "1";
            dirty = true;
        }
    }
}
