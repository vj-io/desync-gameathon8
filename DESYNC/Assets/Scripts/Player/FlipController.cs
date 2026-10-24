using UnityEngine;

namespace Desync.Player
{
    /// Space / GlitchForcedFlip toggles RENDER<->SOURCE. Returning to RENDER inside a WallRender volume is rejected, and so is
    /// SPACE inside a run's dead zone (forced flips still happen there).
    public class FlipController : MonoBehaviour
    {
        MeltMeter melt;
        CharacterController cc;
        float lastFlipAt = -999f;   // double_tap: room time of the previous player flip

        void Awake() { melt = GetComponent<MeltMeter>(); cc = GetComponent<CharacterController>(); }
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
            if (!forced && RunPerks.DoubleTapMelt > 0f) DoubleTap();
            return true;
        }

        /// Double-Tap: a second player flip within the window blinks 2 m forward in the current layer for extra melt.
        void DoubleTap()
        {
            float now = RoomClock.Time;
            if (now - lastFlipAt <= RunPerks.DoubleTapWindow)
            {
                lastFlipAt = -999f;   // the pair is spent; the next flip starts a fresh pair
                Blink(RunPerks.DoubleTapBlink);
                RunPerks.MeltAdjust?.Invoke(RunPerks.DoubleTapMelt);
            }
            else lastFlipAt = now;
        }

        /// Teleports up to `dist` m along the facing, stopping short of anything the player collides with in the current layer
        /// (RENDER walls block, SOURCE walls don't: the layer collision matrix decides). Zero allocations.
        void Blink(float dist)
        {
            Vector3 dir = transform.forward; dir.y = 0f;
            if (!cc || dir.sqrMagnitude < 1e-4f) return;
            dir.Normalize();
            int mask = 0;
            for (int l = 0; l < 32; l++)
                if (l != GameLayers.Player && !Physics.GetIgnoreLayerCollision(GameLayers.Player, l)) mask |= 1 << l;
            float r = cc.radius * 0.9f;
            Vector3 c = transform.TransformPoint(cc.center);
            Vector3 half = transform.up * Mathf.Max(0f, cc.height * 0.5f - cc.radius - 0.05f);   // Blocked()'s capsule: clear of the floor
            if (Physics.CapsuleCast(c - half, c + half, r, dir, out RaycastHit hit, dist, mask, QueryTriggerInteraction.Ignore))
                dist = Mathf.Max(0f, hit.distance - cc.skinWidth);
            cc.enabled = false;   // a blink, not a walk (the controller would sweep and slide)
            transform.position += dir * dist;
            cc.enabled = true;
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
