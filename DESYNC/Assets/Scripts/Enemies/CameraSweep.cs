using UnityEngine;

namespace Desync.Enemies
{
    /// Security camera sweep: pivot yaw = centre -/+ halfAngle, one way per oneWay seconds (eased at the ends),
    /// a pure function of RoomClock.Time + phase. Raises CameraTick(pos) at every sweep end while in RENDER.
    /// Unset fields (< 0) use GameConfig (the classic rooms); halfAngle 0 = a fixed camera (no motion, no ticks).
    /// SPOTLIGHT (EnemyFlags) runs the sweep 30 % faster.
    public class CameraSweep : MonoBehaviour, IResettable
    {
        public Transform pivot;    // local yaw offset from the root's centre yaw
        public float phase;
        public float halfAngle = -1f;   // degrees either side of the centre yaw
        public float oneWay = -1f;      // seconds per one-way sweep

        int _half = int.MinValue;

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
            float t = RoomClock.Time * (EnemyFlags.Spotlight ? EnemyFlags.SpotlightSpeed : 1f) + phase;
            pivot.localRotation = Quaternion.Euler(0f, OffsetAt(t), 0f);
            if (Half <= 0f) return;
            int half = Mathf.FloorToInt(t / OneWay);
            if (half == _half) return;
            if (_half != int.MinValue && !LayerState.InSource && !EnemyFlags.BlindCams) GameEvents.CameraTick?.Invoke(pivot.position);
            _half = half;
        }

        public void ResetState() => _half = int.MinValue;
    }
}
