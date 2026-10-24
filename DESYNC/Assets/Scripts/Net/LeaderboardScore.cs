using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Desync.Rules;
using Newtonsoft.Json;

namespace Desync.Net
{
    /// The scoring inputs that can move inside a room (a live exploit condition changes ScoreMult); they hold from event index
    /// `from` of the room's log on.
    public struct ScoreMods
    {
        public int from;
        public float scoreMult, chainCap, redline;
        public int grazeBonus;

        public static ScoreMods Of(int from, in Modifiers m) =>
            new ScoreMods { from = from, scoreMult = m.ScoreMult, chainCap = m.ChainCap, redline = m.RedlineThreshold, grazeBonus = m.GrazeCapBonus };

        public bool SameAs(in Modifiers m) =>
            scoreMult == m.ScoreMult && chainCap == m.ChainCap && redline == m.RedlineThreshold && grazeBonus == m.GrazeCapBonus;

        public Modifiers ToModifiers()
        {
            var m = Modifiers.Default;
            m.ScoreMult = scoreMult; m.ChainCap = chainCap; m.RedlineThreshold = redline; m.GrazeCapBonus = grazeBonus;
            return m;
        }
    }

    /// One cleared room of a ranked run: its final attempt's event log (ScoreKeeper.Log), the room clock at the exit and the
    /// medal base time the results used.
    public sealed class RoomLog
    {
        public string id;
        public float time, tDev;
        public readonly List<RunEvent> events = new List<RunEvent>();
        public readonly List<ScoreMods> mods = new List<ScoreMods>();   // empty: Modifiers.Default throughout
    }

    /// What the game posts to web/api/leaderboard.ts. The server recomputes `score` from `rooms` and refuses any difference.
    public sealed class RunSubmission
    {
        public string mode, board, buildHash, playerId;   // mode "daily" | "weekly"; board "2026-10-24" | "2026-W43"
        public uint seed;                                  // Seed(board)
        public int score;
        public readonly List<RoomLog> rooms = new List<RoomLog>();
    }

    /// Scores a submission the way the server does (and the results screen did), board keys, and the request body.
    /// web/api/_test/score-cases.json holds cases both this code and the server port must reproduce.
    public static class LeaderboardScore
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// Rules.EventType names, in enum order (the wire format; a test pins it to the enum).
        public static readonly string[] EventNames =
            { "Flip", "CleanFlip", "ChainBreak", "Graze", "Redline", "Hack", "HackPerfect", "Detected", "Exit", "Death", "RoomClear", "NoTraceRoom", "PuppetString" };

        /// The room's results total: Rules.ScoreTally over its log, each event under the mods in force, + the time bonus to gold.
        public static int Room(RoomLog r)
        {
            var tally = new ScoreTally();
            var m = Modifiers.Default;
            for (int i = 0, k = 0; i < r.events.Count; i++)
            {
                while (k < r.mods.Count && r.mods[k].from <= i) m = r.mods[k++].ToModifiers();
                tally.Apply(r.events[i], m);
            }
            return tally.Total + Scoring.TimeBonus(r.time, Medals.Threshold(Medal.Gold, r.tDev));
        }

        public static int Run(IReadOnlyList<RoomLog> rooms)
        {
            int s = 0;
            for (int i = 0; i < rooms.Count; i++) s += Room(rooms[i]);
            return s;
        }

        // ---------- boards ----------

        /// "2026-10-24": the Daily board of a UTC day.
        public static string Day(DateTime utc) => Utc(utc).ToString("yyyy-MM-dd", Inv);

        /// "2026-W43": the Weekly board, ISO-8601 week of a UTC date (the week's Thursday decides the year).
        public static string Week(DateTime utc)
        {
            var d = Utc(utc).Date;
            var thursday = d.AddDays(3 - ((int)d.DayOfWeek + 6) % 7);
            return thursday.Year.ToString(Inv) + "-W" + ((thursday.DayOfYear - 1) / 7 + 1).ToString("00", Inv);
        }

        /// A board's run seed: FNV-1a of its key (for a day that is Rules.Rng.DailySeed).
        public static uint Seed(string board) => Rng.HashStr(board);

        static DateTime Utc(DateTime t) => t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : t;

        // ---------- request body ----------

        public static string ToJson(RunSubmission s)
        {
            var sw = new StringWriter(Inv);
            using (var w = new JsonTextWriter(sw))
            {
                w.WriteStartObject();
                w.WritePropertyName("mode"); w.WriteValue(s.mode);
                w.WritePropertyName("board"); w.WriteValue(s.board);
                w.WritePropertyName("seed"); w.WriteValue((long)s.seed);
                w.WritePropertyName("buildHash"); w.WriteValue(s.buildHash);
                w.WritePropertyName("playerId"); w.WriteValue(s.playerId);
                w.WritePropertyName("score"); w.WriteValue(s.score);
                w.WritePropertyName("rooms"); w.WriteStartArray();
                foreach (var r in s.rooms)
                {
                    w.WriteStartObject();
                    w.WritePropertyName("id"); w.WriteValue(r.id);
                    w.WritePropertyName("time"); Num(w, r.time);
                    w.WritePropertyName("tDev"); Num(w, r.tDev);
                    w.WritePropertyName("mods"); w.WriteStartArray();
                    foreach (var m in r.mods)
                    {
                        w.WriteStartArray();
                        w.WriteValue(m.from); Num(w, m.scoreMult); Num(w, m.chainCap); Num(w, m.redline); w.WriteValue(m.grazeBonus);
                        w.WriteEndArray();
                    }
                    w.WriteEndArray();
                    w.WritePropertyName("events"); w.WriteStartArray();
                    foreach (var e in r.events)
                    {
                        w.WriteStartArray();
                        Num(w, e.T); w.WriteValue(EventNames[(int)e.Type]); w.WriteValue(e.Ice); Num(w, e.Melt);
                        w.WriteEndArray();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            return sw.ToString();
        }

        /// A float as the exact decimal of its value (G17 of the widened double), so the server's Math.fround gets the same float.
        static void Num(JsonWriter w, float f) => w.WriteRawValue(((double)f).ToString("G17", Inv));
    }
}
