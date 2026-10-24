using UnityEngine;

namespace Desync.Audio
{
    /// Self-bootstrapping persistent audio director (CONTRACT: AfterSceneLoad + DontDestroyOnLoad, duplicate-safe).
    /// Split by concern: this file = voices, loops and the core event handlers; AudioManager.Music.cs = adaptive music;
    /// AudioManager.Cues.cs = scoring, run-flow, boss and enemy cues.
    ///
    /// VOICES. At most 16 AudioSources sound at once (VoiceCap). Loops outrank one-shots and are never stolen: the 4 base music
    /// stems, the 3 music layers (tension, TRACE a / b; they only play while audible), the heartbeat, the ICE hum and the Lantern
    /// hum. Every other cue goes through one pool of 5 x 2D + 5 x 3D sources that is trimmed to whatever the loops leave free
    /// (16 - loops), lowest priority and oldest first, so any mix of events stays inside the cap.
    /// WebGL has no AudioMixer snapshots / effects / filters ("Audio in Web": only group volume works), so all mixing is
    /// AudioSource volumes (see the music partial). Audio before the first user gesture is silent on the web (browser autoplay
    /// policy); nothing here depends on it being audible.
    /// ICE hum: one looping 3D voice that follows GameState.NearestIce while GameState.IceAlive > 0 and the player is in SOURCE
    /// (linear rolloff 2-15 m like the other threat cues), faded in/out; so a live hunter is locatable by ear in first person.
    public sealed partial class AudioManager : MonoBehaviour
    {
        public static AudioManager I { get; private set; }

        public const int VoiceCap = 16;

        const int Pool2D = 5, Pool3D = 5;
        const float HumGain = 0.5f, HumIn = 0.25f, HumOut = 0.5f;   // hum level, fade-in / fade-out seconds

        // priorities for voice stealing (higher wins)
        const int PLow = 1, PMid = 4, PHigh = 8, PMax = 10;

        AudioBank _bank;
        AudioSource _hb, _hum, _lantern;
        Pool _pool;

        // sfx state
        bool _hbOn;
        float _hbVol, _humVol;
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
        public AudioSource IceHum => _hum;
        public AudioSource LanternHum => _lantern;
        public int PlayingVoices() => LoopVoices() + _pool.Playing();

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

            InitMusic();

            _hb = NewSource("heartbeat", false);
            _hb.clip = _bank.heartbeat;
            _hb.loop = true;
            _hum = NewLoop("ice.hum", _bank.iceHum);
            _lantern = NewLoop("lantern.hum", _bank.lanternHum);
            _pool = new Pool(transform, Pool2D, Pool3D);

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

        /// A looping 3D voice (linear rolloff 2-15 m like the other threat cues).
        AudioSource NewLoop(string name, AudioClip clip)
        {
            var s = NewSource(name, true);
            s.clip = clip;
            s.loop = true;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 2f;
            s.maxDistance = 15f;
            s.priority = 32;
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
            SubscribeCues();
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
            UnsubscribeCues();
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
            UpdateIceHum(dt);
            UpdateLanternHum(dt);
            UpdateGlitchBeeps();
            UpdateUplinkTicks();
            UpdateDecrypt(dt);
            Trim();   // net: whatever the loops took this frame, the pooled cues fit in the rest
        }

        // ------------------------------------------------------------------ voice budget
        /// Loop voices currently sounding (music stems and layers, heartbeat, ICE hum, Lantern hum): never stolen.
        int LoopVoices()
        {
            int n = (_hb && _hb.isPlaying ? 1 : 0) + (_hum && _hum.isPlaying ? 1 : 0) + (_lantern && _lantern.isPlaying ? 1 : 0);
            for (int i = 0; i < _music.Length; i++) if (_music[i] && _music[i].isPlaying) n++;
            return n;
        }

        /// Pooled one-shots may use whatever the loops leave of the cap.
        void Trim() => _pool.Trim(VoiceCap - LoopVoices());

        /// Starts a loop voice and gives back the pooled voice it displaces when the cap is full.
        void StartLoop(AudioSource s)
        {
            s.Play();
            Trim();
        }

