using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// GameCompleted: full-screen "SIMULATION: COMPROMISED".
    public sealed class FinaleScreen : UIPanel
    {
        GlitchLabel _title; TMP_Text _sub; Image _scrim; Tween _fade, _subTw; float _t; bool _subShown;

        protected override void Create()
        {
            Group.blocksRaycasts = false;
            _scrim = UIKit.Img(Root, "Scrim", new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0f)); UIKit.Stretch(_scrim.rectTransform);
            var v = UIKit.Img(Root, "Vignette", new Color(UIKit.Magenta.r, UIKit.Magenta.g, UIKit.Magenta.b, 0.35f), UIKit.Vignette); UIKit.Stretch(v.rectTransform);
            _title = GlitchLabel.Create(Root, "Title", Anc.C, new Vector2(0, 70), new Vector2(1800, 400), UIKit.Title, 150, UIKit.Paper, TextAlignmentOptions.Center, 6f, true, 10f);
            _title.IdleEvery = 0.9f;
            _sub = UIKit.Text(Root, "Sub", UIKit.Term, 56, UIKit.Green, TextAlignmentOptions.Center, "THE ARCHITECTS ARE EXPOSED. THE CITY IS WATCHING.");
            UIKit.Place(_sub.rectTransform, Anc.C, new Vector2(0, -190), new Vector2(1700, 80));
        }

        public override void Show()
        {
            base.Show();
            UIKit.Kill(ref _fade);
            _scrim.color = new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0f);
            _fade = Tween.Alpha(_scrim, 0.88f, 0.5f, Ease.OutQuad, useUnscaledTime: true);
            _title.Reveal("SIMULATION:\nCOMPROMISED", 0.9f);
            _title.Glitch(2.5f, 0.6f);
            _sub.gameObject.SetActive(false); _subShown = false; _t = 0f;
            GameEvents.ShakeRequest?.Invoke(0.7f);
        }

        public override void Hide() { UIKit.Kill(ref _fade); UIKit.Kill(ref _subTw); base.Hide(); }

        public override void Tick(float dt)
        {
            _t += dt;
            if (!_subShown && _t > 1.3f)
            {
                _subShown = true; _sub.gameObject.SetActive(true);
                UIKit.Pop(ref _subTw, _sub.transform, 0.7f, 0.4f);
            }
        }
    }
}
