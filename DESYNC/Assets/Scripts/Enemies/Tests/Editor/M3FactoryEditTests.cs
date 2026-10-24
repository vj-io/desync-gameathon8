using NUnit.Framework;
using UnityEngine;

namespace Desync.Enemies.Tests
{
    /// The M3 factories build fully wired objects at EDIT time too (the factory contract), with the spec stats.
    public class M3FactoryEditTests
    {
        Transform _root;

        [SetUp] public void SetUp() => _root = new GameObject("M3EnemyTestRoot").transform;
        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(_root.gameObject);
            foreach (var t in Object.FindObjectsByType<TraceSystem>(FindObjectsInactive.Include)) Object.DestroyImmediate(t.gameObject);
        }

        [Test]
        public void M3FactoriesBuildWiredObjects()
        {
            var lantern = EnemyFactory.Lantern(_root, new[] { Vector3.zero, new Vector3(4f, 0f, 0f) }, 1.5f);
            var spin = lantern.GetComponent<LanternBeam>();
            Assert.AreEqual(90f, spin.beam.eulerAngles.y, 0.5f, "beam posed at its phase");
            var lc = lantern.GetComponentInChildren<VisionCone>();
            Assert.AreEqual(LanternBeam.Range, lc.range);
            Assert.AreEqual(LanternBeam.Angle, lc.angle);
            Assert.IsFalse(lc.bothLayers);
            var lp = lantern.GetComponent<DronePatrol>();
            Assert.AreEqual(LanternBeam.Speed, lp.Speed);
            Assert.IsNull(lp.hijack);
            Assert.AreEqual(GameLayers.Drone, lantern.layer);

            var sen = EnemyFactory.Sentinel(_root, new Vector3(5f, 0f, 0f)).GetComponent<Sentinel>();
            Assert.IsNotNull(sen.charge);
            Assert.IsFalse(sen.charge.gameObject.activeSelf);
            Assert.AreEqual(1, sen.bodyRenderers.Length);

            var split = EnemyFactory.SplitterIce(_root, new Vector3(10f, 0f, 0f)).GetComponent<SplitterIce>();
            Assert.IsFalse(split.hunter.gameObject.activeSelf, "pooled inactive");
            Assert.AreEqual(new Vector3(10f, 0f, 0f), split.hunter.patrol[0]);
            Assert.AreEqual(2, split.minis.Length);
            foreach (var m in split.minis) { Assert.IsFalse(m.gameObject.activeSelf); Assert.AreEqual(GameLayers.Ice, m.gameObject.layer); }

            var dm = EnemyFactory.Daemon(_root, new Vector3(15f, 0f, 0f)).GetComponent<Daemon>();
            Assert.IsFalse(dm.agent.enabled, "placed on the NavMesh on its first frame");
            Assert.AreEqual(new Vector3(15f, 0f, 0f), dm.post);

            var w = EnemyFactory.Watcher(_root, new Vector3(0f, 3f, 5f), 180f);
            var wc = w.GetComponentInChildren<VisionCone>();
            Assert.IsTrue(wc.bothLayers);
            Assert.IsFalse(wc.blindable);
            Assert.AreEqual(EnemyFactory.WatcherRange, wc.range);
            Assert.IsNull(w.GetComponent<CameraSweep>(), "not a camera");
            Assert.AreSame(wc, w.GetComponent<WatcherEye>().cone);

            var ps = EnemyFactory.PuppetString(_root, new Vector3(0f, 0f, 8f), "PS-3").GetComponent<PuppetStringPickup>();
            Assert.AreEqual("PS-3", ps.id);
            Assert.IsFalse(ps.visual.gameObject.activeSelf, "SOURCE only");

            Assert.AreEqual(1, Object.FindObjectsByType<TraceSystem>(FindObjectsInactive.Include).Length, "one TraceSystem per room");
        }
    }
}
