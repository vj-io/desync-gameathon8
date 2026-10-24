using System;
using System.Collections;
using Desync.Flow;
using Desync.Gen;
using Desync.Net;
using Desync.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Desync.M5.Tests
{
    /// ?autoplay=1&seed=2026&timescale=4&act=1: the query parser, and the bot clearing ACT 1 on seed 2026 through the real run flow
    /// (rooms are stubs: a floor, a RoomInfo, the player, as in the campaign tests).
    public class AutoplayTests
    {
        const string SaveKey = "desync.save.m5auto";
        Action<RoomSpec, Transform, Action<IDisposable>> realBuild;
        GameObject host;

        // ------------------------------------------------------------------ parser
        [Test]
        public void Parse_ReadsTheDocumentedQuery()
        {
            var a = Autoplay.Parse("https://desync-nine.vercel.app/?autoplay=1&seed=2026&timescale=4&act=1");
            Assert.IsTrue(a.on); Assert.AreEqual(2026u, a.seed); Assert.AreEqual(4f, a.timescale); Assert.AreEqual(1, a.act);
            Assert.AreEqual(RunMode.Quick, Autoplay.ModeFor(a));
        }

        [Test]
        public void Parse_Defaults_Clamps_AndIgnoresJunk()
        {
            Assert.IsFalse(Autoplay.Parse(null).on);
            Assert.IsFalse(Autoplay.Parse("").on);
            Assert.IsFalse(Autoplay.Parse("http://localhost:8080/").on, "no query string: nothing happens");
            Assert.IsFalse(Autoplay.Parse("http://x/?autoplay=0").on);
            Assert.IsFalse(Autoplay.Parse("http://x/?autoplay").on, "a bare flag is not 1");
            Assert.IsTrue(Autoplay.Parse("http://x/?AUTOPLAY=true").on);

            var a = Autoplay.Parse("http://x/?autoplay=1");
            Assert.AreEqual(Autoplay.DefaultSeed, a.seed); Assert.AreEqual(1f, a.timescale); Assert.AreEqual(1, a.act);

            Assert.AreEqual(8f, Autoplay.Parse("http://x/?autoplay=1&timescale=99").timescale);
            Assert.AreEqual(1f, Autoplay.Parse("http://x/?autoplay=1&timescale=0.1").timescale);
            Assert.AreEqual(1f, Autoplay.Parse("http://x/?autoplay=1&timescale=NaN").timescale);
            Assert.AreEqual(Autoplay.DefaultSeed, Autoplay.Parse("http://x/?autoplay=1&seed=-5").seed);
            Assert.AreEqual(Autoplay.DefaultSeed, Autoplay.Parse("http://x/?autoplay=1&seed=abc").seed);
            Assert.AreEqual(4294967295u, Autoplay.Parse("http://x/?autoplay=1&seed=4294967295").seed);
            Assert.AreEqual(1, Autoplay.Parse("http://x/?autoplay=1&act=0").act);
            Assert.AreEqual(RunMode.Campaign, Autoplay.ModeFor(Autoplay.Parse("http://x/?autoplay=1&act=3")));
            Assert.IsTrue(Autoplay.Parse("http://x/?autoplay=1&seed=7#top&autoplay=0").on, "the fragment is not part of the query");
        }

        // ------------------------------------------------------------------ the run
        [SetUp]
        public void SetUp()
        {
            Isolation.Begin(SaveKey);
            realBuild = RunManager.BuildRoom;
            RunManager.BuildRoom = StubBuild;
            GameState.God = false;
            Autoplay.ResetStatus();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (host) Object.Destroy(host);
            Autoplay.ResetStatus();
            if (RunManager.I) { RunManager.I.Abandon(); RunManager.I.Abandon(); }
            RunManager.BuildRoom = realBuild;
            GameState.God = false;
            Time.timeScale = 1f;
            var s = SceneManager.GetSceneByName(RunManager.StageScene);
            if (s.IsValid() && s.isLoaded && SceneManager.sceneCount > 1) yield return SceneManager.UnloadSceneAsync(s);
            Isolation.End(SaveKey);
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

        static string Where() => "phase=" + RunState.Phase + " node=" + RunState.NodeId + " act=" + RunState.Act + " rooms=" + RunState.Grades.Count
            + " roomLive=" + RunManager.I.RoomLive + " roomInfo=" + (RoomInfo.Current != null) + " gamePhase=" + GameState.Phase + " alive=" + GameState.Alive
            + " paused=" + GameState.Paused + " timeScale=" + Time.timeScale;

        [UnityTest, Timeout(240000)]
        public IEnumerator Act1_OnSeed2026_ClearsToTheRunResults_AtTimescale4()
        {
            host = new GameObject("TestAutoplay"); Object.DontDestroyOnLoad(host);   // like the real one: the run loads the Breach stage scene
            var ap = host.AddComponent<Autoplay>();
            ap.Begin(new Autoplay.Args { on = true, seed = 2026, timescale = 4f, act = 1 });
            Assert.AreEqual(Autoplay.State.Running, Autoplay.Status);
            Assert.IsTrue(GameState.God, "god mode keeps the bot alive");

            float sawScale = 1f;
            for (float t = 0f; t < 200f && Autoplay.Status == Autoplay.State.Running; t += Time.unscaledDeltaTime)
            {
                sawScale = Mathf.Max(sawScale, Time.timeScale);
                yield return null;
            }
            Assert.AreEqual(Autoplay.State.Done, Autoplay.Status, "autoplay: " + Autoplay.Failure + " | " + Where());
            Assert.IsNull(Autoplay.Failure);
            Assert.AreEqual(RunMode.Quick, RunState.Mode);
            Assert.AreEqual(2026u, RunState.Seed, "the run is on the requested seed");
            Assert.AreEqual(RunPhase.Over, RunState.Phase);
            Assert.IsTrue(RunState.Won, "act 1 and its boss were cleared");
            Assert.GreaterOrEqual(Autoplay.RoomsCleared, 2);
            Assert.IsTrue(RunUnlocks.Killed(SaveService.Data, 1), "the AUDITOR's first kill is saved");
            Assert.GreaterOrEqual(sawScale, 4f, "timescale was applied while it played");
            Assert.AreEqual(1f, Time.timeScale, "and released at the end");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator SameSeed_PlaysTheSameRooms()
        {
            string Play()
            {
                return string.Join(",", RunState.Grades);
            }
            yield return null;
            host = new GameObject("TestAutoplay"); Object.DontDestroyOnLoad(host);   // like the real one: the run loads the Breach stage scene
            var ap = host.AddComponent<Autoplay>();
            ap.Begin(new Autoplay.Args { on = true, seed = 2026, timescale = 8f, act = 1 });
            for (float t = 0f; t < 200f && Autoplay.Status == Autoplay.State.Running; t += Time.unscaledDeltaTime) yield return null;
            Assert.AreEqual(Autoplay.State.Done, Autoplay.Status, Autoplay.Failure);
            string first = Play();
            string graph = string.Join("|", RunState.Graph.acts[0].ConvertAll(n => n.id + ":" + n.type));
            Object.Destroy(host);

            Autoplay.ResetStatus();
            RunManager.I.Abandon();
            host = new GameObject("TestAutoplay2"); Object.DontDestroyOnLoad(host);
            ap = host.AddComponent<Autoplay>();
            ap.Begin(new Autoplay.Args { on = true, seed = 2026, timescale = 8f, act = 1 });
            for (float t = 0f; t < 200f && Autoplay.Status == Autoplay.State.Running; t += Time.unscaledDeltaTime) yield return null;
            Assert.AreEqual(Autoplay.State.Done, Autoplay.Status, Autoplay.Failure);
            Assert.AreEqual(graph, string.Join("|", RunState.Graph.acts[0].ConvertAll(n => n.id + ":" + n.type)), "the same run graph");
            Assert.AreEqual(first, Play(), "and the same grades");
        }
    }
}
