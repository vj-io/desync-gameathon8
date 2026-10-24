using Desync.Content;
using Desync.Flow;
using PrimeTween;
using TMPro;
using UnityEngine;

namespace Desync.UI
{
    /// Black Market (RunPhase.Market): RunState.Market as cards with prices in Cycles. Click / ←→ + SPACE buys
    /// (RunEvents.Buy(index)); a "removal" item opens a picker of held exploits (RunEvents.RemoveExploit(id): the run charges
    /// the removal). LEAVE raises RunEvents.Leave. Repaints whenever Cycles, Backups, the loadout or a sold flag change.
    public sealed class MarketScreen : UIPanel
    {
        const int MaxItems = 6;
        RunCard[] _cards; UIButton _leave; TMP_Text _sub;
        GameObject _pickGo; UIButton[] _pick; UIButton _cancel;
        RectTransform _panel; Tween _pop;
        readonly string[] _pickId = new string[Loadout.MaxHeld];
        int _count, _focus, _pickFocus, _pickCount, _shownFrame = -1, _sig = int.MinValue;
        float _t; bool _left, _picking;

        internal bool Picking => _picking;

        protected override void Create()
        {
            var dim = UIKit.Img(Root, "Dim", new Color(0f, 0f, 0.03f, 0.82f)); UIKit.Stretch(dim.rectTransform); dim.raycastTarget = true;
            _panel = UIKit.Stretch(UIKit.Obj(Root, "Panel"));
            var head = GlitchLabel.Create(_panel, "Head", Anc.T, new Vector2(0, -40), new Vector2(1400, 100), UIKit.Title, 70, UIKit.MedalColor(3), TextAlignmentOptions.Center, 3f, true, 8f);
            head.SetText("BLACK MARKET");
            _sub = UIKit.Text(_panel, "Sub", UIKit.Hud, 30, UIKit.Paper, TextAlignmentOptions.Center); _sub.characterSpacing = 6f;
            UIKit.Place(_sub.rectTransform, Anc.T, new Vector2(0, -146), new Vector2(1600, 40));
            _cards = new RunCard[MaxItems];
            for (int i = 0; i < MaxItems; i++)
            {
                int k = i;
                _cards[i] = RunCard.Create(_panel, "Item" + i, Vector2.zero, new Vector2(290, 480), () => Click(k), () => Focus(k));
                _cards[i].Name.fontSize = 28; _cards[i].Desc.fontSize = 30;
            }
            _leave = UIButton.Create(_panel, "Leave", "LEAVE", Anc.B, new Vector2(0, 120), new Vector2(420, 80), 32, UIKit.Cyan, Leave, UIKit.Hud);
            _leave.Hovered = () => Focus(MaxItems);
            var keys = UIKit.Text(_panel, "Keys", UIKit.Hud, 24, UIKit.Dim, TextAlignmentOptions.Center, "CLICK TO BUY · ←/→ + SPACE · PAD D-PAD + A"); keys.characterSpacing = 6f;
            UIKit.Place(keys.rectTransform, Anc.B, new Vector2(0, 44), new Vector2(1700, 34));

            // removal picker
            var pb = UIKit.Box(_panel, "RemovePicker", UIKit.Magenta, 0.97f);
            UIKit.Place(pb.rectTransform, Anc.C, new Vector2(0, 0), new Vector2(900, 640)); _pickGo = pb.gameObject;
            var ph = UIKit.Text(pb.transform, "Head", UIKit.Title, 40, UIKit.Paper, TextAlignmentOptions.Center, "REMOVE WHICH EXPLOIT?"); ph.fontStyle = FontStyles.Bold;
            UIKit.Place(ph.rectTransform, Anc.T, new Vector2(0, -30), new Vector2(860, 56));
            _pick = new UIButton[Loadout.MaxHeld];
            for (int i = 0; i < _pick.Length; i++)
            {
                int k = i;
                _pick[i] = UIButton.Create(pb.transform, "Remove" + i, "", Anc.T, new Vector2(0, -110 - i * 84), new Vector2(760, 72), 28, UIKit.Paper, () => Remove(k), UIKit.Hud);
                _pick[i].Hovered = () => PickFocus(k);
            }
            _cancel = UIButton.Create(pb.transform, "Cancel", "CANCEL", Anc.B, new Vector2(0, 30), new Vector2(320, 64), 28, UIKit.Cyan, ClosePicker, UIKit.Hud);
            _cancel.Hovered = () => PickFocus(Loadout.MaxHeld);
            _pickGo.SetActive(false);
        }

