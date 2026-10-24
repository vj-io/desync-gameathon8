using UnityEngine;

namespace Desync.Player
{
    /// Space / GlitchForcedFlip toggles RENDER<->SOURCE. Returning to RENDER inside a WallRender volume is rejected, and so is
    /// SPACE inside a run's dead zone (forced flips still happen there).
    public class FlipController : MonoBehaviour
    {
        MeltMeter melt;

        void Awake() => melt = GetComponent<MeltMeter>();
        void OnEnable() => GameEvents.GlitchForcedFlip += OnForced;
        void OnDisable() => GameEvents.GlitchForcedFlip -= OnForced;

        static bool CanFlip => GameState.Alive && !GameState.Paused && !GameState.Cinematic;

        void OnForced() { if (CanFlip) TryFlip(true); }

        void Update()
        {
            if (GameState.Pulse > 0f)
                GameState.Pulse = Mathf.Max(0f, GameState.Pulse - Time.unscaledDeltaTime / Mathf.Max(0.01f, GameConfig.I.flipPulse));
            if (CanFlip && GameInput.Flip.WasPressedThisFrame()) TryFlip();
        }

        /// No cooldown; allowed mid-hack.
        public bool TryFlip() => TryFlip(false);

        bool TryFlip(bool forced)
        {
            if (!forced && RunPerks.InDeadZone(transform.position)) { GameEvents.FlipRejected?.Invoke(); return false; }   // DEAD_ZONES: SPACE does nothing here
            bool toSource = !LayerState.InSource;
            if (!toSource && Blocked()) { GameEvents.FlipRejected?.Invoke(); return false; }
            LayerState.Set(toSource);
            GameState.Flips++;
            GameState.Pulse = 1f;
            GameEvents.Flipped?.Invoke(toSource);   // listeners read the melt from before the flip cost (REDLINE does)
            if (melt) melt.OnFlip(forced);
            return true;
        }

        /// True if the player capsule (slightly shrunk) overlaps any WallRender collider.
        public bool Blocked()
        {
            var cc = GetComponent<CharacterController>();
            float r = cc.radius * 0.9f;
            Vector3 c = transform.TransformPoint(cc.center);
            Vector3 half = transform.up * Mathf.Max(0f, cc.height * 0.5f - cc.radius - 0.05f);
            return Physics.CheckCapsule(c - half, c + half, r, GameLayers.Mask(GameLayers.WallRender), QueryTriggerInteraction.Ignore);
        }
    }
}
