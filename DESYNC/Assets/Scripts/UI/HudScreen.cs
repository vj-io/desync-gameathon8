using System.Text;
using Desync.Content;
using Desync.Flow;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// In-room HUD. Everything is polled from GameState / events; text is only rewritten when its value changes
    /// (TMP.SetText with float args formats into an internal buffer: no per-frame allocation).
    public sealed partial class HudScreen : UIPanel
    {
        // ---- layer tag
        GlitchLabel _layer; TMP_Text _layerSub; bool _shownSource; Tween _layerTw; float _rejectT;
        // ---- left block
        TMP_Text _room, _timer, _term, _traces, _exit; Tween _exitTw, _termTw;
        string[] _roomCodes, _pipStr, _bannerPips; int _traceMax;
        int _ri = -99, _cs = -1, _td = -1, _tt = -1, _tc = -1; bool _exitOpen;
        readonly char[] _clock = new char[8];
        LiveText _timerL, _scoreL, _multL, _ghostL, _traceL, _meltL, _glitchL, _lockL, _hjL, _bossL;
        // ---- medal ticks (under the timer)
        GameObject _medalGo; TMP_Text _medalNext; Image _medalFill; Image[] _medalTicks; string[] _medalLabels = new string[5];
        float _tDev = -1f; int _medal = -1;
        // ---- score block (top-right)
        TMP_Text _score, _mult, _ghost; RectTransform _multRt; int _scoreShown = -1, _mult100 = -1, _chain = -1; float _multPulse;
        sealed class ScorePopUI { public RectTransform Rt; public TMP_Text Pts, Label; public LiveText PtsL; public CanvasGroup G; public float Age, Off, TargetOff; public bool On; }
        ScorePopUI[] _spops;
        static readonly string[] EventLabels = Spaced(System.Enum.GetNames(typeof(Desync.Rules.EventType)));   // "CleanFlip" -> "CLEAN FLIP"
        static string[] Spaced(string[] a)
        {
            for (int i = 0; i < a.Length; i++)
            {
                var sb = new StringBuilder();
                for (int k = 0; k < a[i].Length; k++) { if (k > 0 && char.IsUpper(a[i][k])) sb.Append(' '); sb.Append(char.ToUpperInvariant(a[i][k])); }
                a[i] = sb.ToString();
            }
            return a;
        }
        // ---- redline callout + hint
        GameObject _redGo; TMP_Text _red; bool _redOn;
        GameObject _hintGo; CanvasGroup _hintGroup; TMP_Text _hint; string _pendingHint; float _hintT;
        // ---- trace banner + edge
        RectTransform _traceRt; CanvasGroup _traceGroup; TMP_Text _traceText, _tracePips; Image _edge; Tween _traceTw;
        bool _traceOn; int _trLast = -1; float _edgeA;
        // ---- melt
        RectTransform _meltRt; TMP_Text _melt, _warn; Image _meltFill; Tween _meltTw; int _mi = -1; bool _warnOn, _warnVis = true; bool _shaking; float _shakeT;
        // ---- glitch / lockdown / hijack / demo
        GameObject _glitchGo; TMP_Text _glitchNum; RectTransform _glitchNumRt; Tween _glitchTw; int _g10 = -1, _gInt = -1;
        TMP_Text _lock; GameObject _lockGo; RectTransform _lockRt; Tween _lockTw; int _ls = -1;
        GameObject _hjGo; TMP_Text _hjText; Image _hjFill; float _hjT; int _h10 = -1; bool _hj;
        GameObject _demo; TMP_Text _demoText; int _dg;
        // ---- crosshair
        Image _dot; RectTransform _keyRt; TMP_Text _prompt; string _promptSrc; Tween _keyTw;
        // ---- hack hint
        GameObject _hackGo; bool _inHack; Tween _hackTw; RectTransform _hackRt;
        // ---- story typewriter
        TMP_Text _story; CanvasGroup _storyGroup; Tween _storyFade; int _storyState, _storyShown, _storyLen; float _storyChars, _storyHold;
        // ---- pops
        sealed class Pop { public GlitchLabel L; public CanvasGroup G; public RectTransform Rt; public float Age, Life, Y, TargetY; public bool On; public Tween S; }
        Pop[] _pops;
        // ---- glitch bars
        Image[] _bars; float[] _barT;
        // ---- reboot flash
        GameObject _rebootGo; CanvasGroup _rebootGroup; GlitchLabel _rebootLabel; float _rebootT; bool _rebootOn;
        // ---- control card
        CanvasGroup _cardGroup; RectTransform[] _chips; float _cardT; int _cardState; Tween _cardFade;
        // ---- BREACH run block: held exploits (bottom-right, glow while their condition holds), backups, room mutator chips
        GameObject _runGo; bool _runOn; string _nodeShown; int _nodeNum;
        RectTransform[] _xRt; Image[] _xBorder, _xGlow; CanvasGroup[] _xGroup; TMP_Text[] _xGlyph, _xInit; string[] _xWhen; bool[] _xLit; readonly Exploit[] _xShown = new Exploit[Loadout.MaxHeld]; int _xCount = -1;
        GameObject[] _mGo; TMP_Text[] _mText; readonly Mutator[] _mShown = new Mutator[2]; int _mCount = -1;
        TMP_Text _backups, _deepTag; int _bShown = -1, _dlShown = -1;
        // ---- AUDITOR boss bar (under the layer tag) + Tracker ICE spawn cue (edge arrow toward the nest)
        GameObject _bossGo; TMP_Text _bossText; Image _bossFill; int _bossPhase = -1, _up10 = -2;
        RectTransform _trkRt; TMP_Text _trk; float _trkT; Vector3 _trkPos;
        static readonly string[] BossLabels = { "AUDITOR · P1 · SCANNING", "AUDITOR · P2 · ESCALATED", "AUDITOR · P3 · UPLINK", "AUDITOR · UPLINK REACHED" };

        const float Cps = 64f, MeltShakeAt = 90f, MultPulse = 0.12f, PopLife = 0.9f, HintLife = 4f;

        protected override void Create()
        {
            Group.blocksRaycasts = false;
            var cfg = GameConfig.I;
            var hud = UIKit.Hud; var term = UIKit.Term; var title = UIKit.Title;
            var cyan = UIKit.Cyan; var magenta = UIKit.Magenta; var danger = UIKit.Danger; var paper = UIKit.Paper; var dim = UIKit.Dim;

            _traceMax = Mathf.Max(1, cfg.tracesToAlarm);
            _roomCodes = new string[6];
            for (int i = 0; i < 6; i++)
            {
                string n = cfg.roomNames != null && i < cfg.roomNames.Length ? cfg.roomNames[i] : "";
                _roomCodes[i] = "R" + (i + 1).ToString("00") + " · " + n.ToUpperInvariant();
            }
            _pipStr = new string[_traceMax + 1]; _bannerPips = new string[_traceMax + 1];
            string hot = "#" + ColorUtility.ToHtmlStringRGB(danger);
            for (int n = 0; n <= _traceMax; n++)
            {
                var a = new StringBuilder("TRACES  "); var b = new StringBuilder();
                for (int k = 0; k < _traceMax; k++) { a.Append(k < n ? "<color=" + hot + ">●</color>" : "<alpha=#66>○<alpha=#FF>"); b.Append(k < n ? "●" : "○"); }
                _pipStr[n] = a.ToString(); _bannerPips[n] = b.ToString();
            }

            // ---- trace edge vignette (red, behind everything)
            _edge = UIKit.Img(Root, "TraceEdge", new Color(danger.r, danger.g, danger.b, 0f), UIKit.Vignette);
            UIKit.Stretch(_edge.rectTransform);

            // ---- layer tag (top centre)
            _layer = GlitchLabel.Create(Root, "LayerTag", Anc.T, new Vector2(0, -22), new Vector2(620, 90), hud, 62, cyan, TextAlignmentOptions.Center, 2.5f, true, 16f);
            _layerSub = UIKit.Text(Root, "LayerSub", hud, 20, dim, TextAlignmentOptions.Center, "THE LIE");
            _layerSub.characterSpacing = 10f; UIKit.Place(_layerSub.rectTransform, Anc.T, new Vector2(0, -112), new Vector2(620, 28));

            // ---- left block (top-left)
            var lb = UIKit.Img(Root, "Left", new Color(UIKit.Panel.r, UIKit.Panel.g, UIKit.Panel.b, 0.6f));
            UIKit.Place(lb.rectTransform, Anc.TL, new Vector2(32, -28), new Vector2(560, 236));
            var bar = UIKit.Img(lb.transform, "Accent", cyan); UIKit.Place(bar.rectTransform, Anc.TL, Vector2.zero, new Vector2(5, 236));
            _room = UIKit.Text(lb.transform, "Room", hud, 44, paper, TextAlignmentOptions.Left, "R-- · ---"); _room.fontStyle = FontStyles.Bold;
            UIKit.Place(_room.rectTransform, Anc.TL, new Vector2(24, -10), new Vector2(520, 54));
            _timer = UIKit.Text(lb.transform, "Timer", hud, 40, paper, TextAlignmentOptions.Left, "00:00.00"); _timer.fontStyle = FontStyles.Bold;
            UIKit.Place(_timer.rectTransform, Anc.TL, new Vector2(24, -62), new Vector2(520, 46));
            BuildMedalRow(lb.transform);
            _term = UIKit.Text(lb.transform, "Terminals", hud, 28, paper, TextAlignmentOptions.Left, "TERMINALS 0/0");
            UIKit.Place(_term.rectTransform, Anc.TL, new Vector2(24, -146), new Vector2(380, 34));
            _exit = UIKit.Text(lb.transform, "ExitOpen", hud, 28, UIKit.Green, TextAlignmentOptions.Left, "EXIT OPEN ►"); _exit.fontStyle = FontStyles.Bold;
            UIKit.Place(_exit.rectTransform, Anc.TL, new Vector2(330, -146), new Vector2(220, 34)); _exit.gameObject.SetActive(false);
            _traces = UIKit.Text(lb.transform, "Traces", hud, 28, paper, TextAlignmentOptions.Left, _pipStr[0]);
            UIKit.Place(_traces.rectTransform, Anc.TL, new Vector2(24, -184), new Vector2(520, 34));

            BuildScoreBlock();
            BuildRunBlock();

            // ---- trace banner (under the layer tag)
            var tb = UIKit.Img(Root, "TraceBanner", new Color(danger.r * 0.35f, 0f, 0.02f, 0.88f));
            _traceRt = UIKit.Place(tb.rectTransform, Anc.T, new Vector2(0, -150), new Vector2(520, 70));
            _traceGroup = tb.gameObject.AddComponent<CanvasGroup>();
            var tf = UIKit.Img(tb.transform, "Frame", danger, UIKit.Frame, Image.Type.Sliced); UIKit.Stretch(tf.rectTransform);
            var tl = UIKit.Text(tb.transform, "Label", title, 34, danger, TextAlignmentOptions.Left, "TRACE"); tl.fontStyle = FontStyles.Bold; tl.characterSpacing = 6f;
            UIKit.Place(tl.rectTransform, Anc.L, new Vector2(20, 0), new Vector2(170, 60));
            _traceText = UIKit.Text(tb.transform, "Secs", hud, 40, paper, TextAlignmentOptions.Center, "6.0s");
            UIKit.Place(_traceText.rectTransform, Anc.C, new Vector2(20, 0), new Vector2(170, 60));
            _tracePips = UIKit.Text(tb.transform, "Pips", hud, 38, danger, TextAlignmentOptions.Right, _bannerPips[0]);
            UIKit.Place(_tracePips.rectTransform, Anc.R, new Vector2(-18, 0), new Vector2(150, 60));
            tb.gameObject.SetActive(false);

            // ---- AUDITOR boss bar (below the trace banner)
            var bb = UIKit.Box(Root, "Boss", danger, 0.75f);
            UIKit.Place(bb.rectTransform, Anc.T, new Vector2(0, -232), new Vector2(560, 62)); _bossGo = bb.gameObject;
            _bossText = UIKit.Text(bb.transform, "Label", hud, 30, paper, TextAlignmentOptions.Center); _bossText.fontStyle = FontStyles.Bold; _bossText.characterSpacing = 4f;
            UIKit.Stretch(_bossText.rectTransform, 0, 8, 0, 0);
            var bf = UIKit.Img(bb.transform, "BarBg", new Color(1, 1, 1, 0.12f)); UIKit.Place(bf.rectTransform, Anc.B, new Vector2(0, 6), new Vector2(536, 6));
            _bossFill = UIKit.Img(bf.transform, "Fill", danger); UIKit.Stretch(_bossFill.rectTransform); _bossFill.type = Image.Type.Filled; _bossFill.fillMethod = Image.FillMethod.Horizontal;
            _bossGo.SetActive(false);
            _trk = UIKit.Text(Root, "TrackerCue", hud, 72, danger, TextAlignmentOptions.Center, "▲");
            _trkRt = UIKit.Place(_trk.rectTransform, Anc.C, Vector2.zero, new Vector2(90, 90)); _trk.gameObject.SetActive(false);

            // ---- top-right stack (under the score block): demo / hijack / lockdown
            var demo = UIKit.Box(Root, "Demo", cyan, 0.7f);
            UIKit.Place(demo.rectTransform, Anc.TR, new Vector2(-32, -262), new Vector2(380, 44)); _demo = demo.gameObject;
            _demoText = UIKit.Text(demo.transform, "Label", hud, 24, cyan, TextAlignmentOptions.Center, "DEMO MODE · TRACE-FREE"); UIKit.Stretch(_demoText.rectTransform);
            _demo.SetActive(false);

            var hj = UIKit.Box(Root, "Hijack", cyan, 0.75f);
            UIKit.Place(hj.rectTransform, Anc.TR, new Vector2(-32, -318), new Vector2(380, 66)); _hjGo = hj.gameObject;
            _hjText = UIKit.Text(hj.transform, "Label", hud, 30, cyan, TextAlignmentOptions.Left, "HIJACK 10.0s"); _hjText.fontStyle = FontStyles.Bold;
            UIKit.Place(_hjText.rectTransform, Anc.TL, new Vector2(16, -6), new Vector2(350, 36));
            var hjb = UIKit.Img(hj.transform, "BarBg", new Color(1, 1, 1, 0.12f)); UIKit.Place(hjb.rectTransform, Anc.BL, new Vector2(16, 10), new Vector2(348, 8));
            _hjFill = UIKit.Img(hjb.transform, "Fill", cyan); UIKit.Stretch(_hjFill.rectTransform); _hjFill.type = Image.Type.Filled; _hjFill.fillMethod = Image.FillMethod.Horizontal;
            _hjGo.SetActive(false);

            var lk = UIKit.Box(Root, "Lockdown", danger, 0.75f);
            _lockRt = UIKit.Place(lk.rectTransform, Anc.TR, new Vector2(-32, -394), new Vector2(380, 56)); _lockGo = lk.gameObject;
            _lock = UIKit.Text(lk.transform, "Label", hud, 30, paper, TextAlignmentOptions.Center, "LOCKDOWN 90s"); _lock.fontStyle = FontStyles.Bold; UIKit.Stretch(_lock.rectTransform);
            _lockGo.SetActive(false);

            // ---- crosshair + interact key
            _dot = UIKit.Img(Root, "Dot", new Color(1f, 1f, 1f, 0.85f), UIKit.Disc);
            UIKit.Place(_dot.rectTransform, Anc.C, Vector2.zero, new Vector2(8, 8));
            var key = UIKit.Img(Root, "Key", new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.75f));
            _keyRt = UIKit.Place(key.rectTransform, Anc.C, Vector2.zero, new Vector2(64, 64));
            var kf = UIKit.Img(key.transform, "Frame", cyan, UIKit.Frame, Image.Type.Sliced); UIKit.Stretch(kf.rectTransform);
            var kt = UIKit.Text(key.transform, "E", hud, 44, cyan, TextAlignmentOptions.Center, "E"); kt.fontStyle = FontStyles.Bold; UIKit.Stretch(kt.rectTransform);
            _prompt = UIKit.Text(key.transform, "Prompt", hud, 28, cyan, TextAlignmentOptions.Center, "");
            UIKit.Place(_prompt.rectTransform, Anc.B, new Vector2(0, -42), new Vector2(520, 36));
            key.gameObject.SetActive(false);

            // ---- hack hint
            var hk = UIKit.Box(Root, "HackHint", cyan, 0.7f);
            _hackRt = UIKit.Place(hk.rectTransform, Anc.B, new Vector2(0, 88), new Vector2(760, 86)); _hackGo = hk.gameObject;
            var ht = UIKit.Text(hk.transform, "Label", hud, 40, cyan, TextAlignmentOptions.Center, "ARROWS  ← ↑ → ↓"); ht.fontStyle = FontStyles.Bold; ht.characterSpacing = 4f;
            UIKit.Place(ht.rectTransform, Anc.T, new Vector2(0, -4), new Vector2(740, 46));
            var hs = UIKit.Text(hk.transform, "Sub", hud, 20, dim, TextAlignmentOptions.Center, "ENTER THE SEQUENCE · SPACE STILL FLIPS");
            UIKit.Place(hs.rectTransform, Anc.B, new Vector2(0, 7), new Vector2(740, 24));
            _hackGo.SetActive(false);

            // ---- story line (typewriter, VT323)
            _story = UIKit.Text(Root, "Story", term, 44, UIKit.Green, TextAlignmentOptions.Bottom);
            _story.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Place(_story.rectTransform, Anc.B, new Vector2(0, 176), new Vector2(1500, 130));
            _storyGroup = _story.gameObject.AddComponent<CanvasGroup>(); _storyGroup.alpha = 0f;

            // ---- melt block (bottom-left)
            var mb = UIKit.Obj(Root, "Melt"); _meltRt = UIKit.Place(mb, Anc.BL, new Vector2(36, 34), new Vector2(460, 120));
            _warn = UIKit.Text(mb, "Warn", hud, 30, danger, TextAlignmentOptions.Left, "MELT WARNING"); _warn.fontStyle = FontStyles.Bold; _warn.characterSpacing = 6f;
            UIKit.Place(_warn.rectTransform, Anc.BL, new Vector2(0, 74), new Vector2(460, 36)); _warn.gameObject.SetActive(false);
            _melt = UIKit.Text(mb, "Readout", hud, 38, cyan, TextAlignmentOptions.Left, "MELT 0%"); _melt.fontStyle = FontStyles.Bold;
            UIKit.Place(_melt.rectTransform, Anc.BL, new Vector2(0, 26), new Vector2(460, 46));
            var mbg = UIKit.Img(mb, "BarBg", new Color(1, 1, 1, 0.14f)); UIKit.Place(mbg.rectTransform, Anc.BL, new Vector2(0, 8), new Vector2(380, 10));
            _meltFill = UIKit.Img(mbg.transform, "Fill", cyan); UIKit.Stretch(_meltFill.rectTransform); _meltFill.type = Image.Type.Filled; _meltFill.fillMethod = Image.FillMethod.Horizontal; _meltFill.fillAmount = 0f;
            for (int i = 0; i < 2; i++)   // 75 / 90 ticks
            {
                var tk = UIKit.Img(mbg.transform, "Tick" + (i == 0 ? 75 : 90), i == 0 ? new Color(magenta.r, magenta.g, magenta.b, 0.9f) : danger);
                UIKit.Place(tk.rectTransform, Anc.L, new Vector2(380f * (i == 0 ? 0.75f : 0.9f) - 1.5f, 0f), new Vector2(3f, 20f));
            }

            // ---- REDLINE callout (SOURCE at melt >= redline threshold: flipping back pays)
            var rl = UIKit.Box(Root, "Redline", danger, 0.72f);
            UIKit.Place(rl.rectTransform, Anc.BL, new Vector2(36, 168), new Vector2(460, 48)); _redGo = rl.gameObject;
            _red = UIKit.Text(rl.transform, "Label", hud, 30, danger, TextAlignmentOptions.Center, "REDLINE ► FLIP +" + Desync.Rules.Scoring.RedlinePts);
            _red.fontStyle = FontStyles.Bold; _red.characterSpacing = 4f; UIKit.Stretch(_red.rectTransform);
            _redGo.SetActive(false);

            // ---- glitch countdown (big number, upper-centre)
            var gg = UIKit.Obj(Root, "GlitchCountdown"); UIKit.Place(gg, Anc.C, new Vector2(0, -170), new Vector2(700, 200)); _glitchGo = gg.gameObject;
            var gc = UIKit.Text(gg, "Caption", hud, 28, magenta, TextAlignmentOptions.Center, "GLITCH ZONE · FORCED FLIP IN"); gc.characterSpacing = 6f;
            UIKit.Place(gc.rectTransform, Anc.T, new Vector2(0, 0), new Vector2(700, 36));
            _glitchNum = UIKit.Text(gg, "Number", title, 120, magenta, TextAlignmentOptions.Center, "3.0"); _glitchNum.fontStyle = FontStyles.Bold;
            _glitchNumRt = UIKit.Place(_glitchNum.rectTransform, Anc.B, new Vector2(0, 0), new Vector2(700, 150));
            _glitchGo.SetActive(false);

            // ---- centre pops
            _pops = new Pop[4];
            for (int i = 0; i < _pops.Length; i++)
            {
                var gl = GlitchLabel.Create(Root, "Pop" + i, Anc.C, new Vector2(0, 150), new Vector2(1500, 120), title, 78, paper, TextAlignmentOptions.Center, 3f, true, 6f);
                var g = gl.gameObject.AddComponent<CanvasGroup>(); g.alpha = 0f;
                _pops[i] = new Pop { L = gl, G = g, Rt = (RectTransform)gl.transform };
                gl.gameObject.SetActive(false);
            }

            // ---- glitch bars
            _bars = new Image[3]; _barT = new float[3];
            for (int i = 0; i < 3; i++) { _bars[i] = UIKit.Img(Root, "Bar" + i, cyan); _bars[i].gameObject.SetActive(false); }

            BuildControlCard();

            // ---- reboot flash (top of the stack)
            var rb = UIKit.Img(Root, "Reboot", new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.86f)); UIKit.Stretch(rb.rectTransform);
            _rebootGo = rb.gameObject; _rebootGroup = _rebootGo.AddComponent<CanvasGroup>();
            _rebootLabel = GlitchLabel.Create(rb.transform, "Label", Anc.C, Vector2.zero, new Vector2(1500, 240), title, 170, danger, TextAlignmentOptions.Center, 6f, true, 16f);
            _rebootGo.SetActive(false);

            // ---- contextual hint (after 3 deaths in a room): above the REBOOT screen so it reads during the wipe
            var hb = UIKit.Box(Root, "Hint", magenta, 0.85f);
            UIKit.Place(hb.rectTransform, Anc.B, new Vector2(0, 300), new Vector2(1400, 96)); _hintGo = hb.gameObject;
            _hintGroup = _hintGo.AddComponent<CanvasGroup>();
            var htag = UIKit.Text(hb.transform, "Tag", hud, 26, magenta, TextAlignmentOptions.Left, "HINT"); htag.fontStyle = FontStyles.Bold; htag.characterSpacing = 6f;
            UIKit.Place(htag.rectTransform, Anc.L, new Vector2(24, 0), new Vector2(110, 40));
            _hint = UIKit.Text(hb.transform, "Text", term, 36, paper, TextAlignmentOptions.Left);
            _hint.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Place(_hint.rectTransform, Anc.L, new Vector2(140, 0), new Vector2(1236, 88));
            _hintGo.SetActive(false);

            BuildM3();
            SetLayer(false, false);
            _timerL = new LiveText(_timer); _scoreL = new LiveText(_score); _multL = new LiveText(_mult); _ghostL = new LiveText(_ghost);
            _traceL = new LiveText(_traceText); _meltL = new LiveText(_melt); _glitchL = new LiveText(_glitchNum); _lockL = new LiveText(_lock);
            _hjL = new LiveText(_hjText); _bossL = new LiveText(_bossText);
        }

        void BuildMedalRow(Transform parent)
        {
            var row = UIKit.Obj(parent, "Medals"); UIKit.Place(row, Anc.TL, new Vector2(24, -110), new Vector2(520, 30)); _medalGo = row.gameObject;
            var bg = UIKit.Img(row, "BarBg", new Color(1, 1, 1, 0.14f)); UIKit.Place(bg.rectTransform, Anc.L, Vector2.zero, new Vector2(220, 8));
            _medalFill = UIKit.Img(bg.transform, "Fill", UIKit.Paper); UIKit.Stretch(_medalFill.rectTransform);
            _medalFill.type = Image.Type.Filled; _medalFill.fillMethod = Image.FillMethod.Horizontal; _medalFill.fillAmount = 0f;
            // the bar spans 0 .. SILVER (1.6 x tDev); ticks mark SYNC (1.0) and GOLD (1.25), the right end is SILVER
            _medalTicks = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                int m = 4 - i;   // SYNC, GOLD, SILVER
                _medalTicks[i] = UIKit.Img(bg.transform, "Tick" + UIKit.MedalNames[m], UIKit.MedalColor(m));
                UIKit.Place(_medalTicks[i].rectTransform, Anc.L, new Vector2(220f * UIKit.MedalLimit(m, 1f) / 1.6f - 1.5f, 0f), new Vector2(3f, 18f));
            }
            _medalNext = UIKit.Text(row, "Next", UIKit.Hud, 24, UIKit.Paper, TextAlignmentOptions.Left, "");
            UIKit.Place(_medalNext.rectTransform, Anc.L, new Vector2(236, 0), new Vector2(290, 30));
            _medalGo.SetActive(false);
        }

        void BuildScoreBlock()
        {
            var hud = UIKit.Hud;
            _score = UIKit.Text(Root, "Score", hud, 68, UIKit.Paper, TextAlignmentOptions.Right, "0"); _score.fontStyle = FontStyles.Bold;   // Share Tech Mono: tabular digits
            UIKit.Place(_score.rectTransform, Anc.TR, new Vector2(-36, -16), new Vector2(520, 80));
            _mult = UIKit.Text(Root, "Mult", hud, 40, UIKit.Dim, TextAlignmentOptions.Right, "x1.00"); _mult.fontStyle = FontStyles.Bold;
            _multRt = UIKit.Place(_mult.rectTransform, Anc.TR, new Vector2(-36, -96), new Vector2(300, 46));
            _ghost = UIKit.Text(Root, "Ghost", hud, 30, UIKit.Cyan, TextAlignmentOptions.Right, ""); _ghost.fontStyle = FontStyles.Bold; _ghost.characterSpacing = 4f;
            UIKit.Place(_ghost.rectTransform, Anc.TR, new Vector2(-36, -142), new Vector2(360, 38));
            _ghost.gameObject.SetActive(false);

            _spops = new ScorePopUI[6];
            for (int i = 0; i < _spops.Length; i++)
            {
                var rt = UIKit.Place(UIKit.Obj(Root, "ScorePop" + i), Anc.TR, new Vector2(-36, -196), new Vector2(460, 44));
                var p = new ScorePopUI { Rt = rt, G = rt.gameObject.AddComponent<CanvasGroup>() };
                p.Pts = UIKit.Text(rt, "Pts", hud, 38, UIKit.Paper, TextAlignmentOptions.Right); p.Pts.fontStyle = FontStyles.Bold;
                UIKit.Place(p.Pts.rectTransform, Anc.R, Vector2.zero, new Vector2(170, 44));
                p.Label = UIKit.Text(rt, "Label", hud, 24, UIKit.Dim, TextAlignmentOptions.Right); p.Label.characterSpacing = 4f;
                UIKit.Place(p.Label.rectTransform, Anc.R, new Vector2(-180, -2), new Vector2(280, 34));
                p.PtsL = new LiveText(p.Pts);
                rt.gameObject.SetActive(false);
                _spops[i] = p;
            }
        }

        void BuildRunBlock()
        {
            var run = UIKit.Stretch(UIKit.Obj(Root, "Run")); _runGo = run.gameObject;
            int n = Loadout.MaxHeld;
            _xRt = new RectTransform[n]; _xBorder = new Image[n]; _xGlow = new Image[n]; _xGroup = new CanvasGroup[n]; _xGlyph = new TMP_Text[n]; _xInit = new TMP_Text[n]; _xWhen = new string[n]; _xLit = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var rt = UIKit.Place(UIKit.Obj(run, "Exploit" + i), Anc.BR, Vector2.zero, new Vector2(84, 84));
                _xGroup[i] = rt.gameObject.AddComponent<CanvasGroup>();
                _xGlow[i] = UIKit.Img(rt, "Glow", Color.clear, UIKit.Disc); UIKit.Stretch(_xGlow[i].rectTransform, -20, -20, -20, -20);
                var bg = UIKit.Img(rt, "Bg", new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.82f)); UIKit.Stretch(bg.rectTransform);
                _xBorder[i] = UIKit.Img(rt, "Border", UIKit.Cyan); UIKit.Stretch(_xBorder[i].rectTransform);
                _xGlyph[i] = UIKit.Text(rt, "Glyph", UIKit.Hud, 40, UIKit.Cyan, TextAlignmentOptions.Center); UIKit.Place(_xGlyph[i].rectTransform, Anc.T, new Vector2(0, -6), new Vector2(84, 48));
                _xInit[i] = UIKit.Text(rt, "Init", UIKit.Hud, 20, UIKit.Paper, TextAlignmentOptions.Center); _xInit[i].characterSpacing = 4f;
                UIKit.Place(_xInit[i].rectTransform, Anc.B, new Vector2(0, 6), new Vector2(84, 26));
                _xRt[i] = rt; rt.gameObject.SetActive(false);
            }
            _backups = UIKit.Text(run, "Backups", UIKit.Hud, 28, UIKit.Green, TextAlignmentOptions.Right); _backups.fontStyle = FontStyles.Bold; _backups.characterSpacing = 4f;
            UIKit.Place(_backups.rectTransform, Anc.BR, new Vector2(-36, 128), new Vector2(300, 36));
            _deepTag = UIKit.Text(run, "DeepLayer", UIKit.Hud, 24, UIKit.Magenta, TextAlignmentOptions.Right); _deepTag.fontStyle = FontStyles.Bold; _deepTag.characterSpacing = 4f;
            UIKit.Place(_deepTag.rectTransform, Anc.BR, new Vector2(-36, 166), new Vector2(420, 32)); _deepTag.gameObject.SetActive(false);
            _mGo = new GameObject[2]; _mText = new TMP_Text[2];
            for (int i = 0; i < 2; i++)   // under the room block (top-left): mutators belong to the room
            {
                var chip = UIKit.Box(run, "Mutator" + i, UIKit.Magenta, 0.75f);
                UIKit.Place(chip.rectTransform, Anc.TL, new Vector2(32 + i * 284, -280), new Vector2(272, 44)); _mGo[i] = chip.gameObject;
                _mText[i] = UIKit.Text(chip.transform, "Label", UIKit.Hud, 22, UIKit.Magenta, TextAlignmentOptions.Center); _mText[i].characterSpacing = 3f;
                UIKit.Stretch(_mText[i].rectTransform, 8, 0, 8, 0);
                _mGo[i].SetActive(false);
            }
            _runGo.SetActive(false);
        }

        /// BREACH only: loadout / mutators / backups change -> rebuild (allocates, rare); every frame the conditional glow (no allocation).
        void TickRun()
        {
            bool on = RunView.Active;
            if (on != _runOn) { _runOn = on; _runGo.SetActive(on); _xCount = -1; _mCount = -1; _bShown = -1; _dlShown = -1; }
            if (!on) return;
            if (GameState.Chain > RunView.BestChain) RunView.BestChain = GameState.Chain;

            var held = RunState.Held;
            bool changed = held.Count != _xCount;
            for (int i = 0; i < held.Count && i < _xShown.Length && !changed; i++) changed = !ReferenceEquals(held[i], _xShown[i]);
            if (changed)
            {
                _xCount = held.Count;
                int n = Mathf.Min(held.Count, _xShown.Length);
                for (int i = 0; i < _xShown.Length; i++)
                {
                    _xShown[i] = i < held.Count ? held[i] : null;
                    _xRt[i].gameObject.SetActive(i < n);
                    if (i >= n) continue;
                    var e = held[i]; var c = RunView.ArchColor(e.archetype);
                    _xRt[i].anchoredPosition = new Vector2(-36 - (n - 1 - i) * 96, 34);
                    UIKit.SetBorder(_xBorder[i], (int)e.rarity, 1f); _xBorder[i].color = c;
                    _xGlyph[i].SetText(RunView.ArchGlyph[(int)e.archetype]); _xGlyph[i].color = c;
                    _xInit[i].SetText(RunView.Initials(e.name));
                    _xWhen[i] = RunView.When(e); _xLit[i] = false;
                    _xGlow[i].color = new Color(c.r, c.g, c.b, 0f); _xRt[i].localScale = Vector3.one;
                }
            }
            var muts = RunState.RoomMutators;
            bool mch = muts.Count != _mCount;
            for (int i = 0; i < muts.Count && i < _mShown.Length && !mch; i++) mch = !ReferenceEquals(muts[i], _mShown[i]);
            if (mch)
            {
                _mCount = muts.Count;
                for (int i = 0; i < _mShown.Length; i++)
                {
                    _mShown[i] = i < muts.Count ? muts[i] : null;
                    _mGo[i].SetActive(_mShown[i] != null);
                    if (_mShown[i] != null) _mText[i].SetText(UIKit.Safe(_mShown[i].name.ToUpperInvariant(), UIKit.Hud));
                }
            }
            if (DeepRun.Level != _dlShown)   // Deep Layer: the level is the heat total
            {
                _dlShown = DeepRun.Level; _deepTag.gameObject.SetActive(_dlShown > 0);
                if (_dlShown > 0) _deepTag.SetText("DEEP LAYER " + _dlShown + " · HEAT " + _dlShown);
            }
            if (RunState.Backups != _bShown) { _bShown = RunState.Backups; _backups.SetText("BACKUP " + RunView.Backups(_bShown)); _backups.color = _bShown > 0 ? UIKit.Green : UIKit.Dim; }

            var ctx = RunView.Live(_nodeNum);
            float pulse = 0.4f + 0.2f * Mathf.Sin(Time.unscaledTime * 7f);
            for (int i = 0; i < _xCount && i < _xShown.Length; i++)
            {
                if (_xShown[i] != null && _xShown[i].rarity == Rarity.E) UIKit.AnimateBorder(_xBorder[i]);
                bool lit = _xWhen[i] != null && Conditions.Eval(_xWhen[i], ctx);
                if (lit != _xLit[i]) { _xLit[i] = lit; if (lit) _xRt[i].localScale = Vector3.one * 1.18f; }
                float chipA = _xWhen[i] != null && !lit ? 0.5f : 1f;   // a conditional exploit waiting for its condition dims; lit = bright + glow
                if (_xGroup[i].alpha != chipA) _xGroup[i].alpha = chipA;
                if (lit || _xGlow[i].color.a > 0f)
                {
                    var g = _xGlow[i].color; g.a = lit ? pulse : 0f; _xGlow[i].color = g;
                    float s = _xRt[i].localScale.x; if (s > 1f) _xRt[i].localScale = Vector3.one * Mathf.Max(1f, s - Time.unscaledDeltaTime * 1.5f);
                }
            }
        }

        /// AUDITOR phase, and the uplink countdown in P3 (Enemies.Auditor.Current; null outside the boss arena).
        void TickBoss(float dt)
        {
            var a = Desync.Enemies.Auditor.Current;
            var comp = Desync.Enemies.Compiler.Current; var arch = Desync.Enemies.ArchitectBoss.Current;
            int kind = a != null ? 1 : comp != null ? 2 : arch != null ? 3 : 0;   // which boss owns the bar
            bool on = kind != 0;
            if (on != _bossGo.activeSelf || kind != _bossKind)
            {
                _bossKind = kind; _bossGo.SetActive(on); _bossPhase = -1; _up10 = -2; ResetBossExtras();
                if (on) BossAppeared(kind);
            }
            if (kind == 2) TickCompiler(comp, dt);
            else if (kind == 3) TickArchitect(arch, dt);
            if (kind == 1)
            {
                int ph = Mathf.Clamp(a.Phase, 1, 4);
                float up = a.UplinkRemaining;
                int u10 = up >= 0f ? (int)(up * 10f) : -1;
                if (ph != _bossPhase || u10 != _up10)
                {
                    if (ph != _bossPhase && _bossPhase > 0) UIKit.Pop(ref _traceTw, _bossGo.transform, 1.2f, 0.3f);
                    _bossPhase = ph; _up10 = u10;
                    if (u10 >= 0) _bossL.S("AUDITOR · UPLINK ").I(u10 / 10).C('.').I(u10 % 10).C('s').Done();
                    else _bossText.SetText(BossLabels[ph - 1]);
                    _bossText.color = u10 >= 0 && up < 5f ? UIKit.Danger : UIKit.Paper;
                    _bossFill.fillAmount = u10 >= 0 ? up / Desync.Enemies.Auditor.UplinkTime : ph >= 4 ? 0f : 1f - (ph - 1) / 3f;
                }
            }
            if (_trkT <= 0f) return;
            _trkT -= dt;
            if (_trkT <= 0f) { _trk.gameObject.SetActive(false); return; }
            var cam = PlayerRef.Cam;
            var local = cam ? cam.transform.InverseTransformPoint(_trkPos) : Vector3.forward;
            float ang = Mathf.Atan2(local.x, local.z);   // 0 = straight ahead (arrow up)
            _trkRt.anchoredPosition = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang)) * 320f;
            _trkRt.localRotation = Quaternion.Euler(0f, 0f, -ang * Mathf.Rad2Deg);
            _trk.alpha = !UIKit.Flashes || ((int)(_trkT * 8f) & 1) == 0 ? 1f : 0.35f;
        }

        /// Enemies.EnemyEvents.TrackerSpawned: a Tracker ICE left its nest. Pop + an edge arrow toward it for 1.6 s.
        public void OnTrackerSpawned(Vector3 pos)
        {
            _trkPos = pos; _trkT = 1.6f; _trk.gameObject.SetActive(true);
            ShowPop("TRACKER ICE", UIKit.Danger);
        }

        void BuildControlCard()
        {
            var cg = UIKit.Obj(Root, "ControlCard"); UIKit.Place(cg, Anc.T, new Vector2(0, -200), new Vector2(1760, 150));   // top third: keeps the floor decal and crosshair area clear
            _cardGroup = cg.gameObject.AddComponent<CanvasGroup>(); _cardGroup.alpha = 0f;
            string[,] k = { { "WASD", "MOVE" }, { "MOUSE", "LOOK" }, { "SPACE", "FLIP" }, { "E", "HACK" }, { "← ↑ → ↓", "CODE" }, { "R", "RESTART" }, { "ESC", "PAUSE" } };
            _chips = new RectTransform[7];
            const float w = 232f, gap = 12f;
            float x0 = -(7 * w + 6 * gap) * 0.5f + w * 0.5f;
            for (int i = 0; i < 7; i++)
            {
                var col = i == 2 ? UIKit.Magenta : UIKit.Cyan;
                var chip = UIKit.Img(cg, "Chip" + i, new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.8f));
                _chips[i] = UIKit.Place(chip.rectTransform, Anc.C, new Vector2(x0 + i * (w + gap), 0), new Vector2(w, 128));
                var f = UIKit.Img(chip.transform, "Frame", col, UIKit.Frame, Image.Type.Sliced); UIKit.Stretch(f.rectTransform);
                var kt = UIKit.Text(chip.transform, "Key", UIKit.Hud, k[i, 0].Length > 6 ? 34 : 44, col, TextAlignmentOptions.Center, k[i, 0]); kt.fontStyle = FontStyles.Bold;
                UIKit.Place(kt.rectTransform, Anc.T, new Vector2(0, -14), new Vector2(w - 10, 56));
                var ct = UIKit.Text(chip.transform, "Cap", UIKit.Hud, 28, UIKit.Paper, TextAlignmentOptions.Center, k[i, 1]); ct.characterSpacing = 6f;
                UIKit.Place(ct.rectTransform, Anc.B, new Vector2(0, 14), new Vector2(w - 10, 36));
            }
            cg.gameObject.SetActive(false);
        }

        // =================================================================== events (called by UIManager)
        public void OnRoomStarted(int room)
        {
            ClearTransient();
            if (room == 0) ShowControlCard();
            var intro = GameConfig.I.introLines;
            if (room == 1 && intro != null && intro.Length > 1) ShowStory(intro[1]);   // PRD 5.6: story line 2 (line 1 = title, line 3 = ending)
        }

        void ClearTransient()
        {
            _storyState = 0; _storyGroup.alpha = 0f; UIKit.Kill(ref _storyFade);
            for (int i = 0; i < _pops.Length; i++) { _pops[i].On = false; _pops[i].G.alpha = 0f; _pops[i].L.gameObject.SetActive(false); }
            for (int i = 0; i < _spops.Length; i++) { _spops[i].On = false; _spops[i].Rt.gameObject.SetActive(false); }
            _hintT = 0f; _hintGo.SetActive(false);
            ClearM3();
            UIKit.Kill(ref _cardFade); _cardState = 0; _cardGroup.alpha = 0f; _cardGroup.gameObject.SetActive(false);
            for (int i = 0; i < _bars.Length; i++) { _barT[i] = 0f; _bars[i].gameObject.SetActive(false); }
            _cs = -1; _trLast = -1; _g10 = -1; _ls = -1; _h10 = -1; _tDev = -1f; _multPulse = 0f; _multRt.localScale = Vector3.one;
        }

        public void OnFlipped(bool toSource) { SetLayer(toSource, true); }

        public void OnFlipRejected()
        {
            _layer.Glitch(1.6f, 0.35f); _layer.SetColor(UIKit.Danger); _rejectT = 0.3f;
            UIKit.Pop(ref _layerTw, _layer.transform, 1.15f, 0.25f);
        }

        void SetLayer(bool source, bool glitch)
        {
            _shownSource = source;
            _layer.SetText(UIKit.Safe(LayerName(source), UIKit.Hud));
            LayerFrame(source);
            _layer.SetColor(source ? UIKit.Green : UIKit.Cyan);
            _layerSub.SetText(source ? "THE TRUTH" : "THE LIE");
            _layerSub.color = source ? new Color(UIKit.Green.r, UIKit.Green.g, UIKit.Green.b, 0.7f) : UIKit.Dim;
            _rejectT = 0f;
            if (!glitch) return;
            _layer.Glitch(1.4f, 0.4f);
            UIKit.Pop(ref _layerTw, _layer.transform, 1.35f, 0.32f);
            FlashBars(source ? UIKit.Green : UIKit.Cyan);
        }

        void FlashBars(Color c)
        {
            if (!UIKit.Flashes) return;
            for (int i = 0; i < _bars.Length; i++)
            {
                var b = _bars[i];
                float h = Random.Range(4f, 38f);
                UIKit.Place(b.rectTransform, Anc.L, new Vector2(0, Random.Range(-420f, 420f)), new Vector2(0, h));
                b.rectTransform.anchorMin = new Vector2(0, 0.5f); b.rectTransform.anchorMax = new Vector2(1, 0.5f); b.rectTransform.sizeDelta = new Vector2(0, h);
                b.color = new Color(c.r, c.g, c.b, Random.Range(0.18f, 0.4f));
                b.gameObject.SetActive(true); _barT[i] = Random.Range(0.06f, 0.16f);
            }
        }

        public void OnTraceStarted(int n)
        {
            UIKit.Pop(ref _traceTw, _traceRt, 1.25f, 0.3f);
            if (_tracePips != null) _tracePips.SetText(_bannerPips[Mathf.Clamp(n, 0, _traceMax)]);
        }

        public void OnMeltWarning(bool on) { if (on) { UIKit.Pop(ref _meltTw, _meltRt, 1.3f, 0.35f); } }

        /// Score pop under the score block (pooled: the oldest is recycled, no allocation).
        public void OnScored(ScorePop s)
        {
            if (s.points <= 0) return;
            ScorePopUI p = null;
            for (int i = 0; i < _spops.Length && p == null; i++) if (!_spops[i].On) p = _spops[i];
            if (p == null) { p = _spops[0]; for (int i = 1; i < _spops.Length; i++) if (_spops[i].Age > p.Age) p = _spops[i]; }
            for (int i = 0; i < _spops.Length; i++) if (_spops[i].On && _spops[i] != p) _spops[i].TargetOff += 44f;   // older pops step down
            p.On = true; p.Age = 0f; p.Off = 0f; p.TargetOff = 0f;
            p.Rt.gameObject.SetActive(true); p.G.alpha = 1f;
            int ti = (int)s.type;
            p.PtsL.C('+').I(s.points).Done();
            p.Label.SetText(!string.IsNullOrEmpty(s.label) ? s.label : ti >= 0 && ti < EventLabels.Length ? EventLabels[ti] : "");
            var c = PopColor(s.type); p.Pts.color = c;
            p.Rt.localScale = Vector3.one * 1.25f;
        }

        static Color PopColor(Desync.Rules.EventType t)
        {
            switch (t)
            {
                case Desync.Rules.EventType.Graze: return UIKit.Magenta;
                case Desync.Rules.EventType.Redline: return UIKit.Danger;
                case Desync.Rules.EventType.Hack:
                case Desync.Rules.EventType.HackPerfect:
                case Desync.Rules.EventType.NoTraceRoom:
                case Desync.Rules.EventType.RoomClear: return UIKit.Green;
                default: return UIKit.Cyan;
            }
        }

        /// Flow raises Hint after the third death in a room; it shows on the next REBOOT for 4 s.
        public void OnHint(string text) { _pendingHint = text; }

        public void OnAccessGranted(string line) { ShowStory(line); }

        void ShowStory(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            line = UIKit.Safe(line, UIKit.Term);
            string full = "> " + line;
            _story.SetText(full);
            _storyLen = full.Length;
            _story.maxVisibleCharacters = 0; _storyChars = 0f; _storyShown = 0; _storyState = 1;
            UIKit.Kill(ref _storyFade); _storyGroup.alpha = 1f;
        }

        public void OnExitOpened() { ShowPop("EXIT OPEN", UIKit.Green); }

        public void OnRebooting()
        {
            ClearTransient();
            _rebootOn = true; _rebootT = 1.2f;
            _rebootGo.SetActive(true); _rebootGroup.alpha = 1f;
            _rebootLabel.Reveal("REBOOT", 0.12f); _rebootLabel.Glitch(2.2f, 0.5f);
            if (UIKit.Flashes) FlashBars(UIKit.Danger);
            if (!string.IsNullOrEmpty(_pendingHint))
            {
                _hint.SetText(UIKit.Safe(_pendingHint, UIKit.Term)); _pendingHint = null;
                _hintT = HintLife; _hintGroup.alpha = 1f; _hintGo.SetActive(true);
            }
        }

        public void OnRoomReset() { if (_rebootOn) _rebootT = Mathf.Min(_rebootT, 0.25f); }

        public void OnHijacked(bool on) { _hjT = on ? GameConfig.I.hijackDuration : 0f; _h10 = -1; }

        public void ShowPop(string text, Color c)
        {
            if (string.IsNullOrEmpty(text)) return;
            Pop p = null;
            for (int i = 0; i < _pops.Length && p == null; i++) if (!_pops[i].On) p = _pops[i];
            if (p == null) { p = _pops[0]; for (int i = 1; i < _pops.Length; i++) if (_pops[i].Age > p.Age) p = _pops[i]; }
            for (int i = 0; i < _pops.Length; i++) if (_pops[i].On && _pops[i] != p) _pops[i].TargetY += 96f;
            p.On = true; p.Age = 0f; p.Life = 1.4f; p.Y = 0f; p.TargetY = 0f;
            p.L.gameObject.SetActive(true);
            p.L.SetText(UIKit.Safe(text, UIKit.Title)); p.L.SetColor(c);
            p.Rt.anchoredPosition = new Vector2(0, 150);
            p.G.alpha = 1f;
            p.L.Glitch(1.2f, 0.3f);
            UIKit.Pop(ref p.S, p.Rt, 0.35f, 0.38f);
        }

        void ShowControlCard()
        {
            _cardGroup.gameObject.SetActive(true); _cardGroup.alpha = 1f; UIKit.Kill(ref _cardFade);
            for (int i = 0; i < _chips.Length; i++)
            {
                _chips[i].localScale = Vector3.zero;
                Tween.Scale(_chips[i], 0f, 1f, 0.32f, Ease.OutBack, startDelay: 0.25f + i * 0.07f, useUnscaledTime: true);
            }
            _cardT = 3.4f; _cardState = 1;
        }

        // =================================================================== per-frame
        public override void Tick(float dt)
        {
            var cfg = GameConfig.I;
            if (LayerState.InSource != _shownSource) SetLayer(LayerState.InSource, false);
            if (_rejectT > 0f) { _rejectT -= dt; if (_rejectT <= 0f) _layer.SetColor(_shownSource ? UIKit.Green : UIKit.Cyan); }

            // room / time / terminals / traces
            int ri = GameState.RoomIndex;
            if (ri != _ri || !ReferenceEquals(RunState.NodeId, _nodeShown))
            {
                _ri = ri; _cs = -1; _nodeShown = RunState.NodeId;
                var node = RunView.Active ? RunView.Node(_nodeShown) : null;   // BREACH rooms are named by their node
                _nodeNum = node != null ? node.index + 1 : 0;
                _room.SetText(node != null ? RunView.NodeLabel(node) : ri >= 0 && ri < _roomCodes.Length ? _roomCodes[ri] : "R-- · ---");
            }
            float t = RoomClock.Time; int cs = (int)(t * 100f + 0.001f);
            if (cs != _cs) { _cs = cs; UIKit.Clock(t, _clock); _timerL.A(_clock, 8).Done(); }
            TickMedals(t);
            TickScore(dt);
            int td = GameState.TerminalsDone, tt = GameState.TerminalsTotal;
            if (td != _td || tt != _tt) { _td = td; _tt = tt; _term.SetText("TERMINALS {0:0}/{1:0}", td, tt); if (td > 0) UIKit.Pop(ref _termTw, _term.transform, 1.2f, 0.25f); }
            if (GameState.ExitOpen != _exitOpen)
            {
                _exitOpen = GameState.ExitOpen; _exit.gameObject.SetActive(_exitOpen);
                if (_exitOpen) UIKit.Pop(ref _exitTw, _exit.transform, 1.5f, 0.35f);
            }
            int tc = Mathf.Clamp(GameState.TraceCount, 0, _traceMax);
            if (tc != _tc) { _tc = tc; _traces.SetText(_pipStr[tc]); _tracePips.SetText(_bannerPips[tc]); }

            TickRun();
            TickBoss(dt);
            TickM3(dt);
            TickTrace(dt);
            TickMelt(dt);
            TickCountdowns(dt, cfg);
            TickCrosshair();
            TickText(dt);
            TickOverlays(dt);
        }

        void TickMedals(float t)
        {
            float tDev = RoomGoals.TDev(GameState.RoomIndex);
            if (tDev != _tDev)
            {
                _tDev = tDev; _medal = -1;
                _medalGo.SetActive(tDev > 0f);
                if (tDev > 0f)   // labels change only with the room: build them here, never per frame
                {
                    for (int m = 2; m <= 4; m++) _medalLabels[m] = UIKit.MedalNames[m] + " " + UIKit.ClockShort(UIKit.MedalLimit(m, tDev));
                    _medalLabels[1] = "BRONZE · CLEAR";
                }
            }
            if (tDev <= 0f) { if (_timer.color != UIKit.Paper) _timer.color = UIKit.Paper; return; }
            int medal = UIKit.MedalAt(t, tDev);
            if (medal != _medal)
            {
                _medal = medal;
                var c = UIKit.MedalColor(medal);
                _medalNext.SetText(_medalLabels[medal]); _medalNext.color = c; _medalFill.color = c;
                _timer.color = medal <= 1 ? UIKit.Danger : UIKit.Paper;
                for (int i = 0; i < _medalTicks.Length; i++) { var k = _medalTicks[i].color; k.a = 4 - i > medal ? 0.3f : 1f; _medalTicks[i].color = k; }   // passed thresholds dim
            }
            _medalFill.fillAmount = Mathf.Clamp01(t / (1.6f * tDev));
        }

        void TickScore(float dt)
        {
            int score = GameState.Score;
            if (score != _scoreShown) { _scoreShown = score; _scoreL.I(score).Done(); }
            float mult = GameState.Multiplier;
            int m100 = Mathf.RoundToInt(mult * 100f);
            if (m100 != _mult100)
            {
                bool first = _mult100 < 0; _mult100 = m100;
                _multL.C('x').I(m100 / 100).C('.').I(m100 % 100, 2).Done();
                _mult.color = mult >= 3f ? UIKit.Magenta : mult >= 2f ? UIKit.Green : mult > 1.001f ? UIKit.Cyan : UIKit.Dim;
                if (!first) _multPulse = MultPulse;   // +12 % for 120 ms
            }
            if (_multPulse > 0f)
            {
                _multPulse = Mathf.Max(0f, _multPulse - dt);
                _multRt.localScale = Vector3.one * (1f + 0.12f * (_multPulse / MultPulse));
            }
            int chain = GameState.Chain;
            if (chain != _chain)
            {
                _chain = chain;
                _ghost.gameObject.SetActive(chain > 0);
                if (chain > 0) { _ghostL.S("GHOST x").I(chain).Done(); _ghost.color = chain >= 5 ? UIKit.Magenta : chain >= 3 ? UIKit.Green : UIKit.Cyan; }
            }
            for (int i = 0; i < _spops.Length; i++)
            {
                var p = _spops[i];
                if (!p.On) continue;
                p.Age += dt;
                if (p.Age >= PopLife) { p.On = false; p.Rt.gameObject.SetActive(false); continue; }
                p.Off = Mathf.Lerp(p.Off, p.TargetOff, 1f - Mathf.Exp(-16f * dt));
                p.Rt.anchoredPosition = new Vector2(-36f, -196f - p.Off + 16f * (p.Age / PopLife));   // newest right under GHOST, drifting up
                float s = p.Rt.localScale.x; if (s > 1f) p.Rt.localScale = Vector3.one * Mathf.Max(1f, s - dt * 3f);
                float left = PopLife - p.Age;
                p.G.alpha = left < 0.3f ? left / 0.3f : 1f;
            }
        }

        void TickTrace(float dt)
        {
            bool on = GameState.TraceActive;
            if (on != _traceOn)
            {
                _traceOn = on; _traceRt.gameObject.SetActive(on); _trLast = -1;
                if (on) { UIKit.Pop(ref _traceTw, _traceRt, 1.3f, 0.3f); }
            }
            if (on)
            {
                int r10 = (int)(Mathf.Max(0f, GameState.TraceRemaining) * 10f);
                if (r10 != _trLast) { _trLast = r10; _traceL.I(r10 / 10).C('.').I(r10 % 10).C('s').Done(); }
                _traceGroup.alpha = UIKit.Flashes ? 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 16f) : 1f;
            }
            float target = on ? (UIKit.Flashes ? 0.34f + 0.2f * Mathf.Sin(Time.unscaledTime * 9f) : 0.32f) : 0f;
            if (_edgeA > 0.002f || target > 0f)
            {
                _edgeA = Mathf.MoveTowards(_edgeA, target, dt * (on ? 3f : 1.5f));
                var c = _edge.color; c.a = _edgeA; _edge.color = c;
            }
        }

        void TickMelt(float dt)
        {
            var cfg = GameConfig.I;
            float melt = Mathf.Clamp(GameState.Melt, 0f, 100f);
            int mi = (int)(melt + 0.5f);
            if (mi != _mi)
            {
                _mi = mi; _meltL.S("MELT ").I(mi).C('%').Done(); _meltFill.fillAmount = melt / 100f;
                var c = melt >= cfg.meltWarn ? UIKit.Danger : Color.Lerp(UIKit.Cyan, UIKit.Magenta, Mathf.Clamp01(melt / Mathf.Max(1f, cfg.meltWarn)));
                _melt.color = c; _meltFill.color = c;
            }
            bool warn = melt >= cfg.meltWarn;
            if (warn != _warnOn) { _warnOn = warn; _warn.gameObject.SetActive(warn); _warnVis = true; if (warn) UIKit.Pop(ref _meltTw, _meltRt, 1.3f, 0.35f); }
            if (warn)
            {
                bool vis = !UIKit.Flashes || ((int)(Time.unscaledTime * 5f) & 1) == 0;
                if (vis != _warnVis) { _warnVis = vis; _warn.alpha = vis ? 1f : 0.2f; }
            }
            // REDLINE callout: in SOURCE at melt >= the redline threshold, flipping back to RENDER pays.
            bool red = LayerState.InSource && GameState.Alive && melt >= GameState.Mods.RedlineThreshold;
            if (red != _redOn) { _redOn = red; _redGo.SetActive(red); if (red) UIKit.Pop(ref _meltTw, _meltRt, 1.15f, 0.25f); }
            if (red) _red.alpha = !UIKit.Flashes || ((int)(Time.unscaledTime * 6f) & 1) == 0 ? 1f : 0.45f;
            // Flash from 70 % (above), shake from 90 %: the whole HUD jitters (a few px, growing to 100 %) and the melt block a little harder.
            // 30 Hz steps read as a shake at any frame rate.
            if (melt >= MeltShakeAt && UIKit.Shake && !GameState.Paused)
            {
                _shakeT -= dt;
                if (_shakeT <= 0f)
                {
                    _shakeT = 1f / 30f; _shaking = true;
                    float a = Mathf.Lerp(1.5f, 4f, Mathf.InverseLerp(MeltShakeAt, 100f, melt));
                    Root.anchoredPosition = new Vector2(Random.Range(-a, a), Random.Range(-a, a));
                    _meltRt.anchoredPosition = new Vector2(36f + Random.Range(-3f, 3f), 34f + Random.Range(-3f, 3f));
                }
            }
            else if (_shaking) { _shaking = false; _shakeT = 0f; Root.anchoredPosition = Vector2.zero; _meltRt.anchoredPosition = new Vector2(36f, 34f); }
        }

        void TickCountdowns(float dt, GameConfig cfg)
        {
            // glitch-zone forced-flip countdown
            float g = GameState.GlitchCountdown;
            bool gon = g >= 0f;
            if (gon != _glitchGo.activeSelf) { _glitchGo.SetActive(gon); _g10 = -1; _gInt = -1; }
            if (gon)
            {
                int g10 = (int)(g * 10f);
                if (g10 != _g10)
                {
                    _g10 = g10; _glitchL.I(g10 / 10).C('.').I(g10 % 10).Done();
                    _glitchNum.color = g < 1f ? UIKit.Danger : UIKit.Magenta;
                    if (g10 / 10 != _gInt) { _gInt = g10 / 10; UIKit.Pop(ref _glitchTw, _glitchNumRt, 1.3f, 0.3f); }
                }
            }
            // room-5 trace countdown
            float lc = GameState.TraceCountdown;
            bool lon = lc >= 0f;
            if (lon != _lockGo.activeSelf) { _lockGo.SetActive(lon); _ls = -1; }
            if (lon)
            {
                int ls = Mathf.CeilToInt(lc);
                if (ls != _ls)
                {
                    _ls = ls; _lockL.S("LOCKDOWN  ").I(ls).C('s').Done();
                    _lock.color = ls <= 20 ? UIKit.Danger : UIKit.Paper;
                    if (ls <= 10) UIKit.Pop(ref _lockTw, _lockRt, 1.2f, 0.25f);
                }
            }
            // hijack
            bool hj = GameState.HijackActive;
            if (hj != _hj) { _hj = hj; _hjGo.SetActive(hj); _h10 = -1; if (hj && _hjT <= 0f) _hjT = cfg.hijackDuration; if (!hj) _hjT = 0f; }
            if (hj)
            {
                _hjT = Mathf.Max(0f, _hjT - Time.deltaTime);
                int h10 = (int)(_hjT * 10f);
                if (h10 != _h10) { _h10 = h10; _hjL.S("HIJACK  ").I(h10 / 10).C('.').I(h10 % 10).C('s').Done(); _hjFill.fillAmount = _hjT / Mathf.Max(0.1f, cfg.hijackDuration); }
            }
            int dg = (GameState.DemoMode ? 1 : 0) | (GameState.God ? 2 : 0);   // F9 demo mode / debug god mode
            if (dg != _dg) { _dg = dg; _demo.SetActive(dg != 0); _demoText.SetText(dg == 1 ? "DEMO MODE · TRACE-FREE" : dg == 2 ? "GOD MODE" : "DEMO · GOD MODE"); }
        }

        void TickCrosshair()
        {
            string p = GameState.InteractPrompt;
            if (!string.Equals(p, _promptSrc))
            {
                _promptSrc = p;
                bool has = p != null;
                _dot.enabled = !has;
                _keyRt.gameObject.SetActive(has);
                if (has)
                {
                    if (p.Length > 2 && p[0] == 'E' && p[1] == ' ') p = p.Substring(2).TrimStart();
                    _prompt.SetText(UIKit.Safe(p, UIKit.Hud));
                    UIKit.Pop(ref _keyTw, _keyRt, 0.6f, 0.22f);
                }
            }
            bool hack = GameState.InHack;
            if (hack != _inHack)
            {
                _inHack = hack; _hackGo.SetActive(hack);
                if (hack) UIKit.Pop(ref _hackTw, _hackRt, 0.8f, 0.25f);
            }
        }

        void TickText(float dt)
        {
            if (_storyState == 1)
            {
                _storyChars += dt * Cps;
                int w = Mathf.Min((int)_storyChars, _storyLen);
                if (w != _storyShown) { _storyShown = w; _story.maxVisibleCharacters = w; }
                if (w >= _storyLen) { _storyState = 2; _storyHold = 3.6f; }
            }
            else if (_storyState == 2)
            {
                _storyHold -= dt;
                if (_storyHold <= 0f) { _storyState = 0; UIKit.Fade(ref _storyFade, _storyGroup, 0f, 0.6f); }
            }

            for (int i = 0; i < _pops.Length; i++)
            {
                var p = _pops[i];
                if (!p.On) continue;
                p.Age += dt;
                if (p.Age >= p.Life) { p.On = false; p.G.alpha = 0f; p.L.gameObject.SetActive(false); continue; }
                p.Y = Mathf.Lerp(p.Y, p.TargetY, 1f - Mathf.Exp(-14f * dt));
                p.Rt.anchoredPosition = new Vector2(0f, 150f + p.Y);
                float left = p.Life - p.Age;
                p.G.alpha = left < 0.35f ? left / 0.35f : 1f;
            }

            if (_cardState == 1)
            {
                _cardT -= dt;
                if (_cardT <= 0f) { _cardState = 2; _cardT = 0.7f; UIKit.Fade(ref _cardFade, _cardGroup, 0f, 0.6f); }
            }
            else if (_cardState == 2)
            {
                _cardT -= dt;
                if (_cardT <= 0f) { _cardState = 0; _cardGroup.gameObject.SetActive(false); }
            }
        }

        void TickOverlays(float dt)
        {
            for (int i = 0; i < _bars.Length; i++)
            {
                if (_barT[i] <= 0f) continue;
                _barT[i] -= dt;
                if (_barT[i] <= 0f) _bars[i].gameObject.SetActive(false);
            }
            if (_rebootOn)
            {
                _rebootT -= dt;
                _rebootGroup.alpha = Mathf.Clamp01(_rebootT / 0.25f);
                if (_rebootT <= 0f) { _rebootOn = false; _rebootGo.SetActive(false); }
            }
            if (_hintT > 0f)
            {
                _hintT -= dt;
                _hintGroup.alpha = Mathf.Clamp01(_hintT / 0.4f);
                if (_hintT <= 0f) _hintGo.SetActive(false);
            }
        }
    }
}
