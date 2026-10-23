using UnityEngine;

namespace Desync.Player
{
    /// Space / GlitchForcedFlip toggles RENDER<->SOURCE. Returning to RENDER inside a WallRender volume is rejected.
    public class FlipController : MonoBehaviour
    {
        void OnEnable() => GameEvents.GlitchForcedFlip += OnForced;
        void OnDisable() => GameEvents.GlitchForcedFlip -= OnForced;

        static bool CanFlip => GameState.Alive && !GameState.Paused && !GameState.Cinematic;

        void OnForced() { if (CanFlip) TryFlip(); }

        void Update()
        {
            if (GameState.Pulse > 0f)
                GameState.Pulse = Mathf.Max(0f, GameState.Pulse - Time.unscaledDeltaTime / Mathf.Max(0.01f, GameConfig.I.flipPulse));
            if (CanFlip && GameInput.Flip.WasPressedThisFrame()) TryFlip();
        }

        /// No cooldown; allowed mid-hack. Melt spike is applied by MeltMeter on GameEvents.Flipped.
        public bool TryFlip()
        {
            bool toSource = !LayerState.InSource;
            if (!toSource && Blocked()) { GameEvents.FlipRejected?.Invoke(); return false; }
            LayerState.Set(toSource);
            GameState.Flips++;
            GameState.Pulse = 1f;
            GameEvents.Flipped?.Invoke(toSource);
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
