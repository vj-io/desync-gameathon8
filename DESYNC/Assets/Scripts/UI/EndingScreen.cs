using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Ending scene: per-room grades, total time, scrolling credits (Resources/Credits.txt), PLAY AGAIN.
    public sealed class EndingScreen : UIPanel
    {
        const int N = 6;

        GlitchLabel _head;
        TMP_Text _intro, _total, _credits;
        readonly TMP_Text[] _letter = new TMP_Text[N], _info = new TMP_Text[N];
        readonly RectTransform[] _cell = new RectTransform[N];
        readonly Image[] _cellFrame = new Image[N];
        RectTransform _creditsRt, _view; float _scroll, _creditsH; UIButton _again; Tween _pulse;
        float _introChars; int _introShown, _introLen; float _introDelay;
        const float ScrollSpeed = 42f;

        protected override void Create()
        {
            var bg = UIKit.Img(Root, "Bg", new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.9f)); UIKit.Stretch(bg.rectTransform); bg.raycastTarget = true;
            var vg = UIKit.Img(Root, "Vignette", new Color(UIKit.Magenta.r, UIKit.Magenta.g, UIKit.Magenta.b, 0.25f), UIKit.Vignette); UIKit.Stretch(vg.rectTransform);

            const float lx = -470f;
            _head = GlitchLabel.Create(Root, "Head", Anc.T, new Vector2(lx, -44), new Vector2(940, 110), UIKit.Title, 84, UIKit.Paper, TextAlignmentOptions.Center, 3f, true, 8f);
            _head.IdleEvery = 3f;
            _intro = UIKit.Text(Root, "Intro", UIKit.Term, 44, UIKit.Green, TextAlignmentOptions.Top);
            _intro.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Place(_intro.rectTransform, Anc.T, new Vector2(lx, -170), new Vector2(920, 130));

            const float w = 300f, h = 190f, g = 20f;
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
                UIKit.Place(_letter[i].rectTransform, Anc.C, new Vector2(0, -2), new Vector2(w, 130));
                _info[i] = UIKit.Text(cell.transform, "Info", UIKit.Hud, 22, UIKit.Paper, TextAlignmentOptions.Center);
                UIKit.Place(_info[i].rectTransform, Anc.B, new Vector2(0, 10), new Vector2(w - 10, 28));
            }
            _total = UIKit.Text(Root, "Total", UIKit.Hud, 34, UIKit.Cyan, TextAlignmentOptions.Center, "TOTAL --:--.-"); _total.fontStyle = FontStyles.Bold; _total.characterSpacing = 4f;
            UIKit.Place(_total.rectTransform, Anc.T, new Vector2(lx, -330 - 2 * h - g - 16), new Vector2(940, 44));

            // ---- credits window (right)
            const float cx = 500f;
            var ctitle = UIKit.Text(Root, "CreditsHead", UIKit.Title, 34, UIKit.Cyan, TextAlignmentOptions.Center, "CREDITS"); ctitle.fontStyle = FontStyles.Bold; ctitle.characterSpacing = 10f;
            UIKit.Place(ctitle.rectTransform, Anc.T, new Vector2(cx, -52), new Vector2(760, 50));
            var box = UIKit.Box(Root, "CreditsBox", new Color(UIKit.Cyan.r, UIKit.Cyan.g, UIKit.Cyan.b, 0.6f), 0.7f);
            UIKit.Place(box.rectTransform, Anc.T, new Vector2(cx, -112), new Vector2(780, 700));
            _view = UIKit.Obj(box.transform, "View"); UIKit.Stretch(_view, 14, 14, 14, 14); _view.gameObject.AddComponent<RectMask2D>();
            _credits = UIKit.Text(_view, "Text", UIKit.Hud, 24, UIKit.Paper, TextAlignmentOptions.TopLeft);
            _credits.textWrappingMode = TextWrappingModes.Normal;
            _creditsRt = _credits.rectTransform; _creditsRt.anchorMin = new Vector2(0, 1); _creditsRt.anchorMax = new Vector2(1, 1); _creditsRt.pivot = new Vector2(0.5f, 1f);
            _creditsRt.sizeDelta = new Vector2(0, 100);

            _again = UIButton.Create(Root, "PlayAgain", "PLAY AGAIN", Anc.B, new Vector2(cx, 60), new Vector2(560, 96), 52, UIKit.Magenta, () => GameEvents.StartRequested?.Invoke(0));
        }

        public override void Show()
        {
            base.Show();
            var cfg = GameConfig.I;
            _head.Reveal("RUN REPORT", 0.7f);
            string line = cfg.introLines != null && cfg.introLines.Length > 2 ? cfg.introLines[2] : "";
            _intro.SetText(UIKit.Safe(line, UIKit.Term)); _intro.maxVisibleCharacters = 0; _introLen = line.Length; _introChars = 0; _introShown = 0; _introDelay = 0.8f;

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
                _cell[i].localScale = Vector3.zero;
                Tween.Scale(_cell[i], 0f, 1f, 0.35f, Ease.OutBack, startDelay: 0.3f + i * 0.12f, useUnscaledTime: true);
            }
            int tt = (int)(total * 10f);
            _total.SetText("TOTAL  {0:0}:{1:00}.{2:0}  ·  {3:0} TRACES  ·  {4:0} FLIPS", tt / 600, (tt / 10) % 60, tt % 10, traces, flips);

            var asset = Resources.Load<TextAsset>("Credits");
            string txt = asset ? asset.text : "Credits are compiled from credits/*.md at build time.";
            _credits.SetText(UIKit.Safe(txt, UIKit.Hud));
            _credits.ForceMeshUpdate();
            _creditsH = _credits.preferredHeight;
            _creditsRt.sizeDelta = new Vector2(0, _creditsH);
            _scroll = -((RectTransform)_view).rect.height * 0.85f;
            _creditsRt.anchoredPosition = new Vector2(0, _scroll);

            UIKit.Kill(ref _pulse);
            _again.transform.localScale = Vector3.one;
            _pulse = Tween.Scale(_again.transform, 1f, 1.04f, 0.6f, Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo, useUnscaledTime: true);

            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public override void Hide() { UIKit.Kill(ref _pulse); base.Hide(); }

        public override void Tick(float dt)
        {
            if (_introDelay > 0f) _introDelay -= dt;
            else
            {
                _introChars += dt * 55f;
                int w = Mathf.Min((int)_introChars, _introLen);
                if (w != _introShown) { _introShown = w; _intro.maxVisibleCharacters = w; }
            }
            _scroll += ScrollSpeed * dt;
            float viewH = _view.rect.height;
            if (_scroll > _creditsH + viewH * 0.1f) _scroll = -viewH * 0.85f;
            _creditsRt.anchoredPosition = new Vector2(0, _scroll);
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }
    }
}
