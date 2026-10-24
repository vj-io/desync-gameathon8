using System;
using Desync.Flow;
using Desync.Net;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// ARCHIVE (UIManager.OpenArchive): the 30 story shards in three act columns (HALCYON memos, board transcripts, Architect
    /// logs), a reader for the focused one, and the endings seen, with what the true ending still needs. Shards not found yet
    /// show as locked slots. One linear focus ring (30 slots, then BACK): arrows / WASD / d-pad move it, hover moves it, SPACE
    /// or a click on BACK (or ESC anywhere) closes.
    public sealed class ShardArchive : UIPanel
    {
        const float SlotW = 360f, SlotH = 56f, SlotGap = 8f, ColGap = 20f, InputGuard = 0.25f;
        const int Slots = ShardBook.Total;

        readonly UIButton[] _slot = new UIButton[Slots];
        readonly Image[] _ring = new Image[Slots + 1];
        readonly TMP_Text[] _endName = new TMP_Text[3], _endState = new TMP_Text[3];
        readonly Image[] _endFrame = new Image[3];
        UIButton _back;
        TMP_Text _count, _readMeta, _readTitle, _readText, _need;
        RectTransform _panel; Tween _pop;
        int _focus, _shownFrame = -1; float _t;

        internal int Focus => _focus;
        internal int Collected { get; private set; }
        internal string ReaderTitle => _readTitle.text;
        internal string NeedText => _need.text;

        protected override void Create()
        {
            var dim = UIKit.Img(Root, "Dim", new Color(0f, 0f, 0.03f, 0.92f)); UIKit.Stretch(dim.rectTransform); dim.raycastTarget = true;
            var panel = UIKit.Box(Root, "Panel", new Color(UIKit.Green.r, UIKit.Green.g, UIKit.Green.b, 0.8f), 0.95f);
            _panel = UIKit.Place(panel.rectTransform, Anc.C, Vector2.zero, new Vector2(1840, 1020));

            var head = UIKit.Text(panel.transform, "Head", UIKit.Title, 54, UIKit.Paper, TextAlignmentOptions.Left, "ARCHIVE");
            head.fontStyle = FontStyles.Bold; head.characterSpacing = 8f;
            UIKit.Place(head.rectTransform, Anc.TL, new Vector2(40, -20), new Vector2(700, 70));
            _count = UIKit.Text(panel.transform, "Count", UIKit.Hud, 26, UIKit.Green, TextAlignmentOptions.Right);
            _count.characterSpacing = 4f; UIKit.Place(_count.rectTransform, Anc.TR, new Vector2(-40, -40), new Vector2(900, 36));

            var content = ShardBook.Content;
            for (int a = 0; a < ShardBook.Acts; a++)
            {
                float x = 40f + a * (SlotW + ColGap);
                var act = UIKit.Text(panel.transform, "Act" + (a + 1), UIKit.Hud, 22, ActColor(a + 1), TextAlignmentOptions.Left,
                                     UIKit.Safe("ACT " + (a + 1) + " · " + content.Act(a + 1), UIKit.Hud));
                act.characterSpacing = 3f; UIKit.Place(act.rectTransform, Anc.TL, new Vector2(x, -108), new Vector2(SlotW, 30));
                for (int k = 0; k < ShardBook.PerAct; k++)
                {
                    int i = a * ShardBook.PerAct + k;
                    var b = UIButton.Create(panel.transform, "Slot" + i, "", Anc.TL, new Vector2(x, -(148 + k * (SlotH + SlotGap))), new Vector2(SlotW, SlotH), 22, UIKit.Dim, () => SetFocus(i), UIKit.Hud);
                    b.Label.alignment = TextAlignmentOptions.Left; b.Label.richText = false;
                    UIKit.Stretch(b.Label.rectTransform, 16, 0, 10, 0);
                    _slot[i] = b;
                    Ring(i, b);
                }
            }

            // reader
            var reader = UIKit.Box(panel.transform, "Reader", new Color(UIKit.Cyan.r, UIKit.Cyan.g, UIKit.Cyan.b, 0.6f), 0.9f);
            UIKit.Place(reader.rectTransform, Anc.TR, new Vector2(-40, -108), new Vector2(620, 520));
            _readMeta = UIKit.Text(reader.transform, "Meta", UIKit.Hud, 22, UIKit.Dim, TextAlignmentOptions.Left); _readMeta.characterSpacing = 3f;
            UIKit.Place(_readMeta.rectTransform, Anc.TL, new Vector2(28, -22), new Vector2(570, 30));
            _readTitle = UIKit.Text(reader.transform, "Title", UIKit.Title, 34, UIKit.Paper, TextAlignmentOptions.Left); _readTitle.fontStyle = FontStyles.Bold;
            _readTitle.textWrappingMode = TextWrappingModes.Normal; _readTitle.richText = false;
            UIKit.Place(_readTitle.rectTransform, Anc.TL, new Vector2(28, -62), new Vector2(570, 90));
            _readText = UIKit.Text(reader.transform, "Text", UIKit.Term, 40, UIKit.Green, TextAlignmentOptions.TopLeft);
            _readText.textWrappingMode = TextWrappingModes.Normal; _readText.richText = false;
            UIKit.Place(_readText.rectTransform, Anc.TL, new Vector2(28, -160), new Vector2(570, 340));

            // endings
            var endHead = UIKit.Text(panel.transform, "EndingsHead", UIKit.Hud, 22, UIKit.Paper, TextAlignmentOptions.Left, "ENDINGS");
            endHead.characterSpacing = 3f; UIKit.Place(endHead.rectTransform, Anc.TR, new Vector2(-40, -652), new Vector2(620, 30));
            for (int e = 0; e < 3; e++)
            {
                var box = UIKit.Img(panel.transform, "Ending" + e, new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.85f));
                UIKit.Place(box.rectTransform, Anc.TR, new Vector2(-40 - (2 - e) * 210, -692), new Vector2(200, 110));
                _endFrame[e] = UIKit.Img(box.transform, "Frame", UIKit.Dim, UIKit.Frame, Image.Type.Sliced); UIKit.Stretch(_endFrame[e].rectTransform);
                _endName[e] = UIKit.Text(box.transform, "Name", UIKit.Title, 28, UIKit.Paper, TextAlignmentOptions.Center, Endings.All[e].ToUpperInvariant());
                _endName[e].fontStyle = FontStyles.Bold; UIKit.Place(_endName[e].rectTransform, Anc.T, new Vector2(0, -18), new Vector2(190, 40));
                _endState[e] = UIKit.Text(box.transform, "State", UIKit.Hud, 20, UIKit.Dim, TextAlignmentOptions.Center); _endState[e].characterSpacing = 2f;
                UIKit.Place(_endState[e].rectTransform, Anc.B, new Vector2(0, 14), new Vector2(190, 30));
            }
            _need = UIKit.Text(panel.transform, "Need", UIKit.Hud, 22, UIKit.Dim, TextAlignmentOptions.Left);
            _need.textWrappingMode = TextWrappingModes.Normal; _need.characterSpacing = 2f;
            UIKit.Place(_need.rectTransform, Anc.TR, new Vector2(-40, -816), new Vector2(620, 60));

            _back = UIButton.Create(panel.transform, "Back", "BACK", Anc.BR, new Vector2(-40, 40), new Vector2(300, 84), 32, UIKit.Cyan, Hide, UIKit.Hud);
            Ring(Slots, _back);
        }

        void Ring(int idx, UIButton b)
        {
            var r = UIKit.Img(b.transform, "Focus", UIKit.Paper, UIKit.Frame, Image.Type.Sliced); UIKit.Stretch(r.rectTransform, -6, -6, -6, -6); r.enabled = false;
            _ring[idx] = r;
            b.Hovered = () => SetFocus(idx);
        }

        static Color ActColor(int act) => act == 1 ? UIKit.Cyan : act == 2 ? UIKit.Magenta : UIKit.Green;

        public override void Show()
        {
            base.Show();
            _t = 0f; _shownFrame = Time.frameCount;
            var save = SaveService.Data;
            var content = ShardBook.Content;
            Collected = ShardBook.Count(save);
            _count.SetText("PUPPET STRINGS " + Collected + " / " + ShardBook.Total);
            int first = -1;
            for (int i = 0; i < Slots; i++)
            {
                int act = i / ShardBook.PerAct + 1, n = i % ShardBook.PerAct + 1;
                string id = ShardBook.Id(act, n);
                var s = ShardBook.Has(save, id) ? content.Find(id) : null;
                var b = _slot[i];
                bool have = s != null;
                b.Label.SetText(UIKit.Safe(n.ToString("00") + "  " + (have ? s.title : "------"), UIKit.Hud));
                b.Base = have ? ActColor(act) : new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, 0.45f);
                b.Hot = have ? UIKit.Paper : b.Base;
                b.SetSelected(false);
                if (have && first < 0) first = i;
            }
            for (int e = 0; e < 3; e++)
            {
                bool seen = Endings.Seen(save, Endings.All[e]);
                var c = seen ? (e == 2 ? UIKit.Green : e == 0 ? UIKit.Cyan : UIKit.Magenta) : new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, 0.5f);
                _endFrame[e].color = c; _endName[e].color = seen ? UIKit.Paper : UIKit.Dim;
                _endState[e].SetText(seen ? "SEEN" : "NOT YET"); _endState[e].color = c;
            }
            int kills = Endings.ArchitectKills(save);
            _need.SetText(Endings.Seen(save, Endings.Desync)
                ? "TRUE ENDING SEEN · ENDLESS OVERCLOCK IS OPEN"
                : "TRUE ENDING: ARCHITECT KILLS " + Mathf.Min(kills, Endings.KillsForDesync) + " / " + Endings.KillsForDesync
                  + " · PUPPET STRINGS " + Collected + " / " + ShardBook.Total);
            _need.color = Endings.DesyncReady(save) ? UIKit.Green : UIKit.Dim;
            SetFocus(first >= 0 ? first : 0);
            UIKit.Pop(ref _pop, _panel, 0.94f, 0.2f);
            UIKit.FreeCursor();
        }

        public override void Hide() { UIKit.Kill(ref _pop); base.Hide(); }

        void SetFocus(int f)
        {
            int count = Slots + 1;
            _focus = (f % count + count) % count;
            for (int i = 0; i < count; i++) if (_ring[i]) _ring[i].enabled = i == _focus;
            if (_focus < Slots) Read(_focus);
        }

        void Read(int i)
        {
            int act = i / ShardBook.PerAct + 1, n = i % ShardBook.PerAct + 1;
            string id = ShardBook.Id(act, n);
            var s = ShardBook.Has(SaveService.Data, id) ? ShardBook.Content.Find(id) : null;
            _readMeta.SetText(UIKit.Safe("ACT " + act + " · " + ShardBook.Content.Act(act) + " · " + n.ToString("00") + " / " + ShardBook.PerAct, UIKit.Hud));
            _readTitle.SetText(s != null ? UIKit.Safe(s.title, UIKit.Title) : "NOT FOUND YET");
            _readTitle.color = s != null ? ActColor(act) : UIKit.Dim;
            _readText.SetText(s != null ? UIKit.Safe(s.text, UIKit.Term)
                                        : "A puppet string hangs somewhere in act " + act + ". Look in SOURCE, behind the walls that flicker.");
            _readText.color = s != null ? UIKit.Green : UIKit.Dim;
        }

        public override void Tick(float dt)
        {
            _t += dt;
            UIKit.FreeCursor();
            if (_t < InputGuard || Time.frameCount == _shownFrame) return;   // the key that opened the screen must not also act
            if (GameInput.Pause.WasPressedThisFrame()) { Hide(); return; }
            int step = UIKit.NavStep;
            if (step != 0) SetFocus(_focus + step);
            else if (_focus == Slots && UIKit.Confirm.WasPressedThisFrame()) Hide();
        }
    }

    /// A short toast when a story shard is collected (ShardEvents.Collected): which string of the 30, and its title.
    public sealed class ShardToast : UIPanel
    {
        const float Hold = 3f, FadeOut = 0.4f;

        TMP_Text _line, _title;
        RectTransform _box;
        Tween _pop, _fade;
        float _left;

        protected override void Create()
        {
            var box = UIKit.Box(Root, "Box", new Color(UIKit.Green.r, UIKit.Green.g, UIKit.Green.b, 0.85f), 0.9f);
            _box = UIKit.Place(box.rectTransform, Anc.T, new Vector2(0, -150), new Vector2(760, 100));
            _line = UIKit.Text(box.transform, "Line", UIKit.Hud, 22, UIKit.Green, TextAlignmentOptions.Center); _line.characterSpacing = 4f;
            UIKit.Place(_line.rectTransform, Anc.T, new Vector2(0, -12), new Vector2(740, 30));
            _title = UIKit.Text(box.transform, "Title", UIKit.Title, 34, UIKit.Paper, TextAlignmentOptions.Center); _title.fontStyle = FontStyles.Bold;
            _title.richText = false; UIKit.Place(_title.rectTransform, Anc.B, new Vector2(0, 12), new Vector2(740, 48));
        }

        /// ShardEvents.Collected.
        public void OnCollected(string id)
        {
            var s = ShardBook.Content.Find(id);
            int n = ShardBook.Count(SaveService.Data);
            _line.SetText("PUPPET STRING " + n.ToString("00") + " / " + ShardBook.Total + "  ·  ARCHIVE");
            _title.SetText(UIKit.Safe(s != null ? s.title : id, UIKit.Title));
            Show();
            _left = Hold;
            UIKit.Kill(ref _fade);
            Group.alpha = 1f;
            UIKit.Pop(ref _pop, _box, 0.7f, 0.3f);
        }

        public override void Hide() { UIKit.Kill(ref _pop); UIKit.Kill(ref _fade); base.Hide(); }

        public override void Tick(float dt)
        {
            if (_left <= 0f) return;
            _left -= dt;
            if (_left <= 0f) { Hide(); return; }
            if (_left <= FadeOut && !_fade.isAlive) UIKit.Fade(ref _fade, Group, 0f, _left);
        }
    }
}
