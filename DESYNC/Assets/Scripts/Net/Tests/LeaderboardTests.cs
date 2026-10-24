using System;
using System.Collections.Generic;
using System.IO;
using Desync.Rules;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using EventType = Desync.Rules.EventType;

namespace Desync.Net.Tests
{
    /// The leaderboard's C# half: web/api/_test/score-cases.json (also run by `node --test` against web/api/leaderboard.ts) keeps
    /// the game's scoring and the server's port in lockstep; the request body round-trips every float exactly; board keys and
    /// seeds match the server's.
    public class LeaderboardTests
    {
        static readonly string FixturePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "web", "api", "_test", "score-cases.json"));
        static JObject fixture;
        static JObject Fixture => fixture ??= JObject.Parse(File.ReadAllText(FixturePath));

        /// The request body's room format (what the server parses) -> RoomLog.
        static RoomLog ParseRoom(JToken r)
        {
            var log = new RoomLog { id = (string)r["id"], time = (float)(double)r["time"], tDev = (float)(double)r["tDev"] };
            foreach (var m in r["mods"])
                log.mods.Add(new ScoreMods { from = (int)m[0], scoreMult = (float)(double)m[1], chainCap = (float)(double)m[2], redline = (float)(double)m[3], grazeBonus = (int)m[4] });
            foreach (var e in r["events"])
            {
                var type = (EventType)Array.IndexOf(LeaderboardScore.EventNames, (string)e[1]);
                log.events.Add(new RunEvent((float)(double)e[0], type, log.id, e[2].Type == JTokenType.Null ? null : (string)e[2], (float)(double)e[3]));
            }
            return log;
        }

        [Test]
        public void SharedCases_ScoreExactlyAsExpected()
        {
            var cases = (JArray)Fixture["cases"];
            Assert.GreaterOrEqual(cases.Count, 6);
            foreach (var c in cases)
            {
                var rooms = new List<RoomLog>();
                foreach (var r in c["rooms"]) rooms.Add(ParseRoom(r));
                var want = c["roomScores"].ToObject<int[]>();
                for (int i = 0; i < rooms.Count; i++) Assert.AreEqual(want[i], LeaderboardScore.Room(rooms[i]), (string)c["name"] + " room " + i);
                Assert.AreEqual((int)c["expected"], LeaderboardScore.Run(rooms), (string)c["name"]);
            }
        }

        [Test]
        public void RequestBody_RoundTripsEveryFloatAndScore()
        {
            foreach (var c in (JArray)Fixture["cases"])
            {
                var run = new RunSubmission { mode = "daily", board = "2026-10-24", seed = LeaderboardScore.Seed("2026-10-24"), buildHash = "abc1234", playerId = new string('a', 32) };
                foreach (var r in c["rooms"]) run.rooms.Add(ParseRoom(r));
                run.score = LeaderboardScore.Run(run.rooms);
                var body = JObject.Parse(LeaderboardScore.ToJson(run));
                Assert.AreEqual(3698842276L, (long)body["seed"]);
                Assert.AreEqual(run.score, (int)body["score"]);
                var back = new List<RoomLog>();
                foreach (var r in body["rooms"]) back.Add(ParseRoom(r));
                Assert.AreEqual(run.score, LeaderboardScore.Run(back), (string)c["name"]);
                for (int i = 0; i < back.Count; i++)
                {
                    Assert.AreEqual(run.rooms[i].time, back[i].time); Assert.AreEqual(run.rooms[i].tDev, back[i].tDev);
                    for (int k = 0; k < back[i].mods.Count; k++) Assert.AreEqual(run.rooms[i].mods[k].scoreMult, back[i].mods[k].scoreMult);
                    for (int k = 0; k < back[i].events.Count; k++)
                    {
                        Assert.AreEqual(run.rooms[i].events[k].T, back[i].events[k].T);
                        Assert.AreEqual(run.rooms[i].events[k].Type, back[i].events[k].Type);
                        Assert.AreEqual(run.rooms[i].events[k].Ice, back[i].events[k].Ice);
                    }
                }
            }
            // a float with no short decimal form survives as the exact same float
            var odd = new RunSubmission { mode = "weekly", board = "2026-W43", buildHash = "dev", playerId = "" };
            var room = new RoomLog { id = "a1n0", time = 1f / 3f, tDev = 0.1f };
            room.events.Add(new RunEvent(1f / 3f, EventType.RoomClear));
            odd.rooms.Add(room);
            var json = (JObject)JObject.Parse(LeaderboardScore.ToJson(odd))["rooms"][0];
            Assert.AreEqual(1f / 3f, (float)(double)json["time"]);
            Assert.AreEqual(0.1f, (float)(double)json["tDev"]);
            Assert.AreEqual(JTokenType.Null, json["events"][0][2].Type);
        }

        [Test]
        public void EventNames_AreTheEnumInOrder()
        {
            CollectionAssert.AreEqual(Enum.GetNames(typeof(EventType)), LeaderboardScore.EventNames);
        }

        [Test]
        public void Boards_SeedsAndIsoWeeks_MatchTheServer()
        {
            foreach (var s in (JArray)Fixture["seeds"]) Assert.AreEqual((uint)(long)s[1], LeaderboardScore.Seed((string)s[0]), (string)s[0]);
            foreach (var w in (JArray)Fixture["weeks"])
            {
                var day = DateTime.ParseExact((string)w[0], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                Assert.AreEqual((string)w[1], LeaderboardScore.Week(DateTime.SpecifyKind(day.AddHours(12), DateTimeKind.Utc)), (string)w[0]);
            }
            var t = new DateTime(2026, 10, 24, 23, 59, 59, DateTimeKind.Utc);
            Assert.AreEqual("2026-10-24", LeaderboardScore.Day(t));
            Assert.AreEqual(Rng.DailySeed(t), LeaderboardScore.Seed(LeaderboardScore.Day(t)), "a day's board seed is the daily seed");
        }

        [Test]
        public void BoardReply_ParsesTopAndRank_AndAnythingElseIsOffline()
        {
            var v = Leaderboard.ParseBoard("2026-10-24", 200,
                "{\"board\":\"2026-10-24\",\"mode\":\"daily\",\"total\":3,\"top\":[{\"rank\":1,\"tag\":\"3F9A1C\",\"score\":9000},{\"rank\":2,\"tag\":\"00AB12\",\"score\":7000}],\"you\":{\"rank\":2,\"score\":7000}}");
            Assert.IsTrue(v.online);
            Assert.AreEqual(3, v.total);
            Assert.AreEqual(2, v.top.Count);
            Assert.AreEqual("3F9A1C", v.top[0].tag); Assert.AreEqual(9000, v.top[0].score);
            Assert.AreEqual(2, v.yourRank); Assert.AreEqual(7000, v.yourScore);
            Assert.AreEqual(0, Leaderboard.ParseBoard("x", 200, "{\"total\":0,\"top\":[],\"you\":null}").yourRank);
            Assert.IsFalse(Leaderboard.ParseBoard("x", 503, "{\"error\":\"offline\"}").online);
            Assert.IsFalse(Leaderboard.ParseBoard("x", 0, "").online);
        }

        [Test]
        public void Offline_AnswersAtOnce()
        {
            var old = Leaderboard.BaseUrl;
            try
            {
                Leaderboard.BaseUrl = null;
                BoardView got = null; int status = -1;
                Leaderboard.Fetch("2026-10-24", "p", v => got = v);
                Leaderboard.Submit(new RunSubmission { mode = "daily", board = "2026-10-24", buildHash = "dev", playerId = "" }, (s, rank) => status = s);
                Assert.IsNotNull(got); Assert.IsFalse(got.online);
                Assert.AreEqual(0, status);
            }
            finally { Leaderboard.BaseUrl = old; }
        }
    }
}
