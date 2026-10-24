using Desync.Content;
using Desync.Flow;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Desync.UI
{
    /// Exploit draft (RunPhase.Draft): RunState.Offer as up to 3 cards that decrypt over 0.8 s with ticking glyphs (rising pitch).
    /// Pick with 1/2/3, a click, or ←/→ + SPACE / pad; [R] / pad Select rerolls (when a token is held); SKIP passes.
    /// Raises RunEvents.PickExploit(index | -1) and RunEvents.RerollOffer. A changed Offer (reroll) re-decrypts.
    public sealed class DraftScreen : UIPanel
    {
        internal const float Decrypt = 0.8f, CardLag = 0.1f, InputGuard = 0.25f, TickEvery = 0.05f;
        const int N = 3;
        const string Glyphs = "01#$%&@/\\|=+*?ABCDEFXYZ";

        RunCard[] _cards; UIButton _skip, _reroll;
        TMP_Text _sub, _keys, _empty;
        RectTransform _panel; Tween _pop;
        readonly Exploit[] _shown = new Exploit[N];
        readonly string[] _name = new string[N], _desc = new string[N];
        readonly char[] _buf = new char[256];
        int _count, _focus, _shownFrame = -1, _ticks, _rerolls = -1;
        float _t; bool _acted, _decrypting;

        internal bool Decrypting => _decrypting;
        internal string NameText(int i) => _cards[i].Name.text;
        internal string TargetName(int i) => _name[i];

        public void Init(AudioSource sfx) { }   // draft sounds are played by AudioManager (RunEvents)

        protected override void Create()
        {
            var dim = UIKit.Img(Root, "Dim", new Color(0f, 0f, 0.03f, 0.82f)); UIKit.Stretch(dim.rectTransform); dim.raycastTarget = true;
            _panel = UIKit.Stretch(UIKit.Obj(Root, "Panel"));
            var head = GlitchLabel.Create(_panel, "Head", Anc.T, new Vector2(0, -40), new Vector2(1400, 100), UIKit.Title, 70, UIKit.Paper, TextAlignmentOptions.Center, 3f, true, 8f);
            head.SetText("EXPLOIT DRAFT");
            _sub = UIKit.Text(_panel, "Sub", UIKit.Hud, 28, UIKit.Cyan, TextAlignmentOptions.Center); _sub.characterSpacing = 6f;
            UIKit.Place(_sub.rectTransform, Anc.T, new Vector2(0, -146), new Vector2(1400, 36));

            _cards = new RunCard[N];
            for (int i = 0; i < N; i++)
            {
                int k = i;
                _cards[i] = RunCard.Create(_panel, "Card" + i, new Vector2((i - 1) * 520f, 10f), new Vector2(460, 560), () => Click(k), () => Focus(k));
            }
            _empty = UIKit.Text(_panel, "Empty", UIKit.Title, 40, UIKit.Dim, TextAlignmentOptions.Center, "NO EXPLOIT FITS YOUR LOADOUT"); _empty.characterSpacing = 6f;
            UIKit.Place(_empty.rectTransform, Anc.C, new Vector2(0, 10), new Vector2(1400, 60));

            _reroll = UIButton.Create(_panel, "Reroll", "[R] REROLL", Anc.B, new Vector2(-230, 120), new Vector2(400, 80), 30, UIKit.Magenta, Reroll, UIKit.Hud);
            _skip = UIButton.Create(_panel, "Skip", "SKIP", Anc.B, new Vector2(230, 120), new Vector2(400, 80), 30, UIKit.Cyan, () => Pick(-1), UIKit.Hud);
            _skip.Hovered = () => Focus(N);
            _keys = UIKit.Text(_panel, "Keys", UIKit.Hud, 24, UIKit.Dim, TextAlignmentOptions.Center, "1 / 2 / 3 OR CLICK · ←/→ + SPACE · PAD D-PAD + A · SELECT REROLLS"); _keys.characterSpacing = 6f;
            UIKit.Place(_keys.rectTransform, Anc.B, new Vector2(0, 44), new Vector2(1700, 34));
        }

        public override void Show()
        {
            base.Show();
            _acted = false; _shownFrame = Time.frameCount;
            Load();
            UIKit.Pop(ref _pop, _panel, 0.94f, 0.2f);
        }

        public override void Hide() { UIKit.Kill(ref _pop); base.Hide(); }

        /// Takes RunState.Offer and restarts the decrypt.
        void Load()
        {
            var offer = RunState.Offer;
            _count = Mathf.Min(N, offer.Count);
            for (int i = 0; i < N; i++)
            {
                _shown[i] = i < _count ? offer[i] : null;
                _cards[i].Show(i < _count);
                if (i >= _count) continue;
                var e = offer[i];
                _cards[i].Set(e, "[" + (i + 1) + "]");
                _name[i] = _cards[i].Name.text; _desc[i] = _cards[i].Desc.text;
            }
            _empty.gameObject.SetActive(_count == 0);
            _sub.SetText("PICK 1 OF " + _count + " · HELD " + RunState.Held.Count + "/" + Loadout.MaxHeld);
            _rerolls = -1;
            _t = 0f; _ticks = 0; _decrypting = _count > 0;
            Focus(_count > 0 ? 0 : N);
            Scramble(0f);
        }

        void Focus(int f)
        {
            _focus = f;
            for (int i = 0; i < N; i++) _cards[i].SetFocus(i == f && i < _count);
            _skip.SetSelected(f >= _count);
        }

        /// Writes every card's name/desc at decrypt time t: revealed characters left to right, random glyphs after (spaces kept).
        void Scramble(float t)
        {
            for (int i = 0; i < _count; i++)
            {
                float k = Mathf.Clamp01((t - i * CardLag) / (Decrypt - (N - 1) * CardLag));
                Write(_cards[i].Name, _name[i], k);
                Write(_cards[i].Desc, _desc[i], k);
            }
        }

        void Write(TMP_Text t, string s, float k)
        {
            if (k >= 1f) { if (!ReferenceEquals(t.text, s)) t.SetText(s); return; }
            int n = Mathf.Min(s.Length, _buf.Length), shown = (int)(n * k);
            for (int i = 0; i < n; i++)
            {
                char c = s[i];
                _buf[i] = i < shown || c == ' ' || c == '\n' ? c : Glyphs[Random.Range(0, Glyphs.Length)];
            }
            t.SetText(_buf, 0, n);
        }

        void Finish() { _t = Decrypt; _decrypting = false; Scramble(Decrypt); }

        void Click(int i)
        {
            if (_decrypting) { Finish(); return; }   // a click during the decrypt only reveals
            Pick(i);
        }

        void Pick(int i)
        {
            if (_acted || i >= _count) return;
            _acted = true;
            if (i >= 0) UIKit.Pop(ref _pop, _cards[i].Rt, 1.15f, 0.2f);
            RunEvents.PickExploit?.Invoke(i);
        }

        void Reroll()
        {
            if (_acted || RunState.Rerolls <= 0) return;
            RunEvents.RerollOffer?.Invoke();
        }


        public override void Tick(float dt)
        {
            // a reroll (or any change of the offer) re-decrypts
            var offer = RunState.Offer;
            bool changed = Mathf.Min(N, offer.Count) != _count;
            for (int i = 0; i < _count && !changed; i++) changed = !ReferenceEquals(offer[i], _shown[i]);
            if (changed && !_acted) Load();
            if (RunState.Rerolls != _rerolls)
            {
                _rerolls = RunState.Rerolls;
                _reroll.Label.SetText(_rerolls > 0 ? "[R] REROLL ({0:0})" : "NO REROLLS", _rerolls);
                _reroll.Base = _rerolls > 0 ? UIKit.Magenta : UIKit.Dim; _reroll.SetSelected(false);
            }
            for (int i = 0; i < _count; i++) _cards[i].Tick();

            if (_decrypting)
            {
                _t = Mathf.Min(Decrypt, _t + dt);
                Scramble(_t);
                int ticks = (int)(_t / TickEvery);
                if (ticks > _ticks) _ticks = ticks;   // the decrypt ticks themselves are played by AudioManager (one voice budget)
                if (_t >= Decrypt) _decrypting = false;
            }

            if (_acted || Time.frameCount == _shownFrame) return;
            var kb = Keyboard.current;
            bool num = false;
            if (kb != null) for (int i = 0; i < N; i++) num |= kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame;
            int step = UIKit.NavStep;
            bool confirm = UIKit.Confirm.WasPressedThisFrame(), reroll = GameInput.Restart.WasPressedThisFrame();
            if (!(num || step != 0 || confirm || reroll)) return;
            if (_t < InputGuard && _decrypting) return;   // the key that closed the results must not also act
            if (_decrypting) { Finish(); return; }
            if (reroll) { Reroll(); return; }
            if (num)
            {
                for (int i = 0; i < N; i++) if (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame) { Pick(i); return; }
            }
            if (step != 0)   // cards 0.._count-1, then SKIP
            {
                int m = _count + 1, f = ((Mathf.Min(_focus, _count) + step) % m + m) % m;
                Focus(f >= _count ? N : f);
                return;
            }
            if (confirm) Pick(_focus >= _count ? -1 : _focus);
        }
    }
}
