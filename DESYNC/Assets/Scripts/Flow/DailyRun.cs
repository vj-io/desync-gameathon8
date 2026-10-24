using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Desync.Content;
using Desync.Gen;
using Desync.Net;
using Desync.Rules;
using UnityEngine;

namespace Desync.Flow
{
    /// Daily Breach and the Weekly Ladder (spec "Story, modes").
    /// DAILY: an act 1 run (Quick Breach shape) on the UTC day's shared seed, every room under the day's fixed mutator pair
    /// (RunManager asks Mutators), Zero-Day disabled. One SCORED attempt per UTC day: the first daily run started that day; later
    /// ones are practice (nothing saved or posted). The scored run's total goes to SaveV3.dailyBest[day] and its Wordle-style
    /// share block to settings "daily_share".
    /// WEEKLY LADDER: a full three-act CAMPAIGN on the ISO week's seed, open after the first AUDITOR kill; best score per week in
    /// settings "daily_week_yyyy-ww". Started with StartWeekly only (the seed alone does not make a campaign a ladder run).
    /// While a ranked run plays, every cleared room's final attempt is kept (ScoreKeeper.Log, the scoring Modifiers in force per
    /// event, room time, medal time) and posted at the run's end to web/api/leaderboard.ts, which recomputes the score.
    /// Self-bootstraps; RunManager calls Mutators (room setup) and RoomCleared (room results).
    [DefaultExecutionOrder(-105)]   // after RunManager (-110) writes this frame's live Modifiers, before anything scores with them
    public sealed class DailyRun : MonoBehaviour
    {
        public enum Board { None, Daily, Weekly }

        public const string ScoredKey = "daily_scored";   // settings: the UTC day whose scored attempt is spent
        public const string ShareKey = "daily_share";     // settings: that attempt's share block
        public const string WeekPrefix = "daily_week_";   // settings: best Weekly Ladder score, "daily_week_2026-43"

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        // Wordle squares by Rules.Medal: none, bronze, silver, gold, SYNC
        public static readonly string[] MedalGlyphs = { "⬛", "\U0001F7EB", "⬜", "\U0001F7E8", "\U0001F7EA" };

        public static DailyRun I;

        /// The run being recorded; None outside Daily / Weekly Ladder runs.
        public static Board Current { get; private set; }
        /// Daily: the scored attempt (false = practice). Weekly: always.
        public static bool Ranked { get; private set; }
        /// "2026-10-24" / "2026-W43" of the run being recorded.
        public static string BoardKey { get; private set; }
        /// The last ranked run's submission: -1 none / in flight, AssistRun (not posted), else the HTTP status (0 = offline).
        public static int SubmitStatus { get; private set; } = -1;
        public const int AssistRun = -2;
        public static int SubmitRank { get; private set; }
        /// The latest Daily run's share block (scored or practice), complete from its last cleared room on: what the run results
        /// should show and copy for a Daily instead of the run share line.
        public static string LastShare { get; private set; }
        /// A recorded run ended or a submission answered (the Daily screen repaints).
        public static Action Changed;
        /// Starts a seeded run. Default: RunManager.Begin. Tests swap in a stub.
        public static Action<RunMode, uint> BeginRun = (mode, seed) => RunManager.I.Begin(mode, seed);

