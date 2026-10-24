using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Desync.Art.Tests
{
    /// Readability work of M3 rendered for real (a camera into a render texture, pixels read back): 'X' walls draw as a dense slab in SOURCE,
    /// the Sysadmin Key cone variants draw through walls, the colour-blind palette reaches the shaders. Needs ArtSetup to have run (the
    /// xray cone materials) and the shared materials in AssetLib.
    public class ArtReadabilityTests
    {
        const int Size = 96;
        GameObject _camGo, _root;
        Camera _cam;
        RenderTexture _rt;
        Material _world;

        [UnitySetUp]
        public IEnumerator Up()
        {
            _root = new GameObject("ArtTestRoot");
            _camGo = new GameObject("ArtTestCam", typeof(Camera));
            _camGo.transform.SetPositionAndRotation(new Vector3(0f, 0f, -3f), Quaternion.identity);
            _cam = _camGo.GetComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = Color.black;
            _cam.orthographic = true; _cam.orthographicSize = 0.8f; _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 20f;
            _rt = new RenderTexture(Size, Size, 24);
            _cam.targetTexture = _rt;
            _world = AssetLib.Get<Material>("mat.world");
            Assert.IsNotNull(_world, "mat.world is registered (ArtSetup)");
            yield return null;
        }

        [TearDown]
        public void Down()
        {
            Palette.Colorblind = false; PlayerPrefs.DeleteKey(Palette.Key);
            LayerState.Set(false);
            PostFXController.SetWorldBlend(new[] { _world }, false);
            if (_cam) _cam.targetTexture = null;
            if (_rt) { _rt.Release(); Object.Destroy(_rt); }
            if (_camGo) Object.Destroy(_camGo);
            if (_root) Object.Destroy(_root);
        }

        /// A 1 x 1 m quad facing the camera at depth z, vertex colour c, run through MeshPrep like every room mesh.
        GameObject Quad(string name, Material mat, Color c, float z)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.colors = new[] { c, c, c, c };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            MeshPrep.Process(mesh);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(_root.transform, false);
            go.transform.position = new Vector3(0f, 0f, z);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// Average LINEAR colour of the central 40 % of the frame.
        Color Render()
        {
            _cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = _rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            int lo = (int)(Size * 0.3f), hi = (int)(Size * 0.7f);
            float r = 0, g = 0, b = 0; int n = 0;
            for (int y = lo; y < hi; y++)
                for (int x = lo; x < hi; x++) { var c = tex.GetPixel(x, y).linear; r += c.r; g += c.g; b += c.b; n++; }
            Object.Destroy(tex);
            return new Color(r / n, g / n, b / n);
        }

        void Source(bool on)
        {
            LayerState.Set(on);
            PostFXController.SetWorldBlend(new[] { _world }, on);
        }

        [Test]
        public void Solid_Flag_Is_Well_Below_Every_Ordinary_Vertex_Alpha()
        {
            var c = WorldFlags.Solid(new Color(0.1f, 0.2f, 0.3f, 1f));
            Assert.AreEqual(WorldFlags.SolidAlpha, c.a);
            Assert.AreEqual(0.1f, c.r);
            Assert.Less(WorldFlags.SolidAlpha, WorldFlags.SolidBelow);
            Assert.Greater(0.7f, WorldFlags.SolidBelow, "wall colours multiplied by a shade factor keep alpha >= 0.7, clear of the flag");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Solid_X_Wall_Draws_As_A_Dense_Slab_In_Source()
        {
            var vc = new Color(0.05f, 0.06f, 0.12f);
            Source(true);
            var open = Quad("open", _world, vc, 0f);
            yield return null;
            var a = Render();
            Object.Destroy(open); yield return null;
            var solid = Quad("solid", _world, WorldFlags.Solid(vc), 0f);
            yield return null;
            var b = Render();
            Debug.Log($"[ART-TEST] SOURCE quad average green: passable {a.g:0.000}, solid {b.g:0.000}");
            Assert.Greater(a.g, 0f, "the passable wall draws something (wireframe, glyphs)");
            Assert.Greater(b.g, a.g * 2.5f, "a solid wall's slab is far denser than the thin passable look");
            Assert.Greater(b.g, 0.12f, "and reads as a block, not a wire");
            Object.Destroy(solid);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Solid_Flag_Changes_Nothing_In_Render()
        {
            var vc = new Color(0.2f, 0.25f, 0.4f);
            Source(false);
            var open = Quad("open", _world, vc, 0f);
            yield return null;
            var a = Render();
            Object.Destroy(open); yield return null;
            var solid = Quad("solid", _world, WorldFlags.Solid(vc), 0f);
            yield return null;
            var b = Render();
            Assert.AreEqual(a.r, b.r, 0.01f); Assert.AreEqual(a.g, b.g, 0.01f); Assert.AreEqual(a.b, b.b, 0.01f, "RENDER ignores the flag: 'X' walls already read heavier there");
            Object.Destroy(solid);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Xray_Cone_Draws_Through_A_Wall_The_Plain_Cone_Does_Not()
        {
            var plain = ConeMats.Get(false, false); var xray = ConeMats.Get(false, true);
            Assert.IsNotNull(plain); Assert.IsNotNull(xray);
            Assert.AreNotSame(plain, xray, "the xray variant is its own material (run ArtSetup)");
            Assert.AreEqual((float)UnityEngine.Rendering.CompareFunction.Always, xray.GetFloat("_ZTest"));
            Assert.AreEqual((float)UnityEngine.Rendering.CompareFunction.LessEqual, plain.GetFloat("_ZTest"));
            Assert.AreSame(ConeMats.Get(true, true), AssetLib.Get<Material>("mat.cone.trace.xray"));

            Source(false);
            Quad("wall", _world, new Color(0.3f, 0.3f, 0.35f), 0f);   // opaque in RENDER, in front
            var cone = Quad("cone", plain, Color.white, 1f);           // behind it
            yield return null;
            var hidden = Render();
            cone.GetComponent<MeshRenderer>().sharedMaterial = xray;
            yield return null;
            var seen = Render();
            Debug.Log($"[ART-TEST] cone behind a wall: plain r {hidden.r:0.000}, xray r {seen.r:0.000}");
            Assert.Greater(seen.r, hidden.r + 0.05f, "the Sysadmin Key variant shows through the wall");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Colorblind_Palette_Reaches_The_Shaders_And_Marks_Threat_Bodies()
        {
            Assert.IsFalse(Palette.Colorblind);
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_Colorblind"));
            var vc = new Color(0.05f, 0.06f, 0.12f);
            Source(true);
            Quad("wall", _world, vc, 0f);
            yield return null;
            var normal = Render();
            Palette.Colorblind = true;
            yield return null;
            Assert.AreEqual(1f, Shader.GetGlobalFloat("_Colorblind"));
            var src = Shader.GetGlobalVector("_CbSource");
            Assert.AreEqual(Palette.CbGreen.linear.r, src.x, 1e-3f); Assert.AreEqual(Palette.CbGreen.linear.g, src.y, 1e-3f);
            var cb = Render();
            Debug.Log($"[ART-TEST] SOURCE wall linear rgb: authored ({normal.r:0.000},{normal.g:0.000},{normal.b:0.000}) colour-blind ({cb.r:0.000},{cb.g:0.000},{cb.b:0.000})");
            Assert.Less(normal.r, normal.g * 0.1f, "authored SOURCE is pure green");
            Assert.Greater(cb.r, cb.g * 0.6f, "the colour-blind SOURCE hue is yellow: red joins green");

            Assert.AreEqual(1f, AssetLib.Get<Material>("mat.ice").GetFloat("_Hazard"), "ICE carries the stripe + danger-hue flag");
            Assert.AreEqual(1f, AssetLib.Get<Material>("mat.cone.trace").GetFloat("_CbHue"), "the TRACE cone is a threat hue");
            Assert.AreEqual(0f, AssetLib.Get<Material>("mat.hijacked").GetFloat("_Hazard"), "the hijacked (friendly) drone is not");
        }

        [Test]
        public void Palette_Persists_And_Defaults_Off()
        {
            PlayerPrefs.DeleteKey(Palette.Key); Palette.Refresh();
            Assert.IsFalse(Palette.Colorblind);
            Assert.AreEqual(GameConfig.I.green, Palette.Green);
            Palette.Colorblind = true;
            Assert.AreEqual(1, PlayerPrefs.GetInt(Palette.Key, 0));
            Assert.AreEqual(Palette.CbGreen, Palette.Green); Assert.AreEqual(Palette.CbDanger, Palette.Danger);
            Assert.AreNotEqual(GameConfig.I.danger, Palette.Danger);
            PlayerPrefs.SetInt(Palette.Key, 0); Palette.Refresh();
            Assert.IsFalse(Palette.Colorblind, "Refresh re-reads the saved flag");
        }
    }
}
