using System;
using System.Globalization;
using System.Text;
using Desync.Flow;
using Desync.Net;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Daily Breach + Weekly Ladder hub (title menu; UIManager.OpenDaily). Left: the UTC day, its seed and fixed mutator pair, whether
    /// today's scored attempt is still open, the scored run's share block ([C] copies it) and what the leaderboard said about it;
    /// the Weekly Ladder's week, seed and best (locked until the first AUDITOR kill). Right: the top 10 of the day's (or the week's)
    /// board and this player's rank, or LEADERBOARD OFFLINE with the local best. One focus ring (START, WEEKLY, BOARD, COPY, BACK):
    /// arrows / WASD / d-pad move it, SPACE / Enter / pad A act, mouse hover moves it and a click acts.
    public sealed class DailyScreen : UIPanel
    {
        const float InputGuard = 0.25f;
        const int Rows = 10, StartIdx = 0, WeeklyIdx = 1, BoardIdx = 2, CopyIdx = 3, BackIdx = 4, Count = 5;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        TMP_Text _date, _seed, _mutNames, _mutDesc, _status, _posted, _share, _weekHead, _weekLine, _boardHead, _rows, _you;
        RectTransform _panel; Tween _pop, _btnTw;
        readonly UIButton[] _btn = new UIButton[Count];
        readonly Image[] _ring = new Image[Count];
        readonly Action[] _act = new Action[Count];
        readonly StringBuilder _sb = new StringBuilder(512);
        string _day, _week, _shareText;
        bool _weekBoard, _scored, _weekOpen, _acted;
        int _focus, _shownFrame = -1, _req;
        float _t, _copiedT;

        internal int Focus => _focus;
        internal bool ShowingWeek => _weekBoard;
        internal string ShareText => _shareText;
        internal string Status => _status.text;
        internal string Posted => _posted.text;
        internal string RowsText => _rows.text;
        internal string YouText => _you.text;
        internal string MutatorText => _mutNames.text;
        internal string WeekText => _weekLine.text;

        protected override void Create()
        {
            var dim = UIKit.Img(Root, "Dim", new Color(0f, 0f, 0.03f, 0.9f)); UIKit.Stretch(dim.rectTransform); dim.raycastTarget = true;
            var panel = UIKit.Box(Root, "Panel", new Color(UIKit.Magenta.r, UIKit.Magenta.g, UIKit.Magenta.b, 0.8f), 0.95f);
            _panel = UIKit.Place(panel.rectTransform, Anc.C, Vector2.zero, new Vector2(1640, 980));
            var p = panel.transform;

            var head = UIKit.Text(p, "Head", UIKit.Title, 54, UIKit.Paper, TextAlignmentOptions.Left, "DAILY BREACH"); head.fontStyle = FontStyles.Bold; head.characterSpacing = 8f;
            UIKit.Place(head.rectTransform, Anc.TL, new Vector2(40, -20), new Vector2(900, 70));
            _date = Label(p, "Date", UIKit.Hud, 28, UIKit.Dim, TextAlignmentOptions.Right, Anc.TR, new Vector2(-40, -36), new Vector2(700, 36));
            Label(p, "Sub", UIKit.Hud, 24, UIKit.Magenta, TextAlignmentOptions.Left, Anc.TL, new Vector2(44, -92), new Vector2(1500, 32))
                .SetText("ACT 1 · 4 ROOMS + AUDITOR · EVERYONE PLAYS THE SAME SEED · ONE SCORED ATTEMPT PER UTC DAY");

            _seed = Label(p, "Seed", UIKit.Hud, 28, UIKit.Paper, TextAlignmentOptions.Left, Anc.TL, new Vector2(44, -150), new Vector2(900, 36));
            Label(p, "MutLabel", UIKit.Hud, 22, UIKit.Dim, TextAlignmentOptions.Left, Anc.TL, new Vector2(44, -200), new Vector2(900, 28)).SetText("TODAY'S MUTATORS");
            _mutNames = Label(p, "Mutators", UIKit.Hud, 32, UIKit.Magenta, TextAlignmentOptions.Left, Anc.TL, new Vector2(44, -230), new Vector2(900, 42));
            _mutDesc = Label(p, "MutDesc", UIKit.Term, 30, UIKit.Paper, TextAlignmentOptions.TopLeft, Anc.TL, new Vector2(44, -276), new Vector2(900, 70));
            _mutDesc.textWrappingMode = TextWrappingModes.Normal;
            _status = Label(p, "Status", UIKit.Hud, 30, UIKit.Green, TextAlignmentOptions.Left, Anc.TL, new Vector2(44, -362), new Vector2(900, 40));
            _posted = Label(p, "Posted", UIKit.Hud, 22, UIKit.Dim, TextAlignmentOptions.Left, Anc.TL, new Vector2(44, -404), new Vector2(900, 30));
            _share = Label(p, "Share", UIKit.Hud, 30, UIKit.Paper, TextAlignmentOptions.TopLeft, Anc.TL, new Vector2(44, -450), new Vector2(900, 290));
            _share.richText = true;

            _weekHead = Label(p, "WeekHead", UIKit.Hud, 30, UIKit.Cyan, TextAlignmentOptions.Left, Anc.BL, new Vector2(44, 184), new Vector2(1500, 40));
            _weekLine = Label(p, "WeekLine", UIKit.Hud, 22, UIKit.Dim, TextAlignmentOptions.Left, Anc.BL, new Vector2(44, 148), new Vector2(1500, 30));

            _boardHead = Label(p, "BoardHead", UIKit.Hud, 26, UIKit.Cyan, TextAlignmentOptions.Left, Anc.TL, new Vector2(1000, -150), new Vector2(600, 36));
            _rows = Label(p, "Rows", UIKit.Term, 34, UIKit.Paper, TextAlignmentOptions.TopLeft, Anc.TL, new Vector2(1000, -196), new Vector2(600, 400));
            _rows.richText = true;
            _you = Label(p, "You", UIKit.Hud, 26, UIKit.Magenta, TextAlignmentOptions.Left, Anc.TL, new Vector2(1000, -610), new Vector2(600, 70));
            _you.textWrappingMode = TextWrappingModes.Normal;

            Reg(StartIdx, UIButton.Create(p, "Start", "START", Anc.BR, new Vector2(-40, 40), new Vector2(380, 84), 40, UIKit.Magenta, Launch, UIKit.Hud), Launch);
            Reg(WeeklyIdx, UIButton.Create(p, "Weekly", "WEEKLY", Anc.BR, new Vector2(-440, 40), new Vector2(340, 84), 32, UIKit.Cyan, LaunchWeekly, UIKit.Hud), LaunchWeekly);
            Reg(BoardIdx, UIButton.Create(p, "Board", "BOARD: DAY", Anc.BR, new Vector2(-800, 40), new Vector2(280, 84), 28, UIKit.Cyan, ToggleBoard, UIKit.Hud), ToggleBoard);
            Reg(CopyIdx, UIButton.Create(p, "Copy", "[C] COPY", Anc.BR, new Vector2(-1100, 40), new Vector2(260, 84), 28, UIKit.Cyan, Copy, UIKit.Hud), Copy);
            Reg(BackIdx, UIButton.Create(p, "Back", "BACK", Anc.BL, new Vector2(40, 40), new Vector2(220, 84), 32, UIKit.Cyan, Close, UIKit.Hud), Close);
        }

        static TMP_Text Label(Transform parent, string name, TMP_FontAsset font, float size, Color c, TextAlignmentOptions align, Anc a, Vector2 pos, Vector2 box)
        {
            var t = UIKit.Text(parent, name, font, size, c, align); t.characterSpacing = 3f; t.richText = false;
            UIKit.Place(t.rectTransform, a, pos, box);
            return t;
        }

        void Reg(int idx, UIButton b, Action act)
        {
            _btn[idx] = b; _act[idx] = act;
            var r = UIKit.Img(b.transform, "Focus", UIKit.Paper, UIKit.Frame, Image.Type.Sliced); UIKit.Stretch(r.rectTransform, -9, -9, -9, -9); r.enabled = false;
            _ring[idx] = r;
            b.Hovered = () => SetFocus(idx);
        }

        public override void Show()
        {
            base.Show();
            _t = 0f; _acted = false; _copiedT = 0f; _shownFrame = Time.frameCount; _weekBoard = false;
            DailyRun.Changed -= OnChanged; DailyRun.Changed += OnChanged;
            Repaint();
            SetFocus(StartIdx);
            UIKit.Pop(ref _pop, _panel, 0.94f, 0.2f);
            UIKit.FreeCursor();
        }

        public override void Hide()
        {
            DailyRun.Changed -= OnChanged;
            UIKit.Kill(ref _pop); UIKit.Kill(ref _btnTw);
            base.Hide();
        }

        void OnChanged() { if (IsOn) Repaint(); }

        // ---------------------------------------------------------------- state
        /// Reads the day, the profile and the last submission, then asks the server for the board shown. Allocates: on show / change.
        internal void Repaint()
        {
            var now = DateTime.UtcNow;
            var save = SaveService.Data;
            _day = LeaderboardScore.Day(now); _week = LeaderboardScore.Week(now);
            uint seed = LeaderboardScore.Seed(_day);
            _date.SetText(_day + " UTC");
            _seed.SetText("SEED " + seed.ToString(Inv));

            var muts = DailyRun.Mutators(seed, RunView.Cat);
            _sb.Clear();
            foreach (var m in muts) _sb.Append(_sb.Length > 0 ? " · " : "").Append(m.name.ToUpperInvariant());
            _mutNames.SetText(UIKit.Safe(_sb.Length > 0 ? _sb.ToString() : "NONE", UIKit.Hud));
            _sb.Clear();
            foreach (var m in muts) _sb.Append(_sb.Length > 0 ? "  " : "").Append(m.desc);
            _mutDesc.SetText(UIKit.Safe(_sb.ToString(), UIKit.Term));

            _scored = DailyRun.Scored(save, _day);
            bool has = save.dailyBest.TryGetValue(_day, out int best);
            _status.SetText(!_scored ? "SCORED ATTEMPT READY" : has ? "TODAY " + best.ToString(Inv) + " · MORE RUNS ARE PRACTICE" : "SCORED ATTEMPT USED · MORE RUNS ARE PRACTICE");
            _status.color = _scored ? UIKit.Paper : UIKit.Green;
            _posted.SetText(PostedLine());
            _shareText = save.settings.TryGetValue(DailyRun.ShareKey, out var s) && s.StartsWith("DESYNC DAILY // " + _day, StringComparison.Ordinal) ? s : null;
            _share.SetText(_shareText != null ? Glyphs(_shareText) : "");

            _weekOpen = DailyRun.WeeklyOpen(save);
            int wbest = DailyRun.WeekBest(save, _week);
            _weekHead.SetText("WEEKLY LADDER · " + _week);
            _weekLine.SetText(_weekOpen ? "FULL 3-ACT RUN · SEED " + LeaderboardScore.Seed(_week).ToString(Inv) + " · " + (wbest > 0 ? "BEST " + wbest.ToString(Inv) : "NO RUN YET THIS WEEK")
                                        : "OPENS WITH THE FIRST AUDITOR KILL");

            Paint(_btn[StartIdx], _scored ? "PRACTICE" : "START", true, UIKit.Magenta);
            Paint(_btn[WeeklyIdx], _weekOpen ? "WEEKLY" : "LOCKED", _weekOpen, UIKit.Cyan);
            Paint(_btn[BoardIdx], _weekBoard ? "BOARD: WEEK" : "BOARD: DAY", true, UIKit.Cyan);
            Paint(_btn[CopyIdx], "[C] COPY", _shareText != null, UIKit.Cyan);
            Fetch();
        }

        static void Paint(UIButton b, string label, bool on, Color c)
        {
            b.Label.SetText(label);
            b.Base = on ? c : new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, 0.45f);
            b.Hot = on ? (c == UIKit.Magenta ? UIKit.Cyan : UIKit.Magenta) : b.Base;
            b.SetSelected(false);
        }

        static string PostedLine()
        {
            switch (DailyRun.SubmitStatus)
            {
                case -1: return "";
                case DailyRun.AssistRun: return "ASSIST RUN · SAVED HERE, NOT POSTED";
                case 200: return "POSTED · RANK #" + DailyRun.SubmitRank.ToString(Inv);
                case 409: return "ALREADY ON TODAY'S BOARD";
                case 422: return "THE BOARD DID NOT ACCEPT THIS SCORE";
                case 0: case 503: return "LEADERBOARD OFFLINE · SAVED HERE";
                default: return "POSTING FAILED (" + DailyRun.SubmitStatus.ToString(Inv) + ") · SAVED HERE";
            }
        }

        /// The share block with each medal square drawn as a coloured dot (the HUD font has no emoji).
        internal static string Glyphs(string share)
        {
            for (int m = 0; m < DailyRun.MedalGlyphs.Length; m++)
                share = share.Replace(DailyRun.MedalGlyphs[m], "<color=#" + ColorUtility.ToHtmlStringRGB(UIKit.MedalColor(m)) + ">●</color>");
            return UIKit.Safe(share, UIKit.Hud);
        }

        void Fetch()
        {
            string board = _weekBoard ? _week : _day;
            int req = ++_req;
            _boardHead.SetText(_weekBoard ? "THIS WEEK'S BOARD" : "TODAY'S BOARD");
            _rows.SetText("LOADING...");
            _you.SetText("");
            Leaderboard.Fetch(board, SaveService.Data.playerId, v => { if (req == _req && IsOn) ShowBoard(v); });
        }

        internal void ShowBoard(BoardView v)
        {
            var save = SaveService.Data;
            int local = _weekBoard ? DailyRun.WeekBest(save, _week) : save.dailyBest.TryGetValue(_day, out int b) ? b : 0;
            if (!v.online)
            {
                _rows.SetText("LEADERBOARD OFFLINE\n" + (local > 0 ? "LOCAL BEST " + local.ToString(Inv) : "NO LOCAL SCORE YET"));
                return;
            }
            _boardHead.SetText((_weekBoard ? "THIS WEEK'S BOARD · " : "TODAY'S BOARD · ") + v.total.ToString(Inv) + (v.total == 1 ? " PLAYER" : " PLAYERS"));
            _sb.Clear();
            for (int i = 0; i < v.top.Count && i < Rows; i++)
            {
                var r = v.top[i];
                bool me = r.rank == v.yourRank;
                if (me) _sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(UIKit.Magenta)).Append('>');
                _sb.Append('#').Append(r.rank.ToString(Inv)).Append("<pos=16%>").Append(r.tag).Append("<pos=62%>").Append(r.score.ToString(Inv));
                if (me) _sb.Append("</color>");
                _sb.Append('\n');
            }
            _rows.SetText(_sb.Length > 0 ? _sb.ToString() : "NO RUNS POSTED YET");
            _you.SetText(v.yourRank > 0 ? "YOU #" + v.yourRank.ToString(Inv) + " · " + v.yourScore.ToString(Inv)
                                        : "NOT ON THIS BOARD YET" + (local > 0 ? " · LOCAL BEST " + local.ToString(Inv) : ""));
        }

        // ---------------------------------------------------------------- actions
        void Launch()
        {
            if (_acted) return;
            _acted = true;
            Hide();
            DailyRun.StartDaily();   // RunEvents.StartRun(Daily): the run UI takes over from here
        }

        void LaunchWeekly()
        {
            if (_acted) return;
            if (!_weekOpen) { UIKit.Pop(ref _btnTw, _btn[WeeklyIdx].transform, 0.9f, 0.2f); return; }
            _acted = true;
            Hide();
            RunView.ResetRun();      // a seeded Begin does not raise RunEvents.StartRun
            DailyRun.StartWeekly();
        }

        void ToggleBoard() { if (_acted) return; _weekBoard = !_weekBoard; Paint(_btn[BoardIdx], _weekBoard ? "BOARD: WEEK" : "BOARD: DAY", true, UIKit.Cyan); Fetch(); }

        void Copy()
        {
            if (_shareText == null) { UIKit.Pop(ref _btnTw, _btn[CopyIdx].transform, 0.9f, 0.2f); return; }
            GameEvents.CopyRequested?.Invoke(_shareText);
            _btn[CopyIdx].Label.SetText("[C] COPIED"); _copiedT = 1.5f;
        }

        void Close() { if (!_acted) Hide(); }

        // ---------------------------------------------------------------- focus + input
        void SetFocus(int f)
        {
            _focus = (f % Count + Count) % Count;
            for (int i = 0; i < Count; i++) if (_ring[i]) _ring[i].enabled = i == _focus;
        }

        public override void Tick(float dt)
        {
            if (UIManager.I && UIManager.I.Current != UIManager.Mode.Title) { Hide(); return; }   // a run or a room took over
            _t += dt;
            UIKit.FreeCursor();
            if (_copiedT > 0f) { _copiedT -= dt; if (_copiedT <= 0f) _btn[CopyIdx].Label.SetText("[C] COPY"); }
            if (_acted || _t < InputGuard || Time.frameCount == _shownFrame) return;   // the key that opened the screen must not also act
            int step = UIKit.NavStep;
            if (step != 0) SetFocus(_focus + step);
            else if (UIKit.Confirm.WasPressedThisFrame()) _act[_focus]?.Invoke();
            else if (UIKit.CopyKey.WasPressedThisFrame()) Copy();
            else if (GameInput.Pause.WasPressedThisFrame()) Close();
        }
    }

    /// "DAILY · SCORED" / "DAILY · PRACTICE" / "WEEKLY LADDER" in the HUD corner while such a run's room is played.
    public sealed class DailyTag : UIPanel
    {
        TMP_Text _label;
        DailyRun.Board _shown;
        bool _ranked;

        protected override void Create()
        {
            _label = UIKit.Text(Root, "Label", UIKit.Hud, 24, UIKit.Magenta, TextAlignmentOptions.Right); _label.characterSpacing = 4f; _label.richText = false;
            UIKit.Place(_label.rectTransform, Anc.BR, new Vector2(-36, 166), new Vector2(420, 32));   // the Deep Layer tag's spot: never both
        }

        public override void Tick(float dt)
        {
            var b = DailyRun.Current;
            bool on = b != DailyRun.Board.None && RunState.Phase == RunPhase.Room && UIManager.I && UIManager.I.Current == UIManager.Mode.Room;
            if (on != IsOn) { if (on) Show(); else Hide(); }
            if (!on || (b == _shown && DailyRun.Ranked == _ranked)) return;
            _shown = b; _ranked = DailyRun.Ranked;
            _label.SetText(b == DailyRun.Board.Weekly ? "WEEKLY LADDER" : _ranked ? "DAILY · SCORED" : "DAILY · PRACTICE");
            _label.color = _ranked ? UIKit.Magenta : UIKit.Dim;
        }
    }
}
