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
        StoryScreen _story; StoryAsk _ask; StoryGuide _guide;
        SettingsScreen _settings; RebindScreen _rebind; bool _rebindFromSettings, _tabLatch;
        RunMapScreen _map; DraftScreen _draft; MarketScreen _market; CooldownScreen _cooldown; RunResultsScreen _runResults; DeepLayerScreen _deep;
        ShardArchive _archive; ShardToast _toast; EndingSequence _endingSeq;
        UnlockTree _unlocks; FlipFxLayer _flipFx;
        TimeAttackScreen _ta;
        DailyScreen _daily; DailyTag _dailyTag;
        bool _built, _mapTab, _rebuild, _cbShown;

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
            TitleEntered();
        }

        void OnDestroy() { if (I == this) I = null; }

        // ------------------------------------------------------------------ build
        T Make<T>(string name) where T : UIPanel, new() { var p = new T(); p.Build(_root, name); return p; }

        void BuildCanvas()
        {
            UIKit.ApplyPalette();
            _cbShown = Desync.Art.Palette.Colorblind;
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
            HudGuards.Create(_root);   // chase edge pulse, under the HUD
            _hud = Make<HudScreen>("Hud");
            _flipFx = Make<FlipFxLayer>("FlipFx"); _flipFx.Show();   // the flip cosmetic: always on, draws only while an effect plays
            _guide = Make<StoryGuide>("StoryGuide");
            _recap = Make<RecapScreen>("Recap");
            _goals = Make<GoalsOverlay>("Goals");
            _finale = Make<FinaleScreen>("Finale");
            _results = Make<ResultsScreen>("Results");   // above the finale: room 6 reports at the finale's end
            _draft = Make<DraftScreen>("Draft");
            _market = Make<MarketScreen>("Market");
            _cooldown = Make<CooldownScreen>("Cooldown");
            _map = Make<RunMapScreen>("RunMap");
            _runResults = Make<RunResultsScreen>("RunResults");
            _deep = Make<DeepLayerScreen>("DeepLayer");
            _unlocks = Make<UnlockTree>("Unlocks");
            _ta = Make<TimeAttackScreen>("TimeAttack");
            _daily = Make<DailyScreen>("Daily"); _dailyTag = Make<DailyTag>("DailyTag");
            _title.OnDeep = () => _deep.Show();
            _ask = Make<StoryAsk>("StoryAsk");
            _story = Make<StoryScreen>("Story");
            _title.OnStory = PlayStory; _ask.OnWatch = PlayStory;
            _pause = Make<PauseScreen>("Pause");
            _levels = Make<LevelSelectScreen>("LevelSelect");
            _endingSeq = Make<EndingSequence>("EndingSequence");
            _archive = Make<ShardArchive>("ShardArchive");
            _toast = Make<ShardToast>("ShardToast");
            _settings = Make<SettingsScreen>("Settings");   // SOUND + ACCESS and KEY BINDINGS sit above the pause menu and the title
            _rebind = Make<RebindScreen>("Rebind");
            _settings.OnBack = () => _settings.Hide();
            _settings.OnKeys = () => { _settings.Hide(); _rebindFromSettings = true; _rebind.Show(); };
            _rebind.OnBack = () => { _rebind.Hide(); if (_rebindFromSettings) _settings.Show(); _rebindFromSettings = false; };

            var sfx = GetComponent<AudioSource>();   // UI-local: results tally ticks (pitch per line); kept across a palette rebuild
            if (!sfx) sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false; sfx.spatialBlend = 0f; sfx.ignoreListenerPause = true;
            _results.Init(sfx);
            _draft.Init(sfx);
            _story.Init(sfx);
            A11yUi.PostBuild(_root);   // HUD size, subtitle size, high contrast, remapped-key labels, interface volume
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
            _levels.Hide(); _deep.Hide(); _ask.Hide(); _story.Hide(); _settings.Hide(); _rebind.Hide();
            _archive.Hide(); _endingSeq.Hide();
            _ta.Hide();
            _unlocks.Hide();
            _daily.Hide();
            if (m != Mode.Room) _guide.Leave();
        }

        /// Title scene entered: the first-time STORY prompt, once per player (StoryAsk remembers the answer in the save).
        internal void TitleEntered() { if (Current == Mode.Title && StoryAsk.ShouldAsk()) _ask.Show(); }

        /// Time-Attack select screen (title menu entry; Flow's TimeAttack.SelectRequested after a room).
        internal void OpenTimeAttack() => _ta.Show();

        /// Daily Breach + Weekly Ladder hub (title menu entry, wired by the orchestrator): seed, mutators, attempt, leaderboard, START.
        internal void OpenDaily() => _daily.Show();

        /// Unlock tree (title menu entry, wired by the orchestrator): spend Fragments on exploit pools, starters, Black Market, Time-Attack.
        internal void OpenUnlocks() => _unlocks.Show();

        void PlayStory() { _ask.Hide(); _title.Hide(); _story.Play(); }

        /// Title menu entry: the story shard archive (30 shards by act, endings seen).
        internal void OpenArchive() => _archive.Show();

        void HideRun(bool all)
        {
            _map.Hide(); _draft.Hide(); _market.Hide(); _cooldown.Hide(); _deep.Hide(); _mapTab = false;
            if (all) _runResults.Hide();
        }

        void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            DropExtraEventSystems();
            SetMode(ModeFor(s.name));
            TitleEntered();
            if (Current == Mode.Room) { _results.Hide(); _recap.Hide(); _finale.Hide(); }
        }

        // ------------------------------------------------------------------ event wiring
        void OnEnable()
        {
            if (I != this || !_built) return;
            Subscribe();
        }

        void Subscribe()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            GameEvents.SettingsChanged += OnSettingsChanged;
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
            GameEvents.Flipped += _flipFx.OnFlipped;
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
            TimeAttack.SelectRequested += OpenTimeAttack;
            Desync.Enemies.EnemyEvents.TrackerSpawned += _hud.OnTrackerSpawned;
            Desync.Enemies.BossEvents.Phase += _hud.OnBossPhase;
            Desync.Enemies.BossEvents.Ending += _hud.OnBossEnding;
            Desync.Enemies.BossEvents.Ending += _endingSeq.Arm;
            ShardEvents.Collected += _toast.OnCollected;
        }

        void OnDisable()
        {
            if (I != this || !_built) return;
            Unsubscribe();
        }

        void Unsubscribe()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            GameEvents.SettingsChanged -= OnSettingsChanged;
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
            GameEvents.Flipped -= _flipFx.OnFlipped;
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
            TimeAttack.SelectRequested -= OpenTimeAttack;
            Desync.Enemies.EnemyEvents.TrackerSpawned -= _hud.OnTrackerSpawned;
            Desync.Enemies.BossEvents.Phase -= _hud.OnBossPhase;
            Desync.Enemies.BossEvents.Ending -= _hud.OnBossEnding;
            Desync.Enemies.BossEvents.Ending -= _endingSeq.Arm;
            ShardEvents.Collected -= _toast.OnCollected;
        }

        void OnStartRequested(int room) { _levels.Hide(); }

        // ------------------------------------------------------------------ palette change: widgets copy their colours when built, so rebuild
        void OnSettingsChanged()
        {
            Desync.Art.Palette.Refresh();   // the saved flag may have just been written; the post-FX controller may not have run yet
            if (Desync.Art.Palette.Colorblind != _cbShown) _rebuild = true;   // done at the start of the next frame, not inside the click that raised this
        }

        /// Destroys and rebuilds the canvas in the current palette, then restores what was on screen (the pause menu is the only place the
        /// palette changes, so: mode, HUD, pause). Allocates; runs only on a palette change.
        internal void Rebuild()
        {
            _rebuild = false;
            if (!_built) return;
            bool pause = _pause.IsOn, lockLost = _pause.LockLost, run = RunView.Active && RunState.Phase != RunPhase.Room;
            bool settings = _settings.IsOn, rebind = _rebind.IsOn;
            var phase = RunState.Phase;
            Unsubscribe();
            foreach (var p in new UIPanel[] { _title, _ending, _hud, _recap, _goals, _finale, _results, _draft, _market, _cooldown, _map, _runResults, _deep, _unlocks, _flipFx, _pause, _levels, _story, _ask, _guide, _settings, _rebind, _archive, _toast, _endingSeq })
                p.Hide();   // panels stop their own tweens in Hide
            var old = Canvas.gameObject;
            old.SetActive(false); Destroy(old);
            _mapTab = false;
            BuildCanvas();
            Subscribe();
            SetMode(Current, true);
            if (Current == Mode.Room) _guide.Resume();
            if (run) OnRunPhase(phase);
            if (pause) _pause.Show(lockLost);
            if (settings) _settings.Show();
            if (rebind) _rebind.Show();
        }

        /// Entry points for the menus: SOUND + ACCESS and KEY BINDINGS (the pause menu has buttons for both; the title wires its own).
        internal void OpenSettings() { _rebind.Hide(); _rebindFromSettings = false; _settings.Show(); }
        internal void OpenRebind() { _settings.Hide(); _rebindFromSettings = false; _rebind.Show(); }

        /// Rebuild the canvas at the start of the next frame (high contrast changes what every screen is built with).
        internal void RequestRebuild() { _rebuild = true; }

        void OnLevels(bool on) { if (on) _levels.Show(); else _levels.Hide(); }

        void OnPaused(bool paused)
        {
            if (!paused) { _pause.Hide(); _settings.Hide(); _rebind.Hide(); return; }
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
            _guide.RoomStarted(room);
        }

        void OnDeathRecap(DeathInfo d) { if (Current == Mode.Room) { _goals.Hide(); _recap.Show(d); } }

        void OnRoomReport(RoomReport r)
        {
            if (r == null) return;
            _goals.Hide(); _recap.Hide();
            if (_endingSeq.PlayArmed(() => _results.Show(r))) { _hud.Hide(); return; }   // an ARCHITECT ending plays before its results
            _results.Show(r);
        }

        void OnGameCompleted() { _results.Hide(); _finale.Show(); }

        void OnRebooting() { _results.Hide(); _recap.Hide(); _hud.OnRebooting(); }

        void OnRoomReset() { _recap.Hide(); _hud.OnRoomReset(); }

        // ------------------------------------------------------------------ BREACH run (RunState / RunEvents)
        // Handler order is not guaranteed: the run manager may already have raised its first phase, so re-apply it.
        void OnRunStart(RunMode m) { RunView.ResetRun(); HideRun(true); if (RunState.Phase != RunPhase.None && RunState.Phase != RunPhase.Over) OnRunPhase(RunState.Phase); }

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
        void Update()
        {
            if (_rebuild) Rebuild();
            TickAll(Time.unscaledDeltaTime);
        }

        /// TAB: held, or latched on each press when the accessibility toggle is on. A pause, results or the death card clears the latch.
        bool TabDown()
        {
            if (!A11y.TabToggle || GameState.Paused || _results.IsOn || _recap.IsOn) { _tabLatch = false; return UIKit.GoalsKey.IsPressed(); }
            if (UIKit.GoalsKey.WasPressedThisFrame()) _tabLatch = !_tabLatch;
            return _tabLatch;
        }

        internal void TickAll(float dt)
        {
            switch (Current)
            {
                case Mode.Title:
                    if (_story.IsOn) { _story.Tick(dt); break; }   // the cinematic owns the title camera and keys
                    _title.Blocked = _deep.IsOn || _unlocks.IsOn || _ask.IsOn || _settings.IsOn || _rebind.IsOn || _ta.IsOn || _daily.IsOn || _archive.IsOn; _title.Tick(dt);
                    if (_ask.IsOn) _ask.Tick(dt);
                    break;
                case Mode.Room:
                    if (GameState.Paused && !_pause.IsOn && !_levels.IsOn) OnPaused(true);   // missed the event / level select just closed
                    _hud.Tick(dt);
                    if (_guide.IsOn) _guide.Tick(dt);
                    bool tab = TabDown() && !GameState.Paused && !_results.IsOn && !_recap.IsOn;   // TAB (hold)
                    bool breach = RunView.Active, goals = tab && !breach, map = tab && breach && RunState.Phase == RunPhase.Room;   // BREACH: TAB = run map
                    if (goals != _goals.IsOn) { if (goals) _goals.Show(); else _goals.Hide(); }
                    if (map != _mapTab) { _mapTab = map; if (map) _map.Show(false); else _map.Hide(); }
                    break;
                case Mode.Ending: _ending.Tick(dt); break;
            }
            if (_pause.IsOn) _pause.Tick(dt);
            if (_rebind.IsOn) _rebind.Tick(dt);
            if (_recap.IsOn) _recap.Tick(dt);
            if (_results.IsOn) _results.Tick(dt);
            if (_finale.IsOn) _finale.Tick(dt);
            if (_levels.IsOn) _levels.Tick(dt);
            if (_map.IsOn) _map.Tick(dt);
            if (_draft.IsOn) _draft.Tick(dt);
            if (_market.IsOn) _market.Tick(dt);
            if (_cooldown.IsOn) _cooldown.Tick(dt);
            if (_runResults.IsOn) _runResults.Tick(dt);
            if (_deep.IsOn) _deep.Tick(dt);
            if (_archive.IsOn) _archive.Tick(dt);
            if (_endingSeq.IsOn) _endingSeq.Tick(dt);
            if (_toast.IsOn) _toast.Tick(dt);
            if (_unlocks.IsOn) _unlocks.Tick(dt);
            _flipFx.Tick(dt);
            if (_ta.IsOn) _ta.Tick(dt);
            if (_daily.IsOn) _daily.Tick(dt);
            _dailyTag.Tick(dt);   // shows itself while a Daily / Weekly Ladder room is played
        }
    }
}
