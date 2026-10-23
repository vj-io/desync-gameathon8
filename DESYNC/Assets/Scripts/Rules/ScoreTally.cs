using System;
using System.Collections.Generic;

namespace Desync.Rules
{
    /// Scoring.ScoreFromEvents one event at a time. The game feeds it live and the replay feeds it the stored log: both run
    /// this code, so the live score is the replay score by construction. Payments are summed unrounded and rounded once
    /// (four clean flips pay 62.5 + 75 + 87.5 + 100 = 325, not 326).
    public sealed class ScoreTally
    {
        readonly Dictionary<(string room, string ice), int> _grazes = new Dictionary<(string room, string ice), int>();
        double _sum;

        public int Chain { get; private set; }
        public int Total => (int)Math.Round(_sum, MidpointRounding.AwayFromZero);

        public void Reset()
        {
            _grazes.Clear();
            _sum = 0;
            Chain = 0;
        }

        /// Applies one event; returns what it paid (multiplied, unrounded; 0 when it pays nothing).
        public double Apply(in RunEvent e, in Modifiers m)
        {
            var c = m.Clamped();
            int pts;
            switch (e.Type)
            {
                case EventType.CleanFlip: Chain++; pts = Scoring.CleanFlipPts; break;
                case EventType.ChainBreak:
                case EventType.Detected:
                case EventType.Death: Chain = 0; return 0;
                case EventType.Graze:
                    _grazes.TryGetValue((e.Room, e.Ice), out int n);
                    if (n >= Scoring.GrazeCapPerIce) return 0;
                    _grazes[(e.Room, e.Ice)] = n + 1;
                    pts = Scoring.GrazePts;
                    break;
                case EventType.Exit:   // leaving SOURCE: REDLINE when the melt was at the threshold
                    if (e.Melt < c.RedlineThreshold) return 0;
                    pts = Scoring.RedlinePts;
                    break;
                case EventType.Hack: pts = Scoring.HackPts; break;
                case EventType.HackPerfect: pts = Scoring.HackPerfectPts; break;
                case EventType.NoTraceRoom: pts = Scoring.NoTraceRoomPts; break;
                case EventType.RoomClear: pts = Scoring.RoomClearPts; break;
                case EventType.PuppetString: pts = Scoring.PuppetStringPts; break;
                default: return 0;     // Flip; Redline is informational (its Exit record pays it)
            }
            double paid = pts * (double)Scoring.GhostMultiplier(Chain, c.ChainCap) * c.ScoreMult;
            _sum += paid;
            return paid;
        }
    }
}
