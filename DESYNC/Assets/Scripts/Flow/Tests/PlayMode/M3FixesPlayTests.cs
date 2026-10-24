using System;
using System.Collections;
using Desync.Content;
using Desync.Enemies;
using Desync.Gen;
using Desync.Net;
using Desync.Player;
using Desync.Rules;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EventType = Desync.Rules.EventType;
using Object = UnityEngine.Object;

namespace Desync.Flow.Tests
{
    /// M3 integration fixes on a stub room (floor + RoomInfo + player) through the real run, RoomManager and ScoreKeeper:
    /// the ARCHITECT's final string scores, Redline Surge raises the next room's GRAZE cap, boss state goes with its room,
    /// and the ARCHITECT endings reach the save.
    public class M3FixesPlayTests
    {
        const string SaveKey = "desync.save.m3fixestests";
        static Catalog cat;
        static Catalog Cat => cat ??= Catalog.FromJson(Resources.Load<TextAsset>("Content/exploits").text, Resources.Load<TextAsset>("Content/mutators").text);
        Action<RoomSpec, Transform, Action<IDisposable>> realBuild;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            SaveService.Unload(SaveKey);
            realBuild = RunManager.BuildRoom;
            RunManager.BuildRoom = StubBuild;
            GameState.God = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RunManager.I.Abandon();
            RunManager.I.Abandon();
            RunManager.BuildRoom = realBuild;
            Time.timeScale = 1f;
            GameState.Flags.Clear();
            RunPerks.Clear();
            BossEvents.Reset();
            var s = SceneManager.GetSceneByName(RunManager.StageScene);
            if (s.IsValid() && s.isLoaded && SceneManager.sceneCount > 1) yield return SceneManager.UnloadSceneAsync(s);
            PlayerPrefs.DeleteKey(SaveKey);
            SaveService.Unload();
        }

        static void StubBuild(RoomSpec spec, Transform parent, Action<IDisposable> done)
        {
            var root = new GameObject("StubRoom");
            root.transform.SetParent(parent, false);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.layer = GameLayers.Floor;
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(20f, 1f, 20f);
            var info = new GameObject("RoomInfo").AddComponent<RoomInfo>();
            info.transform.SetParent(root.transform, false);
            info.index = -1;
            info.terminalCount = 1;
            info.spawn = new GameObject("Spawn").transform;
            info.spawn.SetParent(root.transform, false);
            PlayerFactory.Create(root.transform, Vector3.zero, 0f);
            done(new Handle(root));
        }

        sealed class Handle : IDisposable
        {
            readonly GameObject go;
            public Handle(GameObject go) => this.go = go;
            public void Dispose() { if (go) Object.Destroy(go); }
        }

        static IEnumerator WaitRoom()
        {
            for (float t = 0f; t < 10f && !(RunManager.I.RoomLive && RoomInfo.Current && GameState.Phase == GameState.PhaseKind.Playing); t += Time.unscaledDeltaTime)
                yield return null;
            Assert.IsTrue(RunManager.I.RoomLive && RoomInfo.Current, "room built and live");
        }

        IEnumerator EnterRoom(uint seed, params string[] held)
        {
            RunManager.I.Begin(RunMode.Quick, seed);
            foreach (var id in held) RunState.Held.Add(Cat.Exploit(id));
            RunEvents.ChooseNode?.Invoke(RunState.Graph.acts[0].Find(x => x.index == 0).id);   // act 1 node 0: Infiltration
            yield return WaitRoom();
        }

        /// Map -> the next room: Market / Cooldown nodes on the way are left at once.
        static IEnumerator NextRoom()
        {
            for (int step = 0; step < 6; step++)
            {
                Assert.AreEqual(RunPhase.Map, RunState.Phase);
                var nodes = RunState.Graph.acts[RunState.Act - 1];
                var cur = nodes.Find(n => n.id == RunState.NodeId);
                var next = RunManager.Choice(cur == null ? nodes.Find(n => n.index == 0).id : cur.next[0]);
                RunEvents.ChooseNode?.Invoke(next.id);
                if (next.type == NodeType.Market || next.type == NodeType.Cooldown) { RunEvents.Leave?.Invoke(); continue; }
                yield return WaitRoom();
                yield break;
            }
            Assert.Fail("no room within six nodes");
        }

        /// Exit reached, results, CONTINUE, skip the draft: back on the map.
        static void ClearRoom()
        {
            RoomManager.I.OpenExit();
            GameEvents.ExitReached?.Invoke();
            Assert.AreEqual(RunPhase.Results, RunState.Phase);
            GameEvents.ContinueRequested?.Invoke();
            if (RunState.Phase == RunPhase.Draft) RunEvents.PickExploit?.Invoke(-1);
        }

