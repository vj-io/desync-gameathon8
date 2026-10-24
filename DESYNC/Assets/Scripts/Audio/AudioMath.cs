using UnityEngine;

namespace Desync.Audio
{
    /// The pure formulas behind the adaptive mix and the pitched cues, kept apart so they can be checked without an audio device.
    public static class AudioMath
    {
        /// Draft decrypt: 16 ticks, one every 50 ms over 0.8 s, pitch climbing one octave (mirrors the draft screen's reveal).
        public const float DecryptTime = 0.8f, DecryptEvery = 0.05f;
        public const int DecryptTicks = 16;

        /// Tension layer gain 0..1 from melt: 0 at 50 %, 1 at 90 %.
        public static float Tension(float melt) => Mathf.Clamp01((melt - 50f) / 40f);

        /// TRACE layers by level (1 = first TRACE, 2 = second, 3 = alarm). A carries the scanner pulse from level 1,
        /// B adds the klaxon from level 2; the level reached sets how hard each pushes.
        public static float TraceA(int level) => level <= 0 ? 0f : level == 1 ? 0.7f : 1f;
        public static float TraceB(int level) => level <= 1 ? 0f : level == 2 ? 0.7f : 1f;

        /// Score pop pitch: one semitone per GHOST chain step (0.25 of multiplier), at most one octave.
        public static float ScorePitch(float multiplier) => Mathf.Pow(2f, Mathf.Clamp((multiplier - 1f) * 4f, 0f, 12f) / 12f);

        /// Pitch of decrypt tick `i` (0-based): 1.0625 for the first, 2.0 for the last.
        public static float DecryptPitch(int i) => 1f + (i + 1) * DecryptEvery / DecryptTime;

        /// Countdown tick cadence while the uplink runs: once a second, twice a second under 5 s, four times under 2 s.
        /// Returns an id that changes exactly when a tick is due; -1 when the countdown is not running.
        public static int UplinkTickId(float remaining)
        {
            if (remaining <= 0f) return -1;
            return remaining > 5f ? Mathf.CeilToInt(remaining)
                 : remaining > 2f ? 1000 + Mathf.CeilToInt(remaining * 2f)
                 : 2000 + Mathf.CeilToInt(remaining * 4f);
        }

        /// Uplink tick pitch: 1.0 with the full 20 s left, 1.5 at zero.
        public static float UplinkPitch(float remaining) => 1f + 0.5f * (1f - Mathf.Clamp01(remaining / 20f));

        /// Act whose stems play: the run's act while a run is on screen, act 1 otherwise (title, classic rooms).
        public static int ActIndex(bool runOn, int runAct) => runOn ? Mathf.Clamp(runAct - 1, 0, 2) : 0;
    }
}
