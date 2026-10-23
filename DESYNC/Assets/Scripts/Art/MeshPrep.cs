using UnityEngine;

namespace Desync.Art
{
    /// CONTRACT: called by LevelBuilder on every procedural mesh, and by the Art model postprocessor on imported models,
    /// so WorldMat gets the vertex data it needs (e.g. barycentric coords for wireframe/edges). Signature frozen; Art owns the body.
    /// Must be idempotent and must keep vertex colours (base colour) intact.
    public static class MeshPrep
    {
        public static Mesh Process(Mesh mesh) => mesh;
    }
}
