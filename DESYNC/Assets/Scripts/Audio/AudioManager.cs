using UnityEngine;

namespace Desync.Audio
{
    /// Self-bootstrapping persistent audio director (CONTRACT: AfterSceneLoad + DontDestroyOnLoad, duplicate-safe).
    ///
    /// MUSIC. Four stems of one 120 BPM loop (RENDER, SOURCE, and a low-passed copy of each) play in lock-step; the mix is only
    /// AudioSource volumes. WebGL has no AudioMixer snapshots / effects / filters ("Audio in Web": only group volume works), so:
    ///   * "snapshot" = linear crossfade RENDER <-> SOURCE over GameConfig.stemCrossfade (the stems are the same music, so linear).
    ///   * melt low-pass = crossfade towards the .lp stems from 50 % melt (full at 100 %).
    ///   * hack duck = GameConfig.hackDuckDb; pause = music lowered, still playing.
    /// SFX. Fixed pools, no allocation per call: 6 x 2D + 5 x 3D (linear rolloff 2-15 m) + heartbeat loop + 4 music = 16 voices max.
    /// Audio before the first user gesture is silent on the web (browser autoplay policy); nothing here depends on it being audible,
    /// the loop simply starts sounding when the AudioContext resumes (all four stems resume together).
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager I { get; private set; }

        // ---- tuning (mix levels are by ear against the -1 dBFS-normalised clips)
        const float MusicGain = 0.55f;
        const float MeltLpStart = 50f;        // % melt where the low-passed stems start to blend in
        const float PausedMusic = 0.35f;
        const float StingMusic = 0.4f;        // music level under the finale sting
        const int Pool2D = 6, Pool3D = 5;
        const int DriftSamples = 1500;        // ~31 ms @ 48 kHz: re-sync a stem that drifted from stem 0
        const float MusicWaitMax = 8f;        // start anyway if a stem has not finished loading by then

        // priorities for voice stealing (higher wins)
        const int PLow = 1, PMid = 4, PHigh = 8, PMax = 10;

        enum Stem { Render, Source, RenderLp, SourceLp }

        AudioBank _bank;
        readonly AudioSource[] _music = new AudioSource[4];
        AudioSource _hb;
        Pool _p2, _p3;

        // music state
        bool _musicOn;
        float _wait, _drift;
        float _layer, _layerTarget;           // 0 = RENDER, 1 = SOURCE (smoothed)
        float _lp, _duck = 1f, _pause = 1f, _sting = 1f;
        float _stingUntil;
        bool _evPaused;

        // sfx state
        bool _hbOn;
        float _hbVol;
        int _lastStep = -1, _beepId = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { I = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (I) return;
            var go = new GameObject("AudioManager");
            DontDestroyOnLoad(go);
            go.AddComponent<AudioManager>();
        }

        // ---- test / debug hooks
        public bool MusicPlaying => _musicOn;
        public AudioSource MusicSource(int stem) => _music[stem];
        public int PlayingVoices()
        {
            int n = _hb && _hb.isPlaying ? 1 : 0;
            for (int i = 0; i < _music.Length; i++) if (_music[i] && _music[i].isPlaying) n++;
            return n + _p2.Playing() + _p3.Playing();
        }

        void Awake()
        {
            if (I && I != this) { Destroy(gameObject); return; }
            I = this;
            _bank = Resources.Load<AudioBank>("AudioBank");
            if (!_bank)
            {
                Debug.LogWarning("[DESYNC] Resources/AudioBank missing - run Desync.Audio.Editor.AudioSetup.Run. Audio stays silent.");
                _bank = ScriptableObject.CreateInstance<AudioBank>();
            }

            var clips = new[] { _bank.musicRender, _bank.musicSource, _bank.musicRenderLp, _bank.musicSourceLp };
            for (int i = 0; i < 4; i++)
            {
                var s = NewSource("music." + (Stem)i, false);
                s.clip = clips[i];
                s.loop = true;
                s.priority = 0;
                s.ignoreListenerPause = true;
                s.volume = 0f;
                _music[i] = s;
                if (clips[i] && clips[i].loadState == AudioDataLoadState.Unloaded) clips[i].LoadAudioData();
            }

            _hb = NewSource("heartbeat", false);
            _hb.clip = _bank.heartbeat;
            _hb.loop = true;
            _p2 = new Pool(transform, Pool2D, false);
            _p3 = new Pool(transform, Pool3D, true);

            _layer = _layerTarget = LayerState.InSource ? 1f : 0f;
            AudioListener.volume = Settings.Volume;
        }

