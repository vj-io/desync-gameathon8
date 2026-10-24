using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Pause menu: controls list, sliders, toggles, resolution scale, Resume / Restart / Quit.
    /// Every settings change writes Settings.* then raises GameEvents.SettingsChanged.
    public sealed class PauseScreen : UIPanel
    {
        GlitchLabel _head; TMP_Text _sub, _sensVal, _volVal;
        Slider _sens, _vol; NeonToggle _shake, _flash, _fx, _bob, _cb, _subs;
        UIButton _res75, _res100;
        TMP_Text[] _keyT, _capT; int _bindVer = -1; bool _tabToggle;
        RectTransform _panel; Tween _pop, _pulse;
        /// The last Show() was "click to resume" (the pointer was released); the UIManager keeps it across a canvas rebuild.
        internal bool LockLost { get; private set; }

        protected override void Create()
        {
            var cyan = UIKit.Cyan; var magenta = UIKit.Magenta; var paper = UIKit.Paper; var dim = UIKit.Dim;
            var dimmer = UIKit.Img(Root, "Dim", new Color(0f, 0f, 0.03f, 0.74f)); UIKit.Stretch(dimmer.rectTransform); dimmer.raycastTarget = true;

            var panel = UIKit.Box(Root, "Panel", new Color(cyan.r, cyan.g, cyan.b, 0.7f), 0.94f);
            _panel = UIKit.Place(panel.rectTransform, Anc.C, Vector2.zero, new Vector2(1240, 980));

            _head = GlitchLabel.Create(panel.transform, "Head", Anc.T, new Vector2(0, -34), new Vector2(1100, 120), UIKit.Title, 84, paper, TextAlignmentOptions.Center, 3f, true, 8f);
            _sub = UIKit.Text(panel.transform, "Sub", UIKit.Hud, 24, dim, TextAlignmentOptions.Center, "SESSION SUSPENDED"); _sub.characterSpacing = 8f;
            UIKit.Place(_sub.rectTransform, Anc.T, new Vector2(0, -150), new Vector2(1100, 32));

            // ---- controls (left)
            var ch = UIKit.Text(panel.transform, "ControlsHead", UIKit.Title, 28, cyan, TextAlignmentOptions.Left, "CONTROLS"); ch.fontStyle = FontStyles.Bold; ch.characterSpacing = 6f;
            UIKit.Place(ch.rectTransform, Anc.T, new Vector2(-300, -212), new Vector2(480, 40));
            string[,] k = { { "WASD", "MOVE" }, { "MOUSE", "LOOK" }, { "SPACE", "FLIP LAYER" }, { "E", "HACK" }, { "← ↑ → ↓", "ENTER CODE" }, { "R", "RESTART ROOM" }, { "ESC", "PAUSE" }, { "TAB", "MAP / GOALS (HOLD)" } };
            _keyT = new TMP_Text[k.GetLength(0)]; _capT = new TMP_Text[k.GetLength(0)];
            for (int i = 0; i < k.GetLength(0); i++)
            {
                float y = -268 - i * 50;
                var kt = UIKit.Text(panel.transform, "Key" + i, UIKit.Hud, 30, i == 2 ? magenta : cyan, TextAlignmentOptions.Left, k[i, 0]); kt.fontStyle = FontStyles.Bold;
                UIKit.Place(kt.rectTransform, Anc.T, new Vector2(-300 - 120, y), new Vector2(240, 40));
                var ct = UIKit.Text(panel.transform, "Cap" + i, UIKit.Hud, 30, paper, TextAlignmentOptions.Left, k[i, 1]);
                UIKit.Place(ct.rectTransform, Anc.T, new Vector2(-300 + 130, y), new Vector2(300, 40));
                _keyT[i] = kt; _capT[i] = ct;
            }
            // Remapping and the accessibility options: their own screens (UIManager), opened from here.
            UIButton.Create(panel.transform, "KeyBindings", "KEY BINDINGS", Anc.T, new Vector2(-300, -690), new Vector2(480, 60), 30, magenta, () => UIManager.I?.OpenRebind(), UIKit.Hud);
            UIButton.Create(panel.transform, "SoundAccess", "SOUND + ACCESS", Anc.T, new Vector2(-300, -762), new Vector2(480, 60), 30, cyan, () => UIManager.I?.OpenSettings(), UIKit.Hud);

            // ---- settings (right)
            const float cx = 310f, w = 500f;
            var sh = UIKit.Text(panel.transform, "SettingsHead", UIKit.Title, 28, cyan, TextAlignmentOptions.Left, "SETTINGS"); sh.fontStyle = FontStyles.Bold; sh.characterSpacing = 6f;
            UIKit.Place(sh.rectTransform, Anc.T, new Vector2(cx, -212), new Vector2(w, 40));

            var sl = UIKit.Text(panel.transform, "SensLabel", UIKit.Hud, 28, paper, TextAlignmentOptions.Left, "SENSITIVITY");
            UIKit.Place(sl.rectTransform, Anc.T, new Vector2(cx - 90, -268), new Vector2(w - 130, 40));
            _sensVal = UIKit.Text(panel.transform, "SensVal", UIKit.Hud, 28, cyan, TextAlignmentOptions.Right);
            UIKit.Place(_sensVal.rectTransform, Anc.T, new Vector2(cx + w * 0.5f - 60, -268), new Vector2(120, 40));
            _sens = UIControls.NewSlider(panel.transform, "Sens", Anc.T, new Vector2(cx, -312), w, 0.1f, 3f, Settings.Sensitivity, v => { Settings.Sensitivity = v; _sensVal.SetText("{0:0.00}", Settings.Sensitivity); Changed(); });

            var vl = UIKit.Text(panel.transform, "VolLabel", UIKit.Hud, 28, paper, TextAlignmentOptions.Left, "VOLUME");
            UIKit.Place(vl.rectTransform, Anc.T, new Vector2(cx - 90, -366), new Vector2(w - 130, 40));
            _volVal = UIKit.Text(panel.transform, "VolVal", UIKit.Hud, 28, cyan, TextAlignmentOptions.Right);
            UIKit.Place(_volVal.rectTransform, Anc.T, new Vector2(cx + w * 0.5f - 60, -366), new Vector2(120, 40));
            _vol = UIControls.NewSlider(panel.transform, "Vol", Anc.T, new Vector2(cx, -410), w, 0f, 1f, Settings.Volume, v => { Settings.Volume = v; _volVal.SetText("{0:0}%", Mathf.Round(Settings.Volume * 100f)); Changed(); });

            _shake = UIControls.NewToggle(panel.transform, "Shake", "SCREEN SHAKE", Anc.T, new Vector2(cx, -462), w, Settings.Shake, v => { Settings.Shake = v; Changed(); });
            _flash = UIControls.NewToggle(panel.transform, "Flash", "REDUCE FLASHES", Anc.T, new Vector2(cx, -512), w, Settings.ReduceFlashes, v => { Settings.ReduceFlashes = v; Changed(); });
            _fx = UIControls.NewToggle(panel.transform, "ReduceFx", "REDUCE FX", Anc.T, new Vector2(cx, -562), w, Settings.ReduceFx, v => { Settings.ReduceFx = v; Changed(); });   // shake x0, flashes off, chromatic x0.3
            _bob = UIControls.NewToggle(panel.transform, "Bob", "HEAD-BOB", Anc.T, new Vector2(cx, -612), w, Settings.HeadBob, v => { Settings.HeadBob = v; Changed(); });
            // Accessibility: the colour-blind palette (UIManager rebuilds every screen in the new hues) and Architect subtitles.
            _cb = UIControls.NewToggle(panel.transform, "Colorblind", "COLORBLIND PALETTE", Anc.T, new Vector2(cx, -662), w, Desync.Art.Palette.Colorblind, v => { Desync.Art.Palette.Colorblind = v; Changed(); });
            _subs = UIControls.NewToggle(panel.transform, "Subtitles", "SUBTITLES", Anc.T, new Vector2(cx, -712), w, Subtitles.Enabled, v => { Subtitles.Enabled = v; Changed(); });

            var rl = UIKit.Text(panel.transform, "ResLabel", UIKit.Hud, 28, paper, TextAlignmentOptions.Left, "RESOLUTION SCALE");
            UIKit.Place(rl.rectTransform, Anc.T, new Vector2(cx - 90, -768), new Vector2(w - 130, 40));
            _res75 = UIButton.Create(panel.transform, "Res75", "75%", Anc.T, new Vector2(cx + 86, -768), new Vector2(104, 44), 24, cyan, () => SetRes(0.75f), UIKit.Hud);
            _res100 = UIButton.Create(panel.transform, "Res100", "100%", Anc.T, new Vector2(cx + 198, -768), new Vector2(104, 44), 24, cyan, () => SetRes(1f), UIKit.Hud);

            // ---- buttons
            UIButton.Create(panel.transform, "Resume", "RESUME", Anc.B, new Vector2(-380, 54), new Vector2(340, 86), 44, cyan, () => GameEvents.ResumeRequested?.Invoke());
            UIButton.Create(panel.transform, "Restart", "RESTART ROOM", Anc.B, new Vector2(0, 54), new Vector2(400, 86), 34, magenta, OnRestart);
            UIButton.Create(panel.transform, "Quit", "QUIT TO TITLE", Anc.B, new Vector2(400, 54), new Vector2(380, 86), 34, UIKit.Danger, () => GameEvents.QuitRequested?.Invoke());
        }

        static void Changed() { UIKit.RefreshSettings(); GameEvents.SettingsChanged?.Invoke(); }

        static void OnRestart() { GameEvents.RestartRequested?.Invoke(); }   // Flow un-pauses itself, RoomManager reboots

        void SetRes(float v) { Settings.ResolutionScale = v; PaintRes(); Changed(); }

        void PaintRes()
        {
            float r = Settings.ResolutionScale;
            _res75.SetSelected(Mathf.Abs(r - 0.75f) < 0.01f);
            _res100.SetSelected(r > 0.9f);
        }

        /// lockLost: paused because the browser released the pointer (not Esc) -> big CLICK TO RESUME.
        public void Show(bool lockLost)
        {
            base.Show();
            LockLost = lockLost;
            _head.Reveal(lockLost ? "CLICK TO RESUME" : "PAUSED", 0.25f);
            _sub.SetText(lockLost ? "POINTER RELEASED · CLICK / START / A TO RE-ENTER" : "SESSION SUSPENDED · START / A TO RESUME");
            _sub.color = lockLost ? UIKit.Magenta : UIKit.Dim;
            _head.SetColor(lockLost ? UIKit.Magenta : UIKit.Paper);
            _sens.SetValueWithoutNotify(Settings.Sensitivity); _sensVal.SetText("{0:0.00}", Settings.Sensitivity);
            _vol.SetValueWithoutNotify(Settings.Volume); _volVal.SetText("{0:0}%", Mathf.Round(Settings.Volume * 100f));
            _shake.Set(Settings.Shake); _flash.Set(Settings.ReduceFlashes); _fx.Set(Settings.ReduceFx); _bob.Set(Settings.HeadBob);
            _cb.Set(Desync.Art.Palette.Colorblind); _subs.Set(Subtitles.Enabled);
            PaintRes();
            PaintKeys();
            UIKit.Pop(ref _pop, _panel, 0.9f, 0.22f);
            UIKit.Kill(ref _pulse);
            if (lockLost) _pulse = Tween.Scale(_head.transform, 1f, 1.05f, 0.5f, Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo, useUnscaledTime: true);
            else _head.transform.localScale = Vector3.one;
        }

        public override void Show() { Show(false); }

        /// The controls list shows the keys in force (key bindings screen) and whether TAB holds or toggles.
        void PaintKeys()
        {
            _bindVer = Rebinds.Version; _tabToggle = Desync.Flow.A11y.TabToggle;
            _keyT[0].SetText(Rebinds.MoveLabel());
            _keyT[2].SetText(Rebinds.Label(Rebinds.Find("flip"), false));
            _keyT[3].SetText(Rebinds.Label(Rebinds.Find("hack"), false));
            _keyT[5].SetText(Rebinds.Label(Rebinds.Find("restart"), false));
            _keyT[6].SetText(Rebinds.Label(Rebinds.Find("pause"), false));
            _keyT[7].SetText(Rebinds.Label(Rebinds.Find("map"), false));
            _capT[7].SetText(_tabToggle ? "MAP / GOALS (TOGGLE)" : "MAP / GOALS (HOLD)");
        }

        public override void Tick(float dt)
        {
            if (_bindVer != Rebinds.Version || _tabToggle != Desync.Flow.A11y.TabToggle) PaintKeys();
        }

        public override void Hide() { UIKit.Kill(ref _pulse); UIKit.Kill(ref _pop); base.Hide(); }
    }
}
