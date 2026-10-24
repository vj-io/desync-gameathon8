using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Desync.Art;
using Desync.Content;
using Desync.Enemies;
using Desync.Flow;
using Desync.Gen;
using Desync.Net;
using Desync.TestKit;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Desync.UI.Tests
{
    /// M3 UI: Deep Layer select + its title entry, boss HUDs (COMPILER / ARCHITECT), subtitles, decoy marker, the exploit tooltip rows and
    /// condition glow for every catalog exploit, the colour-blind palette toggle, and zero GC with all of it live. Flow, run and room managers
    /// are switched off so nothing loads; the save is redirected to a throw-away key.
    public class M3UITests
    {
        const string SaveKey = "desync.save.m3uitest";
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        GameObject _camGo;
        readonly List<MonoBehaviour> _paused = new List<MonoBehaviour>();
        readonly List<string> _log = new List<string>();   // requests the UI raised: "start:Campaign", "deep:4:hardened_ice1,tight_deadline1", "room:0"

        // ------------------------------------------------------------------ setup
        [UnitySetUp]
        public IEnumerator Up()
        {
            _camGo = new GameObject("M3TestCam", typeof(Camera));
            var c = _camGo.GetComponent<Camera>(); c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.08f, 0.09f, 0.16f);
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb && mb.enabled && (mb.GetType().Name == "GameManager" || mb.GetType().Name == "RunManager" || mb.GetType().Name == "RoomManager"))
                { mb.enabled = false; _paused.Add(mb); }
            SaveService.Unload(SaveKey);
            PlayerPrefs.DeleteKey(SaveKey);
            ResetState();
            _log.Clear();
            RunEvents.StartRun += OnStart; GameEvents.StartRequested += OnRoom; DeepRun.StartRequested += OnDeep;
            yield return null;
            Assert.IsNotNull(UIManager.I, "UIManager did not self-bootstrap");
        }

        [TearDown]
        public void Down()
        {
            EndInput();
            RunEvents.StartRun -= OnStart; GameEvents.StartRequested -= OnRoom; DeepRun.StartRequested -= OnDeep;
            Palette.Colorblind = false; PlayerPrefs.DeleteKey(Palette.Key); PlayerPrefs.DeleteKey(Subtitles.Key);
            ResetState();
            RunEvents.PhaseChanged?.Invoke(RunPhase.None);
            var ui = UIManager.I;
            if (ui) { ui.SetMode(UIManager.Mode.Title, true); ui.SetMode(UIManager.Mode.None, true); }
            if (_camGo) Object.Destroy(_camGo);
            foreach (var mb in _paused) if (mb) mb.enabled = true;
            _paused.Clear();
            PlayerPrefs.DeleteKey(SaveKey);
            SaveService.Unload();
        }

        void OnStart(RunMode m) => _log.Add("start:" + m);
        void OnRoom(int r) => _log.Add("room:" + r);
        void OnDeep(int level, IReadOnlyList<MutatorPick> picks)
        {
            var sb = new System.Text.StringBuilder("deep:" + level + ":");
            for (int i = 0; i < picks.Count; i++) sb.Append(i > 0 ? "," : "").Append(picks[i].id).Append(picks[i].rank);
            _log.Add(sb.ToString());
        }
        string Last => _log.Count > 0 ? _log[_log.Count - 1] : "";

        static void ResetState()
        {
            RunState.Mode = RunMode.Classic; RunState.Phase = RunPhase.None; RunState.Seed = 0; RunState.Graph = null; RunState.NodeId = null; RunState.Act = 1;
            RunState.Held.Clear(); RunState.RoomMutators.Clear(); RunState.Offer.Clear(); RunState.Market.Clear(); RunState.Grades.Clear();
            RunState.Backups = RunState.Cycles = RunState.Rerolls = RunState.Fragments = 0; RunState.MeltCarry = RunState.RunTime = 0f; RunState.RunScore = 0; RunState.Won = false;
            RunView.ResetRun();
            DeepRun.Level = 0; DeepRun.Picks.Clear();
            GameState.RoomIndex = -1; GameState.Melt = 0; GameState.Chain = 0; GameState.TraceCount = 0; GameState.Paused = false; GameState.Alive = true;
            GameState.GlitchCountdown = -1f; GameState.TraceActive = false; GameState.InteractPrompt = null; GameState.Score = 0; GameState.Multiplier = 1f;
            GameState.RoomTDev = 0f;
            RoomClock.Time = 0f; LayerState.Set(false);
            Subtitles.Clear();
            Decoys.ClearAll();
            PlayerRef.Cam = null; PlayerRef.Root = null;
        }

        /// The profile of someone who beat the ARCHITECT once and won Deep Layer `best`.
        static void OpenDeepLayer(int best)
        {
            var save = SaveService.Data;
            if (!save.unlocks.Contains("kill_architect")) save.unlocks.Add("kill_architect");
            save.deepLayerMax = best;
        }

        static T Screen<T>(string field) where T : class =>
            typeof(UIManager).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(UIManager.I) as T;

        static bool Visible(string path) { var go = GameObject.Find(path); return go && go.activeInHierarchy; }
        static GameObject Find(string path) { Assert.IsTrue(Visible(path), "not found / not visible: " + path); return GameObject.Find(path); }

        static void Click(string path)
        {
            var ev = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(Find(path), ev, ExecuteEvents.pointerDownHandler);
        }

        static string Txt(string path) { var t = Find(path).GetComponent<TMP_Text>(); t.ForceMeshUpdate(); return t.GetParsedText(); }

        Keyboard _kb; Gamepad _pad; InputSettings.BackgroundBehavior _bg;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode _gv;
