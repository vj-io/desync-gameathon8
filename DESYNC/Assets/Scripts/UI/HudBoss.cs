using Desync.Enemies;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Boss HUDs. The bar under the layer tag (Hud/Boss: phase label + fill) is shared by the three bosses; each adds what its fight needs:
    ///   AUDITOR    uplink countdown in P3 (in HudScreen.TickBoss, unchanged).
    ///   COMPILER   node count, the wall-swap countdown with its 1 s telegraph (red + flicker), the P3 halving countdown.
    ///   ARCHITECT  strings left, the P3 survive timer, the final-string timer, the gaze-gap cue (READY 0.6 s ahead, FLIP NOW while the gap
    ///              is open) and the two ending choices; its lines go out as subtitles.
    /// Same rules as the rest of the HUD: polled from the boss component, text rewritten only when a value changes, no per-frame allocation.
    public sealed partial class HudScreen
    {
        GameObject _infoGo, _clockGo, _choiceGo; CanvasGroup _clockGroup;
        TMP_Text _info, _clockCap, _clockNum, _choiceA, _choiceB;
        GlitchLabel _cue; GameObject _cueGo;
        LiveText _infoL, _clockL;
        int _bossKind, _bossKey = -1, _clock10 = -2, _info10 = -2, _cueState = -1; bool _gapSaid;

        static readonly string[] CompilerLabels = { "COMPILER · P1 · RECOMPILING", "COMPILER · P2 · COMPILING ICE", "COMPILER · P3 · COLLAPSE", "COMPILER · COMPILED OUT" };
        static readonly string[] ArchitectLabels =
        {
            "ARCHITECT · P1 · THE GAZE", "ARCHITECT · P2 · THE STRINGS", "ARCHITECT · P3 · DESYNC", "ARCHITECT · P3 · FINAL STRING", "ARCHITECT · ",
        };
        static readonly string[] Pips = MakePips(Compiler.Nodes), StringPips = MakePips(ArchitectBoss.Terminals);

        internal bool BossInfoOn => _infoGo && _infoGo.activeSelf;
        internal bool BossClockOn => _clockGo && _clockGo.activeSelf;
        internal bool BossCueOn => _cueGo && _cueGo.activeSelf;
        internal bool BossChoiceOn => _choiceGo && _choiceGo.activeSelf;
        internal string BossInfoText => _info ? _info.text : null;
        internal string BossClockText => _clockNum ? _clockNum.text : null;
        internal string BossCueText => _cue ? _cue.Main.text : null;

        /// "○○○○" .. "●●●●": index = done.
        static string[] MakePips(int n)
        {
            var a = new string[n + 1];
            for (int d = 0; d <= n; d++) { var c = new char[n]; for (int i = 0; i < n; i++) c[i] = i < d ? '●' : '○'; a[d] = new string(c); }
            return a;
        }

        void BuildBoss()
        {
            var danger = UIKit.Danger; var hud = UIKit.Hud;
            var ib = UIKit.Box(Root, "BossInfo", danger, 0.7f);
            UIKit.Place(ib.rectTransform, Anc.T, new Vector2(0, -300), new Vector2(560, 44)); _infoGo = ib.gameObject;
            _info = UIKit.Text(ib.transform, "Label", hud, 26, UIKit.Paper, TextAlignmentOptions.Center); _info.characterSpacing = 3f; UIKit.Stretch(_info.rectTransform);
            _infoGo.SetActive(false);

            var ck = UIKit.Obj(Root, "BossClock"); UIKit.Place(ck, Anc.T, new Vector2(0, -354), new Vector2(640, 134)); _clockGo = ck.gameObject;
            _clockGroup = _clockGo.AddComponent<CanvasGroup>();
            _clockCap = UIKit.Text(ck, "Caption", hud, 26, UIKit.Paper, TextAlignmentOptions.Center); _clockCap.characterSpacing = 6f;
            UIKit.Place(_clockCap.rectTransform, Anc.T, Vector2.zero, new Vector2(640, 32));
            _clockNum = UIKit.Text(ck, "Number", UIKit.Title, 88, UIKit.Paper, TextAlignmentOptions.Center); _clockNum.fontStyle = FontStyles.Bold;
            UIKit.Place(_clockNum.rectTransform, Anc.B, Vector2.zero, new Vector2(640, 102));
            _clockGo.SetActive(false);

            _cue = GlitchLabel.Create(Root, "BossCue", Anc.C, new Vector2(0, -90), new Vector2(1200, 150), UIKit.Title, 112, UIKit.Green, TextAlignmentOptions.Center, 4f, true, 6f);
            _cueGo = _cue.gameObject; _cueGo.SetActive(false);

            var cb = UIKit.Box(Root, "BossChoice", UIKit.Green, 0.86f);
            UIKit.Place(cb.rectTransform, Anc.B, new Vector2(0, 100), new Vector2(1060, 90)); _choiceGo = cb.gameObject;
            _choiceA = UIKit.Text(cb.transform, "Sever", hud, 28, UIKit.Green, TextAlignmentOptions.Center, "SPACE IN THE GAP · SEVER THE STRING · EXPOSED"); _choiceA.fontStyle = FontStyles.Bold; _choiceA.characterSpacing = 3f;
            UIKit.Place(_choiceA.rectTransform, Anc.T, new Vector2(0, -8), new Vector2(1040, 36));
            _choiceB = UIKit.Text(cb.transform, "Merge", hud, 28, UIKit.Magenta, TextAlignmentOptions.Center, "NO FLIP · LET THE TIMER RUN · MERGED"); _choiceB.fontStyle = FontStyles.Bold; _choiceB.characterSpacing = 3f;
            UIKit.Place(_choiceB.rectTransform, Anc.B, new Vector2(0, 8), new Vector2(1040, 36));
            _choiceGo.SetActive(false);

            _infoL = new LiveText(_info); _clockL = new LiveText(_clockNum);
        }

        void ResetBossExtras()
        {
            _bossKey = -1; _clock10 = -2; _info10 = -2; _cueState = -1; _gapSaid = false;
            if (_infoGo) { _infoGo.SetActive(false); _clockGo.SetActive(false); _cueGo.SetActive(false); _choiceGo.SetActive(false); }
        }

        /// BossEvents.Phase: the ARCHITECT's phase lines as subtitles.
        public void OnBossPhase(string id, int phase)
        {
            if (id == ArchitectBoss.Id && (phase == 2 || phase == 3)) ArchitectLines.SayPhase(phase);
        }

        /// BossEvents.Ending ("Exposed" | "Merged"): the ARCHITECT's last line.
        public void OnBossEnding(string ending)
        {
            if (!string.IsNullOrEmpty(ending)) ArchitectLines.SayEnding(ending.ToLowerInvariant());
        }

        void SetClock(string caption, float secs, bool urgent)
        {
            if (!_clockGo.activeSelf) { _clockGo.SetActive(true); _clock10 = -2; }
            int s10 = (int)(Mathf.Max(0f, secs) * 10f);
            if (s10 != _clock10)
            {
                _clock10 = s10; _clockL.I(s10 / 10).C('.').I(s10 % 10).Done();
                _clockNum.color = urgent ? UIKit.Danger : UIKit.Paper;
            }
            _clockGroup.alpha = urgent && UIKit.Flashes ? 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 24f) : 1f;
            if (!ReferenceEquals(_clockCap.text, caption)) _clockCap.SetText(caption);
        }

        void HideClock() { if (_clockGo.activeSelf) _clockGo.SetActive(false); }

        // ---------------------------------------------------------------- COMPILER
        void TickCompiler(Compiler c, float dt)
        {
            int ph = Mathf.Clamp(c.Phase, 1, 4);
            int done = Compiler.Nodes - c.NodesLeft;
            int halve10 = ph == 3 && c.HalveIn >= 0f ? (int)(c.HalveIn * 10f) : -1;
            int key = ph * 100 + done * 10 + (EnemyFlags.BossHeat ? 1 : 0);
            if (key != _bossKey)
            {
                if (_bossKey >= 0 && _bossKey / 100 != ph) UIKit.Pop(ref _traceTw, _bossGo.transform, 1.2f, 0.3f);
                _bossKey = key; _bossPhase = ph;
                _bossText.SetText(CompilerLabels[ph - 1]);
                _bossText.color = UIKit.Paper;
                _bossFill.fillAmount = ph >= 4 ? 1f : done / (float)Compiler.Nodes;
                _info10 = -2;
            }
            if (!_infoGo.activeSelf) _infoGo.SetActive(true);
            if (halve10 != _info10)
            {
                _info10 = halve10;
                _infoL.S("NODES ").S(Pips[Mathf.Clamp(done, 0, Compiler.Nodes)]);
                if (halve10 >= 0) _infoL.S(" · HALVES ").I(halve10 / 10).C('.').I(halve10 % 10);
                else if (ph == 2 || ph == 3) _infoL.S(" · MELT +2");
                _infoL.Done();
            }
            if (ph >= 4) { HideClock(); return; }
            float swap = c.SwapIn;
            SetClock(EnemyFlags.BossHeat ? "WALLS SWAP IN · HEAT" : "WALLS SWAP IN", swap, swap <= Compiler.SwapTelegraph);   // telegraph: the last second
        }

        // ---------------------------------------------------------------- ARCHITECT
        void TickArchitect(ArchitectBoss b, float dt)
        {
            int ph = Mathf.Clamp(b.Phase, 1, 4);
            bool final = ph == 3 && b.FinalRemaining >= 0f;
            int stage = ph == 3 ? (final ? 3 : 2) : ph == 4 ? 4 : ph - 1;   // 0 gaze, 1 strings, 2 survive, 3 final string, 4 ending
            int left = b.StringsLeft;
            int key = stage * 100 + left * 10 + (EnemyFlags.BossHeat ? 1 : 0) + (stage == 4 && b.EndingName == BossEvents.Merged ? 2 : 0);
            if (key != _bossKey)
            {
                if (_bossKey >= 0 && _bossKey / 100 != stage) UIKit.Pop(ref _traceTw, _bossGo.transform, 1.2f, 0.3f);
                _bossKey = key; _bossPhase = ph;
                if (stage == 4) _bossText.SetText(ArchitectLabels[4] + (b.EndingName == BossEvents.Merged ? "MERGED" : "EXPOSED"));
                else _bossText.SetText(ArchitectLabels[stage]);
                _bossText.color = stage >= 2 && stage < 4 ? UIKit.Danger : UIKit.Paper;
                _info10 = -2;
            }
            _bossFill.fillAmount = stage == 2 ? Mathf.Clamp01(b.SurviveRemaining / ArchitectBoss.SurviveTime)
                                 : stage == 3 ? Mathf.Clamp01(b.FinalRemaining / ArchitectBoss.FinalTime)
                                 : stage == 4 ? 0f : 1f - left / (float)ArchitectBoss.Terminals;

            if (!_infoGo.activeSelf) _infoGo.SetActive(true);
            int gap10 = stage == 3 ? (b.GapOpen ? -1 : b.GapIn >= 0f ? (int)(b.GapIn * 10f) : -3) : -2;
            int infoKey = stage * 1000 + left * 100 + gap10 + 10;
            if (infoKey != _info10)
            {
                _info10 = infoKey;
                if (stage <= 1) _infoL.S("STRINGS ").S(StringPips[Mathf.Clamp(ArchitectBoss.Terminals - left, 0, ArchitectBoss.Terminals)]).Done();
                else if (stage == 2) _infoL.S("MELT +3 %/S IN BOTH LAYERS").Done();
                else if (stage == 3)
                {
                    if (b.GapOpen) _infoL.S("GAP OPEN").Done();
                    else if (gap10 >= 0) _infoL.S("NEXT GAP IN ").I(gap10 / 10).C('.').I(gap10 % 10).Done();
                    else _infoL.S("NO GAP LEFT").Done();
                }
                else _infoL.S(b.EndingName == BossEvents.Merged ? "YOU STAYED" : "THE STRING IS CUT").Done();
            }

            if (stage == 2) SetClock("SURVIVE", b.SurviveRemaining, b.SurviveRemaining <= 5f);
            else if (stage == 3) SetClock("FINAL STRING", b.FinalRemaining, b.FinalRemaining <= 3f);
            else HideClock();

            if (_choiceGo.activeSelf != final) _choiceGo.SetActive(final);

            // gap cue: READY 0.6 s ahead, FLIP NOW while it is open (3 states + off), the line says itself once
            int cue = !final ? 0 : b.GapOpen ? 2 : b.GapIn > 0f && b.GapIn <= ArchitectBoss.GapWarn ? 1 : 0;
            if (cue != _cueState)
            {
                _cueState = cue;
                _cueGo.SetActive(cue != 0);
                if (cue == 1) { _cue.SetText("READY"); _cue.SetColor(UIKit.Paper); }
                else if (cue == 2)
                {
                    _cue.SetText("FLIP NOW"); _cue.SetColor(UIKit.Green); _cue.Glitch(1.4f, 0.25f);
                    if (!_gapSaid) { _gapSaid = true; ArchitectLines.SayGap(); }
                }
            }
        }

        /// A boss bar appeared (kind 3 = ARCHITECT): its opening line.
        void BossAppeared(int kind)
        {
            if (kind == 3) ArchitectLines.SayPhase(1);
        }
    }
}
