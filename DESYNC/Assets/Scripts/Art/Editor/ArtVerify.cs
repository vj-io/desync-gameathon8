using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Desync.Art.Editor
{
    /// ART self-checks (runs ArtSetup first; no scene is saved):
    ///  1. MeshPrep asserts on a cube (un-weld, diagonals hidden, idempotent).
    ///  2. Every Desync shader: editor compile errors + an explicit GLES3 (WebGL2) compile of the main variants.
    ///  3. Preview stills of a throwaway office scene in RENDER / SOURCE / post-FX states -> .work/art/preview_*.png
    /// Run: tools/unity.sh exec art Desync.Art.Editor.ArtVerify.Run
    public static class ArtVerify
    {
        static readonly string[] Shaders = { "Desync/World", "Desync/Glow", "Desync/FX", "Desync/Panel", "Desync/GlyphRain", "Desync/PostFX" };
        static readonly string[][] Variants =
        {
            new string[0], new[] { "_SOURCE" }, new[] { "FOG_LINEAR" }, new[] { "_SOURCE", "FOG_LINEAR" },
            new[] { "FOG_LINEAR", "_ADDITIONAL_LIGHTS" }, new[] { "FOG_LINEAR", "_CLUSTER_LIGHT_LOOP" },
        };

        public static void Run()
        {
            ArtSetup.Run();
            int fails = CheckMeshPrep() + CheckModels() + CheckShaders();
            Preview();
            Debug.Log(fails == 0 ? "[DESYNC] ArtVerify passed" : $"[DESYNC] ArtVerify FAILED ({fails})");
        }

        public static void RunPreviewOnly() => Preview();

        /// Follow-up probes: model reimport, _SOURCE variants on Metal + GLES3 and their first frame, handler allocations.
        public static void RunProbes()
        {
            int fails = ReimportModels() + CheckSourceFirstFrame() + CheckAllocs() + CheckModels();
            Debug.Log(fails == 0 ? "[DESYNC] ArtProbes passed" : $"[DESYNC] ArtProbes FAILED ({fails})");
        }

        static int ReimportModels()
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/ThirdParty/Models" });
            foreach (var g in guids) AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
            Debug.Log($"[DESYNC] reimported {guids.Length} models under Assets/ThirdParty/Models");
            return 0;
        }

        /// The _SOURCE variants of the opaque shaders must compile for Metal (editor) and GLES3 (WebGL), and the very first
        /// frame drawn with the keyword on and the blend switch NOT run must not be the magenta error shader.
        static int CheckSourceFirstFrame()
        {
            int fails = 0;
            foreach (var name in new[] { "Desync/World", "Desync/Panel" })
            {
                var pass = ShaderUtil.GetShaderData(Shader.Find(name)).GetSubshader(0).GetPass(0);
                foreach (var plat in new[] { (ShaderCompilerPlatform.Metal, BuildTarget.StandaloneOSX), (ShaderCompilerPlatform.GLES3x, BuildTarget.WebGL) })
                    foreach (var kw in new[] { new[] { "_SOURCE" }, new[] { "_SOURCE", "FOG_LINEAR" }, new[] { "_SOURCE", "FOG_LINEAR", "_CLUSTER_LIGHT_LOOP" } })
                        foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                        {
                            var r = pass.CompileVariant(stage, kw, plat.Item1, plat.Item2);
                            if (r.Success) continue;
                            fails++;
                            foreach (var m in r.Messages) Debug.LogError($"[DESYNC] {plat.Item1} {name} {stage} [{string.Join(",", kw)}]: {m.message}");
                        }
                Debug.Log($"[DESYNC] {name} _SOURCE variants compile on Metal + GLES3: {(fails == 0 ? "ok" : "FAILED")}");
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            foreach (var key in new[] { "mat.world", "mat.poster" })
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.GetComponent<Renderer>().sharedMaterial = AssetLib.Get<Material>(key);
                q.transform.position = new Vector3(key == "mat.world" ? -0.6f : 0.6f, 0f, 2f);
            }
            Shader.EnableKeyword(LayerState.Keyword);   // keyword only: no SetWorldBlend, materials stay opaque
            var first = Probe(cam);
            var second = Probe(cam);
            Shader.DisableKeyword(LayerState.Keyword);
            for (int i = 0; i < 2; i++)
            {
                bool magentaFirst = IsMagenta(first[i]), magentaSecond = IsMagenta(second[i]);
                Debug.Log($"[DESYNC] _SOURCE opaque first frame {(i == 0 ? "mat.world" : "mat.poster")}: first={first[i]} second={second[i]}");
                if (magentaFirst || magentaSecond) { fails++; Debug.LogError("[DESYNC] _SOURCE frame rendered with the error shader"); }
            }
            return fails;
        }

        static bool IsMagenta(Color c) => c.r > 0.9f && c.g < 0.1f && c.b > 0.9f;

        static Color[] Probe(Camera cam)
        {
            var rt = RenderTexture.GetTemporary(256, 128, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(256, 128, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 256, 128), 0, 0);
            RenderTexture.active = null;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            // brightest pixel inside each quad's screen area (glyphs/edges vary; magenta is uniform)
            var res = new Color[2];
            for (int side = 0; side < 2; side++)
            {
                int x = side == 0 ? 80 : 176;
                res[side] = tex.GetPixel(x, 64);
            }
            Object.DestroyImmediate(tex);
            return res;
        }

        /// Bytes allocated per call by the ART handlers that run on GameEvents.Flipped / LayerState.Changed, and by Update.
        static int CheckAllocs()
        {
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var go = new GameObject("allocProbe");
            var fx = go.AddComponent<PostFXController>();
            var mats = new Material[PostFXController.SourceTransparentKeys.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = AssetLib.Get<Material>(PostFXController.SourceTransparentKeys[i]);
            typeof(PostFXController).GetField("_mats", F).SetValue(fx, mats);
            var onLayer = (System.Action<bool>)System.Delegate.CreateDelegate(typeof(System.Action<bool>), fx, typeof(PostFXController).GetMethod("OnLayer", F));
            var onFlip = (System.Action<bool>)System.Delegate.CreateDelegate(typeof(System.Action<bool>), fx, typeof(PostFXController).GetMethod("OnFlipped", F));
            var update = (System.Action)System.Delegate.CreateDelegate(typeof(System.Action), fx, typeof(PostFXController).GetMethod("Update", F));
            var rain = GlyphRain.Attach(new GameObject("cam").AddComponent<Camera>());
            var rainLayer = (System.Action<bool>)System.Delegate.CreateDelegate(typeof(System.Action<bool>), rain, typeof(GlyphRain).GetMethod("OnLayer", F));

            void Round() { onFlip(true); onLayer(true); rainLayer(true); update(); onFlip(false); onLayer(false); rainLayer(false); update(); }
            Round(); Round();   // warm-up (JIT, first-use caches)
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) Round();
            long perFlip = (System.GC.GetAllocatedBytesForCurrentThread() - before) / 40;
            PostFXController.SetWorldBlend(mats, false);
            Object.DestroyImmediate(go);
            Debug.Log($"[DESYNC] ART alloc per flip (Flipped + LayerState.Changed handlers + one Update): {perFlip} bytes");
            return perFlip > 0 ? 1 : 0;
        }

        // ---------------------------------------------------------------------------------------------------------
        static int CheckMeshPrep()
        {
            int fails = 0;
            void Check(bool ok, string what) { if (!ok) { fails++; Debug.LogError("[DESYNC] MeshPrep check failed: " + what); } }

            var cube = Object.Instantiate(Resources.GetBuiltinResource<Mesh>("Cube.fbx"));
            var cols = new Color[cube.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = new Color(0.2f, 0.3f, 0.4f, 1f);
            cube.colors = cols;
            MeshPrep.Process(cube);
            Check(cube.vertexCount == 36, "cube un-welded to 36 vertices, got " + cube.vertexCount);
            var uv = new List<Vector4>();
            cube.GetUVs(MeshPrep.Channel, uv);
            Check(uv.Count == 36, "uv3 present");
            int hidden = 0;
            for (int t = 0; t < 12; t++)
            {
                int f = Mathf.RoundToInt(uv[t * 3].w) - 8;
                Check(f >= 0 && f < 8, "flags valid");
                for (int b = 0; b < 3; b++) if ((f & (1 << b)) != 0) hidden++;
                Check(uv[t * 3].x == 1 && uv[t * 3 + 1].y == 1 && uv[t * 3 + 2].z == 1, "corner barycentrics");
            }
            Check(hidden == 12, "each cube face hides exactly its diagonal (12 half-edges), got " + hidden);
            Check(cube.colors[5] == new Color(0.2f, 0.3f, 0.4f, 1f), "vertex colours kept");
            Check(cube.normals.Length == 36 && cube.uv.Length == 36, "normals + uv0 kept");
            var before = cube.vertices;
            MeshPrep.Process(cube);
            Check(cube.vertexCount == 36 && cube.vertices[7] == before[7], "idempotent");
            Object.DestroyImmediate(cube);
            Debug.Log(fails == 0 ? "[DESYNC] MeshPrep checks passed" : "[DESYNC] MeshPrep checks FAILED");
            return fails;
        }

        /// Every mesh imported under Assets/ThirdParty/Models must come out of ModelPrepPostprocessor prepped.
        static int CheckModels()
        {
            int total = 0, prepped = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/ThirdParty/Models" }))
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                    if (o is Mesh m)
                    {
                        total++;
                        if (MeshPrep.IsPrepped(m) && m.colors.Length == m.vertexCount) prepped++;
                        else Debug.LogError($"[DESYNC] model mesh not prepped: {AssetDatabase.GetAssetPath(m)} / {m.name}");
                    }
            Debug.Log($"[DESYNC] ThirdParty model meshes prepped: {prepped}/{total}");
            return total - prepped;
        }

        // ---------------------------------------------------------------------------------------------------------
        static int CheckShaders()
        {
            int fails = 0;
            foreach (var name in Shaders)
            {
                var s = Shader.Find(name);
                if (!s) { fails++; Debug.LogError("[DESYNC] shader missing " + name); continue; }
                foreach (var msg in ShaderUtil.GetShaderMessages(s))
                    if (msg.severity == ShaderCompilerMessageSeverity.Error)
                    { fails++; Debug.LogError($"[DESYNC] Shader error in {name}: {msg.message} ({msg.file}:{msg.line}) {msg.platform}"); }

                var data = ShaderUtil.GetShaderData(s);
                var pass = data.GetSubshader(0).GetPass(0);
                int ok = 0, total = 0;
                foreach (var kw in Variants)
                    foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                    {
                        total++;
                        var r = pass.CompileVariant(stage, kw, ShaderCompilerPlatform.GLES3x, BuildTarget.WebGL);
                        if (r.Success) { ok++; continue; }
                        fails++;
                        foreach (var m in r.Messages)
                            Debug.LogError($"[DESYNC] GLES3 compile {name} {stage} [{string.Join(",", kw)}]: {m.message} {m.messageDetails} ({m.file}:{m.line})");
                    }
                Debug.Log($"[DESYNC] {name}: GLES3/WebGL {ok}/{total} variants compiled");
            }
            return fails;
        }

        // ---------------------------------------------------------------------------------------------------------
        static readonly string Out = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.work/art"));

        static void Preview()
        {
            Directory.CreateDirectory(Out);
            ShaderUtil.allowAsyncCompilation = false;   // first SOURCE frame must not render with placeholder shaders
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cfg = GameConfig.I;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = cfg.fogStart;
            RenderSettings.fogEndDistance = cfg.fogEnd;
            RenderSettings.fogColor = cfg.fogColor;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            sun.shadows = LightShadows.None;

            Material M(string key) => AssetLib.Get<Material>(key);
            Color navy = new Color(0.07f, 0.08f, 0.16f), wall = new Color(0.12f, 0.13f, 0.24f), desk = new Color(0.22f, 0.24f, 0.38f);
            Color rack = new Color(0.06f, 0.06f, 0.1f), accentM = new Color(0.85f, 0.15f, 0.7f), accentC = new Color(0f, 0.7f, 0.8f);

            // Room shell: floor 14 x 26, walls 3.2 m, a partition with a door gap.
            Box(new Vector3(0, -0.1f, 8), new Vector3(14, 0.2f, 26), navy, M("mat.floor"));
            Box(new Vector3(-7, 1.6f, 8), new Vector3(0.3f, 3.2f, 26), wall, M("mat.world"));
            Box(new Vector3(7, 1.6f, 8), new Vector3(0.3f, 3.2f, 26), wall, M("mat.world"));
            Box(new Vector3(0, 1.6f, 21), new Vector3(14, 3.2f, 0.3f), wall, M("mat.world"));
            Box(new Vector3(-4.2f, 1.6f, 9), new Vector3(5.6f, 3.2f, 0.3f), wall, M("mat.world"));
            Box(new Vector3(4.2f, 1.6f, 9), new Vector3(5.6f, 3.2f, 0.3f), wall, M("mat.world"));
            Box(new Vector3(-6.8f, 1.0f, 4), new Vector3(0.06f, 0.08f, 10), accentM, M("mat.world"));
            Box(new Vector3(6.8f, 1.0f, 4), new Vector3(0.06f, 0.08f, 10), accentC, M("mat.world"));
            Box(new Vector3(0, 3.25f, 8), new Vector3(14, 0.1f, 26), new Color(0.05f, 0.05f, 0.1f), M("mat.world"));   // ceiling
            for (int i = 0; i < 3; i++)
            {
                float z = 2f + i * 2.4f;
                Box(new Vector3(-3.5f, 0.75f, z), new Vector3(1.6f, 0.06f, 0.8f), desk, M("mat.world"));
                Box(new Vector3(-3.5f, 0.37f, z), new Vector3(1.5f, 0.74f, 0.05f), wall, M("mat.world"));
                Box(new Vector3(-3.5f, 1.05f, z + 0.25f), new Vector3(0.6f, 0.4f, 0.05f), rack, M("mat.world"));
                Quad(new Vector3(-3.5f, 1.05f, z + 0.22f), new Vector2(0.54f, 0.34f), Quaternion.identity, M("mat.screen"));
                Box(new Vector3(5.6f, 1.0f, z + 1f), new Vector3(0.9f, 2f, 0.9f), rack, M("mat.world"));
                Box(new Vector3(5.12f, 1.0f, z + 1f), new Vector3(0.04f, 1.8f, 0.05f), accentC, M("mat.world"));
            }
            Box(new Vector3(0.8f, 1.6f, 14f), new Vector3(0.8f, 3.2f, 0.8f), wall, M("mat.world"));   // pillar
            Quad(new Vector3(-2f, 1.7f, 20.8f), new Vector2(1.2f, 1.8f), Quaternion.identity, M("mat.poster"));
            Quad(new Vector3(2f, 1.7f, 20.8f), new Vector2(1.2f, 1.8f), Quaternion.identity, M("mat.poster"));
            Quad(new Vector3(0f, 0.01f, 2.5f), new Vector2(3.2f, 0.5f), Quaternion.Euler(90f, 0f, 0f), M("mat.decal"));

            // Enemies / FX.
            var camPivot = new Vector3(4.2f, 3f, 6.5f);
            var camLook = Quaternion.LookRotation(new Vector3(-0.55f, -0.75f, 0.3f));
            Mesh(ConeMesh(cfg.cameraConeAngle, cfg.cameraConeRange, 20), camPivot, camLook, M("mat.cone"));
            var spot = new GameObject("Spot").AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.transform.SetPositionAndRotation(camPivot, camLook);
            spot.spotAngle = cfg.cameraConeAngle;
            spot.range = cfg.cameraConeRange + 2f;
            spot.intensity = 2.5f;
            spot.color = new Color(1f, 0.55f, 0.9f);   // ENEMIES idle spot colour
            spot.shadows = LightShadows.None;
            Box(camPivot, new Vector3(0.3f, 0.25f, 0.45f), rack, M("mat.world"));
            Mesh(Prism(6, 0.35f, 0.25f), new Vector3(-1.5f, 1.6f, 6f), Quaternion.identity, M("mat.drone"));
            Mesh(Prism(6, 0.35f, 0.25f), new Vector3(1.8f, 1.5f, 5f), Quaternion.identity, M("mat.hijacked"));
            Mesh(Crystal(), new Vector3(1.2f, 0.9f, 11.5f), Quaternion.Euler(10f, 30f, 8f), M("mat.ice"));
            Mesh(Ring(0.9f, 1.2f, 40), new Vector3(-2.5f, 0.02f, 12.5f), Quaternion.identity, M("mat.glitch"));
            Mesh(Ring(0.25f, 0.4f, 24), new Vector3(2.5f, 0.03f, 7f), Quaternion.identity, M("mat.lkp"));
            Box(new Vector3(0f, 1.3f, 20.5f), new Vector3(1.6f, 2.6f, 0.2f), wall, M("mat.world"));
            Quad(new Vector3(0f, 1.25f, 20.35f), new Vector2(1.2f, 2.2f), Quaternion.identity, M("mat.portal"));
            var head = Object.Instantiate(Resources.GetBuiltinResource<Mesh>("Sphere.fbx"));
            MeshPrep.Process(head);
            Mesh(head, new Vector3(-4.5f, 2.2f, 17f), Quaternion.identity, M("mat.architect")).transform.localScale = new Vector3(1.4f, 1.8f, 1.4f);

            var camGo = new GameObject("Cam");
            var cam = camGo.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(new Vector3(0.3f, cfg.eyeHeight, -2.5f), Quaternion.Euler(4f, 6f, 0f));
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;

            var worldMats = new Material[PostFXController.SourceTransparentKeys.Length];
            for (int i = 0; i < worldMats.Length; i++) worldMats[i] = M(PostFXController.SourceTransparentKeys[i]);

            void Layer(bool source)
            {
                LayerState.Set(source);
                PostFXController.SetWorldBlend(worldMats, source);
                RenderSettings.fogColor = source ? PostFXController.SourceFog : cfg.fogColor;
                cam.backgroundColor = RenderSettings.fogColor;
            }
            void Fx(float pulse = 0, float melt = 0, float trace = 0, float reboot = 0, float wipe = -1, float drip = 0, float wave = 99)
            {
                Shader.SetGlobalFloat("_Pulse", pulse);
                Shader.SetGlobalFloat("_Melt", melt);
                Shader.SetGlobalFloat("_Trace", trace);
                Shader.SetGlobalFloat("_Reboot", reboot);
                Shader.SetGlobalFloat("_Wipe", wipe);
                Shader.SetGlobalFloat("_Drip", drip);
                Shader.SetGlobalFloat("_FlashScale", 1f);
                Shader.SetGlobalVector("_FlipWave", new Vector4(cam.transform.position.x, 0f, cam.transform.position.z, wave));
            }

            Layer(false); Fx(); Shot(cam, "preview_render");
            Fx(melt: 0.85f, trace: 1f); Shot(cam, "preview_render_melt_trace");
            Fx(reboot: 0.55f); Shot(cam, "preview_reboot");
            Layer(true);
            var rain = GlyphRain.Attach(cam);
            if (rain) rain.GetComponent<ParticleSystem>().Simulate(1.5f, true, true);
            Fx(); Shot(cam, "preview_source");
            Fx(pulse: 0.9f, wipe: 0.45f, wave: 0.35f); Shot(cam, "preview_source_flip");
            Fx(melt: 1f, drip: 0.6f); Shot(cam, "preview_source_drip");
            Layer(false);
            Fx();
            AssetDatabase.SaveAssets();
        }

        static void Shot(Camera cam, string name)
        {
            var rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.Render();   // warm-up: the first frame after a keyword/blend switch can still use placeholder shaders in the editor
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(Out, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[DESYNC] preview " + name);
        }

        // --- tiny mesh kit (preview only) ----------------------------------------------------------------------
        static GameObject Box(Vector3 c, Vector3 size, Color col, Material m)
        {
            var mesh = Object.Instantiate(Resources.GetBuiltinResource<Mesh>("Cube.fbx"));
            var cols = new Color[mesh.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = col;
            mesh.colors = cols;
            var go = Mesh(MeshPrep.Process(mesh), c, Quaternion.identity, m);
            go.transform.localScale = size;
            return go;
        }

        static GameObject Quad(Vector3 c, Vector2 size, Quaternion rot, Material m)
        {
            // Built-in quad is visible from -Z (a camera looking down +Z); Euler(90,0,0) lays it on the floor facing up.
            var go = Mesh(Object.Instantiate(Resources.GetBuiltinResource<Mesh>("Quad.fbx")), c, rot, m);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            return go;
        }

        static GameObject Mesh(Mesh mesh, Vector3 pos, Quaternion rot, Material m)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetPositionAndRotation(pos, rot);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        static Mesh Build(List<Vector3> v, List<int> t, List<Color> c = null)
        {
            var mesh = new Mesh();
            mesh.SetVertices(v);
            if (c != null) mesh.SetColors(c);
            mesh.SetTriangles(t, 0);
            return MeshPrep.Process(mesh);
        }

        static Mesh ConeMesh(float angle, float range, int sides)
        {
            var v = new List<Vector3> { Vector3.zero };
            var c = new List<Color> { Color.white };   // like ENEMIES' cone: alpha 1 at the apex, 0.1 at the rim
            var t = new List<int>();
            float r = Mathf.Tan(angle * 0.5f * Mathf.Deg2Rad) * range;
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, range));
                c.Add(new Color(1f, 1f, 1f, 0.1f));
                t.AddRange(new[] { 0, 1 + (i + 1) % sides, 1 + i });
            }
            return Build(v, t, c);
        }

        static Mesh Prism(int sides, float radius, float height)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                v.Add(new Vector3(Mathf.Cos(a) * radius, -height / 2, Mathf.Sin(a) * radius));
                v.Add(new Vector3(Mathf.Cos(a) * radius, height / 2, Mathf.Sin(a) * radius));
            }
            for (int i = 0; i < sides; i++)
            {
                int a0 = i * 2, a1 = a0 + 1, b0 = (i + 1) % sides * 2, b1 = b0 + 1;
                t.AddRange(new[] { a0, a1, b1, a0, b1, b0 });
                if (i > 0 && i < sides - 1) t.AddRange(new[] { 1, b1, a1, 0, a0, b0 });   // cap fans from vertex 0/1
            }
            return Build(v, t);
        }

        static Mesh Crystal()
        {
            var v = new List<Vector3> { new Vector3(0, 0.9f, 0), new Vector3(0, -0.5f, 0), new Vector3(0.3f, 0.1f, 0), new Vector3(0, 0.2f, 0.25f), new Vector3(-0.28f, 0f, 0), new Vector3(0, 0.05f, -0.32f) };
            var t = new List<int> { 0, 3, 2, 0, 4, 3, 0, 5, 4, 0, 2, 5, 1, 2, 3, 1, 3, 4, 1, 4, 5, 1, 5, 2 };
            return Build(v, t);
        }

        static Mesh Ring(float inner, float outer, int sides)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                v.Add(new Vector3(Mathf.Cos(a) * inner, 0, Mathf.Sin(a) * inner));
                v.Add(new Vector3(Mathf.Cos(a) * outer, 0, Mathf.Sin(a) * outer));
                int i0 = i * 2, i1 = i0 + 1, j0 = (i + 1) % sides * 2, j1 = j0 + 1;
                t.AddRange(new[] { i0, j0, j1, i0, j1, i1 });
            }
            return Build(v, t);
        }
    }
}
