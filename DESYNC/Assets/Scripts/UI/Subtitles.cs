using UnityEngine;

namespace Desync.UI
{
    /// Subtitles for spoken / scripted lines (the ARCHITECT's). Anything may call Say(); the HUD shows the latest line in a bar at the
    /// bottom centre for a few seconds. The on/off switch is a pause-menu toggle (PlayerPrefs "subs", default on): Core's Settings is
    /// frozen, so it lives here. No allocation after the first call: the HUD compares Seq, nothing is polled from PlayerPrefs per frame.
    public static class Subtitles
    {
        public const string Key = "subs";

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(Key, 1) == 1;
            set => PlayerPrefs.SetInt(Key, value ? 1 : 0);
        }

        public static int Seq { get; private set; }
        public static string Who { get; private set; }
        public static string Line { get; private set; }
        public static float Seconds { get; private set; }

        /// `who` is the speaker tag ("ARCHITECT"), `seconds` <= 0 picks a reading time from the line length (2 to 7 s).
        public static void Say(string who, string line, float seconds = 0f)
        {
            if (string.IsNullOrEmpty(line) || !Enabled) return;
            Who = who; Line = line;
            Seconds = seconds > 0f ? seconds : Mathf.Clamp(1.4f + line.Length * 0.06f, 2f, 7f);
            Seq++;
        }

        /// Clears the line now (room reset): the HUD hides the bar on its next tick.
        public static void Clear() { Line = null; Seq++; }
    }

    /// The ARCHITECT's lines as subtitles. Phases follow BREACH_SPEC "Bosses": P1 gaze, P2 scrambled prompts, P3 "Desync", the gap, endings.
    public static class ArchitectLines
    {
        public const string Who = "ARCHITECT";

        public static readonly string[] Phase =
        {
            "YOU SHOULD NOT BE ABLE TO SEE ME.",
            "THE STRINGS ARE NOT YOURS TO CUT.",
            "BOTH LAYERS. ONE OF YOU. CHOOSE.",
        };
        public const string Gap = "NOW. THE LAST STRING.";
        public const string Exposed = "THEY WILL SEE EVERYTHING.";
        public const string Merged = "GOOD. STAY WITH ME.";
        public const string Desync = "YOU WERE NEVER ONE THING.";

        /// Phase is 1..3 (anything else is ignored).
        public static void SayPhase(int phase) { if (phase >= 1 && phase <= Phase.Length) Subtitles.Say(Who, Phase[phase - 1]); }
        public static void SayGap() { Subtitles.Say(Who, Gap, 2.2f); }

        /// "exposed" | "merged" | "desync" (BossEvents.Ending names).
        public static void SayEnding(string ending)
        {
            switch (ending)
            {
                case "exposed": Subtitles.Say(Who, Exposed, 4f); break;
                case "merged": Subtitles.Say(Who, Merged, 4f); break;
                case "desync": Subtitles.Say(Who, Desync, 4f); break;
            }
        }
    }
}