        static void Redline()
        {
            GameState.Melt = 95f;
            GameEvents.Flipped?.Invoke(false);   // leaving SOURCE at melt >= 90
        }

        [UnityTest]
        public IEnumerator ArchitectFinalString_ScoresPuppetString()
        {
            yield return EnterRoom(62u);
            int s0 = GameState.Score;
            BossEvents.StringSevered?.Invoke(0);
            Assert.AreEqual(s0, GameState.Score, "a terminal's string pays through its hack, not as a PUPPET STRING");
            BossEvents.StringSevered?.Invoke(ArchitectBoss.Terminals);
            Assert.AreEqual(s0 + Scoring.PuppetStringPts, GameState.Score, "the final string: PUPPET_STRING 400 at x1.00");
            var log = ScoreKeeper.I.Log;
            Assert.AreEqual(EventType.PuppetString, log[log.Count - 1].Type);
            Assert.AreEqual(GameState.Score, Scoring.ScoreFromEvents(log, GameState.Mods), "the replay agrees");
        }

        [UnityTest]
        public IEnumerator RedlineSurge_EachRedlineExit_RaisesTheNextRoomsGrazeCap()
        {
            yield return EnterRoom(81u, "redline_surge");
            Assert.IsTrue(GameState.Flags.Contains("flag:redline_surge"));
            Assert.AreEqual(0, GameState.Mods.GrazeCapBonus, "nothing earned yet");
            Redline();
            Redline();
            Assert.AreEqual(2, ScoreKeeper.I.Redlines);
            ClearRoom();
            yield return NextRoom();
            Assert.AreEqual(2, GameState.Mods.GrazeCapBonus, "+1 GRAZE cap per ICE per REDLINE exit");
            int paid = 0;
            Action<ScorePop> onPop = p => { if (p.type == EventType.Graze) paid++; };
            GameEvents.Scored += onPop;
            try { for (int i = 0; i < 6; i++) GameEvents.Grazed?.Invoke("ICE-1"); }
            finally { GameEvents.Scored -= onPop; }
            Assert.AreEqual(5, paid, "the cap is 3 + 2");

            ClearRoom();   // no REDLINE in this room
            yield return NextRoom();
            Assert.AreEqual(0, GameState.Mods.GrazeCapBonus, "the bonus lasts one room");
        }

        [UnityTest]
        public IEnumerator RedlineSurge_NotHeld_GrantsNothing()
        {
            yield return EnterRoom(81u);
            Redline();
            ClearRoom();
            yield return NextRoom();
            Assert.AreEqual(0, GameState.Mods.GrazeCapBonus);
        }

        [UnityTest]
        public IEnumerator BossState_GoesWithItsRoom_AndWithTheRun()
        {
            yield return EnterRoom(91u);
            SetBossState();
            ClearRoom();   // the room is torn down for the draft / map
            AssertBossStateClear("room disposed");

            yield return NextRoom();
            SetBossState();
            RunManager.I.Abandon();   // quit mid-room: the run ends
            AssertBossStateClear("run ended");
        }

        static void SetBossState()
        {
            BossEvents.MeltSource = 3f;
            BossEvents.MeltRender = 3f;
            Shader.SetGlobalFloat("_Desync", 1f);
        }

        static void AssertBossStateClear(string when)
        {
            Assert.AreEqual(0f, BossEvents.MeltSource, when);
            Assert.AreEqual(0f, BossEvents.MeltRender, when);
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_Desync"), when);
            Assert.IsFalse(ArchitectBoss.Current || Compiler.Current || Auditor.Current, when + ": no Current boss");
        }

        [UnityTest]
        public IEnumerator ArchitectEndings_AreSaved_AsUnlocks()
        {
            yield return null;   // RunManager is up
            Assert.IsFalse(SaveService.Data.unlocks.Contains("ending_exposed"));
            BossEvents.Ending?.Invoke(BossEvents.Exposed);
            BossEvents.Ending?.Invoke(BossEvents.Merged);
            BossEvents.Ending?.Invoke(BossEvents.Merged);
            CollectionAssert.AreEqual(new[] { "ending_exposed", "ending_merged" }, SaveService.Data.unlocks);
            SaveService.Flush();
            SaveService.Unload(SaveKey);
            CollectionAssert.Contains(SaveService.Data.unlocks, "ending_merged", "written to the profile");
        }
    }
}
