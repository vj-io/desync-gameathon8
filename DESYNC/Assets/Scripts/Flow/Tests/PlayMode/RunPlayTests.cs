using System;
using System.Collections;
using System.Collections.Generic;
using Desync.Content;
using Desync.Gen;
using Desync.Net;
using Desync.Player;
using Desync.TestKit;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Desync.Flow.Tests
{
    /// The BREACH run loop end to end on a stub room (a floor, a RoomInfo, the player): the real graph, generator specs,
    /// mutators, drafts and RoomManager flow; only the geometry is skipped. Rooms are won with the debug "win" path.
    public class RunPlayTests
    {
        const string SaveKey = "desync.save.runtests";
        static Catalog cat;
        static Catalog Cat => cat ??= Catalog.FromJson(Resources.Load<TextAsset>("Content/exploits").text, Resources.Load<TextAsset>("Content/mutators").text);

        Action<RoomSpec, Transform, Action<IDisposable>> realBuild;
        readonly List<RoomSpec> built = new List<RoomSpec>();

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            SaveService.Unload(SaveKey);   // runs write fragments and stats: keep the developer's profile out of it
            realBuild = RunManager.BuildRoom;
            RunManager.BuildRoom = StubBuild;
            built.Clear();
            GameState.God = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RunManager.I.Abandon();
            RunManager.I.Abandon();   // Over -> None
            RunManager.BuildRoom = realBuild;
            Time.timeScale = 1f;
            var s = SceneManager.GetSceneByName(RunManager.StageScene);
            if (s.IsValid() && s.isLoaded && SceneManager.sceneCount > 1) yield return SceneManager.UnloadSceneAsync(s);
            PlayerPrefs.DeleteKey(SaveKey);
            SaveService.Unload();
        }

        void StubBuild(RoomSpec spec, Transform parent, Action<IDisposable> done)
        {
            built.Add(spec);
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

        /// First legal choice: the current node's first next, or the act's first row-0 node.
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
            Assert.AreEqual(GameState.PhaseKind.Playing, GameState.Phase);
        }

        static void WinRoom()
        {
            RoomManager.I.OpenExit();                // debug "win"
            GameEvents.ExitReached?.Invoke();        // the player walks into the open exit
        }

        /// Chooses the next node and plays it to its results (rooms) or straight through (Market: leave, Cooldown: reroll).
        /// Returns via `log` what it saw; true when it was a room.
        IEnumerator PlayNode(List<string> log)
        {
            var n = NextNode();
            Assert.IsNotNull(n);
            RunEvents.ChooseNode?.Invoke(n.id);
            log?.Add(n.id + ":" + n.type + ":" + n.roomSeed);
            if (n.type == NodeType.Market) { Assert.AreEqual(RunPhase.Market, RunState.Phase); RunEvents.Leave?.Invoke(); yield break; }
            if (n.type == NodeType.Cooldown) { Assert.AreEqual(RunPhase.Cooldown, RunState.Phase); RunEvents.ChooseCooldown?.Invoke(2); yield break; }
            Assert.AreEqual(RunPhase.Room, RunState.Phase);
            yield return WaitRoom();
            yield return null;
            Assert.IsFalse(RunManager.I.GetComponent<Camera>().enabled, "the room's own camera renders it");
            if (n.type == NodeType.Boss)
            {
                Assert.IsTrue(Enemies.Auditor.Current, "the AUDITOR is in its arena");
                Assert.AreEqual(3, GameState.TerminalsTotal);
                if (RunManager.BuildRoom != StubBuild)
                    Assert.IsNotNull(Enemies.Auditor.Current.GetComponentInParent<Levels.RoomHandle>(), "under the room handle: Dispose frees it");
            }
            var spec = RunManager.I.CurrentSpec;
            log?.Add("room " + spec.seed + " mirror " + spec.mirror + " " + string.Join("|", spec.tiles ?? new string[0]).GetHashCode()
                     + " muts " + string.Join(",", RunState.RoomMutators.ConvertAll(m => m.id)));
            WinRoom();
            Assert.AreEqual(RunPhase.Results, RunState.Phase);
            Assert.AreEqual(GameState.PhaseKind.Results, GameState.Phase);
        }

        [UnityTest]
        public IEnumerator QuickRun_StartToBoss_WithADebugWinPerRoom() => QuickRun();

        /// The same run through the real RoomBuilder (geometry, entities, NavMesh) and the AUDITOR arena.
        [UnityTest]
        public IEnumerator QuickRun_RealRooms_StartToBoss()
        {
            RunManager.BuildRoom = realBuild;
            GameState.God = true;   // the rooms are live: nothing may end the run while the test steps through it
            yield return QuickRun();
            yield return null;   // Destroy lands at the end of the frame
            Assert.IsNull(Object.FindAnyObjectByType<Levels.RoomHandle>(), "every built room was disposed");
        }

        IEnumerator QuickRun()
        {
            int bankBefore = SaveService.Data.fragments, runsBefore = SaveService.Data.stats.runs;
            RunManager.I.Begin(RunMode.Quick, 2026u);
            Assert.AreEqual(RunPhase.Map, RunState.Phase);
            Assert.AreEqual(runsBefore + 1, SaveService.Data.stats.runs);
            int rooms = 0;
            bool boss = false;
            for (int step = 0; step < 8 && !boss; step++)
            {
                var n = NextNode();
                yield return PlayNode(null);
                if (RunState.Phase == RunPhase.Map) continue;   // market / cooldown
                rooms++;
                boss = n.type == NodeType.Boss;
                GameEvents.ContinueRequested?.Invoke();
                if (boss) break;
                if (RunState.Phase == RunPhase.Draft)
                {
                    Assert.AreEqual(3, RunState.Offer.Count);
                    var pick = RunState.Offer[0];
                    RunEvents.PickExploit?.Invoke(0);
                    Assert.Contains(pick, RunState.Held);
                }
                Assert.AreEqual(RunPhase.Map, RunState.Phase);
                Assert.IsFalse(RoomInfo.Current, "the room is gone once the map is up");
                yield return null;
                Assert.IsTrue(RunManager.I.GetComponent<Camera>().enabled, "a backdrop renders between rooms");
            }
            Assert.IsTrue(boss, "reached the AUDITOR node");
            Assert.AreEqual(RunPhase.Over, RunState.Phase, "QUICK ends at the act 1 boss");
            Assert.IsTrue(RunState.Won);
            Assert.AreEqual(rooms, RunState.Grades.Count);
            if (RunManager.BuildRoom == StubBuild) Assert.AreEqual(rooms, built.Count);
            Assert.GreaterOrEqual(RunState.Fragments, rooms * RunRules.FragClear);
            Assert.AreEqual(bankBefore + RunState.Fragments, SaveService.Data.fragments, "banked into the profile");
            Assert.AreEqual(Rules.Modifiers.Default.FlipCost, GameState.Mods.FlipCost, "no run modifiers leak out of the run");
            Assert.AreEqual(0, GameState.Flags.Count);
        }

        IEnumerator Record(uint seed, List<string> log)
        {
            RunManager.I.Begin(RunMode.Campaign, seed);
            foreach (var act in RunState.Graph.acts)
                foreach (var n in act)
                    log.Add(n.id + n.type + n.roomSeed + "->" + string.Join(",", n.next));
            for (int rooms = 0; rooms < 3;)
            {
                yield return PlayNode(log);
                if (RunState.Phase == RunPhase.Map) continue;
                rooms++;
                GameEvents.ContinueRequested?.Invoke();
                if (RunState.Phase != RunPhase.Draft) continue;
                log.Add("offer " + string.Join(",", RunState.Offer.ConvertAll(e => e.id)));
                RunEvents.PickExploit?.Invoke(rooms % RunState.Offer.Count);
            }
            log.Add("held " + string.Join(",", RunState.Held.ConvertAll(e => e.id)) + " cycles " + RunState.Cycles);
            RunManager.I.Abandon();
        }

        [UnityTest]
        public IEnumerator SameSeed_SameNodesRoomsMutatorsAndOffers_TwoRunsInOneProcess()
        {
            var a = new List<string>();
            var b = new List<string>();
            var c = new List<string>();
            yield return Record(424242u, a);
            yield return Record(424242u, b);
            yield return Record(424243u, c);
            CollectionAssert.AreEqual(a, b);
            CollectionAssert.AreNotEqual(a, c);
        }

        [UnityTest]
        public IEnumerator Death_SpendsABackupAndRespawns_ThenEndsTheRun()
        {
            RunManager.I.Begin(RunMode.Quick, 5u);
            RunState.Backups = 1;
            int bankBefore = SaveService.Data.fragments;
            RunEvents.ChooseNode?.Invoke(NextNode().id);
            yield return WaitRoom();
            int rebooting = 0, reset = 0;
            Action onReboot = () => rebooting++, onReset = () => reset++;
            GameEvents.Rebooting += onReboot;
            GameEvents.RoomReset += onReset;
            try
            {
                GameEvents.RestartRequested?.Invoke();
                Assert.AreEqual(0, rebooting, "no free restart inside a run");

                PlayerRef.Kill(DeathCause.Ice);
                Assert.AreEqual(0, RunState.Backups, "the death spent the Backup");
                GameEvents.RestartRequested?.Invoke();   // skip the recap
                yield return new WaitForSecondsRealtime(GameConfig.I.restartWipe + 0.2f);
                Assert.AreEqual(1, reset, "respawn at room start");
                Assert.IsTrue(GameState.Alive);
                Assert.AreEqual(RunPhase.Room, RunState.Phase);
                Assert.AreEqual(0, GameState.Chain, "chain reset");

                PlayerRef.Kill(DeathCause.Melt);
                GameEvents.RestartRequested?.Invoke();
                Assert.AreEqual(RunPhase.Over, RunState.Phase, "no Backup left: the run ends");
                Assert.IsFalse(RunState.Won);
                Assert.AreEqual(RunRules.Bank(0, true), RunState.Fragments);
                Assert.AreEqual(bankBefore + RunState.Fragments, SaveService.Data.fragments);
                Assert.IsFalse(RoomInfo.Current);
            }
            finally
            {
                GameEvents.Rebooting -= onReboot;
                GameEvents.RoomReset -= onReset;
            }
        }

        [UnityTest]
        public IEnumerator Melt_CarriesHalfIntoTheNextRoom_AndBackToItOnRespawn()
        {
            RunManager.I.Begin(RunMode.Campaign, 9u);
            RunState.Backups = 1;
            RunEvents.ChooseNode?.Invoke(NextNode().id);
            yield return WaitRoom();
            Assert.AreEqual(0f, GameState.Melt, 0.01f);
            RoomManager.I.OpenExit();
            GameState.Melt = 60f;
            GameEvents.ExitReached?.Invoke();
            Assert.AreEqual(30f, RunState.MeltCarry, 0.01f);
            GameEvents.ContinueRequested?.Invoke();
            if (RunState.Phase == RunPhase.Draft) RunEvents.PickExploit?.Invoke(-1);
            Assert.AreEqual(RunPhase.Map, RunState.Phase);

            while (true)   // to the next room (a market is left, a cooldown takes a reroll token, keeping the melt)
            {
                var n = NextNode();
                RunEvents.ChooseNode?.Invoke(n.id);
                if (n.type == NodeType.Market) { RunEvents.Leave?.Invoke(); continue; }
                if (n.type == NodeType.Cooldown) { RunEvents.ChooseCooldown?.Invoke(2); continue; }
                break;
            }
            yield return WaitRoom();
            Assert.AreEqual(30f, GameState.Melt, 0.5f, "the next room starts at half the exit melt");

            PlayerRef.Kill(DeathCause.Ice);
            GameEvents.RestartRequested?.Invoke();
            yield return new WaitForSecondsRealtime(GameConfig.I.restartWipe + 0.05f);
            Assert.AreEqual(30f, GameState.Melt, 1f, "a Backup respawn starts at the same melt");
        }

        [UnityTest]
        public IEnumerator ColdBoot_StartsTheRoomAtZero()
        {
            RunManager.I.Begin(RunMode.Campaign, 31u);
            RunState.MeltCarry = 30f;
            RunState.Held.Add(Cat.Exploit("cold_boot"));
            RunEvents.ChooseNode?.Invoke(NextNode().id);
            yield return WaitRoom();
            Assert.IsTrue(GameState.Flags.Contains("flag:cold_boot"));
            Assert.AreEqual(0f, GameState.Melt, 0.01f, "Cold Boot");
        }

        static object _sink;

        [UnityTest]
        public IEnumerator LiveModifiers_AreRefoldedEachFrame_WithoutAllocating()
        {
            RunManager.I.Begin(RunMode.Quick, 13u);
            foreach (var id in new[] { "slipstream", "thermal_throttle", "fever_code", "audit_trail", "heat_sink" }) RunState.Held.Add(Cat.Exploit(id));
            RunState.MeltCarry = 72f;   // + the flip cost: Fever Code (melt >= 75) is live
            RunEvents.ChooseNode?.Invoke(NextNode().id);
            yield return WaitRoom();
            RunPerks.DeadZones.Add(new Bounds(new Vector3(50f, 0f, 50f), Vector3.one));   // somewhere else: checked on every SPACE
            var off = new List<MonoBehaviour>();
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            {
                string ns = mb.GetType().Namespace ?? "";
                if (mb.enabled && ns.StartsWith("Desync") && !ns.StartsWith("Desync.Flow") && !ns.StartsWith("Desync.Player")) { mb.enabled = false; off.Add(mb); }
            }
            try
            {
                Assert.IsTrue(PlayerRef.Root.GetComponent<FlipController>().TryFlip(), "into SOURCE: the melt (a condition input) moves every frame");
                yield return null;
                // every Flow / Player Update + LateUpdate, counted exactly (Desync.TestKit.Allocs: a real frame also carries the runner's own)
                var frame = Allocs.Frame("Desync.Flow", "Desync.Player");
                frame.Run();   // the bound calls' first run
                Assert.Greater(Allocs.Count(() => _sink = new byte[64]), 0, "the instrument sees a known allocation");
                int extra = 0;
                float m0 = GameState.Melt;
                for (int f = 0; f < 60; f++) extra += frame.Count();
                Assert.Greater(GameState.Melt, m0);
                Assert.Greater(GameState.Melt, 75f);
                Assert.AreEqual(1.5f, GameState.Mods.ScoreMult, 1e-4f, "Fever Code live at melt >= 75");
                Debug.Log("[DESYNC] run live modifiers: allocations in 60 frames = " + extra);
                Assert.AreEqual(0, extra, "managed allocations in Flow + Player frames: " + frame.Offenders);
            }
            finally
            {
                foreach (var mb in off) if (mb) mb.enabled = true;
            }
        }

        [UnityTest]
        public IEnumerator Perks_CoolantLeak_HeatExchange_PacketLoss_Slipstream_HeatSink_DeadZone()
        {
            RunManager.I.Begin(RunMode.Quick, 11u);
            foreach (var id in new[] { "coolant_leak", "heat_exchange", "packet_loss", "slipstream", "heat_sink" }) RunState.Held.Add(Cat.Exploit(id));
            RunState.MeltCarry = 40f;
            RunEvents.ChooseNode?.Invoke(NextNode().id);
            yield return WaitRoom();
            Assert.IsTrue(GameState.Flags.Contains("flag:heat_sink") && GameState.Flags.Contains("flag:packet_loss"));
            Assert.AreEqual(95f, RunPerks.HeatSinkTo);

            float m0 = GameState.Melt;
            GameEvents.HackFinished?.Invoke(true);
            Assert.AreEqual(m0 - 15f, GameState.Melt, 0.01f, "Coolant Leak");
            Assert.AreEqual(1, GameState.Chain, "Packet Loss: the hack is a chain step");
            m0 = GameState.Melt;
            GameEvents.Grazed?.Invoke("ICE-1");
            Assert.AreEqual(m0 - 3f, GameState.Melt, 0.01f, "Heat Exchange");

            Assert.AreEqual(1f, GameState.Mods.MoveSpeedMult, 1e-4f);
            for (int i = 0; i < 3; i++) GameEvents.HackFinished?.Invoke(false);
            Assert.AreEqual(4, GameState.Chain);
            yield return null;
            Assert.AreEqual(1.08f, GameState.Mods.MoveSpeedMult, 1e-4f, "Slipstream at chain >= 4");

            GameState.Melt = 100f;
            yield return null;
            yield return null;
            Assert.AreEqual(95f, GameState.Melt, 0.5f, "Heat Sink: the first 100 % becomes 95 %");
            Assert.IsTrue(GameState.Alive);

            var flip = PlayerRef.Root.GetComponent<FlipController>();
            RunPerks.DeadZones.Add(new Bounds(PlayerRef.Position, new Vector3(4f, 4f, 4f)));
            Assert.IsFalse(flip.TryFlip(), "SPACE does nothing in a dead zone");
            Assert.IsFalse(LayerState.InSource);
            GameEvents.GlitchForcedFlip?.Invoke();
            Assert.IsTrue(LayerState.InSource, "forced flips still happen");
        }
    }
}
