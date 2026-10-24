using System;
using System.Text.RegularExpressions;
using Desync.Rules;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Desync.Net.Tests
{
    public class SaveServiceTests
    {
        string key;
        double now;
        Func<double> oldClock;
        readonly float[] oldT = new float[SceneFlow.RoomCount];
        readonly int[] oldG = new int[SceneFlow.RoomCount];

        [SetUp]
        public void SetUp()
        {
            key = "desync.test.save." + Guid.NewGuid().ToString("N");   // never the real profile
            now = 100;
            oldClock = SaveService.Clock;
            SaveService.Clock = () => now;
            SaveService.Unload(key);
            // The legacy keys are real PlayerPrefs: park them so a developer's own bests can't leak into (or be eaten by) a test.
            for (int i = 0; i < SceneFlow.RoomCount; i++)
            {
                oldT[i] = PlayerPrefs.GetFloat("bestT" + i, 0f); oldG[i] = PlayerPrefs.GetInt("bestG" + i, 0);
                PlayerPrefs.DeleteKey("bestT" + i); PlayerPrefs.DeleteKey("bestG" + i);
            }
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(key); PlayerPrefs.DeleteKey(key + ".backup");
            for (int i = 0; i < SceneFlow.RoomCount; i++)
            {
                PlayerPrefs.DeleteKey("bestT" + i); PlayerPrefs.DeleteKey("bestG" + i);
                if (oldG[i] != 0) { PlayerPrefs.SetFloat("bestT" + i, oldT[i]); PlayerPrefs.SetInt("bestG" + i, oldG[i]); }
            }
            PlayerPrefs.Save();
            SaveService.Clock = oldClock;
            SaveService.Unload();
        }

        static RoomReport Report(int room, float time, int total, char grade, Medal medal) =>
            new RoomReport { room = room, time = time, total = total, grade = grade, medal = medal };

        JObject Stored() => JObject.Parse(PlayerPrefs.GetString(key));

        [Test]
        public void RoundTrip_RecordedRoomSurvivesReload_AndPlayerIdIsStable()
        {
            string id = SaveService.Data.playerId;
            Assert.IsNotEmpty(id);
            SaveService.RecordRoom(Report(0, 41.27f, 4200, 'A', Medal.Gold), "r1");
            SaveService.Flush();

            SaveService.Unload(key);
            var b = SaveService.GetBest("r1");
            Assert.AreEqual(41.27f, b.time, 1e-4f);
            Assert.AreEqual(4200, b.score);
            Assert.AreEqual("A", b.grade);
            Assert.AreEqual("G", SaveService.Data.medals["r1"]);
            Assert.AreEqual(id, SaveService.Data.playerId);
            Assert.AreEqual(3, SaveService.Data.version);
        }

        [Test]
        public void RecordRoom_KeepsTheBetterOfEachRecord()
        {
            SaveService.RecordRoom(Report(1, 50f, 3000, 'A', Medal.Gold), "r2");
            SaveService.RecordRoom(Report(1, 60f, 3500, 'B', Medal.Silver), "r2");   // slower, higher score, lower grade, lower medal
            var b = SaveService.GetBest("r2");
            Assert.AreEqual(50f, b.time, 1e-4f, "best time stays");
            Assert.AreEqual(3500, b.score, "best score rises on its own");
            Assert.AreEqual("A", b.grade, "A is above B");
            Assert.AreEqual("G", SaveService.Data.medals["r2"], "gold is not downgraded to silver");

            SaveService.RecordRoom(Report(1, 44f, 100, 'S', Medal.Sync), "r2");
            Assert.AreEqual(44f, SaveService.GetBest("r2").time, 1e-4f);
            Assert.AreEqual("S", SaveService.GetBest("r2").grade);
            Assert.AreEqual("SYNC", SaveService.Data.medals["r2"]);
        }

        [Test]
        public void RecordRoom_IgnoresJunk_AndTotalsCount()
        {
            SaveService.RecordRoom(null, "r1");
            SaveService.RecordRoom(Report(0, 0f, 100, 'S', Medal.Gold), "r1");   // no clear time
            SaveService.RecordRoom(Report(0, 10f, 100, 'S', Medal.Gold), "");
            Assert.IsNull(SaveService.GetBest("r1"));
            Assert.AreEqual(0, SaveService.MedalCount());

            SaveService.RecordRoom(Report(0, 30f, 100, 'S', Medal.Bronze), "r1");
            SaveService.RecordRoom(Report(1, 45.5f, 100, 'B', Medal.None), "r2");
            Assert.AreEqual(1, SaveService.MedalCount(), "None earns no medal");
            Assert.AreEqual(75.5f, SaveService.TotalBestTime(), 1e-4f);
        }

        [Test]
        public void CorruptSave_FallsBackToDefaults_AndKeepsTheRawText()
        {
            PlayerPrefs.SetString(key, "{not json");
            Assert.AreEqual(3, SaveService.Data.version);
            Assert.AreEqual(0, SaveService.Data.best.Count);
            Assert.AreEqual("{not json", PlayerPrefs.GetString(key + ".backup"));
            Assert.DoesNotThrow(() => Stored(), "the broken text was replaced by a valid save");
        }

        [Test]
        public void ValidJsonWithoutVersion_IsBackedUpToo()
        {
            PlayerPrefs.SetString(key, "{\"hello\":1}");
            Assert.AreEqual(0, SaveService.Data.best.Count);
            Assert.AreEqual("{\"hello\":1}", PlayerPrefs.GetString(key + ".backup"));
        }

        [Test]
        public void NewerSave_IsNeverOverwritten()
        {
            const string newer = "{\"version\":99,\"x\":1}";
            PlayerPrefs.SetString(key, newer);
            SaveService.RecordRoom(Report(0, 30f, 100, 'S', Medal.Gold), "r1");
            SaveService.Flush();
            Assert.AreEqual(newer, PlayerPrefs.GetString(key));
            Assert.IsFalse(PlayerPrefs.HasKey(key + ".backup"));
        }

        [Test]
        public void Save_IsThrottled_AndTickWritesThePostponedOne()
        {
            var d = SaveService.Data;   // the first load writes the fresh profile at t=100
            now = 110;
            Assert.IsTrue(SaveService.Save(), "outside the window: immediate");
            d.stats.runs = 7;
            now = 111;
            Assert.IsFalse(SaveService.Save(), "inside the 2 s window");
            Assert.AreEqual(0, (int)Stored()["stats"]["runs"]);
            SaveService.Tick();
            Assert.AreEqual(0, (int)Stored()["stats"]["runs"], "still too early");
            now = 112.01;
            SaveService.Tick();
            Assert.AreEqual(7, (int)Stored()["stats"]["runs"], "the trailing write lands once the window has passed");
        }

        [Test]
        public void Flush_IgnoresTheThrottle()
        {
            SaveService.Data.stats.deaths = 3;
            Assert.IsFalse(SaveService.Save(), "the load just wrote, so this one waits");
            SaveService.Flush();
            Assert.AreEqual(3, (int)Stored()["stats"]["deaths"]);
        }

        [Test]
        public void LegacyBests_AreImportedOnce_AndTheOldKeysStay()
        {
            PlayerPrefs.SetFloat("bestT0", 42.5f); PlayerPrefs.SetInt("bestG0", 'A');
            PlayerPrefs.SetFloat("bestT2", 70f); PlayerPrefs.SetInt("bestG2", 'B');
            var b1 = SaveService.GetBest("r1");
            Assert.AreEqual(42.5f, b1.time, 1e-4f);
            Assert.AreEqual("A", b1.grade);
            Assert.AreEqual(70f, SaveService.GetBest("r3").time, 1e-4f);
            Assert.IsNull(SaveService.GetBest("r2"));
            Assert.IsTrue(PlayerPrefs.HasKey("bestG0"), "UI still reads the old keys");

            SaveService.Flush();
            PlayerPrefs.SetFloat("bestT0", 1f);   // a later change to the old keys must not be re-imported
            SaveService.Unload(key);
            Assert.AreEqual(42.5f, SaveService.GetBest("r1").time, 1e-4f);
        }
    }

    public class NetHostTests
    {
        string key;
        bool god;

        [SetUp]
        public void SetUp()
        {
            key = "desync.test.host." + Guid.NewGuid().ToString("N");
            SaveService.Unload(key);
            god = GameState.God;
            GameState.God = false;
        }

        [TearDown]
        public void TearDown()
        {
            GameState.God = god;
            PlayerPrefs.DeleteKey(key); PlayerPrefs.DeleteKey(key + ".backup");
            SaveService.Unload();
        }

        [Test]
        public void Events_UpdateStats()
        {
            var s = SaveService.Data.stats;
            int runs = s.runs, deaths = s.deaths, flips = s.flips, wins = s.wins;
            NetHost.OnStart(0); NetHost.OnFlipped(true); NetHost.OnFlipped(false);
            NetHost.OnKilled(DeathCause.Ice); NetHost.OnCompleted();
            Assert.AreEqual(runs + 1, s.runs);
            Assert.AreEqual(flips + 2, s.flips);
            Assert.AreEqual(deaths + 1, s.deaths);
            Assert.AreEqual(wins + 1, s.wins);
            GameState.God = true;
            NetHost.OnKilled(DeathCause.Ice);
            Assert.AreEqual(deaths + 1, s.deaths, "god mode deaths do not count");
        }

        [Test]
        public void RoomReport_IsRecordedUnderRoomIdFromIndex()
        {
            NetHost.OnReport(new RoomReport { room = 2, time = 61.5f, total = 900, grade = 'B', medal = Medal.Silver });
            Assert.AreEqual(61.5f, SaveService.GetBest("r3").time, 1e-4f);
            Assert.AreEqual("S", SaveService.Data.medals["r3"]);
        }

        [Test]
        public void Copy_FillsTheClipboardAndPopsCopied()
        {
            string popped = null;
            Action<string, Color> h = (t, c) => popped = t;
            GameEvents.PopText += h;
            try
            {
                NetHost.OnCopy("DESYNC // test");
                Assert.AreEqual("DESYNC // test", GUIUtility.systemCopyBuffer);
                Assert.AreEqual("COPIED", popped);
                popped = null;
                NetHost.OnCopy("");
                Assert.IsNull(popped, "empty text copies nothing");
            }
            finally { GameEvents.PopText -= h; }
        }
    }

    public class AnalyticsTests
    {
        string key; bool dnt;

        [SetUp] public void SetUp() { key = Analytics.ProviderKey; dnt = Analytics.DoNotTrack; Analytics.Clear(); }
        [TearDown] public void TearDown() { Analytics.ProviderKey = key; Analytics.DoNotTrack = dnt; Analytics.Clear(); }

        [Test]
        public void NoProvider_RecordsNothing()
        {
            Analytics.ProviderKey = "";
            Analytics.Track("game_loaded");
            Assert.AreEqual(0, Analytics.Pending);
        }

        [Test]
        public void DoNotTrack_RecordsNothing_EvenWithAProvider()
        {
            Analytics.ProviderKey = "k"; Analytics.DoNotTrack = true;
            Analytics.Track("room_start", ("room", "r1"));
            Assert.AreEqual(0, Analytics.Pending);
        }

        [Test]
        public void WithProvider_QueuesInOrder_AndIsBounded()
        {
            Analytics.ProviderKey = "k"; Analytics.DoNotTrack = false;
            Analytics.Track("room_start", ("room", "r1"), ("seed", 2026u));
            Analytics.Track("room_end");
            Assert.IsTrue(Analytics.TryDequeue(out var e));
            Assert.AreEqual("room_start", e.Name);
            Assert.AreEqual(("seed", (object)2026u), e.Props[1]);
            Assert.IsTrue(e.UtcMs > 0);
            Assert.AreEqual("room_end", Analytics.TryDequeue(out var e2) ? e2.Name : null);
            Assert.IsFalse(Analytics.TryDequeue(out _));

            for (int i = 0; i < 1000; i++) Analytics.Track("restart");
            Assert.AreEqual(256, Analytics.Pending);
        }
    }

    public class DiagnosticsTests
    {
        [Test]
        public void Stamp_CarriesSeedBuildAndSaveVersion()
        {
            Assert.AreEqual("seed=2026 build=5de1e19 save=3", Diagnostics.Stamp(2026, "5de1e19", 3));
            uint old = GameState.Seed;
            GameState.Seed = 77;
            try { StringAssert.IsMatch(@"^seed=77 build=\S+ save=3$", Diagnostics.Stamp()); }
            finally { GameState.Seed = old; }
        }

        [Test]
        public void BuildInfo_ParsesShaAndTime_AndFallsBackToDev()
        {
            BuildInfo.Parse("5de1e19 2026-10-24T10:00:00Z\n", out var sha, out var utc);
            Assert.AreEqual("5de1e19", sha);
            Assert.AreEqual("2026-10-24T10:00:00Z", utc);
            BuildInfo.Parse(null, out sha, out utc);
            Assert.AreEqual("dev", sha);
            Assert.AreEqual("", utc);
            Assert.IsNotEmpty(BuildInfo.Hash);
        }
    }
}
