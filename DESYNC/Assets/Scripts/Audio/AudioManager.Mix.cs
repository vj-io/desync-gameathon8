using Desync.Flow;
using UnityEngine;

namespace Desync.Audio
{
    /// The pure formulas of the mix pass, apart from the engine so they can be checked without an audio device. Levels and the reasoning
    /// behind them are written down in docs/AUDIO.md.
    public static class MixMath
    {
        /// Every clip in the bank is normalised to -1 dBFS (the music comment in AudioManager.Music.cs; AudioSetup does the same for SFX).
        public const float ClipPeak = 0.89f;
        /// Estimated output peak above which the limiter pulls the master down (-0.26 dBFS, just under digital full scale).
        public const float Ceiling = 0.97f;
        /// Limiter timing: it clamps within ~30 ms and lets go over ~0.5 s (gain units per second).
        public const float AttackPerSecond = 1f / 0.03f, ReleasePerSecond = 1f / 0.5f;
        /// Music level while the death card is up, and for the second after a stinger.
        public const float RecapDuck = 0.35f, StingerDuck = 0.55f, StingerSeconds = 1.1f;
        /// Duck timing (gain units per second): quick down, slow back up.
        public const float DuckDownPerSecond = 12f, DuckUpPerSecond = 1.2f;

        /// Estimated output peak of the voices sounding now, from the sum of their squared volumes: separate sounds add in power, not in
        /// level (one voice at volume v peaks at v x ClipPeak; two equal voices at 1.41x that; sixteen at 4x).
        public static float PeakEstimate(float sumOfSquares) => Mathf.Sqrt(Mathf.Max(0f, sumOfSquares)) * ClipPeak;

        /// 1 while the estimate is under the ceiling, else the gain that brings it back to the ceiling.
        public static float LimiterGain(float estimate) => estimate <= Ceiling ? 1f : Ceiling / estimate;

        /// One step of the limiter's gain towards `target` (attack when it falls, release when it recovers).
        public static float Step(float current, float target, float dt) =>
            Mathf.MoveTowards(current, target, dt * (target < current ? AttackPerSecond : ReleasePerSecond));

        /// Music level the duck aims for: under the death card, under a stinger, or untouched.
        public static float DuckTarget(bool recap, bool stinger) => Mathf.Min(recap ? RecapDuck : 1f, stinger ? StingerDuck : 1f);

        public static float StepDuck(float current, float target, float dt) =>
            Mathf.MoveTowards(current, target, dt * (target < current ? DuckDownPerSecond : DuckUpPerSecond));
    }

    /// MIX PASS. Four buses (master = Settings.Volume on the AudioListener; music, effects and interface = A11y.*Volume), music ducking
    /// under the death card and stingers, and a voice-sum limiter. WebGL has no AudioMixer and no audio filters (CONTRACT), so all of it is
    /// AudioSource volumes and one listener gain:
    ///   * Pooled cues (Play2D / Play3D) take their bus when they start, through MixCue.
    ///   * Music stems, layers, heartbeat and the two hums have their volume rewritten by Update every frame; LateUpdate multiplies the
    ///     fresh value by the bus (and the music duck), and leaves a value alone that nothing rewrote, so nothing compounds.
    ///   * The limiter estimates the output peak from the volumes of the voices that are playing (their powers add) and lowers
    ///     AudioListener.volume just enough to stay under MixMath.Ceiling.
    public sealed partial class AudioManager
    {
        float _mixDuck = 1f, _limiter = 1f, _stingerUntil = -1f, _master = -1f;
        AudioSource[] _mixSources, _scaled;
        float[] _scaledTo;

        // ---- test / debug hooks
        public float MusicDuck => _mixDuck;
        public float LimiterGain => _limiter;

        /// A cue about to start through the pool: returns its volume on its bus, and starts the music duck when it is a stinger
        /// (priority PMax - 1 and up: alarms, death, reboot, REDLINE, boss phases, finale).
        float MixCue(AudioClip c, int prio, float vol)
        {
            if (prio >= PMax - 1) _stingerUntil = Time.unscaledTime + MixMath.StingerSeconds;
            return vol * (IsUiCue(c) ? A11y.UiVolume : A11y.SfxVolume);
        }

        /// Menu, map, draft, market and score-pop sounds ride the interface bus; everything in the world rides effects.
        bool IsUiCue(AudioClip c) =>
            c && (c == _bank.uiClick || c == _bank.uiPop || c == _bank.scorePop || c == _bank.mapOpen || c == _bank.mapChoose
                  || c == _bank.decryptTick || c == _bank.draftPick || c == _bank.marketBuy || c == _bank.cooldown);

        /// Settings.Volume lives in PlayerPrefs (a lookup that allocates in the editor): read it when the settings change, not every frame.
        void MixSettingsChanged() { _master = Settings.Volume; AudioListener.volume = _master * _limiter; }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (_mixSources == null)
            {
                _mixSources = GetComponentsInChildren<AudioSource>(true);
                _scaled = new AudioSource[MusicVoices + 3];
                for (int i = 0; i < MusicVoices; i++) _scaled[i] = _music[i];
                _scaled[MusicVoices] = _hb; _scaled[MusicVoices + 1] = _hum; _scaled[MusicVoices + 2] = _lantern;
                _scaledTo = new float[_scaled.Length];
                for (int i = 0; i < _scaledTo.Length; i++) _scaledTo[i] = -1f;
            }

            _mixDuck = MixMath.StepDuck(_mixDuck, MixMath.DuckTarget(GameState.Phase == GameState.PhaseKind.Recap, Time.unscaledTime < _stingerUntil), dt);
            float music = A11y.MusicVolume * _mixDuck, sfx = A11y.SfxVolume;
            for (int i = 0; i < _scaled.Length; i++)
            {
                var s = _scaled[i];
                if (!s || (i < MusicVoices && !_musicOn)) continue;
                float v = s.volume;
                if (v == _scaledTo[i]) continue;   // not rewritten since we scaled it
                float f = i < MusicVoices ? music : sfx;
                if (f == 1f) { _scaledTo[i] = -1f; continue; }   // nothing to scale; remember nothing, so a later bus change still sees fresh values
                s.volume = _scaledTo[i] = v * f;
            }

            float power = 0f;
            for (int i = 0; i < _mixSources.Length; i++)
            {
                var s = _mixSources[i];
                if (!s || !s.isPlaying) continue;
                float v = s.volume;
                power += v * v;
            }
            _limiter = MixMath.Step(_limiter, MixMath.LimiterGain(MixMath.PeakEstimate(power)), dt);
            if (_master < 0f) _master = Settings.Volume;
            AudioListener.volume = _master * _limiter;
        }
    }
}
