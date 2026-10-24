using System;
using System.Collections.Generic;
using System.Text;
using Desync.Content;
using Desync.Flow;
using PrimeTween;
using TMPro;
using UnityEngine;

namespace Desync.UI
{
    /// End of a BREACH run (RunEvents.RunEnded / RunPhase.Over): fragments earned, nodes reached, grades row, time, score and
    /// the share text. Lines reveal over ~1 s (any key skips). [R] new run (same mode), [SPACE] title (QuitRequested),
    /// [C] copy (CopyRequested).
    public sealed class RunResultsScreen : UIPanel
    {
        const int Lines = 5;
        internal const float LineGap = 0.18f, Count = 0.5f, End = 1.4f, InputGuard = 0.4f;

        GlitchLabel _head; TMP_Text _sub, _shareLine, _copyLabel;
        TMP_Text[] _value; CanvasGroup[] _line;
        RectTransform _panel; Tween _pop;
        string _share; int _frags, _shownFrame = -1, _fragShown = -1;
        float _t, _copiedT; bool _acted;
        readonly StringBuilder _sb = new StringBuilder(128);

        internal string ShareText => _share;
        internal bool Done => _t >= End;

        protected override void Create()
        {
            var dim = UIKit.Img(Root, "Dim", new Color(0f, 0f, 0.03f, 0.86f)); UIKit.Stretch(dim.rectTransform); dim.raycastTarget = true;
            var panel = UIKit.Box(Root, "Panel", new Color(UIKit.Magenta.r, UIKit.Magenta.g, UIKit.Magenta.b, 0.8f), 0.95f);
            _panel = UIKit.Place(panel.rectTransform, Anc.C, Vector2.zero, new Vector2(1300, 900));
            _head = GlitchLabel.Create(panel.transform, "Head", Anc.T, new Vector2(0, -30), new Vector2(1200, 110), UIKit.Title, 78, UIKit.Paper, TextAlignmentOptions.Center, 3f, true, 8f);
            _sub = UIKit.Text(panel.transform, "Sub", UIKit.Hud, 26, UIKit.Dim, TextAlignmentOptions.Center); _sub.characterSpacing = 6f;
            UIKit.Place(_sub.rectTransform, Anc.T, new Vector2(0, -140), new Vector2(1200, 34));

            string[] labels = { "FRAGMENTS EARNED", "NODES REACHED", "GRADES", "RUN TIME", "RUN SCORE" };
            _value = new TMP_Text[Lines]; _line = new CanvasGroup[Lines];
            for (int i = 0; i < Lines; i++)
            {
                var rt = UIKit.Place(UIKit.Obj(panel.transform, "Line" + i), Anc.T, new Vector2(0, -210 - i * 82), new Vector2(1040, 70));
                _line[i] = rt.gameObject.AddComponent<CanvasGroup>();
                var l = UIKit.Text(rt, "Label", UIKit.Hud, 32, UIKit.Dim, TextAlignmentOptions.Left, labels[i]); l.characterSpacing = 6f;
                UIKit.Stretch(l.rectTransform);
                _value[i] = UIKit.Text(rt, "Value", i == 0 ? UIKit.Title : UIKit.Hud, i == 0 ? 54 : 44, UIKit.Paper, TextAlignmentOptions.Right); _value[i].fontStyle = FontStyles.Bold;
                UIKit.Stretch(_value[i].rectTransform);
            }
            _value[0].color = UIKit.MedalColor(3);

            _shareLine = UIKit.Text(panel.transform, "ShareLine", UIKit.Term, 32, UIKit.Green, TextAlignmentOptions.Center); _shareLine.richText = false;
            UIKit.Place(_shareLine.rectTransform, Anc.T, new Vector2(0, -640), new Vector2(1200, 44));

            UIButton.Create(panel.transform, "RunNew", "[R] NEW RUN", Anc.B, new Vector2(-400, 50), new Vector2(360, 80), 30, UIKit.Magenta, () => Button(0), UIKit.Hud);
            UIButton.Create(panel.transform, "RunTitle", "[SPACE] TITLE", Anc.B, new Vector2(0, 50), new Vector2(360, 80), 30, UIKit.Cyan, () => Button(1), UIKit.Hud);
            var copy = UIButton.Create(panel.transform, "RunCopy", "[C] COPY", Anc.B, new Vector2(400, 50), new Vector2(360, 80), 30, UIKit.Cyan, () => Button(2), UIKit.Hud);
            _copyLabel = copy.Label;
        }

