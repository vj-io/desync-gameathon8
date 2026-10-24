using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Story-guided Room 1: entered straight from the STORY (watched or skipped), the first room shows one short ECHO prompt at the
    /// moment each skill is needed, read from GameState / LayerState only (no new events), and fades it once it is satisfied:
    /// at spawn "press SPACE" (until the first SOURCE), melt > 40 in SOURCE "back to RENDER" (until RENDER), near the terminal
    /// "press E" (until the hack starts), in the hack "arrows" (until it ends), exit open "the exit is open" (4 s). Nothing shows
    /// during a recap, results or pause. Progress survives a REBOOT and a canvas rebuild; leaving Room 1 disarms it. Classic play
    /// (no story first) never arms it. No allocation per frame.
    public sealed class StoryGuide : UIPanel
    {
        public enum Step { None, Flip, Melt, Terminal, Hack, Exit }

        /// StoryScreen sets it as it hands over to Room 1; UIManager clears it when anything but Room 1 starts.
        public static bool Armed;
        static bool _flipped, _melted, _exitDone;
        static float _exitT;

        const float MeltAt = 40f, NearTerminal = 6f, ExitShow = 4f, Cps = 50f, FadeOut = 0.35f;

        RectTransform _box; Image _frame; TMP_Text _text; Tween _fade, _pop;
        Desync.Enemies.Terminal[] _terms;
        Step _shown; float _chars; int _len, _vis;

        internal Step Shown => _shown;
        internal string Line => _shown == Step.None ? null : _text.text;

        protected override void Create()
        {
            Group.blocksRaycasts = false;
            var box = UIKit.Box(Root, "Prompt", UIKit.Cyan, 0.7f);
            _box = UIKit.Place(box.rectTransform, Anc.C, new Vector2(0, -170), new Vector2(1180, 88));
            _frame = box.transform.GetChild(0).GetComponent<Image>();
            var tag = UIKit.Text(box.transform, "Tag", UIKit.Hud, 24, UIKit.Dim, TextAlignmentOptions.Left, "ECHO //");
            tag.characterSpacing = 4f; UIKit.Place(tag.rectTransform, Anc.L, new Vector2(24, 0), new Vector2(140, 40));
            _text = UIKit.Text(box.transform, "Text", UIKit.Term, 50, UIKit.Paper, TextAlignmentOptions.Center);
            UIKit.Place(_text.rectTransform, Anc.C, new Vector2(40, 0), new Vector2(960, 80));
        }

        /// UIManager on every RoomStarted: Room 1 right after the story starts the guide from the top; anything else disarms it.
        public void RoomStarted(int room)
        {
            if (room != 0 || !Armed) { Leave(); return; }
            _flipped = _melted = _exitDone = false; _exitT = 0f; _terms = null;
            Show();
        }

        /// After a canvas rebuild (palette change): carry on where the old panel was.
        public void Resume() { if (Armed && GameState.RoomIndex == 0) Show(); }

        public void Leave() { Armed = false; Hide(); }

        public override void Show()
        {
            base.Show();
            UIKit.Kill(ref _fade); UIKit.Kill(ref _pop);
            Group.alpha = 0f; _shown = Step.None;
        }

        public override void Hide() { UIKit.Kill(ref _fade); UIKit.Kill(ref _pop); _shown = Step.None; base.Hide(); }

        public override void Tick(float dt)
        {
            var want = Want(dt);
            if (want != _shown) Switch(want);
            if (_vis < _len)
            {
                _chars += dt * Cps;
                int k = Mathf.Min(_len, (int)_chars);
                if (k != _vis) { _vis = k; _text.maxVisibleCharacters = k; }
            }
        }

        Step Want(float dt)
        {
            if (LayerState.InSource) _flipped = true;
            if (_shown == Step.Melt && !LayerState.InSource) _melted = true;
            if (!GameState.Alive || GameState.Paused || GameState.Phase != GameState.PhaseKind.Playing) return Step.None;
            if (GameState.ExitOpen)
            {
                if (!_exitDone && (_exitT += dt) >= ExitShow) _exitDone = true;
                return _exitDone ? Step.None : Step.Exit;
            }
            if (!_melted && LayerState.InSource && GameState.Melt > MeltAt) return Step.Melt;
            if (!_flipped) return Step.Flip;
            if (GameState.InHack) return Step.Hack;
            if (GameState.TerminalsDone < GameState.TerminalsTotal && NearTheTerminal()) return Step.Terminal;
            return Step.None;
        }

        /// Looking at it (the E prompt is up) or within NearTerminal metres of an unhacked terminal.
        bool NearTheTerminal()
        {
            if (GameState.InteractPrompt != null) return true;
            if (!PlayerRef.Exists) return false;
            if (_terms == null) _terms = Object.FindObjectsByType<Desync.Enemies.Terminal>();   // once per room
            var p = PlayerRef.Position;
            for (int i = 0; i < _terms.Length; i++)
            {
                var t = _terms[i];
                if (t && !t.Done && (t.transform.position - p).sqrMagnitude < NearTerminal * NearTerminal) return true;
            }
            return false;
        }

        void Switch(Step want)
        {
            _shown = want;
            UIKit.Kill(ref _fade);
            if (want == Step.None) { _fade = Tween.Alpha(Group, 0f, FadeOut, Ease.OutQuad, useUnscaledTime: true); return; }
            var g = StoryScript.I.guide;
            string line; Color c;
            switch (want)
            {
                case Step.Flip: line = Rebinds.FlipText(g.flip); c = UIKit.Cyan; break;   // "Press SPACE": the flip key as bound
                case Step.Melt: line = g.melt; c = UIKit.Danger; break;
                case Step.Terminal: line = g.terminal; c = UIKit.Cyan; break;
                case Step.Hack: line = g.hack; c = UIKit.Magenta; break;
                default: line = g.exit; c = UIKit.Green; break;
            }
            line = UIKit.Safe(line, UIKit.Term) ?? "";
            _text.SetText(line); _frame.color = c;
            _len = line.Length; _chars = 0f; _vis = 0; _text.maxVisibleCharacters = 0;
            Group.alpha = 1f;
            UIKit.Pop(ref _pop, _box, 1.08f, 0.25f, Ease.OutCubic);
        }
    }
}