        AudioSource NewSource(string name, bool spatial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = spatial ? 1f : 0f;
            s.dopplerLevel = 0f;
            s.bypassReverbZones = true;
            return s;
        }

        void OnEnable()
        {
            LayerState.Changed += OnLayerChanged;
            GameEvents.Flipped += OnFlipped;
            GameEvents.FlipRejected += OnFlipRejected;
            GameEvents.CameraTick += OnCameraTick;
            GameEvents.TraceStarted += OnTraceStarted;
            GameEvents.Alarm += OnAlarm;
            GameEvents.IceWindup += OnIceWindup;
            GameEvents.IceSpawned += OnIceSpawned;
            GameEvents.PlayerKilled += OnPlayerKilled;
            GameEvents.MeltWarning += OnMeltWarning;
            GameEvents.Footstep += OnFootstep;
            GameEvents.HackStarted += OnHackStarted;
            GameEvents.HackTick += OnHackTick;
            GameEvents.HackFailed += OnHackFailed;
            GameEvents.HackCancelled += OnHackCancelled;
            GameEvents.AccessGranted += OnAccessGranted;
            GameEvents.Hijacked += OnHijacked;
            GameEvents.GlitchZone += OnGlitchZone;
            GameEvents.ExitOpened += OnExitOpened;
            GameEvents.RoomCleared += OnRoomCleared;
            GameEvents.Rebooting += OnRebooting;
            GameEvents.RoomReset += OnRoomReset;
            GameEvents.PopText += OnPopText;
            GameEvents.Paused += OnPaused;
            GameEvents.GameCompleted += OnGameCompleted;
            GameEvents.SettingsChanged += OnSettingsChanged;
            GameEvents.StartRequested += OnStartRequested;
            GameEvents.ResumeRequested += OnUiClick;
            GameEvents.RestartRequested += OnUiClick;
            GameEvents.QuitRequested += OnUiClick;
            GameEvents.LevelSelectToggled += OnLevelSelect;
        }

        void OnDisable()
        {
            LayerState.Changed -= OnLayerChanged;
            GameEvents.Flipped -= OnFlipped;
            GameEvents.FlipRejected -= OnFlipRejected;
            GameEvents.CameraTick -= OnCameraTick;
            GameEvents.TraceStarted -= OnTraceStarted;
            GameEvents.Alarm -= OnAlarm;
            GameEvents.IceWindup -= OnIceWindup;
            GameEvents.IceSpawned -= OnIceSpawned;
            GameEvents.PlayerKilled -= OnPlayerKilled;
            GameEvents.MeltWarning -= OnMeltWarning;
            GameEvents.Footstep -= OnFootstep;
            GameEvents.HackStarted -= OnHackStarted;
            GameEvents.HackTick -= OnHackTick;
            GameEvents.HackFailed -= OnHackFailed;
            GameEvents.HackCancelled -= OnHackCancelled;
            GameEvents.AccessGranted -= OnAccessGranted;
            GameEvents.Hijacked -= OnHijacked;
            GameEvents.GlitchZone -= OnGlitchZone;
            GameEvents.ExitOpened -= OnExitOpened;
            GameEvents.RoomCleared -= OnRoomCleared;
            GameEvents.Rebooting -= OnRebooting;
            GameEvents.RoomReset -= OnRoomReset;
            GameEvents.PopText -= OnPopText;
            GameEvents.Paused -= OnPaused;
            GameEvents.GameCompleted -= OnGameCompleted;
            GameEvents.SettingsChanged -= OnSettingsChanged;
            GameEvents.StartRequested -= OnStartRequested;
            GameEvents.ResumeRequested -= OnUiClick;
            GameEvents.RestartRequested -= OnUiClick;
            GameEvents.QuitRequested -= OnUiClick;
            GameEvents.LevelSelectToggled -= OnLevelSelect;
        }

