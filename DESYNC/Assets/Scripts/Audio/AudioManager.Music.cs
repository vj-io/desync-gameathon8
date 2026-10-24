using Desync.Flow;
using UnityEngine;

namespace Desync.Audio
{
    /// ADAPTIVE MUSIC. Seven music voices, all looping, all 16 bars (32 s) of the same 120 BPM grid, so they stay
    /// sample-locked:
    ///   * 4 base stems: RENDER, SOURCE and a low-passed copy of each. The mix is only AudioSource volumes:
    ///       - "snapshot" = linear crossfade RENDER <-> SOURCE over GameConfig.stemCrossfade (same music, so linear),
    ///       - melt low-pass = crossfade towards the .lp stems from 50 % melt (full at 100 %),
    ///       - hack duck = GameConfig.hackDuckDb; pause = music lowered, still playing.
    ///   * 3 layers on top of the stems (they only play while audible, started on the stems' position):
    ///       - tension: fades in with melt, clamp((melt - 50) / 40, 0, 1),
    ///       - TRACE a (level 1+) and TRACE b (level 2+): scanner pulse, then klaxon, per GameState.TraceCount while a TRACE runs.
    ///   * the act: the 4 base stems belong to one act's set (act 1, 2 or 3 = RunState.Act while a run is on screen). An act change
    ///     never stacks a second set: the new set loads in the background while the old one plays, then the music dips to silence
    ///     (ActFade), the four stems swap clips and restart together, and it comes back up. Acts 2 and 3 are loaded on demand and
    ///     the set that was left is unloaded, so at most one set (plus the layers) sits in memory outside the swap.
    public sealed partial class AudioManager
    {
        // ---- tuning (mix levels are by ear against the -1 dBFS-normalised clips)
        const float MusicGain = 0.55f;
        const float MeltLpStart = 50f;        // % melt where the low-passed stems start to blend in
        const float PausedMusic = 0.35f;
        const float StingMusic = 0.4f;        // music level under the finale sting
        const int DriftSamples = 1500;        // ~31 ms @ 48 kHz: re-sync a stem that drifted from stem 0
        const float MusicWaitMax = 8f;        // start anyway if a stem has not finished loading by then
        const float TensionGain = 0.8f, TraceAGain = 0.7f, TraceBGain = 0.65f;   // layer level against the stems
        const float ActFade = 0.6f;           // seconds to dip out / back in on an act change

        public const int StemCount = 4, TensionStem = 4, TraceAStem = 5, TraceBStem = 6, MusicVoices = 7;

        enum Stem { Render, Source, RenderLp, SourceLp, Tension, TraceA, TraceB }
        enum Swap { None, Loading, Out, In }

        readonly AudioSource[] _music = new AudioSource[MusicVoices];

        // music state
        bool _musicOn;
        float _wait, _drift;
        float _layer, _layerTarget;           // 0 = RENDER, 1 = SOURCE (smoothed)
        float _lp, _duck = 1f, _pause = 1f, _sting = 1f;
        float _stingUntil;
        bool _evPaused;
        float _tension, _traceA, _traceB;     // layer levels 0..1 (smoothed)
        int _act, _swapTo;
        float _actGain = 1f, _swapWait;       // _actGain dips to 0 while the stems swap
        Swap _swap;

        // ---- test / debug hooks
        public bool MusicPlaying => _musicOn;
        public AudioSource MusicSource(int stem) => _music[stem];
        public int Act => _act;
        public bool ActSwapping => _swap != Swap.None;

