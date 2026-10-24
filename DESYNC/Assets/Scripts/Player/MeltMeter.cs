using Desync.Rules;
using UnityEngine;

namespace Desync.Player
{
    /// GameState.Melt through Rules.Melt with GameState.Mods: rises in SOURCE, drains in RENDER, flip cost on every flip
    /// (FlipController calls OnFlip). At 100 it holds meltDeathDrip s, then kills. Run perks: Heat Sink turns the first 100 of
    /// an attempt into RunPerks.HeatSinkTo; Coolant Leak / Heat Exchange take melt off per hack completion / GRAZE.
    public class MeltMeter : MonoBehaviour, IResettable
    {
        public const string GlitchKiller = "GLITCH";   // killer id (and DeathCause.Glitch) when a glitch zone's forced flip pushed melt to 100

        bool warn, glitch, sunk;
        float hold;

        static bool Frozen => GameState.God || GameState.Cinematic || !GameState.Alive;

        void OnEnable()
        {
            GameEvents.HackFinished += OnHackFinished;
            GameEvents.Grazed += OnGrazed;
        }

        void OnDisable()
        {
            GameEvents.HackFinished -= OnHackFinished;
            GameEvents.Grazed -= OnGrazed;
            if (warn) { warn = false; GameEvents.MeltWarning?.Invoke(false); }   // leaving the room ends the heartbeat
        }

        void OnHackFinished(bool perfect) { if (RunPerks.CoolantLeak > 0f && !Frozen) Set(GameState.Melt - RunPerks.CoolantLeak); }
        void OnGrazed(string ice) { if (RunPerks.HeatExchange > 0f && !Frozen) Set(GameState.Melt - RunPerks.HeatExchange); }

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
                if (!sunk && RunPerks.HeatSinkTo >= 0f) { sunk = true; glitch = false; hold = 0f; Set(RunPerks.HeatSinkTo); return; }
                hold += Time.deltaTime;   // post-FX draws the drip while Melt >= 100
                if (hold >= GameConfig.I.meltDeathDrip)
                {
                    PlayerRef.LastKillerId = glitch ? GlitchKiller : null;   // GlitchZone left LastKillerPos at its centre
                    PlayerRef.Kill(glitch ? DeathCause.Glitch : DeathCause.Melt);
                }
                return;
            }
            hold = 0f;   // back under 100 (Coolant Leak): a later 100 drips from the start
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
            glitch = sunk = false;
            Set(0f);
        }
    }
}
