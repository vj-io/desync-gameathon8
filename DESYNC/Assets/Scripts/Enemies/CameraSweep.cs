using UnityEngine;

namespace Desync.Enemies
{
    /// Security camera sweep: pivot yaw = centre -/+ cameraSweepAngle, one way per cameraSweepPeriod (eased at the ends),
    /// a pure function of RoomClock.Time + phase. Raises CameraTick(pos) at every sweep end while in RENDER.
    public class CameraSweep : MonoBehaviour, IResettable
    {
        public Transform pivot;    // local yaw offset from the root's centre yaw
        public float phase;

        int _half = int.MinValue;

        public static float Offset(float t)
        {
            var c = GameConfig.I;
            return -c.cameraSweepAngle * Mathf.Cos(Mathf.PI * t / Mathf.Max(0.1f, c.cameraSweepPeriod));
        }

        void Update()
        {
            if (GameState.Cinematic || !pivot) return;
            float t = RoomClock.Time + phase;
            pivot.localRotation = Quaternion.Euler(0f, Offset(t), 0f);
            int half = Mathf.FloorToInt(t / Mathf.Max(0.1f, GameConfig.I.cameraSweepPeriod));
            if (half == _half) return;
            if (_half != int.MinValue && !LayerState.InSource) GameEvents.CameraTick?.Invoke(pivot.position);
            _half = half;
        }

        public void ResetState() => _half = int.MinValue;
    }
}
