using System;
using PrimeTween;
using TMPro;
using UnityEngine;

namespace Desync.UI
{
    /// KEY BINDINGS: one row per verb (move x4, flip, hack, restart, pause, map / goals) with a keyboard and a pad column. Click a
    /// key, press the new one; Esc cancels; a key another verb already uses swaps the two, so FLIP is never duplicated.
    /// RESET DEFAULTS restores every key and button. Reached from the pause menu and from SOUND + ACCESS.
    public sealed class RebindScreen : UIPanel
    {
        const float RowY0 = -268f, RowGap = 54f;

        UIButton[] _key, _pad;
        TMP_Text _msg;
        RectTransform _panel; Tween _pop;
        Rebinds.Slot _waiting; bool _waitPad;
        internal Action OnBack;
        internal bool IsCapturing => _waiting != null;

        protected override void Create()
        {
            var cyan = UIKit.Cyan; var magenta = UIKit.Magenta; var paper = UIKit.Paper; var dim = UIKit.Dim;
            var dimmer = UIKit.Img(Root, "Dim", new Color(0f, 0f, 0.03f, 0.9f)); UIKit.Stretch(dimmer.rectTransform); dimmer.raycastTarget = true;
            dimmer.gameObject.AddComponent<UIBlocker>();
            var panel = UIKit.Box(Root, "Panel", new Color(cyan.r, cyan.g, cyan.b, 0.7f), 1f);   // opaque: the pause menu underneath must not show through
            panel.raycastTarget = true; panel.gameObject.AddComponent<UIBlocker>();
            _panel = UIKit.Place(panel.rectTransform, Anc.C, Vector2.zero, new Vector2(1240, 980));

            var head = UIKit.Text(panel.transform, "Head", UIKit.Title, 64, paper, TextAlignmentOptions.Center, "KEY BINDINGS"); head.fontStyle = FontStyles.Bold; head.characterSpacing = 3f;
            UIKit.Place(head.rectTransform, Anc.T, new Vector2(0, -34), new Vector2(1100, 90));
            var sub = UIKit.Text(panel.transform, "Sub", UIKit.Hud, 24, dim, TextAlignmentOptions.Center, "CLICK A KEY, THEN PRESS THE NEW ONE · ESC CANCELS · A KEY IN USE SWAPS"); sub.characterSpacing = 4f;
            UIKit.Place(sub.rectTransform, Anc.T, new Vector2(0, -130), new Vector2(1160, 32));

            var hk = UIKit.Text(panel.transform, "KeyboardHead", UIKit.Title, 26, cyan, TextAlignmentOptions.Center, "KEYBOARD"); hk.fontStyle = FontStyles.Bold; hk.characterSpacing = 6f;
            UIKit.Place(hk.rectTransform, Anc.T, new Vector2(150, -208), new Vector2(300, 36));
            var hp = UIKit.Text(panel.transform, "PadHead", UIKit.Title, 26, cyan, TextAlignmentOptions.Center, "GAMEPAD"); hp.fontStyle = FontStyles.Bold; hp.characterSpacing = 6f;
            UIKit.Place(hp.rectTransform, Anc.T, new Vector2(440, -208), new Vector2(300, 36));

            var slots = Rebinds.Slots;
            _key = new UIButton[slots.Length]; _pad = new UIButton[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                int k = i;
                float y = RowY0 - i * RowGap;
                bool flip = slots[i].Id == "flip";
                var l = UIKit.Text(panel.transform, "Row" + i, UIKit.Hud, 30, flip ? magenta : paper, TextAlignmentOptions.Left, slots[i].Label);
                UIKit.Place(l.rectTransform, Anc.T, new Vector2(-330, y), new Vector2(420, 44));
                _key[i] = UIButton.Create(panel.transform, "Key" + i, "", Anc.T, new Vector2(150, y), new Vector2(270, 46), 26, flip ? magenta : cyan, () => Begin(k, false), UIKit.Hud);
                _pad[i] = UIButton.Create(panel.transform, "Pad" + i, "", Anc.T, new Vector2(440, y), new Vector2(270, 46), 26, flip ? magenta : cyan, () => Begin(k, true), UIKit.Hud);
                if (!slots[i].PadRebindable) { _pad[i].Base = new Color(dim.r, dim.g, dim.b, 0.7f); _pad[i].Hot = _pad[i].Base; }   // a stick or nothing: shown, not clickable
            }

            _msg = UIKit.Text(panel.transform, "Msg", UIKit.Hud, 28, UIKit.Green, TextAlignmentOptions.Center);
            UIKit.Place(_msg.rectTransform, Anc.T, new Vector2(0, RowY0 - slots.Length * RowGap - 10), new Vector2(1100, 40));
            var note = UIKit.Text(panel.transform, "Note", UIKit.Hud, 22, dim, TextAlignmentOptions.Center, "FLIP IS ONE KEY. MOUSE LOOK, THE ARROW-KEY HACK CODE AND THE PAD STICKS ARE FIXED.");
            UIKit.Place(note.rectTransform, Anc.T, new Vector2(0, RowY0 - slots.Length * RowGap - 54), new Vector2(1160, 32));

            UIButton.Create(panel.transform, "ResetKeys", "RESET DEFAULTS", Anc.B, new Vector2(-300, 54), new Vector2(420, 86), 32, magenta, () => { Rebinds.Cancel(); _waiting = null; Rebinds.ResetAll(); Say("ALL KEYS RESET", UIKit.Green); Paint(); });
            UIButton.Create(panel.transform, "Back", "BACK", Anc.B, new Vector2(300, 54), new Vector2(420, 86), 40, cyan, () => { Stop(); OnBack?.Invoke(); });
        }

