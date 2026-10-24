using System;
using System.Collections.Generic;
using System.Text;
using Desync.Content;
using Desync.Flow;
using Desync.Net;
using Desync.Rules;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// The unlock tree (Flow/Unlocks.cs, Content/unlocks.json): Fragments buy exploit pools, archetype starters, Black Market access and
    /// Time-Attack. Opened from the title menu through UIManager.OpenUnlocks(). Four rows of cards on a 6-column grid (exploit pools, duos,
    /// starters, access) with a line from every requirement to the node it opens; each card shows its cost and its state: OWNED (green),
    /// EQUIPPED (the starter a run begins with, magenta), BUY (cyan: affordable now), NEED n MORE (the requirements are met, the Fragments
    /// are not) or LOCKED (a requirement is still to buy, dim). Nothing here needs the Deep Layer.
    /// Buying takes two steps so a stray click never spends: the first activation arms the card ("PRESS AGAIN"), the second pays. An owned
    /// starter card equips it (again = none). Two buttons wear the cosmetics the Deep Layer paid (flip effect, title).
    /// One linear focus ring (cards in reading order, FLIP EFFECT, TITLE, BACK): arrows / WASD / d-pad move it, SPACE / Enter / pad A act,
    /// mouse hover moves it and a click acts.
    public sealed class UnlockTree : UIPanel
    {
        const float CardW = 276f, CardH = 118f, PitchX = 300f, PitchY = 150f, Left = 40f, Top = 150f, InputGuard = 0.25f, ArmTime = 4f;

        sealed class Card
        {
            public UnlockNode N; public UIButton B; public TMP_Text Name, Cost, Sub, State, When;
        }

        sealed class Link { public UnlockNode From; public Image[] Seg = new Image[3]; }

        UnlockData _data;
        Card[] _card;
        readonly List<Link> _links = new List<Link>();
        UIButton _flip, _titleBtn, _back;
        Image[] _ring; UIButton[] _btn; Action[] _act;
        TMP_Text _frags, _worn, _dName, _dBody, _status;
        RectTransform _panel; Tween _pop, _tw;
        readonly StringBuilder _sb = new StringBuilder(160);
        int _focus, _armed = -1, _shownFrame = -1;
        float _t, _armT;

        internal int CardCount => _card.Length;
        internal int Focus => _focus;
        internal int Armed => _armed;
        internal string StatusText => _status.text;
        internal string FragmentsText => _frags.text;
        internal string DetailName => _dName.text;
        internal string DetailBody => _dBody.text;
        internal int IndexOf(string id) { for (int i = 0; i < _card.Length; i++) if (_card[i].N.id == id) return i; return -1; }
        internal string CardState(string id) { int i = IndexOf(id); return i < 0 ? null : _card[i].State.text; }
        internal int FlipIdx => _card.Length;
        internal int TitleIdx => _card.Length + 1;
        internal int BackIdx => _card.Length + 2;
        internal string FlipLabel => _flip.Label.text;
        internal string TitleLabel => _titleBtn.Label.text;

        protected override void Create()
        {
            _data = Unlocks.Data;
            var dim = UIKit.Img(Root, "Dim", new Color(0f, 0f, 0.03f, 0.9f)); UIKit.Stretch(dim.rectTransform); dim.raycastTarget = true;
            var panel = UIKit.Box(Root, "Panel", new Color(UIKit.Cyan.r, UIKit.Cyan.g, UIKit.Cyan.b, 0.8f), 0.95f);
            _panel = UIKit.Place(panel.rectTransform, Anc.C, Vector2.zero, new Vector2(1840, 1040));

            var head = UIKit.Text(panel.transform, "Head", UIKit.Title, 54, UIKit.Paper, TextAlignmentOptions.Left, "UNLOCKS"); head.fontStyle = FontStyles.Bold; head.characterSpacing = 8f;
            UIKit.Place(head.rectTransform, Anc.TL, new Vector2(40, -20), new Vector2(900, 70));
            var sub = UIKit.Text(panel.transform, "Sub", UIKit.Hud, 24, UIKit.Cyan, TextAlignmentOptions.Left, "SPEND FRAGMENTS · EVERY NODE IS OPEN WITHOUT THE DEEP LAYER"); sub.characterSpacing = 4f;
            UIKit.Place(sub.rectTransform, Anc.TL, new Vector2(44, -92), new Vector2(1400, 32));
            _frags = UIKit.Text(panel.transform, "Fragments", UIKit.Title, 40, UIKit.MedalColor(3), TextAlignmentOptions.Right); _frags.fontStyle = FontStyles.Bold; _frags.characterSpacing = 4f;
            UIKit.Place(_frags.rectTransform, Anc.TR, new Vector2(-40, -26), new Vector2(760, 52));
            _worn = UIKit.Text(panel.transform, "Worn", UIKit.Hud, 24, UIKit.Dim, TextAlignmentOptions.Right); _worn.characterSpacing = 3f;
            UIKit.Place(_worn.rectTransform, Anc.TR, new Vector2(-40, -84), new Vector2(760, 32));

            var ordered = _data.Ordered();
            _card = new Card[ordered.Count];
            _btn = new UIButton[ordered.Count + 3]; _ring = new Image[_btn.Length]; _act = new Action[_btn.Length];

            // links first: the cards draw over their ends
            foreach (var n in ordered)
                foreach (var r in n.requires)
                {
                    var from = _data.Find(r);
                    if (from != null) _links.Add(MakeLink(panel.transform, from, n));
                }

            for (int i = 0; i < ordered.Count; i++)
            {
                int k = i; var n = ordered[i];
                var b = UIButton.Create(panel.transform, "Card_" + n.id, "", Anc.TL, CardPos(n), new Vector2(CardW, CardH), 20, UIKit.Dim, () => Activate(k), UIKit.Hud);
                var c = new Card { N = n, B = b };
                c.Name = UIKit.Text(b.transform, "Name", UIKit.Hud, 22, UIKit.Paper, TextAlignmentOptions.Left, UIKit.Safe(n.name, UIKit.Hud)); c.Name.fontStyle = FontStyles.Bold; c.Name.richText = false;
                UIKit.Place(c.Name.rectTransform, Anc.TL, new Vector2(14, -10), new Vector2(CardW - 28, 30));
                c.Sub = UIKit.Text(b.transform, "Sub", UIKit.Hud, 18, UIKit.Dim, TextAlignmentOptions.Left, UIKit.Safe(SubLine(n), UIKit.Hud)); c.Sub.characterSpacing = 1f; c.Sub.richText = false;
                UIKit.Place(c.Sub.rectTransform, Anc.TL, new Vector2(14, -46), new Vector2(CardW - 112, 28));
                c.When = UIKit.Text(b.transform, "When", UIKit.Hud, 16, UIKit.Dim, TextAlignmentOptions.Right, UIKit.Safe(n.when, UIKit.Hud)); c.When.richText = false;
                UIKit.Place(c.When.rectTransform, Anc.TR, new Vector2(-14, -48), new Vector2(90, 26));
                c.State = UIKit.Text(b.transform, "State", UIKit.Hud, 22, UIKit.Dim, TextAlignmentOptions.Left); c.State.richText = false; c.State.characterSpacing = 1f;
                UIKit.Place(c.State.rectTransform, Anc.BL, new Vector2(14, 12), new Vector2(CardW - 90, 30));
                c.Cost = UIKit.Text(b.transform, "Cost", UIKit.Hud, 24, UIKit.Dim, TextAlignmentOptions.Right); c.Cost.richText = false;
                UIKit.Place(c.Cost.rectTransform, Anc.BR, new Vector2(-14, 12), new Vector2(72, 30));
                _card[i] = c;
                Reg(i, b, () => Activate(k));
            }

            int N = _card.Length;
            _dName = UIKit.Text(panel.transform, "DetailName", UIKit.Title, 36, UIKit.Paper, TextAlignmentOptions.Left); _dName.fontStyle = FontStyles.Bold; _dName.characterSpacing = 4f; _dName.richText = false;
            UIKit.Place(_dName.rectTransform, Anc.TL, new Vector2(40, -742), new Vector2(1100, 48));
            _dBody = UIKit.Text(panel.transform, "DetailBody", UIKit.Term, 30, UIKit.Paper, TextAlignmentOptions.TopLeft); _dBody.richText = false; _dBody.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Place(_dBody.rectTransform, Anc.TL, new Vector2(40, -796), new Vector2(1100, 120));
            _status = UIKit.Text(panel.transform, "Status", UIKit.Hud, 26, UIKit.Paper, TextAlignmentOptions.Left); _status.characterSpacing = 3f; _status.richText = false;
            UIKit.Place(_status.rectTransform, Anc.BL, new Vector2(44, 40), new Vector2(1100, 34));

            _flip = UIButton.Create(panel.transform, "Flip", "", Anc.TR, new Vector2(-40, -742), new Vector2(620, 64), 26, UIKit.Cyan, () => CycleCosmetic(CosmeticSlot.Flip), UIKit.Hud);
            _titleBtn = UIButton.Create(panel.transform, "Title", "", Anc.TR, new Vector2(-40, -818), new Vector2(620, 64), 26, UIKit.Cyan, () => CycleCosmetic(CosmeticSlot.Title), UIKit.Hud);
            _back = UIButton.Create(panel.transform, "Back", "BACK", Anc.BR, new Vector2(-40, 40), new Vector2(620, 84), 36, UIKit.Cyan, Close, UIKit.Hud);
            Reg(N, _flip, () => CycleCosmetic(CosmeticSlot.Flip)); Reg(N + 1, _titleBtn, () => CycleCosmetic(CosmeticSlot.Title)); Reg(N + 2, _back, Close);
        }

        static Vector2 CardPos(UnlockNode n) => new Vector2(Left + n.col * PitchX, -(Top + n.row * PitchY));

        /// A requirement line: right edge of `from`, a step at the middle of the gap, left edge of `to` (straight when they share a row).
        Link MakeLink(Transform parent, UnlockNode from, UnlockNode to)
        {
            var l = new Link { From = from };
            float x0 = Left + from.col * PitchX + CardW, x1 = Left + to.col * PitchX, xm = (x0 + x1) * 0.5f;
            float y0 = -(Top + from.row * PitchY + CardH * 0.5f), y1 = -(Top + to.row * PitchY + CardH * 0.5f);
            l.Seg[0] = Seg(parent, from.id + ">" + to.id + "a", x0, y0, xm, y0);
            l.Seg[1] = Seg(parent, from.id + ">" + to.id + "b", xm, y0, xm, y1);
            l.Seg[2] = Seg(parent, from.id + ">" + to.id + "c", xm, y1, x1, y1);
            return l;
        }

        static Image Seg(Transform parent, string name, float xa, float ya, float xb, float yb)
        {
            const float T = 4f;
            var im = UIKit.Img(parent, name, UIKit.Dim);
            bool vertical = Mathf.Approximately(xa, xb);
            float w = vertical ? T : Mathf.Max(T, Mathf.Abs(xb - xa)), h = vertical ? Mathf.Max(T, Mathf.Abs(yb - ya)) : T;
            var rt = UIKit.Place(im.rectTransform, Anc.TL, new Vector2(Mathf.Min(xa, xb) - (vertical ? T * 0.5f : 0f), Mathf.Max(ya, yb) + (vertical ? 0f : T * 0.5f)), new Vector2(w, h));
            rt.gameObject.SetActive(!(Mathf.Approximately(xa, xb) && Mathf.Approximately(ya, yb)));
            return im;
        }

        void Reg(int idx, UIButton b, Action act)
        {
            _btn[idx] = b; _act[idx] = act;
            var r = UIKit.Img(b.transform, "Focus", UIKit.Paper, UIKit.Frame, Image.Type.Sliced); UIKit.Stretch(r.rectTransform, -9, -9, -9, -9); r.enabled = false;
            _ring[idx] = r;
            int k = idx;
            b.Hovered = () => SetFocus(k);
        }

        string SubLine(UnlockNode n)
        {
            if (n.exploits.Count > 0) return n.exploits.Count + (n.id == "duos" ? " DUOS" : " EXPLOITS");
            if (n.starter != null) return (RunView.Cat.Exploit(n.starter)?.name ?? n.starter).ToUpperInvariant();
            return n.tag;
        }

        // ---------------------------------------------------------------- show
        public override void Show()
        {
            base.Show();
            _t = 0f; _armT = 0f; _armed = -1; _shownFrame = Time.frameCount;
            Repaint();
            int first = 0;
            for (int i = 0; i < _card.Length; i++)
                if (Unlocks.State(SaveService.Data, _data, _card[i].N) == UnlockState.Open) { first = i; break; }
            SetFocus(first);
            _status.SetText("");
            UIKit.Pop(ref _pop, _panel, 0.94f, 0.2f);
            UIKit.FreeCursor();
        }

        public override void Hide() { UIKit.Kill(ref _pop); UIKit.Kill(ref _tw); base.Hide(); }

        // ---------------------------------------------------------------- paint
        void Repaint()
        {
            var save = SaveService.Data;
            _frags.SetText("FRAGMENTS " + save.fragments);
            var worn = Cosmetics.Equipped(save, CosmeticSlot.Title);
            _worn.SetText(worn != null ? UIKit.Safe("TITLE · " + worn.name, UIKit.Hud) : "");
            _worn.color = UIKit.MedalColor(3);
            var starter = Unlocks.Starter(save, _data);

            for (int i = 0; i < _card.Length; i++)
            {
                var c = _card[i]; var n = c.N;
                var st = Unlocks.State(save, _data, n);
                bool equipped = starter == n;
                string line; Color tint;
                switch (st)
                {
                    case UnlockState.Owned:
                        line = equipped ? "EQUIPPED" : "OWNED"; tint = equipped ? UIKit.Magenta : UIKit.Green; break;
                    case UnlockState.Open: line = "BUY"; tint = UIKit.Cyan; break;
                    case UnlockState.Short: line = "NEED " + (n.cost - save.fragments) + " MORE"; tint = new Color(UIKit.Cyan.r, UIKit.Cyan.g, UIKit.Cyan.b, 0.55f); break;
                    default: line = UIKit.Safe("LOCKED", UIKit.Hud); tint = new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, 0.5f); break;
                }
                c.State.SetText(line); c.State.color = tint;
                c.Cost.SetText(n.Free || st == UnlockState.Owned ? "" : n.cost.ToString());
                c.Cost.color = st == UnlockState.Open ? UIKit.MedalColor(3) : UIKit.Dim;
                c.B.Base = tint; c.B.Hot = equipped ? UIKit.Cyan : UIKit.Magenta;
                c.B.SetSelected(false);
                c.Name.color = st == UnlockState.Locked ? UIKit.Dim : UIKit.Paper;
            }
            foreach (var l in _links)
            {
                bool done = Unlocks.Owned(save, _data, l.From);
                var col = done ? new Color(UIKit.Green.r, UIKit.Green.g, UIKit.Green.b, 0.8f) : new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, 0.45f);
                foreach (var s in l.Seg) s.color = col;
            }
            PaintCosmetics(save);
            PaintDetail(save);
        }

        void PaintCosmetics(SaveV3 save)
        {
            PaintSlot(save, CosmeticSlot.Flip, _flip, "FLIP EFFECT");
            PaintSlot(save, CosmeticSlot.Title, _titleBtn, "TITLE");
        }

        static void PaintSlot(SaveV3 save, CosmeticSlot slot, UIButton b, string label)
        {
            var wear = Cosmetics.Equipped(save, slot);
            bool any = false;
            foreach (var c in Cosmetics.All) any |= c.slot == slot && Cosmetics.Owned(save, c);
            b.Label.SetText(UIKit.Safe(label + ": " + (wear != null ? wear.name : any ? "NONE" : "EARN IN THE DEEP LAYER"), UIKit.Hud));
            b.Base = wear != null ? UIKit.MedalColor(3) : any ? UIKit.Cyan : new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, 0.5f);
            b.Hot = UIKit.Magenta;
            b.SetSelected(false);
        }

        void PaintDetail(SaveV3 save)
        {
            _sb.Clear();
            if (_focus < _card.Length)
            {
                var n = _card[_focus].N;
                var st = Unlocks.State(save, _data, n);
                _dName.SetText(UIKit.Safe(n.name + (n.Free ? "" : "  ·  " + n.cost + " FRAGMENTS"), UIKit.Hud));
                _sb.Append(n.desc);
                if (st == UnlockState.Locked) { var m = Unlocks.Missing(save, _data, n); if (m != null) _sb.Append("\nBUY ").Append(m.name).Append(" FIRST."); }
                else if (n.starter != null && st == UnlockState.Owned) _sb.Append(Unlocks.Starter(save, _data) == n ? "\nEquipped. Press again to start runs empty-handed." : "\nPress to equip.");
                _dBody.SetText(UIKit.Safe(_sb.ToString(), UIKit.Term));
            }
            else if (_focus == FlipIdx)
            {
                _dName.SetText("FLIP EFFECT");
                _dBody.SetText(UIKit.Safe("How a flip looks: a ring, shards, a streak. Earned by winning Deep Layer 12, 14, 16, 18 and 30. Press to step through what you own.", UIKit.Term));
            }
            else if (_focus == TitleIdx)
            {
                _dName.SetText("TITLE");
                _dBody.SetText(UIKit.Safe("A title shown on this screen. Earned by winning Deep Layer 11, 13, 15, 17, 19 and 20. Press to step through what you own.", UIKit.Term));
            }
            else
            {
                _dName.SetText("BACK");
                _dBody.SetText("Return to the title.");
            }
        }

        // ---------------------------------------------------------------- actions
        void Activate(int i)
        {
            if (i < 0 || i >= _card.Length) return;
            if (_focus != i) SetFocus(i);   // a click without a hover (touch, tests) still moves the ring
            var save = SaveService.Data; var n = _card[i].N;
            switch (Unlocks.State(save, _data, n))
            {
                case UnlockState.Owned:
                    if (n.starter != null)
                    {
                        bool wearing = Unlocks.Starter(save, _data) == n;
                        Unlocks.SetStarter(wearing ? "" : n.id);
                        Say(wearing ? "STARTER OFF · RUNS BEGIN EMPTY-HANDED" : "EQUIPPED · RUNS BEGIN WITH " + (RunView.Cat.Exploit(n.starter)?.name ?? n.starter).ToUpperInvariant(), UIKit.Magenta);
                        Repaint();
                    }
                    else Say("ALREADY OWNED", UIKit.Green);
                    Bump(i);
                    break;
                case UnlockState.Open:
                    if (_armed == i) Buy(i); else { _armed = i; _armT = ArmTime; Say("PRESS AGAIN TO SPEND " + n.cost + " FRAGMENTS", UIKit.MedalColor(3)); Bump(i); }
                    break;
                case UnlockState.Short: Say("NEED " + (n.cost - save.fragments) + " MORE FRAGMENTS", UIKit.Paper); Bump(i); break;
                default:
                    var m = Unlocks.Missing(save, _data, n);
                    Say(m != null ? "BUY " + m.name + " FIRST" : "LOCKED", UIKit.Paper); Bump(i); break;
            }
        }

        void Buy(int i)
        {
            var n = _card[i].N;
            _armed = -1; _armT = 0f;
            if (!Unlocks.Buy(n.id)) { Say("CANNOT BUY " + n.name, UIKit.Paper); return; }
            if (n.starter != null && Unlocks.Starter() == "") Unlocks.SetStarter(n.id);   // the first starter bought is worn at once
            Say("UNLOCKED · " + n.name, UIKit.Green);
            Repaint();
            Bump(i);
        }

        void CycleCosmetic(CosmeticSlot slot)
        {
            var save = SaveService.Data;
            var next = Cosmetics.Next(save, slot);
            bool any = false;
            foreach (var c in Cosmetics.All) any |= c.slot == slot && Cosmetics.Owned(save, c);
            if (!any) { Say(slot == CosmeticSlot.Flip ? "WIN DEEP LAYER 12, 14, 16, 18 OR 30 FOR A FLIP EFFECT" : "WIN DEEP LAYER 11, 13, 15, 17, 19 OR 20 FOR A TITLE", UIKit.Paper); Bump(slot == CosmeticSlot.Flip ? FlipIdx : TitleIdx); return; }
            Cosmetics.Equip(slot, next != null ? next.id : "");
            Say(next != null ? UIKit.Safe(next.name + " · " + next.desc.ToUpperInvariant(), UIKit.Hud) : (slot == CosmeticSlot.Flip ? "FLIP EFFECT OFF" : "NO TITLE"), UIKit.MedalColor(3));
            Repaint();
            Bump(slot == CosmeticSlot.Flip ? FlipIdx : TitleIdx);
        }

        void Say(string line, Color c) { _status.SetText(line); _status.color = c; }

        void Bump(int i) { if (_btn[i]) UIKit.Pop(ref _tw, _btn[i].transform, 0.94f, 0.18f); }

        void Close() { Hide(); }

        // ---------------------------------------------------------------- focus + input
        void SetFocus(int f)
        {
            int count = _btn.Length;
            f = (f % count + count) % count;
            if (_armed >= 0 && _armed != f) { _armed = -1; _armT = 0f; }   // moving off an armed card cancels the purchase
            _focus = f;
            for (int i = 0; i < count; i++) if (_ring[i]) _ring[i].enabled = i == _focus;
            PaintDetail(SaveService.Data);
        }

        public override void Tick(float dt)
        {
            _t += dt;
            UIKit.FreeCursor();
            if (_armed >= 0)
            {
                _armT -= dt;
                if (_armT <= 0f) { _armed = -1; Say("", UIKit.Paper); }
            }
            if (_t < InputGuard || Time.frameCount == _shownFrame) return;   // the key that opened the screen must not also act
            int step = UIKit.NavStep;
            if (step != 0) SetFocus(_focus + step);
            else if (UIKit.Confirm.WasPressedThisFrame()) _act[_focus]?.Invoke();
        }
    }
}