        static bool weeklyArmed;   // StartWeekly -> the run's first map
        RunGraph tracked;          // the run being recorded (a fresh graph per run)
        readonly List<RoomLog> rooms = new List<RoomLog>();
        readonly List<ScoreMods> mods = new List<ScoreMods>(8);   // this attempt's scoring mods, by first event index
        readonly List<Medal> medals = new List<Medal>();
        readonly List<char> grades = new List<char>();
        bool unverified;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I) return;
            var go = new GameObject("DailyRun");
            DontDestroyOnLoad(go);
            I = go.AddComponent<DailyRun>();
        }

        void OnEnable()
        {
            RunEvents.PhaseChanged += OnPhase;
            GameEvents.RoomStarted += OnRoomStarted;
            GameEvents.RoomReset += OnAttempt;
        }

        void OnDisable()
        {
            RunEvents.PhaseChanged -= OnPhase;
            GameEvents.RoomStarted -= OnRoomStarted;
            GameEvents.RoomReset -= OnAttempt;
        }

        // ---------- rules (pure) ----------

        /// The day's two run mutators, one per tag, eligible from node 1 (the Elite rule of RunRules.RoomMutators).
        public static List<Mutator> Mutators(uint seed, Catalog cat) =>
            RunRules.RoomMutators(Rng.Substream(seed, "daily"), cat, new RunNode { type = NodeType.Elite, index = 0 });

        public static bool WeeklyOpen(SaveV3 save) => RunUnlocks.Killed(save, 1);

        /// The scored attempt of `day` is spent.
        public static bool Scored(SaveV3 save, string day) => save.settings.TryGetValue(ScoredKey, out var d) && d == day;

        /// "daily_week_2026-43" for board "2026-W43".
        public static string WeekSaveKey(string week) => WeekPrefix + week.Replace("-W", "-");

        public static int WeekBest(SaveV3 save, string week) =>
            save.settings.TryGetValue(WeekSaveKey(week), out var v) && int.TryParse(v, NumberStyles.Integer, Inv, out int b) ? b : 0;

        /// DESYNC DAILY // 2026-10-24, one medal square + grade per room, then total and run time.
        public static string ShareBlock(string day, bool practice, IReadOnlyList<Medal> medals, IReadOnlyList<char> grades, int total, float runTime)
        {
            var sb = new StringBuilder(160);
            sb.Append("DESYNC DAILY // ").Append(day);
            if (practice) sb.Append(" // PRACTICE");
            sb.Append('\n');
            for (int i = 0; i < grades.Count; i++)
            {
                int m = (int)medals[i];
                sb.Append(MedalGlyphs[m >= 0 && m < MedalGlyphs.Length ? m : 0]).Append(' ').Append(grades[i]).Append('\n');
            }
            sb.Append(total.ToString(Inv)).Append(" // ").Append(RoomReports.Clock(runTime)).Append('\n').Append("desync-nine.vercel.app");
            return sb.ToString();
        }

        // ---------- starting ----------

        /// Today's Daily Breach (scored when it is the day's first). The run manager seeds it with the UTC day.
        public static void StartDaily() => RunEvents.StartRun?.Invoke(RunMode.Daily);

        /// This week's ladder run; false (nothing starts) while the first AUDITOR kill is missing.
        public static bool StartWeekly()
        {
            if (!WeeklyOpen(SaveService.Data)) return false;
            weeklyArmed = true;
            BeginRun(RunMode.Campaign, LeaderboardScore.Seed(LeaderboardScore.Week(DateTime.UtcNow)));
            return true;
        }

        void OnPhase(RunPhase p)
        {
            if (p == RunPhase.Map && RunState.Act == 1 && RunState.NodeId == null && !ReferenceEquals(tracked, RunState.Graph)) Track();
            else if ((p == RunPhase.Over || p == RunPhase.None) && Current != Board.None) Finish();
        }

        /// A run just reached its first map: record it when it is a Daily or an armed Weekly Ladder run.
        void Track()
        {
            bool armed = weeklyArmed, weekly = false;
            weeklyArmed = false;
            Current = Board.None;
            if (RunState.Mode == RunMode.Daily) BoardKey = MatchBoard(LeaderboardScore.Day, TimeSpan.FromDays(1));
            else if (armed && RunState.Mode == RunMode.Campaign && DeepRun.Level == 0) { weekly = true; BoardKey = MatchBoard(LeaderboardScore.Week, TimeSpan.FromDays(7)); }
            else return;
            if (BoardKey == null) return;   // a seed that is not this (or the last) day / week: an ordinary run
            tracked = RunState.Graph;
            Current = weekly ? Board.Weekly : Board.Daily;
            var save = SaveService.Data;
            Ranked = weekly || !Scored(save, BoardKey);
            if (!weekly && Ranked) { save.settings[ScoredKey] = BoardKey; SaveService.Save(); }   // spent at the start: quitting does not refund it
            rooms.Clear(); medals.Clear(); grades.Clear(); mods.Clear();
            unverified = false;
            SubmitStatus = -1; SubmitRank = 0;
            Changed?.Invoke();
        }

        /// The board (now, or one period back for a run started just before the change) whose seed is the run's.
        static string MatchBoard(Func<DateTime, string> key, TimeSpan period)
        {
            var now = DateTime.UtcNow;
            foreach (var t in new[] { now, now - period })
                if (LeaderboardScore.Seed(key(t)) == RunState.Seed) return key(t);
            return null;
        }

        // ---------- recording ----------

        void OnRoomStarted(int room) => OnAttempt();

        /// A fresh attempt (room start, Backup respawn): ScoreKeeper's log starts over, so do the mods.
        void OnAttempt()
        {
            if (Current == Board.None) return;
            mods.Clear();
            Sample();
        }

        void Update() { if (Current != Board.None && RunManager.I && RunManager.I.RoomLive) Sample(); }

        /// The scoring mods in force from the next logged event on (RunManager re-folds conditional exploits in its Update).
        void Sample()
        {
            if (!ScoreKeeper.I) return;
            var m = GameState.Mods;
            int n = ScoreKeeper.I.Log.Count;
            if (mods.Count > 0 && mods[mods.Count - 1].SameAs(m)) return;
            if (mods.Count > 0 && mods[mods.Count - 1].from == n) mods[mods.Count - 1] = ScoreMods.Of(n, m);
            else mods.Add(ScoreMods.Of(n, m));
        }

        /// RunManager, as a run room's results are built: keeps the room for the leaderboard. Daily: returns the share block so
        /// far (the results' [C] copies it); null otherwise (the run's own share text stays).
        public static string RoomCleared(RoomReport r) => I && Current != Board.None ? I.Keep(r) : null;

        string Keep(RoomReport r)
        {
            var log = new RoomLog { id = RunState.NodeId, time = r.time, tDev = GameState.RoomTDev };
            if (ScoreKeeper.I) log.events.AddRange(ScoreKeeper.I.Log);
            log.mods.AddRange(mods);
            int replay = LeaderboardScore.Room(log);
            if (replay != r.total)
            {
                unverified = true;   // the server would refuse it: say why here rather than post a run it rejects
                Debug.LogWarning("[DESYNC] " + BoardKey + " room " + log.id + ": the log replays to " + replay + ", the results said " + r.total + " (" + Diagnostics.Stamp() + ")");
            }
            rooms.Add(log);
            medals.Add(r.medal);
            grades.Add(r.grade);
            if (Current != Board.Daily) return null;
            return LastShare = ShareBlock(BoardKey, !Ranked, medals, grades, RunState.RunScore, RunState.RunTime);
        }

        /// Run results or quit: save the ranked result, post it, stop recording.
        void Finish()
        {
            var save = SaveService.Data;
            int score = RunState.RunScore;
            if (Current == Board.Daily) LastShare = ShareBlock(BoardKey, !Ranked, medals, grades, score, RunState.RunTime);
            if (Current == Board.Daily && Ranked)
            {
                save.dailyBest[BoardKey] = score;
                save.settings[ShareKey] = LastShare;
            }
            else if (Current == Board.Weekly && score > WeekBest(save, BoardKey)) save.settings[WeekSaveKey(BoardKey)] = score.ToString(Inv);
            if (Ranked) SaveService.Save();
            if (Ranked && rooms.Count > 0) Submit(score);
            Current = Board.None;
            tracked = null;
            Changed?.Invoke();
        }

        void Submit(int score)
        {
            var run = new RunSubmission
            {
                mode = Current == Board.Daily ? "daily" : "weekly", board = BoardKey, seed = RunState.Seed,
                buildHash = BuildInfo.Hash, playerId = SaveService.Data.playerId, score = score,
            };
            run.rooms.AddRange(rooms);
            if (Assist.Flagged) { SubmitStatus = AssistRun; return; }   // Assist keeps every record but never reaches a leaderboard
            if (unverified || LeaderboardScore.Run(run.rooms) != score) { SubmitStatus = 422; return; }
            Leaderboard.Submit(run, (status, rank) =>
            {
                SubmitStatus = status;
                SubmitRank = rank;
                Changed?.Invoke();
            });
        }
    }
}
