using UnityEngine;

namespace Desync.Flow
{
    /// Local per-room best clear in PlayerPrefs: "bestT{room}" (seconds) + "bestG{room}" (grade char). Better = higher grade, then faster time.
    /// UI never references Flow: UIKit.BestLabel reads these same two keys.
    public static class BestTimes
    {
        /// Stores `r` if it beats the saved best of its room. Returns true when it did.
        public static bool Record(RoomResult r)
        {
            int rank = "CBAS".IndexOf(r.grade);
            if (r.room < 0 || r.room >= SceneFlow.RoomCount || rank < 0 || r.time <= 0f) return false;
            string kt = "bestT" + r.room, kg = "bestG" + r.room;
            int old = "CBAS".IndexOf((char)PlayerPrefs.GetInt(kg, 0));
            if (rank < old || (rank == old && r.time >= PlayerPrefs.GetFloat(kt, float.MaxValue))) return false;
            PlayerPrefs.SetFloat(kt, r.time);
            PlayerPrefs.SetInt(kg, r.grade);
            PlayerPrefs.Save();   // rare (once per clear): WebGL only flushes to IndexedDB on Save
            return true;
        }
    }
}
