using Desync.Gen;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Desync.Enemies.Tests
{
    /// BREACH additions: the generated-room factory overloads, the tracker nest and the AUDITOR build at edit time,
    /// and the AUDITOR arena (Content/bosses/auditor.json) is a valid RoomSpec that agrees with Auditor's offsets.
    public class BreachEnemyEditTests
    {
        Transform _root;

        [SetUp] public void SetUp() => _root = new GameObject("BreachEnemyTestRoot").transform;
        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(_root.gameObject);
            foreach (var t in Object.FindObjectsByType<TraceSystem>(FindObjectsInactive.Include)) Object.DestroyImmediate(t.gameObject);
        }

        [Test]
        public void GeneratedRoomFactoriesBuildWithSpecStats()
        {
            var fixedCam = EnemyFactory.FixedCamera(_root, new Vector3(0f, 2.5f, 0f), 90f);
            var fc = fixedCam.GetComponentInChildren<VisionCone>();
            Assert.AreEqual(60f, fc.angle);
            Assert.AreEqual(12f, fc.range);
            Assert.AreEqual(0f, fixedCam.GetComponent<CameraSweep>().halfAngle, "fixed camera never sweeps");

            var sweepCam = EnemyFactory.SweepCamera(_root, new Vector3(4f, 2.5f, 0f), 0f);
            var sc = sweepCam.GetComponentInChildren<VisionCone>();
            var sw = sweepCam.GetComponent<CameraSweep>();
            Assert.AreEqual(10f, sc.range);
            Assert.AreEqual(90f, sc.angle + 2f * sw.halfAngle, 1e-4f, "cone + travel cover the 90 deg arc");
            Assert.AreEqual(4f, sw.oneWay, 1e-4f, "8 s period = 4 s each way");

            var drone = EnemyFactory.Drone(_root, new[] { Vector3.zero, new Vector3(6f, 0f, 0f) }, 1f, 3f);
            var dp = drone.GetComponent<DronePatrol>();
            Assert.AreEqual(3f, dp.Speed);
            Assert.AreEqual(10f, dp.cone.range);
            Assert.AreEqual(45f, dp.cone.angle);
            Assert.AreEqual(3f, drone.transform.position.x, 0.01f, "phase 1 s -> 3 m along the loop");
            Assert.AreEqual(GameConfig.I.droneSpeed, EnemyFactory.Drone(_root, new[] { Vector3.zero }, 0f).GetComponent<DronePatrol>().Speed,
                            "classic overload keeps GameConfig speed");

            var gz = EnemyFactory.GlitchZone(_root, new Vector3(-4f, 0f, 0f), 1.5f, 1f, 0.5f).GetComponent<GlitchZone>();
            Assert.AreEqual(1f, gz.delay);
            Assert.AreEqual(0.5f, gz.telegraph);
            Assert.AreEqual(-1f, EnemyFactory.GlitchZone(_root, Vector3.zero, 2f).GetComponent<GlitchZone>().delay, "classic overload unchanged");

            var nest = EnemyFactory.TrackerIce(_root, new Vector3(6f, 0f, 6f)).GetComponent<TrackerNest>();
            Assert.IsNotNull(nest.hunter);
            Assert.IsFalse(nest.hunter.gameObject.activeSelf, "tracker waits in its nest");
            Assert.AreEqual(GameLayers.Ice, nest.hunter.gameObject.layer);
            Assert.IsNotNull(nest.glow);
        }

        [Test]
        public void AuditorBuildsRingScannersZonesAndHunters()
        {
            var a = EnemyFactory.Auditor(_root, new Vector3(12f, 0f, -12f)).GetComponent<Auditor>();
            Assert.AreEqual(Auditor.RingHeight, a.ring.position.y, 1e-4f);
            Assert.AreEqual(3, a.scanners.Length);
            foreach (var s in a.scanners)
            {
                Assert.IsFalse(s.bothLayers, "scanners see RENDER only");
                Assert.IsNotNull(s.spot);
            }
            Assert.AreEqual(Auditor.GlitchOffsets.Length, a.glitchZones.Length);
            foreach (var z in a.glitchZones) Assert.IsFalse(z.activeSelf, "glitch zones wait for P2");
            Assert.AreEqual(3, a.hunters.Length);
            foreach (var h in a.hunters)
            {
                Assert.IsFalse(h.gameObject.activeSelf);
                Assert.AreEqual(4, h.patrol.Length);
            }
            Assert.Less(Vector3.Distance(new Vector3(12f - 7.75f, 0f, -12f + 3.75f), a.hunters[0].patrol[0]), 1e-4f, "loops are centre offsets");
            Assert.AreEqual(1, a.Phase);
            Assert.AreEqual(1, Object.FindObjectsByType<TraceSystem>(FindObjectsInactive.Include).Length);
        }

        [Test]
        public void AuditorArenaIsAValidRoomSpecMatchingTheBoss()
        {
            var text = Resources.Load<TextAsset>("Content/bosses/auditor").text;
            var spec = JsonConvert.DeserializeObject<RoomSpec>(text);
            Assert.IsNull(spec.fallbackRoom);
            Assert.AreEqual(spec.gridH * 16, spec.tiles.Length);
            foreach (var row in spec.tiles) Assert.AreEqual(spec.gridW * 16, row.Length);

            int terminals = 0, spawns = 0, exits = 0;
            foreach (var e in spec.entities)
            {
                if (e.kind == EntityKind.Terminal) terminals++;
                if (e.kind == EntityKind.Spawn) spawns++;
                if (e.kind == EntityKind.Exit) exits++;
                Assert.AreEqual(e.kind == EntityKind.Spawn ? 'S' : e.kind == EntityKind.Exit ? 'E' : 'T', Tile(spec, e.x, e.z), e.id);
            }
            Assert.AreEqual(3, terminals);
            Assert.AreEqual(1, spawns);
            Assert.AreEqual(1, exits);

            var boss = JObject.Parse(text)["boss"];
            Assert.AreEqual("auditor", (string)boss["id"]);
            Assert.AreEqual(Auditor.ArenaCenter.x, (float)boss["x"]);
            Assert.AreEqual(Auditor.ArenaCenter.y, (float)boss["z"]);
            foreach (var o in Auditor.GlitchOffsets) Assert.AreEqual('G', Tile(spec, Auditor.ArenaCenter.x + o.x, Auditor.ArenaCenter.y + o.y));
            foreach (var loop in Auditor.Loops)
                for (int i = 0; i < loop.Length; i++)
                    Assert.AreEqual(i == 0 ? 'I' : 'P', Tile(spec, Auditor.ArenaCenter.x + loop[i].x, Auditor.ArenaCenter.y + loop[i].y));

            var v = Generator.Validate(spec);
            Debug.Log($"[DESYNC] AUDITOR arena: best path {v.bestPathSeconds:0.0} s, melt peak {v.minMeltPeak:0} %, par {spec.par}");
            Assert.IsTrue(v.reachable);
            Assert.IsEmpty(v.reasons, string.Join(",", v.reasons));
        }

        /// Tile char under a room-space point (x east, z = -south; 0.5 m tiles, row 0 = north).
        static char Tile(RoomSpec s, float x, float z) => s.tiles[Mathf.FloorToInt(-z / 0.5f)][Mathf.FloorToInt(x / 0.5f)];
    }
}
