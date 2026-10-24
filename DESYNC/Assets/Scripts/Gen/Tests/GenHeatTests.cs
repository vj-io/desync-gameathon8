using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using Desync.Gen;

// Deep Layer heat placement (HeatPlacement): Double Patrol, Tracker Swarm, Unstable Reality, Gaze Everywhere on top of the
// budgeted room. Placement never changes what the validator sees, so the room under it is the same room.
public class GenHeatTests
{
    static ChunkLibrary Lib => _lib ??= ChunkLibrary.FromFolder("Assets/Resources/Content/chunks");
    static ChunkLibrary _lib;
    static readonly uint[] Seeds = Enumerable.Range(0, 80).Select(i => (uint)(i * 15485863 + 11)).ToArray();

    static readonly HeatPlacement All = new HeatPlacement { extraDrones = 1, trackerShare = 0.5f, glitchBonus = 0.5f, watchers = true };

    static int Count(RoomSpec r, EntityKind k) => r.entities.Count(e => e.kind == k);
    static int Half(float x) => (int)System.Math.Floor(x + 0.5f);
    /// Tiles with the threat markers blanked: what the room is, whatever stands on it.
    static string Shape(RoomSpec r) => string.Join("\n", r.tiles).Replace('P', '.').Replace('G', '.').Replace('C', '.').Replace('I', '.');

    [Test] public void PlacementOff_IsTheSameRoom()
    {
        foreach (var seed in Seeds.Take(30))
            for (int act = 1; act <= 3; act++)
                Assert.AreEqual(JsonConvert.SerializeObject(Generator.GenerateRoom(seed, act, 2, 6, Lib)),
                                JsonConvert.SerializeObject(Generator.GenerateRoom(seed, act, 2, 6, Lib, default)), $"act {act} seed {seed}");
    }

    [Test] public void Placement_IsDeterministic()
    {
        var a = JsonConvert.SerializeObject(Generator.GenerateRoom(77, 3, 2, 10, Lib, All));
        var b = JsonConvert.SerializeObject(Generator.GenerateRoom(77, 3, 2, 10, Lib, All));
        Assert.AreEqual(a, b);
    }

    /// Every placement keeps the room (shape, mirror, par, the budgeted entities except the hunters it converts) and only adds.
    [TestCase(1), TestCase(2), TestCase(3)]
    public void Placement_AddsOnTopOfTheSameRoom(int act)
    {
        int drones = 0, trackers = 0, glitch = 0, rooms = 0;
        foreach (var seed in Seeds)
        {
            var b = Generator.GenerateRoom(seed, act, 2, 10, Lib);
            var h = Generator.GenerateRoom(seed, act, 2, 10, Lib, All);
            if (b.fallbackRoom != null) { Assert.IsNotNull(h.fallbackRoom, $"seed {seed}"); continue; }
            rooms++;
            Assert.AreEqual(Shape(b), Shape(h), $"seed {seed}");
            Assert.AreEqual(b.mirror, h.mirror);
            Assert.AreEqual(b.par, h.par, $"seed {seed}");
            foreach (var e in b.entities)
            {
                var m = h.entities.Single(x => x.id == e.id);
                Assert.AreEqual(e.x, m.x); Assert.AreEqual(e.z, m.z);
                if (e.kind == EntityKind.IceVent) Assert.That(m.kind, Is.EqualTo(EntityKind.IceVent).Or.EqualTo(EntityKind.TrackerIce));
                else Assert.AreEqual(e.kind, m.kind, e.id);
            }
            int hunters = Count(b, EntityKind.IceVent);
            Assert.AreEqual(Count(b, EntityKind.TrackerIce) + Half(hunters * 0.5f), Count(h, EntityKind.TrackerIce), $"Tracker Swarm, seed {seed}");
            Assert.AreEqual(hunters - Half(hunters * 0.5f), Count(h, EntityKind.IceVent));
            int dDrones = Count(h, EntityKind.Drone) - Count(b, EntityKind.Drone), dGlitch = Count(h, EntityKind.Glitch) - Count(b, EntityKind.Glitch);
            Assert.That(dDrones, Is.InRange(0, 1), "Double Patrol adds one drone");
            Assert.That(dGlitch, Is.InRange(0, Half(Count(b, EntityKind.Glitch) * 0.5f)), "Unstable Reality adds up to +50 %");
            if (act == 3) Assert.GreaterOrEqual(Count(h, EntityKind.Watcher), 1, $"Gaze Everywhere, seed {seed}");
            else Assert.AreEqual(Count(b, EntityKind.Watcher), Count(h, EntityKind.Watcher));
            drones += dDrones; trackers += Half(hunters * 0.5f); glitch += dGlitch;
        }
        System.Console.WriteLine($"act {act}: rooms {rooms}, +drones {drones}, ->trackers {trackers}, +glitch {glitch}");
        Assert.Greater(rooms, Seeds.Length * 3 / 4);
        Assert.Greater(drones, rooms / 2, "most rooms have a free patrol loop for Double Patrol");
        if (act >= 2) Assert.Greater(glitch, 0, "glitch zones exist from act 2 and Unstable Reality adds to them");
    }

    /// Placed heat threats keep every generator rule: on their markers, unique ids, rooms still validate with the same margins,
    /// no ICE near the entry, a new Watcher never sees the spawn or a terminal, and no more fallbacks than without heat.
    [TestCase(1), TestCase(2), TestCase(3)]
    public void Placement_KeepsValidatorMarginsAndMarkers(int act)
    {
        int fbBase = 0, fbHeat = 0;
        foreach (var seed in Seeds)
        {
            if (Generator.GenerateRoom(seed, act, 3, 10, Lib).fallbackRoom != null) fbBase++;
            var room = Generator.GenerateRoom(seed, act, 3, 10, Lib, All);
            if (room.fallbackRoom != null) { fbHeat++; continue; }
            var v = Generator.Validate(room);
            CollectionAssert.IsEmpty(v.reasons, $"seed {seed}");
            Assert.IsTrue(v.reachable);
            Assert.LessOrEqual(v.minMeltPeak, 85f);
            Assert.Less(v.maxCameraExposureOnBestPath, 0.4f);
            Assert.AreEqual(room.entities.Count, room.entities.Select(e => e.id).Distinct().Count(), $"seed {seed}");
            foreach (var e in room.entities)
            {
                char ch = room.tiles[(int)(-e.z / 0.5f)][(int)(e.x / 0.5f)];
                char want = e.kind == EntityKind.Glitch ? 'G' : e.kind == EntityKind.Drone || e.kind == EntityKind.Lantern || e.kind == EntityKind.Guard ? 'P'
                    : e.kind == EntityKind.CameraFixed || e.kind == EntityKind.CameraSweep || e.kind == EntityKind.Watcher ? 'C'
                    : e.kind == EntityKind.IceVent || e.kind == EntityKind.TrackerIce ? 'I' : ch;
                Assert.AreEqual(want, ch, $"{e.id} seed {seed}");
            }
        }
        Assert.AreEqual(fbBase, fbHeat, "heat placement never costs a generated room");
    }
}
