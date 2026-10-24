using Desync.Enemies;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// M3 additions to the in-room HUD: the colour-blind layer frame, subtitles and the decoy marker. Same rules as the rest of the HUD:
    /// polled state, text rewritten only when it changes, no allocation per frame.
    public sealed partial class HudScreen
    {
        // ---- layer frame: solid in RENDER, dashed in SOURCE (colour-blind palette only; colour is never the only cue)
        Image _frame;
        // ---- subtitles (bottom centre)
        GameObject _subGo; CanvasGroup _subGroup; TMP_Text _subWho, _subText; int _subSeq; float _subT;
        // ---- decoy marker (nearest live decoy: on screen where it is, at the screen edge when it is not)
        RectTransform _dmRt; TMP_Text _dmGlyph, _dmLabel; GameObject _dmGo; int _dmKind = -1;

        static readonly string[] DecoyGlyph = { "◇", "○", "◆" }, DecoyLabel = { "DECOY", "ECHO", "NOISE" };
        const float SubFade = 0.4f, DecoyRange = 60f;

        internal bool FrameOn => _frame && _frame.enabled;
        internal bool SubtitleOn => _subGo && _subGo.activeSelf;
        internal bool DecoyMarkerOn => _dmGo && _dmGo.activeSelf;
        internal string SubtitleText => _subText ? _subText.text : null;

        void BuildM3()
        {
            _frame = UIKit.Img(Root, "LayerFrame", UIKit.Cyan);
            UIKit.Stretch(_frame.rectTransform, 12, 12, 12, 12);
            _frame.transform.SetSiblingIndex(1);   // behind everything but the trace edge
            UIKit.SetBorder(_frame, 0, 1.5f);
            _frame.enabled = false;

            var sb = UIKit.Box(Root, "Subtitle", UIKit.Dim, 0.84f);
            UIKit.Place(sb.rectTransform, Anc.B, new Vector2(0, 18), new Vector2(980, 68)); _subGo = sb.gameObject;
            _subGroup = _subGo.AddComponent<CanvasGroup>();
            _subWho = UIKit.Text(sb.transform, "Who", UIKit.Hud, 22, UIKit.Magenta, TextAlignmentOptions.Left); _subWho.fontStyle = FontStyles.Bold; _subWho.characterSpacing = 4f;
            UIKit.Place(_subWho.rectTransform, Anc.L, new Vector2(16, 0), new Vector2(180, 40));
            _subText = UIKit.Text(sb.transform, "Text", UIKit.Term, 30, UIKit.Paper, TextAlignmentOptions.Left);
            _subText.textWrappingMode = TextWrappingModes.Normal; _subText.richText = false;
            UIKit.Place(_subText.rectTransform, Anc.R, new Vector2(-14, 0), new Vector2(750, 62));
            _subGo.SetActive(false);
            _subSeq = Subtitles.Seq;

            _dmRt = UIKit.Place(UIKit.Obj(Root, "DecoyMarker"), Anc.C, Vector2.zero, new Vector2(140, 80)); _dmGo = _dmRt.gameObject;
            _dmGlyph = UIKit.Text(_dmRt, "Glyph", UIKit.Hud, 46, UIKit.Cyan, TextAlignmentOptions.Center); _dmGlyph.richText = false;
            UIKit.Place(_dmGlyph.rectTransform, Anc.T, Vector2.zero, new Vector2(140, 50));
            _dmLabel = UIKit.Text(_dmRt, "Label", UIKit.Hud, 18, UIKit.Paper, TextAlignmentOptions.Center); _dmLabel.characterSpacing = 4f; _dmLabel.richText = false;
            UIKit.Place(_dmLabel.rectTransform, Anc.B, Vector2.zero, new Vector2(140, 24));
            _dmGo.SetActive(false);

            BuildBoss();
            _rebootGo.transform.SetAsLastSibling(); _hintGo.transform.SetAsLastSibling();   // the REBOOT wipe and the hint stay on top
        }

        /// "RENDER" / "SOURCE"; the colour-blind palette adds a shape (solid square / hatched square) so the tag reads without hue.
        static string LayerName(bool source) => Desync.Art.Palette.Colorblind ? (source ? "▒ SOURCE" : "■ RENDER") : (source ? "SOURCE" : "RENDER");

        void LayerFrame(bool source)
        {
            _frame.enabled = Desync.Art.Palette.Colorblind;
            UIKit.SetBorder(_frame, source ? 1 : 0, 1.5f);
            _frame.color = source ? UIKit.Green : UIKit.Cyan;
        }

        void ClearM3()
        {
            if (_subGo) _subGo.SetActive(false);
            _subT = 0f;
            if (_dmGo) _dmGo.SetActive(false);
        }

        void TickM3(float dt)
        {
            TickSubtitle(dt);
            TickDecoy();
        }

        void TickSubtitle(float dt)
        {
            if (Subtitles.Seq != _subSeq)
            {
                _subSeq = Subtitles.Seq;
                string line = Subtitles.Line;
                if (string.IsNullOrEmpty(line)) { _subT = 0f; _subGo.SetActive(false); }
                else
                {
                    _subWho.SetText(Subtitles.Who ?? "");
                    _subText.SetText(UIKit.Safe(line, UIKit.Term));
                    _subT = Subtitles.Seconds; _subGroup.alpha = 1f; _subGo.SetActive(true);
                }
            }
            if (_subT <= 0f) return;
            _subT -= dt;
            if (_subT <= 0f) { _subGo.SetActive(false); return; }
            _subGroup.alpha = _subT < SubFade ? _subT / SubFade : 1f;
        }

        void TickDecoy()
        {
            bool on = false;
            var cam = PlayerRef.Cam;
            if (cam && PlayerRef.Exists && Decoys.Count > 0 && Decoys.Nearest(PlayerRef.Position, DecoyRange, out var pos, out var kind))
            {
                on = true;
                int k = Mathf.Clamp((int)kind, 0, DecoyGlyph.Length - 1);
                if (k != _dmKind)
                {
                    _dmKind = k;
                    _dmGlyph.SetText(DecoyGlyph[k]); _dmLabel.SetText(DecoyLabel[k]);
                    var c = k == 1 ? UIKit.Green : k == 2 ? UIKit.Paper : UIKit.Cyan;   // palette hues only: no new threat colour
                    _dmGlyph.color = c; _dmLabel.color = c;
                }
                var vp = cam.WorldToViewportPoint(pos);
                if (vp.z < 0f) { vp.x = vp.x < 0.5f ? 0.96f : 0.04f; vp.y = 1f - vp.y; }   // behind the camera: pin to the side it is on
                vp.x = Mathf.Clamp(vp.x, 0.05f, 0.95f); vp.y = Mathf.Clamp(vp.y, 0.1f, 0.9f);
                var size = Root.rect.size;
                _dmRt.anchoredPosition = new Vector2((vp.x - 0.5f) * size.x, (vp.y - 0.5f) * size.y);
            }
            if (on != _dmGo.activeSelf) _dmGo.SetActive(on);
        }
    }
}
