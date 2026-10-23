using TMPro;
using UnityEngine;

namespace Desync.UI
{
    /// RGB-split text: a magenta ghost, a cyan ghost and the main label stacked. Glitch() jitters them,
    /// Reveal() decodes the text from random characters. Zero allocation per frame.
    public sealed class GlitchLabel : MonoBehaviour
    {
        public TMP_Text Main, A, B;
        public float Split = 2f;          // resting ghost offset in canvas px
        public float IdleEvery;           // >0: random micro-glitch every ~N seconds
        float _glitchT, _glitchDur = 0.3f, _glitchAmp;
        float _idleT;
        bool _atRest;
        string _target; float _revealT, _revealDur; bool _revealing; char[] _buf = new char[64];
        const string Scramble = "01#$%&@<>/\\|=+*?ABCDEFXYZ";

        public static GlitchLabel Create(Transform parent, string name, Anc a, Vector2 pos, Vector2 size, TMP_FontAsset font, float fontSize,
            Color color, TextAlignmentOptions align = TextAlignmentOptions.Center, float split = 2f, bool bold = false, float spacing = 0f)
        {
            var rt = UIKit.Place(UIKit.Obj(parent, name), a, pos, size);
            var gl = rt.gameObject.AddComponent<GlitchLabel>();
            gl.Split = split;
            var ca = new Color(UIKit.Magenta.r, UIKit.Magenta.g, UIKit.Magenta.b, 0.8f);
            var cb = new Color(UIKit.Cyan.r, UIKit.Cyan.g, UIKit.Cyan.b, 0.8f);
            gl.A = Make(rt, "GhostA", font, fontSize, ca, align, bold, spacing);
            gl.B = Make(rt, "GhostB", font, fontSize, cb, align, bold, spacing);
            gl.Main = Make(rt, "Main", font, fontSize, color, align, bold, spacing);
            gl.Rest(true);
            return gl;
        }

        static TMP_Text Make(RectTransform parent, string n, TMP_FontAsset f, float size, Color c, TextAlignmentOptions al, bool bold, float spacing)
        {
            var t = UIKit.Text(parent, n, f, size, c, al);
            UIKit.Stretch(t.rectTransform);
            if (bold) t.fontStyle = FontStyles.Bold;
            t.characterSpacing = spacing;
            return t;
        }

        public void SetText(string s)
        {
            _revealing = false;
            Main.SetText(s); A.SetText(s); B.SetText(s);
        }

        public void SetColor(Color c) { Main.color = c; }

        public void Show(bool on) { A.enabled = B.enabled = Main.enabled = on; }

        public void Glitch(float amp, float dur)
        {
            if (!UIKit.Flashes) amp *= 0.25f;
            if (amp <= 0f) return;
            _glitchAmp = Mathf.Max(_glitchAmp * Mathf.Clamp01(_glitchT / _glitchDur), amp * 18f);
            _glitchDur = Mathf.Max(0.05f, dur); _glitchT = _glitchDur;
        }

        /// Decode `s` from random characters over `dur` seconds (left to right).
        public void Reveal(string s, float dur)
        {
            _target = s; _revealDur = Mathf.Max(0.05f, dur); _revealT = 0f; _revealing = true;
            if (_buf.Length < s.Length) _buf = new char[s.Length * 2];
            Fill(0f);
        }

        void Fill(float p)
        {
            int n = _target.Length, fixedN = Mathf.FloorToInt(p * n);
            for (int i = 0; i < n; i++)
                _buf[i] = (i < fixedN || _target[i] == ' ' || _target[i] == '\n') ? _target[i] : Scramble[Random.Range(0, Scramble.Length)];
            Main.SetText(_buf, 0, n); A.SetText(_buf, 0, n); B.SetText(_buf, 0, n);
        }

        void Rest(bool force)
        {
            if (_atRest && !force) return;
            _atRest = true;
            A.rectTransform.anchoredPosition = new Vector2(-Split, 0f);
            B.rectTransform.anchoredPosition = new Vector2(Split, 0f);
            Main.rectTransform.anchoredPosition = Vector2.zero;
        }

        void OnEnable() { _idleT = IdleEvery > 0 ? Random.Range(IdleEvery * 0.6f, IdleEvery * 1.4f) : 0f; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_revealing)
            {
                _revealT += dt;
                float p = _revealT / _revealDur;
                if (p >= 1f) { _revealing = false; Main.SetText(_target); A.SetText(_target); B.SetText(_target); }
                else Fill(p);
            }
            if (IdleEvery > 0f)
            {
                _idleT -= dt;
                if (_idleT <= 0f) { Glitch(0.6f, 0.18f); _idleT = Random.Range(IdleEvery * 0.6f, IdleEvery * 1.4f); }
            }
            if (_glitchT > 0f)
            {
                _glitchT -= dt;
                float k = Mathf.Clamp01(_glitchT / _glitchDur), a = _glitchAmp * k;
                _atRest = false;
                A.rectTransform.anchoredPosition = new Vector2(-Split - Random.Range(0f, a), Random.Range(-a, a) * 0.3f);
                B.rectTransform.anchoredPosition = new Vector2(Split + Random.Range(0f, a), Random.Range(-a, a) * 0.3f);
                Main.rectTransform.anchoredPosition = new Vector2(Random.Range(-a, a) * 0.2f, Random.Range(-a, a) * 0.1f);
            }
            else Rest(false);
        }
    }
}
