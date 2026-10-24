using System.Linq;
using Desync.Gen;
using NUnit.Framework;

namespace Desync.Flow.Tests
{
    /// Three-act structure: a boss arena per act (RunBosses) and where Auditor Drones patrol (RunRules.AuditorDrones).
    public class RunActsTests
    {
        static RunNode Node(NodeType type, int act, int index) =>
            new RunNode { id = "a" + act + "n" + index, type = type, act = act, index = index, roomSeed = 1234u + (uint)act };

        [Test]
        public void Campaign_IsThreeActs_WithABossPerAct()
        {
            Assert.AreEqual(3, RunRules.CampaignActs);
            CollectionAssert.AreEqual(new[] { "auditor", "compiler", "architect" }, Enumerable.Range(1, 3).Select(RunUnlocks.Boss));
            for (int act = 1; act <= 3; act++)
            {
                var node = Node(NodeType.Boss, act, 4);
                var arena = RunBosses.For(node);
                Assert.IsNotNull(arena, "act " + act);
                string expect = RunBosses.Factories.ContainsKey(RunUnlocks.Boss(act)) ? RunUnlocks.Boss(act) : RunBosses.Fallback;
                Assert.AreEqual(expect, arena.boss);
                Assert.AreEqual(node.roomSeed, arena.spec.seed, "seeded with the node");
                Assert.AreEqual(act, arena.spec.act);
                Assert.AreEqual(arena.spec.entities.Count(e => e.kind == EntityKind.Terminal), arena.terminals);
                Assert.Greater(arena.terminals, 0, "the arena's hacks open its exit");
                Assert.AreEqual(1, arena.spec.entities.Count(e => e.kind == EntityKind.Spawn));
                Assert.AreEqual(1, arena.spec.entities.Count(e => e.kind == EntityKind.Exit));
            }
        }

        [Test]
        public void AuditorDrones_FromAct2ThirdRoom_AndAllOfAct3_GeneratedRoomsOnly()
        {
            var room = new RoomSpec();
            var fallback = new RoomSpec { fallbackRoom = "Room_03" };
            for (int i = 0; i < 4; i++)
            {
                Assert.IsFalse(RunRules.AuditorDrones(Node(NodeType.Infil, 1, i), room), "act 1 room " + i);
                Assert.AreEqual(i >= 2, RunRules.AuditorDrones(Node(NodeType.Infil, 2, i), room), "act 2 room " + i);
                Assert.IsTrue(RunRules.AuditorDrones(Node(NodeType.Elite, 3, i), room), "act 3 room " + i);
            }
            Assert.IsFalse(RunRules.AuditorDrones(Node(NodeType.Boss, 3, 4), room), "boss arenas");
            Assert.IsFalse(RunRules.AuditorDrones(Node(NodeType.Infil, 3, 1), fallback), "original rooms keep their tuning");
        }
    }
}
