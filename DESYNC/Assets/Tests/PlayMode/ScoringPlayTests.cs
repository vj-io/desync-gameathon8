using System.Collections;
using Desync.Rules;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RuleEvent = Desync.Rules.EventType;

namespace Desync.QA
{
    /// Live scoring through the real flow (events -> ScoreKeeper -> GameState.Score / GameEvents.Scored).
    /// The pure Scoring/Melt functions have their own EditMode tests (SCORE-*, MELT-*); these check the room wiring.
    public class ScoringPlayTests : QaFixture
    {
        [UnityTest]
        public IEnumerator Graze_PaysThreeTimesPerIce_FourthPaysNothing()
        {
            Assert.IsTrue(Flip(), "GRAZE is a SOURCE event");
            float t0 = RoomClock.Time;
            yield return Until(() => RoomClock.Time > t0 + 1.2f, 3f, "the clean-flip window to close");

            for (int k = 1; k <= Scoring.GrazeCapPerIce + 1; k++)
            {
                int before = GameState.Score, paid = Paid(RuleEvent.Graze);
                GameEvents.Grazed?.Invoke("ICE-1");
                yield return null;
                int delta = GameState.Score - before;
                if (k <= Scoring.GrazeCapPerIce)
                {
                    Assert.AreEqual(paid + 1, Paid(RuleEvent.Graze), $"graze {k} of ICE-1 pays");
                    Assert.GreaterOrEqual(delta, Scoring.GrazePts, $"graze {k}: at least the base points");
                    Assert.LessOrEqual(delta, Scoring.GrazePts * 4, $"graze {k}: at most base x the 4.0 multiplier cap");
                }
                else
                {
                    Assert.AreEqual(0, delta, "the 4th graze of the same ICE pays 0");
                    Assert.AreEqual(paid, Paid(RuleEvent.Graze), "...and raises no paying Scored event");
                }
            }

            int other = GameState.Score;
            GameEvents.Grazed?.Invoke("ICE-2");
            yield return null;
            Assert.Greater(GameState.Score, other, "the cap is per ICE: a different one still pays");
        }

        /// Enter SOURCE, then leave it (flip to RENDER) with the melt at the given value, and let the events settle.
        IEnumerator ExitSourceAt(float melt)
        {
            Assert.IsTrue(Flip(), "into SOURCE");
            GameState.Melt = melt;   // same frame as the flip below: nothing moves it in between
            Assert.IsTrue(Flip(), "out of SOURCE");
            yield return Frames(3);
        }

        [UnityTest]
        public IEnumerator Redline_PaysOnlyAtOrAboveNinety()
        {
            yield return ExitSourceAt(89.9f);
            Assert.AreEqual(0, Paid(RuleEvent.Redline), "89.9 % is not a REDLINE");

            yield return ExitSourceAt(90f);
            Assert.AreEqual(1, Paid(RuleEvent.Redline), "90 % is");
            var p = pops.Find(x => x.type == RuleEvent.Redline);
            Assert.AreEqual(Scoring.RedlinePts * p.multiplier, p.points, 1.5f, "250 x the multiplier at that moment");
        }

        [UnityTest]
        public IEnumerator Redline_ThresholdComesFromModifiers()
        {
            var m = GameState.Mods;
            m.RedlineThreshold = 85f;   // Brinkmanship
            GameState.Mods = m;

            yield return ExitSourceAt(84.9f);
            Assert.AreEqual(0, Paid(RuleEvent.Redline));
            yield return ExitSourceAt(85f);
            Assert.AreEqual(1, Paid(RuleEvent.Redline), "the live threshold is GameState.Mods.RedlineThreshold, not a constant 90");
        }
    }
}
