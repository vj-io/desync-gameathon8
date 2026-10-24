using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Desync.Art.Editor
{
    /// Idempotent ART setup: texture import settings, every ART material (created or refreshed in place), AssetLib keys,
    /// and the DesyncPostFX Full Screen Pass feature on PC_Renderer (SSAO feature deactivated: PRD 10.6 "no SSAO").
    /// Run: tools/unity.sh exec art Desync.Art.Editor.ArtSetup.Run
    public static class ArtSetup
    {
        public const string Dir = "Assets/Art";
        public const string MatDir = Dir + "/Materials";
        public const string TexDir = Dir + "/Textures";
        const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        const string FeatureName = "DesyncPostFX";

        public static void Run()
        {
            AssetDatabase.Refresh();
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Dir, "Materials");

            var glyphs = Texture(TexDir + "/tex_glyphs.png", true, TextureWrapMode.Repeat, 4);
            var decal = Texture(TexDir + "/tex_decal.png", true, TextureWrapMode.Clamp, 8);
            var poster = Texture(TexDir + "/tex_halcyon.png", false, TextureWrapMode.Clamp, 4);

            var cfg = GameConfig.I;
            Color cyan = cfg.cyan, magenta = cfg.magenta, green = cfg.green, danger = cfg.danger;
            Color Src(float w) => new Color(green.r, green.g, green.b, w);

            // --- world -------------------------------------------------------------------------------------------
            Material World(string file, float grid) => Mat(file, "Desync/World", m =>
            {
                m.SetTexture("_GlyphTex", glyphs);
                m.SetColor("_EdgeColorLow", cyan);
                m.SetColor("_EdgeColorHigh", magenta);
                m.SetColor("_GridColor", cyan);
                m.SetVector("_Grid", new Vector4(1f, 4f, grid, 0f));
                m.SetColor("_SourceColor", green);
                m.SetVector("_EdgeGradient", new Vector4(0.2f, 3f, 0f, 0f));
                m.SetFloat("_EdgeWidth", 1.3f);
                m.SetVector("_EdgeGlow", new Vector4(1.6f, 7f, 0.45f, 0f));
                m.SetFloat("_Shade", 0.6f);
                m.SetVector("_SourceMix", new Vector4(0.035f, 0.85f, 0.45f, 0.3f));
                m.SetFloat("_GlyphScale", 0.3f);
                PostFXController.SetWorldBlend(new[] { m }, false);
            });
            Put("mat.world", World("World", 0f));
            Put("mat.floor", World("Floor", 0.35f));

            // --- solid emissive bodies ---------------------------------------------------------------------------
            Material Glow(string file, Color baseCol, float vcWeight, Color rim, float rimPower, Color edge, Vector4 edgeGlow, Vector4 anim) =>
                Mat(file, "Desync/Glow", m =>
                {
                    m.SetColor("_BaseColor", baseCol);
                    m.SetFloat("_VertexColorWeight", vcWeight);
                    m.SetColor("_RimColor", rim);
                    m.SetFloat("_RimPower", rimPower);
                    m.SetColor("_EdgeColor", edge);
                    m.SetVector("_EdgeGlow", edgeGlow);
                    m.SetVector("_Anim", anim);
                });
            // _Hazard marks threat bodies: the colour-blind palette gives them the danger hue and moving stripes (Palette.cs).
            var drone = Glow("Drone", new Color(0.16f, 0.16f, 0.24f), 1f, A(magenta, 1.1f), 2.5f, magenta, new Vector4(1.6f, 1.1f, 6f, 0.4f), Vector4.zero);
            var ice = Glow("Ice", new Color(0.3f, 0.02f, 0.05f), 0f, A(danger, 2.4f), 1.8f, new Color(1f, 0.45f, 0.5f), new Vector4(2f, 1.2f, 6f, 0.5f), new Vector4(11f, 0.15f, 0.3f, 0f));
            if (drone) drone.SetFloat("_Hazard", 1f);
            if (ice) ice.SetFloat("_Hazard", 1f);
            Put("mat.drone", drone);
            Put("mat.hijacked", Glow("Hijacked", new Color(0f, 0.2f, 0.25f), 0.5f, A(cyan, 1.8f), 2f, cyan, new Vector4(2f, 1.3f, 7f, 0.5f), new Vector4(6f, 0.12f, 0f, 0f)));
            Put("mat.ice", ice);

            // --- additive effects (one shader) -------------------------------------------------------------------
            Material Fx(string file, Color col, float intensity, Color source, float fade, Vector4 band, Vector4 anim, Vector4 shape, Texture mask = null) =>
                Mat(file, "Desync/FX", m =>
                {
                    m.SetTexture("_MainTex", mask);
                    m.SetColor("_BaseColor", col);
                    m.SetFloat("_Intensity", intensity);
                    m.SetColor("_SourceColor", source);
                    m.SetFloat("_FadeRange", fade);
                    m.SetVector("_Band", band);
                    m.SetVector("_Anim", anim);
                    m.SetVector("_Shape", shape);
                });
            var none = Src(0f);
            // Cones: ENEMIES' cone mesh fades apex -> rim in vertex alpha (any range), so the pivot fade stays off.
            var cone = Fx("Cone", new Color(1f, 0.55f, 0.9f), 0.32f, none, 0f, new Vector4(5f, 4f, 0.18f, 0f), Vector4.zero, new Vector4(0.35f, 0f, 1f, 0f));
            var coneTrace = Fx("ConeTrace", danger, 0.6f, none, 0f, new Vector4(7f, 14f, 0.35f, 0f), new Vector4(18f, 0.25f, 0.2f, 0f), new Vector4(0.35f, 0f, 1f, 0f));
            var iceTrail = Fx("IceTrail", danger, 1.4f, none, 0f, Vector4.zero, new Vector4(0f, 0f, 0.2f, 0f), new Vector4(0f, 0f, 1f, 0f));
            var portalLocked = Fx("PortalLocked", new Color(1f, 0.1f, 0.3f), 0.55f, none, 0f, new Vector4(4f, 1.5f, 0.3f, 0f), new Vector4(1.5f, 0.15f, 0.3f, 0.3f), new Vector4(0.6f, 0f, 0f, 0f));
            foreach (var t in new[] { coneTrace, iceTrail, portalLocked }) if (t) t.SetFloat("_CbHue", 1f);   // threat hue: danger colour in the colour-blind palette
            Put("mat.cone", cone);
            Put("mat.cone.trace", coneTrace);
            Put("mat.ice.trail", iceTrail);
            Put("mat.portal", Fx("Portal", cyan, 1.3f, Src(1f), 0f, new Vector4(7f, 6f, 0.4f, 0f), new Vector4(4f, 0.25f, 0f, 0f), new Vector4(0.6f, 0f, 0f, 0f)));
            Put("mat.portal.locked", portalLocked);
            // Sysadmin Key: the same cones with the depth test on Always, so they draw through walls (Palette.cs ConeMats picks them).
            Material Xray(string file, Material from) => !from ? null : Mat(file, "Desync/FX", m =>
            {
                m.CopyPropertiesFromMaterial(from);
                m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            });
            Put("mat.cone.xray", Xray("ConeXray", cone));
            Put("mat.cone.trace.xray", Xray("ConeTraceXray", coneTrace));
            var architect = Fx("Architect", green, 1.6f, none, 0f, new Vector4(3f, 2f, 0.35f, 0f), new Vector4(0f, 0f, 0.25f, 0f), new Vector4(0f, 0.92f, 1f, 0f));
            if (architect) architect.SetFloat("_CbHue", 2f);   // a SOURCE-green body: yellow in the colour-blind palette
            Put("mat.architect", architect);
            Put("mat.strings", Fx("Strings", new Color(1f, 0.55f, 0.95f), 1.2f, none, 0f, new Vector4(2f, 6f, 0.5f, 0f), new Vector4(0f, 0f, 0.4f, 0f), new Vector4(0f, 0f, 1f, 0f)));
            Put("mat.lkp", Fx("LKP", cyan, 1.3f, none, 0f, new Vector4(10f, 8f, 0.3f, 0f), new Vector4(8f, 0.45f, 0f, 0f), new Vector4(0.5f, 0f, 1f, 0f)));
            Put("mat.glitch", Fx("Glitch", magenta, 1f, Src(0.6f), 0f, new Vector4(12f, 10f, 0.5f, 0f), new Vector4(0f, 0f, 0.5f, 1f), new Vector4(0.5f, 0f, 1f, 0f)));
            Put("mat.decal", Fx("Decal", cyan, 1.6f, Src(1f), 0f, Vector4.zero, new Vector4(2.5f, 0.2f, 0f, 0f), new Vector4(0f, 0f, 1f, 0f), decal));

            // --- panels ------------------------------------------------------------------------------------------
            Material Panel(string file, Texture tex, Color tint, float mode, float glow, Vector2 grid) => Mat(file, "Desync/Panel", m =>
            {
                m.SetTexture("_MainTex", tex);
                m.SetTexture("_GlyphTex", glyphs);
                m.SetColor("_BaseColor", tint);
                m.SetFloat("_Mode", mode);
                m.SetFloat("_Glow", glow);
                m.SetVector("_CodeGrid", grid);
                m.SetColor("_SourceColor", green);
                m.SetColor("_DangerColor", danger);
                PostFXController.SetWorldBlend(new[] { m }, false);
            });
            Put("mat.poster", Panel("Poster", poster, Color.white, 0f, 1.1f, new Vector2(16f, 24f)));
            Put("mat.screen", Panel("Screen", null, cyan, 1f, 1f, new Vector2(24f, 16f)));

            // --- glyphs + rain -----------------------------------------------------------------------------------
            Put("tex.glyphs", glyphs);
            Put("mat.glyphrain", Mat("GlyphRain", "Desync/GlyphRain", m =>
            {
                m.SetTexture("_GlyphTex", glyphs);
                m.SetColor("_Color", green);
                m.SetColor("_HeadColor", new Color(0.75f, 1f, 0.85f));
                m.SetFloat("_Intensity", 1.3f);
            }));

            // --- post-FX -----------------------------------------------------------------------------------------
            var postfx = Mat("PostFX", "Desync/PostFX", m => { });
            RendererFeature(postfx);

            AssetDatabase.SaveAssets();
            Debug.Log("[DESYNC] ArtSetup done");
        }

        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        static void Put(string key, UnityEngine.Object value)
        {
            if (!value) { Debug.LogError("[DESYNC] ArtSetup: missing asset for " + key); return; }
            AssetLib.Put(key, value);
        }

        static Material Mat(string file, string shaderName, Action<Material> set)
        {
            var shader = Shader.Find(shaderName);
            if (!shader) { Debug.LogError("[DESYNC] ArtSetup: shader not found " + shaderName); return null; }
            var path = $"{MatDir}/{file}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(shader) { name = file };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            set(m);
            m.enableInstancing = false;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D Texture(string path, bool mask, TextureWrapMode wrap, int aniso)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) { Debug.LogError("[DESYNC] ArtSetup: missing texture " + path); return null; }
            ti.textureType = mask ? TextureImporterType.SingleChannel : TextureImporterType.Default;
            if (mask)
            {
                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                s.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
                ti.SetTextureSettings(s);
                ti.textureCompression = TextureImporterCompression.Uncompressed;   // R8: crisp glyph edges, tiny
            }
            ti.sRGBTexture = !mask;
            ti.mipmapEnabled = true;
            ti.wrapMode = wrap;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = aniso;
            ti.maxTextureSize = 1024;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void RendererFeature(Material postfx)
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (!data) { Debug.LogError("[DESYNC] ArtSetup: renderer not found " + RendererPath); return; }
            FullScreenPassRendererFeature feature = null;
            foreach (var f in data.rendererFeatures)
            {
                if (f is FullScreenPassRendererFeature fs && f.name == FeatureName) feature = fs;
                if (f is ScreenSpaceAmbientOcclusion && f.isActive) { f.SetActive(false); EditorUtility.SetDirty(f); }
            }
            if (!feature)
            {
                feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                feature.name = FeatureName;
                AssetDatabase.AddObjectToAsset(feature, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                var so = new SerializedObject(data);
                var list = so.FindProperty("m_RendererFeatures");
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
                var map = so.FindProperty("m_RendererFeatureMap");
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedProperties();
            }
            feature.passMaterial = postfx;
            feature.passIndex = 0;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
            feature.fetchColorBuffer = true;
            feature.requirements = ScriptableRenderPassInput.None;
            feature.bindDepthStencilAttachment = false;
            feature.SetActive(true);
            EditorUtility.SetDirty(feature);
            data.SetDirty();
            EditorUtility.SetDirty(data);
        }
    }
}
