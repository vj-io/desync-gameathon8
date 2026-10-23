using System.Globalization;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Room results (GameEvents.RoomReportReady), tally style: EVENTS -> COMBO PEAK -> TIME BONUS -> TOTAL count up over
    /// 250 ms each (tick pitch +1 semitone per line), MEDAL reveals, the GRADE stamps (80 ms hit-stop), then best/delta and
    /// NEXT TARGET. The whole tally is 2.6 s. Any key jumps to the end state; the next press acts:
    /// [R] RestartRequested, [SPACE] ContinueRequested, [C] CopyRequested(shareText). Time-driven: Apply(t) is idempotent.
    public sealed class ResultsScreen : UIPanel
    {
        sealed class Row { public RectTransform Rt; public TMP_Text Label, Value; public CanvasGroup G; public float Start; public int Last, Ticks; }

        Row[] _rows;
        TMP_Text _head, _sub, _grade, _time, _score, _next, _skip, _copyLabel;
        RectTransform _panel, _gradeRt; CanvasGroup _foot; Image _gradeFlash; Tween _pop;
        RoomReport _r; string _medalText;
        float _t, _copiedT; bool _acted, _stamped; int _skipFrame = -1;
        AudioSource _sfx; AudioClip _click, _popClip;

        internal const float RowStart = 0.25f, RowGap = 0.32f, Count = 0.25f, Stamp = 1.95f, FootAt = 2.25f, End = 2.6f, InputGuard = 0.3f, HitStop = 0.08f;
        const int Lines = 5;   // EVENTS, COMBO PEAK, TIME BONUS, TOTAL, MEDAL (the GRADE stamp is the 6th beat)

        internal bool Done => _t >= End;
        internal bool Acted => _acted;
        internal string ValueText(int row) => _rows[row].Value.text;

        public void Init(AudioSource sfx) { _sfx = sfx; }

        protected override void Create()
        {
            var cyan = UIKit.Cyan; var paper = UIKit.Paper; var dim = UIKit.Dim;
            var dimmer = UIKit.Img(Root, "Dim", new Color(0f, 0f, 0.03f, 0.62f)); UIKit.Stretch(dimmer.rectTransform);
            var panel = UIKit.Box(Root, "Panel", new Color(cyan.r, cyan.g, cyan.b, 0.8f), 0.94f);
            _panel = UIKit.Place(panel.rectTransform, Anc.C, Vector2.zero, new Vector2(1240, 860));

            _head = UIKit.Text(panel.transform, "Head", UIKit.Title, 48, paper, TextAlignmentOptions.Center); _head.fontStyle = FontStyles.Bold; _head.characterSpacing = 6f;
            UIKit.Place(_head.rectTransform, Anc.T, new Vector2(0, -28), new Vector2(1160, 64));
            _sub = UIKit.Text(panel.transform, "Sub", UIKit.Hud, 26, dim, TextAlignmentOptions.Center); _sub.characterSpacing = 6f;
            UIKit.Place(_sub.rectTransform, Anc.T, new Vector2(0, -96), new Vector2(1160, 34));

            string[] labels = { "EVENTS", "COMBO PEAK", "TIME BONUS", "TOTAL", "MEDAL" };
            float[] ys = { -160, -226, -292, -372, -452 };
            _rows = new Row[Lines];
            for (int i = 0; i < Lines; i++)
            {
                bool total = i == 3;
                var rt = UIKit.Place(UIKit.Obj(panel.transform, "Line" + i), Anc.TL, new Vector2(70, ys[i]), new Vector2(640, total ? 66 : 56));
                rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition += new Vector2(320, -(total ? 33 : 28));   // centre pivot: pops scale in place
                var row = new Row { Rt = rt, G = rt.gameObject.AddComponent<CanvasGroup>(), Start = RowStart + i * RowGap, Last = int.MinValue };
                row.Label = UIKit.Text(rt, "Label", UIKit.Hud, total ? 34 : 30, total ? paper : dim, TextAlignmentOptions.Left, labels[i]); row.Label.characterSpacing = 6f;
                UIKit.Stretch(row.Label.rectTransform);
                row.Value = UIKit.Text(rt, "Value", total ? UIKit.Title : UIKit.Hud, total ? 54 : 42, paper, TextAlignmentOptions.Right); row.Value.fontStyle = FontStyles.Bold;
                UIKit.Stretch(row.Value.rectTransform);
                _rows[i] = row;
            }
            var sep = UIKit.Img(panel.transform, "Rule", new Color(cyan.r, cyan.g, cyan.b, 0.5f)); UIKit.Place(sep.rectTransform, Anc.TL, new Vector2(70, -356), new Vector2(640, 2));

            // grade stamp (right)
            var box = UIKit.Box(panel.transform, "GradeBox", new Color(UIKit.Magenta.r, UIKit.Magenta.g, UIKit.Magenta.b, 0.8f), 0.7f);
            UIKit.Place(box.rectTransform, Anc.TR, new Vector2(-90, -150), new Vector2(380, 380));
            _grade = UIKit.Text(box.transform, "Grade", UIKit.Title, 300, paper, TextAlignmentOptions.Center); _grade.fontStyle = FontStyles.Bold;
            _gradeRt = UIKit.Stretch(_grade.rectTransform);
            _gradeFlash = UIKit.Img(box.transform, "Flash", new Color(1, 1, 1, 0f)); UIKit.Stretch(_gradeFlash.rectTransform);

            // best / delta / next target
            var foot = UIKit.Obj(panel.transform, "Foot"); UIKit.Place(foot, Anc.TL, new Vector2(70, -556), new Vector2(1100, 150));
            _foot = foot.gameObject.AddComponent<CanvasGroup>();
            _time = UIKit.Text(foot, "BestTime", UIKit.Hud, 30, paper, TextAlignmentOptions.Left); UIKit.Place(_time.rectTransform, Anc.TL, Vector2.zero, new Vector2(1100, 40));
            _score = UIKit.Text(foot, "BestScore", UIKit.Hud, 30, paper, TextAlignmentOptions.Left); UIKit.Place(_score.rectTransform, Anc.TL, new Vector2(0, -46), new Vector2(1100, 40));
            _next = UIKit.Text(foot, "NextTarget", UIKit.Hud, 30, cyan, TextAlignmentOptions.Left); _next.fontStyle = FontStyles.Bold;
            UIKit.Place(_next.rectTransform, Anc.TL, new Vector2(0, -92), new Vector2(1100, 40));

            // keys
            UIButton.Create(panel.transform, "ResultsRetry", "[R] RETRY", Anc.B, new Vector2(-390, 40), new Vector2(340, 80), 32, UIKit.Magenta, () => Button(0), UIKit.Hud);
            UIButton.Create(panel.transform, "ResultsContinue", "[SPACE] CONTINUE", Anc.B, new Vector2(0, 40), new Vector2(400, 80), 32, cyan, () => Button(1), UIKit.Hud);
            var copy = UIButton.Create(panel.transform, "ResultsCopy", "[C] COPY", Anc.B, new Vector2(390, 40), new Vector2(340, 80), 32, cyan, () => Button(2), UIKit.Hud);
            _copyLabel = copy.Label;
            _skip = UIKit.Text(panel.transform, "Skip", UIKit.Hud, 22, dim, TextAlignmentOptions.Center, "ANY KEY SKIPS"); _skip.characterSpacing = 8f;
            UIKit.Place(_skip.rectTransform, Anc.B, new Vector2(0, 128), new Vector2(600, 30));
        }

        public void Show(RoomReport r)
        {
            base.Show();
            _r = r; _t = 0f; _acted = false; _stamped = false; _copiedT = 0f; _skipFrame = -1;
            if (!_click) _click = AssetLib.Get<AudioClip>("sfx.ui.click");
            if (!_popClip) _popClip = AssetLib.Get<AudioClip>("sfx.ui.pop");
            var cfg = GameConfig.I; var inv = CultureInfo.InvariantCulture;
            int ri = Mathf.Clamp(r.room, 0, 5);
            string name = cfg.roomNames != null && ri < cfg.roomNames.Length ? cfg.roomNames[ri].ToUpperInvariant() : "";
            _head.SetText("R" + (ri + 1).ToString("00") + " · " + name + " CLEARED");
            _sub.SetText("TIME " + UIKit.ClockShort(r.time));
            int medal = (int)r.medal;
            _medalText = (medal >= 1 && medal <= 4 ? UIKit.MedalNames[medal] : "NONE");
            _rows[4].Value.color = UIKit.MedalColor(medal);
            for (int i = 0; i < Lines; i++) { _rows[i].Last = int.MinValue; _rows[i].Ticks = 0; }
            char g = r.grade == 0 ? 'C' : r.grade;
            _grade.SetText(UIKit.GradeStr(g)); _grade.color = UIKit.GradeColor(g);

            // best + delta (strings built once here)
            if (r.prevBestTime > 0f)
            {
                float d = r.time - r.prevBestTime;
                _time.SetText("TIME  " + UIKit.ClockShort(r.time) + "   BEST " + UIKit.ClockShort(r.prevBestTime) + "   " +
                              (d < 0f ? "<color=#" + Hex(UIKit.Green) + ">-" + (-d).ToString("0.00", inv) + " s NEW BEST</color>"
                                      : "<color=#" + Hex(UIKit.Dim) + ">+" + d.ToString("0.00", inv) + " s</color>"));
            }
            else _time.SetText("TIME  " + UIKit.ClockShort(r.time) + "   <color=#" + Hex(UIKit.Green) + ">FIRST CLEAR · NEW BEST</color>");
            if (r.prevBestScore > 0)
            {
                int d = r.total - r.prevBestScore;
                _score.SetText("SCORE " + r.total.ToString(inv) + "   BEST " + r.prevBestScore.ToString(inv) + "   " +
                               (d > 0 ? "<color=#" + Hex(UIKit.Green) + ">+" + d.ToString(inv) + " NEW BEST</color>"
                                      : "<color=#" + Hex(UIKit.Dim) + ">" + d.ToString(inv) + "</color>"));
            }
            else _score.SetText("SCORE " + r.total.ToString(inv));
            _next.SetText("NEXT TARGET  " + UIKit.Safe(string.IsNullOrEmpty(r.nextTarget) ? "ALL TARGETS MET" : r.nextTarget.ToUpperInvariant(), UIKit.Hud));
            _copyLabel.SetText("[C] COPY");

            UIKit.Pop(ref _pop, _panel, 0.9f, 0.2f);
            Apply(0f, false);
        }

        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        public override void Hide() { UIKit.Kill(ref _pop); base.Hide(); }

        public override void Tick(float dt)
        {
            if (_copiedT > 0f) { _copiedT -= dt; if (_copiedT <= 0f) _copyLabel.SetText("[C] COPY"); }
            if (_acted || GameState.Paused) return;
            if (_t < End)
            {
                if (_t >= InputGuard && UIKit.AnyKey.WasPressedThisFrame()) { Skip(); return; }
                float prev = _t;
                _t = Mathf.Min(End, _t + dt);
                if (prev < Stamp && _t >= Stamp) GameEvents.HitStopRequest?.Invoke(HitStop);
                Apply(_t, true);
                return;
            }
            if (GameInput.Restart.WasPressedThisFrame()) Act(0);
            else if (GameInput.Flip.WasPressedThisFrame()) Act(1);
            else if (UIKit.CopyKey.WasPressedThisFrame()) Act(2);
        }

        /// Any key during the tally: jump to the end state (one pop, no hit-stop). The next press acts.
        internal void Skip()
        {
            if (_t >= End) return;
            _t = End; _skipFrame = Time.frameCount; _stamped = true;
            Apply(End, false);
            Play(_popClip, 5);
        }

        void Button(int kind)
        {
            if (_acted) return;
            if (_t < End || Time.frameCount == _skipFrame) { Skip(); return; }   // the click that skipped must not also act
            Act(kind);
        }

        void Act(int kind)
        {
            switch (kind)
            {
                case 0: _acted = true; GameEvents.RestartRequested?.Invoke(); break;
                case 1: _acted = true; GameEvents.ContinueRequested?.Invoke(); break;
                default:
                    GameEvents.CopyRequested?.Invoke(_r != null ? _r.shareText : "");
                    _copyLabel.SetText("[C] COPIED"); _copiedT = 1.5f;
                    break;
            }
            Play(_click, 0);
        }

        /// Puts every element in its state at tally time t; sound = play the tick/pop/stamp cues crossed since last frame.
        void Apply(float t, bool sound)
        {
            var r = _r;
            for (int i = 0; i < Lines; i++)
            {
                var row = _rows[i];
                float age = t - row.Start;
                if (age < 0f) { row.G.alpha = 0f; continue; }
                row.G.alpha = 1f;
                float s = 1f + 0.15f * (1f - Mathf.Clamp01(age / 0.1f));
                row.Rt.localScale = new Vector3(s, s, 1f);
                float k = Mathf.Clamp01(age / Count);
                int v;
                switch (i)
                {
                    case 0: v = Mathf.RoundToInt(r.eventsScore * k); if (v != row.Last) row.Value.SetText("+{0:0}", v); break;
                    case 1: v = Mathf.RoundToInt((1f + (Mathf.Max(1f, r.peakMultiplier) - 1f) * k) * 100f); if (v != row.Last) row.Value.SetText("x{0:0.00}", v / 100f); break;
                    case 2: v = Mathf.RoundToInt(r.timeBonus * k); if (v != row.Last) row.Value.SetText("+{0:0}", v); break;
                    case 3: v = Mathf.RoundToInt(r.total * k); if (v != row.Last) row.Value.SetText("{0:0}", v); break;
                    default: v = 1; if (v != row.Last) row.Value.SetText(_medalText); break;
                }
                row.Last = v;
                if (!sound) { row.Ticks = 5; continue; }
                if (i == 4) { if (row.Ticks == 0) { row.Ticks = 5; Play(_popClip, i); } continue; }
                int ticks = Mathf.Min(5, (int)(k * 5f + 0.0001f));
                if (ticks > row.Ticks) { row.Ticks = ticks; Play(_click, i); }   // ticks every 50 ms while the line counts
            }

            float st = t - Stamp;
            _grade.enabled = st >= 0f;
            if (st >= 0f)
            {
                float e = 1f - Mathf.Clamp01(st / 0.12f);
                _gradeRt.localScale = Vector3.one * (1f + 1.4f * e * e);
                var fc = _gradeFlash.color; fc.a = UIKit.Flashes ? 0.6f * Mathf.Clamp01(1f - st / 0.35f) : 0f; _gradeFlash.color = fc;
                if (sound && !_stamped)
                {
                    _stamped = true; Play(_popClip, 5);
                    if (UIKit.Shake) GameEvents.ShakeRequest?.Invoke(0.25f);
                }
            }
            else { var fc = _gradeFlash.color; fc.a = 0f; _gradeFlash.color = fc; }
            _foot.alpha = Mathf.Clamp01((t - FootAt) / 0.2f);
            _skip.enabled = t < End;
        }

        void Play(AudioClip c, int line)
        {
            if (!_sfx || !c) return;
            _sfx.pitch = Mathf.Pow(2f, line / 12f);   // +1 semitone per line
            _sfx.PlayOneShot(c, 0.7f);
        }
    }
}
