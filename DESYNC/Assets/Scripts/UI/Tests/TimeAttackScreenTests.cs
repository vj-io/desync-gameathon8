using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Desync.Flow;
using Desync.Gen;
using Desync.Net;
using Desync.Rules;
using NUnit.Framework;
using TMPro;
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
    /// Time-Attack select: the 42-room grid with medals, best times and next targets, the focused room's detail, the lock,
    /// keyboard focus, BACK / Esc, and a click starting the room. Game, run and room managers are switched off; the save is
    /// redirected; the room build is stubbed out inside an empty stage scene so nothing heavy loads.
    public class TimeAttackScreenTests
    {
        const string SaveKey = "desync.save.tauitest";
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        GameObject _camGo;
        Scene _stage;
        readonly List<MonoBehaviour> _paused = new List<MonoBehaviour>();
        Action<RoomSpec, Transform, Action<IDisposable>> _realBuild;
        RoomSpec _built;

        [UnitySetUp]
        public IEnumerator Up()
        {
            _camGo = new GameObject("TaTestCam", typeof(Camera));
            var c = _camGo.GetComponent<Camera>(); c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.08f, 0.09f, 0.16f);
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb && mb.enabled && (mb.GetType().Name == "GameManager" || mb.GetType().Name == "RunManager" || mb.GetType().Name == "RoomManager"))
                { mb.enabled = false; _paused.Add(mb); }
            SaveService.Unload(SaveKey);
            PlayerPrefs.DeleteKey(SaveKey);
            _realBuild = RunManager.BuildRoom;
            RunManager.BuildRoom = (spec, parent, done) => _built = spec;   // never finishes: no room is built
            _built = null;
            yield return null;
            Assert.IsNotNull(UIManager.I, "UIManager did not self-bootstrap");
        }

        [UnityTearDown]
        public IEnumerator Down()
        {
            EndInput();
            if (TimeAttack.I) TimeAttack.I.Stop();
            TimeAttackRooms.DebugOpen = Debug.isDebugBuild;
            RunManager.BuildRoom = _realBuild;
            var ui = UIManager.I;
            if (ui) { ui.SetMode(UIManager.Mode.Title, true); ui.SetMode(UIManager.Mode.None, true); }
            if (_stage.IsValid() && _stage.isLoaded && SceneManager.sceneCount > 1) yield return SceneManager.UnloadSceneAsync(_stage);
            if (_camGo) Object.Destroy(_camGo);
            foreach (var mb in _paused) if (mb) mb.enabled = true;
            _paused.Clear();
            Cursor.lockState = CursorLockMode.None;
            PlayerPrefs.DeleteKey(SaveKey);
            SaveService.Unload();
        }

        static TimeAttackScreen Screen() =>
            typeof(UIManager).GetField("_ta", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(UIManager.I) as TimeAttackScreen;
        static TitleScreen Title() =>
            typeof(UIManager).GetField("_title", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(UIManager.I) as TitleScreen;

        static GameObject Find(string path) { var go = GameObject.Find(path); Assert.IsTrue(go && go.activeInHierarchy, "not found / not visible: " + path); return go; }
        static void Click(string path) =>
            ExecuteEvents.Execute(Find(path), new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerDownHandler);
        static void Hover(string path) =>
            ExecuteEvents.Execute(Find(path), new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
        static Color BarColor(int tile) => Find("TimeAttack/Panel/Tile" + tile + "/Bar").GetComponent<Image>().color;

        Keyboard _kb; InputSettings.BackgroundBehavior _bg;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode _gv;
#endif
        IEnumerator Key(Key k)
        {
            if (_kb == null)
            {
                _bg = InputSystem.settings.backgroundBehavior;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
                _gv = InputSystem.settings.editorInputBehaviorInPlayMode;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
                _kb = InputSystem.AddDevice<Keyboard>(); InputSystem.EnableDevice(_kb);
            }
            InputSystem.QueueStateEvent(_kb, new KeyboardState(k));
            yield return Frames(2);
            InputSystem.QueueStateEvent(_kb, new KeyboardState());
            yield return Frames(2);
        }

        void EndInput()
        {
            if (_kb == null) return;
            InputSystem.RemoveDevice(_kb); _kb = null;
            InputSystem.settings.backgroundBehavior = _bg;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = _gv;
#endif
        }

        static int Index(string id) { var all = TimeAttackRooms.All; for (int i = 0; i < all.Count; i++) if (all[i].id == id) return i; return -1; }

        [UnityTest, Timeout(120000)]
        public IEnumerator Grid_Shows_Medals_Best_Times_And_Next_Targets()
        {
            var save = SaveService.Data;
            var silver = TimeAttackRooms.Find("ta_a1_02");
            float tDev = TimeAttackRooms.TDev(silver);
            float best = Medals.Threshold(Medal.Silver, tDev) - 0.05f;   // just inside SILVER
            TimeAttackRooms.Record(save, silver, new RoomReport { room = -1, time = best, total = 2000, grade = 'B', medal = Medals.For(best, tDev) });
            save.medals["r2"] = "SYNC"; save.best["r2"] = new RoomBest { time = 20f, score = 3000, grade = "S" };

            var ui = UIManager.I; var ta = Screen();
            ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            ui.OpenTimeAttack();
            Assert.IsTrue(ta.IsOn);
            Assert.AreEqual(42, ta.TileCount);
            Assert.IsTrue(ta.Unlocked, "the editor opens it without the unlock");
            ui.TickAll(0.016f);
            Assert.IsTrue(Title().Blocked, "the title menu ignores keys under the screen");
            yield return Frames(60);   // one dev time per frame

            int si = Index("ta_a1_02"), ci = Index("r2"), none = Index("ta_a3_12");
            Assert.AreEqual(UIKit.ClockShort(best), ta.TileBest(si));
            Assert.AreEqual(RoomReports.NextTarget(best, tDev, 0, 0, 0, 0).Replace(" to ", " "), ta.TileNext(si));
            StringAssert.EndsWith("GOLD", ta.TileNext(si));
            Assert.AreEqual(UIKit.MedalColor((int)Medal.Silver), BarColor(si));
            Assert.AreEqual("0:20.00", ta.TileBest(ci));
            Assert.AreEqual("SYNC", ta.TileNext(ci));
            Assert.AreEqual(UIKit.MedalColor((int)Medal.Sync), BarColor(ci));
            Assert.AreEqual("--", ta.TileBest(none));
            Assert.AreEqual("", ta.TileNext(none));
            StringAssert.Contains("SYNC 1 / 42", Find("TimeAttack/Panel/Summary").GetComponent<TMP_Text>().text);

            Hover("TimeAttack/Panel/Tile" + si);
            Assert.AreEqual("ta_a1_02", ta.SelectedId);
            Assert.AreEqual("ACT 1 · ROOM 02", ta.NameText);
            StringAssert.Contains(RoomReports.NextTarget(best, tDev, 0, 0, 0, 0), ta.NextText, "\"0.8 s to GOLD\"");
            yield return Shot("ta01_select");

            Click("TimeAttack/Panel/Back");
            Assert.IsFalse(ta.IsOn);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Locked_Without_The_Unlock_Then_A_Click_Starts_The_Room()
        {
            TimeAttackRooms.DebugOpen = false;
            var ui = UIManager.I; var ta = Screen();
            ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            ui.OpenTimeAttack();
            Assert.IsFalse(ta.Unlocked);
            StringAssert.Contains("LOCKED", ta.StatusText);
            yield return Shot("ta02_locked");
            int i = Index("ta_a2_07");
            Click("TimeAttack/Panel/Tile" + i);
            Assert.IsTrue(ta.IsOn, "locked: nothing starts");
            Assert.IsFalse(TimeAttack.Live);
            ta.Hide();

            SaveService.Data.unlocks.Add(TimeAttackRooms.UnlockKey);   // the meta workstream's unlock
            var stage = SceneManager.GetSceneByName(RunManager.StageScene);   // already on the stage: no scene load
            if (!stage.IsValid() || !stage.isLoaded) stage = _stage = SceneManager.CreateScene(RunManager.StageScene);
            SceneManager.SetActiveScene(stage);
            ui.OpenTimeAttack();
            Assert.IsTrue(ta.Unlocked);
            Assert.AreEqual("", ta.StatusText);
            yield return Frames(2);
            Click("TimeAttack/Panel/Tile" + i);
            Assert.IsFalse(ta.IsOn, "the screen gives way to the room");
            Assert.IsTrue(TimeAttack.Live);
            Assert.AreEqual("ta_a2_07", TimeAttack.Current.id);
            yield return Frames(2);
            Assert.AreSame(TimeAttackRooms.Spec(TimeAttack.Current), _built, "the room's fixed spec goes to the builder");
            TimeAttack.I.Stop();
            ui.OpenTimeAttack();
            Assert.AreEqual("ta_a2_07", ta.SelectedId, "reopens on the room played last");
            ta.Hide();
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Keyboard_Moves_The_Focus_And_Esc_Closes()
        {
            var ui = UIManager.I; var ta = Screen();
            ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            ui.OpenTimeAttack();
            yield return new WaitForSecondsRealtime(0.4f);   // past the input guard
            int f = ta.Focus;
            yield return Key(UnityEngine.InputSystem.Key.RightArrow);
            Assert.AreEqual(f + 1, ta.Focus);
            Assert.AreEqual(TimeAttackRooms.All[f + 1].id, ta.SelectedId, "focusing a tile selects it");
            yield return Key(UnityEngine.InputSystem.Key.LeftArrow);
            yield return Key(UnityEngine.InputSystem.Key.LeftArrow);
            Assert.AreEqual((f - 1 + ta.TileCount + 2) % (ta.TileCount + 2), ta.Focus, "wraps through BACK / START");
            yield return Key(UnityEngine.InputSystem.Key.Escape);
            Assert.IsFalse(ta.IsOn);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Hud_And_Results_Name_A_Generated_Room_By_Its_Id()
        {
            TimeAttackRooms.DebugOpen = true;
            var ui = UIManager.I;
            var stage = SceneManager.GetSceneByName(RunManager.StageScene);   // the build is stubbed: no room appears
            if (!stage.IsValid() || !stage.isLoaded) stage = _stage = SceneManager.CreateScene(RunManager.StageScene);
            SceneManager.SetActiveScene(stage);
            Assert.IsTrue(TimeAttack.Play("ta_a1_07"));
            ui.SetMode(UIManager.Mode.Room, true);
            ui.TickAll(0.016f);
            Assert.AreEqual("A1-07", Find("Hud/Left/Room").GetComponent<TMP_Text>().text);
            var results = (ResultsScreen)typeof(UIManager).GetField("_results", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(ui);
            results.Show(new RoomReport { room = -1, time = 12.3f, medal = Medal.Gold, total = 2400, grade = 'A' });
            Assert.AreEqual("A1-07 CLEARED", Find("Results/Panel/Head").GetComponent<TMP_Text>().text);
            results.Hide();
            TimeAttack.I.Stop();
            ui.TickAll(0.016f);
            StringAssert.DoesNotContain("A1-07", Find("Hud/Left/Room").GetComponent<TMP_Text>().text, "out of Time-Attack the label names the room again");
            yield return null;
        }

        // ------------------------------------------------------------------ screenshots (.work/timeattack/shots, best effort)
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
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = _rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            try
            {
                var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".work", "timeattack", "shots"));
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            }
            catch (Exception e) { Debug.LogWarning("[UI-TEST] shot " + name + " not written: " + e.Message); }
            Object.Destroy(tex);
            cam.targetTexture = null;
            ui.Canvas.renderMode = rmode; ui.Canvas.worldCamera = wcam; cs.uiScaleMode = mode; cs.scaleFactor = scale;
        }
    }
}
