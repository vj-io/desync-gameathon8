using System;
using Desync.Flow;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Self-bootstrapping persistent UI root (CONTRACT pattern). Builds one overlay Canvas in code, owns the screens and
    /// routes GameEvents to them. UI raises Start/Resume/Restart/Quit/SettingsChanged requests; for BREACH runs it reads
    /// Flow's RunState and raises RunEvents (the run contract), never calling Flow code directly.
    public sealed class UIManager : MonoBehaviour
    {
        public enum Mode { None, Title, Room, Ending }

        public static UIManager I { get; private set; }
        public Mode Current { get; private set; } = Mode.None;
        public Canvas Canvas { get; private set; }

        RectTransform _root;
        EventSystem _es;
        TitleScreen _title; HudScreen _hud; PauseScreen _pause; LevelSelectScreen _levels; ResultsScreen _results; RecapScreen _recap; GoalsOverlay _goals;
        FinaleScreen _finale; EndingScreen _ending;
        RunMapScreen _map; DraftScreen _draft; MarketScreen _market; CooldownScreen _cooldown; RunResultsScreen _runResults;
        bool _built, _mapTab;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (I != null) return;
            var go = new GameObject("DesyncUI");
            DontDestroyOnLoad(go);
            go.AddComponent<UIManager>();
        }

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            UIKit.Init();
            GameInput.Init();
            BuildCanvas();
            EnsureEventSystem();
            _built = true;
            SetMode(ModeFor(SceneManager.GetActiveScene().name), true);
        }

        void OnDestroy() { if (I == this) I = null; }

        // ------------------------------------------------------------------ build
        T Make<T>(string name) where T : UIPanel, new() { var p = new T(); p.Build(_root, name); return p; }

        void BuildCanvas()
        {
            var cgo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            Canvas = cgo.GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay; Canvas.sortingOrder = 100;
            var cs = cgo.GetComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920f, 1080f);
            cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            cs.matchWidthOrHeight = 0.5f;
            _root = (RectTransform)cgo.transform;

            // bottom -> top
            _title = Make<TitleScreen>("Title");
            _ending = Make<EndingScreen>("Ending");
            _hud = Make<HudScreen>("Hud");
            _recap = Make<RecapScreen>("Recap");
            _goals = Make<GoalsOverlay>("Goals");
            _finale = Make<FinaleScreen>("Finale");
            _results = Make<ResultsScreen>("Results");   // above the finale: room 6 reports at the finale's end
            _draft = Make<DraftScreen>("Draft");
            _market = Make<MarketScreen>("Market");
            _cooldown = Make<CooldownScreen>("Cooldown");
            _map = Make<RunMapScreen>("RunMap");
            _runResults = Make<RunResultsScreen>("RunResults");
            _pause = Make<PauseScreen>("Pause");
            _levels = Make<LevelSelectScreen>("LevelSelect");

            var sfx = gameObject.AddComponent<AudioSource>();   // UI-local: results tally ticks (pitch per line)
            sfx.playOnAwake = false; sfx.spatialBlend = 0f; sfx.ignoreListenerPause = true;
            _results.Init(sfx);
            _draft.Init(sfx);
        }

        void EnsureEventSystem()
        {
            _es = EventSystem.current;
            if (!_es) _es = FindAnyObjectByType<EventSystem>();
            if (!_es)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem));
                go.transform.SetParent(transform, false);
                _es = go.GetComponent<EventSystem>();
            }
            if (!_es.GetComponent<InputSystemUIInputModule>())
            {
                foreach (var m in _es.GetComponents<BaseInputModule>()) Destroy(m);   // legacy StandaloneInputModule would throw with Input System only
                _es.gameObject.AddComponent<InputSystemUIInputModule>();
            }
        }

        /// A scene may ship its own EventSystem: keep ours, drop the extras (UI must keep working across loads).
        void DropExtraEventSystems()
        {
            foreach (var es in FindObjectsByType<EventSystem>(FindObjectsInactive.Include))
            {
                if (es == _es) continue;
                foreach (var m in es.GetComponents<BaseInputModule>()) Destroy(m);
                Destroy(es);
            }
            if (!_es) EnsureEventSystem();
        }

        // ------------------------------------------------------------------ mode
        static Mode ModeFor(string scene)
        {
            if (scene == SceneFlow.Title) return Mode.Title;
            if (scene == SceneFlow.Ending) return Mode.Ending;
            return scene != null && scene.StartsWith("Room_", StringComparison.Ordinal) ? Mode.Room : Mode.None;
        }

        /// Public for tests / debug tooling; normal flow derives the mode from the loaded scene and RoomStarted.
        public void SetMode(Mode m, bool force = false)
        {
            if (!_built || (m == Current && !force)) return;
            Current = m;
            if (m == Mode.Title) _title.Show(); else _title.Hide();
            if (m == Mode.Ending) _ending.Show(); else _ending.Hide();
            if (m == Mode.Room) _hud.Show(); else _hud.Hide();
            if (m != Mode.Room) { _pause.Hide(); _results.Hide(); _recap.Hide(); _goals.Hide(); _finale.Hide(); }
            if (m == Mode.Title || m == Mode.Ending) HideRun(true);
            _levels.Hide();
        }

        void HideRun(bool all)
        {
            _map.Hide(); _draft.Hide(); _market.Hide(); _cooldown.Hide(); _mapTab = false;
            if (all) _runResults.Hide();
        }

        void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            DropExtraEventSystems();
            SetMode(ModeFor(s.name));
            if (Current == Mode.Room) { _results.Hide(); _recap.Hide(); _finale.Hide(); }
        }

        // ------------------------------------------------------------------ event wiring
        void OnEnable()
        {
            if (I != this || !_built) return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            GameEvents.StartRequested += OnStartRequested;
            GameEvents.Paused += OnPaused;
            GameEvents.LevelSelectToggled += OnLevels;
            GameEvents.RoomStarted += OnRoomStarted;
            GameEvents.DeathRecap += OnDeathRecap;
            GameEvents.RoomReportReady += OnRoomReport;
            GameEvents.Scored += _hud.OnScored;
            GameEvents.Hint += _hud.OnHint;
            GameEvents.GameCompleted += OnGameCompleted;
            GameEvents.Flipped += _hud.OnFlipped;
            GameEvents.FlipRejected += _hud.OnFlipRejected;
            GameEvents.TraceStarted += _hud.OnTraceStarted;
            GameEvents.MeltWarning += _hud.OnMeltWarning;
            GameEvents.AccessGranted += _hud.OnAccessGranted;
            GameEvents.ExitOpened += _hud.OnExitOpened;
            GameEvents.Rebooting += OnRebooting;
            GameEvents.RoomReset += OnRoomReset;
            GameEvents.Hijacked += _hud.OnHijacked;
            GameEvents.PopText += _hud.ShowPop;
            RunEvents.PhaseChanged += OnRunPhase;
            RunEvents.RunEnded += OnRunEnded;
            RunEvents.StartRun += OnRunStart;
            Desync.Enemies.EnemyEvents.TrackerSpawned += _hud.OnTrackerSpawned;
        }

        void OnDisable()
        {
            if (I != this || !_built) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            GameEvents.StartRequested -= OnStartRequested;
            GameEvents.Paused -= OnPaused;
            GameEvents.LevelSelectToggled -= OnLevels;
            GameEvents.RoomStarted -= OnRoomStarted;
            GameEvents.DeathRecap -= OnDeathRecap;
            GameEvents.RoomReportReady -= OnRoomReport;
            GameEvents.Scored -= _hud.OnScored;
            GameEvents.Hint -= _hud.OnHint;
            GameEvents.GameCompleted -= OnGameCompleted;
            GameEvents.Flipped -= _hud.OnFlipped;
            GameEvents.FlipRejected -= _hud.OnFlipRejected;
            GameEvents.TraceStarted -= _hud.OnTraceStarted;
            GameEvents.MeltWarning -= _hud.OnMeltWarning;
            GameEvents.AccessGranted -= _hud.OnAccessGranted;
            GameEvents.ExitOpened -= _hud.OnExitOpened;
            GameEvents.Rebooting -= OnRebooting;
            GameEvents.RoomReset -= OnRoomReset;
            GameEvents.Hijacked -= _hud.OnHijacked;
            GameEvents.PopText -= _hud.ShowPop;
            RunEvents.PhaseChanged -= OnRunPhase;
            RunEvents.RunEnded -= OnRunEnded;
            RunEvents.StartRun -= OnRunStart;
            Desync.Enemies.EnemyEvents.TrackerSpawned -= _hud.OnTrackerSpawned;
        }

        void OnStartRequested(int room) { _levels.Hide(); }

        void OnLevels(bool on) { if (on) _levels.Show(); else _levels.Hide(); }

        void OnPaused(bool paused)
        {
            if (!paused) { _pause.Hide(); return; }
            if (_levels.IsOn) return;   // F2 pauses too; the pause menu appears (via the poll in TickAll) once level select closes
            // Flow raises Paused(true) BEFORE unlocking: a pointer that is already free means the browser took the lock.
            _pause.Show(Cursor.lockState != CursorLockMode.Locked);
        }

        void OnRoomStarted(int room)
        {
            SetMode(Mode.Room);
            if (!_hud.IsOn) _hud.Show();   // a BREACH node screen may have hidden it
            _results.Hide(); _recap.Hide(); _finale.Hide(); _levels.Hide();
            if (!GameState.Paused) _pause.Hide();
            _hud.OnRoomStarted(room);
        }

        void OnDeathRecap(DeathInfo d) { if (Current == Mode.Room) { _goals.Hide(); _recap.Show(d); } }

        void OnRoomReport(RoomReport r) { if (r == null) return; _goals.Hide(); _recap.Hide(); _results.Show(r); }

        void OnGameCompleted() { _results.Hide(); _finale.Show(); }

        void OnRebooting() { _results.Hide(); _recap.Hide(); _hud.OnRebooting(); }

        void OnRoomReset() { _recap.Hide(); _hud.OnRoomReset(); }

        // ------------------------------------------------------------------ BREACH run (RunState / RunEvents)
        // Handler order is not guaranteed: the run manager may already have raised its first phase, so re-apply it.
        void OnRunStart(RunMode m) { RunView.ResetRun(); HideRun(true); if (RunState.Phase != RunPhase.None) OnRunPhase(RunState.Phase); }

        /// Map / Draft / Market / Cooldown are full screens between rooms; Room hands back to the HUD; Over shows the run results.
        void OnRunPhase(RunPhase p)
        {
            RunView.TrackNode();
            UIPanel same = p == RunPhase.Draft ? _draft : p == RunPhase.Market ? (UIPanel)_market : p == RunPhase.Cooldown ? _cooldown : null;
            if (same != null && same.IsOn) return;   // the run re-raises a phase after a reroll / purchase: the screen repaints itself
            bool between = p == RunPhase.Map || p == RunPhase.Draft || p == RunPhase.Market || p == RunPhase.Cooldown;
            if (between) { _results.Hide(); _recap.Hide(); _goals.Hide(); _hud.Hide(); }
            else if (p == RunPhase.Room && Current == Mode.Room && !_hud.IsOn) _hud.Show();
            HideRun(p == RunPhase.Room);
            switch (p)
            {
                case RunPhase.Map: _map.Show(true); break;
                case RunPhase.Draft: _draft.Show(); break;
                case RunPhase.Market: _market.Show(); break;
                case RunPhase.Cooldown: _cooldown.Show(); break;
                case RunPhase.Over: OnRunEnded(); break;
            }
        }

        void OnRunEnded()
        {
            if (_runResults.IsOn) return;
            HideRun(false); _results.Hide(); _recap.Hide(); _goals.Hide();
            _runResults.Show();
        }

        // ------------------------------------------------------------------ frame
        void Update() { TickAll(Time.unscaledDeltaTime); }

        internal void TickAll(float dt)
        {
            switch (Current)
            {
                case Mode.Title: _title.Tick(dt); break;
                case Mode.Room:
                    if (GameState.Paused && !_pause.IsOn && !_levels.IsOn) OnPaused(true);   // missed the event / level select just closed
                    _hud.Tick(dt);
                    bool tab = UIKit.GoalsKey.IsPressed() && !GameState.Paused && !_results.IsOn && !_recap.IsOn;   // TAB (hold)
                    bool breach = RunView.Active, goals = tab && !breach, map = tab && breach && RunState.Phase == RunPhase.Room;   // BREACH: TAB = run map
                    if (goals != _goals.IsOn) { if (goals) _goals.Show(); else _goals.Hide(); }
                    if (map != _mapTab) { _mapTab = map; if (map) _map.Show(false); else _map.Hide(); }
                    break;
                case Mode.Ending: _ending.Tick(dt); break;
            }
            if (_pause.IsOn) _pause.Tick(dt);
            if (_recap.IsOn) _recap.Tick(dt);
            if (_results.IsOn) _results.Tick(dt);
            if (_finale.IsOn) _finale.Tick(dt);
            if (_levels.IsOn) _levels.Tick(dt);
            if (_map.IsOn) _map.Tick(dt);
            if (_draft.IsOn) _draft.Tick(dt);
            if (_market.IsOn) _market.Tick(dt);
            if (_cooldown.IsOn) _cooldown.Tick(dt);
            if (_runResults.IsOn) _runResults.Tick(dt);
        }
    }
}
