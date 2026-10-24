using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using Desync.Gen;

// Generation acceptance tests (spec GEN-01..06). Chunks live in Assets/Resources/Content/chunks/*.json.
public class GenTests
{
    static ChunkLibrary Lib => _lib ??= ChunkLibrary.FromFolder("Assets/Resources/Content/chunks");
    static ChunkLibrary _lib;
    static readonly uint[] Seeds = Enumerable.Range(0, 500).Select(i => (uint)(i * 7919 + 1)).ToArray();

    [TestCase(1), TestCase(2), TestCase(3)]
    public void GEN_01_SolvableWithMeltMargin(int act)
    {
        foreach (var seed in Seeds)
        {
            var room = Generator.GenerateRoom(seed, act, 1, 0, Lib);
            Assert.IsNull(room.fallbackRoom, $"seed {seed} fell back");
            var v = Generator.Validate(room);
            Assert.IsTrue(v.reachable, $"seed {seed}: {string.Join(",", v.reasons ?? new List<string>())}");
            Assert.LessOrEqual(v.minMeltPeak, 85f, $"seed {seed}");
            Assert.Less(v.maxCameraExposureOnBestPath, 0.4f, $"seed {seed}");
        }
    }

    [Test] public void GEN_02_Deterministic()
    {
        var a = JsonConvert.SerializeObject(Generator.GenerateRoom(42, 2, 2, 5, Lib));
        var b = JsonConvert.SerializeObject(Generator.GenerateRoom(42, 2, 2, 5, Lib));
        Assert.AreEqual(a, b);
    }

    [Test] public void GEN_03_RunGraphShape()
    {
        var g = Generator.GenerateRun(2026, 0);
        Assert.AreEqual(3, g.acts.Count);
        foreach (var act in g.acts)
        {
            Assert.AreEqual(1, act.Count(n => n.type == NodeType.Boss));
            Assert.GreaterOrEqual(act.Count, 5);
            var ids = new HashSet<string>(act.Select(n => n.id));
            var reached = new HashSet<string>(act.Where(n => n.index == 0).Select(n => n.id));
            foreach (var n in act.OrderBy(n => n.index)) if (reached.Contains(n.id)) foreach (var x in n.next) reached.Add(x);
            Assert.IsTrue(ids.SetEquals(reached), "every node reachable from the first row");
        }
    }

    [Test] public void GEN_04_Act1FirstNodeAlwaysInfil()
    {
        foreach (var s in Seeds.Take(200))
            foreach (var n in Generator.GenerateRun(s, 0).acts[0].Where(n => n.index == 0))
                Assert.AreEqual(NodeType.Infil, n.type);
    }

    [Test] public void GEN_05_NoIceNearEntry()
    {
        foreach (var seed in Seeds.Take(200))
            CollectionAssert.DoesNotContain(Generator.Validate(Generator.GenerateRoom(seed, 3, 3, 20, Lib)).reasons ?? new List<string>(), "ice_near_entry");
    }

    [Test] public void GEN_06_P99Under50ms()
    {
        Generator.GenerateRoom(1, 3, 1, 10, Lib);   // warm-up
        var t = Seeds.Take(200).Select(seed => { var sw = Stopwatch.StartNew(); Generator.GenerateRoom(seed, 3, 1, 10, Lib); return sw.Elapsed.TotalMilliseconds; }).OrderBy(x => x).ToList();
        Assert.Less(t[(int)(t.Count * 0.99)], 50.0);
    }

    [Test] public void GEN_07_ChunkLibraryWellFormed()
    {
        Assert.GreaterOrEqual(Lib.chunks.Count, 20, "M2 needs >= 20 chunks (acts 1-2)");
        foreach (var c in Lib.chunks)
        {
            Assert.AreEqual(16, c.tiles.Length, c.id);
            foreach (var row in c.tiles) Assert.AreEqual(16, row.Length, c.id);
        }
        Assert.AreEqual(Lib.chunks.Count, Lib.chunks.Select(c => c.id).Distinct().Count(), "unique ids");
    }
}
