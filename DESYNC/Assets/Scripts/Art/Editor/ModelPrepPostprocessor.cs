using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Desync.Art.Editor
{
    /// Imported models under Assets/ThirdParty/Models/**: bakes each submesh's material base colour (x its texture at the
    /// vertex UV, for palette-atlas kits) into vertex colours, merges submeshes into one (one draw call with mat.world),
    /// then runs MeshPrep. Assign mat.world to the renderer afterwards (LEVELS does this on its prefabs).
    public class ModelPrepPostprocessor : AssetPostprocessor
    {
        const string Root = "Assets/ThirdParty/Models/";

        public override uint GetVersion() => 4;

        // Unity 6 refuses vertex access in OnPostprocessModel unless Read/Write is on (models are tiny; the CPU copy is cheap).
        void OnPreprocessModel()
        {
            if (assetPath.StartsWith(Root) && assetImporter is ModelImporter mi) mi.isReadable = true;
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(Root)) return;
            var textures = new Dictionary<Texture, Texture2D>();
            var done = new HashSet<Mesh>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (!mf.sharedMesh || !r) continue;
                if (done.Add(mf.sharedMesh)) Bake(mf.sharedMesh, r.sharedMaterials, textures);
                if (r.sharedMaterials.Length > 1) r.sharedMaterials = new[] { r.sharedMaterials[0] };
            }
            foreach (var t in textures.Values) if (t) Object.DestroyImmediate(t);
        }

        void Bake(Mesh mesh, Material[] mats, Dictionary<Texture, Texture2D> textures)
        {
            var vCount = mesh.vertexCount;
            var src = mesh.colors;
            var uv = new List<Vector2>();
            mesh.GetUVs(0, uv);
            var col = new Color[vCount];
            for (int i = 0; i < vCount; i++) col[i] = src.Length == vCount ? src[i] : Color.white;

            // Un-weld first so a vertex shared by two submeshes can take both colours.
            var tris = new List<int>();
            var srcSub = new List<int[]>();
            for (int s = 0; s < mesh.subMeshCount; s++) srcSub.Add(mesh.GetTriangles(s));
            var pos = mesh.vertices;
            var nrm = mesh.normals;
            var outPos = new List<Vector3>();
            var outNrm = new List<Vector3>();
            var outCol = new List<Color>();
            var outUv = new List<Vector2>();
            for (int s = 0; s < srcSub.Count; s++)
            {
                var m = s < mats.Length ? mats[s] : null;
                Color baseCol = Color.white;
                Texture2D tex = null;
                if (m)
                {
                    baseCol = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.color : Color.white;
                    var mt = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.HasProperty("_MainTex") ? m.mainTexture : null;
                    if (mt) tex = Readable(mt, textures);
                }
                foreach (var v in srcSub[s])
                {
                    var c = col[v] * baseCol;
                    if (tex && uv.Count == vCount) c *= tex.GetPixelBilinear(uv[v].x, uv[v].y);
                    c.a = 1f;
                    tris.Add(outPos.Count);
                    outPos.Add(pos[v]);
                    if (nrm.Length == vCount) outNrm.Add(nrm[v]);
                    outCol.Add(c);
                    if (uv.Count == vCount) outUv.Add(uv[v]);
                }
            }

            mesh.Clear();
            mesh.indexFormat = outPos.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(outPos);
            if (outNrm.Count == outPos.Count) mesh.SetNormals(outNrm);
            mesh.SetColors(outCol);
            if (outUv.Count == outPos.Count) mesh.SetUVs(0, outUv);
            mesh.subMeshCount = 1;
            mesh.SetTriangles(tris, 0, false);
            if (outNrm.Count != outPos.Count) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            MeshPrep.Process(mesh);
        }

        /// Texture pixels without touching its import settings: decode the source file (png/jpg/tga via LoadImage when possible).
        static Texture2D Readable(Texture t, Dictionary<Texture, Texture2D> cache)
        {
            if (cache.TryGetValue(t, out var r)) return r;
            r = null;
            var path = AssetDatabase.GetAssetPath(t);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(File.ReadAllBytes(path))) r = tex;
                else { Object.DestroyImmediate(tex); Debug.LogWarning($"[DESYNC] ModelPrep: can't decode {path}; using material colour only"); }
            }
            cache[t] = r;
            return r;
        }
    }
}
