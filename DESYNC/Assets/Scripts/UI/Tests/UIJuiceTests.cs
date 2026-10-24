using System.Collections;
using System.IO;
using Desync.TestKit;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Desync.UI.Tests
{
    /// Death recap, results tally, HUD score/timer/medals, REDUCE FX, zero GC. Screenshots go to .work/ui2/shots.
    public class UIJuiceTests
    {
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        GameObject _camGo;
        Camera Cam => _camGo.GetComponent<Camera>();

        [UnitySetUp]
        public IEnumerator Up()
        {
            _camGo = new GameObject("JuiceTestCam", typeof(Camera));
            var c = Cam; c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.12f, 0.13f, 0.2f);
            _camGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            PlayerRef.Cam = c;
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb && mb.GetType().Name == "GameManager") mb.enabled = false;   // live Flow would act on our requests
            ResetState();
            yield return null;
            Assert.IsNotNull(UIManager.I, "UIManager did not self-bootstrap");
            UIManager.I.SetMode(UIManager.Mode.Room, true);
        }

        [TearDown]
        public void Down()
        {
            EndKeys();
            ResetState();
            PlayerRef.Cam = null;
            if (_camGo) Object.Destroy(_camGo);
            if (UIManager.I) UIManager.I.SetMode(UIManager.Mode.None, true);
        }

        static void ResetState()
        {
            GameState.RoomIndex = -1; GameState.Melt = 0; GameState.Score = 0; GameState.Chain = 0; GameState.Multiplier = 1f; GameState.RoomTDev = 0f;
            GameState.Paused = false; GameState.Alive = true; GameState.TraceActive = false; GameState.InHack = false; GameState.InteractPrompt = null;
            RoomClock.Time = 0; LayerState.Set(false);
        }

        // Virtual keyboard: batchmode has no focus, and a real keyboard is focus-disabled there.
        Keyboard _kb; InputSettings.BackgroundBehavior _bg;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode _gv;
