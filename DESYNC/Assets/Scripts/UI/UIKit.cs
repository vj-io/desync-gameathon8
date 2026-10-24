using System.Text;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Desync.UI
{
    public enum Anc { TL, T, TR, L, C, R, BL, B, BR }

    /// Per-frame numbers with no garbage, Editor included. TMP's format / char[] SetText overloads build a string on every call
    /// in the Editor (it syncs its inspector text under UNITY_EDITOR), so the Editor GC checks saw the HUD allocate although
    /// builds never do. LiveText composes into a buffer, copies it into one string per length that this label owns (allocated
    /// once, rewritten in place) and hands that to SetText(string), which keeps the reference: no allocation anywhere.
    /// Usage: label.S("MELT ").I(melt).C('%').Done();
    public sealed class LiveText
    {
        readonly TMP_Text _t;
        readonly string[] _byLen = new string[33];
        readonly char[] _buf = new char[32];
        int _n;

        public LiveText(TMP_Text t) { _t = t; }

        public LiveText C(char c) { if (_n < _buf.Length) _buf[_n++] = c; return this; }
        public LiveText S(string s) { for (int i = 0; i < s.Length; i++) C(s[i]); return this; }
        public LiveText A(char[] src, int n) { for (int i = 0; i < n; i++) C(src[i]); return this; }

        /// `v` in decimal, zero-padded to `digits`.
        public LiveText I(int v, int digits = 1)
        {
            if (v < 0) { C('-'); v = -v; }
            int start = _n, d = 0;
            do { C((char)('0' + v % 10)); v /= 10; d++; } while (v > 0 || d < digits);
            for (int i = start, j = _n - 1; i < j; i++, j--) { char x = _buf[i]; _buf[i] = _buf[j]; _buf[j] = x; }
            return this;
        }

        public unsafe void Done()
        {
            int n = _n; _n = 0;
            if (n == 0) { _t.SetText(string.Empty); return; }
            var s = _byLen[n];
            if (s == null) _byLen[n] = s = new string(' ', n);   // a fresh string (never interned) this label may rewrite
            fixed (char* p = s) for (int i = 0; i < n; i++) p[i] = _buf[i];
            _t.SetText(s);
        }
    }

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
            _pollAt = t + 0.5f;
            bool calm = Settings.ReduceFx;   // REDUCE FX: shake x0, flashes off (chromatic x0.3 lives in PostFXController)
            _flashes = !Settings.ReduceFlashes && !calm; _shake = Settings.Shake && !calm;
        }
        public static bool Flashes { get { Poll(); return _flashes; } }
        public static bool Shake { get { Poll(); return _shake; } }
        public static void RefreshSettings() { _pollAt = 0f; Poll(); }

        /// The four hues from the saved palette (authored colours, or the colour-blind set). Init calls it; after a palette change the
        /// UIManager calls it and rebuilds every screen, because widgets copy their colours when they are built.
        public static void ApplyPalette()
        {
            Cyan = Desync.Art.Palette.Cyan; Magenta = Desync.Art.Palette.Magenta; Green = Desync.Art.Palette.Green; Danger = Desync.Art.Palette.Danger;
        }

        public static void Init()
        {
            if (_init && White) return;
            _init = true;
            ApplyPalette();
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
            // Rarity borders (drawn Image.Type.Tiled, fillCenter off): [0] solid, [1..4] dashes 6 on / 6 off shifted 3 px per frame,
            // so cycling 1..4 makes the dashes crawl. 24 px sprite, 3 px line, solid 6 px corners.
            Borders = new Sprite[5];
            for (int f = 0; f < 5; f++)
            {
                int shift = (f - 1) * 3;
                Borders[f] = MakeSprite(Tex(24, 24, FilterMode.Point, (x, y) =>
                {
                    if (x >= 3 && y >= 3 && x <= 20 && y <= 20) return 0f;   // inside the line
                    if (f == 0) return 1f;
                    bool hEdge = x >= 6 && x <= 17, vEdge = y >= 6 && y <= 17;
                    if (!hEdge && !vEdge) return 1f;                          // corners
                    int p = (hEdge ? x : y) - 6 + 12 - shift;
                    return p % 12 < 6 ? 1f : 0f;
                }), new Vector4(6, 6, 6, 6));
            }
        }

        public static Sprite[] Borders;

        /// Rarity border on `im` (spec: rarity by border pattern, never by a new hue): 0 = C solid, 1 = R dashed, 2 = E crawling
        /// dashes (call AnimateBorder every frame). `thickness` scales the 3 px line.
        public static void SetBorder(Image im, int rarity, float thickness = 1f)
        {
            im.sprite = Borders[rarity == 0 ? 0 : 1];
            im.type = Image.Type.Tiled; im.fillCenter = false;
            im.pixelsPerUnitMultiplier = 1f / thickness;
        }

        /// Crawling dashes for epic borders: 12 steps a second, no allocation.
        public static void AnimateBorder(Image im) { var s = Borders[1 + (int)(Time.unscaledTime * 12f) % 4]; if (im.sprite != s) im.sprite = s; }

        /// Menus and run screens need a visible, free pointer.
        public static void FreeCursor()
        {
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
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

        // ---------------------------------------------------------------- UI-only keys (Core's GameInput covers R / SPACE / ESC)
        public static readonly InputAction AnyKey = new InputAction("UIAny", InputActionType.Button);
        public static readonly InputAction CopyKey = new InputAction("UICopy", InputActionType.Button, "<Keyboard>/c");
        public static readonly InputAction GoalsKey = new InputAction("UIGoals", InputActionType.Button, "<Keyboard>/tab");
        // Menu navigation (title + run screens): arrows / WASD, pad d-pad / left stick; confirm = Space, Enter, pad South / Start.
        public static readonly InputAction NavPrev = new InputAction("UIPrev", InputActionType.Button);
        public static readonly InputAction NavNext = new InputAction("UINext", InputActionType.Button);
        public static readonly InputAction Confirm = new InputAction("UIConfirm", InputActionType.Button);

        static UIKit()
        {
            foreach (var b in new[] { "<Keyboard>/anyKey", "<Mouse>/leftButton", "<Gamepad>/buttonSouth", "<Gamepad>/buttonEast",
                                      "<Gamepad>/buttonNorth", "<Gamepad>/buttonWest", "<Gamepad>/start", "<Gamepad>/select" })
                AnyKey.AddBinding(b);
            CopyKey.AddBinding("<Gamepad>/buttonNorth");
            foreach (var b in new[] { "<Keyboard>/upArrow", "<Keyboard>/leftArrow", "<Keyboard>/w", "<Keyboard>/a",
                                      "<Gamepad>/dpad/up", "<Gamepad>/dpad/left", "<Gamepad>/leftStick/up", "<Gamepad>/leftStick/left" })
                NavPrev.AddBinding(b);
            foreach (var b in new[] { "<Keyboard>/downArrow", "<Keyboard>/rightArrow", "<Keyboard>/s", "<Keyboard>/d",
                                      "<Gamepad>/dpad/down", "<Gamepad>/dpad/right", "<Gamepad>/leftStick/down", "<Gamepad>/leftStick/right" })
                NavNext.AddBinding(b);
            foreach (var b in new[] { "<Keyboard>/space", "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Gamepad>/buttonSouth", "<Gamepad>/start" })
                Confirm.AddBinding(b);
            AnyKey.Enable(); CopyKey.Enable(); GoalsKey.Enable(); NavPrev.Enable(); NavNext.Enable(); Confirm.Enable();
        }

        /// -1 / +1 when a menu step was pressed this frame, else 0.
        public static int NavStep => NavPrev.WasPressedThisFrame() ? -1 : NavNext.WasPressedThisFrame() ? 1 : 0;

        // ---------------------------------------------------------------- time
        /// Writes "mm:ss.cc" (8 chars, clamped to 99:59.99) into buf. No allocation: pair with TMP_Text.SetText(buf, 0, 8).
        public static int Clock(float t, char[] buf)
        {
            int cs = t > 0f ? Mathf.Min((int)(t * 100f + 0.001f), 99 * 6000 + 5999) : 0;
            int m = cs / 6000, s = cs / 100 % 60, c = cs % 100;
            buf[0] = (char)('0' + m / 10); buf[1] = (char)('0' + m % 10); buf[2] = ':';
            buf[3] = (char)('0' + s / 10); buf[4] = (char)('0' + s % 10); buf[5] = '.';
            buf[6] = (char)('0' + c / 10); buf[7] = (char)('0' + c % 10);
            return 8;
        }

        static readonly char[] _clk = new char[8];
        /// "m:ss.cc" (leading minute zero dropped), e.g. "0:41.27". Allocates: call on Show, never per frame.
        public static string ClockShort(float t) { Clock(t, _clk); return _clk[0] == '0' ? new string(_clk, 1, 7) : new string(_clk, 0, 8); }

        // ---------------------------------------------------------------- medals (index = (int)Rules.Medal: None, Bronze, Silver, Gold, Sync)
        public static readonly string[] MedalNames = { "-", "BRONZE", "SILVER", "GOLD", "SYNC" };
        static readonly float[] MedalMult = { 0f, float.PositiveInfinity, 1.6f, 1.25f, 1f };

        /// Time limit for a medal: SYNC tDev x1.0, GOLD x1.25, SILVER x1.6, BRONZE = any clear (infinity).
        public static float MedalLimit(int medal, float tDev) => medal >= 1 && medal <= 4 ? MedalMult[medal] * tDev : 0f;

        /// Best medal still reachable at room time t (4 SYNC .. 1 BRONZE). tDev <= 0 -> 0 (unknown).
        public static int MedalAt(float t, float tDev)
        {
            if (tDev <= 0f) return 0;
            for (int m = 4; m >= 2; m--) if (t <= MedalMult[m] * tDev) return m;
            return 1;
        }

        public static Color MedalColor(int medal)
        {
            switch (medal)
            {
                case 4: return Magenta;
                case 3: return new Color(1f, 0.82f, 0.25f);
                case 2: return new Color(0.78f, 0.84f, 0.95f);
                case 1: return new Color(0.82f, 0.52f, 0.3f);
                default: return Dim;
            }
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
