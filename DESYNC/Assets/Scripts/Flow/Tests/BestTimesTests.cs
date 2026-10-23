using NUnit.Framework;
using UnityEngine;

namespace Desync.Flow.Tests
{
    public class BestTimesTests
    {
        const int Room = 5;
        string kt = "bestT" + Room, kg = "bestG" + Room;
        bool had; float oldT; int oldG;

        [SetUp]
        public void Save()
        {
            had = PlayerPrefs.HasKey(kg); oldT = PlayerPrefs.GetFloat(kt); oldG = PlayerPrefs.GetInt(kg);
            PlayerPrefs.DeleteKey(kt); PlayerPrefs.DeleteKey(kg);
        }

        [TearDown]
        public void Restore()
        {
            PlayerPrefs.DeleteKey(kt); PlayerPrefs.DeleteKey(kg);
            if (had) { PlayerPrefs.SetFloat(kt, oldT); PlayerPrefs.SetInt(kg, oldG); }
            PlayerPrefs.Save();   // BestTimes.Record saves to disk, so the cleanup must too or the test value outlives the run
        }

        static RoomResult R(char grade, float time) => new RoomResult { room = Room, time = time, grade = grade };

        [Test]
        public void BetterGradeWins_ThenFasterTime_AndPersistsBoth()
        {
            Assert.IsTrue(BestTimes.Record(R('B', 50f)), "first clear is always a best");
            Assert.AreEqual(50f, PlayerPrefs.GetFloat(kt), 1e-4f);
            Assert.AreEqual((int)'B', PlayerPrefs.GetInt(kg));
            Assert.IsFalse(BestTimes.Record(R('B', 50f)), "equal does not replace");
            Assert.IsFalse(BestTimes.Record(R('C', 10f)), "faster but lower grade does not replace");
            Assert.IsTrue(BestTimes.Record(R('B', 49.9f)), "same grade, faster replaces");
            Assert.IsTrue(BestTimes.Record(R('S', 80f)), "higher grade replaces even when slower");
            Assert.IsFalse(BestTimes.Record(R('A', 5f)), "S is above A (char order would say otherwise)");
            Assert.AreEqual(80f, PlayerPrefs.GetFloat(kt), 1e-4f);
            Assert.AreEqual((int)'S', PlayerPrefs.GetInt(kg));
        }

        [Test]
        public void IgnoresJunkResults()
        {
            Assert.IsFalse(BestTimes.Record(new RoomResult { room = 9, time = 5f, grade = 'S' }));
            Assert.IsFalse(BestTimes.Record(R('\0', 5f)));
            Assert.IsFalse(BestTimes.Record(R('S', 0f)));
            Assert.IsFalse(PlayerPrefs.HasKey(kg));
        }
    }
}