        void OnDestroy() { if (I == this) I = null; }

        // ------------------------------------------------------------------ per-frame
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (!_musicOn) TryStartMusic(dt);
            else
            {
                MixMusic(dt);
                _drift += dt;
                if (_drift >= 1f) { _drift = 0f; CheckDrift(); }
            }
            UpdateHeartbeat(dt);
            UpdateGlitchBeeps();
        }

        void TryStartMusic(float dt)
        {
            _wait += dt;
            bool any = false, ready = true;
            for (int i = 0; i < 4; i++)
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
            for (int i = 0; i < 4; i++)
                if (_music[i].clip && _music[i].clip.loadState == AudioDataLoadState.Loaded) { _music[i].timeSamples = 0; _music[i].Play(); }
            _musicOn = true;
        }

        static float Smooth(float x) => x * x * (3f - 2f * x);

        void MixMusic(float dt)
        {
            var cfg = GameConfig.I;
            _layer = Mathf.MoveTowards(_layer, _layerTarget, dt / Mathf.Max(0.01f, cfg.stemCrossfade));
            _lp = Mathf.MoveTowards(_lp, Smooth(Mathf.InverseLerp(MeltLpStart, 100f, GameState.Melt)), dt * 1.5f);
            _duck = Mathf.MoveTowards(_duck, GameState.InHack ? Mathf.Pow(10f, cfg.hackDuckDb / 20f) : 1f, dt * 6f);
            _pause = Mathf.MoveTowards(_pause, GameState.Paused || _evPaused ? PausedMusic : 1f, dt * 4f);
            _sting = Mathf.MoveTowards(_sting, Time.unscaledTime < _stingUntil ? StingMusic : 1f, dt * 2f);
            ApplyMusicVolumes();
        }

        void ApplyMusicVolumes()
        {
            float g = MusicGain * _duck * _pause * _sting;
            float r = 1f - _layer, s = _layer, d = 1f - _lp;
            _music[(int)Stem.Render].volume = g * r * d;
            _music[(int)Stem.Source].volume = g * s * d;
            _music[(int)Stem.RenderLp].volume = g * r * _lp;
            _music[(int)Stem.SourceLp].volume = g * s * _lp;
        }

        void CheckDrift()
        {
            var m0 = _music[0];
            if (!m0.clip || !m0.isPlaying) return;
            int len = m0.clip.samples, t0 = m0.timeSamples;
            for (int i = 1; i < 4; i++)
            {
                var s = _music[i];
                if (!s.clip || !s.isPlaying) continue;
                int d = s.timeSamples - t0;
                if (d > len / 2) d -= len; else if (d < -len / 2) d += len;
                if (d > DriftSamples || d < -DriftSamples) s.timeSamples = t0;
            }
        }

        // ------------------------------------------------------------------ heartbeat / glitch beeps
        void UpdateHeartbeat(float dt)
        {
            var cfg = GameConfig.I;
            // MeltMeter raises MeltWarning(false) on leaving the warning zone / reset; this is the net if an event is missed.
            if (_hbOn && (!GameState.Alive || GameState.Melt < cfg.meltWarn - 3f)) _hbOn = false;
            float k = Mathf.InverseLerp(cfg.meltWarn, 100f, GameState.Melt);
            float target = _hbOn && !GameState.Paused ? Mathf.Lerp(0.55f, 1f, k) : 0f;
            _hbVol = Mathf.MoveTowards(_hbVol, target, dt * (target > _hbVol ? 4f : 3f));
            if (_hbVol > 0.001f)
            {
                if (!_hb.isPlaying && _hb.clip) { _hb.pitch = 1f; _hb.Play(); }
                _hb.volume = _hbVol * 0.8f;
                _hb.pitch = Mathf.Lerp(1f, 1.75f, k);   // tempo (and pitch) climb with melt
            }
            else if (_hb.isPlaying) _hb.Stop();
        }

        void UpdateGlitchBeeps()
        {
            float c = GameState.GlitchCountdown;
            if (c < 0f) { _beepId = -1; return; }
            int id = c > 1f ? Mathf.CeilToInt(c) : 100 + Mathf.CeilToInt(c * 4f);   // 1 beep/s, then 4/s for the last second
            if (id == _beepId) return;
            _beepId = id;
            float total = Mathf.Max(0.1f, GameConfig.I.glitchZoneTime);
            Play2D(_bank.glitchBeep, 0.5f, 0.9f + 0.9f * (1f - Mathf.Clamp01(c / total)), PMid);
        }

        // ------------------------------------------------------------------ events
        void OnLayerChanged(bool source) { _layerTarget = source ? 1f : 0f; }

        void OnFlipped(bool toSource)
            => Play2D(toSource ? _bank.flipSource : _bank.flipRender, toSource ? 1f : 0.9f, Random.Range(0.97f, 1.03f), PHigh);

        void OnFlipRejected() => Play2D(_bank.flipReject, 0.7f, 1f, PHigh);
        void OnCameraTick(Vector3 p) => _p3.Play(_bank.cameraTick, 1f, Random.Range(0.96f, 1.04f), PLow, p);
        void OnTraceStarted(int n)
        {
            if (n >= GameConfig.I.tracesToAlarm) return;   // the alarm trace raises Alarm in the same frame: one big alarm, not two
            Play2D(_bank.traceAlarm, 0.6f, 1f + 0.06f * (n - 1), PMax - 1);
        }

        void OnAlarm() => Play2D(_bank.traceAlarm, 0.8f, 0.8f, PMax);
        void OnIceWindup(Vector3 p) => _p3.Play(_bank.iceWindup, 0.85f, 1f, PMid, p);
        void OnIceSpawned(Vector3 p) => _p3.Play(_bank.iceShriek, 0.8f, Random.Range(0.94f, 1.06f), PHigh, p);

        void OnPlayerKilled(DeathCause cause)
        {
            _hbOn = false;
            if (cause == DeathCause.Melt) Play2D(_bank.flatline, 0.55f, 1f, PMax);
            else Play2D(_bank.deathCrunch, 0.8f, 1f, PMax);
        }

        void OnMeltWarning(bool on) { _hbOn = on && GameState.Alive; }

        void OnFootstep()
        {
            var a = LayerState.InSource ? _bank.footstepSource : _bank.footstepRender;
            if (a == null || a.Length == 0) return;
            int i = Random.Range(0, a.Length);
            if (i == _lastStep && a.Length > 1) i = (i + 1) % a.Length;
            _lastStep = i;
            Play2D(a[i], 0.8f, Random.Range(0.94f, 1.06f), PLow);
        }

        void OnHackStarted() => Play2D(_bank.hackStart, 0.6f, 1f, PMid);
        void OnHackTick(int step) => Play2D(_bank.hackTick, 0.6f, Mathf.Pow(2f, Mathf.Clamp(step, 0, 24) / 12f), PHigh);   // +1 semitone per step
        void OnHackFailed() => Play2D(_bank.hackWrong, 0.55f, 1f, PHigh);
        void OnHackCancelled() => Play2D(_bank.hackCancel, 0.6f, 1f, PMid);
        void OnAccessGranted(string line) => Play2D(_bank.accessGranted, 0.8f, 1f, PHigh);
        void OnHijacked(bool on) => Play2D(on ? _bank.hijackOn : _bank.hijackOff, 0.45f, 1f, PMid);
        void OnGlitchZone(bool inside) { _beepId = -1; }
        void OnExitOpened() => Play2D(_bank.portalOpen, 0.7f, 1f, PHigh);

        void OnRoomCleared(RoomResult r)
        {
            float pitch = r.grade == 'S' ? 1.12f : r.grade == 'A' ? 1.06f : r.grade == 'B' ? 1f : 0.9f;
            Play2D(_bank.roomClear, 0.8f, pitch, PHigh);
        }

        void OnRebooting() { _hbOn = false; Play2D(_bank.reboot, 0.7f, 1f, PMax - 1); }
        void OnRoomReset() { _hbOn = false; }

        void OnPopText(string text, Color c)
        {
            // REJECTED and ACCESS GRANTED already have their own cues; DRONE HIJACKED has the hijack sweep.
            if (text == null || text.StartsWith("REJECTED", System.StringComparison.Ordinal)
                || text.StartsWith("ACCESS", System.StringComparison.Ordinal) || text.StartsWith("DRONE", System.StringComparison.Ordinal)) return;
            Play2D(_bank.uiPop, 0.6f, 1f + 0.05f * Mathf.Clamp(GameState.Combo, 0, 8), PMid);   // GHOST xN climbs
        }

        void OnPaused(bool paused) { _evPaused = paused; }
        void OnGameCompleted() { Play2D(_bank.finaleSting, 0.9f, 1f, PMax); _stingUntil = Time.unscaledTime + 4f; }
        void OnSettingsChanged() { AudioListener.volume = Settings.Volume; }
        void OnStartRequested(int room) => OnUiClick();
        void OnLevelSelect(bool shown) => OnUiClick();
        void OnUiClick() => Play2D(_bank.uiClick, 1f, 1f, PMid);

        void Play2D(AudioClip c, float vol, float pitch, int prio) => _p2.Play(c, vol, pitch, prio, Vector3.zero);

        // ------------------------------------------------------------------ voice pool (allocation-free after construction)
        sealed class Pool
        {
            readonly AudioSource[] _s;
            readonly float[] _t;
            readonly int[] _p;
            readonly bool _spatial;

            public Pool(Transform parent, int n, bool spatial)
            {
                _spatial = spatial;
                _s = new AudioSource[n];
                _t = new float[n];
                _p = new int[n];
                for (int i = 0; i < n; i++)
                {
                    var go = new GameObject((spatial ? "sfx3d." : "sfx2d.") + i);
                    go.transform.SetParent(parent, false);
                    var s = go.AddComponent<AudioSource>();
                    s.playOnAwake = false;
                    s.dopplerLevel = 0f;
                    s.bypassReverbZones = true;
                    s.priority = 128;
                    if (spatial)
                    {
                        s.spatialBlend = 1f;
                        s.rolloffMode = AudioRolloffMode.Linear;
                        s.minDistance = 2f;
                        s.maxDistance = 15f;
                    }
                    else { s.spatialBlend = 0f; s.ignoreListenerPause = true; }   // UI / flow cues still sound while paused
                    _s[i] = s;
                }
            }

            public int Playing()
            {
                int n = 0;
                for (int i = 0; i < _s.Length; i++) if (_s[i].isPlaying) n++;
                return n;
            }

            public void Play(AudioClip c, float vol, float pitch, int prio, Vector3 pos)
            {
                if (!c) return;
                int k = -1;
                for (int i = 0; i < _s.Length; i++) if (!_s[i].isPlaying) { k = i; break; }
                if (k < 0)
                {
                    // all busy: steal the lowest-priority (then oldest) voice that is not more important than this cue
                    for (int i = 0; i < _s.Length; i++)
                        if (_p[i] <= prio && (k < 0 || _p[i] < _p[k] || (_p[i] == _p[k] && _t[i] < _t[k]))) k = i;
                    if (k < 0) return;
                }
                var s = _s[k];
                s.Stop();
                s.clip = c;
                s.volume = vol;
                s.pitch = pitch;
                if (_spatial) s.transform.position = pos;
                s.Play();
                _t[k] = Time.unscaledTime;
                _p[k] = prio;
            }
        }
    }
}
