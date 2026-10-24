using Desync.Rules;
using UnityEngine;

namespace Desync.Player
{
    /// GameState.Melt through Rules.Melt with GameState.Mods: rises in SOURCE, drains in RENDER, flip cost on every flip
    /// (FlipController calls OnFlip). At 100 it holds meltDeathDrip s, then kills. Run perks: Heat Sink turns the first 100 of
    /// an attempt into RunPerks.HeatSinkTo; Coolant Leak / Heat Exchange / Turbo Compile / Overclock Hack add or take melt on
    /// hack completions and GRAZEs; Kernel Panic grants a grace window at 100 to flip to RENDER; Dead Man's Switch revives once
    /// a run on a melt death. Flow-side exploits (Echo Flip, Clock Skew, Double-Tap, Burnout Pulse) adjust melt via MeltAdjust.
    public class MeltMeter : MonoBehaviour, IResettable
    {
        public const string GlitchKiller = "GLITCH";   // killer id (and DeathCause.Glitch) when a glitch zone's forced flip pushed melt to 100

        bool warn, glitch, sunk;
        float hold, grace;

        static bool Frozen => GameState.God || GameState.Cinematic || !GameState.Alive;

        void OnEnable()
        {
            GameEvents.HackFinished += OnHackFinished;
            GameEvents.Grazed += OnGrazed;
            RunPerks.MeltAdjust += OnMeltAdjust;
        }

        void OnDisable()
        {
            GameEvents.HackFinished -= OnHackFinished;
            GameEvents.Grazed -= OnGrazed;
            RunPerks.MeltAdjust -= OnMeltAdjust;
            if (warn) { warn = false; GameEvents.MeltWarning?.Invoke(false); }   // leaving the room ends the heartbeat
        }

        void OnHackFinished(bool perfect)
        {
            if (Frozen) return;
            float delta = -RunPerks.CoolantLeak + RunPerks.TurboCompileMelt;
            if (RunPerks.OverclockHackMelt > 0f && LayerState.InSource) delta += RunPerks.OverclockHackMelt;   // the SOURCE-hack price
            if (delta != 0f) Set(GameState.Melt + delta);
        }

        void OnGrazed(string ice) { if (RunPerks.HeatExchange > 0f && !Frozen) Set(GameState.Melt - RunPerks.HeatExchange); }

        /// Flow-side melt refunds / spends (Echo Flip, Clock Skew, Double-Tap, Burnout Pulse). Negative = refund.
        void OnMeltAdjust(float delta) { if (!Frozen) Set(GameState.Melt + delta); }

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
            if (GameState.Melt >= 100f) { AtHundred(); return; }
            hold = grace = 0f;   // back under 100 (Coolant Leak, a grace flip, a refund): a later 100 drips from the start
            glitch = false;
            var s = new MeltState(GameState.Melt, LayerState.InSource ? Layer.Source : Layer.Render);
            Set(Melt.Step(s, Time.deltaTime, GameState.Mods).Melt);
            if (RunPerks.OverclockedPuppetMelt > 0f && GameState.HijackActive) Set(GameState.Melt + RunPerks.OverclockedPuppetMelt * Time.deltaTime);   // Overclocked Puppet
        }

        /// Melt is pinned at 100: a survive effect may save the attempt, else it drips then kills. Only one survive fires.
        void AtHundred()
        {
            if (!sunk && RunPerks.HeatSinkTo >= 0f) { sunk = true; glitch = false; hold = grace = 0f; Set(RunPerks.HeatSinkTo); return; }

            if (RunPerks.KernelPanicGrace > 0f)
            {
                if (!LayerState.InSource)   // the grace reward: draining in RENDER pulls you back under 100
                {
                    Set(Melt.Step(new MeltState(GameState.Melt, Layer.Render), Time.deltaTime, GameState.Mods).Melt);
                    if (GameState.Melt < 100f) { hold = grace = 0f; glitch = false; return; }
                }
                grace += Time.deltaTime;
                if (grace < RunPerks.KernelPanicGrace) return;   // still inside the grace window
            }

            if (RunPerks.DeadMansReviveTo >= 0f && !glitch)   // a melt death becomes a revive, once per run
            {
                float to = RunPerks.DeadMansReviveTo;
                RunPerks.DeadMansReviveTo = -1f;
                RunPerks.DeadMansFired?.Invoke();
                hold = grace = 0f;
                Set(to);
                return;
            }

            hold += Time.deltaTime;   // post-FX draws the drip while Melt >= 100
            if (hold >= GameConfig.I.meltDeathDrip)
            {
                PlayerRef.LastKillerId = glitch ? GlitchKiller : null;   // GlitchZone left LastKillerPos at its centre
                PlayerRef.Kill(glitch ? DeathCause.Glitch : DeathCause.Melt);
            }
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
            hold = grace = 0f;
            glitch = sunk = false;
            Set(0f);
        }
    }
}
