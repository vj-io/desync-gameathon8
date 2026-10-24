using System.Globalization;
using Desync.Flow;
using Desync.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Desync.M5.Tests")]

namespace Desync.UI
{
    /// The settings that have to reach into the built canvas: HUD size, subtitle size, the high-contrast pass and the key labels
    /// that show a remapped key. UIManager.BuildCanvas calls PostBuild after every screen exists (so a palette / contrast rebuild
    /// gets them again); the rest is applied live from SettingsHost on GameEvents.SettingsChanged / Rebinds.Changed.
    public static class A11yUi
    {
        const float PlateAlpha = 0.94f, SubtitleBarWidth = 980f;

        /// After all screens are built.
        internal static void PostBuild(RectTransform canvas)
        {
            if (!canvas) return;
            if (A11y.HighContrast) { HighContrast(canvas.Find("Hud")); HighContrast(canvas.Find("Goals")); }
            Apply(canvas);
        }

        /// HUD size + subtitle size + key labels + interface volume (live).
        internal static void Apply(RectTransform canvas)
        {
            if (!canvas) return;
            var ears = canvas.parent ? canvas.parent.GetComponent<AudioSource>() : null;   // the UI's own source (results tally ticks, story sounds)
            if (ears) ears.volume = A11y.UiVolume;
            var hud = canvas.Find("Hud") as RectTransform;
            if (!hud) return;
            // The HUD is a stretched root. Scale s about its centre, with anchors at 0.5 -/+ 0.5/s: the rect is W/s x H/s, the scale
            // brings it back to the full canvas, so every corner-anchored block grows (or shrinks) in place. No pixel sizes involved.
            float s = A11y.UiScale, a = 0.5f - 0.5f / s, b = 0.5f + 0.5f / s;
            hud.anchorMin = new Vector2(a, a); hud.anchorMax = new Vector2(b, b);
            hud.offsetMin = hud.offsetMax = Vector2.zero;
            hud.pivot = new Vector2(0.5f, 0.5f);
            hud.localScale = new Vector3(s, s, 1f);
            var sub = hud.Find("Subtitle");   // anchored bottom-centre: it grows upwards
            // The bar is 980 wide in the 1920 design; whatever HUD size is set, keep it inside the screen (the HUD scale multiplies it).
            if (sub) sub.localScale = Vector3.one * Mathf.Min(A11y.SubtitleScale, 1800f / (SubtitleBarWidth * s));
            RefreshLabels(hud);
        }

        /// The HUD's control card and interact key show the keys in force.
        internal static void RefreshLabels(Transform hud)
        {
            if (!hud) return;
            Chip(hud, 0, Rebinds.MoveLabel());
            Chip(hud, 2, Rebinds.Label(Rebinds.Find("flip"), false));
            Chip(hud, 3, Rebinds.Label(Rebinds.Find("hack"), false));
            Chip(hud, 5, Rebinds.Label(Rebinds.Find("restart"), false));
            Chip(hud, 6, Rebinds.Label(Rebinds.Find("pause"), false));
            var e = hud.Find("Key/E");
            if (e && e.TryGetComponent(out TMP_Text t))
            {
                t.enableAutoSizing = true; t.fontSizeMin = 16f; t.fontSizeMax = 44f;
                t.SetText(Rebinds.Label(Rebinds.Find("hack"), false));
            }
        }

        static void Chip(Transform hud, int i, string text)
        {
            var k = hud.Find("ControlCard/Chip" + i + "/Key");
            if (k && k.TryGetComponent(out TMP_Text t)) { t.SetText(text); t.fontSize = text.Length > 6 ? 34f : 44f; }
        }

        /// Near-opaque plates behind HUD text, dim text lifted to the bright paper colour, frames at full strength. Run once on a
        /// freshly built screen (the toggle rebuilds the canvas), so nothing has to be put back.
        internal static void HighContrast(Transform root)
        {
            if (!root) return;
            foreach (var im in root.GetComponentsInChildren<Image>(true))
            {
                var c = im.color;
                if (im.sprite == UIKit.Frame) { c.a = 1f; im.color = c; }
                else if (!im.raycastTarget && c.a > 0.3f && c.a < PlateAlpha && c.r < 0.2f && c.g < 0.2f && c.b < 0.25f) { c.a = PlateAlpha; im.color = c; }
            }
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                if (Near(t.color, UIKit.Dim)) t.color = Color.white;
        }

        static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.05f;
    }

    /// The small ASSIST chip results screens show when the attempt used Assist mode (Assist.Flagged).
    internal static class AssistTag
    {
        public static GameObject Create(Transform parent, Anc a, Vector2 pos)
        {
            var box = UIKit.Box(parent, "AssistTag", UIKit.Magenta, 0.9f);
            UIKit.Place(box.rectTransform, a, pos, new Vector2(170, 44));
            var t = UIKit.Text(box.transform, "Label", UIKit.Hud, 26, UIKit.Magenta, TextAlignmentOptions.Center, Assist.Tag);
            t.fontStyle = FontStyles.Bold; t.characterSpacing = 6f;
            UIKit.Stretch(t.rectTransform);
            box.gameObject.SetActive(false);
            return box.gameObject;
        }
    }

