using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Desync.Content;
using Desync.Flow;
using Desync.Gen;
using Desync.Rules;
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
    /// BREACH run UI: title mode menu, run map, exploit draft, market, cooldown, run-aware room results, run results and the
    /// HUD run row. Each screen's mouse / keyboard / pad input is checked against the RunEvents it raises; the live run,
    /// room and game managers are switched off so nothing loads. Screenshots go to .work/ui3/shots (best effort).
    public class RunUITests
    {
        static string ShotDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".work", "ui3", "shots"));
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        GameObject _camGo;
        readonly List<MonoBehaviour> _paused = new List<MonoBehaviour>();
        readonly List<string> _log = new List<string>();   // every request the UI raised, in order: "start:Campaign", "choose:a1n0", ...

        // ------------------------------------------------------------------ setup
        [UnitySetUp]
        public IEnumerator Up()
        {
            _camGo = new GameObject("RunTestCam", typeof(Camera));
            var c = _camGo.GetComponent<Camera>(); c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.08f, 0.09f, 0.16f);
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb && mb.enabled && (mb.GetType().Name == "GameManager" || mb.GetType().Name == "RunManager" || mb.GetType().Name == "RoomManager"))
                { mb.enabled = false; _paused.Add(mb); }   // live Flow would act on our requests (switched back on in TearDown)
            ResetRun();
            _log.Clear();
            RunEvents.StartRun += OnStart; RunEvents.ChooseNode += OnChoose; RunEvents.PickExploit += OnPick; RunEvents.RerollOffer += OnReroll;
            RunEvents.Buy += OnBuy; RunEvents.RemoveExploit += OnRemove; RunEvents.ChooseCooldown += OnCool; RunEvents.Leave += OnLeave;
            RunEvents.CashOut += OnCash; RunEvents.GoDeeper += OnDeeper;
            GameEvents.StartRequested += OnStartReq; GameEvents.ContinueRequested += OnContinue; GameEvents.RestartRequested += OnRestart;
            GameEvents.QuitRequested += OnQuit; GameEvents.CopyRequested += OnCopy;
            yield return null;
            Assert.IsNotNull(UIManager.I, "UIManager did not self-bootstrap");
        }

        [TearDown]
        public void Down()
        {
            EndInput();
            RunEvents.StartRun -= OnStart; RunEvents.ChooseNode -= OnChoose; RunEvents.PickExploit -= OnPick; RunEvents.RerollOffer -= OnReroll;
            RunEvents.Buy -= OnBuy; RunEvents.RemoveExploit -= OnRemove; RunEvents.ChooseCooldown -= OnCool; RunEvents.Leave -= OnLeave;
            RunEvents.CashOut -= OnCash; RunEvents.GoDeeper -= OnDeeper;
            GameEvents.StartRequested -= OnStartReq; GameEvents.ContinueRequested -= OnContinue; GameEvents.RestartRequested -= OnRestart;
            GameEvents.QuitRequested -= OnQuit; GameEvents.CopyRequested -= OnCopy;
            ResetRun();
            RunEvents.PhaseChanged?.Invoke(RunPhase.None);
            var ui = UIManager.I;
            if (ui) { ui.SetMode(UIManager.Mode.Title, true); ui.SetMode(UIManager.Mode.None, true); }   // Title mode also closes the run results
            if (_camGo) Object.Destroy(_camGo);
            foreach (var mb in _paused) if (mb) mb.enabled = true;
            _paused.Clear();
        }

        void OnStart(RunMode m) => _log.Add("start:" + m);
        void OnChoose(string id) => _log.Add("choose:" + id);
        void OnPick(int i) => _log.Add("pick:" + i);
        void OnReroll() => _log.Add("reroll");
        void OnBuy(int i) => _log.Add("buy:" + i);
        void OnRemove(string id) => _log.Add("remove:" + id);
        void OnCool(int i) => _log.Add("cool:" + i);
        void OnLeave() => _log.Add("leave");
        void OnCash() => _log.Add("cashout");
        void OnDeeper() => _log.Add("deeper");
        void OnStartReq(int r) => _log.Add("room:" + r);
        void OnContinue() => _log.Add("continue");
        void OnRestart() => _log.Add("restart");
        void OnQuit() => _log.Add("quit");
        void OnCopy(string s) => _log.Add("copy:" + s);

        string Last => _log.Count > 0 ? _log[_log.Count - 1] : "";

        static void ResetRun()
        {
            RunState.Mode = RunMode.Classic; RunState.Phase = RunPhase.None; RunState.Seed = 0; RunState.Graph = null; RunState.NodeId = null; RunState.Act = 1;
            RunState.Held.Clear(); RunState.RoomMutators.Clear(); RunState.Offer.Clear(); RunState.Market.Clear(); RunState.Grades.Clear();
            RunState.Backups = RunState.Cycles = RunState.Rerolls = RunState.Fragments = 0; RunState.MeltCarry = RunState.RunTime = 0f; RunState.RunScore = 0; RunState.Won = false;
            RunView.ResetRun();
            GameState.RoomIndex = -1; GameState.Melt = 0; GameState.Chain = 0; GameState.TraceCount = 0; GameState.Paused = false; GameState.Alive = true;
            GameState.GlitchCountdown = -1f; GameState.TraceActive = false; GameState.InteractPrompt = null; GameState.Score = 0; GameState.Multiplier = 1f;
            RoomClock.Time = 0f; LayerState.Set(false);
        }

        /// A bare AUDITOR (no ring, scanners or hunters) driven to phase 3 by three terminal hacks: uplink countdown running.
        static Desync.Enemies.Auditor AuditorInP3()
        {
            var a = new GameObject("TestAuditor").AddComponent<Desync.Enemies.Auditor>();
            a.scanners = new Desync.Enemies.VisionCone[0]; a.hunters = new Desync.Enemies.PatrolHunter[0]; a.glitchZones = new GameObject[0];
            for (int i = 0; i < 3; i++) GameEvents.AccessGranted?.Invoke("TEST LINE");
            Assert.AreEqual(3, a.Phase);
            return a;
        }

        static Exploit X(string id) { var e = RunView.Cat.Exploit(id); Assert.IsNotNull(e, "catalog lacks " + id); return e; }

        /// A campaign run on `seed`, act 1, before the first choice.
        static void NewRun(uint seed, RunPhase phase)
        {
            RunState.Mode = RunMode.Campaign; RunState.Seed = seed; RunState.Graph = Generator.GenerateRun(seed, 0); RunState.Act = 1; RunState.NodeId = null;
            RunState.Phase = phase;
        }

        static void Phase(RunPhase p) { RunState.Phase = p; RunEvents.PhaseChanged?.Invoke(p); }

        static T Screen<T>(string field) where T : class =>
            typeof(UIManager).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(UIManager.I) as T;

        /// A path lookup also returns inactive children (Transform.Find under the first name): check activeInHierarchy.
        static bool Visible(string path) { var go = GameObject.Find(path); return go && go.activeInHierarchy; }
        static GameObject Find(string path) { Assert.IsTrue(Visible(path), "not found / not visible: " + path); return GameObject.Find(path); }

        static void Click(string path)
        {
            var ev = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(Find(path), ev, ExecuteEvents.pointerDownHandler);
        }

        static void Hover(string path) => ExecuteEvents.Execute(Find(path), new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);

        static string Txt(string path) { var t = Find(path).GetComponent<TMP_Text>(); t.ForceMeshUpdate(); return t.GetParsedText(); }

        // ------------------------------------------------------------------ virtual keyboard + pad (batchmode has no focus)
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

        // ------------------------------------------------------------------ screenshots
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

        // ================================================================== title
        [UnityTest, Timeout(120000)]
        public IEnumerator Title_Menu_Starts_Each_Mode()
        {
            var ui = UIManager.I;
            var title = Screen<TitleScreen>("_title");
            ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            Assert.AreEqual(TitleScreen.DefaultItem, title.Selected, "CLASSIC is highlighted on open");
            yield return Shot("r01_title_menu");

            string[] want = { "start:Campaign", "start:Quick", null, "start:Classic" };
            int[] item = { 0, 1, 3, 2 };
            for (int k = 0; k < item.Length; k++)
            {
                ui.SetMode(UIManager.Mode.None, true); ui.SetMode(UIManager.Mode.Title, true);
                _log.Clear();
                yield return Frames(2);
                Click("Title/Card/Mode" + item[k]);
                if (want[k] == null) Assert.IsEmpty(_log, "DAILY is greyed: no start");
                else Assert.AreEqual(new[] { want[k] }, _log.ToArray(), "mode button " + item[k]);
            }

            // keyboard: the highlight wraps CLASSIC -> CAMPAIGN; SPACE starts it
            ui.SetMode(UIManager.Mode.None, true); ui.SetMode(UIManager.Mode.Title, true); _log.Clear();
            yield return Frames(2);
            yield return Key(UnityEngine.InputSystem.Key.DownArrow);
            Assert.AreEqual(0, title.Selected, "next wraps past DAILY to CAMPAIGN");
            yield return Key(UnityEngine.InputSystem.Key.Space);
            Assert.AreEqual("start:Campaign", Last);

            // pad: d-pad up from CLASSIC = QUICK, A starts it
            ui.SetMode(UIManager.Mode.None, true); ui.SetMode(UIManager.Mode.Title, true); _log.Clear();
            yield return Frames(2);
            yield return Pad(GamepadButton.DpadUp);
            Assert.AreEqual(1, title.Selected);
            yield return Pad(GamepadButton.South);
            Assert.AreEqual("start:Quick", Last);

            // a click on the backdrop starts the highlighted mode (the old "click anywhere" path)
            ui.SetMode(UIManager.Mode.None, true); ui.SetMode(UIManager.Mode.Title, true); _log.Clear();
            yield return Frames(2);
            Click("Title/ClickCatcher");
            Assert.AreEqual("start:Classic", Last);
        }

        // ================================================================== map
        [UnityTest, Timeout(120000)]
        public IEnumerator Map_Routes_Choices_And_Previews_Mutators()
        {
            var ui = UIManager.I;
            var map = Screen<RunMapScreen>("_map");
            uint seed = 1;   // a seed whose row 4 has a room (rows 1-3 roll no mutators to preview)
            while (Generator.GenerateRun(seed, 0).acts[0].FindIndex(n => n.index == 3 && RunView.IsRoom(n.type)) < 0) seed++;
            NewRun(seed, RunPhase.Map);
            RunState.Held.Add(X("clean_handshake")); RunState.Backups = 1; RunState.Cycles = 35;
            Phase(RunPhase.Map);
            yield return Frames(2);
            Assert.IsTrue(map.IsOn && map.Interactive, "Map phase opens the interactive map");
            var nodes = RunState.Graph.acts[0];
            var row0 = new List<string>(); foreach (var n in nodes) if (n.index == 0) row0.Add(n.id);
            CollectionAssert.AreEqual(row0, map.ChoiceIds, "act start: the first row is the choice");

            int later = nodes.FindIndex(n => n.index == 1);
            Click("RunMap/Panel/Graph/Node" + later);
            Assert.IsEmpty(_log, "a node that is not a choice does nothing");

            // hover previews the room mutators the run will roll for that node
            int deep = nodes.FindIndex(n => n.index == 3 && RunView.IsRoom(n.type));
            Hover("RunMap/Panel/Graph/Node" + deep);
            var muts = RunRules.RoomMutators(Rng.Substream(nodes[deep].roomSeed, "mutators"), RunView.Cat, nodes[deep]);
            Assert.Greater(muts.Count, 0, "row 4 rooms roll 1-2 mutators");
            StringAssert.Contains(muts[0].name.ToUpperInvariant(), map.PreviewText);
            yield return Shot("r02_map");

            int first = nodes.FindIndex(n => n.id == row0[0]);
            Click("RunMap/Panel/Graph/Node" + first);
            Assert.AreEqual(new[] { "choose:" + row0[0] }, _log.ToArray());
            Click("RunMap/Panel/Graph/Node" + first);
            Assert.AreEqual(1, _log.Count, "one choice per map visit");

            // keyboard: from a chosen node the next row lights; → then SPACE takes the second choice
            RunState.NodeId = row0[0];
            Phase(RunPhase.Map); _log.Clear();
            CollectionAssert.AreEqual(nodes[first].next, map.ChoiceIds);
            yield return Key(UnityEngine.InputSystem.Key.Space);
            Assert.IsEmpty(_log, "the key that opened the map does not choose (input guard)");
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Key(UnityEngine.InputSystem.Key.RightArrow);
            Assert.AreEqual(1, map.FocusIndex);
            yield return Key(UnityEngine.InputSystem.Key.Space);
            Assert.AreEqual("choose:" + nodes[first].next[1], Last);

            // pad
            Phase(RunPhase.Map); _log.Clear();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Pad(GamepadButton.South);
            Assert.AreEqual("choose:" + nodes[first].next[0], Last);

            // a room phase closes it; TAB overlay is read-only (checked in Tab_Shows_Run_Map_Read_Only)
            Phase(RunPhase.Room);
            Assert.IsFalse(map.IsOn);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Tab_Shows_Run_Map_Read_Only()
        {
            var ui = UIManager.I;
            var map = Screen<RunMapScreen>("_map");
            var goals = Screen<GoalsOverlay>("_goals");
            NewRun(7, RunPhase.Room);
            RunState.NodeId = RunState.Graph.acts[0][0].id;
            ui.SetMode(UIManager.Mode.Room, true);
            BeginInput();
            InputSystem.QueueStateEvent(_kb, new KeyboardState(UnityEngine.InputSystem.Key.Tab));
            yield return Frames(3);
            Assert.IsTrue(map.IsOn && !map.Interactive, "BREACH: TAB holds the run map");
            Assert.IsFalse(goals.IsOn, "not the classic goals overlay");
            Click("RunMap/Panel/Graph/Node1");
            Assert.IsEmpty(_log, "the overlay never chooses");
            yield return Shot("r03_map_tab");
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            yield return Frames(3);
            Assert.IsFalse(map.IsOn, "released: gone");
        }

        // ================================================================== draft
        [UnityTest, Timeout(120000)]
        public IEnumerator Draft_Decrypts_And_Routes_Picks()
        {
            var draft = Screen<DraftScreen>("_draft");
            NewRun(11, RunPhase.Draft);
            RunState.Offer.Add(X("clean_handshake")); RunState.Offer.Add(X("heat_sink")); RunState.Offer.Add(X("audit_trail"));
            RunState.Rerolls = 1;
            Phase(RunPhase.Draft);
            yield return null;
            Assert.IsTrue(draft.IsOn);
            Assert.IsTrue(draft.Decrypting, "cards start encrypted");
            Assert.AreNotEqual(draft.TargetName(0), draft.NameText(0), "name scrambled at t=0");
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("r04_draft_decrypting");
            yield return new WaitForSecondsRealtime(DraftScreen.Decrypt);
            Assert.IsFalse(draft.Decrypting, "decrypt done within 0.8 s");
            for (int i = 0; i < 3; i++) Assert.AreEqual(draft.TargetName(i), draft.NameText(i));
            StringAssert.Contains("HEAT SINK", draft.TargetName(1));
            yield return Shot("r05_draft");

            yield return Key(UnityEngine.InputSystem.Key.Digit2);
            Assert.AreEqual(new[] { "pick:1" }, _log.ToArray(), "key 2 picks the second card");

            // mouse
            Phase(RunPhase.None); Phase(RunPhase.Draft); _log.Clear();
            yield return new WaitForSecondsRealtime(DraftScreen.Decrypt + 0.1f);
            Click("Draft/Panel/Card0");
            Assert.AreEqual("pick:0", Last);

            // skip button, keyboard focus (← from card 1 wraps to SKIP via card 0), pad
            Phase(RunPhase.None); Phase(RunPhase.Draft); _log.Clear();
            yield return new WaitForSecondsRealtime(DraftScreen.Decrypt + 0.1f);
            Click("Draft/Panel/Skip");
            Assert.AreEqual("pick:-1", Last);
            Phase(RunPhase.None); Phase(RunPhase.Draft); _log.Clear();
            yield return new WaitForSecondsRealtime(DraftScreen.Decrypt + 0.1f);
            yield return Key(UnityEngine.InputSystem.Key.LeftArrow);
            yield return Pad(GamepadButton.South);
            Assert.AreEqual("pick:-1", Last, "← from the first card lands on SKIP");

            // reroll: R with a token raises it; a new offer re-decrypts; without a token nothing
            Phase(RunPhase.None); Phase(RunPhase.Draft); _log.Clear();
            yield return new WaitForSecondsRealtime(DraftScreen.Decrypt + 0.1f);
            yield return Key(UnityEngine.InputSystem.Key.R);
            Assert.AreEqual(new[] { "reroll" }, _log.ToArray());
            RunState.Rerolls = 0; RunState.Offer[2] = X("slipstream");
            RunEvents.PhaseChanged?.Invoke(RunPhase.Draft);   // the run re-raises the phase with new cards
            yield return null;
            Assert.IsTrue(draft.Decrypting, "a changed offer decrypts again");
            yield return new WaitForSecondsRealtime(DraftScreen.Decrypt + 0.1f);
            StringAssert.Contains("SLIPSTREAM", draft.NameText(2));
            yield return Key(UnityEngine.InputSystem.Key.R);
            Assert.AreEqual(1, _log.Count, "no token: R does nothing");
        }

        // ================================================================== market + cooldown
        [UnityTest, Timeout(120000)]
        public IEnumerator Market_Buys_Removes_And_Leaves()
        {
            var market = Screen<MarketScreen>("_market");
            NewRun(12, RunPhase.Market);
            RunState.Held.Add(X("clean_handshake")); RunState.Cycles = 50; RunState.Backups = 1;
            RunState.Market.Add(new MarketItem { kind = "exploit", exploitId = "slipstream", price = 25 });
            RunState.Market.Add(new MarketItem { kind = "backup", price = 45 });
            RunState.Market.Add(new MarketItem { kind = "removal", price = 20 });
            RunState.Market.Add(new MarketItem { kind = "reroll", price = 15 });
            RunState.Market.Add(new MarketItem { kind = "exploit", exploitId = "audit_trail", price = 60 });
            Phase(RunPhase.Market);
            yield return Frames(2);
            Assert.IsTrue(market.IsOn);
            yield return Shot("r06_market");

            Click("Market/Panel/Item4");
            Assert.IsEmpty(_log, "60 > 50 Cycles: no buy");
            Click("Market/Panel/Item0");
            Assert.AreEqual(new[] { "buy:0" }, _log.ToArray());

            // the run charges and re-raises the phase: the screen repaints in place (focus kept, SOLD shown)
            var it = RunState.Market[0]; it.sold = true; RunState.Market[0] = it; RunState.Cycles -= 25; RunState.Held.Add(X("slipstream"));
            RunEvents.PhaseChanged?.Invoke(RunPhase.Market);
            yield return Frames(2);
            StringAssert.Contains("SOLD", Txt("Market/Panel/Item0/Foot"));
            Click("Market/Panel/Item0");
            Assert.AreEqual(1, _log.Count, "sold items do nothing");

            Click("Market/Panel/Item2");
            Assert.IsTrue(market.Picking, "removal opens the picker");
            Assert.AreEqual(1, _log.Count, "no event until an exploit is picked");
            yield return Shot("r07_market_remove");
            Click("Market/Panel/RemovePicker/Remove1");
            Assert.AreEqual("remove:slipstream", Last);
            Assert.IsFalse(market.Picking);

            Click("Market/Panel/Leave");
            Assert.AreEqual("leave", Last);

            // keyboard: LEAVE has focus on open; SPACE leaves
            Phase(RunPhase.None); Phase(RunPhase.Market); _log.Clear();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Key(UnityEngine.InputSystem.Key.Space);
            Assert.AreEqual(new[] { "leave" }, _log.ToArray());
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Cooldown_Chooses_And_Continues()
        {
            var cool = Screen<CooldownScreen>("_cooldown");
            NewRun(13, RunPhase.Cooldown);
            RunState.MeltCarry = 37f; RunState.Backups = RunRules.MaxBackups; RunState.Rerolls = 1;
            Phase(RunPhase.Cooldown);
            yield return Frames(2);
            Assert.IsTrue(cool.IsOn);
            StringAssert.Contains("37%", Txt("Cooldown/Panel/Option0/Desc"));
            yield return Shot("r08_cooldown");
            Click("Cooldown/Panel/Option1");
            Assert.IsEmpty(_log, "Backups maxed: +1 BACKUP is unavailable");
            Click("Cooldown/Panel/Option0");
            Assert.AreEqual(new[] { "cool:0" }, _log.ToArray());
            Click("Cooldown/Panel/Option2");
            Assert.AreEqual(1, _log.Count, "one choice per visit");
            Click("Cooldown/Panel/Continue");
            Assert.AreEqual("leave", Last);

            Phase(RunPhase.None); Phase(RunPhase.Cooldown); _log.Clear();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Key(UnityEngine.InputSystem.Key.RightArrow);
            yield return Key(UnityEngine.InputSystem.Key.RightArrow);
            yield return Pad(GamepadButton.South);
            Assert.AreEqual(new[] { "cool:2" }, _log.ToArray(), "→ → then pad A takes the reroll token");
        }

        // ================================================================== results
        static RoomReport Report() => new RoomReport { room = -1, eventsScore = 1800, peakMultiplier = 2.25f, timeBonus = 320, total = 2120, time = 48.3f, medal = Medal.Gold, grade = 'A', nextTarget = "0.8 s to SYNC", shareText = "x" };

        [UnityTest, Timeout(120000)]
        public IEnumerator Room_Results_In_A_Run_Hide_Retry_And_Offer_CashOut()
        {
            var ui = UIManager.I;
            var results = Screen<ResultsScreen>("_results");
            ui.SetMode(UIManager.Mode.Room, true);
            NewRun(14, RunPhase.Results);
            var act1 = RunState.Graph.acts[0];
            RunState.NodeId = act1[act1.Count - 1].id;   // the AUDITOR
            RunState.Fragments = 40;
            GameEvents.RoomReportReady?.Invoke(Report());
            yield return null;
            Assert.AreEqual(2, results.RunKind, "act 1 boss of a campaign: cash out or go deeper");
            StringAssert.Contains("AUDITOR DOWN", Txt("Results/Panel/Head"));
            StringAssert.Contains("CASH OUT", Txt("Results/Panel/ResultsRetry/Label"));
            StringAssert.Contains("GO DEEPER", Txt("Results/Panel/ResultsContinue/Label"));
            results.Skip();
            yield return Frames(2);
            yield return Shot("r09_results_boss");
            yield return Key(UnityEngine.InputSystem.Key.R);
            Assert.IsEmpty(_log, "R never restarts in a run");
            yield return Key(UnityEngine.InputSystem.Key.Digit1);
            Assert.AreEqual(new[] { "cashout" }, _log.ToArray());

            // an ordinary run room: no retry button, SPACE continues
            RunState.NodeId = act1[0].id; _log.Clear();
            GameEvents.RoomReportReady?.Invoke(Report());
            yield return null;
            Assert.AreEqual(1, results.RunKind);
            Assert.IsFalse(Visible("Results/Panel/ResultsRetry"), "no retry in a run (the run owns deaths)");
            StringAssert.Contains(RunView.NodeLabel(act1[0]), Txt("Results/Panel/Head"));
            results.Skip();
            yield return Frames(2);
            yield return Key(UnityEngine.InputSystem.Key.R);
            yield return Key(UnityEngine.InputSystem.Key.Space);
            Assert.AreEqual(new[] { "continue" }, _log.ToArray());

            // QUICK ends at the act 1 boss: one way out
            RunState.Mode = RunMode.Quick; RunState.NodeId = act1[act1.Count - 1].id; _log.Clear();
            GameEvents.RoomReportReady?.Invoke(Report());
            yield return null;
            Assert.AreEqual(3, results.RunKind);
            Assert.IsFalse(Visible("Results/Panel/ResultsRetry"));
            StringAssert.Contains("FINISH RUN", Txt("Results/Panel/ResultsContinue/Label"));

            // CLASSIC keeps retry
            ResetRun(); _log.Clear();
            GameEvents.RoomReportReady?.Invoke(new RoomReport { room = 2, total = 900, time = 40f, grade = 'B' });
            yield return null;
            Assert.AreEqual(0, results.RunKind);
            StringAssert.Contains("RETRY", Txt("Results/Panel/ResultsRetry/Label"));
            results.Hide();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Run_Results_Share_Copy_Title_And_New_Run()
        {
            var rr = Screen<RunResultsScreen>("_runResults");
            NewRun(42, RunPhase.Over);
            RunState.Mode = RunMode.Quick; RunState.Grades.AddRange(new[] { 'S', 'A', 'B' }); RunState.RunTime = 222.5f; RunState.Fragments = 37; RunState.RunScore = 12345;
            RunState.NodeId = RunState.Graph.acts[0][RunState.Graph.acts[0].Count - 1].id; RunState.Won = true;
            RunView.Path.AddRange(new[] { "a1n0", "a1n3", "a1n6", "a1n9", RunState.NodeId }); RunView.BestChain = 7;
            RunEvents.RunEnded?.Invoke();
            yield return null;
            Assert.IsTrue(rr.IsOn);
            Assert.AreEqual(RoomReports.ShareText(DateTime.UtcNow, "S A B", 222.5f, 7), rr.ShareText);
            yield return new WaitForSecondsRealtime(RunResultsScreen.End + 0.1f);
            Assert.AreEqual("+37", Txt("RunResults/Panel/Line0/Value"));
            StringAssert.StartsWith("5", Txt("RunResults/Panel/Line1/Value"), "nodes reached");
            Assert.AreEqual("S A B", Txt("RunResults/Panel/Line2/Value"));
            yield return Shot("r10_run_results");
            yield return Key(UnityEngine.InputSystem.Key.C);
            Assert.AreEqual("copy:" + rr.ShareText, Last);
            yield return Key(UnityEngine.InputSystem.Key.Space);
            Assert.AreEqual("quit", Last);

            Phase(RunPhase.Over); _log.Clear();   // over -> results again (the screen is idempotent)
            UIManager.I.SetMode(UIManager.Mode.Title, true); UIManager.I.SetMode(UIManager.Mode.None, true);
            RunEvents.RunEnded?.Invoke();
            yield return new WaitForSecondsRealtime(RunResultsScreen.End + 0.1f);
            Click("RunResults/Panel/RunNew");
            Assert.AreEqual(new[] { "start:Quick" }, _log.ToArray(), "NEW RUN restarts the same mode");
            Assert.IsFalse(rr.IsOn, "a new run closes the results");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Recap_Says_Backup_Used_Or_Run_Over()
        {
            var ui = UIManager.I;
            Assert.IsNotNull(RunManager.I, "run manager self-bootstraps");
            var lostField = typeof(RunManager).GetField("lost", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            ui.SetMode(UIManager.Mode.Room, true);
            NewRun(17, RunPhase.Room);
            RunState.Backups = 1;   // the run spends the Backup before the recap fires
            try
            {
                GameEvents.DeathRecap?.Invoke(new DeathInfo { cause = DeathCause.Melt, roomTime = 12f });
                yield return null;
                Assert.AreEqual("BACKUP USED · 1 LEFT", Txt("Recap/Card/RunStatus"));
                StringAssert.Contains("REBOOT", Txt("Recap/Card/AnyKey"));
                lostField.SetValue(RunManager.I, true);
                GameEvents.DeathRecap?.Invoke(new DeathInfo { cause = DeathCause.Ice, killerId = "ICE-2", killerPos = Vector3.forward * 4f, roomTime = 30f });
                yield return null;
                Assert.AreEqual("RUN OVER", Txt("Recap/Card/RunStatus"));
                StringAssert.Contains("RUN RESULTS", Txt("Recap/Card/AnyKey"));
                yield return Shot("r13_recap_run_over");
            }
            finally { lostField.SetValue(RunManager.I, false); }
            ResetRun();
            GameEvents.DeathRecap?.Invoke(new DeathInfo { cause = DeathCause.Melt, roomTime = 5f });
            yield return null;
            Assert.IsFalse(Visible("Recap/Card/RunStatus"), "CLASSIC: no run line");
            StringAssert.Contains("REBOOT", Txt("Recap/Card/AnyKey"));
            Screen<RecapScreen>("_recap").Hide();
        }

        // ================================================================== phase routing + HUD
        [UnityTest, Timeout(120000)]
        public IEnumerator Phases_Switch_Screens()
        {
            var ui = UIManager.I;
            var hud = Screen<HudScreen>("_hud"); var map = Screen<RunMapScreen>("_map"); var draft = Screen<DraftScreen>("_draft");
            var market = Screen<MarketScreen>("_market"); var cool = Screen<CooldownScreen>("_cooldown");
            ui.SetMode(UIManager.Mode.Room, true);
            NewRun(15, RunPhase.Room);
            RunState.Offer.Add(X("clean_handshake"));
            Phase(RunPhase.Map);
            Assert.IsTrue(map.IsOn); Assert.IsFalse(hud.IsOn, "node screens replace the HUD");
            Phase(RunPhase.Draft);
            Assert.IsTrue(draft.IsOn); Assert.IsFalse(map.IsOn);
            Phase(RunPhase.Market);
            Assert.IsTrue(market.IsOn); Assert.IsFalse(draft.IsOn);
            Phase(RunPhase.Cooldown);
            Assert.IsTrue(cool.IsOn); Assert.IsFalse(market.IsOn);
            Phase(RunPhase.Room);
            Assert.IsFalse(cool.IsOn); Assert.IsTrue(hud.IsOn, "Room hands back to the HUD");
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Hud_Run_Row_Glows_And_Zero_Gc()
        {
            var ui = UIManager.I;
            ui.SetMode(UIManager.Mode.Room, true);
            NewRun(16, RunPhase.Room);
            var node = RunState.Graph.acts[0].Find(n => n.index == 2);
            RunState.NodeId = node.id;
            RunState.Held.Add(X("clean_handshake")); RunState.Held.Add(X("slipstream")); RunState.Held.Add(X("heat_sink")); RunState.Held.Add(X("audit_trail"));
            foreach (var m in RunView.Cat.mutators) if (m.runMutator && RunState.RoomMutators.Count < 2 && (RunState.RoomMutators.Count == 0 || RunState.RoomMutators[0].tag != m.tag)) RunState.RoomMutators.Add(m);
            RunState.Backups = 1;
            for (int i = 0; i < 3; i++) ui.TickAll(0.016f);
            for (int i = 0; i < 4; i++) Find("Hud/Run/Exploit" + i);
            Assert.IsFalse(Visible("Hud/Run/Exploit4"), "4 held: 4 chips");
            Find("Hud/Run/Mutator0"); Find("Hud/Run/Mutator1");
            Assert.AreEqual("BACKUP ●○", Txt("Hud/Run/Backups"));
            StringAssert.StartsWith(RunView.NodeLabel(node), Txt("Hud/Left/Room"));

            var glow = Find("Hud/Run/Exploit1/Glow").GetComponent<Image>();   // Slipstream: chain >= 4
            GameState.Chain = 0; for (int i = 0; i < 3; i++) ui.TickAll(0.016f);
            Assert.AreEqual(0f, glow.color.a, 1e-4f, "condition off: no glow");
            GameState.Chain = 5; for (int i = 0; i < 3; i++) ui.TickAll(0.016f);
            Assert.Greater(glow.color.a, 0.1f, "chain >= 4: Slipstream glows");
            Assert.AreEqual(0f, Find("Hud/Run/Exploit0/Glow").GetComponent<Image>().color.a, 1e-4f, "unconditional exploits never glow");
            Assert.AreEqual(5, RunView.BestChain, "the HUD records the run's best chain for the share text");
            GameState.TraceCount = 2; RoomClock.Time = 31.4f; GameState.Melt = 64f;
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("r11_hud_run");

            // the boss bar and the tracker cue are part of the measured HUD
            var aud = AuditorInP3();
            PlayerRef.Cam = _camGo.GetComponent<Camera>();
            Desync.Enemies.EnemyEvents.TrackerSpawned?.Invoke(new Vector3(-6f, 0f, 2f));
            GameState.TraceActive = true; GameState.TraceRemaining = 5f;

            long total = 0; const int ticks = 240;
            for (int i = 0; i < 30; i++) ui.TickAll(0.016f);
            for (int i = 0; i < ticks; i++)
            {
                RoomClock.Time += 0.016f; GameState.Chain = (i / 20) % 7; GameState.TraceCount = (i / 50) % 3; GameState.Melt = Mathf.Repeat(GameState.Melt + 0.4f, 100f);
                long a = GC.GetTotalMemory(false);
                ui.TickAll(0.016f);
                long b = GC.GetTotalMemory(false);
                if (b > a) total += b - a;
            }
            Debug.Log("[UI-TEST] managed growth over " + ticks + " BREACH HUD ticks: " + total + " bytes");
            Object.Destroy(aud.gameObject); PlayerRef.Cam = null;
            Assert.Less(total / (float)ticks, 64f, "BREACH HUD allocates per frame");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Hud_Auditor_Bar_And_Tracker_Cue()
        {
            var ui = UIManager.I;
            ui.SetMode(UIManager.Mode.Room, true);
            ui.TickAll(0.016f);
            Assert.IsFalse(Visible("Hud/Boss"), "no AUDITOR: no bar");
            var a = new GameObject("TestAuditor").AddComponent<Desync.Enemies.Auditor>();
            a.scanners = new Desync.Enemies.VisionCone[0]; a.hunters = new Desync.Enemies.PatrolHunter[0]; a.glitchZones = new GameObject[0];
            try
            {
                ui.TickAll(0.016f);
                Assert.AreEqual("AUDITOR · P1 · SCANNING", Txt("Hud/Boss/Label"));
                GameEvents.AccessGranted?.Invoke("TEST LINE");
                ui.TickAll(0.016f);
                StringAssert.Contains("P2", Txt("Hud/Boss/Label"));
                GameEvents.AccessGranted?.Invoke("TEST LINE"); GameEvents.AccessGranted?.Invoke("TEST LINE");
                yield return null;
                RoomClock.Time += 0.5f;   // the AUDITOR runs on RoomClock
                yield return Frames(2);
                StringAssert.IsMatch(@"^AUDITOR · UPLINK 19\.\ds$", Txt("Hud/Boss/Label"), "P3: uplink countdown from Auditor.UplinkRemaining");
                yield return Shot("r12_hud_auditor");

                PlayerRef.Cam = _camGo.GetComponent<Camera>();   // at the origin, looking +z
                Desync.Enemies.EnemyEvents.TrackerSpawned?.Invoke(new Vector3(8f, 0f, 0.5f));
                ui.TickAll(0.016f);
                var rt = (RectTransform)Find("Hud/TrackerCue").transform;
                Assert.Greater(rt.anchoredPosition.x, 250f, "the cue points right, toward the nest");
                for (int i = 0; i < 20; i++) ui.TickAll(0.1f);
                Assert.IsFalse(Visible("Hud/TrackerCue"), "the cue fades after 1.6 s");
            }
            finally { Object.Destroy(a.gameObject); PlayerRef.Cam = null; }
            yield return null;
            ui.TickAll(0.016f);
            Assert.IsFalse(Visible("Hud/Boss"), "the AUDITOR gone: bar hidden");
        }
    }
}