        void Say(string s, Color c) { _msg.SetText(s); _msg.color = c; }

        void Begin(int i, bool pad)
        {
            var s = Rebinds.Slots[i];
            if (pad && !s.PadRebindable) { Say("THE PAD STICK IS FIXED", UIKit.Dim); return; }
            _waiting = s; _waitPad = pad;
            Rebinds.Capture(pad);
            Paint();
            Say(pad ? "PRESS A GAMEPAD BUTTON FOR " + s.Label : "PRESS A KEY FOR " + s.Label + "  (ESC CANCELS)", UIKit.Cyan);
        }

        /// Ends this screen's own capture; a capture someone else started is left alone.
        void Stop() { if (_waiting != null) Rebinds.Cancel(); _waiting = null; }

        /// Applies a finished capture (called by Tick; public for tests, which feed a path instead of pressing a key).
        internal void Finish(string path)
        {
            var s = _waiting;
            if (s == null) return;
            _waiting = null; Rebinds.Cancel();
            if (string.IsNullOrEmpty(path)) { Say("CANCELLED", UIKit.Dim); Paint(); return; }
            if (Rebinds.Set(s, _waitPad, path, out var swapped))
                Say(s.Label + " = " + Rebinds.Label(path) + (swapped != null ? "   (SWAPPED WITH " + swapped.Label + ")" : ""), UIKit.Green);
            else Say(Rebinds.Label(path) + " CANNOT BE USED HERE", UIKit.Danger);
            Paint();
        }

        public override void Tick(float dt)
        {
            if (_waiting != null && Rebinds.TakeCapture(out var path)) Finish(path);
        }

        void Paint()
        {
            var slots = Rebinds.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                bool wk = _waiting == slots[i] && !_waitPad, wp = _waiting == slots[i] && _waitPad;
                _key[i].Label.SetText(wk ? "PRESS..." : Rebinds.Label(slots[i], false)); _key[i].SetSelected(wk);
                bool fixedPad = !slots[i].PadRebindable;
                _pad[i].Label.SetText(wp ? "PRESS..." : fixedPad ? (slots[i].Part != null ? "L STICK" : "-") : Rebinds.Label(slots[i], true)); _pad[i].SetSelected(wp);
            }
        }

        public override void Show()
        {
            base.Show();
            Stop();
            Say("", UIKit.Green);
            Paint();
            UIKit.FreeCursor();
            UIKit.Pop(ref _pop, _panel, 0.92f, 0.2f);
        }

        public override void Hide() { Stop(); UIKit.Kill(ref _pop); base.Hide(); }
    }
}
