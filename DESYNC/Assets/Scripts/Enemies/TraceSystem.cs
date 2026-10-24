using UnityEngine;

namespace Desync.Enemies
{
    /// Per-room surveillance state (one per room scene, created by EnemyFactory; lazily at runtime if missing).
    /// TRACE: any cone dwelling >= traceDwell starts a TRACE (count+1) or, while one runs, holds it at traceDuration.
    /// The 3rd TRACE in a room raises Alarm. Also owns the Last-Known-Position: on a RENDER->SOURCE flip within
    /// LkpWindow seconds of being seen (or during a TRACE) the cyan marker drops at the last seen spot and drones investigate.
    public class TraceSystem : MonoBehaviour, IResettable
    {
        public const float LkpWindow = 2f, LkpLife = 6f;

        public static TraceSystem I;

        public Transform lkpMarker;    // child shown at the LKP (mat.lkp)
        public Transform lkpSpin;

        /// Bumped every time a new LKP is placed; drones compare against the last version they handled.
        [System.NonSerialized] public int LkpVersion;
        [System.NonSerialized] public Vector3 Lkp;

        float _last, _seenAt = -1e9f, _lkpUntil = -1f;
        Vector3 _seenPos;

        void OnEnable() { I = this; GameEvents.Flipped += OnFlipped; }
        void OnDisable() { if (I == this) I = null; GameEvents.Flipped -= OnFlipped; }

        static TraceSystem Get() => I ? I : EnemyFactory.BuildTraceSystem(null);

        /// A cone has LOS on the player this frame.
        public static void Seen(Vector3 playerFeet)
        {
            var t = Get();
            t._seenPos = playerFeet;
            t._seenAt = RoomClock.Time;
        }

        /// A cone has dwelt >= traceDwell this frame.
        public static void Sustain()
        {
            var c = GameConfig.I;
            if (GameState.TraceActive) { GameState.TraceRemaining = c.traceDuration; return; }
            GameState.TraceActive = true;
            GameState.TraceRemaining = c.traceDuration;
            GameState.TraceCount++;
            GameEvents.TraceStarted?.Invoke(GameState.TraceCount);
            if (GameState.TraceCount == c.tracesToAlarm) GameEvents.Alarm?.Invoke();
        }

        void Update()
        {
            float dt = EnemyKit.Step(ref _last);
            if (GameState.TraceActive)
            {
                GameState.TraceRemaining -= dt * (EnemyFlags.LogWipe ? EnemyFlags.LogWipeDecay : 1f);
                if (GameState.TraceRemaining <= 0f)
                {
                    GameState.TraceActive = false;
                    GameState.TraceRemaining = 0f;
                    GameEvents.TraceEnded?.Invoke();
                }
            }
            if (!lkpMarker) return;
            bool show = RoomClock.Time < _lkpUntil;
            if (lkpMarker.gameObject.activeSelf != show) lkpMarker.gameObject.SetActive(show);
            if (show && lkpSpin) lkpSpin.localRotation = Quaternion.Euler(0f, Time.time * 120f, 0f);
        }

        void OnFlipped(bool toSource)
        {
            if (!toSource || _seenAt < -1e8f) return;
            if (!GameState.TraceActive && RoomClock.Time - _seenAt > LkpWindow) return;
            Lkp = _seenPos;
            LkpVersion++;
            _lkpUntil = RoomClock.Time + LkpLife;
            if (lkpMarker) lkpMarker.position = _seenPos;
        }

        public void ResetState()
        {
            GameState.TraceActive = false;
            GameState.TraceRemaining = 0f;
            GameState.TraceCount = 0;
            _last = 0f;
            _seenAt = -1e9f;
            _lkpUntil = -1f;
            LkpVersion = 0;
            if (lkpMarker) lkpMarker.gameObject.SetActive(false);
        }
    }
}
