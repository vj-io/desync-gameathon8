using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Desync.Levels.Editor
{
    /// Vertex-coloured mesh accumulator. Quads wind themselves to face the given normal. ToMesh runs MeshPrep.
    sealed class MeshKit
    {
        readonly List<Vector3> pos = new List<Vector3>();
        readonly List<Vector3> nrm = new List<Vector3>();
        readonly List<Color> col = new List<Color>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> idx = new List<int>();

        public bool Empty => idx.Count == 0;

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color ca, Color cb, Color cc, Color cd,
                         Vector2 ua = default, Vector2 ub = default, Vector2 uc = default, Vector2 ud = default)
        {
            int i = pos.Count;
            pos.Add(a); pos.Add(b); pos.Add(c); pos.Add(d);
            for (int k = 0; k < 4; k++) nrm.Add(n);
            col.Add(ca); col.Add(cb); col.Add(cc); col.Add(cd);
            uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) >= 0f) idx.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else idx.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color k) => Quad(a, b, c, d, n, k, k, k, k);

        /// Vertical face over the ground segment p0-p1, from y0 to y1, colour graded bottom -> top.
        public void Face(Vector3 p0, Vector3 p1, float y0, float y1, Vector3 n, Color bottom, Color top)
        {
            var u0 = Vector3.up * y0; var u1 = Vector3.up * y1;
            Quad(p0 + u0, p0 + u1, p1 + u1, p1 + u0, n, bottom, top, top, bottom);
        }

        /// Axis-aligned box without its bottom face; skip[i] hides side i (0 +z, 1 +x, 2 -z, 3 -x).
        public void Box(Vector3 min, Vector3 max, Color side, Color top, bool[] skip = null)
        {
            Vector3 a = new Vector3(min.x, 0, max.z), b = new Vector3(max.x, 0, max.z), c = new Vector3(max.x, 0, min.z), d = new Vector3(min.x, 0, min.z);
            if (skip == null || !skip[0]) Face(a, b, min.y, max.y, Vector3.forward, side, side);
            if (skip == null || !skip[1]) Face(b, c, min.y, max.y, Vector3.right, side * 0.85f, side * 0.85f);
            if (skip == null || !skip[2]) Face(c, d, min.y, max.y, Vector3.back, side, side);
            if (skip == null || !skip[3]) Face(d, a, min.y, max.y, Vector3.left, side * 0.85f, side * 0.85f);
            var y = Vector3.up * max.y;
            Quad(a + y, b + y, c + y, d + y, Vector3.up, top);
        }

        /// Appends `src` transformed by `m`. Colour per vertex from the mesh (ART bakes it on import), else the
        /// submesh colour; then `grade` maps it into the room palette.
        public void Append(Mesh src, Matrix4x4 m, Color[] subColors, Func<Color, Color> grade)
        {
            var v = src.vertices; var n = src.normals; var c = src.colors;
            bool hasN = n.Length == v.Length, hasC = c.Length == v.Length;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                if (src.GetTopology(s) != MeshTopology.Triangles) continue;
                var sub = subColors != null && subColors.Length > 0 ? subColors[Mathf.Min(s, subColors.Length - 1)] : Color.gray;
                foreach (var k in src.GetTriangles(s))
                {
                    idx.Add(pos.Count);
                    pos.Add(m.MultiplyPoint3x4(v[k]));
                    nrm.Add(hasN ? m.MultiplyVector(n[k]).normalized : Vector3.up);
                    col.Add(grade(hasC ? c[k] : sub));
                    uv.Add(Vector2.zero);
                }
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = pos.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetColors(col);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateBounds();
            return Desync.Art.MeshPrep.Process(mesh);
        }
    }
}
