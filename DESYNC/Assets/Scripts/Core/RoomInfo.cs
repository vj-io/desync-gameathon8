using UnityEngine;

namespace Desync
{
    /// Per-room metadata, placed once in each Room_0N scene by LevelBuilder.
    public class RoomInfo : MonoBehaviour
    {
        public static RoomInfo Current;
        public int index;                 // 0..5
        public int terminalCount = 1;
        public float traceCountdown = -1f; // Room 5: seconds before forced alarm, -1 = off
        public Transform spawn;
        void Awake() { Current = this; }
        void OnDestroy() { if (Current == this) Current = null; }
    }
}
