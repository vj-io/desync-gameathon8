using UnityEngine;

namespace Desync.Player
{
    /// GameState.Melt: rises in SOURCE, drains in RENDER, spikes per flip. At 100 it holds meltDeathDrip s, then kills.
    public class MeltMeter : MonoBehaviour, IResettable
    {
        bool warn;
        float hold;

        static bool Frozen => GameState.God || GameState.Cinematic || !GameState.Alive;

        void OnEnable() => GameEvents.Flipped += OnFlipped;
        void OnDisable()
        {
            GameEvents.Flipped -= OnFlipped;
            if (warn) { warn = false; GameEvents.MeltWarning?.Invoke(false); }   // leaving the room ends the heartbeat
        }

        void OnFlipped(bool toSource) { if (!Frozen) Set(GameState.Melt + GameConfig.I.meltFlipSpike); }

        void Update()
        {
            if (Frozen) return;
            var c = GameConfig.I;
            if (GameState.Melt >= 100f)
            {
                hold += Time.deltaTime;   // post-FX draws the drip while Melt >= 100
                if (hold >= c.meltDeathDrip) PlayerRef.Kill(DeathCause.Melt);
                return;
            }
            Set(Step(GameState.Melt, LayerState.InSource, Time.deltaTime, c.meltRiseSource, c.meltDrainRender));
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
            Set(0f);
        }
    }
}
