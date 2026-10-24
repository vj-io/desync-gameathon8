using System;
using System.Collections;
using System.Collections.Generic;
using Desync.Gen;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace Desync.Levels.Tests
{
    /// Generated rooms built at runtime: 20 seeds per act (acts 1-2) from the shipped chunks.
    public class RoomBuilderPlayTests
    {
        const int SeedsPerAct = 20, MaxTris = 60000, MaxDrawCalls = 150;
        const float MaxFrameMs = 16f;

        static ChunkLibrary Chunks()
        {
            var lib = new ChunkLibrary();
            var files = Resources.LoadAll<TextAsset>("Content/chunks");
            Array.Sort(files, (a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var f in files) lib.chunks.AddRange(ChunkLibrary.FromJson(f.text).chunks);
            return lib;
        }

        static RoomHandle _built;

        static IEnumerator Build(RoomSpec spec)
        {
            _built = null;
            bool done = false;
            RoomBuilder.BuildAsync(spec, null, h => { _built = h; done = true; });
            float t0 = Time.realtimeSinceStartup;
            while (!done && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            Assert.IsTrue(done, $"room {spec.seed} act {spec.act} not ready after 30 s");
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator TwentySeedsPerActBuildWithinBudgets()
        {
            var lib = Chunks();
            Assert.Greater(lib.chunks.Count, 0, "no chunks under Resources/Content/chunks");

            // warm-up room: first-use costs (shaders, fonts, physics, pools) are not per-room costs
            yield return Build(Generator.GenerateRoom(7u, 1, 0, 0, lib));
            _built.Dispose();
            yield return null;
            int meshesBefore = Resources.FindObjectsOfTypeAll<Mesh>().Length;

            var worst = new List<float>();
            int maxTris = 0, maxDraws = 0, fallbacks = 0;
            for (int act = 1; act <= 2; act++)
            {
                int built = 0;
                for (uint seed = 1000u * (uint)act; built < SeedsPerAct; seed++)
                {
                    int node = built % 4;
                    var spec = Generator.GenerateRoom(seed, act, node, 0, lib);
                    if (spec.tiles == null) { fallbacks++; continue; }   // classic-room fallback: RunManager loads that scene
                    string tag = $"act {act} node {node} seed {seed}";

                    Assert.IsTrue(Generator.Validate(spec).reachable, tag + ": exit not reachable");
                    yield return Build(spec);
                    var h = _built;
                    Assert.IsTrue(h.Ready, tag);
                    Assert.IsTrue(h.Player, tag + ": no player");
                    Assert.AreSame(h.Player.transform, PlayerRef.Root, tag + ": player not registered");
                    Assert.AreSame(h.Info, RoomInfo.Current, tag + ": RoomInfo.Current");
                    Assert.AreEqual(-1, h.Info.index, tag);
                    Assert.AreEqual(spec.entities.FindAll(e => e.kind == EntityKind.Terminal).Count, h.Info.terminalCount, tag);
                    Assert.IsTrue(NavMesh.SamplePosition(h.Info.spawn.position, out _, 1f, NavMesh.AllAreas), tag + ": no NavMesh at spawn");
                    Assert.LessOrEqual(h.Tris, MaxTris, tag + ": tris");
                    Assert.LessOrEqual(h.DrawCalls, MaxDrawCalls, tag + ": draw calls");
                    worst.Add(h.WorstFrameMs);
                    maxTris = Mathf.Max(maxTris, h.Tris);
                    maxDraws = Mathf.Max(maxDraws, h.DrawCalls);

                    h.Dispose();
                    yield return null;
                    Assert.IsFalse(h, tag + ": room root survived Dispose");
                    Assert.LessOrEqual(Resources.FindObjectsOfTypeAll<Mesh>().Length, meshesBefore, tag + ": meshes leaked");
                    built++;
                }
            }

            worst.Sort();
            float median = worst[worst.Count / 2];
            Debug.Log($"[DESYNC] LEVELS builder test: {worst.Count} rooms, worst frame median {median:0.0} ms, max {worst[worst.Count - 1]:0.0} ms; " +
                      $"max tris {maxTris}, max draw calls {maxDraws}, fallbacks skipped {fallbacks}");
            Assert.LessOrEqual(median, MaxFrameMs, "median worst frame during build");
        }

        /// RunManager may dispose a room and build the next in one frame. Destroy is deferred to the end of the frame, so
        /// no build work may run in the caller's frame: the sensors would find the dying room's TraceSystem and make none.
        [UnityTest]
        public IEnumerator BuildDoesNoWorkInTheCallersFrame()
        {
            var lib = Chunks();
            RoomSpec spec = null;
            for (uint seed = 500u; spec?.tiles == null || !spec.entities.Exists(e => e.kind == EntityKind.CameraFixed || e.kind == EntityKind.CameraSweep); seed++)
                spec = Generator.GenerateRoom(seed, 1, 1, 0, lib);
            RoomHandle h = null;
            RoomBuilder.BuildAsync(spec, null, x => h = x);
            var root = GameObject.Find("BreachRoom " + spec.seed);
            Assert.IsTrue(root, "room root");
            Assert.AreEqual(0, root.GetComponent<RoomHandle>().Frames + root.transform.childCount, "build work ran in the caller's frame");
            float t0 = Time.realtimeSinceStartup;
            while (!h && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            Assert.IsTrue(h, "room never finished");
            Assert.IsTrue(h.GetComponentInChildren<Desync.Enemies.TraceSystem>(true), "room has no TraceSystem of its own");
            h.Dispose();
            yield return null;
        }

        /// The AUDITOR arena (a RoomSpec in Resources/Content/bosses): the builder places its spawn, 3 terminals and the
        /// uplink exit; RunManager adds EnemyFactory.Auditor afterwards, whose ICE patrol loops need the NavMesh.
        [UnityTest]
        public IEnumerator AuditorArenaBuilds()
        {
            var ta = Resources.Load<TextAsset>("Content/bosses/auditor");
            Assert.IsTrue(ta, "Content/bosses/auditor.json");
            var spec = Newtonsoft.Json.JsonConvert.DeserializeObject<RoomSpec>(ta.text);
            yield return Build(spec);
            var h = _built;
            Assert.IsTrue(h.Player, "player");
            Assert.AreEqual(3, h.Info.terminalCount, "terminals");
            var c = Desync.Enemies.Auditor.ArenaCenter;
            foreach (var loop in Desync.Enemies.Auditor.Loops)
                foreach (var o in loop)
                    Assert.IsTrue(NavMesh.SamplePosition(new Vector3(c.x + o.x, 0f, c.y + o.y), out _, 0.5f, NavMesh.AllAreas), $"NavMesh at hunter waypoint {o}");
            Assert.LessOrEqual(h.Tris, MaxTris, "tris");
            Assert.LessOrEqual(h.DrawCalls, MaxDrawCalls, "draw calls");
            h.Dispose();
            yield return null;
        }

        [Test]
        public void FallbackSpecIsRefused()
        {
            Assert.Throws<ArgumentException>(() => RoomBuilder.BuildAsync(new RoomSpec { fallbackRoom = "Room_01" }, null, null));
        }
    }
}
