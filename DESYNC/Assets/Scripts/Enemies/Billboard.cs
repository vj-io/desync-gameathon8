using TMPro;
using UnityEngine;

namespace Desync.Enemies
{
    /// HALCYON propaganda poster (mat.poster + TMP). SOURCE shows the slogan corrupted; the finale (Architect) calls
    /// ShowFace to put a board member's face and name on it.
    public class Billboard : MonoBehaviour, IResettable
    {
        public static readonly string[] Members =
        {
            "CHAIR // V. HALCYON", "CFO // M. ORREN", "CTO // D. KASPRZAK", "COO // R. VELL", "LEGAL // S. AMARI", "BOARD // [REDACTED]",
        };

        public TextMeshPro text;
        public GameObject face;
        public string slogan, corrupt;

        bool _face;

        void OnEnable() { LayerState.Changed += OnLayer; OnLayer(LayerState.InSource); }
        void OnDisable() => LayerState.Changed -= OnLayer;

        /// SOURCE: the poster itself turns to green code, so the slogan corrupts and dims to match.
        void OnLayer(bool source)
        {
            if (_face || !text) return;
            text.text = source ? corrupt : slogan;
            var g = GameConfig.I.green;
            text.color = source ? new Color(g.r, g.g, g.b, 0.45f) : Color.white;
        }

        public void ShowFace(int member)
        {
            _face = true;
            if (face) face.SetActive(true);
            if (!text) return;
            text.text = Members[Mathf.Abs(member) % Members.Length];
            text.color = GameConfig.I.green;
            text.alignment = TextAlignmentOptions.Bottom;
        }

        public void ResetState()
        {
            _face = false;
            if (face) face.SetActive(false);
            if (text) text.alignment = TextAlignmentOptions.Center;
            OnLayer(LayerState.InSource);
        }
    }
}
