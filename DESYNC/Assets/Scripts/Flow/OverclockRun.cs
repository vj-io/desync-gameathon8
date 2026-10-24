using System.Globalization;
using Desync.Gen;
using Desync.Net;
using Desync.Rules;
using UnityEngine;

namespace Desync.Flow
{
    /// Endless Overclock, the true ending's reward: generated rooms back to back, no map, draft or market. Every RoomsPerStep
    /// rooms the act moves on (1, 2, 3, 1, ...) and the heat rises by HeatPerStep (the generator's budget grows by heat / 2);
    /// melt carries over in full; the score multiplier grows by ScoreStep per room; a death ends the run (no Backups).
    /// RunManager plays it as RunMode.Overclock through the hooks that call in here. The best run (rooms cleared, then score)
    /// is SaveV3.settings[BestKey] = "<rooms>,<score>".
    public static class OverclockRun
    {
        public const int RoomsPerStep = 3, HeatPerStep = 2;
        public const float ScoreStep = 0.1f;
        public const string UnlockFlag = "ending_desync", BestKey = "overclock_best";

        /// Testing: opens the mode without the true ending.
        public static bool DebugUnlock;

        /// 0-based index of the room being played.
        public static int Room { get; private set; }

        public static bool Unlocked(SaveV3 save) => DebugUnlock || save.unlocks.Contains(UnlockFlag);

        /// Title menu entry. Starts a run; false (and nothing happens) while the mode is locked.
        public static bool Start()
        {
            if (!Unlocked(SaveService.Data))
            {
                Debug.LogWarning("[DESYNC] Endless Overclock is locked: it opens with the DESYNC ending");
                return false;
            }
            RunEvents.StartRun?.Invoke(RunMode.Overclock);
            return true;
        }

        public static int Step(int room) => room / RoomsPerStep;
        public static int ActOf(int room) => 1 + Step(room) % 3;
        public static int HeatOf(int room) => HeatPerStep * Step(room);
        public static float ScoreBonusOf(int room) => ScoreStep * room;

        static bool Live => RunState.Mode == RunMode.Overclock;
        /// RunManager hooks: the current room's heat and extra score multiplier (0 outside Endless Overclock).
        public static int Heat => Live ? HeatOf(Room) : 0;
        public static float ScoreBonus => Live ? ScoreBonusOf(Room) : 0f;

        /// Room `room` of the run on `seed`: an Infil node of its act, row = its place in the step.
        public static RunNode NodeFor(uint seed, int room) => new RunNode
        {
            id = "oc" + room, type = NodeType.Infil, act = ActOf(room), index = room % RoomsPerStep, lane = 1,
            roomSeed = Rng.Substream(seed, "oc" + room).NextUInt(),
        };

        /// RunManager: the first room of a new run.
        internal static RunNode First(uint seed)
        {
            Room = 0;
            return NodeFor(seed, 0);
        }

        /// RunManager: the room after the one just cleared.
        internal static RunNode Next(uint seed) => NodeFor(seed, ++Room);

        public static void Best(SaveV3 save, out int rooms, out int score)
        {
            rooms = score = 0;
            if (!save.settings.TryGetValue(BestKey, out var s) || s == null) return;
            var p = s.Split(',');
            if (p.Length != 2 || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out rooms)
                || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out score)) rooms = score = 0;
        }

        /// Keeps the better run: more rooms, then more score. True when it became the best.
        public static bool RecordBest(SaveV3 save, int rooms, int score)
        {
            Best(save, out int r, out int s);
            if (rooms < r || (rooms == r && score <= s)) return false;
            save.settings[BestKey] = rooms.ToString(CultureInfo.InvariantCulture) + "," + score.ToString(CultureInfo.InvariantCulture);
            return true;
        }
    }
}
