using UnityEngine;

namespace Desync.Enemies
{
    /// Room 5 glitch floor: while the player stands inside (horizontal radius), a ring shrinks over glitchZoneTime
    /// (GameState.GlitchCountdown). At zero it raises GlitchForcedFlip (FlipController applies it with its normal rejection
    /// rule) and restarts. GlitchZone(true/false) on enter/leave. Works in both layers.
    public class GlitchZone : MonoBehaviour, IResettable
    {
        public float radius = 2f;
        public Transform countdownRing;

        float _remaining, _last;
        bool _inside;

        void Update()
        {
            float dt = EnemyKit.Step(ref _last);
            float total = Mathf.Max(0.1f, GameConfig.I.glitchZoneTime);
            bool inside = PlayerRef.Exists && GameState.Alive && !GameState.Cinematic && Contains(PlayerRef.Position);
            if (inside != _inside)
            {
                _inside = inside;
                _remaining = total;
                GameState.GlitchCountdown = inside ? total : -1f;
                GameEvents.GlitchZone?.Invoke(inside);
            }
            if (inside)
            {
                _remaining -= dt;
                if (_remaining <= 0f)
                {
                    _remaining = total;
                    PlayerRef.LastKillerPos = transform.position;   // MeltMeter names "GLITCH" if this flip melts the player
                    GameEvents.GlitchForcedFlip?.Invoke();
                }
                GameState.GlitchCountdown = _remaining;
            }
            float f = inside ? _remaining / total : 1f + 0.04f * Mathf.Sin(Time.time * 5f);
            if (countdownRing) countdownRing.localScale = new Vector3(f, 1f, f);
        }

        public bool Contains(Vector3 p)
        {
            Vector3 d = p - transform.position;
            if (d.y < -0.6f || d.y > 2.5f) return false;
            d.y = 0f;
            return d.sqrMagnitude <= radius * radius;
        }

        public void ResetState()
        {
            if (_inside) GameState.GlitchCountdown = -1f;
            _inside = false;
            _remaining = GameConfig.I.glitchZoneTime;
            _last = 0f;
            if (countdownRing) countdownRing.localScale = Vector3.one;
        }
    }
}
