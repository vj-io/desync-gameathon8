using System.Globalization;
using Desync.Net;
using Desync.Rules;
using UnityEngine;

namespace Desync.Flow
{
    /// The player's accessibility, audio-mix and Assist settings. They live in SaveV3.settings under the "a11y_" / "assist_" key
    /// prefixes (so they follow the profile) and are cached in fields: reading one every frame costs nothing. Core's Settings
    /// (PlayerPrefs) stays the home of the older toggles (sensitivity, shake, Reduce FX, master volume).
    /// A write goes to the save through SaveService.Save() (throttled); the caller raises GameEvents.SettingsChanged.
    public static class A11y
    {
        public const string BindPrefix = "a11y_bind_",
                            UiScaleKey = "a11y_ui_scale", SubtitleScaleKey = "a11y_sub_scale", HighContrastKey = "a11y_hicon", TabToggleKey = "a11y_tab_toggle",
                            VolMusicKey = "a11y_vol_music", VolSfxKey = "a11y_vol_sfx", VolUiKey = "a11y_vol_ui", AssistKey = "assist_on";
        public const float UiScaleMin = 0.8f, UiScaleMax = 1.4f, SubtitleScaleMin = 1f, SubtitleScaleMax = 1.6f;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static bool synced;
        static float music = 1f, sfx = 1f, ui = 1f, uiScale = 1f, subScale = 1f;
        static bool hiCon, tabToggle, assist;

        /// Reads the cache from the profile once. Per-frame readers (the audio mix, the TAB key) then never touch the save, so they cannot
        /// trigger a load or allocate. Call Reload() if the profile object is ever replaced (SaveService.Unload in tests, an import).
        static void Sync()
        {
            if (synced) return;
            synced = true;
            var d = SaveService.Data;
            music = Float(d, VolMusicKey, 1f, 0f, 1f); sfx = Float(d, VolSfxKey, 1f, 0f, 1f); ui = Float(d, VolUiKey, 1f, 0f, 1f);
            uiScale = Float(d, UiScaleKey, 1f, UiScaleMin, UiScaleMax); subScale = Float(d, SubtitleScaleKey, 1f, SubtitleScaleMin, SubtitleScaleMax);
            hiCon = d.settings.TryGetValue(HighContrastKey, out var h) && h == "1";
            tabToggle = d.settings.TryGetValue(TabToggleKey, out var t) && t == "1";
            assist = d.settings.TryGetValue(AssistKey, out var a) && a == "1";
        }

        /// Forget the cache: the next read comes from the current profile.
        public static void Reload() { synced = false; }

        static float Float(SaveV3 d, string key, float def, float min, float max) =>
            d.settings.TryGetValue(key, out var s) && float.TryParse(s, NumberStyles.Float, Inv, out var v) && !float.IsNaN(v) ? Mathf.Clamp(v, min, max) : def;

        static void Put(string key, string value)
        {
            if (value == null) SaveService.Data.settings.Remove(key); else SaveService.Data.settings[key] = value;
            SaveService.Save();
        }

        static string F(float v) => v.ToString("0.00", Inv);

        /// A stored string (key bindings), or null.
        public static string Get(string key) => SaveService.Data.settings.TryGetValue(key, out var v) ? v : null;
        /// Stores a string (null removes the key).
        public static void Set(string key, string value) { Sync(); Put(key, value); }

        public static float MusicVolume { get { Sync(); return music; } set { Sync(); music = Mathf.Clamp01(value); Put(VolMusicKey, F(music)); } }
        public static float SfxVolume { get { Sync(); return sfx; } set { Sync(); sfx = Mathf.Clamp01(value); Put(VolSfxKey, F(sfx)); } }
        public static float UiVolume { get { Sync(); return ui; } set { Sync(); ui = Mathf.Clamp01(value); Put(VolUiKey, F(ui)); } }
        public static float UiScale { get { Sync(); return uiScale; } set { Sync(); uiScale = Mathf.Clamp(value, UiScaleMin, UiScaleMax); Put(UiScaleKey, F(uiScale)); } }
        public static float SubtitleScale { get { Sync(); return subScale; } set { Sync(); subScale = Mathf.Clamp(value, SubtitleScaleMin, SubtitleScaleMax); Put(SubtitleScaleKey, F(subScale)); } }
        public static bool HighContrast { get { Sync(); return hiCon; } set { Sync(); hiCon = value; Put(HighContrastKey, value ? "1" : null); } }
        /// TAB (run map / goals) toggles instead of being held.
        public static bool TabToggle { get { Sync(); return tabToggle; } set { Sync(); tabToggle = value; Put(TabToggleKey, value ? "1" : null); } }
        public static bool AssistOn { get { Sync(); return assist; } set { Sync(); assist = value; Put(AssistKey, value ? "1" : null); } }

