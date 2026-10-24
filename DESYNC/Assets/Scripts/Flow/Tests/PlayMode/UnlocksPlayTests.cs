using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Desync.Content;
using Desync.Enemies;
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
    /// The unlock tree inside the real run manager: what a fresh profile is offered, what a bought tree opens, which modes are gated, the
    /// equipped starter, the Black Market gate on the run map, and the Deep Layer reward paid when a deep run is won. Rooms are stubs.
    public class UnlocksPlayTests
    {
        const string SaveKey = "desync.save.unlocktests";
        Action<RoomSpec, Transform, Action<IDisposable>> realBuild;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            SaveService.Unload(SaveKey);   // runs write unlocks, fragments and stats: keep the developer's profile out of it
            realBuild = RunManager.BuildRoom;
            RunManager.BuildRoom = StubBuild;
            GameState.God = false;
            Unlocks.Data = null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RunManager.I.Abandon();
            RunManager.I.Abandon();   // Over -> None
            RunManager.BuildRoom = realBuild;
            GameState.God = false;
            Time.timeScale = 1f;
            var s = SceneManager.GetSceneByName(RunManager.StageScene);
            if (s.IsValid() && s.isLoaded && SceneManager.sceneCount > 1) yield return SceneManager.UnloadSceneAsync(s);
            PlayerPrefs.DeleteKey(SaveKey);
            SaveService.Unload();
            DeepRun.Level = 0;
            DeepRun.Picks.Clear();
            DeepRun.Rewards.Clear();
            EnemyFlags.Refresh();
            Unlocks.Data = null;
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

        static IEnumerator WaitRoom(float timeout = 10f)
        {
            for (float t = 0f; t < timeout && !(RunManager.I.RoomLive && RoomInfo.Current && GameState.Phase == GameState.PhaseKind.Playing); t += Time.unscaledDeltaTime)
                yield return null;
            Assert.IsTrue(RunManager.I.RoomLive && RoomInfo.Current, "room built and live");
        }

        static void WinRoom()
        {
            RoomManager.I.OpenExit();
            GameEvents.ExitReached?.Invoke();
            Assert.AreEqual(RunPhase.Results, RunState.Phase);
        }

        /// The pool the run drafts and sells from (private to the run manager).
        static Catalog RunPool() => (Catalog)typeof(RunManager).GetField("runPool", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(RunManager.I);

        static IEnumerable<string> Ids(Catalog c) => c.exploits.Select(e => e.id);

        static int Markets(RunGraph g) => g.acts.Sum(a => a.Count(n => n.type == NodeType.Market));

        /// A seed whose run map has Black Market nodes at `heat`.
        static uint MarketSeed(int heat)
        {
            for (uint s = 1; s < 400; s++) if (Markets(Generator.GenerateRun(s, heat)) > 0) return s;
            throw new InconclusiveException("no seed with a market");
        }

        static void Own(params string[] nodes)
        {
            SaveService.Data.fragments = 100000;
            foreach (var n in nodes) Assert.IsTrue(Unlocks.Buy(n), n);
            SaveService.Data.fragments = 0;
        }

        // ================================================================== gated runs
        [UnityTest]
        public IEnumerator Campaign_FreshProfile_FirstEightExploits_NoStarter_NoBlackMarket()
        {
            uint seed = MarketSeed(0);
            Assert.Greater(Markets(Generator.GenerateRun(seed, 0)), 0, "the seed does have markets before the gate");
            RunManager.I.Begin(RunMode.Campaign, seed);
            CollectionAssert.AreEquivalent(Unlocks.Data.Find("pool_a").exploits, Ids(RunPool()), "a fresh profile drafts from the first 8");
            CollectionAssert.IsEmpty(RunState.Held);
            Assert.AreEqual(0, Markets(RunState.Graph), "Black Market nodes play as Infiltration rooms until bought");
            Assert.AreEqual(Generator.GenerateRun(seed, 0).acts.Sum(a => a.Count), RunState.Graph.acts.Sum(a => a.Count), "the map keeps its shape");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Campaign_WithTheTreeBought_OpensThePoolTheMarketAndTheStarter()
        {
            Own("pool_b", "pool_c", "blackmarket", "starter_ghost", "starter_phantom");
            Assert.IsTrue(Unlocks.SetStarter("starter_phantom"));
            uint seed = MarketSeed(0);
            RunManager.I.Begin(RunMode.Campaign, seed);
            Assert.AreEqual(24, RunPool().exploits.Count, "8 free + 16");
            CollectionAssert.IsSupersetOf(Ids(RunPool()), Unlocks.Data.Find("pool_c").exploits);
            CollectionAssert.AreEqual(new[] { "ghost_ping" }, RunState.Held.Select(e => e.id), "the PHANTOM starter");
            Assert.AreEqual(Markets(Generator.GenerateRun(seed, 0)), Markets(RunState.Graph), "the Black Market is on the map");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Quick_IsGatedLikeCampaign()
        {
            Own("starter_ghost");
            Unlocks.SetStarter("starter_ghost");
            RunManager.I.Begin(RunMode.Quick, MarketSeed(0));
            Assert.AreEqual(8, RunPool().exploits.Count);
            CollectionAssert.AreEqual(new[] { "clean_handshake" }, RunState.Held.Select(e => e.id));
            Assert.AreEqual(0, Markets(RunState.Graph));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Daily_SeesEveryExploitAndTheMarket_AndStartsEmptyHanded()
        {
            Own("starter_ghost");
            Unlocks.SetStarter("starter_ghost");
            uint seed = MarketSeed(0);
            RunManager.I.Begin(RunMode.Daily, seed);
            Assert.AreEqual(58, RunPool().exploits.Count, "a shared seed compares like with like: all 48 + 10 duos");
            CollectionAssert.IsEmpty(RunState.Held, "no starter in a shared-seed run");
            Assert.AreEqual(Markets(Generator.GenerateRun(seed, 0)), Markets(RunState.Graph));
            yield return null;
        }

        // ================================================================== Deep Layer
        [UnityTest]
        public IEnumerator DeepLayer_KeepsTheBlackMarket_ButUsesTheTreesPoolAndStarter()
        {
            var save = SaveService.Data;
            RunUnlocks.RecordKill(save, 3);
            save.deepLayerMax = 10;
            Own("starter_ghost");
            Unlocks.SetStarter("starter_ghost");
            uint seed = MarketSeed(11);
            Assert.IsTrue(RunManager.I.BeginDeep(11, null, seed));
            Assert.AreEqual(Markets(Generator.GenerateRun(seed, 11)), Markets(RunState.Graph), "Poor Market needs a market: Deep Layer always has one");
            Assert.AreEqual(8, RunPool().exploits.Count, "the Deep Layer drafts from what the tree opened");
            CollectionAssert.AreEqual(new[] { "clean_handshake" }, RunState.Held.Select(e => e.id));
            // heat exclusions still apply on top: Persistent Melt (a DL 11 preset pick) keeps Cold Boot out of the pool
            Own("pool_b", "pool_c", "pool_d");
            RunManager.I.Begin(RunMode.Campaign, seed);   // a normal run again: the full DL 0 pool of the three bought packs
            Assert.IsTrue(Ids(RunPool()).Contains("cold_boot"));
            RunManager.I.Abandon();
            Assert.IsTrue(RunManager.I.BeginDeep(11, null, seed));
            Assert.IsFalse(Ids(RunPool()).Contains("cold_boot"), "Persistent Melt x Cold Boot");
            yield return null;
        }

        [UnityTest]
        public IEnumerator DeepLayer11Win_PaysItsReward_AtTheRunsEnd_AndTheNextRunClearsIt()
        {
            var save = SaveService.Data;
            RunUnlocks.RecordKill(save, 3);
            save.deepLayerMax = 10;
            Assert.IsTrue(RunManager.I.BeginDeep(11, null, 7u));
            RunState.Act = 3;
            var act3 = RunState.Graph.acts[2];
            RunState.NodeId = act3.Find(n => n.index == 3).id;
            var boss = act3.Find(n => n.type == NodeType.Boss);
            RunEvents.ChooseNode?.Invoke(boss.id);
            yield return WaitRoom();
            WinRoom();
            GameEvents.ContinueRequested?.Invoke();
            Assert.AreEqual(RunPhase.Over, RunState.Phase);
            Assert.IsTrue(RunState.Won);
            Assert.AreEqual(11, save.deepLayerMax);
            CollectionAssert.AreEqual(new[] { "deep_diver" }, DeepRun.Rewards.Select(c => c.id), "the results can read what the win paid");
            CollectionAssert.IsSupersetOf(save.unlocks, new[] { "dl_reward_11", "cosmetic_deep_diver" });
            Assert.AreEqual("deep_diver", Cosmetics.Equipped(CosmeticSlot.Title).id);

            RunManager.I.Begin(RunMode.Quick, 1u);
            CollectionAssert.IsEmpty(DeepRun.Rewards, "a new run clears the last win's rewards");
            CollectionAssert.Contains(save.unlocks, "cosmetic_deep_diver", "the profile keeps them");
        }

        [UnityTest]
        public IEnumerator DeepLayer30Win_PaysTheSyncGoldTrail()
        {
            var save = SaveService.Data;
            RunUnlocks.RecordKill(save, 3);
            save.deepLayerMax = 29;
            Assert.IsTrue(RunManager.I.BeginDeep(30, null, 9u));
            Assert.AreEqual(30, DeepRun.Level);
            RunState.Act = 3;
            var act3 = RunState.Graph.acts[2];
            RunState.NodeId = act3.Find(n => n.index == 3).id;
            RunEvents.ChooseNode?.Invoke(act3.Find(n => n.type == NodeType.Boss).id);
            yield return WaitRoom();
            WinRoom();
            GameEvents.ContinueRequested?.Invoke();
            Assert.IsTrue(RunState.Won);
            Assert.AreEqual(30, save.deepLayerMax);
            CollectionAssert.AreEqual(new[] { "sync_gold" }, DeepRun.Rewards.Select(c => c.id));
            Assert.AreEqual("sync_gold", Cosmetics.Equipped(CosmeticSlot.Flip).id, "worn at once");
        }
    }
}