#endif
        void BeginInput()
        {
            if (_kb != null) return;
            _bg = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            _gv = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            _kb = InputSystem.AddDevice<Keyboard>(); InputSystem.EnableDevice(_kb);
            _pad = InputSystem.AddDevice<Gamepad>();
        }

        void EndInput()
        {
            if (_kb == null) return;
            InputSystem.RemoveDevice(_kb); InputSystem.RemoveDevice(_pad); _kb = null; _pad = null;
            InputSystem.settings.backgroundBehavior = _bg;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = _gv;
#endif
        }

        IEnumerator Key(Key k)
        {
            BeginInput();
            InputSystem.QueueStateEvent(_kb, new KeyboardState(k));
            yield return Frames(2);
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            yield return Frames(2);
        }

        IEnumerator Pad(GamepadButton b)
        {
            BeginInput();
            InputSystem.QueueStateEvent(_pad, new GamepadState(b));
            yield return Frames(2);
            InputSystem.QueueStateEvent(_pad, new GamepadState());
            yield return Frames(2);
        }

        // ------------------------------------------------------------------ screenshots (.work/ui4/shots, best effort)
        static string ShotDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".work", "ui4", "shots"));
        static RenderTexture _rt;
        IEnumerator Shot(string name)
        {
            var ui = UIManager.I; var cam = _camGo.GetComponent<Camera>();
            if (!_rt) _rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var cs = ui.Canvas.GetComponent<CanvasScaler>();
            var mode = cs.uiScaleMode; float scale = cs.scaleFactor; var rmode = ui.Canvas.renderMode; var wcam = ui.Canvas.worldCamera;
            cam.targetTexture = _rt;
            cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; cs.scaleFactor = 1f;
            ui.Canvas.renderMode = RenderMode.ScreenSpaceCamera; ui.Canvas.worldCamera = cam; ui.Canvas.planeDistance = 1f;
            yield return null;
            var fx = new List<MonoBehaviour>();   // the post-FX pass would distort a camera capture of the UI: off for the shot only
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb && mb.enabled && mb.GetType().Name == "PostFXController") { mb.enabled = false; fx.Add(mb); }
            Canvas.ForceUpdateCanvases();
            cam.Render();
            foreach (var mb in fx) if (mb) mb.enabled = true;
            var prev = RenderTexture.active; RenderTexture.active = _rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            try { Directory.CreateDirectory(ShotDir); File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG()); }
            catch (Exception e) { Debug.LogWarning("[UI-TEST] shot " + name + " not written: " + e.Message); }
            Object.Destroy(tex);
            cam.targetTexture = null;
            ui.Canvas.renderMode = rmode; ui.Canvas.worldCamera = wcam; cs.uiScaleMode = mode; cs.scaleFactor = scale;
        }

        static Exploit X(string id) { var e = RunView.Cat.Exploit(id); Assert.IsNotNull(e, "catalog lacks " + id); return e; }

        static void NewRun(RunPhase phase)
        {
            RunState.Mode = RunMode.Campaign; RunState.Seed = 7; RunState.Graph = Generator.GenerateRun(7, 0); RunState.Act = 1; RunState.NodeId = null; RunState.Phase = phase;
        }

        // ================================================================== title -> Deep Layer
        [UnityTest, Timeout(120000)]
        public IEnumerator Title_Deep_Layer_Is_Locked_Until_The_First_Architect_Kill()
        {
            var ui = UIManager.I; var title = Screen<TitleScreen>("_title"); var deep = Screen<DeepLayerScreen>("_deep");
            ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            Assert.IsFalse(title.DeepOpen);
            StringAssert.Contains("LOCKED", Txt("Title/Card/Mode4/Label"));
            Click("Title/Card/Mode4");
            Assert.IsFalse(deep.IsOn, "locked: the click opens nothing");
            Assert.IsEmpty(_log, "locked: no run starts");

            yield return Key(UnityEngine.InputSystem.Key.DownArrow);   // CLASSIC -> DAILY
            Assert.AreEqual(TitleScreen.DailyItem, title.Selected);
            yield return Key(UnityEngine.InputSystem.Key.DownArrow);   // DAILY -> (DEEP skipped) -> the next open entry
            var save = SaveService.Data;
            Assert.AreEqual(TimeAttackRooms.Unlocked(save) ? TitleScreen.TimeAttackItem : OverclockRun.Unlocked(save) ? TitleScreen.OverclockItem : TitleScreen.UnlocksItem,
                            title.Selected, "a locked Deep Layer is skipped by the keyboard");

            OpenDeepLayer(0);
            ui.SetMode(UIManager.Mode.None, true); ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            Assert.IsTrue(title.DeepOpen);
            Assert.AreEqual("DEEP LAYER", Txt("Title/Card/Mode4/Label"));
            yield return Key(UnityEngine.InputSystem.Key.DownArrow); yield return Key(UnityEngine.InputSystem.Key.DownArrow);   // CLASSIC -> DAILY -> DEEP LAYER
            Assert.AreEqual(TitleScreen.DeepItem, title.Selected);
            yield return Shot("m01_title_deep");
            yield return Key(UnityEngine.InputSystem.Key.Space);
            Assert.IsTrue(deep.IsOn, "SPACE on DEEP LAYER opens the select");
            Assert.IsEmpty(_log, "it does not start a run by itself");
            ui.TickAll(0.016f);
            Assert.IsTrue(title.Blocked, "the title ignores its keys while the select is up");
        }

        // ================================================================== run start: handler order
        /// In builds the run manager's StartRun handler runs BEFORE the UI's: it has already raised PhaseChanged(Map) when the UI's own
        /// handler hides the run screens, so the UI must re-apply the phase (the map used to vanish: a blank screen after QUICK / CAMPAIGN).
        [UnityTest, Timeout(120000)]
        public IEnumerator Run_Start_Shows_The_Map_Even_When_The_Run_Manager_Handles_It_First()
        {
            var ui = UIManager.I; var map = Screen<RunMapScreen>("_map");
            ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            var saved = RunEvents.StartRun;   // the UI's handler (and this fixture's logger), in subscription order
            Action<RunMode> fakeRunManager = mode =>
            {
                RunState.Mode = mode; RunState.Seed = 11; RunState.Graph = Generator.GenerateRun(11, 0); RunState.Act = 1; RunState.NodeId = null;
                RunState.Phase = RunPhase.Map;
                RunEvents.PhaseChanged?.Invoke(RunPhase.Map);   // what RunManager.Begin does before returning to the event
            };
            try
            {
                RunEvents.StartRun = null;
                RunEvents.StartRun += fakeRunManager;   // first in the invocation list ...
                RunEvents.StartRun += saved;            // ... the UI second, as in a build
                Click("Title/Card/Mode0");              // CAMPAIGN BREACH
                Assert.AreEqual(RunPhase.Map, RunState.Phase);
                Assert.IsTrue(map.IsOn, "the map is visible after StartRun");
                Assert.IsTrue(map.Interactive, "and it is the choosing map");
                Assert.IsNotEmpty(map.ChoiceIds, "with the first row to choose from");
            }
            finally { RunEvents.StartRun = saved; }
        }

        // ================================================================== Deep Layer select
        [UnityTest, Timeout(120000)]
        public IEnumerator Deep_Layer_Select_Levels_Heat_And_Start()
        {
            var ui = UIManager.I; var deep = Screen<DeepLayerScreen>("_deep");
            OpenDeepLayer(3);   // DL 1-4 open
            ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            Click("Title/Card/Mode4");
            Assert.IsTrue(deep.IsOn);
            Assert.AreEqual(4, deep.MaxLevel); Assert.AreEqual(4, deep.Level, "opens on the newest open level");
            Assert.AreEqual(20, deep.TileCount, "the 20 heat mutators");
            Assert.AreEqual(4, deep.Heat); Assert.IsTrue(deep.CanStart);
            Assert.AreEqual("HEAT 4 / 4", Txt("DeepLayer/Panel/HeatTotal"));
            StringAssert.Contains("+40 %", deep.RewardText); StringAssert.Contains("280", deep.RewardText, "a 200 run banks 280 at DL 4");
            foreach (var p in DeepRules.Preset(4)) Assert.AreEqual(p.rank, deep.RankOf(p.id), "preset " + p.id);
            yield return Shot("m02_deep_layer");

            // a locked level does nothing
            Click("DeepLayer/Panel/Level9");
            Assert.AreEqual(4, deep.Level);
            // an open level loads its preset
            Click("DeepLayer/Panel/Level2");
            Assert.AreEqual(2, deep.Level); Assert.AreEqual(2, deep.Heat); Assert.AreEqual("HEAT 2 / 2", Txt("DeepLayer/Panel/HeatTotal"));

            // a tile steps its rank 0 -> 1 -> .. -> max -> 0, the heat total follows, START is refused until it matches the level
            int idx = -1;
            for (int i = 0; i < deep.TileCount; i++) if (GameObject.Find("DeepLayer/Panel/Tile" + i) != null && Txt("DeepLayer/Panel/Tile" + i + "/Name") == "BLACKOUT") idx = i;
            Assert.GreaterOrEqual(idx, 0, "Blackout tile");
            Click("DeepLayer/Panel/Tile" + idx);
            Assert.AreEqual(1, deep.RankOf("blackout")); Assert.AreEqual(3, deep.Heat); Assert.IsFalse(deep.CanStart);
            StringAssert.Contains("REMOVE 1", deep.StatusText);
            yield return Shot("m03_deep_layer_bad_heat");
            _log.Clear(); Click("DeepLayer/Panel/Start");
            Assert.IsEmpty(_log, "heat 3 / 2: refused");
            Click("DeepLayer/Panel/Tile" + idx);
            Assert.AreEqual(0, deep.RankOf("blackout"), "single-rank mutator wraps to off");
            Assert.IsTrue(deep.CanStart);

            // PRESET restores after a swap
            Click("DeepLayer/Panel/Tile" + idx); Assert.IsFalse(deep.CanStart);
            Click("DeepLayer/Panel/Preset");
            Assert.AreEqual(0, deep.RankOf("blackout")); Assert.IsTrue(deep.CanStart);

            // START raises DeepRun.StartRequested(level, picks) in canonical order
            Click("DeepLayer/Panel/Level3");
            Click("DeepLayer/Panel/Start");
            var want = DeepRules.Preset(3);
            var sb = new System.Text.StringBuilder("deep:3:");
            for (int i = 0; i < want.Count; i++) sb.Append(i > 0 ? "," : "").Append(want[i].id).Append(want[i].rank);
            Assert.AreEqual(new[] { sb.ToString() }, _log.ToArray());
            Click("DeepLayer/Panel/Start");
            Assert.AreEqual(1, _log.Count, "one start per visit");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Deep_Layer_Select_Keyboard_And_Pad_Focus_Ring()
        {
            var ui = UIManager.I; var deep = Screen<DeepLayerScreen>("_deep");
            OpenDeepLayer(9);   // all ten open
            ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            Click("Title/Card/Mode4");
            Assert.AreEqual(10, deep.Level, "DL 10 open: the select opens on it");
            Assert.AreEqual(deep.TileCount + DeepRules.MaxLevel + 2, deep.Focus, "focus starts on START");
            yield return new WaitForSecondsRealtime(0.4f);   // input guard

            yield return Key(UnityEngine.InputSystem.Key.RightArrow);   // wraps START -> DL 1
            Assert.AreEqual(0, deep.Focus);
            yield return Key(UnityEngine.InputSystem.Key.Space);
            Assert.AreEqual(1, deep.Level, "SPACE on a level picks it");
            yield return Key(UnityEngine.InputSystem.Key.LeftArrow);   // back to START
            yield return Key(UnityEngine.InputSystem.Key.Space);
            Assert.AreEqual(1, _log.Count); StringAssert.StartsWith("deep:1:", Last);

            // pad: d-pad moves, A acts (a fresh visit)
            _log.Clear();
            ui.SetMode(UIManager.Mode.None, true); ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            Click("Title/Card/Mode4");
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Pad(GamepadButton.DpadRight); yield return Pad(GamepadButton.South);
            Assert.AreEqual(1, deep.Level, "pad A on DL 1");
            yield return Pad(GamepadButton.DpadLeft); yield return Pad(GamepadButton.South);
            StringAssert.StartsWith("deep:1:", Last);

            // BACK closes it and the title is usable again
            ui.SetMode(UIManager.Mode.None, true); ui.SetMode(UIManager.Mode.Title, true); _log.Clear();
            yield return Frames(2);
            Click("Title/Card/Mode4");
            Click("DeepLayer/Panel/Back");
            Assert.IsFalse(deep.IsOn);
        }

        // ================================================================== boss HUDs
        static Compiler BareCompiler()
        {
            var c = new GameObject("TestCompiler").AddComponent<Compiler>();
            c.scanners = new VisionCone[0]; c.wallGroups = new Transform[0]; c.wallRenderers = new MeshRenderer[0];
            c.hunters = new PatrolHunter[0]; c.compiled = new SplitterIce[0]; c.voids = new GameObject[0];
            return c;
        }

        static ArchitectBoss BareArchitect() => new GameObject("TestArchitectBoss").AddComponent<ArchitectBoss>();
        static object _sink;

        [UnityTest, Timeout(120000)]
        public IEnumerator Hud_Compiler_Nodes_Swap_Telegraph_And_Halving()
        {
            var ui = UIManager.I; var hud = Screen<HudScreen>("_hud");
            ui.SetMode(UIManager.Mode.Room, true);
            ui.TickAll(0.016f);
            Assert.IsFalse(Visible("Hud/Boss"), "no boss: no bar");
            var c = BareCompiler();
            try
            {
                yield return null;
                RoomClock.Time = 1.5f; yield return Frames(2);   // SwapIn = 4.5 s
                ui.TickAll(0.016f);
                Assert.AreEqual("COMPILER · P1 · RECOMPILING", Txt("Hud/Boss/Label"));
                Assert.AreEqual("NODES ○○○○", Txt("Hud/BossInfo/Label"));
                Assert.AreEqual("4.5", Txt("Hud/BossClock/Number"));
                Assert.AreEqual("WALLS SWAP IN", Txt("Hud/BossClock/Caption"));
                Assert.AreEqual(Compiler.Nodes, c.NodesLeft);
                Assert.AreEqual(UIKit.Paper, Find("Hud/BossClock/Number").GetComponent<TMP_Text>().color, "outside the telegraph the countdown is plain");
                yield return Shot("m04_hud_compiler");

                RoomClock.Time = 5.3f; yield return Frames(2); ui.TickAll(0.016f);   // the last second: telegraph
                Assert.AreEqual(UIKit.Danger, Find("Hud/BossClock/Number").GetComponent<TMP_Text>().color, "telegraph: the countdown turns to the threat colour");
                yield return Shot("m04b_hud_compiler_telegraph");

                GameEvents.AccessGranted?.Invoke("n1"); GameEvents.AccessGranted?.Invoke("n2");   // <= 2 nodes left: P2
                yield return Frames(2); ui.TickAll(0.016f);
                StringAssert.Contains("P2", Txt("Hud/Boss/Label"));
                StringAssert.StartsWith("NODES ●●○○", Txt("Hud/BossInfo/Label"));
                GameEvents.AccessGranted?.Invoke("n3"); GameEvents.AccessGranted?.Invoke("n4");   // P3: the arena halves
                RoomClock.Time = 8f; yield return Frames(3); ui.TickAll(0.016f);
                StringAssert.Contains("P3", Txt("Hud/Boss/Label"));
                StringAssert.StartsWith("NODES ●●●●", Txt("Hud/BossInfo/Label"));
                StringAssert.Contains("HALVES", Txt("Hud/BossInfo/Label"), "P3 shows the halving countdown");
            }
            finally { Object.Destroy(c.gameObject); }
            yield return null; ui.TickAll(0.016f);
            Assert.IsFalse(Visible("Hud/BossInfo"), "boss gone: extras hidden");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Hud_Architect_Phases_Gap_Cue_Endings_And_Subtitles()
        {
            var ui = UIManager.I;
            ui.SetMode(UIManager.Mode.Room, true);
            ui.TickAll(0.016f);
            var a = BareArchitect();
            try
            {
                yield return null; ui.TickAll(0.016f);
                Assert.AreEqual("ARCHITECT · P1 · THE GAZE", Txt("Hud/Boss/Label"));
                Assert.AreEqual("STRINGS ○○○", Txt("Hud/BossInfo/Label"));
                Assert.IsTrue(Visible("Hud/Subtitle"), "the opening line is a subtitle");
                Assert.AreEqual(ArchitectLines.Phase[0], Txt("Hud/Subtitle/Text"));
                Assert.AreEqual("ARCHITECT", Txt("Hud/Subtitle/Who"));
                Assert.IsFalse(Visible("Hud/BossClock")); Assert.IsFalse(Visible("Hud/BossChoice"));

                GameEvents.AccessGranted?.Invoke("s1");   // first string: P2 (the boss raises BossEvents.Phase itself)
                yield return null; ui.TickAll(0.016f);
                StringAssert.Contains("P2", Txt("Hud/Boss/Label")); Assert.AreEqual("STRINGS ●○○", Txt("Hud/BossInfo/Label"));
                Assert.AreEqual(ArchitectLines.Phase[1], Txt("Hud/Subtitle/Text"));

                RoomClock.Time = 1f;
                GameEvents.AccessGranted?.Invoke("s2"); GameEvents.AccessGranted?.Invoke("s3");   // P3: survive 30 s
                yield return Frames(2); ui.TickAll(0.016f);
                StringAssert.Contains("DESYNC", Txt("Hud/Boss/Label"));
                Assert.AreEqual("SURVIVE", Txt("Hud/BossClock/Caption"));
                StringAssert.Contains("BOTH LAYERS", Txt("Hud/BossInfo/Label"));
                RoomClock.Time = 11f; yield return Frames(2); ui.TickAll(0.016f);   // 10 s later: ~20 s left
                StringAssert.IsMatch(@"^(19|20)\.\d$", Txt("Hud/BossClock/Number"));
                yield return Shot("m05_hud_architect_survive");

                RoomClock.Time = 41.5f; yield return Frames(2); ui.TickAll(0.016f);   // survived: the final window starts
                StringAssert.Contains("FINAL STRING", Txt("Hud/Boss/Label"));
                Assert.IsTrue(Visible("Hud/BossChoice"), "the two endings are spelled out");
                StringAssert.Contains("EXPOSED", Txt("Hud/BossChoice/Sever")); StringAssert.Contains("MERGED", Txt("Hud/BossChoice/Merge"));
                float t0 = RoomClock.Time;
                RoomClock.Time = t0 + 1.7f; yield return Frames(2); ui.TickAll(0.016f);   // 0.3 s before the first gap
                Assert.IsTrue(Visible("Hud/BossCue")); Assert.AreEqual("READY", Txt("Hud/BossCue/Main"));
                RoomClock.Time = t0 + 2.1f; yield return Frames(2); ui.TickAll(0.016f);   // the gap is open
                Assert.IsTrue(a.GapOpen); Assert.AreEqual("FLIP NOW", Txt("Hud/BossCue/Main"));
                Assert.AreEqual("GAP OPEN", Txt("Hud/BossInfo/Label"));
                Assert.AreEqual(ArchitectLines.Gap, Txt("Hud/Subtitle/Text"));
                yield return Shot("m06_hud_architect_gap");
                RoomClock.Time = t0 + 2.6f; yield return Frames(2); ui.TickAll(0.016f);
                Assert.IsFalse(Visible("Hud/BossCue"), "gap closed: cue off");
                StringAssert.Contains("NEXT GAP IN", Txt("Hud/BossInfo/Label"));

                RoomClock.Time = t0 + 11f; yield return Frames(2); ui.TickAll(0.016f);   // no flip in a gap: Merged
                Assert.AreEqual(4, a.Phase); Assert.AreEqual(BossEvents.Merged, a.EndingName);
                Assert.AreEqual("ARCHITECT · MERGED", Txt("Hud/Boss/Label"));
                Assert.IsFalse(Visible("Hud/BossChoice"));
            }
            finally { Object.Destroy(a.gameObject); }
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Hud_Architect_Offers_The_Desync_Way_And_Names_That_Ending()
        {
            var ui = UIManager.I;
            ui.SetMode(UIManager.Mode.Room, true);
            ui.TickAll(0.016f);
            var a = BareArchitect();
            var glyph = new GameObject("TestDesyncGlyph").transform;
            glyph.SetParent(a.transform, false); glyph.localPosition = ArchitectBoss.DesyncAt; glyph.gameObject.SetActive(false);
            a.desyncGlyph = glyph;
            var player = new GameObject("TestPlayer"); player.transform.position = new Vector3(0f, 0f, -30f);
            PlayerRef.Root = player.transform;
            ArchitectBoss.OfferDesync = true;
            try
            {
                yield return null; ui.TickAll(0.016f);
                RoomClock.Time = 1f;
                GameEvents.AccessGranted?.Invoke("s1"); GameEvents.AccessGranted?.Invoke("s2"); GameEvents.AccessGranted?.Invoke("s3");
                yield return Frames(2); ui.TickAll(0.016f);
                RoomClock.Time = 41.5f; yield return Frames(2); ui.TickAll(0.016f);   // survived: the final window
                Assert.IsTrue(Visible("Hud/BossChoice"));
                Assert.IsTrue(Visible("Hud/BossChoice/Desync"), "the third way is spelled out beside the two endings");
                StringAssert.Contains("DESYNC", Txt("Hud/BossChoice/Desync"));
                StringAssert.Contains("EXPOSED", Txt("Hud/BossChoice/Sever")); StringAssert.Contains("MERGED", Txt("Hud/BossChoice/Merge"));
                yield return Shot("m06b_hud_architect_desync_way");

                player.transform.position = glyph.position - Vector3.up * ArchitectBoss.DesyncAt.y;   // touch the glyph
                RoomClock.Time += 0.1f; yield return Frames(2); ui.TickAll(0.016f);
                Assert.AreEqual(ArchitectBoss.DesyncEnding, a.EndingName);
                Assert.AreEqual("ARCHITECT · DESYNC", Txt("Hud/Boss/Label"));
                Assert.AreEqual("NO LAYER HOLDS YOU", Txt("Hud/BossInfo/Label"));
                Assert.IsFalse(Visible("Hud/BossChoice"));
            }
            finally { ArchitectBoss.OfferDesync = false; PlayerRef.Root = null; Object.Destroy(player); Object.Destroy(a.gameObject); }
            yield return null;
            ui.TickAll(0.016f);

            // not offered: the panel keeps its two lines
            a = BareArchitect();
            try
            {
                yield return null; ui.TickAll(0.016f);
                RoomClock.Time = 1f;
                GameEvents.AccessGranted?.Invoke("s1"); GameEvents.AccessGranted?.Invoke("s2"); GameEvents.AccessGranted?.Invoke("s3");
                yield return Frames(2); ui.TickAll(0.016f);
                RoomClock.Time = 41.5f; yield return Frames(2); ui.TickAll(0.016f);
                Assert.IsTrue(Visible("Hud/BossChoice"));
                Assert.IsFalse(Visible("Hud/BossChoice/Desync"), "no third way until it is earned");
            }
            finally { Object.Destroy(a.gameObject); }
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Hud_Architect_Exposed_Ending_Line()
        {
            var ui = UIManager.I;
            ui.SetMode(UIManager.Mode.Room, true);
            BossEvents.Ending?.Invoke(BossEvents.Exposed);
            ui.TickAll(0.016f);
            Assert.AreEqual(ArchitectLines.Exposed, Txt("Hud/Subtitle/Text"));
            BossEvents.Ending?.Invoke(BossEvents.Merged);
            ui.TickAll(0.016f);
            Assert.AreEqual(ArchitectLines.Merged, Txt("Hud/Subtitle/Text"));
            yield return null;
        }

        // ================================================================== subtitles + decoy marker
        [UnityTest, Timeout(120000)]
        public IEnumerator Subtitles_Show_Fade_And_Honour_The_Setting()
        {
            var ui = UIManager.I; var hud = Screen<HudScreen>("_hud");
            ui.SetMode(UIManager.Mode.Room, true);
            ui.TickAll(0.016f);
            Assert.IsFalse(hud.SubtitleOn);
            Subtitles.Say("ARCHITECT", "TEST LINE", 1f);
            ui.TickAll(0.016f);
            Assert.IsTrue(hud.SubtitleOn); Assert.AreEqual("TEST LINE", hud.SubtitleText);
            for (int i = 0; i < 12; i++) ui.TickAll(0.1f);
            Assert.IsFalse(hud.SubtitleOn, "gone after its time");
            Subtitles.Enabled = false;
            Subtitles.Say("ARCHITECT", "HIDDEN", 1f);
            ui.TickAll(0.016f);
            Assert.IsFalse(hud.SubtitleOn, "SUBTITLES off: nothing is shown");
            Subtitles.Enabled = true;
            Assert.GreaterOrEqual(Subtitles.Seconds, 1f);
            Subtitles.Say("ARCHITECT", new string('X', 200));
            Assert.AreEqual(7f, Subtitles.Seconds, "reading time is capped");
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Hud_Decoy_Marker_Tracks_The_Nearest_Decoy()
        {
            var ui = UIManager.I; var hud = Screen<HudScreen>("_hud");
            ui.SetMode(UIManager.Mode.Room, true);
            var cam = _camGo.GetComponent<Camera>(); PlayerRef.Cam = cam;
            var root = new GameObject("TestPlayer"); root.transform.position = new Vector3(0f, 0f, -4f); PlayerRef.Root = root.transform;
            try
            {
                ui.TickAll(0.016f);
                Assert.IsFalse(hud.DecoyMarkerOn, "no decoy: no marker");
                RoomClock.Time = 0.1f;
                Assert.GreaterOrEqual(Decoys.Spawn(new Vector3(0f, 0f, 6f), 3f, DecoyKind.Silhouette), 0);
                ui.TickAll(0.016f);
                Assert.IsTrue(hud.DecoyMarkerOn);
                Assert.AreEqual("DECOY", Txt("Hud/DecoyMarker/Label"));
                yield return Shot("m11_hud_decoy");
                var rt = (RectTransform)Find("Hud/DecoyMarker").transform;
                Assert.Less(Mathf.Abs(rt.anchoredPosition.x), 40f, "straight ahead: centred");
                Decoys.Spawn(new Vector3(0f, 0f, -3f), 3f, DecoyKind.Echo);   // nearer to the player at z = -4, but behind the camera
                ui.TickAll(0.016f);
                Assert.AreEqual("ECHO", Txt("Hud/DecoyMarker/Label"), "the nearest decoy wins");
                float half = Find("Hud").GetComponent<RectTransform>().rect.width * 0.5f;
                Assert.Greater(Mathf.Abs(rt.anchoredPosition.x), half * 0.8f, "behind the camera: pinned to a screen edge");
                RoomClock.Time = 9f;   // lifetimes run on RoomClock
                ui.TickAll(0.016f);
                Assert.IsFalse(hud.DecoyMarkerOn, "expired decoys leave no marker");
            }
            finally { Decoys.ClearAll(); Object.Destroy(root); PlayerRef.Cam = null; PlayerRef.Root = null; }
            yield return null;
        }

        // ================================================================== exploit tooltips + condition glow
        [UnityTest, Timeout(180000)]
        public IEnumerator Hud_Condition_Glow_Covers_Every_Conditional_Exploit()
        {
            var ui = UIManager.I;
            ui.SetMode(UIManager.Mode.Room, true);
            NewRun(RunPhase.Room);
            int checkedCount = 0;
            foreach (var e in RunView.Cat.exploits)
            {
                string when = RunView.When(e);
                if (when == null) continue;
                // find a state that satisfies the condition and one that does not, scanning the values the grammar can name
                bool haveOn = false, haveOff = false; ConditionContext on = default, off = default;
                foreach (int chain in new[] { 0, 4, 5, 8, 10, 12 })
                    foreach (float melt in new[] { 0f, 50f, 60f, 70f, 75f, 80f, 90f, 100f })
                        foreach (int trace in new[] { 0, 1, 2 })
                            foreach (var layer in new[] { Rules.Layer.Render, Rules.Layer.Source })
                            {
                                var c = new ConditionContext { chain = chain, melt = melt, trace = trace, layer = layer };
                                if (Conditions.Eval(when, c)) { if (!haveOn) { on = c; haveOn = true; } } else if (!haveOff) { off = c; haveOff = true; }
                            }
                Assert.IsTrue(haveOn && haveOff, e.id + " (" + when + "): the condition can be met and missed");
                RunState.Held.Clear(); RunState.Held.Add(e);
                void Apply(ConditionContext c)
                {
                    GameState.Chain = c.chain; GameState.Melt = c.melt; GameState.TraceCount = c.trace; LayerState.Set(c.layer == Rules.Layer.Source);
                    for (int i = 0; i < 3; i++) ui.TickAll(0.016f);
                }
                Apply(off);
                var glow = Find("Hud/Run/Exploit0/Glow").GetComponent<Image>();
                var group = Find("Hud/Run/Exploit0").GetComponent<CanvasGroup>();
                Assert.AreEqual(0f, glow.color.a, 1e-4f, e.id + ": condition off, no glow");
                Assert.AreEqual(0.5f, group.alpha, 1e-4f, e.id + ": waiting for its condition, the chip dims");
                Apply(on);
                Assert.Greater(glow.color.a, 0.1f, e.id + " (" + when + "): condition met, the chip glows");
                Assert.AreEqual(1f, group.alpha, 1e-4f, e.id + ": lit chips are bright");
                checkedCount++;
                yield return null;
            }
            Assert.GreaterOrEqual(checkedCount, 15, "the catalog has many conditional exploits (and duos)");
            LayerState.Set(false);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Map_Loadout_Rows_Are_Tooltips_With_Live_State()
        {
            var ui = UIManager.I; var map = Screen<RunMapScreen>("_map");
            ui.SetMode(UIManager.Mode.Room, true);
            NewRun(RunPhase.Room);
            RunState.Held.Add(X("clean_handshake")); RunState.Held.Add(X("slipstream")); RunState.Held.Add(X("live_wire"));
            map.Show(false);   // the TAB overlay
            ui.TickAll(0.016f);
            Assert.IsTrue(Visible("RunMap/Panel/LoadoutRow2"));
            Assert.AreEqual("· EMPTY SLOT", Txt("RunMap/Panel/LoadoutRow3/Name"));
            StringAssert.Contains("CLEAN HANDSHAKE", Txt("RunMap/Panel/LoadoutRow0/Name"));
            Assert.AreEqual(X("clean_handshake").desc, Txt("RunMap/Panel/LoadoutRow0/Desc"), "the effect text is the tooltip");
            Assert.AreEqual("PASSIVE", Txt("RunMap/Panel/LoadoutRow0/State"));
            Assert.AreEqual("IDLE", Txt("RunMap/Panel/LoadoutRow1/State"), "Slipstream waits for chain >= 4");
            GameState.Chain = 5; ui.TickAll(0.016f);
            Assert.AreEqual("ACTIVE", Txt("RunMap/Panel/LoadoutRow1/State"));
            Assert.AreEqual("IDLE", Txt("RunMap/Panel/LoadoutRow2/State"), "Live Wire waits for max TRACE");
            GameState.TraceCount = 2; ui.TickAll(0.016f);
            Assert.AreEqual("ACTIVE", Txt("RunMap/Panel/LoadoutRow2/State"));
            yield return Shot("m07_map_loadout");
            map.Hide();
            yield return null;
        }

        // ================================================================== colour-blind palette
        [UnityTest, Timeout(120000)]
        public IEnumerator Palette_Toggle_Rebuilds_The_UI_And_Publishes_The_Shader_Globals()
        {
            var ui = UIManager.I;
            ui.SetMode(UIManager.Mode.Room, true);
            ui.TickAll(0.016f);
            var authored = GameConfig.I;
            Assert.AreEqual(authored.cyan, UIKit.Cyan);
            Assert.IsFalse(Palette.Colorblind);
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_Colorblind"));
            Assert.IsFalse(Screen<HudScreen>("_hud").FrameOn, "no layer frame in the authored palette");
            Assert.AreEqual("RENDER", Txt("Hud/LayerTag/Main"));

            Palette.Colorblind = true;               // what the pause-menu toggle does
            GameEvents.SettingsChanged?.Invoke();
            yield return Frames(3);                  // the rebuild runs at the start of the next frame
            Assert.AreEqual(Palette.CbCyan, UIKit.Cyan); Assert.AreEqual(Palette.CbGreen, UIKit.Green);
            Assert.AreEqual(Palette.CbMagenta, UIKit.Magenta); Assert.AreEqual(Palette.CbDanger, UIKit.Danger);
            Assert.AreEqual(1f, Shader.GetGlobalFloat("_Colorblind"));
            var hud = Screen<HudScreen>("_hud");
            Assert.IsTrue(hud.IsOn, "the HUD is back after the rebuild"); Assert.IsTrue(hud.FrameOn, "RENDER: solid frame");
            Assert.AreEqual(UIKit.Safe("■ RENDER", UIKit.Hud), Txt("Hud/LayerTag/Main"), "the tag carries a shape, not only a hue");
            Assert.AreEqual(Palette.CbCyan, Find("Hud/LayerTag/Main").GetComponent<TMP_Text>().color);
            var frame = Find("Hud/LayerFrame").GetComponent<Image>();
            var solidSprite = frame.sprite;
            yield return Shot("m09_hud_cb_render");
            LayerState.Set(true); yield return Frames(3);
            Assert.AreEqual(UIKit.Safe("▒ SOURCE", UIKit.Hud), Txt("Hud/LayerTag/Main"));
            Assert.AreNotEqual(solidSprite, Find("Hud/LayerFrame").GetComponent<Image>().sprite, "SOURCE: dashed frame, RENDER: solid");
            Assert.AreEqual(Palette.CbGreen, Find("Hud/LayerTag/Main").GetComponent<TMP_Text>().color);
            yield return Shot("m10_hud_cb_source");
            LayerState.Set(false);

            Palette.Colorblind = false; GameEvents.SettingsChanged?.Invoke();
            yield return Frames(3);
            Assert.AreEqual(authored.cyan, UIKit.Cyan); Assert.AreEqual(0f, Shader.GetGlobalFloat("_Colorblind"));
            Assert.IsFalse(Screen<HudScreen>("_hud").FrameOn);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Pause_Menu_Toggles_Colorblind_And_Subtitles_And_Survives_The_Rebuild()
        {
            var ui = UIManager.I;
            ui.SetMode(UIManager.Mode.Room, true);
            GameState.Paused = true; GameEvents.Paused?.Invoke(true);
            yield return Frames(2);
            Assert.IsTrue(Visible("Pause/Panel/Colorblind")); Assert.IsTrue(Visible("Pause/Panel/Subtitles"));
            yield return Shot("m08_pause_access");
            var cb = Find("Pause/Panel/Colorblind").GetComponent<Toggle>();
            Assert.IsFalse(cb.isOn);
            cb.isOn = true;   // fires onValueChanged exactly like a click
            Assert.IsTrue(Palette.Colorblind, "the toggle writes the setting");
            yield return Frames(3);
            Assert.AreEqual(Palette.CbCyan, UIKit.Cyan);
            Assert.IsTrue(Visible("Pause/Panel/Colorblind"), "the pause menu is shown again after the rebuild");
            Assert.IsTrue(Find("Pause/Panel/Colorblind").GetComponent<Toggle>().isOn, "and still reads ON");
            var subs = Find("Pause/Panel/Subtitles").GetComponent<Toggle>();
            Assert.IsTrue(subs.isOn); subs.isOn = false;
            Assert.IsFalse(Subtitles.Enabled);
            yield return Frames(2);
            GameState.Paused = false; GameEvents.Paused?.Invoke(false);
        }

        // ================================================================== zero GC
        [UnityTest, Timeout(180000)]
        public IEnumerator Zero_Gc_Hud_With_Bosses_Subtitles_And_Decoy_Marker()
        {
            var ui = UIManager.I;
            ui.SetMode(UIManager.Mode.Room, true);
            NewRun(RunPhase.Room);
            RunState.Held.Add(X("slipstream")); RunState.Held.Add(X("fever_code")); RunState.Held.Add(X("live_wire"));
            DeepRun.Level = 7;
            var cam = _camGo.GetComponent<Camera>(); PlayerRef.Cam = cam;
            var root = new GameObject("TestPlayer"); root.transform.position = new Vector3(0f, 0f, -4f); PlayerRef.Root = root.transform;
            var comp = BareCompiler();
            RoomClock.Time = 0.1f;
            Decoys.Spawn(new Vector3(2f, 0f, 6f), 50f, DecoyKind.Echo);
            Subtitles.Say("ARCHITECT", "KEEP COUNTING.", 100f);
            try
            {
                yield return null;
                const int ticks = 300;
                void Step(int i)
                {
                    RoomClock.Time += 0.016f; GameState.Chain = (i / 20) % 7; GameState.TraceCount = (i / 50) % 3; GameState.Melt = Mathf.Repeat(GameState.Melt + 0.4f, 100f);
                    GameState.Score += 5; GameState.Multiplier = 1f + 0.25f * GameState.Chain;
                }
                Action tick = () => ui.TickAll(0.016f);
                Assert.Greater(Allocs.Count(() => _sink = new byte[64]), 0, "the instrument sees a known allocation");
                // the same ticks from the same inputs twice: the first run warms (first use of pooled pops, tweens, text buffers),
                // the second is counted
                float t0 = RoomClock.Time, m0 = GameState.Melt; int s0 = GameState.Score;
                int Measure()
                {
                    int sum = 0;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        RoomClock.Time = t0; GameState.Melt = m0; GameState.Score = s0;
                        for (int i = 0; i < ticks; i++) { Step(i); if (pass == 0) tick(); else sum += Allocs.Beyond(tick); }
                    }
                    return sum;
                }
                int total = Measure();
                Debug.Log("[UI-TEST] allocations over " + ticks + " HUD ticks with the COMPILER bar, subtitle, decoy marker, DL tag: " + total);
                Assert.AreEqual(0, total, "the M3 HUD allocates per frame (COMPILER)");
                Object.Destroy(comp.gameObject);
                var arch = BareArchitect();
                yield return null;
                GameEvents.AccessGranted?.Invoke("s1"); GameEvents.AccessGranted?.Invoke("s2"); GameEvents.AccessGranted?.Invoke("s3");
                RoomClock.Time += 30.5f; yield return Frames(2);   // survive done: the final window with gaps
                t0 = RoomClock.Time; m0 = GameState.Melt; s0 = GameState.Score;
                total = Measure();
                Debug.Log("[UI-TEST] allocations over " + ticks + " HUD ticks with the ARCHITECT final window: " + total);
                Assert.AreEqual(0, total, "the M3 HUD allocates per frame (ARCHITECT)");
                Object.Destroy(arch.gameObject);
            }
            finally
            {
                if (comp) Object.Destroy(comp.gameObject);
                Decoys.ClearAll(); Object.Destroy(root); PlayerRef.Cam = null; PlayerRef.Root = null; DeepRun.Level = 0;
            }
            yield return null;
        }
    }
}
