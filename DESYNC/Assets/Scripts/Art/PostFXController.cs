using UnityEngine;
using UnityEngine.SceneManagement;

namespace Desync.Art
{
    /// Persistent layer-visuals driver (self-bootstraps, survives scene loads). Every frame it feeds the fullscreen post-FX
    /// globals from GameState; on LayerState.Changed it switches fog colour, camera background and the blend state of the
    /// shared world materials; it attaches GlyphRain to whichever camera PlayerRef.Cam currently is. Zero GC per frame.
    public class PostFXController : MonoBehaviour
    {
        static PostFXController _i;

        static readonly int PulseId = Shader.PropertyToID("_Pulse"), MeltId = Shader.PropertyToID("_Melt"),
            TraceId = Shader.PropertyToID("_Trace"), RebootId = Shader.PropertyToID("_Reboot"), WipeId = Shader.PropertyToID("_Wipe"),
            DripId = Shader.PropertyToID("_Drip"), FlashId = Shader.PropertyToID("_FlashScale"), FlipWaveId = Shader.PropertyToID("_FlipWave"),
            SrcBlendId = Shader.PropertyToID("_SrcBlend"), DstBlendId = Shader.PropertyToID("_DstBlend"),
            ZWriteId = Shader.PropertyToID("_ZWrite"), CullId = Shader.PropertyToID("_Cull");

        /// Shared materials that turn additive / see-through in SOURCE.
        public static readonly string[] SourceTransparentKeys = { "mat.world", "mat.floor", "mat.poster", "mat.screen" };
        public static readonly Color SourceFog = new Color(0.0f, 0.02f, 0.012f);

        const float WipeTime = 0.3f;

        Material[] _mats;
        Camera _rainCam;
        bool _calm;
        float _reboot, _rebootHold, _wipe = -1f, _drip, _wave = 99f;
        bool _rebootClosing;
        Vector3 _waveOrigin;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_i) return;
            var go = new GameObject("[PostFX]");
            DontDestroyOnLoad(go);
            go.AddComponent<PostFXController>();
        }

        void Awake()
        {
            if (_i && _i != this) { Destroy(gameObject); return; }
            _i = this;
            _mats = new Material[SourceTransparentKeys.Length];
            for (int k = 0; k < _mats.Length; k++) _mats[k] = AssetLib.Get<Material>(SourceTransparentKeys[k]);
        }

        void OnEnable()
        {
            if (_i != this) return;
            LayerState.Changed += OnLayer;
            GameEvents.Flipped += OnFlipped;
            GameEvents.Rebooting += OnRebooting;
            GameEvents.RoomReset += OnRoomReset;
            GameEvents.SettingsChanged += OnSettings;
            SceneManager.sceneLoaded += OnScene;
            OnSettings();
            OnLayer(LayerState.InSource);
        }

        void OnDisable()
        {
            if (_i != this) return;
            LayerState.Changed -= OnLayer;
            GameEvents.Flipped -= OnFlipped;
            GameEvents.Rebooting -= OnRebooting;
            GameEvents.RoomReset -= OnRoomReset;
            GameEvents.SettingsChanged -= OnSettings;
            SceneManager.sceneLoaded -= OnScene;
            // Material assets edited in play mode stay edited in the editor: always leave them in RENDER state.
            SetWorldBlend(_mats, false);
        }

        void OnDestroy() { if (_i == this) _i = null; }

        void OnSettings() => _calm = Settings.ReduceFlashes;
        void OnScene(Scene s, LoadSceneMode m) => OnLayer(LayerState.InSource);
        void OnRoomReset() => _rebootHold = 0f;

        void OnRebooting()
        {
            _rebootClosing = true;
            _rebootHold = Mathf.Max(GameConfig.I.restartWipe, 0.02f);   // stay closed until RoomReset, at most one more wipe length
        }

        void OnFlipped(bool toSource)
        {
            _wipe = 0f;
            _wave = 0f;
            _waveOrigin = PlayerRef.Exists ? PlayerRef.Position : Vector3.zero;
        }

        void OnLayer(bool source)
        {
            var cfg = GameConfig.I;
            RenderSettings.fogColor = source ? SourceFog : cfg.fogColor;
            var cam = PlayerRef.Cam ? PlayerRef.Cam : Camera.main;
            if (cam)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = RenderSettings.fogColor;
            }
            SetWorldBlend(_mats, source);
        }

        /// SOURCE: additive, no depth write, both faces, transparent queue. RENDER: opaque. Public for ArtVerify previews.
        public static void SetWorldBlend(Material[] mats, bool source)
        {
            if (mats == null) return;
            foreach (var m in mats)
            {
                if (!m) continue;
                m.SetFloat(SrcBlendId, 1f);
                m.SetFloat(DstBlendId, source ? 1f : 0f);
                m.SetFloat(ZWriteId, source ? 0f : 1f);
                m.SetFloat(CullId, source ? 0f : 2f);
                m.renderQueue = source ? 3000 : 2000;
            }
        }

        void Update()
        {
            var cfg = GameConfig.I;
            float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
            float flash = _calm ? 0.25f : 1f;

            float pulse = GameState.Pulse;
            if (GameState.GlitchCountdown >= 0f) pulse = Mathf.Max(pulse, 0.3f);
            Shader.SetGlobalFloat(PulseId, pulse * flash);
            Shader.SetGlobalFloat(MeltId, Mathf.Clamp01(GameState.Melt / 100f));
            Shader.SetGlobalFloat(TraceId, GameState.TraceActive ? (_calm ? 0.6f : 0.6f + 0.4f * Mathf.Sin(now * 12f)) : 0f);
            Shader.SetGlobalFloat(FlashId, flash);

            // REBOOT: close over restartWipe (RoomManager resets at the end of it), hold until RoomReset, open over restartWipe.
            float step = dt / Mathf.Max(cfg.restartWipe, 0.02f);
            if (_rebootClosing) { _reboot += step; if (_reboot >= 1f) { _reboot = 1f; _rebootClosing = false; } }
            else if (_reboot > 0f)
            {
                if (_rebootHold > 0f) _rebootHold -= dt;
                else _reboot = Mathf.Max(0f, _reboot - step);
            }
            Shader.SetGlobalFloat(RebootId, _reboot);

            if (_wipe >= 0f) { _wipe += dt / WipeTime; if (_wipe > 1f) _wipe = -1f; }
            Shader.SetGlobalFloat(WipeId, _wipe);

            _drip = GameState.Melt >= 100f ? Mathf.Min(1f, _drip + dt / Mathf.Max(cfg.meltDeathDrip, 0.05f)) : 0f;
            Shader.SetGlobalFloat(DripId, _drip);

            _wave = Mathf.Min(_wave + dt, 99f);
            Shader.SetGlobalVector(FlipWaveId, new Vector4(_waveOrigin.x, _waveOrigin.y, _waveOrigin.z, _wave));

            var cam = PlayerRef.Cam;
            if (cam && cam != _rainCam)
            {
                _rainCam = cam;
                GlyphRain.Attach(cam);
                OnLayer(LayerState.InSource);
            }
        }
    }
}
