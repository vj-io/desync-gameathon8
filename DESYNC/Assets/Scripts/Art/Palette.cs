using UnityEngine;

namespace Desync.Art
{
    /// Colour-blind alt palette (M5 groundwork). One saved flag, PlayerPrefs "cb" (Core's Settings is frozen, so it lives here), read
    /// through Colorblind. Apply() publishes the alt colours as shader globals (_Colorblind, _CbRenderLow / High, _CbSource, _CbDanger,
    /// all linear) for World, Panel, FX, GlyphRain and Glow; the UI reads the four hues below. Blue + orange carry RENDER, yellow is SOURCE,
    /// vermillion is the threat hue: they differ in luminance as well as hue, and every layer / threat cue also has a pattern (hatched
    /// 'X' walls, striped ICE, dashed HUD frame in SOURCE) so colour is never the only signal.
    public static class Palette
    {
        public const string Key = "cb";

        public static readonly Color CbCyan = new Color(0.337f, 0.706f, 0.914f),       // #56B4E9 sky blue   (RENDER, was cyan)
                                     CbMagenta = new Color(0.902f, 0.624f, 0f),         // #E69F00 orange     (RENDER accent, was magenta)
                                     CbGreen = new Color(0.941f, 0.894f, 0.259f),       // #F0E442 yellow     (SOURCE, was green)
                                     CbDanger = new Color(0.835f, 0.369f, 0f);          // #D55E00 vermillion (threats, was red)

        static readonly int ColorblindId = Shader.PropertyToID("_Colorblind"), LowId = Shader.PropertyToID("_CbRenderLow"),
            HighId = Shader.PropertyToID("_CbRenderHigh"), SourceId = Shader.PropertyToID("_CbSource"), DangerId = Shader.PropertyToID("_CbDanger");

        static bool _cb, _read;

        public static bool Colorblind
        {
            get { if (!_read) Refresh(); return _cb; }
            set { PlayerPrefs.SetInt(Key, value ? 1 : 0); Refresh(); }
        }

        /// The four hues the UI and effects draw with right now.
        public static Color Cyan => Colorblind ? CbCyan : GameConfig.I.cyan;
        public static Color Magenta => Colorblind ? CbMagenta : GameConfig.I.magenta;
        public static Color Green => Colorblind ? CbGreen : GameConfig.I.green;
        public static Color Danger => Colorblind ? CbDanger : GameConfig.I.danger;

        /// Re-reads the saved flag (after GameEvents.SettingsChanged or a direct PlayerPrefs write) and republishes the shader globals.
        public static void Refresh()
        {
            _read = true;
            _cb = PlayerPrefs.GetInt(Key, 0) == 1;
            Apply();
        }

        static Vector4 Lin(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? (Vector4)c.linear : (Vector4)c;

        public static void Apply()
        {
            Shader.SetGlobalFloat(ColorblindId, _cb ? 1f : 0f);
            Shader.SetGlobalVector(LowId, Lin(CbCyan));
            Shader.SetGlobalVector(HighId, Lin(CbMagenta));
            Shader.SetGlobalVector(SourceId, Lin(CbGreen));
            Shader.SetGlobalVector(DangerId, Lin(CbDanger));
        }
    }

    /// Vertex-colour flags the world shader understands (Desync/World). The room builder writes SolidAlpha into the alpha of every vertex
    /// of an 'X' wall (solid in both layers): in SOURCE the shader then draws a dense hatched slab instead of the thin wireframe the
    /// passable walls get. Every other world vertex keeps alpha >= 0.7 (colours multiplied by a shade factor lose a little alpha).
    public static class WorldFlags
    {
        public const float SolidAlpha = 0.15f, SolidBelow = 0.3f;

        public static Color Solid(Color c) { c.a = SolidAlpha; return c; }
    }

    /// Sysadmin Key: see-through cone materials. Same look as mat.cone / mat.cone.trace, depth test Always so the cone draws through
    /// walls in RENDER (in SOURCE walls are see-through anyway). Registered by ArtSetup; the VisionCone owner swaps sharedMaterial.
    public static class ConeMats
    {
        public static Material Get(bool trace, bool xray)
        {
            var m = AssetLib.Get<Material>(trace ? (xray ? "mat.cone.trace.xray" : "mat.cone.trace") : (xray ? "mat.cone.xray" : "mat.cone"));
            return m ? m : AssetLib.Get<Material>(trace ? "mat.cone.trace" : "mat.cone");
        }
    }
}
