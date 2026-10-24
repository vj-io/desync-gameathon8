using System;
using UnityEngine;

namespace Desync.UI
{
    [Serializable] public sealed class StoryAskText { public string title, sub, watch, skip, foot; }
    [Serializable] public sealed class StoryGuideText { public string flip, melt, terminal, hack, exit; }

    /// One camera move in the Title office: a cut to `from` looking at `look`, then an eased move to `to` / `lookTo` over `dur`.
    /// `join` continues from wherever the camera is (no cut).
    [Serializable] public sealed class StoryShot { public float t, dur; public bool join; public Vector3 from, to, look, lookTo; }

    /// One cue at `t` seconds. kind: head (Orbitron caption), sys (VT323 line), card (ECHO's ID: text = name, sub = details),
    /// panel (side render|source: text = title, sub = body), goal (one line of the goal list), layer (side render|source),
    /// flicker (a split-second SOURCE glimpse), logo, clear (drops every caption), wake (the closing prompt; waits for input).
    /// Captions stay `hold` seconds. pitch / vol 0 = 1 / default.
    [Serializable] public sealed class StoryBeat { public float t, hold, pitch, vol; public string kind, text, sub, side, color, sfx; }

    /// STORY mode script, Resources/Content/story.json: the first-time prompt, the cinematic (shots + beats in time order) and the
    /// Room 1 guide lines. Read once.
    [Serializable] public sealed class StoryScript
    {
        /// Typewriter speeds (characters per second) and the reading rule: every caption types out, then stays fully readable for
        /// at least SecondsPerWord per word. Layer flips and flickers are full-screen flashes: at least MinFlashGap apart.
        public const float HeadCps = 60f, SysCps = 45f, BodyCps = 50f, SecondsPerWord = 0.3f, MinFlashGap = 2f;
        public const int MaxWords = 12;

        public StoryAskText ask;
        public StoryGuideText guide;
        public string skipHint;
        public StoryShot[] shots;
        public StoryBeat[] beats;

        static StoryScript _i;
        public static StoryScript I => _i ?? (_i = Load());

        static StoryScript Load()
        {
            var ta = Resources.Load<TextAsset>("Content/story");
            StoryScript s = null;
            try { if (ta) s = JsonUtility.FromJson<StoryScript>(ta.text); }
            catch (ArgumentException e) { Debug.LogWarning("[DESYNC] story.json unreadable: " + e.Message); }
            if (s == null) { Debug.LogWarning("[DESYNC] Resources/Content/story.json missing: STORY has nothing to play"); s = new StoryScript(); }
            if (s.ask == null) s.ask = new StoryAskText();
            if (s.guide == null) s.guide = new StoryGuideText();
            if (s.shots == null) s.shots = new StoryShot[0];
            if (s.beats == null) s.beats = new StoryBeat[0];
            return s;
        }

        /// Words a reader has to read: whitespace-separated tokens holding a letter or digit ("//" and "·" don't count).
        public static int Words(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 0; bool tok = false, alnum = false;
            for (int i = 0; i <= s.Length; i++)
            {
                if (i == s.Length || char.IsWhiteSpace(s[i])) { if (tok && alnum) n++; tok = alnum = false; }
                else { tok = true; alnum |= char.IsLetterOrDigit(s[i]); }
            }
            return n;
        }

        /// Typing speed of a caption kind; goals, panel titles and the logo appear whole.
        public static float Cps(string kind) =>
            kind == "head" ? HeadCps : kind == "sys" ? SysCps : kind == "body" ? BodyCps : float.PositiveInfinity;

        /// Seconds `text` needs on screen as `kind`: typed out, then SecondsPerWord per word fully visible.
        public static float MinHold(string text, string kind) =>
            string.IsNullOrEmpty(text) ? 0f : text.Length / Cps(kind) + SecondsPerWord * Words(text);

        /// Time of the closing "wake" beat = the cinematic's length (-1 when there is none).
        public float Length
        {
            get { for (int i = beats.Length - 1; i >= 0; i--) if (beats[i].kind == "wake") return beats[i].t; return -1f; }
        }
    }
}
