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
    /// M3 run: the three-act CAMPAIGN with a boss per act, first-kill flags, and Deep Layer runs (unlock, picks folded into
    /// every room, Tight Deadline, No Backups, Poor Market, Fewer Choices, Persistent Melt, the Fragments bonus, deepLayerMax).
    /// Rooms are stubs (a floor, a RoomInfo, the player) won through the debug "win" path, except the act 3 build check.
    public class RunCampaignPlayTests
    {
        const string SaveKey = "desync.save.runtests3";
        static Catalog cat;
        static Catalog Cat => cat ??= Catalog.FromJson(Resources.Load<TextAsset>("Content/exploits").text, Resources.Load<TextAsset>("Content/mutators").text);

        Action<RoomSpec, Transform, Action<IDisposable>> realBuild;

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            SaveService.Unload(SaveKey);   // runs write unlocks, fragments and stats: keep the developer's profile out of it
            realBuild = RunManager.BuildRoom;
            RunManager.BuildRoom = StubBuild;
            GameState.God = false;
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
            EnemyFlags.Refresh();   // the run cleared GameState.Flags; no cached boss heat outlives these tests
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

        static List<RunNode> ActNodes() => RunState.Graph.acts[RunState.Act - 1];

        /// The first legal choice: the current node's first next, or the act's first row-0 node.
        static RunNode NextNode()
        {
            var nodes = ActNodes();
            var cur = nodes.Find(n => n.id == RunState.NodeId);
            return RunManager.Choice(cur == null ? nodes.Find(n => n.index == 0).id : cur.next[0]);
        }

        static IEnumerator WaitRoom(float timeout = 10f)
        {
            for (float t = 0f; t < timeout && !(RunManager.I.RoomLive && RoomInfo.Current && GameState.Phase == GameState.PhaseKind.Playing); t += Time.unscaledDeltaTime)
                yield return null;
            Assert.IsTrue(RunManager.I.RoomLive && RoomInfo.Current, "room built and live");
            Assert.AreEqual(GameState.PhaseKind.Playing, GameState.Phase);
        }

        static void WinRoom()
        {
            RoomManager.I.OpenExit();            // debug "win"
            GameEvents.ExitReached?.Invoke();    // the player walks into the open exit
            Assert.AreEqual(RunPhase.Results, RunState.Phase);
        }

        /// The boss an act should field: its own when it has a factory, else the AUDITOR fallback.
        static string ExpectedBoss(int act) => RunBosses.Factories.ContainsKey(RunUnlocks.Boss(act)) ? RunUnlocks.Boss(act) : RunBosses.Fallback;

        /// Plays the run to its end with a debug win per room, skipping drafts; logs what it met.
        IEnumerator PlayToTheEnd(List<string> log)
        {
            for (int step = 0; step < 60 && RunState.Phase == RunPhase.Map; step++)
            {
                var n = NextNode();
                Assert.IsNotNull(n, "a legal next node");
                RunEvents.ChooseNode?.Invoke(n.id);
                if (n.type == NodeType.Market) { Assert.AreEqual(RunPhase.Market, RunState.Phase); RunEvents.Leave?.Invoke(); continue; }
                if (n.type == NodeType.Cooldown) { Assert.AreEqual(RunPhase.Cooldown, RunState.Phase); RunEvents.ChooseCooldown?.Invoke(2); continue; }
                yield return WaitRoom();
                yield return null;
                bool drones = GameState.Flags.Contains(RunRules.AuditorDronesFlag);
                Assert.AreEqual(RunRules.AuditorDrones(n, RunManager.I.CurrentSpec), drones, n.id + " Auditor Drones");
                log.Add(n.id + ":" + n.type + ":" + RunManager.I.CurrentSpec.seed + ":" + RunManager.I.CurrentBoss + ":" + (drones ? "drones" : ""));
                if (n.type == NodeType.Boss)
                {
                    Assert.AreEqual(ExpectedBoss(n.act), RunManager.I.CurrentBoss, "act " + n.act + " boss");
                    int want = RunManager.I.CurrentSpec.entities.Count(e => e.kind == EntityKind.Terminal);
                    Assert.AreEqual(want, GameState.TerminalsTotal, "the arena's terminals open the exit");
                }
                else Assert.IsNull(RunManager.I.CurrentBoss);
                WinRoom();
                if (n.type == NodeType.Boss) Assert.IsTrue(RunUnlocks.Killed(SaveService.Data, n.act), "first kill saved at the boss's results");
                GameEvents.ContinueRequested?.Invoke();
                if (RunState.Phase == RunPhase.Draft) RunEvents.PickExploit?.Invoke(-1);
            }
        }

        [UnityTest]
        public IEnumerator Campaign_SeededRun_ReachesEveryBoss_ThenOpensDeepLayer()
        {
            Assert.IsFalse(RunUnlocks.DeepLayerOpen(SaveService.Data));
            var a = new List<string>();
            RunManager.I.Begin(RunMode.Campaign, 2026u);
            yield return PlayToTheEnd(a);
            Assert.AreEqual(RunPhase.Over, RunState.Phase);
            Assert.IsTrue(RunState.Won, "the ARCHITECT's results end a won CAMPAIGN");
            Assert.AreEqual(3, RunState.Act);
            CollectionAssert.AreEqual(new[] { "auditor", "compiler", "architect" }.Select((b, i) => ExpectedBoss(i + 1)),
                                      a.Where(l => l.Contains(":Boss:")).Select(l => l.Split(':')[3]), string.Join("\n", a));
            Assert.IsFalse(a.Exists(l => l.StartsWith("a1") && l.EndsWith(":drones")), "no Auditor Drones in act 1");
            Assert.IsTrue(a.Exists(l => l.StartsWith("a3") && l.EndsWith(":drones")), "Auditor Drones in act 3 rooms");
            Assert.IsTrue(RunUnlocks.DeepLayerOpen(SaveService.Data));
            Assert.AreEqual(1, RunUnlocks.MaxDeepLayer(SaveService.Data));
            Assert.AreEqual(0, SaveService.Data.deepLayerMax, "a CAMPAIGN win is not a Deep Layer win");

            var b = new List<string>();   // the same seed plays the same run
            RunManager.I.Begin(RunMode.Campaign, 2026u);
            yield return PlayToTheEnd(b);
            CollectionAssert.AreEqual(a, b);
        }

        [UnityTest]
        public IEnumerator DeepLayer_Locked_ThenPicksFoldIntoEveryRoom()
        {
            var save = SaveService.Data;
            Assert.IsFalse(RunManager.I.BeginDeep(1, null, 7u), "closed before the first ARCHITECT kill");
            Assert.AreEqual(RunPhase.None, RunState.Phase);
            RunUnlocks.RecordKill(save, 3);
            Assert.IsFalse(RunManager.I.BeginDeep(2, null, 7u), "one level at a time");
            save.deepLayerMax = 9;
            var picks = new List<MutatorPick>
            {
                new MutatorPick("tight_deadline", 2), new MutatorPick("no_backups", 1), new MutatorPick("double_patrol", 1),
                new MutatorPick("fewer_choices", 1), new MutatorPick("poor_market", 1),
            };
            Assert.IsFalse(RunManager.I.BeginDeep(10, picks.Take(4).ToList(), 7u), "9 heat is not DL 10");
            Assert.IsTrue(RunManager.I.BeginDeep(10, picks, 7u));
            Assert.AreEqual(RunMode.Campaign, RunState.Mode);
            Assert.AreEqual(10, DeepRun.Level);
            CollectionAssert.AreEqual(DeepRules.Canonical(picks).Select(p => p.id), DeepRun.Picks.Select(p => p.id));

            // No Backups: the Cooldown's +1 Backup is refused
            RunState.Phase = RunPhase.Cooldown;
            RunEvents.ChooseCooldown?.Invoke(1);
            Assert.AreEqual(RunPhase.Cooldown, RunState.Phase);
            Assert.AreEqual(0, RunState.Backups);
            RunEvents.ChooseCooldown?.Invoke(2);
            Assert.AreEqual(RunPhase.Map, RunState.Phase);

            var n = NextNode();
            RunEvents.ChooseNode?.Invoke(n.id);
            yield return WaitRoom();
            var built = DeepRules.Build(RunState.Held, RunState.RoomMutators, 10, DeepRun.Picks, Cat, null);
            CollectionAssert.IsSubsetOf(built.flags, GameState.Flags);
            foreach (var f in new[] { "mut:tight_deadline", "mut:no_backups", "mut:double_patrol", "flag:double_patrol", "flag:fewer_choices", DeepRules.BossHeatFlag })
                Assert.IsTrue(GameState.Flags.Contains(f), f);
            Assert.IsTrue(EnemyFlags.BossHeat, "DL >= 6: boss heat variants");
            Assert.AreEqual(built.mods.FlipCost, GameState.Mods.FlipCost, 1e-5f);
            var spec = RunManager.I.CurrentSpec;
            Assert.AreEqual(10, spec.heat);
            var lib = ChunkLibrary.FromFolder("Assets/Resources/Content/chunks");
            var expect = Generator.GenerateRoom(n.roomSeed, n.act, n.index, 10, lib, DeepRules.Placement(DeepRun.Picks, Cat));
            Assert.AreEqual(string.Join("|", expect.entities.Select(e => e.id + e.kind)), string.Join("|", spec.entities.Select(e => e.id + e.kind)),
                            "the room carries the Double Patrol drone");

            // Tight Deadline 2: medal times -20 %, and a TRACE once the clock passes the tightened par
            Assert.AreEqual(spec.par / 1.6f * 0.8f, GameState.RoomTDev, 1e-3f);
            int traces = 0;
            Action<int> onTrace = c => traces++;
            GameEvents.TraceStarted += onTrace;
            try
            {
                RoomClock.Time = spec.par * 0.8f - 0.5f;
                yield return null;
                Assert.AreEqual(0, GameState.TraceCount, "not before par");
                RoomClock.Time = spec.par * 0.8f + 0.01f;
                yield return null;
                yield return null;
                Assert.AreEqual(1, GameState.TraceCount, "TRACE +1 at par");
                Assert.AreEqual(1, traces);
                yield return null;
                Assert.AreEqual(1, GameState.TraceCount, "once per attempt");
            }
            finally { GameEvents.TraceStarted -= onTrace; }

            // Fewer Choices: drafts offer 1 of 2
            WinRoom();
            GameEvents.ContinueRequested?.Invoke();
            Assert.AreEqual(RunPhase.Draft, RunState.Phase);
            Assert.AreEqual(2, RunState.Offer.Count);
        }

        /// A Deep Layer seed whose act 1 row 0 node leads to a Black Market.
        static uint MarketSeed(out string first, out string market)
        {
            for (uint s = 1; s < 500; s++)
            {
                var act = Generator.GenerateRun(s, 5).acts[0];   // the graph BeginDeep(5, ...) builds
                foreach (var n0 in act.Where(n => n.index == 0))
                    foreach (var id in n0.next)
                        if (act.Find(n => n.id == id).type == NodeType.Market) { first = n0.id; market = id; return s; }
            }
            throw new InconclusiveException("no seed with a market in row 1");
        }

        [UnityTest]
        public IEnumerator DeepLayer_PoorMarket_NoBackups_PersistentMelt_BonusAndBestLayer()
        {
            var save = SaveService.Data;
            RunUnlocks.RecordKill(save, 3);
            save.deepLayerMax = 4;
            uint seed = MarketSeed(out string first, out string market);
            var picks = new List<MutatorPick> { new MutatorPick("poor_market", 1), new MutatorPick("no_backups", 1), new MutatorPick("persistent_melt", 1) };
            Assert.IsTrue(RunManager.I.BeginDeep(5, picks, seed));

            RunEvents.ChooseNode?.Invoke(first);
            yield return WaitRoom();
            RoomManager.I.OpenExit();
            GameState.Melt = 60f;
            GameEvents.ExitReached?.Invoke();
            Assert.AreEqual(60f, RunState.MeltCarry, 0.01f, "Persistent Melt carries 100 %");
            GameEvents.ContinueRequested?.Invoke();
            if (RunState.Phase == RunPhase.Draft) RunEvents.PickExploit?.Invoke(-1);

            RunEvents.ChooseNode?.Invoke(market);
            Assert.AreEqual(RunPhase.Market, RunState.Phase);
            Assert.IsFalse(RunState.Market.Exists(i => i.kind == "backup"), "No Backups: none on sale");
            var reroll = RunState.Market.Find(i => i.kind == "reroll");
            Assert.AreEqual(DeepRules.Price(RunRules.RerollPrice, DeepRun.Picks, Cat), reroll.price);
            Assert.AreEqual(23, reroll.price, "Poor Market: 15 -> 23");
            foreach (var it in RunState.Market.Where(i => i.kind == "exploit"))
                Assert.AreEqual(DeepRules.Price(RunRules.Price(Cat.Exploit(it.exploitId).rarity), DeepRun.Picks, Cat), it.price);
            RunEvents.Leave?.Invoke();

            // straight to the ARCHITECT: its results end a won Deep Layer run
            RunState.Act = 3;
            var act3 = ActNodes();
            RunState.NodeId = act3.Find(n => n.index == 3).id;
            var boss = act3.Find(n => n.type == NodeType.Boss);
            int bankBefore = save.fragments;
            RunEvents.ChooseNode?.Invoke(boss.id);
            yield return WaitRoom();
            Assert.AreEqual(ExpectedBoss(3), RunManager.I.CurrentBoss);
            WinRoom();
            int earned = RunState.Fragments;
            GameEvents.ContinueRequested?.Invoke();
            Assert.AreEqual(RunPhase.Over, RunState.Phase);
            Assert.IsTrue(RunState.Won);
            Assert.AreEqual(earned + DeepRules.Bonus(earned, 5), RunState.Fragments, "Deep Layer Fragments bonus");
            Assert.AreEqual(bankBefore + RunState.Fragments, save.fragments);
            Assert.AreEqual(5, save.deepLayerMax);
            Assert.AreEqual(6, RunUnlocks.MaxDeepLayer(save));
            Assert.AreEqual(5, DeepRun.Level, "the results still know the layer");
        }

        /// Every act's boss node through the real RoomBuilder: its arena, the boss at the arena's boss point under the room
        /// handle, the arena's terminals as the exit's hacks, and a debug win to the boss results.
        [UnityTest]
        public IEnumerator BossArenas_RealBuild_EveryAct()
        {
            RunManager.BuildRoom = realBuild;
            GameState.God = true;
            for (int act = 1; act <= 3; act++)
            {
                RunManager.I.Begin(RunMode.Campaign, 300u + (uint)act);
                RunState.Act = act;
                var nodes = ActNodes();
                RunState.NodeId = nodes.Find(n => n.index == 3).id;
                RunEvents.ChooseNode?.Invoke(nodes.Find(n => n.type == NodeType.Boss).id);
                yield return WaitRoom(30f);
                yield return null;
                Assert.AreEqual(ExpectedBoss(act), RunManager.I.CurrentBoss, "act " + act);
                Assert.AreEqual(RunManager.I.CurrentSpec.entities.Count(e => e.kind == EntityKind.Terminal), GameState.TerminalsTotal, "act " + act);
                Assert.IsNotNull(Object.FindAnyObjectByType<Levels.RoomHandle>(), "act " + act + " arena built");
                WinRoom();
                Assert.IsTrue(RunUnlocks.Killed(SaveService.Data, act));
                RunManager.I.Abandon();
                yield return null;
                Assert.IsNull(Object.FindAnyObjectByType<Levels.RoomHandle>(), "act " + act + " arena disposed");
            }
        }

        /// An act 3 room (4 x 4 chunks) through the real RoomBuilder: geometry, entities and NavMesh build, the room plays, and
        /// the player stands facing the spawn's yaw (a run whose first act 3 room does not face north, so the check means something).
        [UnityTest]
        public IEnumerator Act3_GeneratedRoom_BuildsForReal()
        {
            RunManager.BuildRoom = realBuild;
            GameState.God = true;
            var lib = ChunkLibrary.FromFolder("Assets/Resources/Content/chunks");
            uint seed = 77u;
            RunNode n = null;
            for (; seed < 400u; seed++)   // an act 3 row-0 Infil (no room mutators) whose room spawns facing east, south or west
            {
                n = Generator.GenerateRun(seed, 0).acts[2].Find(x => x.index == 0 && x.type == NodeType.Infil);
                if (n == null) continue;
                var probe = RunRules.Room(n, false, lib);
                if (probe.fallbackRoom == null && probe.entities.Find(e => e.kind == EntityKind.Spawn).yaw != 0f) break;
            }
            RunManager.I.Begin(RunMode.Campaign, seed);
            RunState.Act = 3;
            RunEvents.ChooseNode?.Invoke(n.id);
            yield return WaitRoom(30f);
            var spec = RunManager.I.CurrentSpec;
            Assert.AreEqual(3, spec.act);
            if (spec.fallbackRoom == null)
            {
                Assert.AreEqual(4, spec.gridW);
                Assert.AreEqual(4, spec.gridH);
                var s = spec.entities.Find(e => e.kind == EntityKind.Spawn);
                Assert.AreEqual(0f, Mathf.DeltaAngle(s.yaw, PlayerRef.Root.eulerAngles.y), 0.5f, "the builder and the player apply the spawn's yaw");
            }
            Assert.IsNotNull(Object.FindAnyObjectByType<Levels.RoomHandle>(), "built by the RoomBuilder");
            Assert.Greater(GameState.TerminalsTotal, 0);
            RunManager.I.Abandon();
            yield return null;
            Assert.IsNull(Object.FindAnyObjectByType<Levels.RoomHandle>(), "disposed with the run");
        }
    }
}