        public override void Show()
        {
            base.Show();
            _left = false; _picking = false; _pickGo.SetActive(false); _shownFrame = Time.frameCount; _t = 0f; _sig = int.MinValue;
            Repaint();
            Focus(MaxItems);   // LEAVE first: no accidental buys
            UIKit.Pop(ref _pop, _panel, 0.94f, 0.2f);
        }

        public override void Hide() { UIKit.Kill(ref _pop); base.Hide(); }

        int Signature()
        {
            int s = RunState.Cycles * 31 + RunState.Backups * 7 + RunState.Rerolls * 3 + RunState.Held.Count * 101 + RunState.Market.Count * 1009;
            for (int i = 0; i < RunState.Market.Count; i++) if (RunState.Market[i].sold) s += 1 << (i + 12);
            return s;
        }

        void Repaint()
        {
            _sig = Signature();
            var m = RunState.Market;
            _count = Mathf.Min(MaxItems, m.Count);
            _sub.SetText("CYCLES " + RunState.Cycles + " · BACKUPS " + RunView.Backups(RunState.Backups) + " · HELD " + RunState.Held.Count + "/" + Loadout.MaxHeld);
            float dx = 310f, x0 = -(_count - 1) * dx * 0.5f;
            for (int i = 0; i < MaxItems; i++)
            {
                var c = _cards[i];
                c.Show(i < _count);
                if (i >= _count) continue;
                c.Rt.anchoredPosition = new Vector2(x0 + i * dx, 0f);
                var it = m[i];
                string foot = it.sold ? "SOLD" : it.price + " CYCLES";
                var e = it.kind == "exploit" ? RunView.Cat.Exploit(it.exploitId) : null;
                if (e != null) c.Set(e, foot);
                else
                    switch (it.kind)
                    {
                        case "backup": c.Set("●", "SURVIVAL", "BACKUP", "Die and respawn at room start instead of losing the run. Max " + RunRules.MaxBackups + ".", UIKit.Green, 0, foot); break;
                        case "reroll": c.Set("↔", "TOKEN", "REROLL", "Reroll one exploit offer.", UIKit.Cyan, 0, foot); break;
                        case "removal": c.Set("×", "SERVICE", "REMOVAL", "Strip one held exploit from your loadout.", UIKit.Magenta, 0, foot); break;
                        default: c.Set("?", "ITEM", it.exploitId ?? it.kind ?? "ITEM", "", UIKit.Dim, 0, foot); break;
                    }
                bool can = Affordable(i);
                c.Shade.enabled = !can;
                c.Foot.color = it.sold ? UIKit.Dim : RunState.Cycles >= it.price ? UIKit.Green : UIKit.Danger;
            }
            if (_focus < MaxItems && _focus >= _count) Focus(MaxItems);
        }

        bool Affordable(int i)
        {
            var it = RunState.Market[i];
            if (it.sold || RunState.Cycles < it.price) return false;
            switch (it.kind)   // the run refuses these anyway: show them as unavailable
            {
                case "removal": return RunState.Held.Count > 0;
                case "backup": return RunState.Backups < RunRules.MaxBackups;
                case "exploit": var e = RunView.Cat.Exploit(it.exploitId); return e != null && Loadout.CanHold(RunState.Held, e);
                default: return true;
            }
        }

        void Focus(int f)
        {
            _focus = f;
            for (int i = 0; i < MaxItems; i++) _cards[i].SetFocus(i == f && i < _count);
            _leave.SetSelected(f >= MaxItems);
        }

        void Click(int i)
        {
            if (_left || _picking || i >= _count || !Affordable(i)) return;
            if (RunState.Market[i].kind == "removal") { OpenPicker(); return; }
            RunEvents.Buy?.Invoke(i);
            UIKit.Pop(ref _pop, _cards[i].Rt, 1.1f, 0.2f);
        }

