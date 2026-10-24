using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Desync.Content;
using Desync.Gen;
using Desync.Net;
using Desync.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Desync.Flow.Tests
{
    /// Daily Breach and the Weekly Ladder end to end on stub rooms (a floor, a RoomInfo, the player): one scored attempt a day, act 1
    /// only, the day's mutator pair in every room, the result saved, and the recorded logs replaying to exactly the run's score
    /// (with a scoring modifier that changes mid-room, as a conditional exploit does). The leaderboard is offline (BaseUrl null).
    public class DailyPlayTests
    {
        const string SaveKey = "desync.save.dailytests";
        static Catalog cat;
        static Catalog Cat => cat ??= Catalog.FromJson(Resources.Load<TextAsset>("Content/exploits").text, Resources.Load<TextAsset>("Content/mutators").text);

        Action<RoomSpec, Transform, Action<IDisposable>> realBuild;
        string realBase;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            SaveService.Unload(SaveKey);
            realBuild = RunManager.BuildRoom;
            RunManager.BuildRoom = StubBuild;
            realBase = Leaderboard.BaseUrl;
            Leaderboard.BaseUrl = null;
            GameState.God = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RunManager.I.Abandon();
            RunManager.I.Abandon();
            RunManager.BuildRoom = realBuild;
            Leaderboard.BaseUrl = realBase;
            Time.timeScale = 1f;
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

        static RunNode NextNode()
        {
            var nodes = RunState.Graph.acts[RunState.Act - 1];
            RunNode cur = nodes.Find(n => n.id == RunState.NodeId);
            return RunManager.Choice(cur == null ? nodes.Find(n => n.index == 0).id : cur.next[0]);
        }

        static IEnumerator WaitRoom()
        {
            for (float t = 0f; t < 10f && !(RunManager.I.RoomLive && RoomInfo.Current && GameState.Phase == GameState.PhaseKind.Playing); t += Time.unscaledDeltaTime)
                yield return null;
            Assert.IsTrue(RunManager.I.RoomLive && RoomInfo.Current, "room built and live");
        }

        static void SetScoreMult(float v) { var m = GameState.Mods; m.ScoreMult = v; GameState.Mods = m; }

        /// Plays the daily from its first map to its end; returns the room count via `rooms`.
        IEnumerator PlayDaily(List<string> mutatorsSeen)
        {
            var pair = DailyRun.Mutators(RunState.Seed, Cat).Select(m => m.id).ToList();
            for (int step = 0; step < 10 && RunState.Phase != RunPhase.Over; step++)
            {
                var n = NextNode();
                RunEvents.ChooseNode?.Invoke(n.id);
                if (RunState.Phase == RunPhase.Market) { RunEvents.Leave?.Invoke(); continue; }
                if (RunState.Phase == RunPhase.Cooldown) { RunEvents.ChooseCooldown?.Invoke(2); continue; }
                yield return WaitRoom();
                yield return null;
                var ids = RunState.RoomMutators.Select(m => m.id).ToList();
                mutatorsSeen.Add(n.type + ":" + string.Join(",", ids));
                if (n.type == NodeType.Boss) Assert.IsEmpty(ids, "no run mutators on the boss");
                else CollectionAssert.AreEqual(pair, ids, "the day's pair in every room");

                GameEvents.HackFinished?.Invoke(true);            // under the room's own multiplier
                GameEvents.Grazed?.Invoke("ICE-1");
                float baseMult = GameState.Mods.ScoreMult;
                SetScoreMult(baseMult * 1.5f);                    // what a conditional exploit does mid-room (RunManager writes it in Update)
                yield return null;
                GameEvents.HackFinished?.Invoke(false);
                SetScoreMult(baseMult);
                yield return null;

                RoomManager.I.OpenExit();
                GameEvents.ExitReached?.Invoke();
                Assert.AreEqual(RunPhase.Results, RunState.Phase);
                GameEvents.ContinueRequested?.Invoke();
                if (RunState.Phase == RunPhase.Draft) RunEvents.PickExploit?.Invoke(-1);
            }
        }

        [UnityTest]
        public IEnumerator Daily_OneScoredAttempt_Act1Only_PairEverywhere_LogsReplayToTheScore()
        {
            string day = LeaderboardScore.Day(DateTime.UtcNow);
            DailyRun.StartDaily();
            Assert.AreEqual(RunMode.Daily, RunState.Mode);
            Assert.AreEqual(LeaderboardScore.Seed(day), RunState.Seed, "the UTC day's shared seed");
            Assert.AreEqual(RunPhase.Map, RunState.Phase);
            Assert.AreEqual(DailyRun.Board.Daily, DailyRun.Current);
            Assert.IsTrue(DailyRun.Ranked, "the day's first daily is the scored attempt");
            Assert.IsTrue(DailyRun.Scored(SaveService.Data, day), "spent at the start");

            var seen = new List<string>();
            yield return PlayDaily(seen);
            Assert.AreEqual(RunPhase.Over, RunState.Phase, "the daily ends at the act 1 boss");
            Assert.IsTrue(RunState.Won);
            Assert.AreEqual(1, RunState.Act);
            StringAssert.StartsWith("Boss", seen[seen.Count - 1]);
            Assert.AreEqual(DailyRun.Board.None, DailyRun.Current, "recording stopped");

            var save = SaveService.Data;
            Assert.Greater(RunState.RunScore, 0);
            Assert.AreEqual(RunState.RunScore, save.dailyBest[day], "the scored attempt's total");
            var share = save.settings[DailyRun.ShareKey].Split('\n');
            Assert.AreEqual("DESYNC DAILY // " + day, share[0]);
            Assert.AreEqual(seen.Count + 3, share.Length, "header, one line per room, total, link");
            StringAssert.StartsWith(RunState.RunScore + " // ", share[share.Length - 2]);
            Assert.AreEqual(save.settings[DailyRun.ShareKey], DailyRun.LastShare, "what the run results show");
            Assert.AreEqual(0, DailyRun.SubmitStatus, "posted (offline here), not refused: the logs replayed to the run's score");

            // the same day again: practice, nothing saved over the scored result
            int scored = RunState.RunScore;
            DailyRun.StartDaily();
            Assert.AreEqual(DailyRun.Board.Daily, DailyRun.Current);
            Assert.IsFalse(DailyRun.Ranked, "later attempts are practice");
            RunManager.I.Abandon();
            Assert.AreEqual(scored, SaveService.Data.dailyBest[day]);
        }

        [UnityTest]
        public IEnumerator Weekly_OpensWithTheFirstAuditorKill_IsAFullCampaign()
        {
            Assert.IsFalse(DailyRun.StartWeekly(), "locked before the first AUDITOR kill");
            Assert.AreEqual(RunPhase.None, RunState.Phase);
            SaveService.Data.unlocks.Add("kill_auditor");
            Assert.IsTrue(DailyRun.StartWeekly());
            Assert.AreEqual(RunMode.Campaign, RunState.Mode);
            Assert.AreEqual(LeaderboardScore.Seed(LeaderboardScore.Week(DateTime.UtcNow)), RunState.Seed, "the ISO week's seed");
            Assert.AreEqual(DailyRun.Board.Weekly, DailyRun.Current);
            Assert.IsTrue(DailyRun.Ranked);
            RunManager.I.Abandon();
            Assert.AreEqual(DailyRun.Board.None, DailyRun.Current);

            RunManager.I.Begin(RunMode.Campaign, LeaderboardScore.Seed(LeaderboardScore.Week(DateTime.UtcNow)));
            Assert.AreEqual(DailyRun.Board.None, DailyRun.Current, "the seed alone does not make a ladder run");
            yield break;
        }
    }
}
