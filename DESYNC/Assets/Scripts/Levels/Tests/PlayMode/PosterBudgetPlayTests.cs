using System;
using System.Collections;
using System.Collections.Generic;
using Desync.Gen;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Desync.Levels.Tests
{
    /// Act 3 generated rooms (the busiest): the steps that used to hold a frame past the budget never do now. "posters",
    /// "walls" and "navmesh" paid the session's first TextMeshPro / prepared mesh / NavMesh update (now warmed at boot,
    /// RoomBuilder.WarmUp), "wake" switched every entity on in one frame (now one per work item). Logs every room's worst
    /// frame and its step.
    public class PosterBudgetPlayTests
    {
        const int Rooms = 16;
        const float MaxFrameMs = 16f;
        static readonly string[] Watched = { "posters", "walls", "navmesh", "wake" };

        [UnityTest, Timeout(900000)]
        public IEnumerator Act3Rooms_HitchStepsStayInBudget()
        {
            var lib = new ChunkLibrary();
            var files = Resources.LoadAll<TextAsset>("Content/chunks");
            Array.Sort(files, (a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var f in files) lib.chunks.AddRange(ChunkLibrary.FromJson(f.text).chunks);

            var over = new List<string>();
            var worst = new List<float>();
            int built = 0;
            for (uint seed = 3000u; built < Rooms && seed < 3200u; seed++)
            {
                var spec = Generator.GenerateRoom(seed, 3, built % 4, 0, lib);
                if (spec.tiles == null) continue;
                RoomHandle h = null;
                RoomBuilder.BuildAsync(spec, null, x => h = x);
                for (float t0 = Time.realtimeSinceStartup; !h && Time.realtimeSinceStartup - t0 < 30f;) yield return null;
                Assert.IsTrue(h, $"act 3 room {seed} not ready after 30 s");
                worst.Add(h.WorstFrameMs);
                if (Array.IndexOf(Watched, h.WorstStep) >= 0 && h.WorstFrameMs > MaxFrameMs) over.Add($"{seed}: {h.WorstFrameMs:0.0} ms ({h.WorstStep})");
                h.Dispose();
                yield return null;
                built++;
            }
            worst.Sort();
            Debug.Log($"[DESYNC] LEVELS build budget: {built} act 3 rooms, worst frame median {worst[worst.Count / 2]:0.0} ms, " +
                      $"max {worst[worst.Count - 1]:0.0} ms; posters/walls/navmesh/wake over {MaxFrameMs} ms: {(over.Count == 0 ? "none" : string.Join(", ", over))}");
            Assert.AreEqual(Rooms, built, "act 3 rooms built");
            Assert.IsEmpty(over, "posters / walls / navmesh / wake frames over budget");
        }
    }
}