    /// Persistent host for the settings side of the game: applies the saved key bindings at boot, repaints the HUD labels when a
    /// binding or an accessibility option changes, and relays the analytics events whose sources live above the Net layer (run
    /// start / end, deaths, settings changes). Self-bootstraps like the other managers. Properties sent are anonymous gameplay
    /// facts only (docs/ANALYTICS.md).
    public sealed class SettingsHost : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const float SettleSeconds = 1.5f;   // a slider drag raises SettingsChanged every frame: report the value it rests on
        static SettingsHost I;

        string[] shown = System.Array.Empty<string>();
        float dirtyAt = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I) return;
            var go = new GameObject("DesyncSettings");
            DontDestroyOnLoad(go);
            I = go.AddComponent<SettingsHost>();
        }

        void Awake()
        {
            GameInput.Init();
            Rebinds.LoadAll();
            shown = Snapshot();
        }

        void OnEnable()
        {
            GameEvents.SettingsChanged += OnSettingsChanged;
            GameEvents.PlayerKilled += OnKilled;
            GameEvents.GameCompleted += OnCompleted;
            RunEvents.StartRun += OnStartRun;
            RunEvents.RunEnded += OnRunEnded;
            DeepRun.StartRequested += OnDeepStart;
            Rebinds.Changed += OnSettingsChanged;
        }

        void OnDisable()
        {
            GameEvents.SettingsChanged -= OnSettingsChanged;
            GameEvents.PlayerKilled -= OnKilled;
            GameEvents.GameCompleted -= OnCompleted;
            RunEvents.StartRun -= OnStartRun;
            RunEvents.RunEnded -= OnRunEnded;
            DeepRun.StartRequested -= OnDeepStart;
            Rebinds.Changed -= OnSettingsChanged;
        }

        void OnDestroy() { if (I == this) I = null; }

        void Update()
        {
            if (dirtyAt >= 0f && Time.unscaledTime - dirtyAt >= SettleSeconds) { dirtyAt = -1f; ReportSettings(); }
        }

        void OnSettingsChanged()
        {
            dirtyAt = Time.unscaledTime;
            var ui = UIManager.I;
            if (ui && ui.Canvas) A11yUi.Apply((RectTransform)ui.Canvas.transform);
        }

        // ------------------------------------------------------------------ analytics relay
        internal static void OnStartRun(RunMode m) => Analytics.Track("run_start", ("mode", m.ToString()), ("assist", Assist.On));
        internal static void OnDeepStart(int level, System.Collections.Generic.IReadOnlyList<Desync.Content.MutatorPick> picks) => Analytics.Track("run_start", ("mode", "Deep"), ("level", level), ("assist", Assist.On));

        internal static void OnRunEnded() => Analytics.Track("run_end", ("mode", RunState.Mode.ToString()), ("won", RunState.Won), ("rooms", RunState.Grades.Count),
            ("score", RunState.RunScore), ("time", RunState.RunTime), ("assist", Assist.Flagged));

        /// The six classic rooms end through GameCompleted (a BREACH run ends through RunEnded).
        internal static void OnCompleted()
        {
            if (RunState.Mode != RunMode.Classic) return;
            Analytics.Track("run_end", ("mode", "Classic"), ("won", true), ("rooms", SceneFlow.RoomCount), ("assist", Assist.Flagged));
        }

        internal static void OnKilled(DeathCause cause)
        {
            if (GameState.God) return;
            Analytics.Track("death", ("cause", cause.ToString()), ("room", RunState.NodeId ?? "r" + (GameState.RoomIndex + 1)), ("assist", Assist.On));
        }

        // ------------------------------------------------------------------ settings_changed
        /// Names of the settings reported, in the order Snapshot returns their values.
        internal static readonly string[] SettingNames =
        {
            "assist", "hud_scale", "subtitle_scale", "high_contrast", "tab_toggle", "reduce_fx", "reduce_flashes", "shake", "head_bob",
            "colorblind", "subtitles", "vol_master", "vol_music", "vol_sfx", "vol_ui", "custom_binds",
        };

        internal static string[] Snapshot()
        {
            int custom = 0;
            foreach (var s in Rebinds.Slots)
            {
                if (!Rebinds.IsDefault(s, false)) custom++;
                if (s.PadRebindable && !Rebinds.IsDefault(s, true)) custom++;
            }
            return new[]
            {
                Assist.On ? "1" : "0", A11y.UiScale.ToString("0.0", Inv), A11y.SubtitleScale.ToString("0.0", Inv), A11y.HighContrast ? "1" : "0", A11y.TabToggle ? "1" : "0",
                Settings.ReduceFx ? "1" : "0", Settings.ReduceFlashes ? "1" : "0", Settings.Shake ? "1" : "0", Settings.HeadBob ? "1" : "0",
                Desync.Art.Palette.Colorblind ? "1" : "0", Subtitles.Enabled ? "1" : "0",
                Settings.Volume.ToString("0.0", Inv), A11y.MusicVolume.ToString("0.0", Inv), A11y.SfxVolume.ToString("0.0", Inv), A11y.UiVolume.ToString("0.0", Inv), custom.ToString(),
            };
        }

        /// One settings_changed event per setting whose value differs from the last report. Public for tests.
        public void ReportSettings()
        {
            var now = Snapshot();
            for (int i = 0; i < now.Length && i < shown.Length; i++)
                if (now[i] != shown[i]) Analytics.Track("settings_changed", ("setting", SettingNames[i]), ("value", now[i]));
            shown = now;
        }
    }
}
