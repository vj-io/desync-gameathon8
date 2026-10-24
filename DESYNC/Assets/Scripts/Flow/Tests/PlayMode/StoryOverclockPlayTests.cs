using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Endless Overclock end to end, story shards in run rooms (pickup, save, score, boss payout) and the ARCHITECT's third way.
    /// Rooms are stubs (a floor, a RoomInfo, the player) built from the generator's real specs, as in RunPlayTests.
    public class StoryOverclockPlayTests
    {
        const string SaveKey = "desync.save.storytests";
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
            OverclockRun.DebugUnlock = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RunManager.I.Abandon();
            RunManager.I.Abandon();
            RunManager.BuildRoom = realBuild;
            OverclockRun.DebugUnlock = false;
            ArchitectBoss.OfferDesync = false;
            GameState.God = false;
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

        static IEnumerator WaitRoom()
        {
            for (float t = 0f; t < 10f && !(RunManager.I.RoomLive && RoomInfo.Current && GameState.Phase == GameState.PhaseKind.Playing); t += Time.unscaledDeltaTime)
                yield return null;
            Assert.IsTrue(RunManager.I.RoomLive && RoomInfo.Current, "room built and live");
        }

        static void WinRoom()
        {
            RoomManager.I.OpenExit();
            GameEvents.ExitReached?.Invoke();
        }

        /// The live score multiplier the run should hold now: the room's loadout + the Overclock bonus, conditionals folded.
        static float ExpectedScoreMult(int room)
        {
            var b = DeepRules.Build(RunState.Held, RunState.RoomMutators, 0, DeepRun.Picks, Cat, null);
            var mods = b.mods;
            mods.ScoreMult += OverclockRun.ScoreBonusOf(room);
            var node = RunManager.I.CurrentNode;
            var ctx = new ConditionContext { chain = GameState.Chain, melt = GameState.Melt, trace = GameState.TraceCount, node = node.index + 1,
                                             layer = LayerState.InSource ? Rules.Layer.Source : Rules.Layer.Render };
            return Conditions.Apply(mods, b.conditional, ctx).ScoreMult;
        }

        [UnityTest]
        public IEnumerator Overclock_RoomsBackToBack_ActsCycle_MeltCarriesInFull_DeathEndsAndSavesTheBest()
        {
            var phases = new List<RunPhase>();
            Action<RunPhase> onPhase = phases.Add;
            RunEvents.PhaseChanged += onPhase;
            try
            {
                Assert.AreEqual(RunPhase.None, RunState.Phase);
                LogAssert.Expect(LogType.Warning, "[DESYNC] Endless Overclock is locked: it opens with the DESYNC ending");
                Assert.IsFalse(OverclockRun.Start(), "locked without the true ending");
                Assert.AreEqual(RunPhase.None, RunState.Phase, "nothing started");

                OverclockRun.DebugUnlock = true;
                Assert.IsTrue(OverclockRun.Start());
                Assert.AreEqual(RunMode.Overclock, RunState.Mode);
                Assert.AreEqual(RunPhase.Room, RunState.Phase, "no map: straight into the first room");
                GameState.God = true;   // melt frozen while the test steps through
                const int Rooms = 4;
                float carry = 0f;
                for (int room = 0; room < Rooms; room++)
                {
                    yield return WaitRoom();
                    yield return null;
                    Assert.AreEqual("oc" + room, RunState.NodeId);
                    Assert.AreEqual(OverclockRun.ActOf(room), RunState.Act, "room " + room);
                    Assert.AreEqual(OverclockRun.HeatOf(room), RunManager.I.CurrentSpec.heat, "room " + room + " heat");
                    Assert.AreEqual(0, RunState.Backups);
                    if (!GameState.Flags.Contains("flag:cold_boot")) Assert.AreEqual(carry, GameState.Melt, 0.01f, "room " + room + " starts at the melt it left with");
                    Assert.AreEqual(ExpectedScoreMult(room), GameState.Mods.ScoreMult, 1e-4f, "room " + room + " score multiplier");
                    GameState.Melt = carry = 20f + 10f * room;
                    WinRoom();
                    Assert.AreEqual(RunPhase.Results, RunState.Phase);
                    GameEvents.ContinueRequested?.Invoke();
                    if (room < Rooms - 1) Assert.AreEqual(RunPhase.Room, RunState.Phase, "no draft, no map between rooms");
                }
                Assert.AreEqual(OverclockRun.HeatPerStep, OverclockRun.Heat, "the second step runs hotter");

                yield return WaitRoom();
                yield return null;
                GameState.God = false;
                int score = RunState.RunScore;
                PlayerRef.Kill(DeathCause.Ice);
                GameEvents.RestartRequested?.Invoke();   // skip the recap
                Assert.AreEqual(RunPhase.Over, RunState.Phase, "a death ends Endless Overclock");
                Assert.IsFalse(RunState.Won);
                CollectionAssert.DoesNotContain(phases, RunPhase.Map);
                CollectionAssert.DoesNotContain(phases, RunPhase.Draft);
                Assert.AreEqual(Rooms + "," + score, SaveService.Data.settings[OverclockRun.BestKey]);
                OverclockRun.Best(SaveService.Data, out int rooms, out int best);
                Assert.AreEqual(Rooms, rooms);
                Assert.AreEqual(score, best);
                Assert.AreEqual(0, GameState.Flags.Count, "nothing of the run leaks out");
            }
            finally { RunEvents.PhaseChanged -= onPhase; }
        }

        [UnityTest]
        public IEnumerator Shard_InTheFirstRoom_CollectedOnTouch_Saved_Scored_KeptOnReboot_BossPaysTheNext()
        {
            var collected = new List<string>();
            var strings = new List<string>();
            Action<string> onCollected = collected.Add, onString = strings.Add;
            ShardEvents.Collected += onCollected;
            EnemyEvents.PuppetString += onString;
            try
            {
                var save = SaveService.Data;
                RunManager.I.Begin(RunMode.Quick, 2026u);
                var nodes = RunState.Graph.acts[0];
                RunEvents.ChooseNode?.Invoke(nodes.Find(n => n.index == 0).id);
                yield return WaitRoom();
                yield return null;
                var pickup = Object.FindAnyObjectByType<ShardPickup>();
                Assert.IsTrue(pickup, "a fresh profile finds a shard in its first act 1 room");
                Assert.AreEqual("shard_a1_01", pickup.id);
                Assert.IsTrue(pickup.transform.IsChildOf(RoomInfo.Current.transform.parent), "it goes with the room");

                int before = GameState.Score;
                pickup.transform.position = PlayerRef.Position + Vector3.up * ShardPickup.Height;
                yield return null;
                yield return null;
                CollectionAssert.AreEqual(new[] { "shard_a1_01" }, collected);
                CollectionAssert.AreEqual(new[] { "shard_a1_01" }, strings, "scored as PUPPET STRING");
                Assert.GreaterOrEqual(GameState.Score - before, Rules.Scoring.PuppetStringPts);
                Assert.IsTrue(save.unlocks.Contains("shard_a1_01"));
                Assert.IsFalse(pickup.gameObject.activeSelf);

                RoomManager.I.Reboot();
                yield return new WaitForSecondsRealtime(GameConfig.I.restartWipe + 0.2f);
                Assert.IsFalse(pickup && pickup.gameObject.activeSelf, "a REBOOT does not bring it back");
                Assert.IsTrue(save.unlocks.Contains("shard_a1_01"));

                // on to the AUDITOR: its kill pays the act's next shard
                WinRoom();
                GameEvents.ContinueRequested?.Invoke();
                for (int step = 0; step < 10 && RunState.Phase != RunPhase.Over; step++)
                {
                    if (RunState.Phase == RunPhase.Draft) { RunEvents.PickExploit?.Invoke(-1); continue; }
                    Assert.AreEqual(RunPhase.Map, RunState.Phase);
                    var cur = nodes.Find(n => n.id == RunState.NodeId);
                    var next = RunManager.Choice(cur.next[0]);
                    RunEvents.ChooseNode?.Invoke(next.id);
                    if (next.type == NodeType.Market) { RunEvents.Leave?.Invoke(); continue; }
                    if (next.type == NodeType.Cooldown) { RunEvents.ChooseCooldown?.Invoke(2); continue; }
                    yield return WaitRoom();
                    yield return null;
                    int held = ShardBook.Count(save, 1);
                    collected.Clear(); strings.Clear();
                    WinRoom();
                    if (next.type == NodeType.Boss)
                    {
                        Assert.AreEqual(held + 1, ShardBook.Count(save, 1), "the AUDITOR kill pays a shard");
                        Assert.AreEqual(1, collected.Count);
                        Assert.AreEqual(0, strings.Count, "paid, not picked up: no PUPPET STRING score");
                    }
                    GameEvents.ContinueRequested?.Invoke();
                }
                Assert.AreEqual(RunPhase.Over, RunState.Phase, "QUICK ended at the AUDITOR");
            }
            finally
            {
                ShardEvents.Collected -= onCollected;
                EnemyEvents.PuppetString -= onString;
            }
        }

        [Test]
        public void Shards_OnlyInRunModesWithGeneratedRooms()
        {
            Assert.IsFalse(ShardRun.ModeHolds(RunMode.Classic));
            Assert.IsTrue(ShardRun.ModeHolds(RunMode.Quick));
            Assert.IsTrue(ShardRun.ModeHolds(RunMode.Campaign));
            Assert.IsTrue(ShardRun.ModeHolds(RunMode.Daily));
            Assert.IsTrue(ShardRun.ModeHolds(RunMode.Overclock));
        }

        // ---------------------------------------------------------------- the ARCHITECT's third way

        GameObject host, fakePlayer;
        ArchitectBoss boss;
        string ending;
        int exits;
        void OnEnding(string e) => ending = e;
        void OnExit() => exits++;

        ArchitectBoss BuildBoss()
        {
            LogAssert.ignoreFailingMessages = true;   // the Daemons wake with no NavMesh under them
            host = new GameObject("ArenaHost");
            fakePlayer = new GameObject("FakePlayer");
            PlayerRef.Root = fakePlayer.transform;   // no head or chest: the gaze never sees it
            GameState.Alive = true;
            GameState.Cinematic = false;
            RoomClock.Reset();
            RoomClock.Running = true;
            ending = null; exits = 0;
            BossEvents.Ending += OnEnding;
            GameEvents.ExitReached += OnExit;
            return boss = EnemyFactory.ArchitectBoss(host.transform, Vector3.zero).GetComponent<ArchitectBoss>();
        }

        void DropBoss()
        {
            BossEvents.Ending -= OnEnding;
            GameEvents.ExitReached -= OnExit;
            if (host) Object.Destroy(host);
            if (fakePlayer) Object.Destroy(fakePlayer);
            PlayerRef.Root = null;
            RoomClock.Running = false;
            RoomClock.Reset();
            BossEvents.Reset();
            ArchitectBoss.OfferDesync = false;
            LogAssert.ignoreFailingMessages = false;
        }

        /// Strings hacked, the 30 s survived: the final window opens on the next frame.
        IEnumerator ToFinalWindow()
        {
            for (int i = 0; i < ArchitectBoss.Terminals; i++) GameEvents.AccessGranted?.Invoke("STRING");
            Assert.AreEqual(3, boss.Phase);
            yield return null;
            RoomClock.Tick(ArchitectBoss.SurviveTime + 0.5f);
            yield return null;
            Assert.GreaterOrEqual(boss.FinalRemaining, 0f, "the final window is open");
        }

        [UnityTest]
        public IEnumerator Architect_OfferedDesync_GlyphBehindTheFace_TouchEndsAsDesync()
        {
            try
            {
                ArchitectBoss.OfferDesync = true;
                BuildBoss();
                Assert.IsFalse(boss.desyncGlyph.gameObject.activeSelf, "hidden until the final window");
                yield return ToFinalWindow();
                Assert.IsTrue(boss.desyncGlyph.gameObject.activeSelf, "the third way opens");
                Assert.Greater(boss.transform.InverseTransformPoint(boss.desyncGlyph.position).z, 1f, "behind the face, out of the gaze's half");
                fakePlayer.transform.position = boss.desyncGlyph.position - Vector3.up * ArchitectBoss.DesyncAt.y;
                RoomClock.Tick(0.1f);
                yield return null;
                Assert.AreEqual(ArchitectBoss.DesyncEnding, ending);
                Assert.AreEqual(ArchitectBoss.DesyncEnding, boss.EndingName);
                Assert.AreEqual(4, boss.Phase);
                Assert.AreEqual(1, exits, "the room's results follow");
                Assert.IsFalse(boss.desyncGlyph.gameObject.activeSelf);
                Assert.AreEqual(Endings.Flag(Endings.Desync), OverclockRun.UnlockFlag, "the ending the run records opens Overclock");
            }
            finally { DropBoss(); }
        }

        [UnityTest]
        public IEnumerator Architect_NotOffered_NoGlyph_TheTimerMerges()
        {
            try
            {
                BuildBoss();
                yield return ToFinalWindow();
                Assert.IsFalse(boss.desyncGlyph.gameObject.activeSelf);
                fakePlayer.transform.position = boss.desyncGlyph.position - Vector3.up * ArchitectBoss.DesyncAt.y;
                RoomClock.Tick(0.1f);
                yield return null;
                Assert.IsNull(ending, "nothing to touch");
                RoomClock.Tick(ArchitectBoss.FinalTime + 0.5f);
                yield return null;
                Assert.AreEqual(BossEvents.Merged, ending);
            }
            finally { DropBoss(); }
        }
    }
}
