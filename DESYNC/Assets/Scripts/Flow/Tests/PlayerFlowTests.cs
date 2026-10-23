using Desync.Player;
using NUnit.Framework;
using UnityEngine;

namespace Desync.Flow.Tests
{
    public class PlayerFlowTests
    {
        [Test]
        public void GradingFormula()
        {
            Assert.AreEqual(100f, RoomManager.Points(20f, 0, 2, 25f, 2), 1e-4f);    // under par, no penalties
            Assert.AreEqual(75f, RoomManager.Points(25f, 1, 2, 25f, 2), 1e-4f);     // one TRACE
            Assert.AreEqual(90f, RoomManager.Points(25f, 0, 4, 25f, 2), 1e-4f);     // two flips over par
            Assert.AreEqual(75f, RoomManager.Points(37.5f, 0, 2, 25f, 2), 1e-4f);   // 50 % over par time
            Assert.AreEqual('S', RoomManager.Grade(20f, 0, 2, 25f, 2));
            Assert.AreEqual('A', RoomManager.Grade(25f, 1, 2, 25f, 2));
            Assert.AreEqual('B', RoomManager.Grade(25f, 2, 2, 25f, 2));
            Assert.AreEqual('C', RoomManager.Grade(60f, 3, 9, 25f, 2));
        }

        [Test]
        public void MeltMath()
        {
            Assert.AreEqual(12f, MeltMeter.Step(0f, true, 1f, 12f, 4f), 1e-4f);
            Assert.AreEqual(2f, MeltMeter.Step(10f, false, 2f, 12f, 4f), 1e-4f);
            Assert.AreEqual(0f, MeltMeter.Step(5f, false, 2f, 12f, 4f), 1e-4f);     // clamps at 0
            Assert.AreEqual(100f, MeltMeter.Step(95f, true, 1f, 12f, 4f), 1e-4f);   // clamps at 100
        }

        [Test]
        public void FactoryBuildsWiredPlayer_AndReturnFlipIsBlockedInsideWall()
        {
            var cfg = GameConfig.I;
            var player = PlayerFactory.Create(null, new Vector3(100f, 0f, 100f), 90f);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                Assert.AreEqual(GameLayers.Player, player.layer);
                var cc = player.GetComponent<CharacterController>();
                Assert.AreEqual(cfg.playerHeight, cc.height, 1e-4f);
                var fpc = player.GetComponent<FirstPersonController>();
                Assert.IsNotNull(fpc.head);
                Assert.IsNotNull(fpc.chest);
                Assert.AreEqual(cfg.fov, fpc.cam.fieldOfView, 1e-4f);
                Assert.IsNotNull(fpc.cam.GetComponent<AudioListener>());
                Assert.IsNotNull(fpc.cam.GetComponent<JuiceUtil>());
                Assert.IsNotNull(player.GetComponent<MeltMeter>());
                Assert.IsNotNull(player.GetComponent<Interactor>());
                Assert.AreEqual(90f, player.transform.eulerAngles.y, 1e-3f);

                var flip = player.GetComponent<FlipController>();
                wall.layer = GameLayers.WallRender;
                wall.transform.position = new Vector3(100f, 1f, 100f);
                Physics.SyncTransforms();
                Assert.IsTrue(flip.Blocked(), "capsule inside a WallRender cube must block the return flip");
                wall.transform.position = new Vector3(100f, 1f, 105f);
                Physics.SyncTransforms();
                Assert.IsFalse(flip.Blocked(), "clear of walls must allow the return flip");
            }
            finally
            {
                Object.DestroyImmediate(wall);
                Object.DestroyImmediate(player);
            }
        }
    }
}
