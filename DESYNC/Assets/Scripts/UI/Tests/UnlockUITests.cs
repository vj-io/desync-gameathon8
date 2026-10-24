using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Desync.Art;
using Desync.Content;
using Desync.Flow;
using Desync.Net;
using Desync.Rules;
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
    /// M4 meta UI: the unlock tree (cards, two-step buying, starters, cosmetic buttons, keyboard and pad), the 30-level Deep Layer select
    /// with its rewards, and the flip effect cosmetic. Flow's managers are switched off so nothing loads; the save is redirected to a
    /// throw-away key. Screenshots go to .work/meta/shots (best effort).
    public class UnlockUITests
    {
        const string SaveKey = "desync.save.metauitest";
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        GameObject _camGo;
        readonly List<MonoBehaviour> _paused = new List<MonoBehaviour>();
        readonly List<string> _log = new List<string>();   // "deep:25:..." requests the UI raised

        // ------------------------------------------------------------------ setup
        [UnitySetUp]
        public IEnumerator Up()
        {
            _camGo = new GameObject("MetaTestCam", typeof(Camera));
            var c = _camGo.GetComponent<Camera>(); c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.08f, 0.09f, 0.16f);
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb && mb.enabled && (mb.GetType().Name == "GameManager" || mb.GetType().Name == "RunManager" || mb.GetType().Name == "RoomManager"))
                { mb.enabled = false; _paused.Add(mb); }
            SaveService.Unload(SaveKey);
            PlayerPrefs.DeleteKey(SaveKey);
            Reset();
            _log.Clear();
            DeepRun.StartRequested += OnDeep;
            yield return null;
            Assert.IsNotNull(UIManager.I, "UIManager did not self-bootstrap");
        }

        [TearDown]
        public void Down()
        {
            EndInput();
            DeepRun.StartRequested -= OnDeep;
            Palette.Colorblind = false; PlayerPrefs.DeleteKey(Palette.Key); UIKit.ApplyPalette();
            Settings.ReduceFx = false; Settings.ReduceFlashes = false; UIKit.RefreshSettings();
            Reset();
            RunEvents.PhaseChanged?.Invoke(RunPhase.None);
            var ui = UIManager.I;
            if (ui) { ui.SetMode(UIManager.Mode.Title, true); ui.SetMode(UIManager.Mode.None, true); }
            if (_camGo) Object.Destroy(_camGo);
            foreach (var mb in _paused) if (mb) mb.enabled = true;
            _paused.Clear();
            PlayerPrefs.DeleteKey(SaveKey);
            SaveService.Unload();
        }

        void OnDeep(int level, IReadOnlyList<MutatorPick> picks)
        {
            var sb = new System.Text.StringBuilder("deep:" + level + ":");
            for (int i = 0; i < picks.Count; i++) sb.Append(i > 0 ? "," : "").Append(picks[i].id).Append(picks[i].rank);
            _log.Add(sb.ToString());
        }

        static void Reset()
        {
            RunState.Mode = RunMode.Classic; RunState.Phase = RunPhase.None; RunState.Graph = null; RunState.NodeId = null; RunState.Act = 1;
            RunState.Held.Clear(); RunState.Offer.Clear(); RunState.Market.Clear();
            RunView.ResetRun();
            DeepRun.Level = 0; DeepRun.Picks.Clear(); DeepRun.Rewards.Clear();
            GameState.Paused = false; GameState.Alive = true;
            PlayerRef.Cam = null; PlayerRef.Root = null;
        }

        /// A profile that beat the ARCHITECT and won Deep Layer `best`, holding `fragments`.
        static SaveV3 Profile(int best, int fragments = 0)
        {
            var save = SaveService.Data;
            if (!save.unlocks.Contains("kill_architect")) save.unlocks.Add("kill_architect");
            save.deepLayerMax = best;
            save.fragments = fragments;
            return save;
        }

        static T Screen<T>(string field) where T : class =>
            typeof(UIManager).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(UIManager.I) as T;

        static GameObject Find(string path)
        {
            var t = UIManager.I.Canvas.transform.Find(path);
            Assert.IsNotNull(t, "not found: " + path);
            Assert.IsTrue(t.gameObject.activeInHierarchy, "not visible: " + path);
            return t.gameObject;
        }

        static void Click(string path)
        {
            var ev = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(Find(path), ev, ExecuteEvents.pointerDownHandler);
        }

        static void Hover(string path) =>
            ExecuteEvents.Execute(Find(path), new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);

        static string Txt(string path) { var t = Find(path).GetComponent<TMP_Text>(); t.ForceMeshUpdate(); return t.GetParsedText(); }

        static string Card(string id) => "Unlocks/Panel/Card_" + id;

        UnlockTree OpenTree(int fragments = 0, int best = -1)
        {
            if (best >= 0) Profile(best, fragments); else SaveService.Data.fragments = fragments;
            var ui = UIManager.I;
            ui.SetMode(UIManager.Mode.Title, true);
            ui.OpenUnlocks();
            return Screen<UnlockTree>("_unlocks");
        }

        // ------------------------------------------------------------------ input
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
        static string ShotDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".work", "meta", "shots"));
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

        // ================================================================== the unlock tree
        [UnityTest, Timeout(120000)]
        public IEnumerator Tree_Opens_On_Every_Node_With_Its_Cost_And_State()
        {
            var tree = OpenTree(200);
            yield return Frames(2);
            Assert.IsTrue(tree.IsOn);
            Assert.AreEqual(Unlocks.Data.nodes.Count, tree.CardCount);
            Assert.AreEqual("FRAGMENTS 200", tree.FragmentsText);
            Assert.AreEqual("OWNED", tree.CardState("pool_a"), "the first 8 exploits are free");
            Assert.AreEqual("OWNED", tree.CardState("daily"), "Daily visible from the start");
            Assert.AreEqual("BUY", tree.CardState("pool_b"));
            Assert.AreEqual("BUY", tree.CardState("starter_ghost"));
            Assert.AreEqual("LOCKED", tree.CardState("pool_c"), "behind EXPLOITS II");
            Assert.AreEqual("LOCKED", tree.CardState("starter_phantom"), "behind the GHOST start");
            Assert.AreEqual("BUY", tree.CardState("blackmarket"));
            Assert.AreEqual("BUY", tree.CardState("timeattack"));
            Assert.AreEqual("90", Txt(Card("pool_b") + "/Cost"));
            Assert.AreEqual("EXPLOITS II", Txt(Card("pool_b") + "/Name"));
            Assert.AreEqual("8 EXPLOITS", Txt(Card("pool_b") + "/Sub"));
            Assert.AreEqual("CLEAN HANDSHAKE", Txt(Card("starter_ghost") + "/Sub"));
            Assert.AreEqual("1-2 H", Txt(Card("pool_b") + "/When"));
            yield return Shot("meta01_unlock_tree");
            StringAssert.Contains("EXPLOITS", tree.DetailName, "focus starts on the first card you can buy");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Buying_Takes_Two_Presses_And_Spends_Once()
        {
            var tree = OpenTree(200);
            yield return Frames(2);
            int cost = Unlocks.Data.Find("pool_b").cost;
            Click(Card("pool_b"));
            Assert.AreEqual(tree.IndexOf("pool_b"), tree.Armed, "the first press arms");
            StringAssert.Contains("PRESS AGAIN", tree.StatusText);
            Assert.AreEqual(200, SaveService.Data.fragments, "nothing spent yet");
            Assert.IsFalse(Unlocks.Has("unlock_pool_b"));
            Click(Card("pool_b"));
            Assert.AreEqual(-1, tree.Armed);
            Assert.AreEqual(200 - cost, SaveService.Data.fragments);
            Assert.AreEqual("OWNED", tree.CardState("pool_b"));
            Assert.IsTrue(Unlocks.Has("unlock_pool_b"));
            Assert.AreEqual("FRAGMENTS " + (200 - cost), tree.FragmentsText);
            StringAssert.Contains("UNLOCKED", tree.StatusText);
            Click(Card("pool_b"));
            Assert.AreEqual(200 - cost, SaveService.Data.fragments, "an owned card never charges");
            Assert.AreEqual(-1, tree.Armed);
            Assert.AreEqual("BUY", tree.CardState("pool_c"), "the next link of the chain opens");
            SaveService.Flush(); SaveService.Unload(SaveKey);
            Assert.IsTrue(Unlocks.Has("unlock_pool_b"), "written to the profile");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Locked_And_Short_Cards_Explain_Themselves_And_Never_Spend()
        {
            var tree = OpenTree(50);
            yield return Frames(2);
            Click(Card("pool_c"));
            Assert.AreEqual("BUY EXPLOITS II FIRST", tree.StatusText);
            Assert.AreEqual(-1, tree.Armed);
            Assert.AreEqual("NEED 40 MORE", tree.CardState("pool_b"), "90 for EXPLOITS II, 50 held");
            Click(Card("pool_b"));
            Assert.AreEqual("NEED 40 MORE FRAGMENTS", tree.StatusText);
            Click(Card("pool_b")); Click(Card("pool_b"));
            Assert.AreEqual(50, SaveService.Data.fragments);
            CollectionAssert.DoesNotContain(SaveService.Data.unlocks, "unlock_pool_b");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Moving_The_Focus_Cancels_An_Armed_Purchase()
        {
            var tree = OpenTree(500);
            yield return Frames(2);
            Click(Card("pool_b"));
            Assert.AreEqual(tree.IndexOf("pool_b"), tree.Armed);
            Hover(Card("timeattack"));
            Assert.AreEqual(-1, tree.Armed, "the pointer moved to another card");
            Click(Card("pool_b"));   // arms again, then the same card again would buy
            Assert.AreEqual(tree.IndexOf("pool_b"), tree.Armed);
            tree.Tick(5f);           // the arm times out
            Assert.AreEqual(-1, tree.Armed);
            Click(Card("pool_b"));
            Assert.AreEqual(500, SaveService.Data.fragments, "an expired arm does not buy on the next press");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Starters_Are_Worn_At_Once_One_At_A_Time_And_Toggle_Off()
        {
            var tree = OpenTree(600);
            yield return Frames(2);
            Click(Card("starter_ghost")); Click(Card("starter_ghost"));
            Assert.AreEqual("EQUIPPED", tree.CardState("starter_ghost"), "the first starter bought is worn at once");
            Assert.AreEqual("starter_ghost", Unlocks.Starter());
            Click(Card("starter_phantom")); Click(Card("starter_phantom"));
            Assert.AreEqual("OWNED", tree.CardState("starter_phantom"), "a second purchase leaves the first worn");
            Assert.AreEqual("EQUIPPED", tree.CardState("starter_ghost"));
            Click(Card("starter_phantom"));
            Assert.AreEqual("EQUIPPED", tree.CardState("starter_phantom"), "one at a time");
            Assert.AreEqual("OWNED", tree.CardState("starter_ghost"));
            StringAssert.Contains("GHOST PING", tree.StatusText);
            Click(Card("starter_phantom"));
            Assert.AreEqual("OWNED", tree.CardState("starter_phantom"));
            Assert.AreEqual("", Unlocks.Starter());
            StringAssert.Contains("EMPTY-HANDED", tree.StatusText);
            yield return Shot("meta02_unlock_tree_bought");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Keyboard_And_Pad_Walk_The_Cards_And_Buy()
        {
            var ui = UIManager.I; var title = Screen<TitleScreen>("_title");
            var tree = OpenTree(900);
            yield return new WaitForSecondsRealtime(0.4f);   // input guard
            int b = tree.IndexOf("pool_b");
            Assert.AreEqual(b, tree.Focus, "focus starts on the first card you can buy");
            ui.TickAll(0.016f);
            Assert.IsTrue(title.Blocked, "the title ignores its keys while the tree is up");

            yield return Key(UnityEngine.InputSystem.Key.Space);
            Assert.AreEqual(b, tree.Armed, "SPACE arms");
            yield return Key(UnityEngine.InputSystem.Key.Space);
            Assert.AreEqual("OWNED", tree.CardState("pool_b"), "SPACE again buys");
            yield return Key(UnityEngine.InputSystem.Key.RightArrow);
            Assert.AreEqual(b + 1, tree.Focus);
            yield return Key(UnityEngine.InputSystem.Key.LeftArrow); yield return Key(UnityEngine.InputSystem.Key.LeftArrow);
            Assert.AreEqual(b - 1, tree.Focus);

            // the ring wraps backwards from the first card to BACK (through the two cosmetic buttons)
            yield return Key(UnityEngine.InputSystem.Key.LeftArrow);
            Assert.AreEqual(tree.BackIdx, tree.Focus);

            // pad: d-pad moves, A acts
            yield return Pad(GamepadButton.DpadRight);                  // wraps to the first card
            Assert.AreEqual(0, tree.Focus);
            for (int i = 0; i < tree.IndexOf("timeattack"); i++) yield return Pad(GamepadButton.DpadRight);
            yield return Pad(GamepadButton.South); yield return Pad(GamepadButton.South);
            Assert.AreEqual("OWNED", tree.CardState("timeattack"));
            Assert.IsTrue(Unlocks.Has(Unlocks.TimeAttack), "the key Time-Attack reads");

            // BACK closes it and the title is usable again
            Click("Unlocks/Panel/Back");
            Assert.IsFalse(tree.IsOn);
            ui.TickAll(0.016f);
            Assert.IsFalse(title.Blocked);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Cosmetic_Buttons_Step_Through_What_The_Deep_Layer_Paid()
        {
            var tree = OpenTree(0);
            yield return Frames(2);
            StringAssert.Contains("EARN IN THE DEEP LAYER", tree.FlipLabel);
            Click("Unlocks/Panel/Flip");
            StringAssert.Contains("WIN DEEP LAYER 12", tree.StatusText);
            Assert.IsNull(Cosmetics.Equipped(CosmeticSlot.Flip));

            ui_ClosePanels();
            Profile(10, 0);
            RunUnlocks.RecordDeepWin(SaveService.Data, 14);   // title deep_diver + heat_walker, flips ripple + shards
            tree = OpenTree(0);
            yield return Frames(2);
            StringAssert.Contains("RIPPLE", tree.FlipLabel);
            StringAssert.Contains("DEEP DIVER", tree.TitleLabel);
            StringAssert.Contains("DEEP DIVER", Txt("Unlocks/Panel/Worn"));
            Click("Unlocks/Panel/Flip");
            StringAssert.Contains("SHARDS", tree.FlipLabel);
            Assert.AreEqual("shards", Cosmetics.Equipped(CosmeticSlot.Flip).id);
            Click("Unlocks/Panel/Flip");
            StringAssert.Contains("NONE", tree.FlipLabel);
            Assert.IsNull(Cosmetics.Equipped(CosmeticSlot.Flip));
            Click("Unlocks/Panel/Flip");
            StringAssert.Contains("RIPPLE", tree.FlipLabel);
            Click("Unlocks/Panel/Title");
            StringAssert.Contains("HEAT WALKER", tree.TitleLabel);
            StringAssert.Contains("HEAT WALKER", Txt("Unlocks/Panel/Worn"));
            yield return Shot("meta03_unlock_tree_cosmetics");
        }

        static void ui_ClosePanels()
        {
            var ui = UIManager.I;
            ui.SetMode(UIManager.Mode.None, true); ui.SetMode(UIManager.Mode.Title, true);
        }

        // ================================================================== Deep Layer select: 30 levels and their rewards
        [UnityTest, Timeout(120000)]
        public IEnumerator Deep_Layer_Select_Shows_Thirty_Levels_With_Their_Rewards()
        {
            var ui = UIManager.I; var deep = Screen<DeepLayerScreen>("_deep");
            Profile(11);   // DL 1-12 open, the ARCHITECT beaten
            ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            Screen<TitleScreen>("_title").OnDeep();
            Assert.IsTrue(deep.IsOn);
            Assert.AreEqual(12, deep.MaxLevel); Assert.AreEqual(12, deep.Level);
            for (int level = 1; level <= DeepRules.MaxLevel; level++) Assert.IsNotNull(UIManager.I.Canvas.transform.Find("DeepLayer/Panel/Level" + level), "DL " + level + " button");
            Assert.AreEqual("DL 12", Txt("DeepLayer/Panel/Level12/Label"));
            StringAssert.Contains("30", Txt("DeepLayer/Panel/Level30/Label"), "locked levels are shown, dimmed");
            StringAssert.DoesNotContain("DL", Txt("DeepLayer/Panel/Level30/Label"));
            Assert.AreEqual("HEAT 12 / 12", Txt("DeepLayer/Panel/HeatTotal"));
            StringAssert.Contains("+120 %", deep.RewardText);
            StringAssert.Contains("RIPPLE", deep.PrizeText); StringAssert.Contains("FLIP EFFECT", deep.PrizeText); StringAssert.Contains("WIN DL 12 TO EARN", deep.PrizeText);
            yield return Shot("meta04_deep_layer_30");

            Click("DeepLayer/Panel/Level11");
            StringAssert.Contains("DEEP DIVER", deep.PrizeText); StringAssert.Contains("TITLE", deep.PrizeText);
            Click("DeepLayer/Panel/Level10");
            StringAssert.Contains("FRAGMENTS ONLY", deep.PrizeText);
            Click("DeepLayer/Panel/Level30");
            Assert.AreEqual(10, deep.Level, "a locked level does nothing");

            // once DL 12 is won its reward reads EARNED · WORN
            RunUnlocks.RecordDeepWin(SaveService.Data, 12);
            ui.SetMode(UIManager.Mode.None, true); ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            Screen<TitleScreen>("_title").OnDeep();
            Click("DeepLayer/Panel/Level12");
            StringAssert.Contains("EARNED · WORN", deep.PrizeText);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Deep_Layer_Select_Starts_A_Deep_Level_And_Explains_The_Top_Of_The_Ladder()
        {
            var ui = UIManager.I; var deep = Screen<DeepLayerScreen>("_deep");
            Profile(29);   // every level open
            ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            Screen<TitleScreen>("_title").OnDeep();
            Assert.AreEqual(30, deep.MaxLevel); Assert.AreEqual(30, deep.Level, "opens on the newest open level");
            Assert.AreEqual(30, deep.Heat); Assert.IsTrue(deep.CanStart);
            StringAssert.Contains("SYNC GOLD", deep.PrizeText); StringAssert.Contains("WIN DL 30 TO EARN", deep.PrizeText);
            StringAssert.Contains("+200 %", deep.RewardText); StringAssert.Contains("(CAP)", deep.RewardText);
            StringAssert.Contains("600", deep.RewardText, "a 200 run banks 600 at the cap");
            yield return Shot("meta05_deep_layer_30_open");

            Click("DeepLayer/Panel/Level25");
            Assert.AreEqual(25, deep.Level); Assert.AreEqual(25, deep.Heat);
            StringAssert.Contains("NO REWARD", deep.PrizeText); StringAssert.Contains("STOPS AT DL 20", deep.PrizeText);
            foreach (var p in DeepRules.Preset(25)) Assert.AreEqual(p.rank, deep.RankOf(p.id), "preset " + p.id);

            Click("DeepLayer/Panel/Level20");
            StringAssert.Contains("+200 %", deep.RewardText); StringAssert.DoesNotContain("(CAP)", deep.RewardText);
            Click("DeepLayer/Panel/Level30");
            Click("DeepLayer/Panel/Start");
            var want = DeepRules.Preset(30);
            var sb = new System.Text.StringBuilder("deep:30:");
            for (int i = 0; i < want.Count; i++) sb.Append(i > 0 ? "," : "").Append(want[i].id).Append(want[i].rank);
            CollectionAssert.AreEqual(new[] { sb.ToString() }, _log.ToArray());
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Deep_Layer_Select_Focus_Ring_Spans_All_Thirty_Levels()
        {
            var ui = UIManager.I; var deep = Screen<DeepLayerScreen>("_deep");
            Profile(29);
            ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            Screen<TitleScreen>("_title").OnDeep();
            Assert.AreEqual(deep.TileCount + 30 + 2, deep.Focus, "focus starts on START");
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Key(UnityEngine.InputSystem.Key.RightArrow);   // wraps START -> DL 1
            Assert.AreEqual(0, deep.Focus);
            for (int i = 0; i < 24; i++) yield return Key(UnityEngine.InputSystem.Key.RightArrow);
            Assert.AreEqual(24, deep.Focus);
            yield return Key(UnityEngine.InputSystem.Key.Space);
            Assert.AreEqual(25, deep.Level, "SPACE on DL 25");
        }

        // ================================================================== the flip effect cosmetic
        static float HueDelta(Color a, Color b)
        {
            Color.RGBToHSV(a, out float h1, out _, out _); Color.RGBToHSV(b, out float h2, out _, out _);
            float d = Mathf.Abs(h1 - h2) * 360f;
            return d > 180f ? 360f - d : d;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Flip_Effect_Plays_From_A_Pool_Fades_Out_And_Only_When_Worn()
        {
            var ui = UIManager.I; var fx = Screen<FlipFxLayer>("_flipFx");
            Profile(10);
            RunUnlocks.RecordDeepWin(SaveService.Data, 18);   // ripple, shards, afterglow, streak
            ui.SetMode(UIManager.Mode.Title, true);
            Cosmetics.Equip(CosmeticSlot.Flip, "");
            yield return Frames(2);
            GameEvents.Flipped?.Invoke(true);
            Assert.AreEqual(0, fx.Alive, "nothing worn: nothing plays");

            int[] want = { 1, 8, 2, 2 };   // images per effect: ripple, shards, afterglow, streak
            string[] ids = { "ripple", "shards", "afterglow", "streak" };
            for (int i = 0; i < ids.Length; i++)
            {
                Cosmetics.Equip(CosmeticSlot.Flip, ids[i]);
                GameEvents.Flipped?.Invoke(i % 2 == 0);
                Assert.AreEqual(want[i], fx.Alive, ids[i]);
                for (int k = 0; k < 8; k++) fx.Tick(0.1f);
                Assert.AreEqual(0, fx.Alive, ids[i] + " fades out within a second");
            }
            Cosmetics.Equip(CosmeticSlot.Flip, "ripple");
            GameEvents.Flipped?.Invoke(true);
            yield return Shot("meta06_flip_ripple");
            for (int k = 0; k < 8; k++) fx.Tick(0.1f);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Flip_Effect_Honours_Reduce_FX_And_Reduce_Flashing()
        {
            var ui = UIManager.I; var fx = Screen<FlipFxLayer>("_flipFx");
            Profile(11);
            RunUnlocks.RecordDeepWin(SaveService.Data, 12);
            ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            Settings.ReduceFx = true; UIKit.RefreshSettings();
            GameEvents.Flipped?.Invoke(true);
            Assert.AreEqual(0, fx.Alive, "REDUCE FX: no flip effect");
            Settings.ReduceFx = false; Settings.ReduceFlashes = true; UIKit.RefreshSettings();
            GameEvents.Flipped?.Invoke(false);
            Assert.AreEqual(0, fx.Alive, "REDUCE FLASHING: no flip effect");
            Settings.ReduceFlashes = false; UIKit.RefreshSettings();
            GameEvents.Flipped?.Invoke(false);
            Assert.AreEqual(1, fx.Alive);
            for (int k = 0; k < 8; k++) fx.Tick(0.1f);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Flip_Effects_Draw_Only_Palette_Hues_Never_A_Threat_Hue()
        {
            foreach (var cb in new[] { false, true })
            {
                Palette.Colorblind = cb; UIKit.ApplyPalette();
                foreach (var c in Cosmetics.All)
                {
                    if (c.slot != CosmeticSlot.Flip) continue;
                    foreach (var toSource in new[] { true, false })
                    {
                        var col = FlipFxLayer.Tint(c.id, toSource);
                        Assert.Greater(HueDelta(col, UIKit.Danger), 15f, c.id + (cb ? " (colour-blind)" : "") + ": " + col + " sits on the threat hue");
                        Assert.Greater(HueDelta(col, Palette.CbDanger), 15f, c.id + ": " + col + " sits on the colour-blind threat hue");
                        Assert.Greater(HueDelta(col, Palette.Danger), 15f);
                    }
                }
            }
            Assert.AreEqual(UIKit.MedalColor(3), FlipFxLayer.Tint("sync_gold", true), "SYNC gold in both layers");
            yield return null;
        }

        static object _sink;

        [UnityTest, Timeout(120000)]
        public IEnumerator Flip_Effects_Allocate_Nothing_Per_Flip_Or_Frame()
        {
            var ui = UIManager.I; var fx = Screen<FlipFxLayer>("_flipFx");
            Profile(10);
            RunUnlocks.RecordDeepWin(SaveService.Data, 30);   // every flip effect: ripple .. streak and SYNC GOLD
            ui.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
            Assert.Greater(Allocs.Count(() => _sink = new byte[64]), 0, "the instrument sees a known allocation");
            int frame = 0;
            Action tick = () => { if (frame % 15 == 0) fx.OnFlipped(frame % 30 == 0); fx.Tick(0.016f); };
            foreach (var id in new[] { "sync_gold", "afterglow", "shards" })
            {
                Cosmetics.Equip(CosmeticSlot.Flip, id);
                if (Cosmetics.Equipped(CosmeticSlot.Flip) == null) continue;
                for (int i = 0; i < 40; i++) { if (i % 12 == 0) fx.OnFlipped(i % 24 == 0); fx.Tick(0.016f); }   // warm-up
                int total = 0; const int ticks = 300;
                for (frame = 0; frame < ticks; frame++) total += Allocs.Beyond(tick);
                Debug.Log("[UI-TEST] allocations over " + ticks + " ticks of the " + id + " flip effect: " + total);
                Assert.AreEqual(0, total, id + " allocates per flip or frame");
                for (int k = 0; k < 20; k++) fx.Tick(0.1f);
            }
        }
    }
}
