using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Ending scene: per-room grades, total time, PLAY AGAIN.
    public sealed class EndingScreen : UIPanel
    {
        const int N = 6;

        GlitchLabel _head;
        TMP_Text _intro, _total;
        readonly TMP_Text[] _letter = new TMP_Text[N], _info = new TMP_Text[N], _best = new TMP_Text[N];
        readonly RectTransform[] _cell = new RectTransform[N];
        readonly Image[] _cellFrame = new Image[N];
        UIButton _again; Tween _pulse;
        float _introChars; int _introShown, _introLen; float _introDelay, _armT;

        protected override void Create()
        {
            var bg = UIKit.Img(Root, "Bg", new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.9f)); UIKit.Stretch(bg.rectTransform); bg.raycastTarget = true;
            var vg = UIKit.Img(Root, "Vignette", new Color(UIKit.Magenta.r, UIKit.Magenta.g, UIKit.Magenta.b, 0.25f), UIKit.Vignette); UIKit.Stretch(vg.rectTransform);

            const float lx = 0f;
            _head = GlitchLabel.Create(Root, "Head", Anc.T, new Vector2(lx, -44), new Vector2(940, 110), UIKit.Title, 84, UIKit.Paper, TextAlignmentOptions.Center, 3f, true, 8f);
            _head.IdleEvery = 3f;
            _intro = UIKit.Text(Root, "Intro", UIKit.Term, 44, UIKit.Green, TextAlignmentOptions.Top);
            _intro.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Place(_intro.rectTransform, Anc.T, new Vector2(lx, -170), new Vector2(920, 130));

            const float w = 300f, h = 220f, g = 20f;
            var cfg = GameConfig.I;
            for (int i = 0; i < N; i++)
            {
                int col = i % 3, row = i / 3;
                var cell = UIKit.Img(Root, "Cell" + i, new Color(UIKit.Panel.r, UIKit.Panel.g, UIKit.Panel.b, 0.85f));
                _cell[i] = UIKit.Place(cell.rectTransform, Anc.T, new Vector2(lx + (col - 1) * (w + g), -330 - row * (h + g)), new Vector2(w, h));
                _cellFrame[i] = UIKit.Img(cell.transform, "Frame", UIKit.Cyan, UIKit.Frame, Image.Type.Sliced); UIKit.Stretch(_cellFrame[i].rectTransform);
                string n = cfg.roomNames != null && i < cfg.roomNames.Length ? cfg.roomNames[i].ToUpperInvariant() : "";
                var code = UIKit.Text(cell.transform, "Code", UIKit.Hud, 24, UIKit.Dim, TextAlignmentOptions.Center, "R" + (i + 1).ToString("00") + " · " + n); code.characterSpacing = 4f;
                UIKit.Place(code.rectTransform, Anc.T, new Vector2(0, -10), new Vector2(w - 10, 30));
                _letter[i] = UIKit.Text(cell.transform, "Grade", UIKit.Title, 112, UIKit.Dim, TextAlignmentOptions.Center, "-"); _letter[i].fontStyle = FontStyles.Bold;
                UIKit.Place(_letter[i].rectTransform, Anc.C, new Vector2(0, 6), new Vector2(w, 130));
                _best[i] = UIKit.Text(cell.transform, "Best", UIKit.Hud, 22, UIKit.Cyan, TextAlignmentOptions.Center);
                UIKit.Place(_best[i].rectTransform, Anc.B, new Vector2(0, 38), new Vector2(w - 10, 28));
                _info[i] = UIKit.Text(cell.transform, "Info", UIKit.Hud, 22, UIKit.Paper, TextAlignmentOptions.Center);
                UIKit.Place(_info[i].rectTransform, Anc.B, new Vector2(0, 10), new Vector2(w - 10, 28));
            }
            _total = UIKit.Text(Root, "Total", UIKit.Hud, 34, UIKit.Cyan, TextAlignmentOptions.Center, "TOTAL --:--.-"); _total.fontStyle = FontStyles.Bold; _total.characterSpacing = 4f;
            UIKit.Place(_total.rectTransform, Anc.T, new Vector2(lx, -330 - 2 * h - g - 16), new Vector2(940, 44));

            _again = UIButton.Create(Root, "PlayAgain", "PLAY AGAIN", Anc.B, new Vector2(0, 60), new Vector2(560, 96), 52, UIKit.Magenta, () => GameEvents.StartRequested?.Invoke(0));
        }

        public override void Show()
        {
            base.Show();
            var cfg = GameConfig.I;
            _head.Reveal("RUN REPORT", 0.7f);
            string line = cfg.introLines != null && cfg.introLines.Length > 2 ? cfg.introLines[2] : "";
            _intro.SetText(UIKit.Safe(line, UIKit.Term)); _intro.maxVisibleCharacters = 0; _introLen = line.Length; _introChars = 0; _introShown = 0; _introDelay = 0.8f;
            _armT = 1.5f;   // pad PLAY AGAIN stays disarmed so a button still being mashed at the finale can't skip the report

            float total = 0f; int played = 0, traces = 0, flips = 0;
            var res = GameState.Results;
            for (int i = 0; i < N; i++)
            {
                var r = res != null && i < res.Length ? res[i] : default;
                bool ok = r.grade != 0;
                char g = ok ? r.grade : '-';
                _letter[i].SetText(UIKit.GradeStr(g)); _letter[i].color = UIKit.GradeColor(g);
                var fc = ok ? UIKit.GradeColor(g) : UIKit.Dim * 0.5f;
                _cellFrame[i].color = new Color(fc.r, fc.g, fc.b, ok ? 0.9f : 0.5f);
                if (ok)
                {
                    int tenth = (int)(r.time * 10f); played++; total += r.time; traces += r.traces; flips += r.flips;
                    _info[i].SetText("{0:0}.{1:0}s · {2:0} TRACE", tenth / 10, tenth % 10, r.traces);
                }
                else _info[i].SetText("NOT PLAYED");
                _best[i].SetText(UIKit.BestLabel(i) ?? "");   // saved best clear, shown even for rooms skipped this run
                _cell[i].localScale = Vector3.zero;
                Tween.Scale(_cell[i], 0f, 1f, 0.35f, Ease.OutBack, startDelay: 0.3f + i * 0.12f, useUnscaledTime: true);
            }
            int tt = (int)(total * 10f);
            _total.SetText("TOTAL  {0:0}:{1:00}.{2:0}  ·  {3:0} TRACES  ·  {4:0} FLIPS", tt / 600, (tt / 10) % 60, tt % 10, traces, flips);

            UIKit.Kill(ref _pulse);
            _again.transform.localScale = Vector3.one;
            _pulse = Tween.Scale(_again.transform, 1f, 1.04f, 0.6f, Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo, useUnscaledTime: true);

            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public override void Hide() { UIKit.Kill(ref _pulse); base.Hide(); }

        public override void Tick(float dt)
        {
            if (_armT > 0f) _armT -= dt;
            else if (UIKit.PadConfirm) { GameEvents.StartRequested?.Invoke(0); return; }   // gamepad PLAY AGAIN (Start / South)
            if (_introDelay > 0f) _introDelay -= dt;
            else
            {
                _introChars += dt * 55f;
                int w = Mathf.Min((int)_introChars, _introLen);
                if (w != _introShown) { _introShown = w; _intro.maxVisibleCharacters = w; }
            }
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }
    }
}