        public override void Show()
        {
            base.Show();
            _t = 0f; _acted = false; _copiedT = 0f; _shownFrame = Time.frameCount; _fragShown = -1;
            _head.Reveal(RunState.Won ? "BREACH COMPLETE" : "RUN OVER", 0.5f);
            _head.SetColor(RunState.Won ? UIKit.Green : UIKit.Paper);
            int mode = (int)RunState.Mode;
            _sub.SetText((mode >= 0 && mode < RunView.ModeNames.Length ? RunView.ModeNames[mode] : "") + (DeepRun.Level > 0 ? " · DEEP LAYER " + DeepRun.Level : "") + " · SEED " + RunState.Seed);
            _frags = RunState.Fragments;

            var cur = RunView.Node(RunState.NodeId);
            _value[1].SetText(RunView.Path.Count + (cur != null ? "  · " + RunView.NodeLabel(cur) : ""));

            _sb.Clear();
            var plain = new StringBuilder(32);
            foreach (char g in RunState.Grades)
            {
                if (plain.Length > 0) { plain.Append(' '); _sb.Append(' '); }
                plain.Append(g);
                _sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(UIKit.GradeColor(g))).Append('>').Append(g).Append("</color>");
            }
            _value[2].SetText(_sb.Length > 0 ? _sb.ToString() : "-");
            _value[3].SetText(RoomReports.Clock(RunState.RunTime));
            _value[4].SetText(RunState.RunScore.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _share = RoomReports.ShareText(DateTime.UtcNow, plain.ToString(), RunState.RunTime, RunView.BestChain);
            _shareLine.SetText(_share);
            _copyLabel.SetText("[C] COPY");
            UIKit.Pop(ref _pop, _panel, 0.9f, 0.25f);
            Apply(0f);
        }

        public override void Hide() { UIKit.Kill(ref _pop); base.Hide(); }

        void Apply(float t)
        {
            for (int i = 0; i < Lines; i++) _line[i].alpha = Mathf.Clamp01((t - i * LineGap) / 0.12f);
            int f = Mathf.RoundToInt(_frags * Mathf.Clamp01(t / Count));
            if (f != _fragShown) { _fragShown = f; _value[0].SetText("+{0:0}", f); }
        }

        void Button(int kind)
        {
            if (_acted) return;
            if (_t < End) { _t = End; Apply(End); return; }   // a click during the reveal only skips
            Act(kind);
        }

        void Act(int kind)
        {
            switch (kind)
            {
                case 0:   // a Deep Layer run restarts at the same level and picks
                    _acted = true;
                    if (DeepRun.Level > 0) { int level = DeepRun.Level; var picks = new List<MutatorPick>(DeepRun.Picks); RunView.ResetRun(); DeepRun.StartRequested?.Invoke(level, picks); }
                    else RunView.Start(RunState.Mode);
                    break;
                case 1: _acted = true; GameEvents.QuitRequested?.Invoke(); break;
                default: GameEvents.CopyRequested?.Invoke(_share ?? ""); _copyLabel.SetText("[C] COPIED"); _copiedT = 1.5f; break;
            }
        }

        public override void Tick(float dt)
        {
            if (_copiedT > 0f) { _copiedT -= dt; if (_copiedT <= 0f) _copyLabel.SetText("[C] COPY"); }
            if (_acted || Time.frameCount == _shownFrame) return;
            if (_t < End)
            {
                if (_t >= InputGuard && UIKit.AnyKey.WasPressedThisFrame()) { _t = End; Apply(End); return; }
                _t = Mathf.Min(End, _t + dt);
                Apply(_t);
                return;
            }
            if (GameInput.Restart.WasPressedThisFrame()) Act(0);
            else if (UIKit.Confirm.WasPressedThisFrame()) Act(1);
            else if (UIKit.CopyKey.WasPressedThisFrame()) Act(2);
        }
    }
}
