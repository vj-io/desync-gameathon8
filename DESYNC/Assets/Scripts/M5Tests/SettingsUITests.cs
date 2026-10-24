using System.Collections;
using System.Collections.Generic;
using Desync.Flow;
using Desync.Net;
using Desync.Rules;
using Desync.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Desync.M5.Tests
{
    /// The screens and the live canvas: SOUND + ACCESS drives the store, HUD size keeps the corners anchored, high contrast lifts the
    /// plates, remapped keys show on the HUD and the pause list, the ASSIST tag follows the flag, the analytics relay reports only
    /// what changed.
    public class SettingsUITests
    {
        const string SaveKey = "desync.save.m5ui";
        readonly List<MonoBehaviour> paused = new List<MonoBehaviour>();
        System.Action<string, string> oldSink; bool oldDnt;
        readonly List<(string name, string json)> events = new List<(string, string)>();

        [UnitySetUp]
        public IEnumerator Up()
        {
            Isolation.Begin(SaveKey);
            Rebinds.ResetAll(); AssistHost.Reset();
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb && mb.enabled && (mb.GetType().Name == "GameManager" || mb.GetType().Name == "RunManager" || mb.GetType().Name == "RoomManager"))
                { mb.enabled = false; paused.Add(mb); }   // live Flow would act on the events these tests raise
            oldSink = Analytics.Sink; oldDnt = Analytics.DoNotTrack;
            Analytics.DoNotTrack = false; events.Clear();
            Analytics.Sink = (n, j) => events.Add((n, j));
            yield return null;
            Assert.IsNotNull(UIManager.I, "UIManager did not self-bootstrap");
        }

        [TearDown]
        public void Down()
        {
            Analytics.Sink = oldSink; Analytics.DoNotTrack = oldDnt;
            Rebinds.ResetAll();
            A11y.ResetDefaults();
            Assist.On = false; AssistHost.Reset();
            GameState.Paused = false; GameState.Phase = GameState.PhaseKind.Title;
            RunState.Phase = RunPhase.None; RunState.Mode = RunMode.Classic; RunState.Grades.Clear(); RunState.Won = false; RunState.NodeId = null;
            GameState.God = false;
            if (UIManager.I)
            {
                UIManager.I.SetMode(UIManager.Mode.Title, true);
                UIManager.I.SetMode(UIManager.Mode.None, true);
            }
            foreach (var mb in paused) if (mb) mb.enabled = true;
            paused.Clear();
            Isolation.End(SaveKey);
        }

        static Transform Root => UIManager.I.Canvas.transform;

        static void Click(string objName)
        {
            var go = GameObject.Find(objName);
            Assert.IsNotNull(go, "button not found: " + objName);
            ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerDownHandler);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Settings_Screen_DrivesTheStore_AndRaisesSettingsChanged()
        {
            float vol0 = Settings.Volume;   // the master slider writes the real PlayerPrefs value: put it back
            UIManager.I.OpenSettings();
            yield return null;
            Assert.IsTrue(Root.Find("Settings").gameObject.activeSelf);
            int changed = 0; System.Action h = () => changed++;
            GameEvents.SettingsChanged += h;
            try
            {
                Root.Find("Settings/Panel/Music").GetComponent<Slider>().value = 0.3f;
                Root.Find("Settings/Panel/Sfx").GetComponent<Slider>().value = 0.6f;
                Root.Find("Settings/Panel/Ui").GetComponent<Slider>().value = 0.1f;
                Root.Find("Settings/Panel/Master").GetComponent<Slider>().value = 0.5f;
                Assert.AreEqual(0.3f, A11y.MusicVolume, 1e-3f); Assert.AreEqual(0.6f, A11y.SfxVolume, 1e-3f);
                Assert.AreEqual(0.1f, A11y.UiVolume, 1e-3f); Assert.AreEqual(0.5f, Settings.Volume, 1e-3f);
                Assert.AreEqual("30%", Root.Find("Settings/Panel/MusicVal").GetComponent<TMP_Text>().text);

                Root.Find("Settings/Panel/Assist").GetComponent<Toggle>().isOn = true;
                Assert.IsTrue(Assist.On); Assert.IsTrue(Assist.Flagged);
                Root.Find("Settings/Panel/TabToggle").GetComponent<Toggle>().isOn = true;
                Assert.IsTrue(A11y.TabToggle);
                Assert.GreaterOrEqual(changed, 6, "every change raises SettingsChanged");

                Click("HudSizePlus"); Click("HudSizePlus");
                Assert.AreEqual(1.2f, A11y.UiScale, 1e-3f);
                Click("HudSizeMinus");
                Assert.AreEqual(1.1f, A11y.UiScale, 1e-3f);
                for (int i = 0; i < 6; i++) Click("HudSizePlus");
                Assert.AreEqual(A11y.UiScaleMax, A11y.UiScale, 1e-3f, "stops at the top of the range");
                for (int i = 0; i < 12; i++) Click("HudSizeMinus");
                Assert.AreEqual(A11y.UiScaleMin, A11y.UiScale, 1e-3f, "and at the bottom");
                Click("SubSizePlus");
                Assert.AreEqual(1.1f, A11y.SubtitleScale, 1e-3f);

                Click("ResetAccess");
                Assert.AreEqual(1f, A11y.UiScale); Assert.AreEqual(1f, A11y.MusicVolume); Assert.IsFalse(Assist.On); Assert.IsFalse(A11y.TabToggle);
                Assert.AreEqual(0.8f, Settings.Volume, 1e-3f, "master back to its default too");
            }
            finally { GameEvents.SettingsChanged -= h; Settings.Volume = vol0; }
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator SettingsAndKeysScreens_Navigate_AndCloseWithThePauseMenu()
        {
            UIManager.I.OpenSettings();
            yield return null;
            Click("Keys");
            yield return null;
            Assert.IsFalse(Root.Find("Settings").gameObject.activeSelf);
            Assert.IsTrue(Root.Find("Rebind").gameObject.activeSelf);
            Click("Back");   // from KEY BINDINGS back to SOUND + ACCESS (it was opened from there)
            yield return null;
            Assert.IsTrue(Root.Find("Settings").gameObject.activeSelf);
            Assert.IsFalse(Root.Find("Rebind").gameObject.activeSelf);
            Click("Back");
            yield return null;
            Assert.IsFalse(Root.Find("Settings").gameObject.activeSelf);

            UIManager.I.OpenRebind();
            yield return null;
            Assert.IsTrue(Root.Find("Rebind").gameObject.activeSelf);
            GameEvents.Paused?.Invoke(false);   // the game resumes: no menu stays up
            yield return null;
            Assert.IsFalse(Root.Find("Rebind").gameObject.activeSelf);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator RebindScreen_ShowsKeys_AndAppliesACapturedKey()
        {
            UIManager.I.OpenRebind();
            yield return null;
            var panel = (RebindScreen)typeof(UIManager).GetField("_rebind", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(UIManager.I);
            var flipKey = Root.Find("Rebind/Panel/Key4").GetComponentInChildren<TMP_Text>();   // row 4 = FLIP LAYER
            var hackKey = Root.Find("Rebind/Panel/Key5").GetComponentInChildren<TMP_Text>();   // row 5 = HACK
            Assert.AreEqual("SPACE", flipKey.text); Assert.AreEqual("E", hackKey.text);
            Assert.AreEqual("A", Root.Find("Rebind/Panel/Pad4").GetComponentInChildren<TMP_Text>().text, "flip on the pad");
            Assert.AreEqual("L STICK", Root.Find("Rebind/Panel/Pad0").GetComponentInChildren<TMP_Text>().text, "move on the pad is the fixed stick");

            Click("Key4");
            Assert.IsTrue(Rebinds.Capturing, "clicking a key waits for the next press");
            Assert.AreEqual("PRESS...", flipKey.text);
            panel.Finish("<Keyboard>/f");   // what Tick does with a finished capture
            Assert.IsFalse(Rebinds.Capturing);
            Assert.AreEqual("F", flipKey.text);
            Assert.AreEqual("<Keyboard>/f", A11y.Get("a11y_bind_flip_k"));

            Click("Key5");
            panel.Finish("<Keyboard>/f");   // F is FLIP's now: the two swap
            Assert.AreEqual("F", hackKey.text);
            Assert.AreEqual("E", flipKey.text, "flip took hack's old key");

            Click("Key4");
            panel.Finish("");               // Esc: cancelled
            Assert.AreEqual("E", flipKey.text);

            Click("ResetKeys");
            Assert.AreEqual("SPACE", flipKey.text); Assert.AreEqual("E", hackKey.text);
            Assert.IsNull(A11y.Get("a11y_bind_flip_k"));
            yield return null;
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator HudSize_ScalesAboutTheCentre_SoCornersStayAnchored()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            var canvas = (RectTransform)Root;
            var hud = (RectTransform)Root.Find("Hud");
            Assume.That(canvas.rect.width > 1f && canvas.rect.height > 1f, "canvas has no size in this run");
            foreach (float s in new[] { 0.8f, 1f, 1.1f, 1.4f })
            {
                A11y.UiScale = s;
                GameEvents.SettingsChanged?.Invoke();
                Canvas.ForceUpdateCanvases();
                Assert.AreEqual(s, hud.localScale.x, 1e-4f);
                var a = new Vector3[4]; var b = new Vector3[4];
                hud.GetWorldCorners(a); canvas.GetWorldCorners(b);
                for (int i = 0; i < 4; i++) Assert.Less((a[i] - b[i]).magnitude, 0.5f, "scale " + s + ": the HUD still fills the canvas, corner " + i);
            }
            A11y.UiScale = 1f; GameEvents.SettingsChanged?.Invoke();
            Assert.AreEqual(Vector2.zero, hud.anchorMin); Assert.AreEqual(Vector2.one, hud.anchorMax); Assert.AreEqual(1f, hud.localScale.x);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator SubtitleSize_ScalesTheBar()
        {
            yield return null;
            var bar = Root.Find("Hud/Subtitle");
            Assert.IsNotNull(bar);
            Assert.AreEqual(1f, bar.localScale.x);
            A11y.SubtitleScale = 1.5f; GameEvents.SettingsChanged?.Invoke();
            Assert.AreEqual(1.5f, bar.localScale.x, 1e-4f);

            A11y.UiScale = 1.4f; A11y.SubtitleScale = 1.6f; GameEvents.SettingsChanged?.Invoke();
            Assert.LessOrEqual(980f * bar.localScale.x * 1.4f, 1800.5f, "the bar stays inside the 1920-wide screen at the largest HUD and subtitle sizes");
            Assert.Greater(bar.localScale.x, 1f, "and is still larger than normal");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator HighContrast_LiftsThePlates_AndSurvivesARebuild()
        {
            yield return null;
            float plain = Root.Find("Hud/Left").GetComponent<Image>().color.a;
            Assert.Less(plain, 0.7f);
            A11y.HighContrast = true;
            UIManager.I.Rebuild();
            yield return null;
            Assert.GreaterOrEqual(Root.Find("Hud/Left").GetComponent<Image>().color.a, 0.93f, "near-opaque plate behind the HUD text");
            A11y.HighContrast = false;
            UIManager.I.Rebuild();
            yield return null;
            Assert.Less(Root.Find("Hud/Left").GetComponent<Image>().color.a, 0.7f, "and back to the normal look");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator RemappedKeys_ShowOnTheHudCard_AndThePauseList()
        {
            yield return null;
            Assert.AreEqual("SPACE", Root.Find("Hud/ControlCard/Chip2/Key").GetComponent<TMP_Text>().text);
            Assert.IsTrue(Rebinds.Set(Rebinds.Find("flip"), false, "<Keyboard>/f", out _));
            Assert.IsTrue(Rebinds.Set(Rebinds.Find("up"), false, "<Keyboard>/i", out _));
            Assert.AreEqual("F", Root.Find("Hud/ControlCard/Chip2/Key").GetComponent<TMP_Text>().text, "the control card follows Rebinds.Changed");
            Assert.AreEqual("I A S D", Root.Find("Hud/ControlCard/Chip0/Key").GetComponent<TMP_Text>().text);
            Assert.IsTrue(Rebinds.Set(Rebinds.Find("hack"), false, "<Keyboard>/g", out _));
            Assert.AreEqual("G", Root.Find("Hud/Key/E").GetComponent<TMP_Text>().text, "and so does the interact key");

            GameState.Paused = true;
            GameEvents.Paused?.Invoke(true);
            yield return null; yield return null;
            Assert.AreEqual("F", Root.Find("Pause/Panel/Key2").GetComponent<TMP_Text>().text);
            Assert.AreEqual("I A S D", Root.Find("Pause/Panel/Key0").GetComponent<TMP_Text>().text);
            A11y.TabToggle = true;
            yield return null; yield return null;
            Assert.AreEqual("MAP / GOALS (TOGGLE)", Root.Find("Pause/Panel/Cap7").GetComponent<TMP_Text>().text);
            Rebinds.ResetAll();
            yield return null; yield return null;
            Assert.AreEqual("SPACE", Root.Find("Pause/Panel/Key2").GetComponent<TMP_Text>().text);
        }

        static RoomReport Report() => new RoomReport { room = -1, eventsScore = 1800, peakMultiplier = 2.25f, timeBonus = 320, total = 2120, time = 48.3f, medal = Medal.Gold, grade = 'A' };

        [UnityTest, Timeout(60000)]
        public IEnumerator Results_ShowTheAssistTag_OnlyWhenFlagged()
        {
            UIManager.I.SetMode(UIManager.Mode.Room, true);
            yield return null;
            GameEvents.RoomReportReady?.Invoke(Report());
            yield return null;
            Assert.IsTrue(Root.Find("Results").gameObject.activeSelf);
            Assert.IsFalse(Root.Find("Results/Panel/AssistTag").gameObject.activeSelf, "no tag without Assist");

            Assist.On = true; GameState.Phase = GameState.PhaseKind.Playing; GameState.Paused = false;
            AssistHost.Latch();   // played a moment with Assist on...
            Assist.On = false;    // ...and switched it off again: the attempt stays flagged
            GameEvents.RoomReportReady?.Invoke(Report());
            yield return null;
            Assert.IsTrue(Root.Find("Results/Panel/AssistTag").gameObject.activeSelf);
            StringAssert.Contains("ASSIST", Root.Find("Results/Panel/AssistTag/Label").GetComponent<TMP_Text>().text);

            AssistHost.Reset();
            Assist.On = true;
            GameEvents.RoomReportReady?.Invoke(Report());
            yield return null;
            Assert.IsTrue(Root.Find("Results/Panel/AssistTag").gameObject.activeSelf, "on right now counts too");
        }

        // ------------------------------------------------------------------ analytics relay
        [Test]
        public void Relay_RunStartEndAndDeath_CarryOnlyAnonymousFacts()
        {
            SettingsHost.OnStartRun(RunMode.Quick);
            Assert.AreEqual("run_start", events[0].name);
            StringAssert.Contains("\"mode\":\"Quick\"", events[0].json); StringAssert.Contains("\"assist\":false", events[0].json);

            RunState.Mode = RunMode.Quick; RunState.Won = true; RunState.RunScore = 4200; RunState.RunTime = 301.5f;
            RunState.Grades.Clear(); RunState.Grades.Add('S'); RunState.Grades.Add('A');
            SettingsHost.OnRunEnded();
            Assert.AreEqual("run_end", events[1].name);
            foreach (var part in new[] { "\"won\":true", "\"rooms\":2", "\"score\":4200", "\"time\":301.5" }) StringAssert.Contains(part, events[1].json);

            GameState.God = false; RunState.NodeId = "a1n2";
            SettingsHost.OnKilled(DeathCause.Ice);
            Assert.AreEqual("death", events[2].name);
            StringAssert.Contains("\"cause\":\"Ice\"", events[2].json); StringAssert.Contains("\"room\":\"a1n2\"", events[2].json);
            GameState.God = true;
            SettingsHost.OnKilled(DeathCause.Melt);
            Assert.AreEqual(3, events.Count, "god mode deaths are not reported");

            RunState.Mode = RunMode.Classic;
            SettingsHost.OnCompleted();
            Assert.AreEqual("run_end", events[3].name);
            StringAssert.Contains("\"mode\":\"Classic\"", events[3].json);
            RunState.Mode = RunMode.Quick;
            SettingsHost.OnCompleted();
            Assert.AreEqual(4, events.Count, "a BREACH run ends through RunEnded, not GameCompleted");

            foreach (var e in events) StringAssert.DoesNotContain(SaveService.Data.playerId, e.json);
        }

        [Test]
        public void SettingsChanged_ReportsOneEventPerChangedSetting_ThenNothing()
        {
            var go = new GameObject("TestSettingsHost");
            try
            {
                var host = go.AddComponent<SettingsHost>();   // Awake takes the baseline snapshot
                host.ReportSettings();
                Assert.AreEqual(0, events.Count, "nothing changed yet");

                A11y.UiScale = 1.2f; Assist.On = true; A11y.MusicVolume = 0.5f;
                Assert.IsTrue(Rebinds.Set(Rebinds.Find("flip"), false, "<Keyboard>/f", out _));
                host.ReportSettings();
                var names = new List<string>();
                foreach (var e in events) { Assert.AreEqual("settings_changed", e.name); names.Add(e.json); }
                Assert.AreEqual(4, events.Count);
                StringAssert.Contains("\"setting\":\"hud_scale\",\"value\":\"1.2\"", names[0] + names[1] + names[2] + names[3]);
                StringAssert.Contains("\"setting\":\"assist\",\"value\":\"1\"", string.Join("", names));
                StringAssert.Contains("\"setting\":\"vol_music\",\"value\":\"0.5\"", string.Join("", names));
                StringAssert.Contains("\"setting\":\"custom_binds\",\"value\":\"1\"", string.Join("", names), "the count of remapped keys, never which keys");
                StringAssert.DoesNotContain("<Keyboard>", string.Join("", names));

                events.Clear();
                host.ReportSettings();
                Assert.AreEqual(0, events.Count, "reported once");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void HighContrast_Pass_RaisesOnlyDarkPlates_AndBrightensDimText()
        {
            var root = new GameObject("HC", typeof(RectTransform)).transform;
            try
            {
                var plate = UIKit.Img(root, "Plate", new Color(UIKit.Panel.r, UIKit.Panel.g, UIKit.Panel.b, 0.6f));
                var glow = UIKit.Img(root, "Glow", new Color(1f, 0.15f, 0.2f, 0.4f));   // a threat colour: not a plate
                var faint = UIKit.Img(root, "Faint", new Color(0f, 0f, 0f, 0.1f));
                var dim = UIKit.Text(root, "Dim", null, 20, UIKit.Dim, TextAlignmentOptions.Left, "x");
                var paper = UIKit.Text(root, "Paper", null, 20, UIKit.Paper, TextAlignmentOptions.Left, "x");
                A11yUi.HighContrast(root);
                Assert.GreaterOrEqual(plate.color.a, 0.93f);
                Assert.AreEqual(0.4f, glow.color.a, 1e-4f);
                Assert.AreEqual(0.1f, faint.color.a, 1e-4f, "scrims that are not text plates stay as they were");
                Assert.AreEqual(Color.white, dim.color);
                Assert.AreEqual(UIKit.Paper, paper.color);
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }
    }
}
