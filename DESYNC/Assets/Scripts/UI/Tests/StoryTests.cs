using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Desync.Net;
using Desync.TestKit;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Desync.UI.Tests
{
    /// STORY mode: the script's pacing, the first-time prompt (once per player, both answers, mouse / keyboard / pad), the title's
    /// STORY entry, skipping at every beat (ESC, pad Start, held SPACE), REDUCE FLASHING, the Room 1 story prompts, the wake-up
    /// into Room_01 and zero GC per frame during the cinematic. A throwaway save profile; Flow's GameManager stays off except in
    /// the test that wakes up in Room_01. Screenshots go to .work/story/shots (best effort).
    public class StoryTests
    {
        const string SaveKey = "desync.test.story";
        readonly List<int> _starts = new List<int>();   // StartRequested(room) raised by the UI
        readonly List<MonoBehaviour> _paused = new List<MonoBehaviour>();
        GameObject _camGo;

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static T Screen<T>(string field) where T : class =>
            typeof(UIManager).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(UIManager.I) as T;

        static void Click(string path)
        {
            var go = GameObject.Find(path);
            Assert.IsTrue(go && go.activeInHierarchy, "not found / not visible: " + path);
            ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerDownHandler);
        }

        static void Advance(StoryScreen story, float to) { while (story.Clock < to && !story.Finished) UIManager.I.TickAll(0.05f); }

        void OnStart(int room) => _starts.Add(room);

        // ------------------------------------------------------------------ setup
        [UnitySetUp]
        public IEnumerator Up()
        {
            _camGo = new GameObject("StoryTestCam", typeof(Camera));
            var c = _camGo.GetComponent<Camera>(); c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.035f, 0.04f, 0.11f);
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb && mb.enabled && mb.GetType().Name == "GameManager") { mb.enabled = false; _paused.Add(mb); }   // no real scene loads on StartRequested
            SaveService.Unload(SaveKey); PlayerPrefs.DeleteKey(SaveKey);
            StoryGuide.Armed = false; LayerState.Set(false);
            _starts.Clear(); GameEvents.StartRequested += OnStart;
            yield return null;
            Assert.IsNotNull(UIManager.I, "UIManager did not self-bootstrap");
        }

        [TearDown]
        public void Down()
        {
            EndInput();
            LogAssert.ignoreFailingMessages = false;
            GameEvents.StartRequested -= OnStart;
            var ui = UIManager.I;
            if (ui) ui.SetMode(UIManager.Mode.None, true);
            StoryGuide.Armed = false; LayerState.Set(false); GameState.Pulse = 0f; Time.timeScale = 1f;
            GameState.RoomIndex = -1; GameState.Melt = 0f; GameState.InHack = false; GameState.ExitOpen = false; GameState.Paused = false;
            GameState.TerminalsDone = GameState.TerminalsTotal = 0; GameState.InteractPrompt = null; GameState.Alive = true;
            if (_camGo) Object.Destroy(_camGo);
            foreach (var mb in _paused) if (mb) mb.enabled = true;
            _paused.Clear();
            PlayerPrefs.DeleteKey(SaveKey); PlayerPrefs.DeleteKey(SaveKey + ".backup");
            SaveService.Unload();
        }

        // ------------------------------------------------------------------ input (virtual devices: batchmode has no focus)
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

        // ------------------------------------------------------------------ screenshots (.work/story/shots, best effort)
        static string ShotDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".work", "story", "shots"));
        static RenderTexture _rt;
        IEnumerator Shot(string name, Camera cam = null)
        {
            var ui = UIManager.I;
            if (!cam) cam = _camGo.GetComponent<Camera>();
            if (!_rt) _rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var cs = ui.Canvas.GetComponent<CanvasScaler>();
            var mode = cs.uiScaleMode; float scale = cs.scaleFactor; var rmode = ui.Canvas.renderMode; var wcam = ui.Canvas.worldCamera;
            cam.targetTexture = _rt;
            cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; cs.scaleFactor = 1f;
            ui.Canvas.renderMode = RenderMode.ScreenSpaceCamera; ui.Canvas.worldCamera = cam; ui.Canvas.planeDistance = Mathf.Max(cam.nearClipPlane + 0.01f, 0.1f);
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
            catch (Exception e) { Debug.LogWarning("[STORY-TEST] shot " + name + " not written: " + e.Message); }
            Object.Destroy(tex);
            cam.targetTexture = null;
            ui.Canvas.renderMode = rmode; ui.Canvas.worldCamera = wcam; cs.uiScaleMode = mode; cs.scaleFactor = scale;
        }

        // ================================================================== script
        static void Caption(string text, string kind, float hold)
        {
            Assert.IsFalse(string.IsNullOrEmpty(text), kind + " caption is empty");
            Assert.LessOrEqual(StoryScript.Words(text), StoryScript.MaxWords, "over 12 words: " + text);
            Assert.GreaterOrEqual(hold + 1e-4f, StoryScript.MinHold(text, kind), "not on screen long enough to type and read (0.3 s a word): " + text);
        }

        [Test]
        public void Script_Is_Short_Punchy_And_Readable()
        {
            Assert.AreEqual(4, StoryScript.Words("HALCYON CORP // DAY 4,382"), "separators are not words");
            var s = StoryScript.I;
            Assert.Greater(s.beats.Length, 20, "story.json beats");
            Assert.Greater(s.shots.Length, 4, "story.json shots");
            Assert.That(s.Length, Is.InRange(60f, 90f), "the story runs 60-90 s before PRESS SPACE TO WAKE UP");
            float prev = -1f, lastFlash = -99f;
            var all = new List<string>();
            foreach (var b in s.beats)
            {
                Assert.GreaterOrEqual(b.t, prev, "beats are in time order"); prev = b.t;
                Assert.AreEqual(0f, Mathf.Repeat(b.t * 2f, 1f), 1e-4f, "beat on the 120 bpm grid: " + b.kind + " at " + b.t);
                all.Add(b.text); all.Add(b.sub);
                switch (b.kind)
                {
                    case "head": case "sys": Caption(b.text, b.kind, b.hold); break;
                    case "goal": Caption(b.text, "goal", b.hold); break;
                    case "card": case "panel": Caption(b.text, b.kind, b.hold); Caption(b.sub, "body", b.hold); break;
                    case "logo": case "wake": Assert.IsNotEmpty(b.text); break;
                    case "layer": case "flicker":
                        Assert.GreaterOrEqual(b.t - lastFlash, StoryScript.MinFlashGap, "full-screen flashes at most one per 2 s (" + b.t + ")");
                        lastFlash = b.t; break;
                    case "clear": break;
                    default: Assert.Fail("unknown beat kind " + b.kind); break;
                }
            }
            prev = -1f;
            foreach (var sh in s.shots) { Assert.Greater(sh.t, prev); Assert.Greater(sh.dur, 0f); prev = sh.t; }
            Assert.AreEqual(0f, s.shots[0].t, "the camera moves from the first frame");
            foreach (var line in new[] { "You look at a wall.", "And it looks back.", "Code. Everywhere.", "EMPLOYEE E-07 · HALCYON CORPORATION · CLEARANCE: NONE",
                                         "One key. Two realities. Nowhere to hide.", "DESYNC", "The world is programmed. Your freedom isn't.", "PRESS SPACE TO WAKE UP" })
                CollectionAssert.Contains(all, line, "the story keeps the pitch line");
            foreach (var line in new[] { s.guide.flip, s.guide.melt, s.guide.terminal, s.guide.hack, s.guide.exit, s.ask.title, s.ask.sub, s.skipHint })
            {
                Assert.IsFalse(string.IsNullOrEmpty(line));
                Assert.LessOrEqual(StoryScript.Words(line), StoryScript.MaxWords, line);
            }
            Assert.AreEqual("FIRST TIME IN THE SIMULATION?", s.ask.title);
            Assert.AreEqual("WATCH THE STORY", s.ask.watch);
            Assert.AreEqual("SKIP · JUST PLAY", s.ask.skip);
        }

        // ================================================================== first-time prompt
        [UnityTest, Timeout(120000)]
        public IEnumerator First_Time_Prompt_Appears_Once_Per_Player()
        {
            var ui = UIManager.I; var ask = Screen<StoryAsk>("_ask"); var title = Screen<TitleScreen>("_title"); var story = Screen<StoryScreen>("_story");
            ui.SetMode(UIManager.Mode.Title, true); ui.TitleEntered();
            yield return Frames(2);
            Assert.IsTrue(ask.IsOn, "a new player is asked");
            Assert.AreEqual(0, ask.Selected, "WATCH THE STORY is the default");
            Assert.IsTrue(title.Blocked, "the menu ignores its keys under the prompt");
            yield return new WaitForSecondsRealtime(0.7f);
            yield return Shot("s00_first_time_prompt");

            yield return Key(UnityEngine.InputSystem.Key.RightArrow);
            Assert.AreEqual(1, ask.Selected, "arrows move to SKIP");
            yield return Key(UnityEngine.InputSystem.Key.Space);
            Assert.IsFalse(ask.IsOn, "answered");
            Assert.IsFalse(story.IsOn, "SKIP plays no story");
            Assert.IsEmpty(_starts, "SKIP leaves the player on the title menu (the same SPACE does not start a mode)");
            Assert.IsTrue(title.IsOn); Assert.IsFalse(title.Blocked);
            Assert.AreEqual("1", SaveService.Data.settings[StoryAsk.SeenKey]);

            SaveService.Unload(SaveKey);   // reload the profile from PlayerPrefs: the answer was written
            Assert.IsFalse(StoryAsk.ShouldAsk(), "remembered in the save");
            ui.SetMode(UIManager.Mode.None, true); ui.SetMode(UIManager.Mode.Title, true); ui.TitleEntered();
            yield return Frames(2);
            Assert.IsFalse(ask.IsOn, "asked once per player");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Watch_And_The_Story_Menu_Entry_Play_The_Story()
        {
            var ui = UIManager.I; var ask = Screen<StoryAsk>("_ask"); var title = Screen<TitleScreen>("_title"); var story = Screen<StoryScreen>("_story");
            // mouse: WATCH
            ui.SetMode(UIManager.Mode.Title, true); ui.TitleEntered();
            yield return Frames(2);
            Click("StoryAsk/Box/Watch");
            Assert.IsTrue(story.IsOn, "WATCH plays the story");
            Assert.IsFalse(ask.IsOn); Assert.IsFalse(title.IsOn, "the cinematic replaces the title card");
            Assert.AreEqual("1", SaveService.Data.settings[StoryAsk.SeenKey], "WATCH is remembered too");
            Assert.IsEmpty(_starts, "watching starts no room yet");

            // pad: A on a fresh profile = the default, WATCH
            ui.SetMode(UIManager.Mode.None, true);
            Assert.IsFalse(story.IsOn, "a mode change stops the story");
            PlayerPrefs.DeleteKey(SaveKey); SaveService.Unload(SaveKey);
            ui.SetMode(UIManager.Mode.Title, true); ui.TitleEntered();
            yield return Frames(2);
            Assert.IsTrue(ask.IsOn);
            yield return Pad(GamepadButton.South);
            Assert.IsTrue(story.IsOn, "pad A watches");

            // title menu: STORY by click ...
            ui.SetMode(UIManager.Mode.None, true); ui.SetMode(UIManager.Mode.Title, true); ui.TitleEntered();
            yield return Frames(2);
            Assert.IsFalse(ask.IsOn, "answered: no prompt");
            yield return Shot("s00_title_menu_story");
            Click("Title/Card/Mode" + TitleScreen.StoryItem);
            Assert.IsTrue(story.IsOn, "STORY replays it");

            // ... and by keyboard: CLASSIC -> up QUICK -> up STORY, SPACE
            ui.SetMode(UIManager.Mode.None, true); ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            Assert.AreEqual(TitleScreen.DefaultItem, title.Selected);
            yield return Key(UnityEngine.InputSystem.Key.UpArrow);
            Assert.AreEqual(1, title.Selected);
            yield return Key(UnityEngine.InputSystem.Key.UpArrow);
            Assert.AreEqual(TitleScreen.StoryItem, title.Selected);
            yield return Key(UnityEngine.InputSystem.Key.Space);
            Assert.IsTrue(story.IsOn, "SPACE on STORY plays it");
            Assert.IsFalse(story.Finished, "the SPACE that started it is not a skip");
            Assert.IsEmpty(_starts);
        }

        // ================================================================== skipping
        [UnityTest, Timeout(300000)]
        public IEnumerator Skip_Works_At_Every_Beat()
        {
            var ui = UIManager.I; var story = Screen<StoryScreen>("_story");
            var times = new List<float>();
            foreach (var b in StoryScript.I.beats) if (times.Count == 0 || times[times.Count - 1] != b.t) times.Add(b.t);
            for (int i = 0; i < times.Count; i++)
            {
                ui.SetMode(UIManager.Mode.None, true); ui.SetMode(UIManager.Mode.Title, true); _starts.Clear(); StoryGuide.Armed = false;
                Click("Title/Card/Mode" + TitleScreen.StoryItem);
                Advance(story, times[i] + 0.01f);
                Assert.IsFalse(story.Finished, "still playing at " + times[i]);
                if (i % 2 == 0) yield return Key(UnityEngine.InputSystem.Key.Escape); else yield return Pad(GamepadButton.Start);
                Assert.IsTrue(story.Finished, (i % 2 == 0 ? "ESC" : "pad Start") + " skips at " + times[i]);
                Assert.AreEqual(new[] { 0 }, _starts.ToArray(), "skip goes straight to Room 1, once");
                Assert.IsFalse(LayerState.InSource, "back in RENDER");
                Assert.AreEqual(0f, GameState.Pulse, "no flip pulse left behind");
                Assert.IsTrue(StoryGuide.Armed, "Room 1 will show the story prompts");
            }

            // held SPACE: only a press that starts during the story counts
            ui.SetMode(UIManager.Mode.None, true); _starts.Clear();
            BeginInput();
            InputSystem.QueueStateEvent(_kb, new KeyboardState(UnityEngine.InputSystem.Key.Space));
            yield return Frames(2);
            ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            Click("Title/Card/Mode" + TitleScreen.StoryItem);
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.IsFalse(story.Finished, "a SPACE already held when the story began does not skip it");
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            yield return Frames(2);
            InputSystem.QueueStateEvent(_kb, new KeyboardState(UnityEngine.InputSystem.Key.Space));
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsFalse(story.Finished, "a tap is not a skip");
            yield return Shot("s09_hold_to_skip");
            yield return new WaitForSecondsRealtime(0.9f);
            Assert.IsTrue(story.Finished, "holding SPACE skips");
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            yield return Frames(2);
            Assert.AreEqual(new[] { 0 }, _starts.ToArray());

            // the end: PRESS SPACE TO WAKE UP
            ui.SetMode(UIManager.Mode.None, true); ui.SetMode(UIManager.Mode.Title, true); _starts.Clear();
            Click("Title/Card/Mode" + TitleScreen.StoryItem);
            Advance(story, StoryScript.I.Length + 0.5f);
            Assert.IsTrue(story.Waking, "waits for the player at the end");
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.IsFalse(story.Finished, "and keeps waiting");
            Assert.IsEmpty(_starts);
            yield return Key(UnityEngine.InputSystem.Key.Space);
            Assert.IsTrue(story.Finished);
            Assert.AreEqual(new[] { 0 }, _starts.ToArray(), "SPACE wakes up in Room 1");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Reduce_Flashing_Drops_The_Flicker_And_The_Flash()
        {
            var ui = UIManager.I; var story = Screen<StoryScreen>("_story");
            bool prev = Settings.ReduceFlashes;
            Settings.ReduceFlashes = true; UIKit.RefreshSettings();
            try
            {
                float flicker = -1f, source = -1f;
                foreach (var b in StoryScript.I.beats)
                {
                    if (b.kind == "flicker" && flicker < 0f) flicker = b.t;
                    if (b.kind == "layer" && b.side == "source" && source < 0f) source = b.t;
                }
                ui.SetMode(UIManager.Mode.Title, true);
                Click("Title/Card/Mode" + TitleScreen.StoryItem);
                Advance(story, flicker + 0.02f);
                Assert.IsFalse(LayerState.InSource, "no SOURCE glimpse under REDUCE FLASHING");
                Assert.AreEqual(0f, story.FlashAlpha, 1e-3f, "no flash overlay");
                Advance(story, source + 0.02f);
                Assert.IsTrue(LayerState.InSource, "the story's layer flips still happen");
                Assert.AreEqual(0f, story.FlashAlpha, 1e-3f, "without the flash");
            }
            finally { Settings.ReduceFlashes = prev; UIKit.RefreshSettings(); }
            yield return null;
        }

        // ================================================================== Room 1 story prompts
        static void Room1(int terminalsDone = 0)
        {
            GameState.RoomIndex = 0; GameState.Phase = GameState.PhaseKind.Playing; GameState.Alive = true; GameState.Paused = false;
            GameState.Melt = 0f; GameState.InHack = false; GameState.ExitOpen = false; GameState.InteractPrompt = null;
            GameState.TerminalsDone = terminalsDone; GameState.TerminalsTotal = 1;
            LayerState.Set(false);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Room_1_Story_Prompts_Show_In_Turn_And_Fade_When_Satisfied()
        {
            var ui = UIManager.I; var guide = Screen<StoryGuide>("_guide"); var g = StoryScript.I.guide;
            void Tick() => ui.TickAll(0.016f);

            Room1(); GameEvents.RoomStarted?.Invoke(0); Tick();
            Assert.IsFalse(guide.IsOn, "classic play (no story first) is unchanged");

            StoryGuide.Armed = true; Room1(); GameEvents.RoomStarted?.Invoke(0); Tick();
            Assert.IsTrue(guide.IsOn);
            Assert.AreEqual(StoryGuide.Step.Flip, guide.Shown); Assert.AreEqual(g.flip, guide.Line);
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("s10_guide_flip");

            LayerState.Set(true); GameState.Melt = 20f; Tick();
            Assert.AreEqual(StoryGuide.Step.None, guide.Shown, "SPACE pressed: the prompt fades");
            GameState.Melt = 45f; Tick();
            Assert.AreEqual(StoryGuide.Step.Melt, guide.Shown, "melt over 40 in SOURCE"); Assert.AreEqual(g.melt, guide.Line);
            LayerState.Set(false); Tick();
            Assert.AreEqual(StoryGuide.Step.None, guide.Shown, "back in RENDER: fades");
            LayerState.Set(true); Tick();
            Assert.AreEqual(StoryGuide.Step.None, guide.Shown, "each prompt is taught once");
            LayerState.Set(false);

            GameState.InteractPrompt = "E  HACK"; Tick();
            Assert.AreEqual(StoryGuide.Step.Terminal, guide.Shown, "at the terminal"); Assert.AreEqual(g.terminal, guide.Line);
            GameState.InHack = true; Tick();
            Assert.AreEqual(StoryGuide.Step.Hack, guide.Shown, "in the hack"); Assert.AreEqual(g.hack, guide.Line);
            GameState.Paused = true; Tick();
            Assert.AreEqual(StoryGuide.Step.None, guide.Shown, "nothing over the pause menu");
            GameState.Paused = false; Tick();
            Assert.AreEqual(StoryGuide.Step.Hack, guide.Shown);
            GameState.InHack = false; GameState.InteractPrompt = null; GameState.TerminalsDone = 1; GameState.ExitOpen = true; Tick();
            Assert.AreEqual(StoryGuide.Step.Exit, guide.Shown, "access granted"); Assert.AreEqual(g.exit, guide.Line);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("s11_guide_exit");
            for (int i = 0; i < 300; i++) Tick();
            Assert.AreEqual(StoryGuide.Step.None, guide.Shown, "the last prompt fades on its own");

            // a REBOOT keeps what was learned; the terminal prompts come back for the reset terminal
            Room1(); Tick();
            Assert.AreEqual(StoryGuide.Step.None, guide.Shown, "SPACE was already learned");
            GameState.InteractPrompt = "E  HACK"; Tick();
            Assert.AreEqual(StoryGuide.Step.Terminal, guide.Shown);

            GameEvents.RoomStarted?.Invoke(1); Tick();
            Assert.IsFalse(guide.IsOn, "Room 2 has no story prompts");
            Assert.IsFalse(StoryGuide.Armed, "leaving Room 1 disarms them");
        }

        // ================================================================== the whole thing, in the real scenes
        [UnityTest, Timeout(300000)]
        public IEnumerator Story_Plays_In_The_Office_And_Wakes_Up_In_Room_01_With_Prompts()
        {
            var ui = UIManager.I; var story = Screen<StoryScreen>("_story"); var ask = Screen<StoryAsk>("_ask"); var guide = Screen<StoryGuide>("_guide");
            foreach (var mb in _paused) if (mb) mb.enabled = true;   // GameManager loads the room on StartRequested, as in the game
            LogAssert.ignoreFailingMessages = true;                   // other workstreams' scenes may log; story problems are asserted
            try
            {
                SceneManager.LoadScene(SceneFlow.Title);
                yield return Frames(3);
                Assert.AreEqual(UIManager.Mode.Title, ui.Current);
                Assert.IsTrue(ask.IsOn, "a fresh profile is asked when the Title scene opens");
                Click("StoryAsk/Box/Watch");
                var cam = Camera.main;
                Assert.IsNotNull(cam, "Title has its camera");
                var p0 = cam.transform.position;
                yield return new WaitForSecondsRealtime(1.2f);
                Assert.Greater((cam.transform.position - p0).sqrMagnitude, 1e-4f, "the camera moves through the office");
                yield return Shot("s01_open", cam);

                var marks = new (float t, string name)[]
                {
                    (7.5f, "s02_wake_up"), (15.0f, "s03_wall"), (20.2f, "s04_code_everywhere"), (29.5f, "s05_echo_card"),
                    (51.0f, "s06_two_layers"), (57.0f, "s07_threats"), (68.5f, "s08_goals"), (78.5f, "s09_logo"),
                };
                foreach (var (t, name) in marks)
                {
                    Advance(story, t);
                    yield return new WaitForSecondsRealtime(0.5f);   // typewriters and the camera tween catch up
                    if (name == "s04_code_everywhere") Assert.IsTrue(LayerState.InSource, "Code. Everywhere. = SOURCE");
                    yield return Shot(name, cam);
                }
                Advance(story, StoryScript.I.Length + 0.1f);
                Assert.IsTrue(story.Waking);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("s10_press_space", cam);

                yield return Key(UnityEngine.InputSystem.Key.Space);
                Assert.AreEqual(new[] { 0 }, _starts.ToArray(), "PRESS SPACE TO WAKE UP starts Room 1");
                for (float w = 0f; w < 10f && !(ui.Current == UIManager.Mode.Room && guide.IsOn); w += Time.unscaledDeltaTime) yield return null;
                foreach (var mb in _paused) if (mb) mb.enabled = false;   // the room is in: no pointer-lock pause in batchmode from here on
                if (GameState.Paused) { GameState.Paused = false; Time.timeScale = 1f; GameEvents.Paused?.Invoke(false); }
                Assert.AreEqual(SceneFlow.Room(0), SceneManager.GetActiveScene().name, "the story ends in Room_01");
                Assert.AreEqual(UIManager.Mode.Room, ui.Current);
                Assert.IsFalse(story.IsOn);
                Assert.IsTrue(guide.IsOn, "with the story prompts");
                yield return new WaitForSecondsRealtime(0.5f);
                Assert.AreEqual(StoryGuide.Step.Flip, guide.Shown, "\"That wall is a lie. Press SPACE.\" at spawn");
                yield return Shot("s11_room01_spawn", Camera.main);

                LayerState.Set(true);   // in the code: the flip prompt fades, melt climbs past 40 %
                for (float w = 0f; w < 8f && guide.Shown != StoryGuide.Step.Melt; w += Time.unscaledDeltaTime) yield return null;
                Assert.AreEqual(StoryGuide.Step.Melt, guide.Shown, "melt > 40 % in SOURCE: back to RENDER");
                yield return Shot("s12_room01_melt", Camera.main);
                LayerState.Set(false);
                yield return Frames(3);
                Assert.AreEqual(StoryGuide.Step.None, guide.Shown, "back in RENDER: the prompt fades");
            }
            finally
            {
                foreach (var mb in _paused) if (mb) mb.enabled = false;
                Cursor.lockState = CursorLockMode.None;
                SceneManager.LoadScene(SceneFlow.Ending);   // leave no live room behind for the next fixture
            }
            yield return Frames(3);
            LogAssert.ignoreFailingMessages = false;
        }

        // ================================================================== the flip key as bound
        [UnityTest, Timeout(120000)]
        public IEnumerator Prompts_That_Mean_Flip_Name_The_Bound_Key()
        {
            var ui = UIManager.I; var story = Screen<StoryScreen>("_story"); var guide = Screen<StoryGuide>("_guide"); var endings = Screen<EndingSequence>("_endingSeq");
            var skipOf = (Func<object, string>)(o => ((TMPro.TMP_Text)o.GetType().GetField("_skip", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(o)).text);
            var flip = Rebinds.Find("flip");
            try
            {
                Assert.AreEqual(StoryScript.I.guide.flip, Rebinds.FlipText(StoryScript.I.guide.flip), "SPACE while it is the flip key");
                Assert.IsTrue(Rebinds.Set(flip, false, "<Keyboard>/f", out _));
                ui.SetMode(UIManager.Mode.Room, true);
                typeof(StoryGuide).GetMethod("Switch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(guide, new object[] { StoryGuide.Step.Flip });
                StringAssert.Contains("Press F.", guide.Line, "the Room 1 prompt names the bound key");
                StringAssert.DoesNotContain("SPACE", guide.Line);
                Assert.IsTrue(endings.Play("Exposed", null));
                StringAssert.Contains("HOLD F", skipOf(endings), "an ending's skip hint: the hold is the flip key");
                endings.Finish(false);
                ui.SetMode(UIManager.Mode.Title, true);
                story.Play();
                StringAssert.Contains("HOLD F", skipOf(story), "the story's skip hint too");
                yield return null;
            }
            finally { Rebinds.Reset(flip, false); }
            Assert.AreEqual("SPACE", Rebinds.Label(flip, false));
        }

        // ================================================================== allocation
        [UnityTest, Timeout(300000)]
        public IEnumerator Cinematic_Allocates_Nothing_Per_Frame()
        {
            var ui = UIManager.I; var story = Screen<StoryScreen>("_story");
            _camGo.tag = "MainCamera";   // camera moves and glyph rain run too
            Assert.Greater(Allocs.Count(() => _sink = new byte[64]), 0, "the instrument sees a known allocation");
            Action tick = () => ui.TickAll(0.016f);
            for (int pass = 0; pass < 2; pass++)   // pass 0 warms TMP buffers and tween pools
            {
                ui.SetMode(UIManager.Mode.None, true); ui.SetMode(UIManager.Mode.Title, true);
                Click("Title/Card/Mode" + TitleScreen.StoryItem);
                yield return null;
                // A cue tick fires a beat or shows / hides a caption (Unity's own SetActive bookkeeping may allocate there); every
                // other tick is the steady animation and must allocate nothing.
                int ticks = 0, extra = 0, cueTicks = 0, cueExtra = 0;
                while (!story.Waking && ticks < 20000)
                {
                    int beat = story.BeatIndex, shown = story.Root.GetComponentsInChildren<Graphic>(false).Length;
                    int n = Allocs.Beyond(tick);
                    if (story.BeatIndex != beat || story.Root.GetComponentsInChildren<Graphic>(false).Length != shown) { cueTicks++; cueExtra += n; } else extra += n;
                    if (++ticks % 250 == 0) yield return null;   // let tweens and labels run between bursts
                }
                Assert.IsTrue(story.Waking, "the story reached its end");
                if (pass == 0) continue;
                Debug.Log("[STORY-TEST] allocations over " + ticks + " cinematic ticks: " + extra + " between cues, " + cueExtra + " in the " + cueTicks + " cue ticks");
                Assert.AreEqual(0, extra, "the cinematic allocates between cues");
                Assert.LessOrEqual(cueExtra, cueTicks, "a cue allocates at most once on average");
            }
        }

        static object _sink;
    }
}
