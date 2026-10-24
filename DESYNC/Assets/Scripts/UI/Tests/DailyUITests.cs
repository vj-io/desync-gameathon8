using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Desync.Flow;
using Desync.Net;
using Desync.Rules;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Desync.UI.Tests
{
    /// The Daily screen (UIManager.OpenDaily): the day's seed and mutator pair, the scored attempt / practice state, the share block and
    /// [C], the board offline and online, the Weekly Ladder lock, and what START / WEEKLY request. Flow, run and room managers are
    /// switched off; the save goes to a throw-away key; the leaderboard is offline (BaseUrl null) unless a board is handed in.
    public class DailyUITests
    {
        const string SaveKey = "desync.save.dailyuitest";
        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        GameObject _camGo;
        string _base;
        Action<RunMode, uint> _begin;
        readonly List<MonoBehaviour> _paused = new List<MonoBehaviour>();
        readonly List<string> _log = new List<string>();

        [UnitySetUp]
        public IEnumerator Up()
        {
            _camGo = new GameObject("DailyTestCam", typeof(Camera));
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb && mb.enabled && (mb.GetType().Name == "GameManager" || mb.GetType().Name == "RunManager" || mb.GetType().Name == "RoomManager"))
                { mb.enabled = false; _paused.Add(mb); }
            SaveService.Unload(SaveKey);
            PlayerPrefs.DeleteKey(SaveKey);
            _base = Leaderboard.BaseUrl; Leaderboard.BaseUrl = null;
            _begin = DailyRun.BeginRun; DailyRun.BeginRun = (m, s) => _log.Add("begin:" + m + ":" + s);
            _log.Clear();
            RunEvents.StartRun += OnStart; GameEvents.CopyRequested += OnCopy;
            yield return null;
            Assert.IsNotNull(UIManager.I, "UIManager did not self-bootstrap");
            UIManager.I.SetMode(UIManager.Mode.Title, true);
            yield return Frames(2);
        }

        [TearDown]
        public void Down()
        {
            RunEvents.StartRun -= OnStart; GameEvents.CopyRequested -= OnCopy;
            Leaderboard.BaseUrl = _base; DailyRun.BeginRun = _begin;
            var ui = UIManager.I;
            if (ui) { Daily()?.Hide(); ui.SetMode(UIManager.Mode.None, true); }
            if (_camGo) Object.Destroy(_camGo);
            foreach (var mb in _paused) if (mb) mb.enabled = true;
            _paused.Clear();
            PlayerPrefs.DeleteKey(SaveKey);
            SaveService.Unload();
        }

        void OnStart(RunMode m) => _log.Add("start:" + m);
        void OnCopy(string s) => _log.Add("copy:" + s);

        static DailyScreen Daily() =>
            typeof(UIManager).GetField("_daily", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(UIManager.I) as DailyScreen;

        static void Click(string path)
        {
            var go = GameObject.Find(path);
            Assert.IsTrue(go && go.activeInHierarchy, "not visible: " + path);
            ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerDownHandler);
        }

        static string Label(string path) { var t = GameObject.Find(path).GetComponent<TMP_Text>(); t.ForceMeshUpdate(); return t.GetParsedText(); }

        [UnityTest, Timeout(120000)]
        public IEnumerator Fresh_Day_Shows_Seed_Pair_Offline_Board_And_Starts_The_Daily()
        {
            var d = Daily();
            UIManager.I.OpenDaily();
            yield return Frames(2);
            Assert.IsTrue(d.IsOn);
            string day = LeaderboardScore.Day(DateTime.UtcNow);
            uint seed = LeaderboardScore.Seed(day);
            Assert.AreEqual(day + " UTC", Label("Daily/Panel/Date"));
            Assert.AreEqual("SEED " + seed, Label("Daily/Panel/Seed"));
            foreach (var m in DailyRun.Mutators(seed, RunView.Cat)) StringAssert.Contains(m.name.ToUpperInvariant(), d.MutatorText);
            Assert.AreEqual("SCORED ATTEMPT READY", d.Status);
            Assert.AreEqual("START", Label("Daily/Panel/Start/Label"));
            Assert.AreEqual("LOCKED", Label("Daily/Panel/Weekly/Label"));
            StringAssert.Contains("FIRST AUDITOR KILL", d.WeekText);
            Assert.AreEqual("LEADERBOARD OFFLINE\nNO LOCAL SCORE YET", d.RowsText);
            UIManager.I.TickAll(0.016f);
            Assert.IsTrue(Screen<TitleScreen>("_title").Blocked, "the title ignores its keys while the screen is up");
            yield return Shot("daily_fresh");

            Click("Daily/Panel/Weekly");
            Assert.IsEmpty(_log, "the ladder is locked");
            Click("Daily/Panel/Copy");
            Assert.IsEmpty(_log, "nothing to copy yet");
            Click("Daily/Panel/Start");
            CollectionAssert.AreEqual(new[] { "start:Daily" }, _log);
            Assert.IsFalse(d.IsOn, "the run takes over");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Scored_Day_Shows_Practice_Share_Block_Board_And_Weekly()
        {
            var d = Daily();
            var save = SaveService.Data;
            string day = LeaderboardScore.Day(DateTime.UtcNow), week = LeaderboardScore.Week(DateTime.UtcNow);
            string share = DailyRun.ShareBlock(day, false, new[] { Medal.Gold, Medal.Sync, Medal.Silver, Medal.Bronze, Medal.Gold }, new[] { 'S', 'S', 'A', 'B', 'S' }, 13640, 271.2f);
            save.settings[DailyRun.ScoredKey] = day;
            save.settings[DailyRun.ShareKey] = share;
            save.dailyBest[day] = 13640;
            save.unlocks.Add("kill_auditor");
            save.settings[DailyRun.WeekSaveKey(week)] = "34000";

            UIManager.I.OpenDaily();
            yield return Frames(2);
            Assert.AreEqual("TODAY 13640 · MORE RUNS ARE PRACTICE", d.Status);
            Assert.AreEqual("PRACTICE", Label("Daily/Panel/Start/Label"));
            Assert.AreEqual(share, d.ShareText);
            StringAssert.Contains("13640 // 4:31.20", Label("Daily/Panel/Share"));
            Assert.AreEqual("LEADERBOARD OFFLINE\nLOCAL BEST 13640", d.RowsText);
            StringAssert.Contains("BEST 34000", d.WeekText);
            Assert.AreEqual("WEEKLY", Label("Daily/Panel/Weekly/Label"));

            var board = new BoardView { board = day, online = true, total = 42, yourRank = 2, yourScore = 13640 };
            board.top.Add(new BoardRow { rank = 1, tag = "3F9A1C", score = 15020 });
            board.top.Add(new BoardRow { rank = 2, tag = "0A11CE", score = 13640 });
            board.top.Add(new BoardRow { rank = 3, tag = "B0B0B0", score = 9100 });
            d.ShowBoard(board);
            StringAssert.Contains("3F9A1C", d.RowsText);
            Assert.AreEqual("YOU #2 · 13640", d.YouText);
            Assert.AreEqual("TODAY'S BOARD · 42 PLAYERS", Label("Daily/Panel/BoardHead"));
            yield return Shot("daily_scored");

            Click("Daily/Panel/Copy");
            CollectionAssert.AreEqual(new[] { "copy:" + share }, _log, "[C] copies the share block");
            Click("Daily/Panel/Board");
            Assert.IsTrue(d.ShowingWeek);
            Assert.AreEqual("LEADERBOARD OFFLINE\nLOCAL BEST 34000", d.RowsText, "the week's board, its local best");
            _log.Clear();
            Click("Daily/Panel/Weekly");
            CollectionAssert.AreEqual(new[] { "begin:Campaign:" + LeaderboardScore.Seed(week) }, _log, "a campaign on the week's seed");
            Assert.IsFalse(d.IsOn);
        }

        static T Screen<T>(string field) where T : class =>
            typeof(UIManager).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(UIManager.I) as T;

        // ------------------------------------------------------------------ screenshots (.work/daily/shots, best effort)
        static string ShotDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".work", "daily", "shots"));
        IEnumerator Shot(string name)
        {
            var ui = UIManager.I; var cam = _camGo.GetComponent<Camera>();
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var cs = ui.Canvas.GetComponent<CanvasScaler>();
            var mode = cs.uiScaleMode; float scale = cs.scaleFactor; var rmode = ui.Canvas.renderMode; var wcam = ui.Canvas.worldCamera;
            cam.targetTexture = rt;
            cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; cs.scaleFactor = 1f;
            ui.Canvas.renderMode = RenderMode.ScreenSpaceCamera; ui.Canvas.worldCamera = cam; ui.Canvas.planeDistance = 1f;
            yield return null;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            try { Directory.CreateDirectory(ShotDir); File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG()); }
            catch (Exception e) { Debug.LogWarning("[UI-TEST] shot " + name + " not written: " + e.Message); }
            Object.Destroy(tex);
            cam.targetTexture = null; rt.Release(); Object.Destroy(rt);
            ui.Canvas.renderMode = rmode; ui.Canvas.worldCamera = wcam; cs.uiScaleMode = mode; cs.scaleFactor = scale;
        }
    }
}
