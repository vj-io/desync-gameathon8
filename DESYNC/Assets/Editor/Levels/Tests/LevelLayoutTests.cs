using NUnit.Framework;

namespace Desync.Levels.Tests
{
    /// Every room grid is well-formed and beatable within the melt budget, with and without being seen.
    public class LevelLayoutTests
    {
        static readonly int[] StoryStart = { 0, 1, 2, 4, 6, 8 };
        static readonly int[] Terminals = { 1, 1, 2, 2, 2, 3 };

        [Test]
        public void RoomsLintAndValidate()
        {
            Assert.AreEqual(6, RoomLayouts.Rooms.Length);
            foreach (var room in RoomLayouts.Rooms)
            {
                Assert.IsEmpty(room.Lint(), room.name);
                Assert.AreEqual(StoryStart[room.index], room.storyStart, room.name);
                Assert.AreEqual(Terminals[room.index], room.Count(Legend.Terminal), room.name);
                var v = GridValidator.Validate(room);
                Assert.IsTrue(v.ok && v.ghostOk, $"{room.name}: {v}");
                Assert.Less(v.peakMelt, 100f, room.name);
            }
            Assert.IsFalse(GridValidator.Validate(RoomLayouts.Rooms[0]).renderOnly, "Room 1 must force a flip");
            Assert.AreEqual(1, RoomLayouts.Rooms[3].Count(Legend.HijackPad), "Room 4 hijack pad");
            Assert.AreEqual(1, RoomLayouts.Rooms[4].Count(Legend.HijackPad), "Room 5 hijack pad (PRD 5.4: hijack R4+)");
            Assert.IsEmpty(RoomLayouts.Title.Lint());
            Assert.IsEmpty(RoomLayouts.Ending.Lint());
        }
    }
}
