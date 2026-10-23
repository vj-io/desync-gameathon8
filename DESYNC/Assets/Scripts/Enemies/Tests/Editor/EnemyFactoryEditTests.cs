using NUnit.Framework;
using UnityEngine;

namespace Desync.Enemies.Tests
{
    /// Every factory must build a fully wired object at EDIT time (LevelBuilder calls them in the editor).
    public class EnemyFactoryEditTests
    {
        Transform _root;

        [SetUp] public void SetUp() => _root = new GameObject("EnemyTestRoot").transform;
        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(_root.gameObject);
            foreach (var t in Object.FindObjectsByType<TraceSystem>(FindObjectsInactive.Include)) Object.DestroyImmediate(t.gameObject);
        }

        [Test]
        public void AllFactoriesBuildWiredObjects()
        {
            var cam = EnemyFactory.Camera(_root, new Vector3(0f, 2.5f, 0f), 90f, 20f, 0.5f);
            Assert.AreSame(_root, cam.transform.parent);
            Assert.AreEqual(90f, cam.transform.eulerAngles.y, 0.01f);
            var sweep = cam.GetComponent<CameraSweep>();
            Assert.IsNotNull(sweep.pivot);
            var cone = cam.GetComponentInChildren<VisionCone>();
            Assert.IsNotNull(cone.coneRenderer);
            Assert.IsNotNull(cone.spot);
            Assert.AreEqual(LightShadows.None, cone.spot.shadows);
            Assert.AreEqual(1, Object.FindObjectsByType<TraceSystem>(FindObjectsInactive.Include).Length, "one TraceSystem per room");

            var drone = EnemyFactory.Drone(_root, new[] { new Vector3(0f, 0f, 0f), new Vector3(6f, 0f, 0f) }, 1f);
            Assert.AreEqual(GameLayers.Drone, drone.layer);
            Assert.IsTrue(drone.GetComponent<Rigidbody>().isKinematic);
            Assert.IsNotNull(drone.GetComponent<SphereCollider>());
            var patrol = drone.GetComponent<DronePatrol>();
            Assert.IsNotNull(patrol.cone);
            Assert.IsNotNull(patrol.hijack);
            Assert.AreEqual(GameConfig.I.droneSpeed + 0f, drone.transform.position.x, 0.01f, "phase 1 s -> droneSpeed metres along the loop");
            Assert.AreEqual(GameConfig.I.droneHover, drone.transform.position.y, 0.01f);

            var vent = EnemyFactory.IceVent(_root, new Vector3(3f, 0f, 3f));
            var iv = vent.GetComponent<IceVent>();
            Assert.AreEqual(Mathf.Clamp(GameConfig.I.iceMaxAlive, 1, 6), iv.pool.Length);
            foreach (var h in iv.pool)
            {
                Assert.IsFalse(h.gameObject.activeSelf, "pool prewarmed inactive");
                Assert.AreEqual(GameLayers.Ice, h.gameObject.layer);
                Assert.IsFalse(h.agent.enabled);
                Assert.IsNotNull(h.trail);
            }

            var term = EnemyFactory.Terminal(_root, new Vector3(0f, 0f, 5f), 180f, 3, 4);
            var t = term.GetComponent<Terminal>();
            Assert.AreEqual(4, t.storyIndex);
            Assert.IsNotNull(t.screen);
            Assert.AreEqual("E  HACK", t.Prompt);
            AssertLayerCollider(term, GameLayers.Interactable, true);
            AssertLayerCollider(term, GameLayers.WallRender, false);

            var pad = EnemyFactory.HijackPad(_root, new Vector3(2f, 0f, 5f), 0f, drone, 3);
            Assert.AreSame(drone.GetComponent<HijackedDrone>(), pad.GetComponent<HijackPad>().drone);
            AssertLayerCollider(pad, GameLayers.Interactable, true);

            var gz = EnemyFactory.GlitchZone(_root, new Vector3(-4f, 0f, 0f), 2f);
            Assert.AreEqual(2f, gz.GetComponent<GlitchZone>().radius);
            Assert.IsTrue(gz.GetComponent<GlitchZone>().Contains(new Vector3(-4.5f, 0f, 1f)));
            Assert.IsFalse(gz.GetComponent<GlitchZone>().Contains(new Vector3(-4f, 0f, 2.5f)));

            var exit = EnemyFactory.ExitPortal(_root, new Vector3(0f, 0f, -6f), 0f);
            var ep = exit.GetComponent<ExitPortal>();
            Assert.IsNotNull(ep.surface);
            Assert.AreSame(ep.lockedMat, ep.surface.sharedMaterial);

            var arch = EnemyFactory.Architect(_root, new Vector3(0f, 0f, 20f));
            var a = arch.GetComponent<Architect>();
            Assert.AreEqual(EnemyFactory.FaceHeight, a.headPivot.position.y, 0.01f);
            Assert.IsTrue(a.gaze.bothLayers);
            Assert.IsFalse(a.strings.activeSelf, "strings only in SOURCE");
            Assert.AreEqual(5, a.shards.Length);

            var bb = EnemyFactory.Billboard(_root, new Vector3(5f, 2f, 20f), 270f, new Vector2(3f, 1.5f));
            var b = bb.GetComponent<Billboard>();
            Assert.IsFalse(string.IsNullOrEmpty(b.slogan));
            Assert.AreNotEqual(b.slogan, b.corrupt);
            Assert.IsFalse(b.face.activeSelf);

            Assert.AreEqual(1, Object.FindObjectsByType<TraceSystem>(FindObjectsInactive.Include).Length, "still one TraceSystem");
        }

        static void AssertLayerCollider(GameObject go, int layer, bool trigger)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                if (c.gameObject.layer == layer) { Assert.AreEqual(trigger, c.isTrigger); return; }
            Assert.Fail($"{go.name}: no collider on layer {layer}");
        }
    }
}