        // ------------------------------------------------------------------ heartbeat / hums / glitch beeps
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
                if (!_hb.isPlaying && _hb.clip) { _hb.pitch = 1f; StartLoop(_hb); }
                _hb.volume = _hbVol * 0.8f;
                _hb.pitch = Mathf.Lerp(1f, 1.75f, k);   // tempo (and pitch) climb with melt
            }
            else if (_hb.isPlaying) _hb.Stop();
        }

        void UpdateIceHum(float dt)
        {
            bool on = GameState.IceAlive > 0 && LayerState.InSource && GameState.Alive && !GameState.Paused && _hum.clip;
            float target = on ? HumGain : 0f;
            _humVol = Mathf.MoveTowards(_humVol, target, dt * HumGain / (target > _humVol ? HumIn : HumOut));
            if (_humVol > 0.001f)
            {
                if (!_hum.isPlaying) { _hum.transform.position = GameState.NearestIce; StartLoop(_hum); }
                _hum.volume = _humVol;
            }
            else if (_hum.isPlaying) _hum.Stop();
            if (_hum.isPlaying && GameState.IceAlive > 0) _hum.transform.position = GameState.NearestIce;
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
        void OnFlipped(bool toSource)
            => Play2D(toSource ? _bank.flipSource : _bank.flipRender, toSource ? 1f : 0.9f, Random.Range(0.97f, 1.03f), PHigh);

        void OnFlipRejected() => Play2D(_bank.flipReject, 0.7f, 1f, PHigh);
        void OnCameraTick(Vector3 p) => Play3D(_bank.cameraTick, 1f, Random.Range(0.96f, 1.04f), PLow, p);
        void OnTraceStarted(int n)
        {
            if (n >= GameConfig.I.tracesToAlarm) return;   // the alarm trace raises Alarm in the same frame: one big alarm, not two
            Play2D(_bank.traceAlarm, 0.6f, 1f + 0.06f * (n - 1), PMax - 1);
        }

        void OnAlarm() => Play2D(_bank.traceAlarm, 0.8f, 0.8f, PMax);
        void OnIceWindup(Vector3 p) => Play3D(_bank.iceWindup, 0.85f, 1f, PMid, p);
        void OnIceSpawned(Vector3 p) => Play3D(_bank.iceShriek, 0.8f, Random.Range(0.94f, 1.06f), PHigh, p);

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
            // These already have their own cue: REJECTED, ACCESS GRANTED, DRONE HIJACKED (hijack sweep), REDLINE (whoosh),
            // AUDIT ESCALATED / UPLINK OPEN (AUDITOR stingers), GHOST xN (the pitched score pop).
            if (text == null || text.StartsWith("REJECTED", System.StringComparison.Ordinal)
                || text.StartsWith("ACCESS", System.StringComparison.Ordinal) || text.StartsWith("DRONE", System.StringComparison.Ordinal)
                || text.StartsWith("REDLINE", System.StringComparison.Ordinal) || text.StartsWith("AUDIT", System.StringComparison.Ordinal)
                || text.StartsWith("UPLINK", System.StringComparison.Ordinal) || text.StartsWith("GHOST", System.StringComparison.Ordinal)) return;
            Play2D(_bank.uiPop, 0.6f, 1f + 0.05f * Mathf.Clamp(GameState.Combo, 0, 8), PMid);
        }

        void OnPaused(bool paused) { _evPaused = paused; }
        void OnGameCompleted() { Play2D(_bank.finaleSting, 0.9f, 1f, PMax); _stingUntil = Time.unscaledTime + 4f; }
        void OnSettingsChanged() { AudioListener.volume = Settings.Volume; }
        void OnStartRequested(int room) => OnUiClick();
        void OnLevelSelect(bool shown) => OnUiClick();
        void OnUiClick() => Play2D(_bank.uiClick, 1f, 1f, PMid);

        void Play2D(AudioClip c, float vol, float pitch, int prio) => _pool.Play(c, vol, pitch, prio, false, Vector3.zero, VoiceCap - LoopVoices());
        void Play3D(AudioClip c, float vol, float pitch, int prio, Vector3 pos) => _pool.Play(c, vol, pitch, prio, true, pos, VoiceCap - LoopVoices());

        // ------------------------------------------------------------------ voice pool (allocation-free after construction)
        /// n2 non-spatial sources (UI / flow cues, still sound while paused) followed by n3 spatial ones (linear rolloff 2-15 m).
        sealed class Pool
        {
            readonly AudioSource[] _s;
            readonly float[] _t;
            readonly int[] _p;
            readonly int _n2;

            public Pool(Transform parent, int n2, int n3)
            {
                _n2 = n2;
                int n = n2 + n3;
                _s = new AudioSource[n];
                _t = new float[n];
                _p = new int[n];
                for (int i = 0; i < n; i++)
                {
                    bool spatial = i >= n2;
                    var go = new GameObject((spatial ? "sfx3d." : "sfx2d.") + (spatial ? i - n2 : i));
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
                    else { s.spatialBlend = 0f; s.ignoreListenerPause = true; }
                    _s[i] = s;
                }
            }

            public int Playing()
            {
                int n = 0;
                for (int i = 0; i < _s.Length; i++) if (_s[i].isPlaying) n++;
                return n;
            }

            int Idle(int lo, int hi)
            {
                for (int i = lo; i < hi; i++) if (!_s[i].isPlaying) return i;
                return -1;
            }

            /// Lowest-priority (then oldest) playing voice in [lo, hi) that is not more important than `prio`.
            int Victim(int lo, int hi, int prio)
            {
                int k = -1;
                for (int i = lo; i < hi; i++)
                    if (_s[i].isPlaying && _p[i] <= prio && (k < 0 || _p[i] < _p[k] || (_p[i] == _p[k] && _t[i] < _t[k]))) k = i;
                return k;
            }

            /// Stops voices until at most `allow` pooled voices are playing.
            public void Trim(int allow)
            {
                if (allow < 0) allow = 0;
                while (Playing() > allow)
                {
                    int v = Victim(0, _s.Length, int.MaxValue);
                    if (v < 0) return;
                    _s[v].Stop();
                }
            }

            /// `allow` = pooled voices the loops leave free right now (VoiceCap - loops). A cue that would pass it first takes the
            /// voice of the least important cue playing (any kind), or is dropped when everything playing outranks it.
            public void Play(AudioClip c, float vol, float pitch, int prio, bool spatial, Vector3 pos, int allow)
            {
                if (!c) return;
                int lo = spatial ? _n2 : 0, hi = spatial ? _s.Length : _n2;
                int k = Idle(lo, hi);
                if (k < 0)
                {
                    // none free in this kind: reuse the lowest-priority (then oldest) voice that is not more important than this cue
                    k = Victim(lo, hi, prio);
                    if (k < 0) return;
                }
                else if (Playing() >= allow)
                {
                    // a free source, but the loops leave no room for one more voice: take the least important cue's voice (any kind)
                    int g = Victim(0, _s.Length, prio);
                    if (g < 0) return;
                    _s[g].Stop();
                }
                var s = _s[k];
                s.Stop();
                s.clip = c;
                s.volume = vol;
                s.pitch = pitch;
                if (spatial) s.transform.position = pos;
                s.Play();
                _t[k] = Time.unscaledTime;
                _p[k] = prio;
            }
        }
    }
}
