using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Desync.UI.Editor
{
    /// UI workstream setup (idempotent). Run: tools/unity.sh exec ui Desync.UI.Editor.UISetup.Run
    /// Builds static SDF TMP font assets from the OFL TTFs in Assets/Fonts/Source, registers font.hud / font.term /
    /// font.title / font.symbols in AssetLib, creates a TMP Settings asset (so the TMP importer window never opens).
    /// Rebuild() forces the atlases to be regenerated.
    public static class UISetup
    {
        const string Src = "Assets/Fonts/Source/";
        const string Out = "Assets/Fonts/";

        static readonly string Ascii = BuildRange(0x20, 0x7E);
        static readonly string Latin1 = BuildRange(0xA0, 0xFF) + "–—‘’“”•…€";
        // Arrows, pips, blocks the primary fonts lack; baked from Fira Mono and attached as the fallback of the three fonts.
        const string Symbols = "←↑→↓↔↕▲▼◀▶►◄●○◦■□█▌▐▀▄░▒▓◆◇·×—°";

        public static void Run() { Execute(false); }
        public static void Rebuild() { Execute(true); }

        static void Execute(bool force)
        {
            AssetDatabase.Refresh();
            if (!EnsureTmpEssentials()) return;
            TmpSettings(null);   // TMP_FontAsset.CreateFontAsset reads TMP Settings: it must exist first
            var sym = MakeFont("FiraMono-Regular.ttf", "FiraMono Symbols SDF", 48, 6, 512, Symbols, null, force);
            var hud = MakeFont("ShareTechMono-Regular.ttf", "ShareTechMono SDF", 48, 6, 1024, Ascii + Latin1, sym, force);
            var term = MakeFont("VT323-Regular.ttf", "VT323 SDF", 56, 6, 1024, Ascii + Latin1, sym, force);
            var title = MakeFont("Orbitron-VF.ttf", "Orbitron SDF", 64, 6, 1024, Ascii + Latin1, sym, force);
            AssetLib.Put("font.hud", hud);
            AssetLib.Put("font.term", term);
            AssetLib.Put("font.title", title);
            AssetLib.Put("font.symbols", sym);
            TmpSettings(hud);    // now that the fonts exist, make Share Tech Mono the TMP default
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DESYNC] UISetup done: fonts hud/term/title/symbols registered, TMP Settings ok");
        }

        /// The ugui package ships TMP's shaders + default settings as a .unitypackage that has to be imported once.
        /// AssetDatabase.ImportPackage is asynchronous (never finishes under -batchmode -quit), so unpack it by hand
        /// into Assets/Fonts/TextMesh Pro (inside the UI-owned folder; GUIDs are preserved, so TMP's own references hold).
        /// Do NOT import it a second time through the TMP menu: the GUIDs would collide.
        static bool EnsureTmpEssentials()
        {
            if (Shader.Find("TextMeshPro/Mobile/Distance Field")) return true;
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
            string pkg = info != null ? Path.Combine(info.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage") : null;
            if (pkg == null || !File.Exists(pkg)) { Debug.LogError("[DESYNC] UISetup: TMP Essential Resources.unitypackage not found"); return false; }

            string tmp = Path.GetFullPath(Path.Combine("Temp", "UISetup_tmp"));
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            var psi = new System.Diagnostics.ProcessStartInfo("tar", "-xzf \"" + pkg + "\" -C \"" + tmp + "\"") { UseShellExecute = false, CreateNoWindow = true };
            using (var pr = System.Diagnostics.Process.Start(psi)) { pr.WaitForExit(); if (pr.ExitCode != 0) { Debug.LogError("[DESYNC] UISetup: tar failed (" + pr.ExitCode + ")"); return false; } }

            const string from = "Assets/TextMesh Pro", to = Out + "TextMesh Pro";
            int n = 0;
            foreach (var dir in Directory.GetDirectories(tmp))
            {
                string pn = Path.Combine(dir, "pathname");
                if (!File.Exists(pn)) continue;
                string src = File.ReadAllLines(pn)[0].Trim();
                if (!src.StartsWith(from)) continue;
                string dst = to + src.Substring(from.Length);
                string asset = Path.Combine(dir, "asset"), meta = Path.Combine(dir, "asset.meta");
                if (File.Exists(asset)) { Directory.CreateDirectory(Path.GetDirectoryName(dst)); File.Copy(asset, dst, true); n++; }
                else Directory.CreateDirectory(dst);
                if (File.Exists(meta)) File.Copy(meta, dst + ".meta", true);
            }
            Directory.Delete(tmp, true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[DESYNC] UISetup: unpacked " + n + " TMP essential assets into " + to);
            if (Shader.Find("TextMeshPro/Mobile/Distance Field")) return true;
            Debug.Log("[DESYNC] UISetup: TMP shaders not visible yet; run UISetup.Run once more to build the fonts");
            return false;
        }

        static string BuildRange(int a, int b)
        {
            var sb = new StringBuilder();
            for (int i = a; i <= b; i++) sb.Append((char)i);
            return sb.ToString();
        }

        static TMP_FontAsset MakeFont(string ttf, string assetName, int size, int pad, int atlas, string wanted, TMP_FontAsset fallback, bool force)
        {
            string path = Out + assetName + ".asset";
            var font = AssetDatabase.LoadAssetAtPath<Font>(Src + ttf);
            if (!font) { Debug.LogError("[DESYNC] UISetup: missing font " + Src + ttf); return null; }

            // Only bake what the font really has; the rest resolves through the fallback font.
            var sb = new StringBuilder();
            foreach (char c in wanted) if (font.HasCharacter(c) && sb.ToString().IndexOf(c) < 0) sb.Append(c);
            string chars = sb.ToString();

            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing && !force && Covers(existing, fallback, fallback ? 150 : 20)) return existing;
            if (existing) AssetDatabase.DeleteAsset(path);

            var fa = TMP_FontAsset.CreateFontAsset(font, size, pad, GlyphRenderMode.SDFAA, atlas, atlas, AtlasPopulationMode.Dynamic, false);
            if (!fa) { Debug.LogError("[DESYNC] UISetup: CreateFontAsset failed for " + ttf); return null; }
            fa.name = assetName;
            fa.TryAddCharacters(chars, out string missing);
            if (!string.IsNullOrEmpty(missing)) Debug.LogWarning("[DESYNC] UISetup: " + assetName + " could not pack: " + missing);
            fa.atlasPopulationMode = AtlasPopulationMode.Static;   // static atlas: no runtime glyph generation (WebGL safe, zero GC)
            fa.fallbackFontAssetTable = fallback ? new List<TMP_FontAsset> { fallback } : new List<TMP_FontAsset>();

            AssetDatabase.CreateAsset(fa, path);
            fa.material.name = assetName + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            for (int i = 0; i < fa.atlasTextures.Length; i++)
            {
                fa.atlasTextures[i].name = assetName + " Atlas" + (i == 0 ? "" : " " + i);
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[i], fa);
            }
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            Debug.Log("[DESYNC] UISetup: built " + assetName + " (" + chars.Length + " glyphs, atlas " + atlas + ")");
            return fa;
        }

        /// Cheap validity check so Run() is idempotent (glyphs the font lacks can never be packed, so a per-char check would loop forever).
        static bool Covers(TMP_FontAsset fa, TMP_FontAsset fallback, int minGlyphs)
        {
            if (fa.atlasPopulationMode != AtlasPopulationMode.Static || fa.atlasTextures == null || fa.atlasTextures.Length == 0 || !fa.atlasTextures[0]) return false;
            if (fallback && (fa.fallbackFontAssetTable == null || !fa.fallbackFontAssetTable.Contains(fallback))) return false;
            return fa.characterTable != null && fa.characterTable.Count >= minGlyphs;
        }

        static void TmpSettings(TMP_FontAsset def)
        {
            const string path = Out + "Resources/TMP Settings.asset";
            var s = Resources.Load<TMP_Settings>("TMP Settings");
            bool mine = false;
            if (!s)
            {
                s = ScriptableObject.CreateInstance<TMP_Settings>();
                Directory.CreateDirectory(Out + "Resources");
                AssetDatabase.CreateAsset(s, path);
                mine = true;
            }
            var so = new SerializedObject(s);
            void Set(string n, System.Action<SerializedProperty> f) { var p = so.FindProperty(n); if (p != null) f(p); else Debug.LogWarning("[DESYNC] TMP Settings field missing: " + n); }
            if (mine)
            {
                Set("assetVersion", p => p.stringValue = "2");
                Set("m_TextWrappingMode", p => p.intValue = (int)TextWrappingModes.Normal);
                Set("m_enableKerning", p => p.boolValue = true);
                Set("m_defaultFontSize", p => p.floatValue = 36f);
                Set("m_defaultAutoSizeMinRatio", p => p.floatValue = 0.5f);
                Set("m_defaultAutoSizeMaxRatio", p => p.floatValue = 2f);
                Set("m_defaultTextMeshProTextContainerSize", p => p.vector2Value = new Vector2(20, 5));
                Set("m_defaultTextMeshProUITextContainerSize", p => p.vector2Value = new Vector2(200, 50));
            }
            if (def) Set("m_defaultFontAsset", p => p.objectReferenceValue = def);   // Share Tech Mono becomes TMP's default font
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssets();
        }
    }
}
