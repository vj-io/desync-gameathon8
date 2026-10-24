using UnityEngine;

namespace Desync.Enemies
{
    public enum DecoyKind { Silhouette, Echo, Noise }

    /// CONTRACT (M3): decoys that cameras, drones and ICE can lock onto (PHANTOM exploits, Poltergeist, Afterimage...).
    /// Signatures frozen; the enemies workstream implements (pooled, zero GC). Stub bodies are inert.
    public static class Decoys
    {
        /// Spawns a decoy at pos for life seconds; returns its id (or -1 when the pool is full).
        public static int Spawn(Vector3 pos, float life, DecoyKind kind) => -1;
        /// Nearest live decoy within range of from (any kind).
        public static bool Nearest(Vector3 from, float range, out Vector3 pos, out DecoyKind kind) { pos = default; kind = default; return false; }
        /// Removes every decoy (room reset / dispose).
        public static void ClearAll() { }
        public static int Count => 0;
    }
}
