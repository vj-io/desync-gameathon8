using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Desync.Art
{
    /// CONTRACT: called by LevelBuilder on every procedural mesh, and by the Art model postprocessor on imported models,
    /// so WorldMat gets the vertex data it needs (e.g. barycentric coords for wireframe/edges). Signature frozen; Art owns the body.
    /// Must be idempotent and must keep vertex colours (base colour) intact.
    ///
    /// What it writes (in place, returns the same mesh):
    /// - Un-welds triangles (3 unique vertices per triangle); keeps positions, normals, vertex colours (white if missing), uv0, uv1, submeshes.
    /// - UV channel 3 (TEXCOORD3) = (bary.x, bary.y, bary.z, flags): bary is (1,0,0)/(0,1,0)/(0,0,1) per corner;
    ///   flags = 8 + bits of edges to hide in RENDER (1: edge opposite x, 2: opposite y, 4: opposite z).
    ///   An edge is hidden when exactly two triangles share it and they are coplanar (quad diagonals, flat-face triangulation).
    /// - 32-bit indices when the result exceeds 65535 vertices.
    public static class MeshPrep
    {
        public const int Channel = 3;
        const float Coplanar = 0.999f;
        const float Quantum = 1e-4f;

        public static Mesh Process(Mesh mesh)
        {
            if (!mesh || !mesh.isReadable || IsPrepped(mesh)) return mesh;
            for (int s = 0; s < mesh.subMeshCount; s++)
                if (mesh.GetTopology(s) != MeshTopology.Triangles) return mesh;

            var srcPos = mesh.vertices;
            var srcNrm = mesh.normals;
            var srcCol = mesh.colors;
            var srcUv0 = new List<Vector2>();
            var srcUv1 = new List<Vector2>();
            mesh.GetUVs(0, srcUv0);
            mesh.GetUVs(1, srcUv1);
            bool hasNrm = srcNrm.Length == srcPos.Length, hasCol = srcCol.Length == srcPos.Length;
            bool hasUv0 = srcUv0.Count == srcPos.Length, hasUv1 = srcUv1.Count == srcPos.Length;

            int subCount = mesh.subMeshCount;
            var subTris = new int[subCount][];
            int total = 0;
            for (int s = 0; s < subCount; s++) { subTris[s] = mesh.GetTriangles(s); total += subTris[s].Length; }
            if (total == 0) return mesh;

            // Flat list of corners (source vertex index) in submesh order.
            var corners = new int[total];
            int k = 0;
            for (int s = 0; s < subCount; s++) foreach (var i in subTris[s]) corners[k++] = i;
            int triCount = total / 3;

            var flags = HiddenEdges(srcPos, corners, triCount);

            var pos = new Vector3[total];
            var nrm = hasNrm ? new Vector3[total] : null;
            var col = new Color[total];
            var uv0 = hasUv0 ? new Vector2[total] : null;
            var uv1 = hasUv1 ? new Vector2[total] : null;
            var bary = new Vector4[total];
            for (int c = 0; c < total; c++)
            {
                int v = corners[c];
                pos[c] = srcPos[v];
                if (hasNrm) nrm[c] = srcNrm[v];
                col[c] = hasCol ? srcCol[v] : Color.white;
                if (hasUv0) uv0[c] = srcUv0[v];
                if (hasUv1) uv1[c] = srcUv1[v];
                int corner = c % 3;
                bary[c] = new Vector4(corner == 0 ? 1 : 0, corner == 1 ? 1 : 0, corner == 2 ? 1 : 0, 8 + flags[c / 3]);
            }

            mesh.Clear();
            mesh.indexFormat = total > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = pos;
            if (hasNrm) mesh.normals = nrm;
            mesh.colors = col;
            if (hasUv0) mesh.uv = uv0;
            if (hasUv1) mesh.uv2 = uv1;
            mesh.SetUVs(Channel, bary);
            mesh.subMeshCount = subCount;
            int start = 0;
            for (int s = 0; s < subCount; s++)
            {
                var idx = new int[subTris[s].Length];
                for (int i = 0; i < idx.Length; i++) idx[i] = start + i;
                mesh.SetTriangles(idx, s, false);
                start += idx.Length;
            }
            if (!hasNrm) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// True when the mesh already carries MeshPrep data (un-welded, flags present).
        public static bool IsPrepped(Mesh mesh)
        {
            if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord3) || mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord3) != 4) return false;
            long indices = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) indices += mesh.GetIndexCount(s);
            if (indices != mesh.vertexCount) return false;
            var uvs = new List<Vector4>(mesh.vertexCount);
            mesh.GetUVs(Channel, uvs);
            return uvs.Count > 0 && uvs[0].w >= 7.5f;
        }

        /// Per-triangle bitmask of edges shared by exactly two coplanar triangles (bit i = edge opposite corner i).
        static int[] HiddenEdges(Vector3[] pos, int[] corners, int triCount)
        {
            var normals = new Vector3[triCount];
            for (int t = 0; t < triCount; t++)
            {
                Vector3 a = pos[corners[t * 3]], b = pos[corners[t * 3 + 1]], c = pos[corners[t * 3 + 2]];
                normals[t] = Vector3.Cross(b - a, c - a).normalized;
            }

            var edges = new Dictionary<(Vector3Int, Vector3Int), List<int>>(triCount * 2);
            for (int t = 0; t < triCount; t++)
                for (int e = 0; e < 3; e++)
                {
                    var p = Key(pos[corners[t * 3 + (e + 1) % 3]]);
                    var q = Key(pos[corners[t * 3 + (e + 2) % 3]]);
                    var key = Less(p, q) ? (p, q) : (q, p);
                    if (!edges.TryGetValue(key, out var list)) edges[key] = list = new List<int>(2);
                    list.Add(t * 3 + e);
                }

            var flags = new int[triCount];
            foreach (var list in edges.Values)
            {
                if (list.Count != 2) continue;
                int t0 = list[0] / 3, t1 = list[1] / 3;
                if (normals[t0] == Vector3.zero || Vector3.Dot(normals[t0], normals[t1]) < Coplanar) continue;
                flags[t0] |= 1 << (list[0] % 3);
                flags[t1] |= 1 << (list[1] % 3);
            }
            return flags;
        }

        static Vector3Int Key(Vector3 p) =>
            new Vector3Int(Mathf.RoundToInt(p.x / Quantum), Mathf.RoundToInt(p.y / Quantum), Mathf.RoundToInt(p.z / Quantum));

        static bool Less(Vector3Int a, Vector3Int b) =>
            a.x != b.x ? a.x < b.x : a.y != b.y ? a.y < b.y : a.z < b.z;
    }
}
