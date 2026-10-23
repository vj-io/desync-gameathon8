using System.Text;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// In-room HUD. Everything is polled from GameState / events; text is only rewritten when its value changes
    /// (TMP.SetText with float args formats into an internal buffer: no per-frame allocation).
    public sealed class HudScreen : UIPanel
    {
        // ---- layer tag
        GlitchLabel _layer; TMP_Text _layerSub; bool _shownSource; Tween _layerTw; float _rejectT;
        // ---- left block
        TMP_Text _room, _time, _term, _traces, _exit; Tween _exitTw, _termTw;
        string[] _roomCodes, _pipStr, _bannerPips; int _traceMax;
        int _ri = -99, _lastTenth = -1, _td = -1, _tt = -1, _tc = -1; float _par; bool _overPar, _exitOpen;
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
        // ---- combo
        GlitchLabel _combo; CanvasGroup _comboGroup; RectTransform _comboRt; Tween _comboTw, _comboFade;
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

        const float Cps = 64f;

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
            UIKit.Place(lb.rectTransform, Anc.TL, new Vector2(32, -28), new Vector2(560, 196));
            var bar = UIKit.Img(lb.transform, "Accent", cyan); UIKit.Place(bar.rectTransform, Anc.TL, Vector2.zero, new Vector2(5, 196));
            _room = UIKit.Text(lb.transform, "Room", hud, 44, paper, TextAlignmentOptions.Left, "R-- · ---"); _room.fontStyle = FontStyles.Bold;
            UIKit.Place(_room.rectTransform, Anc.TL, new Vector2(24, -10), new Vector2(520, 54));
            _time = UIKit.Text(lb.transform, "Time", hud, 28, paper, TextAlignmentOptions.Left, "TIME 0.0  PAR 0s");
            UIKit.Place(_time.rectTransform, Anc.TL, new Vector2(24, -70), new Vector2(520, 34));
            _term = UIKit.Text(lb.transform, "Terminals", hud, 28, paper, TextAlignmentOptions.Left, "TERMINALS 0/0");
            UIKit.Place(_term.rectTransform, Anc.TL, new Vector2(24, -108), new Vector2(380, 34));
            _exit = UIKit.Text(lb.transform, "ExitOpen", hud, 28, UIKit.Green, TextAlignmentOptions.Left, "EXIT OPEN ►"); _exit.fontStyle = FontStyles.Bold;
            UIKit.Place(_exit.rectTransform, Anc.TL, new Vector2(330, -108), new Vector2(220, 34)); _exit.gameObject.SetActive(false);
            _traces = UIKit.Text(lb.transform, "Traces", hud, 28, paper, TextAlignmentOptions.Left, _pipStr[0]);
            UIKit.Place(_traces.rectTransform, Anc.TL, new Vector2(24, -146), new Vector2(520, 34));

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

            // ---- top-right stack: demo / hijack / lockdown
            var demo = UIKit.Box(Root, "Demo", cyan, 0.7f);
            UIKit.Place(demo.rectTransform, Anc.TR, new Vector2(-32, -28), new Vector2(380, 44)); _demo = demo.gameObject;
            _demoText = UIKit.Text(demo.transform, "Label", hud, 24, cyan, TextAlignmentOptions.Center, "DEMO MODE · TRACE-FREE"); UIKit.Stretch(_demoText.rectTransform);
            _demo.SetActive(false);

            var hj = UIKit.Box(Root, "Hijack", cyan, 0.75f);
            UIKit.Place(hj.rectTransform, Anc.TR, new Vector2(-32, -84), new Vector2(380, 66)); _hjGo = hj.gameObject;
            _hjText = UIKit.Text(hj.transform, "Label", hud, 30, cyan, TextAlignmentOptions.Left, "HIJACK 10.0s"); _hjText.fontStyle = FontStyles.Bold;
            UIKit.Place(_hjText.rectTransform, Anc.TL, new Vector2(16, -6), new Vector2(350, 36));
            var hjb = UIKit.Img(hj.transform, "BarBg", new Color(1, 1, 1, 0.12f)); UIKit.Place(hjb.rectTransform, Anc.BL, new Vector2(16, 10), new Vector2(348, 8));
            _hjFill = UIKit.Img(hjb.transform, "Fill", cyan); UIKit.Stretch(_hjFill.rectTransform); _hjFill.type = Image.Type.Filled; _hjFill.fillMethod = Image.FillMethod.Horizontal;
            _hjGo.SetActive(false);

            var lk = UIKit.Box(Root, "Lockdown", danger, 0.75f);
            _lockRt = UIKit.Place(lk.rectTransform, Anc.TR, new Vector2(-32, -160), new Vector2(380, 56)); _lockGo = lk.gameObject;
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

            // ---- glitch countdown (big number, upper-centre)
            var gg = UIKit.Obj(Root, "GlitchCountdown"); UIKit.Place(gg, Anc.C, new Vector2(0, -170), new Vector2(700, 200)); _glitchGo = gg.gameObject;
            var gc = UIKit.Text(gg, "Caption", hud, 28, magenta, TextAlignmentOptions.Center, "GLITCH ZONE · FORCED FLIP IN"); gc.characterSpacing = 6f;
            UIKit.Place(gc.rectTransform, Anc.T, new Vector2(0, 0), new Vector2(700, 36));
            _glitchNum = UIKit.Text(gg, "Number", title, 120, magenta, TextAlignmentOptions.Center, "3.0"); _glitchNum.fontStyle = FontStyles.Bold;
            _glitchNumRt = UIKit.Place(_glitchNum.rectTransform, Anc.B, new Vector2(0, 0), new Vector2(700, 150));
            _glitchGo.SetActive(false);

            // ---- combo badge (right-centre)
            _combo = GlitchLabel.Create(Root, "Combo", Anc.R, new Vector2(-48, 40), new Vector2(560, 90), hud, 60, cyan, TextAlignmentOptions.Right, 2.5f, true, 4f);
            _comboRt = (RectTransform)_combo.transform; _comboGroup = _combo.gameObject.AddComponent<CanvasGroup>(); _comboGroup.alpha = 0f;
            _comboRt.pivot = new Vector2(1f, 0.5f);

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

            SetLayer(false, false);
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
            UIKit.Kill(ref _comboFade); _comboGroup.alpha = 0f;
            UIKit.Kill(ref _cardFade); _cardState = 0; _cardGroup.alpha = 0f; _cardGroup.gameObject.SetActive(false);
            for (int i = 0; i < _bars.Length; i++) { _barT[i] = 0f; _bars[i].gameObject.SetActive(false); }
            _lastTenth = -1; _trLast = -1; _g10 = -1; _ls = -1; _h10 = -1;
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
            _layer.SetText(source ? "SOURCE" : "RENDER");
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

        public void OnCombo(int n)
        {
            if (n < 2) { UIKit.Fade(ref _comboFade, _comboGroup, 0f, 0.3f); return; }
            _combo.SetText("GHOST ×" + n);
            _combo.SetColor(n >= 5 ? UIKit.Magenta : n >= 3 ? UIKit.Green : UIKit.Cyan);
            UIKit.Kill(ref _comboFade); _comboGroup.alpha = 1f;
            UIKit.Pop(ref _comboTw, _comboRt, 1.5f + Mathf.Min(n, 6) * 0.05f, 0.4f);
            _combo.Glitch(1f, 0.3f);
        }

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
            if (ri != _ri)
            {
                _ri = ri; _lastTenth = -1;
                _par = ri >= 0 && cfg.parTime != null && ri < cfg.parTime.Length ? cfg.parTime[ri] : 0f;
                _room.SetText(ri >= 0 && ri < _roomCodes.Length ? _roomCodes[ri] : "R-- · ---");
            }
            float t = RoomClock.Time; int tenth = (int)(t * 10f);
            if (tenth != _lastTenth)
            {
                _lastTenth = tenth;
                bool over = _par > 0f && t > _par;
                if (over != _overPar) { _overPar = over; _time.color = over ? UIKit.Danger : UIKit.Paper; }
                _time.SetText("TIME {0:0}.{1:0}  PAR {2:0}s", tenth / 10, tenth % 10, _par);
            }
            int td = GameState.TerminalsDone, tt = GameState.TerminalsTotal;
            if (td != _td || tt != _tt) { _td = td; _tt = tt; _term.SetText("TERMINALS {0:0}/{1:0}", td, tt); if (td > 0) UIKit.Pop(ref _termTw, _term.transform, 1.2f, 0.25f); }
            if (GameState.ExitOpen != _exitOpen)
            {
                _exitOpen = GameState.ExitOpen; _exit.gameObject.SetActive(_exitOpen);
                if (_exitOpen) UIKit.Pop(ref _exitTw, _exit.transform, 1.5f, 0.35f);
            }
            int tc = Mathf.Clamp(GameState.TraceCount, 0, _traceMax);
            if (tc != _tc) { _tc = tc; _traces.SetText(_pipStr[tc]); _tracePips.SetText(_bannerPips[tc]); }

            TickTrace(dt);
            TickMelt(dt);
            TickCountdowns(dt, cfg);
            TickCrosshair();
            TickText(dt);
            TickOverlays(dt);
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
                if (r10 != _trLast) { _trLast = r10; _traceText.SetText("{0:0}.{1:0}s", r10 / 10, r10 % 10); }
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
                _mi = mi; _melt.SetText("MELT {0:0}%", mi); _meltFill.fillAmount = melt / 100f;
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
            // PRD 5.4, 70 %+: the whole HUD jitters (a few px, growing to 100 %) and the melt block a little harder. 30 Hz steps read as a shake at any frame rate.
            if (warn && UIKit.Shake && !GameState.Paused)
            {
                _shakeT -= dt;
                if (_shakeT <= 0f)
                {
                    _shakeT = 1f / 30f; _shaking = true;
                    float a = Mathf.Lerp(1.5f, 4f, Mathf.InverseLerp(cfg.meltWarn, 100f, melt));
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
                    _g10 = g10; _glitchNum.SetText("{0:0}.{1:0}", g10 / 10, g10 % 10);
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
                    _ls = ls; _lock.SetText("LOCKDOWN  {0:0}s", ls);
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
                if (h10 != _h10) { _h10 = h10; _hjText.SetText("HIJACK  {0:0}.{1:0}s", h10 / 10, h10 % 10); _hjFill.fillAmount = _hjT / Mathf.Max(0.1f, cfg.hijackDuration); }
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
        }
    }
}
