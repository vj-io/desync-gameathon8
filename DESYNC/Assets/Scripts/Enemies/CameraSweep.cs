using UnityEngine;

namespace Desync.Enemies
{
    /// Security camera sweep: pivot yaw = centre -/+ halfAngle, one way per oneWay seconds (eased at the ends),
    /// a pure function of RoomClock.Time + phase. Raises CameraTick(pos) at every sweep end while in RENDER.
    /// Unset fields (< 0) use GameConfig (the classic rooms); halfAngle 0 = a fixed camera (no motion, no ticks).
    /// SPOTLIGHT (EnemyFlags) runs the sweep 30 % faster.
    /// Perception ladder, telegraph only (the sweep itself never stops, so rooms stay as validated): EnemyEvents.Alerted with
    /// Suspicious for the first SuspiciousTime of its cone's dwell, then Tracing; when sight is lost, Hunting while that
    /// TRACE runs, then Return for one frame and Patrol. Cone colour follows from VisionCone.
    public class CameraSweep : MonoBehaviour, IResettable
    {
        public Transform pivot;    // local yaw offset from the root's centre yaw
        public float phase;
        public float halfAngle = -1f;   // degrees either side of the centre yaw
        public float oneWay = -1f;      // seconds per one-way sweep
        public VisionCone cone;         // set by the factory; found on first use in scenes built before it existed

        int _half = int.MinValue;
        bool _looked;
        Alert _alert;

        float Half => halfAngle < 0f ? GameConfig.I.cameraSweepAngle : halfAngle;
        float OneWay => Mathf.Max(0.1f, oneWay > 0f ? oneWay : GameConfig.I.cameraSweepPeriod);

        /// Classic sweep offset (GameConfig values) at sweep time t.
        public static float Offset(float t)
        {
            var c = GameConfig.I;
            return -c.cameraSweepAngle * Mathf.Cos(Mathf.PI * t / Mathf.Max(0.1f, c.cameraSweepPeriod));
        }

        /// This camera's yaw offset at sweep time t (= RoomClock.Time x speed + phase).
        public float OffsetAt(float t) => -Half * Mathf.Cos(Mathf.PI * t / OneWay);

        void Update()
        {
            if (GameState.Cinematic || !pivot) return;
            Telegraph();
            float t = RoomClock.Time * (EnemyFlags.Spotlight ? EnemyFlags.SpotlightSpeed : 1f) + phase;
            pivot.localRotation = Quaternion.Euler(0f, OffsetAt(t), 0f);
            if (Half <= 0f) return;
            int half = Mathf.FloorToInt(t / OneWay);
            if (half == _half) return;
            if (_half != int.MinValue && !LayerState.InSource && !EnemyFlags.BlindCams) GameEvents.CameraTick?.Invoke(pivot.position);
            _half = half;
        }

        void Telegraph()
        {
            if (!_looked) { _looked = true; if (!cone) cone = GetComponentInChildren<VisionCone>(true); }
            float dwell = cone ? cone.Dwell : 0f;
            Alert a = dwell > 0f ? (dwell >= EnemyEvents.SuspiciousTime ? Alert.Tracing : Alert.Suspicious)
                    : _alert == Alert.Patrol || _alert == Alert.Return ? Alert.Patrol
                    : _alert != Alert.Suspicious && GameState.TraceActive ? Alert.Hunting : Alert.Return;
            if (a == _alert) return;
            _alert = a;
            EnemyEvents.Alerted?.Invoke(pivot.position, a);
        }

        public Alert Alert => _alert;

        public void ResetState() { _half = int.MinValue; _alert = Alert.Patrol; }
    }
}
