using System;
using System.Collections;
using Desync.Gen;
using Desync.Net;
using Desync.Player;
using Desync.Rules;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Desync.Flow.Tests
{
    /// Time-Attack in play: a generated room (stub geometry, the real spec) with its dev time and no run state, R restart
    /// under 300 ms, deaths that just restart, a clear that saves best / medal / ghost, the ghost replaying on the retry,
    /// Continue back to the title's select screen, and a classic room through its own scene.
    public class TimeAttackPlayTests
    {
        const string SaveKey = "desync.save.tatests";
        Action<RoomSpec, Transform, Action<IDisposable>> realBuild;
        RoomSpec builtSpec;
        int selects;
        bool hadBest; float oldBestT; int oldBestG;   // a classic clear writes the real PlayerPrefs best of room 1: keep the developer's

        [SetUp]
        public void SetUp()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            SaveService.Unload(SaveKey);
            realBuild = RunManager.BuildRoom;
            RunManager.BuildRoom = StubBuild;
            builtSpec = null;
            selects = 0;
            TimeAttack.SelectRequested += OnSelect;
            GameState.God = false;
            hadBest = PlayerPrefs.HasKey("bestG0"); oldBestT = PlayerPrefs.GetFloat("bestT0"); oldBestG = PlayerPrefs.GetInt("bestG0");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            TimeAttack.SelectRequested -= OnSelect;
            TimeAttack.I.Stop();
            TimeAttackRooms.DebugOpen = Debug.isDebugBuild;
            RunManager.BuildRoom = realBuild;
            GameState.God = false;
            Time.timeScale = 1f;
            var s = SceneManager.GetSceneByName(RunManager.StageScene);
            if (s.IsValid() && s.isLoaded && SceneManager.sceneCount > 1) yield return SceneManager.UnloadSceneAsync(s);
            PlayerPrefs.DeleteKey("bestT0"); PlayerPrefs.DeleteKey("bestG0");
            if (hadBest) { PlayerPrefs.SetFloat("bestT0", oldBestT); PlayerPrefs.SetInt("bestG0", oldBestG); }
            PlayerPrefs.DeleteKey(SaveKey);
            SaveService.Unload();
        }

        static object _sink;

        void OnSelect() => selects++;

        void StubBuild(RoomSpec spec, Transform parent, Action<IDisposable> done)
        {
            builtSpec = spec;
            var root = new GameObject("StubRoom");
            root.transform.SetParent(parent, false);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.layer = GameLayers.Floor;
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(40f, 1f, 40f);
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

        static IEnumerator Until(Func<bool> cond, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!cond() && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(cond(), "timed out after " + seconds + " s waiting for: " + what);
        }

        static bool Playing => RoomInfo.Current && GameState.Phase == GameState.PhaseKind.Playing && GameState.Alive;

        /// Walks the player east at 3 m/s (by room clock) until the room clock reaches `until`.
        static IEnumerator Walk(float until)
        {
            var fpc = PlayerRef.Root.GetComponent<FirstPersonController>();
            while (RoomClock.Time < until)
            {
                fpc.Teleport(new Vector3(RoomClock.Time * 3f, 0f, 0f), 90f);
                yield return null;
            }
        }

        static IEnumerator Win()
        {
            RoomManager.I.OpenExit();
            GameEvents.ExitReached?.Invoke();
            yield return Until(() => GameState.Phase == GameState.PhaseKind.Results, 2f, "results");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Generated_Room_Has_Its_Dev_Time_No_Run_State_And_Restarts_Fast()
        {
            GameState.Flags.Add("flag:cold_boot");   // leftovers of a run must not leak in
            var m = Modifiers.Default; m.FlipCost = 9f; GameState.Mods = m;
            RoomManager.StartMelt = 40f;
            TimeAttackRooms.DebugOpen = false;
            Assert.IsFalse(TimeAttack.Play("ta_a1_03"), "locked without the meta unlock");
            Assert.IsFalse(TimeAttack.Live);
            SaveService.Data.unlocks.Add(TimeAttackRooms.UnlockKey);
            Assert.IsTrue(TimeAttack.Play("ta_a1_03"));
            Assert.IsFalse(TimeAttack.Play("ta_a9_99"), "unknown id");
            yield return Until(() => Playing, 10f, "the room to start");

            var r = TimeAttackRooms.Find("ta_a1_03");
            Assert.IsTrue(TimeAttack.Live);
            Assert.AreSame(r, TimeAttack.Current);
            Assert.AreSame(TimeAttackRooms.Spec(r), builtSpec, "the fixed-seed spec was built");
            Assert.AreEqual(SceneManager.GetActiveScene().name, RunManager.StageScene);
            Assert.Greater(GameState.RoomTDev, 0f);
            Assert.AreEqual(TimeAttackRooms.TDev(r), GameState.RoomTDev, "HUD timer / medal ticks run on the room's dev time");
            Assert.AreEqual(Rng.HashStr("ta_a1_03"), GameState.Seed, "bug reports carry the room seed");
            Assert.AreEqual(0, GameState.Flags.Count, "no exploits or mutators");
            Assert.AreEqual(5f, GameState.Mods.FlipCost);
            Assert.AreEqual(0f, GameState.Melt);
            Assert.IsFalse(RunManager.I.RoomLive, "not a run room: R restarts, deaths cost nothing");
            Assert.AreEqual(RunPhase.None, RunState.Phase);

            yield return Walk(0.3f);
            GameState.LastRestartMs = -1f;
            RoomManager.I.Reboot();   // what R does
            yield return Until(() => GameState.LastRestartMs > 0f, 2f, "the restart");
            Assert.Less(GameState.LastRestartMs, 300f, "restart returns control in < 300 ms");
            Assert.Less(RoomClock.Time, 0.2f, "clock back to zero");
            Assert.Less(new Vector2(PlayerRef.Position.x, PlayerRef.Position.z).magnitude, 0.05f, "back at the spawn");

            PlayerRef.Kill(DeathCause.Ice);
            Assert.AreEqual(GameState.PhaseKind.Recap, GameState.Phase);
            yield return Until(() => Playing, 4f, "the recap to end in a restart");
            Assert.IsTrue(TimeAttack.Live, "a death never ends Time-Attack");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Clear_Saves_Best_Medal_And_Ghost_And_The_Ghost_Replays()
        {
            var r = TimeAttackRooms.Find("ta_a2_04");
            Assert.IsTrue(TimeAttack.Play(r.id));
            yield return Until(() => Playing, 10f, "the room to start");
            var ta = TimeAttack.I;
            Assert.AreEqual(0, ta.Ghost.Count, "no ghost yet");
            Assert.IsFalse(ta.Figure.Visible);

            yield return Walk(1.2f);
            Assert.Greater(ta.Recording.Count, 20, "20 Hz by room clock");
            Assert.LessOrEqual(ta.Recording.Count, Mathf.CeilToInt(RoomClock.Time * GhostTrack.Hz) + 1);
            yield return Win();

            var save = SaveService.Data;
            float best = TimeAttackRooms.BestTime(save, r);
            Assert.Greater(best, 1.1f);
            Assert.AreEqual(Medal.Sync, TimeAttackRooms.MedalOf(save, r), "1.2 s beats any dev time");
            CollectionAssert.Contains(save.unlocks, r.id + "_sync");
            Assert.IsNotNull(TimeAttackRooms.LoadGhost(save, r.id), "the clear is the room's ghost");
            Assert.AreEqual(best, ta.Ghost.RunTime, 1e-4f);
            Assert.Greater(ta.Ghost.Count, 20);

            GameEvents.RestartRequested?.Invoke();   // [R] RETRY on the results
            yield return Until(() => Playing, 2f, "the retry");
            yield return Until(() => RoomClock.Time >= 0.6f, 3f, "the clock");
            Assert.IsTrue(ta.Figure.Visible, "the ghost runs beside you");
            Assert.AreEqual(RoomClock.Time * 3f, ta.Figure.transform.position.x, 0.35f, "where you were at this time");
            Assert.AreEqual(90f, ta.Figure.transform.eulerAngles.y, 6f, "facing where you faced");
            Assert.IsFalse(ta.Figure.InSource);
            LayerState.Set(true);
            yield return null;
            Assert.IsFalse(ta.Figure.InSource, "the ghost shows its own layer, not yours");
            LayerState.Set(false);

            TestDelegate play = () =>
            {
                for (int i = 0; i < 200; i++)
                {
                    ta.Ghost.Sample(i * 0.01f, out var p, out float yaw, out bool src);
                    ta.Figure.Show(p, yaw, src || (i & 8) != 0);
                }
            };
            play();   // warm up
            Assert.That(() => { _sink = new byte[64]; }, Is.AllocatingGCMemory(), "the instrument sees a known allocation");
            Assert.That(play, Is.Not.AllocatingGCMemory(), "ghost playback allocates nothing");

            yield return Until(() => RoomClock.Time >= best + TimeAttack.GhostLinger + 0.1f, 4f, "the ghost's end");
            Assert.IsFalse(ta.Figure.Visible, "gone once its run is over");
            yield return Win();   // slower: the ghost stays
            Assert.AreEqual(best, ta.Ghost.RunTime, 1e-4f);
            Assert.AreEqual(best, TimeAttackRooms.BestTime(SaveService.Data, r), 1e-4f);

            GameEvents.ContinueRequested?.Invoke();
            Assert.IsFalse(TimeAttack.Live, "Continue ends the room");
            yield return Until(() => selects == 1, 15f, "the select screen request");
            Assert.AreEqual(SceneFlow.Title, SceneManager.GetActiveScene().name);
            Assert.IsFalse(ta.Figure.Visible);
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator Classic_Room_Uses_Its_Scene_Medal_Record_And_Ghost()
        {
            Assert.IsTrue(TimeAttack.Play("r1"));
            yield return Until(() => Playing && SceneManager.GetActiveScene().name == SceneFlow.Room(0), 15f, "Room_01");
            Assert.IsTrue(TimeAttack.Live);
            Assert.AreEqual(10f, GameState.RoomTDev, 1e-4f, "rooms.json dev time");
            yield return Walk(0.5f);
            yield return Win();
            var save = SaveService.Data;
            Assert.AreEqual("SYNC", save.medals["r1"], "classic medals in SaveV3.medals (NetHost)");
            Assert.Greater(save.best["r1"].time, 0.4f);
            Assert.IsNotNull(TimeAttackRooms.LoadGhost(save, "r1"));
            GameEvents.ContinueRequested?.Invoke();
            yield return Until(() => selects == 1, 15f, "the select screen request");
            Assert.AreEqual(SceneFlow.Title, SceneManager.GetActiveScene().name, "back to the title, not on to Room_02");
        }
    }
}