        void InitMusic()
        {
            for (int i = 0; i < MusicVoices; i++)
            {
                var s = NewSource("music." + (Stem)i, false);
                var c = i < StemCount ? _bank.Stem(0, i) : i == TensionStem ? _bank.musicTension : i == TraceAStem ? _bank.musicTraceA : _bank.musicTraceB;
                s.clip = c;
                s.loop = true;
                s.priority = 0;
                s.ignoreListenerPause = true;
                s.volume = 0f;
                _music[i] = s;
                if (c && c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
            }
            _layer = _layerTarget = LayerState.InSource ? 1f : 0f;
        }

        void OnLayerChanged(bool source) { _layerTarget = source ? 1f : 0f; }

        void TryStartMusic(float dt)
        {
            _wait += dt;
            bool any = false, ready = true;
            for (int i = 0; i < StemCount; i++)
            {
                var c = _music[i].clip;
                if (!c) continue;
                any = true;
                if (c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
                if (c.loadState == AudioDataLoadState.Loading || c.loadState == AudioDataLoadState.Unloaded) ready = false;
            }
            if (!any) { _musicOn = true; return; }
            if (!ready && _wait < MusicWaitMax) return;   // start all four in the same frame so they stay sample-locked
            _layer = _layerTarget;
            _lp = Smooth(Mathf.InverseLerp(MeltLpStart, 100f, GameState.Melt));
            ApplyMusicVolumes();
            for (int i = 0; i < StemCount; i++)
                if (_music[i].clip && _music[i].clip.loadState == AudioDataLoadState.Loaded) { _music[i].timeSamples = 0; _music[i].Play(); }
            _musicOn = true;
            Trim();
        }

        static float Smooth(float x) => x * x * (3f - 2f * x);

        void MixMusic(float dt)
        {
            var cfg = GameConfig.I;
            UpdateAct(dt);
            _layer = Mathf.MoveTowards(_layer, _layerTarget, dt / Mathf.Max(0.01f, cfg.stemCrossfade));
            _lp = Mathf.MoveTowards(_lp, Smooth(Mathf.InverseLerp(MeltLpStart, 100f, GameState.Melt)), dt * 1.5f);
            _duck = Mathf.MoveTowards(_duck, GameState.InHack ? Mathf.Pow(10f, cfg.hackDuckDb / 20f) : 1f, dt * 6f);
            _pause = Mathf.MoveTowards(_pause, GameState.Paused || _evPaused ? PausedMusic : 1f, dt * 4f);
            _sting = Mathf.MoveTowards(_sting, Time.unscaledTime < _stingUntil ? StingMusic : 1f, dt * 2f);

            float tension = AudioMath.Tension(GameState.Melt);
            int level = GameState.TraceActive ? Mathf.Clamp(GameState.TraceCount, 0, 3) : 0;
            float a = AudioMath.TraceA(level), b = AudioMath.TraceB(level);
            _tension = Mathf.MoveTowards(_tension, tension, dt * (tension > _tension ? 1f : 0.7f));         // 1 s up, 1.4 s down
            _traceA = Mathf.MoveTowards(_traceA, a, dt * (a > _traceA ? 6.5f : 1.25f));                     // 0.15 s in, 0.8 s out
            _traceB = Mathf.MoveTowards(_traceB, b, dt * (b > _traceB ? 6.5f : 1.25f));
            ApplyMusicVolumes();
        }

        void ApplyMusicVolumes()
        {
            float g = MusicGain * _duck * _pause * _sting * _actGain;
            float r = 1f - _layer, s = _layer, d = 1f - _lp;
            _music[(int)Stem.Render].volume = g * r * d;
            _music[(int)Stem.Source].volume = g * s * d;
            _music[(int)Stem.RenderLp].volume = g * r * _lp;
            _music[(int)Stem.SourceLp].volume = g * s * _lp;
            SetLayer(TensionStem, g * TensionGain * _tension);
            SetLayer(TraceAStem, g * TraceAGain * _traceA);
            SetLayer(TraceBStem, g * TraceBGain * _traceB);
        }

        /// A layer sounds only while audible: it starts on the stems' position (the drift check keeps it there) and stops at silence,
        /// so an idle layer costs no voice.
        void SetLayer(int i, float vol)
        {
            var s = _music[i];
            s.volume = vol;
            if (vol > 0.002f)
            {
                var c = s.clip;
                if (!s.isPlaying && c && c.loadState == AudioDataLoadState.Loaded && _music[0].isPlaying)
                {
                    s.timeSamples = Mathf.Min(_music[0].timeSamples, c.samples - 1);
                    StartLoop(s);
                }
            }
            else if (s.isPlaying) s.Stop();
        }

        void CheckDrift()
        {
            var m0 = _music[0];
            if (!m0.clip || !m0.isPlaying) return;
            int len = m0.clip.samples, t0 = m0.timeSamples;
            for (int i = 1; i < MusicVoices; i++)
            {
                var s = _music[i];
                if (!s.clip || !s.isPlaying || s.clip.samples != len) continue;
                int d = s.timeSamples - t0;
                if (d > len / 2) d -= len; else if (d < -len / 2) d += len;
                if (d > DriftSamples || d < -DriftSamples) s.timeSamples = t0;
            }
        }

        // ------------------------------------------------------------------ act sets
        static int ActOf() => AudioMath.ActIndex(RunState.Phase != RunPhase.None, RunState.Act);

        void UpdateAct(float dt)
        {
            int want = ActOf();
            switch (_swap)
            {
                case Swap.None:
                    if (want == _act) return;
                    _swapTo = want;
                    _swapWait = 0f;
                    Prefetch(want);
                    _swap = Swap.Loading;
                    break;
                case Swap.Loading:
                    _swapWait += dt;
                    if (want != _swapTo)
                    {
                        Unload(_swapTo);                       // the run moved on before the swap began: follow it
                        if (want == _act) { _swap = Swap.None; return; }
                        _swapTo = want;
                        _swapWait = 0f;
                        Prefetch(want);
                    }
                    else if (ActLoaded(_swapTo) || _swapWait >= MusicWaitMax) _swap = Swap.Out;
                    break;
                case Swap.Out:
                    _actGain = Mathf.MoveTowards(_actGain, 0f, dt / ActFade);
                    if (_actGain <= 0f) { SwapStems(_swapTo); _swap = Swap.In; }
                    break;
                case Swap.In:
                    _actGain = Mathf.MoveTowards(_actGain, 1f, dt / ActFade);
                    if (_actGain >= 1f) _swap = Swap.None;
                    break;
            }
        }

        /// Silent moment of an act change: every music voice stops, the four stems take the new set's clips and TryStartMusic
        /// restarts them together at sample 0 (the layers come back on demand, in step with them).
        void SwapStems(int act)
        {
            int old = _act;
            for (int i = 0; i < MusicVoices; i++) if (_music[i].isPlaying) _music[i].Stop();
            for (int i = 0; i < StemCount; i++) _music[i].clip = _bank.Stem(act, i);
            _act = act;
            _musicOn = false;
            _wait = 0f;
            Unload(old);
        }

        void Prefetch(int act)
        {
            for (int i = 0; i < StemCount; i++)
            {
                var c = _bank.Stem(act, i);
                if (c && c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
            }
        }

        bool ActLoaded(int act)
        {
            for (int i = 0; i < StemCount; i++)
            {
                var c = _bank.Stem(act, i);
                if (c && (c.loadState == AudioDataLoadState.Loading || c.loadState == AudioDataLoadState.Unloaded)) return false;
            }
            return true;
        }

        void Unload(int act)
        {
            if (act == _act) return;
            for (int i = 0; i < StemCount; i++)
            {
                var c = _bank.Stem(act, i);
                if (c && c.loadState != AudioDataLoadState.Unloaded) c.UnloadAudioData();
            }
        }
    }
}
