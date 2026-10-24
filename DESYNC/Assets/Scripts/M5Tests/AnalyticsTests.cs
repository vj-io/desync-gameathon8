using System.Collections.Generic;
using System.Text.RegularExpressions;
using Desync.Net;
using NUnit.Framework;

namespace Desync.M5.Tests
{
    /// Analytics routing: events go to the page sink as flat, anonymous JSON; Do Not Track stops them; the profile id only ever
    /// leaves the game hashed.
    public class AnalyticsTests
    {
        const string SaveKey = "desync.save.m5an";
        readonly List<(string name, string json)> got = new List<(string, string)>();
        System.Action<string, string> oldSink; bool oldDnt; string oldKey;

        [SetUp]
        public void SetUp()
        {
            Isolation.Begin(SaveKey);
            oldSink = Analytics.Sink; oldDnt = Analytics.DoNotTrack; oldKey = Analytics.ProviderKey;
            Analytics.DoNotTrack = false; Analytics.ProviderKey = "";
            Analytics.Clear();
            got.Clear();
            Analytics.Sink = (n, j) => got.Add((n, j));
        }

        [TearDown]
        public void TearDown()
        {
            Analytics.Sink = oldSink; Analytics.DoNotTrack = oldDnt; Analytics.ProviderKey = oldKey;
            Analytics.Clear();
            Isolation.End(SaveKey);
        }

        [Test]
        public void Sink_GetsTheNameAndFlatJson_AndTheQueueStaysEmpty()
        {
            Analytics.Track("run_start", ("mode", "Quick"), ("won", true), ("time", 12.3456f), ("rooms", 5), ("seed", 2026u));
            Assert.AreEqual(1, got.Count);
            Assert.AreEqual("run_start", got[0].name);
            StringAssert.StartsWith("{\"mode\":\"Quick\",\"won\":true,\"time\":12.346,\"rooms\":5,\"seed\":2026,\"pid\":\"", got[0].json);
            Assert.AreEqual(0, Analytics.Pending, "the page is the sink: nothing is queued");
        }

        [Test]
        public void DoNotTrack_SendsNothing()
        {
            Analytics.DoNotTrack = true;
            Analytics.Track("game_loaded");
            Assert.AreEqual(0, got.Count);
        }

        [Test]
        public void Json_IsFlat_Escaped_Bounded_AndHasOnePid()
        {
            string nasty = "a\"b\\c\nd" + new string('x', 300);
            string json = Analytics.ToJson(new (string, object)[]
            {
                ("note", nasty), ("none", null), ("nan", float.NaN), ("cause", DeathCause.Ice), ("pid", "spoofed"), ("", "no key"),
            });
            StringAssert.Contains("\"none\":null", json);
            StringAssert.Contains("\"nan\":null", json);
            StringAssert.Contains("\"cause\":\"Ice\"", json);
            StringAssert.DoesNotContain("spoofed", json);
            StringAssert.DoesNotContain("no key", json);
            StringAssert.DoesNotContain("\n", json);
            Assert.AreEqual(1, Regex.Matches(json, "\"pid\":").Count, "exactly one pid, ours");
            var note = Regex.Match(json, "\"note\":\"((?:[^\"\\\\]|\\\\.)*)\"");
            Assert.IsTrue(note.Success);
            Assert.LessOrEqual(note.Groups[1].Length, 100 + 8, "text is cut at 100 characters (plus escapes)");
        }

        [Test]
        public void PlayerId_LeavesOnlyAsAShortOneWayHash()
        {
            string raw = SaveService.Data.playerId;
            Assert.IsNotEmpty(raw);
            string h = Analytics.HashPlayerId(raw);
            Assert.IsTrue(Regex.IsMatch(h, "^[0-9a-f]{12}$"), h);
            Assert.AreEqual(h, Analytics.HashPlayerId(raw), "stable per profile");
            Assert.AreNotEqual(h, Analytics.HashPlayerId(raw + "x"));
            Assert.AreEqual("", Analytics.HashPlayerId(""));

            Analytics.Track("death", ("cause", "Ice"));
            StringAssert.Contains("\"pid\":\"" + h + "\"", got[0].json);
            StringAssert.DoesNotContain(raw, got[0].json);
        }

        [Test]
        public void WithoutASink_TheOldQueueBehaviourHolds()
        {
            Analytics.Sink = null;
            Analytics.Track("game_loaded");
            Assert.AreEqual(0, Analytics.Pending, "no provider: records nothing");
            Analytics.ProviderKey = "k";
            Analytics.Track("room_end", ("room", "r1"));
            Assert.AreEqual(1, Analytics.Pending);
            Assert.IsTrue(Analytics.TryDequeue(out var e));
            Assert.AreEqual("room_end", e.Name);
        }
    }
}
