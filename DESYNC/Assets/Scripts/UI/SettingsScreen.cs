using System;
using Desync.Flow;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Swallows pointer-down on dead space. The pause flow resumes the game on a click that lands on no control (GameManager), so
    /// every full-screen panel opened from the pause menu puts one of these under its widgets.
    internal sealed class UIBlocker : MonoBehaviour, IPointerDownHandler
    {
        public void OnPointerDown(PointerEventData e) { }
    }

    /// SOUND + ACCESS: the four volume sliders (master, music, effects, interface) and the accessibility options (HUD size, subtitle
    /// size, high-contrast HUD, TAB as toggle, Assist mode), reached from the pause menu (and the title, through
    /// UIManager.OpenSettings). Master volume is Core's Settings.Volume; everything else is saved through A11y. Every change
    /// raises GameEvents.SettingsChanged.
    public sealed class SettingsScreen : UIPanel
    {
        public const float ScaleStep = 0.1f;

        Slider _master, _music, _sfx, _ui;
        TMP_Text _masterV, _musicV, _sfxV, _uiV, _hudV, _subV;
        NeonToggle _hc, _tab, _assist;
        RectTransform _panel; Tween _pop;
        /// Raised by BACK; the UIManager decides what is behind (pause menu or title).
        internal Action OnBack, OnKeys;

        protected override void Create()
        {
            var cyan = UIKit.Cyan; var magenta = UIKit.Magenta; var paper = UIKit.Paper; var dim = UIKit.Dim;
            var dimmer = UIKit.Img(Root, "Dim", new Color(0f, 0f, 0.03f, 0.9f)); UIKit.Stretch(dimmer.rectTransform); dimmer.raycastTarget = true;
            dimmer.gameObject.AddComponent<UIBlocker>();
            var panel = UIKit.Box(Root, "Panel", new Color(cyan.r, cyan.g, cyan.b, 0.7f), 1f);   // opaque: the pause menu underneath must not show through
            panel.raycastTarget = true; panel.gameObject.AddComponent<UIBlocker>();
            _panel = UIKit.Place(panel.rectTransform, Anc.C, Vector2.zero, new Vector2(1240, 980));

            var head = UIKit.Text(panel.transform, "Head", UIKit.Title, 64, paper, TextAlignmentOptions.Center, "SOUND + ACCESS"); head.fontStyle = FontStyles.Bold; head.characterSpacing = 3f;
            UIKit.Place(head.rectTransform, Anc.T, new Vector2(0, -34), new Vector2(1100, 90));
            var sub = UIKit.Text(panel.transform, "Sub", UIKit.Hud, 24, dim, TextAlignmentOptions.Center, "SAVED WITH YOUR PROFILE · APPLIED AT ONCE"); sub.characterSpacing = 8f;
            UIKit.Place(sub.rectTransform, Anc.T, new Vector2(0, -130), new Vector2(1100, 32));

            // ---- sound (left)
            const float lx = -310f, rx = 310f, w = 500f;
            Head(panel.transform, "SoundHead", "SOUND", lx, -196, w);
            _master = AddSlider(panel.transform, "Master", "MASTER", lx, -252, w, Settings.Volume, v => { Settings.Volume = v; _masterV.SetText("{0:0}%", Mathf.Round(Settings.Volume * 100f)); Changed(); }, out _masterV);
            _music = AddSlider(panel.transform, "Music", "MUSIC", lx, -346, w, A11y.MusicVolume, v => { A11y.MusicVolume = v; _musicV.SetText("{0:0}%", Mathf.Round(A11y.MusicVolume * 100f)); Changed(); }, out _musicV);
            _sfx = AddSlider(panel.transform, "Sfx", "EFFECTS", lx, -440, w, A11y.SfxVolume, v => { A11y.SfxVolume = v; _sfxV.SetText("{0:0}%", Mathf.Round(A11y.SfxVolume * 100f)); Changed(); }, out _sfxV);
            _ui = AddSlider(panel.transform, "Ui", "INTERFACE", lx, -534, w, A11y.UiVolume, v => { A11y.UiVolume = v; _uiV.SetText("{0:0}%", Mathf.Round(A11y.UiVolume * 100f)); Changed(); }, out _uiV);
            var note = UIKit.Text(panel.transform, "SoundNote", UIKit.Hud, 22, dim, TextAlignmentOptions.TopLeft, "MUSIC DUCKS UNDER STINGERS\nAND THE DEATH CARD.\nA LIMITER KEEPS THE LOUDEST\nMOMENTS FROM CLIPPING.");
            note.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Place(note.rectTransform, Anc.T, new Vector2(lx, -640), new Vector2(w, 70));

            // ---- access (right)
            Head(panel.transform, "AccessHead", "ACCESS", rx, -196, w);
            Stepper(panel.transform, "HudSize", "HUD SIZE", rx, -252, w, () => A11y.UiScale, v => A11y.UiScale = v, A11y.UiScaleMin, A11y.UiScaleMax, out _hudV);
            Stepper(panel.transform, "SubSize", "SUBTITLE SIZE", rx, -318, w, () => A11y.SubtitleScale, v => A11y.SubtitleScale = v, A11y.SubtitleScaleMin, A11y.SubtitleScaleMax, out _subV);
            _hc = UIControls.NewToggle(panel.transform, "HighContrast", "HIGH CONTRAST HUD", Anc.T, new Vector2(rx, -388), w, A11y.HighContrast, v => { A11y.HighContrast = v; Changed(); UIManager.I?.RequestRebuild(); });
            _tab = UIControls.NewToggle(panel.transform, "TabToggle", "TAB MAP: TOGGLE, NOT HOLD", Anc.T, new Vector2(rx, -444), w, A11y.TabToggle, v => { A11y.TabToggle = v; Changed(); });
            _assist = UIControls.NewToggle(panel.transform, "Assist", "ASSIST MODE", Anc.T, new Vector2(rx, -500), w, Assist.On, v => { Assist.On = v; Changed(); });
            var an = UIKit.Text(panel.transform, "AssistNote", UIKit.Hud, 22, magenta, TextAlignmentOptions.TopLeft,
                "MELT RISES 25% SLOWER\nTRACE WINDOW +0.2 S\nICE 15% SLOWER\nUNLOCKS, MEDALS, RECORDS STAY.\nNO LEADERBOARD FOR THE RUN;\nRESULTS SHOW AN ASSIST TAG.");
            an.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Place(an.rectTransform, Anc.T, new Vector2(rx, -556), new Vector2(w, 170));

            // ---- buttons
            UIButton.Create(panel.transform, "ResetAccess", "RESET DEFAULTS", Anc.B, new Vector2(-400, 54), new Vector2(380, 86), 32, magenta, ResetDefaults);
            UIButton.Create(panel.transform, "Keys", "KEY BINDINGS", Anc.B, new Vector2(0, 54), new Vector2(380, 86), 32, cyan, () => OnKeys?.Invoke());
            UIButton.Create(panel.transform, "Back", "BACK", Anc.B, new Vector2(400, 54), new Vector2(380, 86), 40, cyan, () => OnBack?.Invoke());
        }

        static void Changed() { UIKit.RefreshSettings(); GameEvents.SettingsChanged?.Invoke(); }

        static void Head(Transform p, string name, string text, float x, float y, float w)
        {
            var t = UIKit.Text(p, name, UIKit.Title, 28, UIKit.Cyan, TextAlignmentOptions.Left, text); t.fontStyle = FontStyles.Bold; t.characterSpacing = 6f;
            UIKit.Place(t.rectTransform, Anc.T, new Vector2(x, y), new Vector2(w, 40));
        }

        static Slider AddSlider(Transform p, string name, string label, float x, float y, float w, float value, UnityEngine.Events.UnityAction<float> on, out TMP_Text val)
        {
            var l = UIKit.Text(p, name + "Label", UIKit.Hud, 28, UIKit.Paper, TextAlignmentOptions.Left, label);
            UIKit.Place(l.rectTransform, Anc.T, new Vector2(x - 90, y), new Vector2(w - 130, 40));
            val = UIKit.Text(p, name + "Val", UIKit.Hud, 28, UIKit.Cyan, TextAlignmentOptions.Right);
            UIKit.Place(val.rectTransform, Anc.T, new Vector2(x + w * 0.5f - 60, y), new Vector2(120, 40));
            return UIControls.NewSlider(p, name, Anc.T, new Vector2(x, y - 44), w, 0f, 1f, value, on);
        }

        /// "- 100% +" in `ScaleStep` steps: a slider would move under the pointer as the HUD rescales.
        void Stepper(Transform p, string name, string label, float x, float y, float w, Func<float> get, Action<float> set, float min, float max, out TMP_Text val)
        {
            var l = UIKit.Text(p, name + "Label", UIKit.Hud, 28, UIKit.Paper, TextAlignmentOptions.Left, label);
            UIKit.Place(l.rectTransform, Anc.T, new Vector2(x - 120, y), new Vector2(w - 250, 40));
            var v = UIKit.Text(p, name + "Val", UIKit.Hud, 28, UIKit.Cyan, TextAlignmentOptions.Center);
            UIKit.Place(v.rectTransform, Anc.T, new Vector2(x + w * 0.5f - 110, y), new Vector2(100, 40));
            val = v;
            void Step(int d)
            {
                float n = Mathf.Round((get() + d * ScaleStep) / ScaleStep) * ScaleStep;
                set(Mathf.Clamp(n, min, max));
                v.SetText("{0:0}%", Mathf.Round(get() * 100f));
                Changed();
            }
            UIButton.Create(p, name + "Minus", "-", Anc.T, new Vector2(x + w * 0.5f - 178, y), new Vector2(52, 44), 30, UIKit.Cyan, () => Step(-1), UIKit.Hud);
            UIButton.Create(p, name + "Plus", "+", Anc.T, new Vector2(x + w * 0.5f - 42, y), new Vector2(52, 44), 30, UIKit.Cyan, () => Step(1), UIKit.Hud);
        }

        void ResetDefaults()
        {
            A11y.ResetDefaults();
            Settings.Volume = 0.8f;
            Changed();
            UIManager.I?.RequestRebuild();   // high contrast may have been on
            Paint();
        }

        void Paint()
        {
            _master.SetValueWithoutNotify(Settings.Volume); _masterV.SetText("{0:0}%", Mathf.Round(Settings.Volume * 100f));
            _music.SetValueWithoutNotify(A11y.MusicVolume); _musicV.SetText("{0:0}%", Mathf.Round(A11y.MusicVolume * 100f));
            _sfx.SetValueWithoutNotify(A11y.SfxVolume); _sfxV.SetText("{0:0}%", Mathf.Round(A11y.SfxVolume * 100f));
            _ui.SetValueWithoutNotify(A11y.UiVolume); _uiV.SetText("{0:0}%", Mathf.Round(A11y.UiVolume * 100f));
            _hudV.SetText("{0:0}%", Mathf.Round(A11y.UiScale * 100f));
            _subV.SetText("{0:0}%", Mathf.Round(A11y.SubtitleScale * 100f));
            _hc.Set(A11y.HighContrast); _tab.Set(A11y.TabToggle); _assist.Set(Assist.On);
        }

        public override void Show()
        {
            base.Show();
            Paint();
            UIKit.FreeCursor();
            UIKit.Pop(ref _pop, _panel, 0.92f, 0.2f);
        }

        public override void Hide() { UIKit.Kill(ref _pop); base.Hide(); }
    }
}
