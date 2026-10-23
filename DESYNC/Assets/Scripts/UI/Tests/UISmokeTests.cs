using System.Collections;
using System.IO;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace Desync.UI.Tests
{
    /// PlayMode smoke test: builds every screen, drives them with GameEvents/GameState, clicks buttons through the
    /// EventSystem handlers, checks allocations and writes screenshots to .work/ui/shots (best effort).
    public class UISmokeTests
    {
        static string ShotDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".work", "ui", "shots"));

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static RenderTexture _rt;
        static Camera _shotCam;

        /// ScreenCapture does not present frames in batchmode, so render the UI canvas into a 1920x1080 RenderTexture ourselves.
        static IEnumerator Shot(string name, Camera sceneCam = null)
        {
            var ui = UIManager.I;
            if (!_rt) _rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            if (_shotCam && _shotCam != sceneCam && _shotCam != (_cam ? _cam.GetComponent<Camera>() : null)) _shotCam = null;
            var want = sceneCam ? sceneCam : _cam.GetComponent<Camera>();
            if (want != _shotCam)   // the runner / scene loads may destroy our camera between tests
            {
                if (_shotCam) _shotCam.targetTexture = null;
                _shotCam = want;
                _shotCam.targetTexture = _rt;
                var cs = ui.Canvas.GetComponent<CanvasScaler>();
                cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; cs.scaleFactor = 1f;
                ui.Canvas.renderMode = RenderMode.ScreenSpaceCamera; ui.Canvas.worldCamera = _shotCam; ui.Canvas.planeDistance = Mathf.Max(_shotCam.nearClipPlane + 0.01f, 0.1f);   // nearest thing in the transparent queue, like an overlay
            }
            yield return null;
            // the Art post-FX pass (melt ring, aberration) would distort the UI in this camera-rendered capture: neutralise it
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb && mb.GetType().Name == "PostFXController") mb.enabled = false;
            foreach (var n in new[] { "_Pulse", "_Melt", "_Trace", "_Reboot" }) Shader.SetGlobalFloat(n, 0f);
            Canvas.ForceUpdateCanvases();
            _shotCam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(ShotDir);
            File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            Debug.Log("[UI-TEST] shot " + name);
        }

        static void Click(string objName)
        {
            var go = GameObject.Find(objName);
            Assert.IsNotNull(go, "button not found: " + objName);
            var ev = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(go, ev, ExecuteEvents.pointerDownHandler);
        }

        static void ResetState()
        {
            GameState.RoomIndex = -1; GameState.Melt = 0; GameState.TraceActive = false; GameState.TraceRemaining = 0; GameState.TraceCount = 0;
            GameState.TraceCountdown = -1; GameState.GlitchCountdown = -1; GameState.HijackActive = false; GameState.DemoMode = false; GameState.InHack = false;
            GameState.InteractPrompt = null; GameState.TerminalsDone = 0; GameState.TerminalsTotal = 0; GameState.ExitOpen = false; GameState.Paused = false;
            RoomClock.Time = 0;
        }

        static GameObject _cam;

        [UnitySetUp]
        public IEnumerator Up()
        {
            if (!_cam)
            {
                _cam = new GameObject("TestCam", typeof(Camera), typeof(AudioListener));
                var c = _cam.GetComponent<Camera>(); c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.035f, 0.04f, 0.11f);
            }
            // Flow's GameManager is live in a test run and would really load Room_xx / Title on our button clicks: silence it.
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb && mb.GetType().Name == "GameManager") mb.enabled = false;
            ResetState();
            yield return null;
        }
        [TearDown] public void Down() { ResetState(); }

        [UnityTest, Timeout(120000)]
        public IEnumerator Bootstraps_And_Builds()
        {
            Debug.Log("[UI-TEST] begin Bootstraps_And_Builds");
            yield return Frames(2);
            var ui = UIManager.I;
            Assert.IsNotNull(ui, "UIManager did not self-bootstrap");
            Assert.IsNotNull(ui.Canvas);
            Assert.AreEqual(1, Object.FindObjectsByType<UIManager>().Length, "duplicate UIManager");
            Assert.IsNotNull(UIKit.Hud, "font.hud missing (run UISetup)");
            Assert.IsNotNull(UIKit.Term, "font.term missing");
            Assert.IsNotNull(UIKit.Title, "font.title missing");
            foreach (var f in new[] { UIKit.Hud, UIKit.Term, UIKit.Title })
                foreach (char c in "ABC xyz 019 ←↑→↓×·—●○")
                    Assert.IsTrue(f.HasCharacter(c, true), f.name + " lacks '" + c + "'");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Real_Scenes_Drive_UI_Modes()
        {
            Debug.Log("[UI-TEST] begin Real_Scenes_Drive_UI_Modes");
            var ui = UIManager.I;
            LogAssert.ignoreFailingMessages = true;   // other workstreams' scenes may log; UI problems are asserted explicitly
            SceneManager.LoadScene("Title");
            yield return null; yield return null;
            Assert.AreEqual(UIManager.Mode.Title, ui.Current, "Title scene should switch the UI to Title mode");
            var cam = Camera.main;
            Assert.IsNotNull(cam, "Title scene has no Main Camera");
            var p0 = cam.transform.position; bool src0 = LayerState.InSource; bool flipped = false;
            for (float t = 0; t < 3.6f; t += Time.unscaledDeltaTime) { if (LayerState.InSource != src0) flipped = true; yield return null; }
            Assert.Greater((cam.transform.position - p0).sqrMagnitude, 0.0001f, "attract mode did not orbit the camera");
            Assert.IsTrue(flipped, "attract mode did not flip RENDER/SOURCE");
            yield return Shot("11_title_scene", cam);

            SceneManager.LoadScene("Room_01");
            yield return null; yield return new WaitForSecondsRealtime(0.8f);
            Assert.AreEqual(UIManager.Mode.Room, ui.Current, "Room scene should switch the UI to Room mode");
            var rcam = Camera.main;
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("12_room01_hud", rcam ? rcam : null);

            SceneManager.LoadScene("Ending");
            yield return null; yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(UIManager.Mode.Ending, ui.Current, "Ending scene should switch the UI to Ending mode");
            LogAssert.ignoreFailingMessages = false;
            LayerState.Set(false); ui.SetMode(UIManager.Mode.None, true);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Title_Click_Raises_Start()
        {
            Debug.Log("[UI-TEST] begin Title_Click_Raises_Start");
            var ui = UIManager.I; int started = -1;
            System.Action<int> h = r => started = r; GameEvents.StartRequested += h;
            ui.SetMode(UIManager.Mode.Title, true);
            yield return new WaitForSecondsRealtime(1.8f);
            yield return Shot("01_title");
            LogAssert.ignoreFailingMessages = true;   // live Flow tries to load Room_01, which is not in the build of a test run
            Click("ClickCatcher");
            LogAssert.ignoreFailingMessages = false;
            GameEvents.StartRequested -= h;
            Assert.AreEqual(0, started);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Title_Space_And_Pad_Start_Raise_Start()
        {
            Debug.Log("[UI-TEST] begin Title_Space_And_Pad_Start_Raise_Start");
            var ui = UIManager.I; int started = -1;
            System.Action<int> h = r => started = r; GameEvents.StartRequested += h;
            var bg = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;   // batchmode has no focus
#if UNITY_EDITOR
            var gv = InputSystem.settings.editorInputBehaviorInPlayMode;   // keyboards only work while a Game View has focus (batchmode has none)
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            var pad = InputSystem.AddDevice<Gamepad>();
            var kb = InputSystem.AddDevice<Keyboard>();   // a real keyboard is focus-disabled in batchmode: use a virtual one
            InputSystem.EnableDevice(kb);
            try
            {
                var presses = new (string name, System.Action press)[]
                {
                    ("pad Start", () => InputSystem.QueueStateEvent(pad, new GamepadState(GamepadButton.Start))),
                    ("pad South", () => InputSystem.QueueStateEvent(pad, new GamepadState(GamepadButton.South))),
                    ("Space", () => InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Space))),
                };
                foreach (var (name, press) in presses)
                {
                    started = -1;
                    ui.SetMode(UIManager.Mode.Title, true);
                    yield return Frames(3);
                    Assert.AreEqual(-1, started, "title must not start on its own");
                    press();
                    yield return Frames(3);
                    Assert.AreEqual(0, started, name + " must start the run");
                    InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.QueueStateEvent(kb, new KeyboardState());
                    yield return Frames(3);
                }
            }
            finally
            {
                InputSystem.settings.backgroundBehavior = bg;
#if UNITY_EDITOR
                InputSystem.settings.editorInputBehaviorInPlayMode = gv;
#endif
                InputSystem.RemoveDevice(pad); InputSystem.RemoveDevice(kb); GameEvents.StartRequested -= h;
                ui.SetMode(UIManager.Mode.None, true);
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Story_Line2_Shows_When_Room2_Starts()
        {
            Debug.Log("[UI-TEST] begin Story_Line2_Shows_When_Room2_Starts");
            var ui = UIManager.I;
            ui.SetMode(UIManager.Mode.Room, true);
            var story = GameObject.Find("Story").GetComponent<TMP_Text>();
            GameState.RoomIndex = 0; GameEvents.RoomStarted?.Invoke(0);
            yield return Frames(2);
            Assert.AreNotEqual("> " + GameConfig.I.introLines[1], story.text, "line 2 belongs to Room 2 only");
            GameState.RoomIndex = 1; GameEvents.RoomStarted?.Invoke(1);
            yield return Frames(2);
            string want = UIKit.Safe("> " + GameConfig.I.introLines[1], UIKit.Term);
            Assert.AreEqual(want, story.text);
            Assert.AreEqual(1f, story.GetComponent<CanvasGroup>().alpha, 1e-4f);
            yield return new WaitForSecondsRealtime(want.Length / 64f + 0.5f);   // typewriter (64 cps) has finished
            Assert.GreaterOrEqual(story.maxVisibleCharacters, want.Length);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Hud_Shakes_From_Melt_Warn_Honours_Setting_And_Settles()
        {
            Debug.Log("[UI-TEST] begin Hud_Shakes_From_Melt_Warn_Honours_Setting_And_Settles");
            var ui = UIManager.I;
            ui.SetMode(UIManager.Mode.Room, true);
            var root = (RectTransform)GameObject.Find("Hud").transform;
            bool oldShake = Settings.Shake;
            try
            {
                Settings.Shake = true; UIKit.RefreshSettings();
                float Max(float melt)
                {
                    GameState.Melt = melt; float m = 0f;
                    for (int i = 0; i < 30; i++) { ui.TickAll(0.016f); m = Mathf.Max(m, root.anchoredPosition.magnitude); }
                    return m;
                }
                Assert.AreEqual(0f, Max(GameConfig.I.meltWarn - 5f), "calm below the warn threshold");
                float hi = Max(100f);
                Assert.Greater(hi, 0.1f, "HUD root jitters at melt >= warn");
                Assert.LessOrEqual(hi, 4f * 1.4143f + 0.01f, "a few px at most");
                Assert.AreEqual(0f, Max(10f), "settles once melt drops");
                Settings.Shake = false; UIKit.RefreshSettings();
                Assert.AreEqual(0f, Max(100f), "Settings.Shake off: no jitter");
                Assert.AreEqual(Vector2.zero, root.anchoredPosition);
                Settings.Shake = true; UIKit.RefreshSettings();
                Max(100f);
                GameState.Paused = true; ui.TickAll(0.016f);
                Assert.AreEqual(Vector2.zero, root.anchoredPosition, "no jitter while paused");
            }
            finally { Settings.Shake = oldShake; UIKit.RefreshSettings(); GameState.Paused = false; GameEvents.Paused?.Invoke(false); GameState.Melt = 0f; ui.TickAll(0.016f); }
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Saved_Best_Shows_On_LevelSelect_And_Ending()
        {
            Debug.Log("[UI-TEST] begin Saved_Best_Shows_On_LevelSelect_And_Ending");
            var ui = UIManager.I;
            var oldT = new float[2]; var oldG = new int[2]; var had = new bool[2];   // keep the developer's own saved bests
            for (int i = 0; i < 2; i++) { had[i] = PlayerPrefs.HasKey("bestG" + i); oldT[i] = PlayerPrefs.GetFloat("bestT" + i); oldG[i] = PlayerPrefs.GetInt("bestG" + i); PlayerPrefs.DeleteKey("bestT" + i); PlayerPrefs.DeleteKey("bestG" + i); }
            try
            {
                PlayerPrefs.SetFloat("bestT1", 62.7f); PlayerPrefs.SetInt("bestG1", 'A');
                Assert.IsNull(UIKit.BestLabel(0));
                Assert.AreEqual("BEST 1:02 A", UIKit.BestLabel(1));

                ui.SetMode(UIManager.Mode.Room, true);
                GameEvents.LevelSelectToggled?.Invoke(true);
                yield return null;
                var par = GameConfig.I.parTime;
                Assert.AreEqual("PAR " + par[0].ToString("0") + "s", Parsed("Room0"));
                Assert.AreEqual("PAR " + par[1].ToString("0") + "s · BEST 1:02 A", Parsed("Room1"));
                yield return Shot("13_levelselect_best");
                GameEvents.LevelSelectToggled?.Invoke(false);

                for (int i = 0; i < 6; i++) GameState.Results[i] = new RoomResult { room = i, time = 20 + i * 13.7f, traces = i % 3, flips = 3 + i, bestCombo = i, grade = "SAABCS"[i] };
                GameState.Results[0] = default;
                ui.SetMode(UIManager.Mode.Ending, true);
                yield return new WaitForSecondsRealtime(2.2f);
                yield return Shot("14_ending_best");
                Assert.AreEqual("", Parsed("Cell0", "Best"));
                Assert.AreEqual("BEST 1:02 A", Parsed("Cell1", "Best"), "saved best shows on the run report, played this run or not");
            }
            finally
            {
                for (int i = 0; i < 2; i++) { PlayerPrefs.DeleteKey("bestT" + i); PlayerPrefs.DeleteKey("bestG" + i); if (had[i]) { PlayerPrefs.SetFloat("bestT" + i, oldT[i]); PlayerPrefs.SetInt("bestG" + i, oldG[i]); } }
                PlayerPrefs.Save();
                for (int i = 0; i < 6; i++) GameState.Results[i] = default;
                ui.SetMode(UIManager.Mode.None, true);
            }
        }

        static string Parsed(string obj, string child = "Info")
        {
            var t = GameObject.Find(obj).transform.Find(child).GetComponent<TMP_Text>();
            t.ForceMeshUpdate();
            return t.GetParsedText().Trim();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Hud_States_Events_And_Zero_Gc()
        {
            Debug.Log("[UI-TEST] begin Hud_States_Events_And_Zero_Gc");
            var ui = UIManager.I;
            ui.SetMode(UIManager.Mode.Room, true);
            GameState.RoomIndex = 0; GameEvents.RoomStarted?.Invoke(0);
            yield return new WaitForSecondsRealtime(1.4f);
            yield return Shot("02_hud_controlcard");

            GameState.RoomIndex = 2; GameState.TerminalsDone = 1; GameState.TerminalsTotal = 2; GameState.Melt = 76f;
            GameState.TraceActive = true; GameState.TraceRemaining = 4.2f; GameState.TraceCount = 2;
            GameState.InteractPrompt = "E  HACK"; GameState.HijackActive = true; GameEvents.Hijacked?.Invoke(true);
            GameState.TraceCountdown = 14f; GameState.GlitchCountdown = 2.4f; GameState.DemoMode = true; GameState.ExitOpen = true;
            RoomClock.Time = 31.4f;
            GameEvents.Flipped?.Invoke(true); LayerState.Set(true);
            GameEvents.PopText?.Invoke("ACCESS GRANTED", UIKit.Cyan);
            GameEvents.AccessGranted?.Invoke("MEMO 01: HALCYON-CITY renders at 98% fidelity. Nobody has noticed.");
            GameEvents.ComboChanged?.Invoke(3);
            GameEvents.ExitOpened?.Invoke();
            yield return new WaitForSecondsRealtime(1.3f);
            yield return Shot("03_hud_full");

            GameState.InHack = true; GameState.InteractPrompt = null;
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("04_hud_hack");
            GameState.InHack = false;

            // steady-state allocation check: tick the whole UI by hand with moving timers, count managed allocations
            long total = 0; int ticks = 240;
            for (int i = 0; i < 30; i++) ui.TickAll(0.016f);
            for (int i = 0; i < ticks; i++)
            {
                RoomClock.Time += 0.016f; GameState.TraceRemaining = Mathf.Max(0.1f, GameState.TraceRemaining - 0.016f);
                GameState.Melt = Mathf.Repeat(GameState.Melt + 0.4f, 100f); GameState.GlitchCountdown = Mathf.Repeat(GameState.GlitchCountdown - 0.016f, 3f);
                long a = System.GC.GetTotalMemory(false);
                ui.TickAll(0.016f);
                long b = System.GC.GetTotalMemory(false);
                if (b > a) total += b - a;
            }
            Debug.Log("[UI-TEST] managed growth over " + ticks + " HUD ticks: " + total + " bytes");
            Assert.Less(total / (float)ticks, 64f, "HUD allocates per frame");

            // grade-card spin + pause menu + glitch labels while ticking: still no allocations
            GameEvents.RoomCleared?.Invoke(new RoomResult { room = 2, time = 61f, traces = 1, flips = 7, bestCombo = 4, grade = 'A' });
            GameState.Paused = true; GameEvents.Paused?.Invoke(true);
            for (int i = 0; i < 20; i++) ui.TickAll(0.016f);
            long total2 = 0;
            for (int i = 0; i < 120; i++)
            {
                long a = System.GC.GetTotalMemory(false);
                ui.TickAll(0.016f);
                long b = System.GC.GetTotalMemory(false);
                if (b > a) total2 += b - a;
            }
            Debug.Log("[UI-TEST] managed growth over 120 grade/pause ticks: " + total2 + " bytes");
            Assert.Less(total2 / 120f, 64f, "grade card / pause allocate per frame");
            GameState.Paused = false; GameEvents.Paused?.Invoke(false);

            GameEvents.Rebooting?.Invoke();
            yield return new WaitForSecondsRealtime(0.15f);
            yield return Shot("05_reboot");
            GameEvents.RoomReset?.Invoke();
            yield return Frames(30);
            LayerState.Set(false); GameState.Paused = false;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Pause_Buttons_Raise_Requests()
        {
            Debug.Log("[UI-TEST] begin Pause_Buttons_Raise_Requests");
            var ui = UIManager.I;
            ui.SetMode(UIManager.Mode.Room, true);
            int resume = 0, restart = 0, quit = 0, settings = 0;
            System.Action r = () => resume++, rs = () => restart++, q = () => quit++, st = () => settings++;
            GameEvents.ResumeRequested += r; GameEvents.RestartRequested += rs; GameEvents.QuitRequested += q; GameEvents.SettingsChanged += st;
            Cursor.lockState = CursorLockMode.None;
            yield return Pause();
            yield return new WaitForSecondsRealtime(0.7f);
            yield return Shot("06_pause_lock_lost");
            Click("Res75"); Assert.AreEqual(0.75f, Settings.ResolutionScale, 0.001f);
            Click("Res100"); Assert.AreEqual(1f, Settings.ResolutionScale, 0.001f);
            LogAssert.ignoreFailingMessages = true;   // live Flow reacts to the requests (scene loads that a test run cannot do)
            Click("Restart"); yield return Pause();
            Click("Quit"); yield return Pause();
            Click("Resume");
            LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(1, restart); Assert.AreEqual(1, quit); Assert.AreEqual(1, resume); Assert.AreEqual(2, settings);
            GameEvents.ResumeRequested -= r; GameEvents.RestartRequested -= rs; GameEvents.QuitRequested -= q; GameEvents.SettingsChanged -= st;
            GameState.Paused = false; GameEvents.Paused?.Invoke(false);
            yield return Frames(3);
        }

        static IEnumerator Pause()
        {
            GameState.Paused = true; GameEvents.Paused?.Invoke(true);
            yield return null;
            Assert.IsNotNull(GameObject.Find("Resume"), "pause menu not visible");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator LevelSelect_Grade_Finale_Ending()
        {
            Debug.Log("[UI-TEST] begin LevelSelect_Grade_Finale_Ending");
            var ui = UIManager.I;
            ui.SetMode(UIManager.Mode.Room, true);
            int picked = -1; System.Action<int> h = i => picked = i; GameEvents.StartRequested += h;
            GameEvents.LevelSelectToggled?.Invoke(true);
            yield return new WaitForSecondsRealtime(0.7f);
            yield return Shot("07_levelselect");
            LogAssert.ignoreFailingMessages = true;
            Click("Room2");
            LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(2, picked);
            GameEvents.StartRequested -= h;
            GameEvents.LevelSelectToggled?.Invoke(false);

            GameEvents.RoomCleared?.Invoke(new RoomResult { room = 2, time = 61.3f, traces = 1, flips = 7, bestCombo = 4, grade = 'A' });
            yield return new WaitForSecondsRealtime(2.2f);
            yield return Shot("08_grade_landed");
            yield return Frames(300);   // card auto-hides

            GameEvents.GameCompleted?.Invoke();
            yield return new WaitForSecondsRealtime(2.0f);
            yield return Shot("09_finale");

            for (int i = 0; i < 6; i++) GameState.Results[i] = new RoomResult { room = i, time = 20 + i * 13.7f, traces = i % 3, flips = 3 + i, bestCombo = i, grade = "SAABCS"[i] };
            GameState.Results[4] = default;
            ui.SetMode(UIManager.Mode.Ending, true);
            yield return new WaitForSecondsRealtime(3.0f);
            yield return Shot("10_ending");
            int again = -1; System.Action<int> h2 = i => again = i; GameEvents.StartRequested += h2;
            LogAssert.ignoreFailingMessages = true;
            Click("PlayAgain"); GameEvents.StartRequested -= h2;
            LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(0, again);
            for (int i = 0; i < 6; i++) GameState.Results[i] = default;
            ui.SetMode(UIManager.Mode.None, true);
        }
    }
}