        void Leave()
        {
            if (_left || _picking) return;
            _left = true;
            RunEvents.Leave?.Invoke();
        }

        // ---------------------------------------------------------------- removal picker
        void OpenPicker()
        {
            _picking = true; _pickGo.SetActive(true);
            var held = RunState.Held;
            _pickCount = Mathf.Min(_pick.Length, held.Count);
            for (int i = 0; i < _pick.Length; i++)
            {
                _pick[i].gameObject.SetActive(i < _pickCount);
                if (i >= _pickCount) { _pickId[i] = null; continue; }
                var e = held[i];
                _pickId[i] = e.id;
                _pick[i].Base = RunView.ArchColor(e.archetype);
                _pick[i].Label.SetText(UIKit.Safe(RunView.ArchGlyph[(int)e.archetype] + "  " + e.name.ToUpperInvariant() + "  · " + RunView.RarityName[(int)e.rarity], UIKit.Hud));
            }
            PickFocus(0);
        }

        void PickFocus(int f)
        {
            _pickFocus = f;
            for (int i = 0; i < _pick.Length; i++) _pick[i].SetSelected(i == f);
            _cancel.SetSelected(f >= _pickCount);
        }

        void Remove(int i)
        {
            if (!_picking || i >= _pickCount || _pickId[i] == null) return;
            RunEvents.RemoveExploit?.Invoke(_pickId[i]);
            ClosePicker();
        }

        void ClosePicker() { _picking = false; _pickGo.SetActive(false); }

        public override void Tick(float dt)
        {
            _t += dt;
            if (Signature() != _sig) Repaint();
            for (int i = 0; i < _count; i++) _cards[i].Tick();
            if (_left || _t < 0.25f || Time.frameCount == _shownFrame) return;
            int step = UIKit.NavStep;
            bool ok = UIKit.Confirm.WasPressedThisFrame();
            if (_picking)
            {
                int m = _pickCount + 1;
                if (step != 0) { int f = ((Mathf.Min(_pickFocus, _pickCount) + step) % m + m) % m; PickFocus(f >= _pickCount ? Loadout.MaxHeld : f); }
                else if (ok) { if (_pickFocus >= _pickCount) ClosePicker(); else Remove(_pickFocus); }
                return;
            }
            if (step != 0) { int m = _count + 1, f = ((Mathf.Min(_focus, _count) + step) % m + m) % m; Focus(f >= _count ? MaxItems : f); }
            else if (ok) { if (_focus >= _count) Leave(); else Click(_focus); }
        }
    }

    /// Cooldown node (RunPhase.Cooldown): one of RunState.CooldownOptions (reset melt carry-over / +1 Backup / reroll token)
    /// via RunEvents.ChooseCooldown(index), then CONTINUE raises RunEvents.Leave.
    public sealed class CooldownScreen : UIPanel
    {
        RunCard[] _cards; UIButton _continue; TMP_Text _sub;
        RectTransform _panel; Tween _pop;
        int _count, _focus, _chosen = -1, _shownFrame = -1;
        float _t; bool _left;

        internal int Chosen => _chosen;

        protected override void Create()
        {
            var dim = UIKit.Img(Root, "Dim", new Color(0f, 0f, 0.03f, 0.82f)); UIKit.Stretch(dim.rectTransform); dim.raycastTarget = true;
            _panel = UIKit.Stretch(UIKit.Obj(Root, "Panel"));
            var head = GlitchLabel.Create(_panel, "Head", Anc.T, new Vector2(0, -40), new Vector2(1400, 100), UIKit.Title, 70, UIKit.Paper, TextAlignmentOptions.Center, 3f, true, 8f);
            head.SetText("COOLDOWN");
            _sub = UIKit.Text(_panel, "Sub", UIKit.Hud, 30, UIKit.Cyan, TextAlignmentOptions.Center); _sub.characterSpacing = 6f;
            UIKit.Place(_sub.rectTransform, Anc.T, new Vector2(0, -146), new Vector2(1600, 40));
            _cards = new RunCard[3];
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                _cards[i] = RunCard.Create(_panel, "Option" + i, new Vector2((i - 1) * 520f, 20f), new Vector2(460, 460), () => Choose(k), () => Focus(k));
            }
            _continue = UIButton.Create(_panel, "Continue", "CONTINUE", Anc.B, new Vector2(0, 120), new Vector2(420, 80), 32, UIKit.Cyan, Leave, UIKit.Hud);
            _continue.Hovered = () => Focus(3);
            var keys = UIKit.Text(_panel, "Keys", UIKit.Hud, 24, UIKit.Dim, TextAlignmentOptions.Center, "PICK ONE · CLICK OR ←/→ + SPACE · PAD D-PAD + A"); keys.characterSpacing = 6f;
            UIKit.Place(keys.rectTransform, Anc.B, new Vector2(0, 44), new Vector2(1700, 34));
        }

