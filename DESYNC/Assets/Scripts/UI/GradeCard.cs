using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Room-clear card: S/A/B/C slot-machine spin that lands on the grade, then time / traces / flips / best combo.
    public sealed class GradeCard : UIPanel
    {
        TMP_Text _head, _time, _traces, _flips, _combo;
        GlitchLabel _letter; RectTransform _panel, _letterRt; Image _flash;
        RectTransform[] _rows; Tween _pop, _letterTw, _flashTw;
        char _final; float _t, _swapT; bool _spinning; int _spinIdx; float _life;
        const string Letters = "SABC";
        const float SpinTime = 0.95f, Life = 6f;

        protected override void Create()
        {
            Group.blocksRaycasts = false;
            var dim = UIKit.Img(Root, "Dim", new Color(0f, 0f, 0.03f, 0.55f)); UIKit.Stretch(dim.rectTransform);
            var panel = UIKit.Box(Root, "Panel", new Color(UIKit.Cyan.r, UIKit.Cyan.g, UIKit.Cyan.b, 0.8f), 0.92f);
            _panel = UIKit.Place(panel.rectTransform, Anc.C, Vector2.zero, new Vector2(1180, 640));

            _head = UIKit.Text(panel.transform, "Head", UIKit.Title, 46, UIKit.Paper, TextAlignmentOptions.Center, "ROOM CLEARED"); _head.fontStyle = FontStyles.Bold; _head.characterSpacing = 6f;
            UIKit.Place(_head.rectTransform, Anc.T, new Vector2(0, -34), new Vector2(1100, 64));
            var sub = UIKit.Text(panel.transform, "Sub", UIKit.Hud, 22, UIKit.Dim, TextAlignmentOptions.Center, "CONNECTION STABLE · NEXT SECTOR LOADING"); sub.characterSpacing = 6f;
            UIKit.Place(sub.rectTransform, Anc.T, new Vector2(0, -100), new Vector2(1100, 30));

            var box = UIKit.Box(panel.transform, "LetterBox", new Color(UIKit.Magenta.r, UIKit.Magenta.g, UIKit.Magenta.b, 0.8f), 0.7f);
            _letterRt = UIKit.Place(box.rectTransform, Anc.C, new Vector2(-340, -40), new Vector2(380, 400));
            _letter = GlitchLabel.Create(box.transform, "Letter", Anc.C, Vector2.zero, new Vector2(380, 400), UIKit.Title, 330, UIKit.Paper, TextAlignmentOptions.Center, 6f, true);
            _flash = UIKit.Img(box.transform, "Flash", new Color(1, 1, 1, 0f)); UIKit.Stretch(_flash.rectTransform);

            _rows = new RectTransform[4];
            _time = Row(panel.transform, 0, 0);
            _traces = Row(panel.transform, 1, 1);
            _flips = Row(panel.transform, 2, 2);
            _combo = Row(panel.transform, 3, 3);
        }

        TMP_Text Row(Transform p, int i, int slot)
        {
            var t = UIKit.Text(p, "Row" + i, UIKit.Hud, 46, UIKit.Paper, TextAlignmentOptions.Left);
            _rows[slot] = UIKit.Place(t.rectTransform, Anc.C, new Vector2(190, 90 - i * 92), new Vector2(640, 60));
            _rows[slot].pivot = new Vector2(0.5f, 0.5f);
            return t;
        }

        public void Show(RoomResult r)
        {
            base.Show();
            var cfg = GameConfig.I;
            int ri = Mathf.Clamp(r.room, 0, 5);
            string name = cfg.roomNames != null && ri < cfg.roomNames.Length ? cfg.roomNames[ri].ToUpperInvariant() : "";
            _head.SetText("R" + (ri + 1).ToString("00") + " · " + name + " CLEARED");
            float par = cfg.parTime != null && ri < cfg.parTime.Length ? cfg.parTime[ri] : 0f;
            int parFlips = cfg.parFlips != null && ri < cfg.parFlips.Length ? cfg.parFlips[ri] : 0;
            int tenth = Mathf.Max(0, (int)(r.time * 10f));
            _time.SetText("TIME     {0:0}.{1:0}s / PAR {2:0}s", tenth / 10, tenth % 10, par);
            _time.color = par > 0f && r.time > par ? UIKit.Danger : UIKit.Green;
            _traces.SetText("TRACES   {0:0}", r.traces);
            _traces.color = r.traces > 0 ? UIKit.Danger : UIKit.Green;
            _flips.SetText("FLIPS    {0:0} / PAR {1:0}", r.flips, parFlips);
            _flips.color = parFlips > 0 && r.flips > parFlips ? UIKit.Danger : UIKit.Green;
            _combo.SetText("BEST GHOST  ×{0:0}", r.bestCombo);
            _combo.color = r.bestCombo >= 3 ? UIKit.Magenta : UIKit.Paper;

            _final = r.grade == 0 ? 'C' : r.grade;
            _spinning = true; _t = 0f; _swapT = 0f; _life = Life;
            UIKit.Pop(ref _pop, _panel, 0.85f, 0.3f);
            for (int i = 0; i < _rows.Length; i++)
            {
                _rows[i].localScale = Vector3.zero;
                Tween.Scale(_rows[i], 0f, 1f, 0.3f, Ease.OutBack, startDelay: 0.25f + i * 0.2f, useUnscaledTime: true);
            }
            Swap(Letters[Random.Range(0, Letters.Length)]);
        }

        void Swap(char g)
        {
            _letter.SetText(UIKit.GradeStr(g)); _letter.SetColor(UIKit.GradeColor(g));
        }

        void Land()
        {
            _spinning = false;
            Swap(_final);
            _letter.Glitch(2f, 0.45f);
            UIKit.Pop(ref _letterTw, _letterRt, 1.5f, 0.5f, Ease.OutElastic);
            if (UIKit.Flashes)
            {
                _flash.color = new Color(1f, 1f, 1f, 0.55f);
                UIKit.Kill(ref _flashTw);
                _flashTw = Tween.Alpha(_flash, 0f, 0.4f, Ease.OutQuad, useUnscaledTime: true);
            }
            GameEvents.ShakeRequest?.Invoke(_final == 'S' ? 0.8f : 0.4f);
        }

        public override void Hide() { UIKit.Kill(ref _pop); UIKit.Kill(ref _letterTw); UIKit.Kill(ref _flashTw); _spinning = false; base.Hide(); }

        public override void Tick(float dt)
        {
            _life -= dt;
            if (_life <= 0f) { Hide(); return; }
            if (!_spinning) return;
            _t += dt; _swapT -= dt;
            if (_t >= SpinTime) { Land(); return; }
            if (_swapT <= 0f)
            {
                _swapT = Mathf.Lerp(0.045f, 0.13f, _t / SpinTime);   // decelerating ticker
                _spinIdx = (_spinIdx + 1 + Random.Range(0, 3)) % Letters.Length;
                Swap(Letters[_spinIdx]);
            }
        }
    }
}