#endif
        Keyboard BeginKeys()
        {
            _bg = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            _gv = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            _kb = InputSystem.AddDevice<Keyboard>();
            InputSystem.EnableDevice(_kb);
            return _kb;
        }

        void EndKeys()
        {
            if (_kb == null) return;
            InputSystem.settings.backgroundBehavior = _bg;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = _gv;
#endif
            InputSystem.RemoveDevice(_kb); _kb = null;
        }

        IEnumerator Press(Key k) { InputSystem.QueueStateEvent(_kb, new KeyboardState(k)); yield return Frames(2); InputSystem.QueueStateEvent(_kb, new KeyboardState()); yield return Frames(2); }

        static T Get<T>(string path) where T : Component
        {
            var go = GameObject.Find(path);
            Assert.IsNotNull(go, "missing " + path);
            return go.GetComponent<T>();
        }

        static string Txt(string path) { var t = Get<TMP_Text>(path); return t.text; }

        /// GameObject.Find resolves "a/b" paths through inactive children too: check the hierarchy state explicitly.
        static bool Shown(string path) { var go = GameObject.Find(path); return go && go.activeInHierarchy; }

        // ------------------------------------------------------------------ pure formatting
        [Test]
        public void Clock_Formats_mm_ss_cc_And_Medal_Limits()
        {
            var b = new char[8];
            string F(float t) { UIKit.Clock(t, b); return new string(b); }
            Assert.AreEqual("00:00.00", F(0f));
            Assert.AreEqual("00:00.00", F(-3f));
            Assert.AreEqual("00:41.27", F(41.27f));
            Assert.AreEqual("01:23.45", F(83.456f));
            Assert.AreEqual("10:00.00", F(600f));
            Assert.AreEqual("99:59.99", F(1e6f));
            Assert.AreEqual("0:41.27", UIKit.ClockShort(41.27f));
            Assert.AreEqual("12:34.50", UIKit.ClockShort(754.5f));

            Assert.AreEqual(0, UIKit.MedalAt(10f, 0f), "unknown tDev");
            Assert.AreEqual(4, UIKit.MedalAt(30f, 30f), "SYNC at tDev");
            Assert.AreEqual(3, UIKit.MedalAt(37.5f, 30f), "GOLD at 1.25");
            Assert.AreEqual(2, UIKit.MedalAt(37.6f, 30f));
            Assert.AreEqual(2, UIKit.MedalAt(48f, 30f), "SILVER at 1.6");
            Assert.AreEqual(1, UIKit.MedalAt(48.1f, 30f), "BRONZE past silver");
            Assert.AreEqual(37.5f, UIKit.MedalLimit(3, 30f), 1e-4f);
        }

        [Test]
        public void Recap_Cause_Text()
        {
            Assert.AreEqual("ICE-2", RecapScreen.CauseText(DeathCause.Ice, "ICE-2"));
            Assert.AreEqual("ICE", RecapScreen.CauseText(DeathCause.Ice, null));
            Assert.AreEqual("MELT 100%", RecapScreen.CauseText(DeathCause.Melt, null));
            Assert.AreEqual("TRACE LOCKDOWN", RecapScreen.CauseText(DeathCause.Alarm, null));
            Assert.AreEqual("TRACE LOCKDOWN", RecapScreen.CauseText(DeathCause.TraceTimer, null));
            Assert.AreEqual("GLITCH ZONE", RecapScreen.CauseText(DeathCause.Glitch, "GLITCH-1"));
            Assert.AreEqual("DRONE", RecapScreen.CauseText(DeathCause.Drone, "DRONE-1"));
            Assert.AreEqual("ARCHITECT", RecapScreen.CauseText(DeathCause.Architect, null));
        }

        // ------------------------------------------------------------------ HUD
        [UnityTest, Timeout(120000)]
        public IEnumerator Hud_Timer_Score_Multiplier_Ghost_Medals()
        {
            var ui = UIManager.I;
            GameState.RoomIndex = 2; GameEvents.RoomStarted?.Invoke(2);
            GameState.RoomTDev = 30f; RoomClock.Time = 31.4f;
            ui.TickAll(0.016f);
            Assert.AreEqual("00:31.40", Txt("Timer"));
            Assert.AreEqual("GOLD 0:37.50", Txt("Medals/Next"), "past SYNC: GOLD is the next threshold");
            RoomClock.Time = 50f; ui.TickAll(0.016f);
            Assert.AreEqual("BRONZE · CLEAR", Txt("Medals/Next"));
            Assert.AreEqual(UIKit.Danger, Get<TMP_Text>("Timer").color, "no medal better than bronze left");

            GameState.Score = 1234; ui.TickAll(0.016f);
            Assert.AreEqual("1234", Txt("Score"));
            Assert.AreEqual("x1.00", Txt("Mult"));
            var mult = Get<RectTransform>("Mult");
            GameState.Multiplier = 1.5f; GameState.Chain = 2; ui.TickAll(0.001f);
            Assert.AreEqual("x1.50", Txt("Mult"));
            Assert.Greater(mult.localScale.x, 1.1f, "pulses +12 % on change");
            Assert.LessOrEqual(mult.localScale.x, 1.12f + 1e-4f);
            for (int i = 0; i < 10; i++) ui.TickAll(0.016f);
            Assert.AreEqual(1f, mult.localScale.x, 1e-4f, "back to rest after 120 ms");
            Assert.AreEqual("GHOST x2", Txt("Ghost"));
            GameState.Chain = 0; ui.TickAll(0.016f);
            Assert.IsNull(GameObject.Find("Ghost"), "GHOST hidden at chain 0");

            GameState.RoomIndex = 9; GameState.RoomTDev = 0f; ui.TickAll(0.016f);   // no live value and no rooms.json entry
            Assert.IsFalse(Shown("Medals"), "medal ticks hidden while tDev is unknown");
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Hud_ScorePops_Pooled_And_Redline_Callout()
        {
            var ui = UIManager.I;
            GameState.RoomIndex = 0; GameEvents.RoomStarted?.Invoke(0);
            GameState.RoomTDev = 20f; RoomClock.Time = 12.5f; GameState.Score = 4200; GameState.Multiplier = 2.25f; GameState.Chain = 5;
            for (int i = 0; i < 8; i++)
                GameEvents.Scored?.Invoke(new ScorePop { type = i % 2 == 0 ? Rules.EventType.Graze : Rules.EventType.CleanFlip, points = 100 + i, multiplier = 1f });
            ui.TickAll(0.016f);
            int on = 0;
            for (int i = 0; i < 6; i++) if (GameObject.Find("ScorePop" + i)) on++;
            Assert.AreEqual(6, on, "6 pooled pops, the oldest recycled");
            Assert.IsNull(GameObject.Find("ScorePop6"));
            bool labels = false;
            for (int i = 0; i < 6; i++) { var t = Txt("ScorePop" + i + "/Label"); labels |= t == "CLEAN FLIP"; Assert.IsTrue(t == "GRAZE" || t == "CLEAN FLIP", t); }
            Assert.IsTrue(labels);

            Assert.IsNotNull(GameObject.Find("Melt/BarBg/Tick75")); Assert.IsNotNull(GameObject.Find("Melt/BarBg/Tick90"));
            GameState.Melt = 92f; ui.TickAll(0.016f);
            Assert.IsNull(GameObject.Find("Redline"), "RENDER: no callout");
            LayerState.Set(true); ui.TickAll(0.016f);
            Assert.IsNotNull(GameObject.Find("Redline"), "SOURCE at >= 90: REDLINE callout");
            yield return Shot("u01_hud_score_redline");
            GameState.Melt = 80f; ui.TickAll(0.016f);
            Assert.IsNull(GameObject.Find("Redline"));
            for (int i = 0; i < 70; i++) ui.TickAll(0.016f);
            Assert.IsNull(GameObject.Find("ScorePop0"), "pops expire");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Hud_Shakes_From_90_Not_70()
        {
            var ui = UIManager.I;
            var root = Get<RectTransform>("Hud");
            bool oldShake = Settings.Shake, oldFx = Settings.ReduceFx;
            try
            {
                Settings.Shake = true; Settings.ReduceFx = false; UIKit.RefreshSettings();
                float Max(float melt)
                {
                    GameState.Melt = melt; float m = 0f;
                    for (int i = 0; i < 30; i++) { ui.TickAll(0.016f); m = Mathf.Max(m, root.anchoredPosition.magnitude); }
                    return m;
                }
                Assert.AreEqual(0f, Max(80f), "flash only between 70 and 90");
                Assert.Greater(Max(95f), 0.1f, "shake at >= 90");
                Settings.ReduceFx = true; UIKit.RefreshSettings();
                Assert.IsFalse(UIKit.Shake); Assert.IsFalse(UIKit.Flashes);
                Assert.AreEqual(0f, Max(100f), "REDUCE FX: shake x0");
            }
            finally { Settings.Shake = oldShake; Settings.ReduceFx = oldFx; UIKit.RefreshSettings(); GameState.Melt = 0f; ui.TickAll(0.016f); }
            yield return null;
        }

        // ------------------------------------------------------------------ death recap
        [UnityTest, Timeout(120000)]
        public IEnumerator Recap_Card_Marker_Desat_And_Hint()
        {
            var ui = UIManager.I;
            GameState.RoomIndex = 2; GameEvents.RoomStarted?.Invoke(2);
            var recap = Recap();
            GameEvents.DeathRecap?.Invoke(new DeathInfo { cause = DeathCause.Ice, killerId = "ICE-2", killerPos = new Vector3(0.6f, 0.2f, 5f), roomTime = 41.27f, nearMiss = "exit was 2.3 m away" });
            ui.TickAll(0.016f);
            Assert.IsTrue(recap.IsOn);
            Assert.AreEqual("ICE-2", Txt("Recap/Card/Cause"));
            Assert.AreEqual("0:41.27", Txt("Recap/Card/Time"));
            Assert.AreEqual("EXIT WAS 2.3 M AWAY", Txt("Recap/Card/NearMiss"));
            Assert.IsTrue(Shown("Recap/Card/AnyKey"));
            Assert.IsTrue(recap.MarkerOnScreen, "killer in view: ring");
            Assert.Greater(recap.MarkerPos.x, 0f, "ring sits right of centre, where the killer is");
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(0.6f, Shader.GetGlobalFloat("_Desat"), 0.01f, "post-FX desaturates 60 %");
            Assert.AreEqual(1f, Shader.GetGlobalVector("_DesatFocus").w, "colour spot on the killer");
            yield return Shot("u02_recap_ring");

            GameEvents.DeathRecap?.Invoke(new DeathInfo { cause = DeathCause.Ice, killerId = "ICE-3", killerPos = new Vector3(0f, 0f, -5f), roomTime = 3f });
            ui.TickAll(0.016f);
            Assert.IsTrue(recap.MarkerArrow, "killer behind: edge arrow");
            Assert.Less(recap.MarkerPos.y, 0f);
            GameEvents.DeathRecap?.Invoke(new DeathInfo { cause = DeathCause.Ice, killerId = "ICE-4", killerPos = new Vector3(50f, 0f, 1f), roomTime = 3f });
            ui.TickAll(0.016f);
            Assert.IsTrue(recap.MarkerArrow, "killer far right: edge arrow");
            Assert.Greater(recap.MarkerPos.x, 0f);
            yield return Shot("u03_recap_arrow");

            GameEvents.DeathRecap?.Invoke(new DeathInfo { cause = DeathCause.Melt, roomTime = 8.4f });
            ui.TickAll(0.016f);
            Assert.AreEqual("MELT 100%", Txt("Recap/Card/Cause"));
            Assert.IsFalse(Shown("Recap/Card/NearMiss"), "no near-miss line when there is none");
            Assert.IsFalse(Shown("Recap/Killer"), "no killer marker for melt");

            GameEvents.Hint?.Invoke("Flip back before the ring closes.");
            ui.TickAll(0.016f);
            Assert.IsNull(GameObject.Find("Hint"), "hint waits for the REBOOT");
            GameEvents.Rebooting?.Invoke();
            Assert.IsFalse(recap.IsOn, "REBOOT hides the recap");
            Assert.AreEqual("Flip back before the ring closes.", Txt("Hint/Text"));
            GameEvents.RoomReset?.Invoke();
            yield return null;
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_Desat"), 1e-4f, "colour back after the reset");
            for (int i = 0; i < 8; i++) ui.TickAll(0.45f);
            Assert.IsNotNull(GameObject.Find("Hint"), "still up before 4 s");
            ui.TickAll(0.45f);
            Assert.IsNull(GameObject.Find("Hint"), "gone after 4 s");
            GameEvents.Rebooting?.Invoke();
            Assert.IsNull(GameObject.Find("Hint"), "shown once");
            GameEvents.RoomReset?.Invoke();
        }

        static RecapScreen Recap() => (RecapScreen)typeof(UIManager).GetField("_recap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(UIManager.I);
        static ResultsScreen Results() => (ResultsScreen)typeof(UIManager).GetField("_results", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(UIManager.I);

        // ------------------------------------------------------------------ results
        static RoomReport Report() => new RoomReport
        {
            room = 2, eventsScore = 3450, peakMultiplier = 2.75f, timeBonus = 480, total = 3930, time = 41.27f, medal = Rules.Medal.Gold, grade = 'S',
            prevBestTime = 42.09f, prevBestScore = 3580, nextTarget = "0.8 s to SYNC", shareText = "DESYNC // 2026-10-24 // A S // 0:41.27 // GHOST x5 // desync-nine.vercel.app",
        };

        [UnityTest, Timeout(120000)]
        public IEnumerator Results_Tally_Skip_Then_Keys_Raise_Events()
        {
            int restart = 0, cont = 0, copies = 0, stops = 0; string copied = null; float stopLen = 0f;
            System.Action r = () => restart++, c = () => cont++; System.Action<string> cp = s => { copies++; copied = s; }; System.Action<float> hs = s => { stops++; stopLen = s; };
            GameEvents.RestartRequested += r; GameEvents.ContinueRequested += c; GameEvents.CopyRequested += cp; GameEvents.HitStopRequest += hs;
            BeginKeys();
            try
            {
                Assert.LessOrEqual(ResultsScreen.End, 4f, "tally <= 4 s");
                var res = Results();
                GameState.RoomIndex = 2; GameEvents.RoomStarted?.Invoke(2);
                GameEvents.RoomReportReady?.Invoke(Report());
                yield return null;
                Assert.IsTrue(res.IsOn);
                Assert.IsFalse(res.Done);
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Press(Key.R);
                Assert.IsTrue(res.Done, "first press skips to the end state");
                Assert.AreEqual(0, restart, "the skipping press must not act");
                Assert.AreEqual("+3450", res.ValueText(0)); Assert.AreEqual("x2.75", res.ValueText(1));
                Assert.AreEqual("+480", res.ValueText(2)); Assert.AreEqual("3930", res.ValueText(3)); Assert.AreEqual("GOLD", res.ValueText(4));
                Assert.AreEqual(0, stops, "no hit-stop on a skip");
                yield return Press(Key.R);
                Assert.AreEqual(1, restart, "second press: [R] RETRY");
                yield return Press(Key.Space); yield return Press(Key.R);
                Assert.AreEqual(1, restart); Assert.AreEqual(0, cont, "acts once");
                GameEvents.Rebooting?.Invoke();
                Assert.IsFalse(res.IsOn, "REBOOT closes the results");
                GameEvents.RoomReset?.Invoke();

                GameEvents.RoomReportReady?.Invoke(Report());
                yield return new WaitForSecondsRealtime(1.0f);
                Assert.AreEqual("+3450", res.ValueText(0), "EVENTS counted up within 250 ms");
                yield return new WaitForSecondsRealtime(ResultsScreen.End - 1.0f + 0.3f);
                Assert.IsTrue(res.Done, "the tally ends on its own");
                Assert.AreEqual(1, stops, "grade stamp hit-stop"); Assert.AreEqual(0.08f, stopLen, 1e-4f);
                yield return Shot("u04_results");
                yield return Press(Key.C);
                Assert.AreEqual(1, copies); Assert.AreEqual(Report().shareText, copied);
                Assert.IsFalse(res.Acted, "copy keeps the results up");
                Assert.AreEqual("[C] COPIED", Txt("Results/Panel/ResultsCopy/Label"));
                yield return Press(Key.Space);
                Assert.AreEqual(1, cont, "[SPACE] CONTINUE");
                Assert.AreEqual(1, restart);
            }
            finally
            {
                EndKeys();
                GameEvents.RestartRequested -= r; GameEvents.ContinueRequested -= c; GameEvents.CopyRequested -= cp; GameEvents.HitStopRequest -= hs;
                GameEvents.Rebooting?.Invoke(); GameEvents.RoomReset?.Invoke();
            }
        }

        // ------------------------------------------------------------------ overlays + settings
        [UnityTest, Timeout(120000)]
        public IEnumerator Goals_Overlay_Title_Line_And_ReduceFx()
        {
            var ui = UIManager.I;
            GameState.RoomIndex = 2; GameState.RoomTDev = 30f; GameEvents.RoomStarted?.Invoke(2);
            var goals = (GoalsOverlay)typeof(UIManager).GetField("_goals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(ui);
            var kb = BeginKeys();
            yield return Frames(2);
            Assert.IsFalse(goals.IsOn);
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Tab));   // hold
            yield return Frames(3);
            Assert.IsTrue(goals.IsOn, "TAB held: goals overlay");
            Assert.That(Txt("Goals/Panel/Head"), Does.StartWith("R03"));
            StringAssert.Contains("0:37.50", Txt("Goals/Panel/MedalList"), "GOLD limit = tDev x 1.25");
            StringAssert.Contains("PAR     2700", Txt("Goals/Panel/ScoreList"), "rooms.json parScore for r3");
            yield return Shot("u05_goals");
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Frames(3);
            Assert.IsFalse(goals.IsOn, "released: gone");
            EndKeys();

            Net.SaveService.Unload("desync.test.ui2");   // a throwaway profile: never touch the developer's own save
            try
            {
                string line = TitleScreen.GoalsLine();
                StringAssert.IsMatch(@"^BEST (--:--|\d+:\d\d) · MEDALS \d+/24$", line);   // a fresh profile may still import legacy bests
            }
            finally { Net.SaveService.Unload(); PlayerPrefs.DeleteKey("desync.test.ui2"); PlayerPrefs.DeleteKey("desync.test.ui2.backup"); PlayerPrefs.Save(); }

            bool old = Settings.ReduceFx;
            try
            {
                Settings.ReduceFx = true; GameEvents.SettingsChanged?.Invoke();
                yield return null;
                Assert.AreEqual(1f, Shader.GetGlobalFloat("_FxReduce"), "chromatic x0.3 switch");
                Assert.AreEqual(0f, Shader.GetGlobalFloat("_FlashScale"), "flashes off");
                Settings.ReduceFx = false; GameEvents.SettingsChanged?.Invoke();
                yield return null;
                Assert.AreEqual(0f, Shader.GetGlobalFloat("_FxReduce"));
            }
            finally { Settings.ReduceFx = old; GameEvents.SettingsChanged?.Invoke(); }

            GameState.Paused = true; GameEvents.Paused?.Invoke(true);
            yield return null;
            Assert.IsNotNull(GameObject.Find("ReduceFx"), "REDUCE FX toggle in the pause menu");
            Assert.IsNotNull(GameObject.Find("Flash"), "REDUCE FLASHES toggle kept");
            yield return Shot("u06_pause_reducefx");
            GameState.Paused = false; GameEvents.Paused?.Invoke(false);
        }

        // ------------------------------------------------------------------ allocations
        [UnityTest, Timeout(120000)]
        public IEnumerator Zero_Gc_Hud_Recap_Results_240_Ticks()
        {
            var ui = UIManager.I;
            GameState.RoomIndex = 1; GameState.RoomTDev = 25f; GameEvents.RoomStarted?.Invoke(1);
            var pops = new[] { new ScorePop { type = Rules.EventType.CleanFlip, points = 75, multiplier = 1.5f, label = "CLEAN FLIP" },
                               new ScorePop { type = Rules.EventType.Graze, points = 300, multiplier = 2f } };
            Assert.Greater(Allocs.Count(() => _sink = new byte[64]), 0, "the instrument sees a known allocation");
            int at = 0; System.Action<int> run = null;
            System.Action tick = () => { run(at); ui.TickAll(0.016f); };
            // Steady state: `start` sets the scene, the ticks run once to warm (the first use of each pooled pop, tween and text
            // buffer), then `start` again and the same ticks are counted, TMP's editor-only text copies aside.
            int Measure(int ticks, System.Action start, System.Action<int> step)
            {
                run = step;
                start(); for (at = 0; at < ticks; at++) tick();
                start(); int total = 0;
                for (at = 0; at < ticks; at++) total += Allocs.Beyond(tick);
                return total;
            }
            float t0 = RoomClock.Time, m0 = GameState.Melt; int s0 = GameState.Score;
            int hud = Measure(240, () => { RoomClock.Time = t0; GameState.Melt = m0; GameState.Score = s0; GameState.Chain = 0; GameState.Multiplier = 1f; }, i =>
            {
                RoomClock.Time += 0.016f; GameState.Melt = Mathf.Repeat(GameState.Melt + 0.5f, 100f);
                if (i % 10 == 0) { GameEvents.Scored?.Invoke(pops[i / 10 % 2]); GameState.Score += 75; GameState.Chain = i / 10 % 7; GameState.Multiplier = 1f + 0.25f * GameState.Chain; }
            });
            Debug.Log("[UI-TEST] allocations over 240 HUD ticks: " + hud);
            Assert.AreEqual(0, hud, "HUD allocates per frame");

            var death = new DeathInfo { cause = DeathCause.Ice, killerId = "ICE-1", killerPos = new Vector3(3f, 0f, 4f), roomTime = 20f, nearMiss = "0.4 s from melt-safe" };
            int recap = Measure(120, () => GameEvents.DeathRecap?.Invoke(death), i => { });
            Debug.Log("[UI-TEST] allocations over 120 recap ticks: " + recap);
            Assert.AreEqual(0, recap, "recap allocates per frame");
            GameEvents.Rebooting?.Invoke(); GameEvents.RoomReset?.Invoke();

            int res = Measure(240, () => GameEvents.RoomReportReady?.Invoke(Report()), i => { });   // the whole 2.6 s tally (ticks, stamp) and the end state
            Debug.Log("[UI-TEST] allocations over 240 results ticks: " + res);
            Assert.AreEqual(0, res, "results tally allocates per frame");
            Assert.IsTrue(Results().Done);
            GameEvents.Rebooting?.Invoke(); GameEvents.RoomReset?.Invoke();
            yield return null;
        }

        static object _sink;

        // ------------------------------------------------------------------ screenshots (best effort)
        static string ShotDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".work", "ui2", "shots"));

        /// Batchmode presents no frames: render the canvas through our camera into a 1920x1080 texture, then restore the overlay.
        IEnumerator Shot(string name)
        {
            var ui = UIManager.I;
            var cs = ui.Canvas.GetComponent<CanvasScaler>();
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var cam = Cam;
            cam.targetTexture = rt;
            cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; cs.scaleFactor = 1f;
            ui.Canvas.renderMode = RenderMode.ScreenSpaceCamera; ui.Canvas.worldCamera = cam; ui.Canvas.planeDistance = 0.5f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(ShotDir);
            File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            cam.targetTexture = null; rt.Release(); Object.Destroy(rt);
            ui.Canvas.renderMode = RenderMode.ScreenSpaceOverlay; ui.Canvas.worldCamera = null;
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        }
    }
}