        /// Every volume, scale and toggle back to its default (key bindings have their own reset).
        public static void ResetDefaults()
        {
            Sync();
            music = sfx = ui = uiScale = subScale = 1f; hiCon = tabToggle = assist = false;
            foreach (var k in new[] { VolMusicKey, VolSfxKey, VolUiKey, UiScaleKey, SubtitleScaleKey, HighContrastKey, TabToggleKey, AssistKey })
                SaveService.Data.settings.Remove(k);
            SaveService.Save();
        }
    }

    /// ASSIST MODE (accessibility): melt rises at x0.75, the camera TRACE window is 0.2 s longer, ICE moves at x0.85. It is a
    /// plain Rules.Modifiers fold, so MeltMeter, the vision cones and every ICE pick it up through GameState.Mods with no
    /// per-system code. It never locks an unlock, a medal or a record: its one consequence is that leaderboard submission is
    /// refused, and results show an ASSIST tag. Anything that submits a score must check Flagged (not just On): switching Assist
    /// off before the last room does not clean the run.
    public static class Assist
    {
        public const float MeltRise = 0.75f, TraceWindowBonus = 0.2f, IceSpeed = 0.85f;
        public const string Tag = "ASSIST";

        /// The setting (pause menu, persisted).
        public static bool On { get => A11y.AssistOn; set => A11y.AssistOn = value; }
        /// Assist was on at any moment of the current attempt (run, or classic playthrough). Cleared back at the title.
        public static bool Used { get; internal set; }
        /// True when the current attempt is not leaderboard-eligible.
        public static bool Flagged => On || Used;

        /// The modifiers with Assist applied. Pure: the base game's numbers in, the eased numbers out.
        public static Modifiers Apply(in Modifiers m)
        {
            var r = m;
            r.SourceRise *= MeltRise;
            r.TraceWindow += TraceWindowBonus;
            r.IceSpeedMult *= IceSpeed;
            return r;
        }
    }

    /// Keeps GameState.Mods = Assist.Apply(what the run wrote) while Assist is on. RunManager and the room flow rewrite
    /// GameState.Mods at room start and whenever a conditional exploit flips, from their own source of truth; this host notices a
    /// write that is not its own, remembers it as the base, and republishes the eased copy, so no other file needs to know about
    /// Assist. It runs before every gameplay Update; a write made later in the same frame is eased from the next frame.
    [DefaultExecutionOrder(-1000)]
    public sealed class AssistHost : MonoBehaviour
    {
        static Modifiers baseMods, applied;
        static bool have;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<AssistHost>()) return;
            var go = new GameObject("DesyncAssist");
            DontDestroyOnLoad(go);
            go.AddComponent<AssistHost>();
        }

        void Update() { Fold(); Latch(); }

        /// Assist.Used: set while an attempt is being played with Assist on, cleared once back at the title with no run open.
        public static void Latch()
        {
            if (GameState.Phase == GameState.PhaseKind.Playing && !GameState.Paused && Assist.On) Assist.Used = true;
            else if (GameState.Phase == GameState.PhaseKind.Title && RunState.Phase == RunPhase.None) Assist.Used = false;
        }

        /// One fold step (public for tests).
        public static void Fold()
        {
            var cur = GameState.Mods;
            if (!have || !Same(cur, applied)) baseMods = cur;   // a write that is not ours: the run's new numbers
            var want = Assist.On ? Assist.Apply(baseMods) : baseMods;
            if (!Same(want, cur)) GameState.Mods = want;
            applied = want; have = true;
        }

        /// Forgets the remembered base (tests).
        public static void Reset() { have = false; Assist.Used = false; }

        static bool Same(in Modifiers a, in Modifiers b) =>
            a.FlipCost == b.FlipCost && a.SourceRise == b.SourceRise && a.RenderDrain == b.RenderDrain && a.TraceWindow == b.TraceWindow
            && a.ChainWindow == b.ChainWindow && a.ChainCap == b.ChainCap && a.ScoreMult == b.ScoreMult && a.MoveSpeedMult == b.MoveSpeedMult
            && a.IceSpeedMult == b.IceSpeedMult && a.RedlineThreshold == b.RedlineThreshold && a.CameraRangeMult == b.CameraRangeMult
            && a.HackLengthDelta == b.HackLengthDelta && a.GrazeCapBonus == b.GrazeCapBonus;
    }
}
