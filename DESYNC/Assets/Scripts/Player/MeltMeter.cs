using Desync.Rules;
using UnityEngine;

namespace Desync.Player
{
    /// GameState.Melt through Rules.Melt with GameState.Mods: rises in SOURCE, drains in RENDER, flip cost on every flip
    /// (FlipController calls OnFlip). At 100 it holds meltDeathDrip s, then kills.
    public class MeltMeter : MonoBehaviour, IResettable
    {
        public const string GlitchKiller = "GLITCH";   // killer id (and DeathCause.Glitch) when a glitch zone's forced flip pushed melt to 100

        bool warn, glitch;
        float hold;

        static bool Frozen => GameState.God || GameState.Cinematic || !GameState.Alive;

        void OnDisable()
        {
            if (warn) { warn = false; GameEvents.MeltWarning?.Invoke(false); }   // leaving the room ends the heartbeat
        }

        /// Flip cost. Called by FlipController after GameEvents.Flipped, so listeners there still read the melt from before it.
        public void OnFlip(bool forced)
        {
            if (Frozen) return;
            float before = GameState.Melt;
            Set(Melt.Flip(new MeltState(before, Layer.Render), GameState.Mods).Melt);
            if (forced && before < 100f && GameState.Melt >= 100f) glitch = true;
        }

        void Update()
        {
            if (Frozen) return;
            if (GameState.Melt >= 100f)
            {
                hold += Time.deltaTime;   // post-FX draws the drip while Melt >= 100
                if (hold >= GameConfig.I.meltDeathDrip)
                {
                    PlayerRef.LastKillerId = glitch ? GlitchKiller : null;   // GlitchZone left LastKillerPos at its centre
                    PlayerRef.Kill(glitch ? DeathCause.Glitch : DeathCause.Melt);
                }
                return;
            }
            glitch = false;
            var s = new MeltState(GameState.Melt, LayerState.InSource ? Layer.Source : Layer.Render);
            Set(Melt.Step(s, Time.deltaTime, GameState.Mods).Melt);
        }

        /// Pure melt integration step (%/s rates), clamped to 0..100.
        public static float Step(float melt, bool inSource, float dt, float rise, float drain) =>
            Mathf.Clamp(melt + (inSource ? rise : -drain) * dt, 0f, 100f);

        void Set(float v)
        {
            GameState.Melt = Mathf.Clamp(v, 0f, 100f);
            bool w = GameState.Melt >= GameConfig.I.meltWarn;
            if (w == warn) return;
            warn = w;
            GameEvents.MeltWarning?.Invoke(w);
        }

        public void ResetState()
        {
            hold = 0f;
            glitch = false;
            Set(0f);
        }
    }
}
