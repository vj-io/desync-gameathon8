using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Desync.UI
{
    /// F2 level select: six room buttons -> GameEvents.StartRequested(i). Keys 1-6 also work (keyboard fallback).
    public sealed class LevelSelectScreen : UIPanel
    {
        const int N = 6;
        readonly TMP_Text[] _info = new TMP_Text[N];
        readonly UIButton[] _btn = new UIButton[N];
        RectTransform _panel; Tween _pop;

        protected override void Create()
        {
            var cfg = GameConfig.I;
            var dim = UIKit.Img(Root, "Dim", new Color(0f, 0f, 0.03f, 0.8f)); UIKit.Stretch(dim.rectTransform); dim.raycastTarget = true;
            var panel = UIKit.Box(Root, "Panel", new Color(UIKit.Magenta.r, UIKit.Magenta.g, UIKit.Magenta.b, 0.8f), 0.94f);
            _panel = UIKit.Place(panel.rectTransform, Anc.C, Vector2.zero, new Vector2(1500, 800));

            var head = GlitchLabel.Create(panel.transform, "Head", Anc.T, new Vector2(0, -34), new Vector2(1200, 110), UIKit.Title, 76, UIKit.Paper, TextAlignmentOptions.Center, 3f, true, 8f);
            head.SetText("LEVEL SELECT");
            var sub = UIKit.Text(panel.transform, "Sub", UIKit.Hud, 24, UIKit.Dim, TextAlignmentOptions.Center, "DEBUG ACCESS · F2 TO CLOSE · KEYS 1-6");
            sub.characterSpacing = 6f; UIKit.Place(sub.rectTransform, Anc.T, new Vector2(0, -138), new Vector2(1200, 32));

            const float w = 440f, h = 230f, gx = 30f, gy = 30f;
            for (int i = 0; i < N; i++)
            {
                int col = i % 3, row = i / 3, room = i;
                float x = (col - 1) * (w + gx), y = -(200 + row * (h + gy));
                string name = cfg.roomNames != null && i < cfg.roomNames.Length ? cfg.roomNames[i] : "";
                var b = UIButton.Create(panel.transform, "Room" + i, "R" + (i + 1).ToString("00"), Anc.T, new Vector2(x, y), new Vector2(w, h), 64, i == 5 ? UIKit.Magenta : UIKit.Cyan, () => GameEvents.StartRequested?.Invoke(room));
                b.Label.rectTransform.anchorMin = new Vector2(0, 0.35f); b.Label.rectTransform.anchorMax = Vector2.one;
                b.Label.rectTransform.offsetMin = b.Label.rectTransform.offsetMax = Vector2.zero;
                var nt = UIKit.Text(b.transform, "Name", UIKit.Hud, 34, UIKit.Paper, TextAlignmentOptions.Center, name.ToUpperInvariant()); nt.characterSpacing = 8f;
                UIKit.Place(nt.rectTransform, Anc.B, new Vector2(0, 66), new Vector2(w - 20, 40));
                _info[i] = UIKit.Text(b.transform, "Info", UIKit.Hud, 24, UIKit.Dim, TextAlignmentOptions.Center);
                UIKit.Place(_info[i].rectTransform, Anc.B, new Vector2(0, 22), new Vector2(w - 20, 32));
                _btn[i] = b;
            }
        }

        public override void Show()
        {
            base.Show();
            var cfg = GameConfig.I;
            for (int i = 0; i < N; i++)
            {
                float par = cfg.parTime != null && i < cfg.parTime.Length ? cfg.parTime[i] : 0f;
                var r = GameState.Results != null && i < GameState.Results.Length ? GameState.Results[i] : default;
                _info[i].SetText(r.grade != 0 ? "PAR {0:0}s · BEST " + r.grade : "PAR {0:0}s", par);
            }
            UIKit.Pop(ref _pop, _panel, 0.9f, 0.22f);
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;   // buttons need a free pointer
            Cursor.visible = true;
        }

        public override void Hide() { UIKit.Kill(ref _pop); base.Hide(); }

        public override void Tick(float dt)
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            for (int i = 0; i < N; i++)
                if (kb[Key.Digit1 + i].wasPressedThisFrame) { GameEvents.StartRequested?.Invoke(i); return; }
        }
    }
}
