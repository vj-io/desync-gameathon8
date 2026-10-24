using UnityEngine;

namespace Desync.Enemies
{
    /// Glitch floor: while the player stands inside (horizontal radius), a ring shrinks over `delay` s (GameConfig.glitchZoneTime
    /// when unset: Room 5). At zero it raises GlitchForcedFlip (FlipController applies it with its normal rejection rule) and
    /// restarts. GameState.GlitchCountdown (screen tear, beeps, HUD) is published the whole time, or with `telegraph` > 0
    /// only for the last `telegraph` s (generated rooms: 1.0 s delay, 0.5 s tear). GlitchZone(true/false) on enter/leave.
    /// Works in both layers.
    public class GlitchZone : MonoBehaviour, IResettable
    {
        public float radius = 2f;
        public Transform countdownRing;
        public float delay = -1f;   // seconds inside before the forced flip; <= 0 = GameConfig.glitchZoneTime
        public float telegraph;     // > 0: GlitchCountdown only during the last `telegraph` seconds

        float Total => delay > 0f ? delay : Mathf.Max(0.1f, GameConfig.I.glitchZoneTime);

        float _remaining, _last;
        bool _inside;

        void Update()
        {
            float dt = EnemyKit.Step(ref _last);
            float total = Total;
            bool inside = PlayerRef.Exists && GameState.Alive && !GameState.Cinematic && Contains(PlayerRef.Position);
            if (inside != _inside)
            {
                _inside = inside;
                _remaining = total;
                GameState.GlitchCountdown = inside && telegraph <= 0f ? total : -1f;
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
                GameState.GlitchCountdown = telegraph <= 0f || _remaining <= telegraph ? _remaining : -1f;
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
            _remaining = Total;
            _last = 0f;
            if (countdownRing) countdownRing.localScale = Vector3.one;
        }
    }
}