        public override void Show()
        {
            base.Show();
            _left = false; _chosen = -1; _shownFrame = Time.frameCount; _t = 0f;
            Paint();
            Focus(0);
            UIKit.Pop(ref _pop, _panel, 0.94f, 0.2f);
        }

        public override void Hide() { UIKit.Kill(ref _pop); base.Hide(); }

        void Paint()
        {
            var opts = RunState.CooldownOptions;
            _count = Mathf.Min(3, opts.Length);
            _sub.SetText("MELT CARRY-OVER " + Mathf.RoundToInt(RunState.MeltCarry) + "% · BACKUPS " + RunView.Backups(RunState.Backups) + " · REROLLS " + RunState.Rerolls);
            for (int i = 0; i < 3; i++)
            {
                var c = _cards[i];
                c.Show(i < _count);
                if (i >= _count) continue;
                bool usable = Usable(i);
                string foot = _chosen == i ? "APPLIED" : !usable ? "MAXED" : "";
                switch (opts[i])
                {
                    case "reset_melt": c.Set("▼", "COOLANT", "RESET MELT", "Carry-over melt " + Mathf.RoundToInt(RunState.MeltCarry) + "% → 0% for the next room.", UIKit.Cyan, 0, foot); break;
                    case "backup": c.Set("●", "SURVIVAL", "+1 BACKUP", "Backups " + RunState.Backups + " → " + Mathf.Min(RunRules.MaxBackups, RunState.Backups + 1) + ". Die and respawn at room start.", UIKit.Green, 0, foot); break;
                    case "reroll": c.Set("↔", "TOKEN", "+1 REROLL", "Reroll one exploit offer. Tokens " + RunState.Rerolls + " → " + (RunState.Rerolls + 1) + ".", UIKit.Magenta, 0, foot); break;
                    default: c.Set("?", "OPTION", opts[i], "", UIKit.Dim, 0, foot); break;
                }
                c.Shade.enabled = (_chosen >= 0 && _chosen != i) || (_chosen < 0 && !usable);
                c.Foot.color = _chosen == i ? UIKit.Green : UIKit.Dim;
            }
        }

        bool Usable(int i) => !(RunState.CooldownOptions[i] == "backup" && RunState.Backups >= RunRules.MaxBackups);

        void Focus(int f)
        {
            _focus = f;
            for (int i = 0; i < 3; i++) _cards[i].SetFocus(i == f && i < _count);
            _continue.SetSelected(f >= _count);
        }

        void Choose(int i)
        {
            if (_left || _chosen >= 0 || i >= _count || !Usable(i)) return;
            _chosen = i;
            RunEvents.ChooseCooldown?.Invoke(i);
            Paint();
            Focus(3);
        }

        void Leave()
        {
            if (_left) return;
            _left = true;
            RunEvents.Leave?.Invoke();
        }

        public override void Tick(float dt)
        {
            _t += dt;
            if (_left || _t < 0.25f || Time.frameCount == _shownFrame) return;
            int step = UIKit.NavStep;
            if (step != 0) { int m = _count + 1, f = ((Mathf.Min(_focus, _count) + step) % m + m) % m; Focus(f >= _count ? 3 : f); }
            else if (UIKit.Confirm.WasPressedThisFrame()) { if (_focus >= _count || _chosen >= 0) Leave(); else Choose(_focus); }
        }
    }
}
