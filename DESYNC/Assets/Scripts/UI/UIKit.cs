using System.Text;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    public enum Anc { TL, T, TR, L, C, R, BL, B, BR }

    /// Shared look + tiny builders for the code-built uGUI. All sprites are generated once at runtime (no art assets).
    public static class UIKit
    {
        public static TMP_FontAsset Hud, Term, Title;
        public static Color Cyan = new Color(0f, 0.94f, 1f), Magenta = new Color(1f, 0.17f, 0.84f), Green = new Color(0f, 1f, 0.4f), Danger = new Color(1f, 0.15f, 0.2f);
        public static readonly Color Ink = new Color(0.02f, 0.025f, 0.07f, 1f);
        public static readonly Color Panel = new Color(0.02f, 0.03f, 0.08f, 0.78f);
        public static readonly Color Dim = new Color(0.62f, 0.68f, 0.82f, 1f);
        public static readonly Color Paper = new Color(0.86f, 0.93f, 1f, 1f);
        public static Sprite White, Frame, Vignette, Disc, Ring;
        static bool _init;

        // Settings live in PlayerPrefs: poll them at most twice a second instead of every frame.
        static float _pollAt; static bool _flashes = true, _shake = true;
        static void Poll()
        {
            float t = Time.unscaledTime;
            if (t < _pollAt && t > _pollAt - 0.6f) return;
            _pollAt = t + 0.5f; _flashes = !Settings.ReduceFlashes; _shake = Settings.Shake;
        }
        public static bool Flashes { get { Poll(); return _flashes; } }
        public static bool Shake { get { Poll(); return _shake; } }
        public static void RefreshSettings() { _pollAt = 0f; Poll(); }

        public static void Init()
        {
            if (_init && White) return;
            _init = true;
            var c = GameConfig.I;
            Cyan = c.cyan; Magenta = c.magenta; Green = c.green; Danger = c.danger;
            Hud = AssetLib.Get<TMP_FontAsset>("font.hud"); if (!Hud) Hud = TMP_Settings.GetFontAsset();
            Term = AssetLib.Get<TMP_FontAsset>("font.term"); if (!Term) Term = Hud;
            Title = AssetLib.Get<TMP_FontAsset>("font.title"); if (!Title) Title = Hud;

            White = MakeSprite(Tex(4, 4, FilterMode.Point, (x, y) => 1f));
            // 16px frame, 2px line, 9-sliced.
            Frame = MakeSprite(Tex(16, 16, FilterMode.Point, (x, y) => (x < 2 || y < 2 || x > 13 || y > 13) ? 1f : 0f), new Vector4(2, 2, 2, 2));
            Vignette = MakeSprite(Tex(64, 64, FilterMode.Bilinear, (x, y) =>
            {
                float dx = (x + 0.5f) / 32f - 1f, dy = (y + 0.5f) / 32f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;
                float a = Mathf.Clamp01((r - 0.45f) / 0.55f);
                return a * a * (3f - 2f * a);
            }));
            Disc = MakeSprite(Tex(64, 64, FilterMode.Bilinear, (x, y) =>
            {
                float dx = (x + 0.5f) / 32f - 1f, dy = (y + 0.5f) / 32f - 1f;
                return Mathf.Clamp01((1f - Mathf.Sqrt(dx * dx + dy * dy)) * 32f * 0.5f + 0.5f);
            }));
            Ring = MakeSprite(Tex(64, 64, FilterMode.Bilinear, (x, y) =>
            {
                float dx = (x + 0.5f) / 32f - 1f, dy = (y + 0.5f) / 32f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                return Mathf.Clamp01((1f - d) * 16f + 0.5f) * Mathf.Clamp01((d - 0.78f) * 16f + 0.5f);
            }));
        }

        static Texture2D Tex(int w, int h, FilterMode fm, System.Func<int, int, float> alpha)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = fm, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(x, y)) * 255f));
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        static Sprite MakeSprite(Texture2D t, Vector4 border = default)
        {
            var s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        // ---------------------------------------------------------------- builders
        public static RectTransform Obj(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static Vector2 AnchorOf(Anc a)
        {
            switch (a)
            {
                case Anc.TL: return new Vector2(0f, 1f);
                case Anc.T: return new Vector2(0.5f, 1f);
                case Anc.TR: return new Vector2(1f, 1f);
                case Anc.L: return new Vector2(0f, 0.5f);
                case Anc.R: return new Vector2(1f, 0.5f);
                case Anc.BL: return new Vector2(0f, 0f);
                case Anc.B: return new Vector2(0.5f, 0f);
                case Anc.BR: return new Vector2(1f, 0f);
                default: return new Vector2(0.5f, 0.5f);
            }
        }

        public static RectTransform Place(RectTransform rt, Anc a, Vector2 pos, Vector2 size)
        {
            var v = AnchorOf(a);
            rt.anchorMin = rt.anchorMax = rt.pivot = v;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        public static Image Img(Transform parent, string name, Color c, Sprite s = null, Image.Type type = Image.Type.Simple)
        {
            var rt = Obj(parent, name);
            var im = rt.gameObject.AddComponent<Image>();
            im.sprite = s ? s : White; im.type = type; im.color = c; im.raycastTarget = false;
            return im;
        }

        /// Plain TMP label. Position it afterwards with Place()/Stretch().
        public static TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center, string text = "")
        {
            var rt = Obj(parent, name);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font) t.font = font;
            t.fontSize = size; t.color = color; t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false; t.text = text;
            return t;
        }

        /// Dark translucent panel with a neon frame. Returns the fill image (frame is its first child).
        public static Image Box(Transform parent, string name, Color frame, float fill = -1f)
        {
            var bg = Img(parent, name, fill < 0 ? Panel : new Color(Ink.r, Ink.g, Ink.b, fill));
            var fr = Img(bg.transform, "Frame", frame, Frame, Image.Type.Sliced);
            Stretch(fr.rectTransform);
            return bg;
        }

        // ---------------------------------------------------------------- tween helpers (always unscaled: hit-stop / pause must not freeze UI)
        public static void Kill(ref Tween t) { if (t.isAlive) t.Stop(); }

        public static void Pop(ref Tween slot, Transform t, float from = 0.6f, float dur = 0.3f, Ease ease = Ease.OutBack)
        {
            Kill(ref slot);
            t.localScale = Vector3.one * from;
            slot = Tween.Scale(t, from, 1f, dur, ease, useUnscaledTime: true);
        }

        public static void Fade(ref Tween slot, CanvasGroup g, float to, float dur, float delay = 0f)
        {
            Kill(ref slot);
            if (Mathf.Approximately(g.alpha, to)) return;   // PrimeTween warns when start == end
            slot = Tween.Alpha(g, to, dur, Ease.OutQuad, startDelay: delay, useUnscaledTime: true);
        }

        // ---------------------------------------------------------------- gamepad + local bests
        /// Gamepad Start or South pressed this frame (the title / ending "go" button; Esc stays inert there).
        public static bool PadConfirm
        {
            get { var p = UnityEngine.InputSystem.Gamepad.current; return p != null && (p.startButton.wasPressedThisFrame || p.buttonSouth.wasPressedThisFrame); }
        }

        /// Saved best clear of a room as "BEST 0:42 A", or null. Flow writes PlayerPrefs "bestT{room}" (seconds) + "bestG{room}" (grade char).
        /// Allocates: call on Show, never per frame.
        public static string BestLabel(int room)
        {
            int g = PlayerPrefs.GetInt("bestG" + room, 0);
            if (g == 0) return null;
            int s = (int)PlayerPrefs.GetFloat("bestT" + room, 0f);
            return "BEST " + (s / 60) + ":" + (s % 60).ToString("00") + " " + (char)g;
        }

        // ---------------------------------------------------------------- grades
        public static Color GradeColor(char g)
        {
            switch (g)
            {
                case 'S': return Magenta;
                case 'A': return Cyan;
                case 'B': return Green;
                case 'C': return new Color(0.75f, 0.8f, 0.95f);
                default: return Dim * 0.7f;
            }
        }

        static readonly string[] GradeStrs = { "S", "A", "B", "C", "-" };
        public static string GradeStr(char g)
        {
            switch (g) { case 'S': return GradeStrs[0]; case 'A': return GradeStrs[1]; case 'B': return GradeStrs[2]; case 'C': return GradeStrs[3]; default: return GradeStrs[4]; }
        }

        /// Replace characters the font cannot draw (TMP would log a warning per glyph).
        public static string Safe(string s, TMP_FontAsset f)
        {
            if (string.IsNullOrEmpty(s) || !f) return s;
            StringBuilder sb = null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\n' || c == '\r' || f.HasCharacter(c, true)) { sb?.Append(c); continue; }
                if (sb == null) { sb = new StringBuilder(s.Length); sb.Append(s, 0, i); }
                sb.Append('?');
            }
            return sb == null ? s : sb.ToString();
        }
    }
}
