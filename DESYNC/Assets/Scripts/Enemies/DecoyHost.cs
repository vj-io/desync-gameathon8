using UnityEngine;

namespace Desync.Enemies
{
    /// Visuals of the Decoys pool: one DontDestroyOnLoad object with Decoys.Capacity slots, each holding the three looks
    /// (built once, toggled, never instantiated again). Drives Decoys.Tick every frame.
    public class DecoyHost : MonoBehaviour
    {
        public const float NoisePulse = 0.5f;   // seconds per ring pulse

        GameObject[,] _looks;

        internal static DecoyHost Create()
        {
            var go = new GameObject("Decoys");
            if (Application.isPlaying) DontDestroyOnLoad(go);
            var h = go.AddComponent<DecoyHost>();
            h.Build();
            return h;
        }

        void Build()
        {
            var c = GameConfig.I;
            var figure = Figure();
            var rb = new MeshBuilder();
            rb.Ring(0.8f, 1f, 32, 0.03f, Color.white);
            var ring = rb.ToMesh("DecoyRing", false);
            var cyan = EnemyKit.Mat("mat.hijacked", c.cyan);
            var green = EnemyKit.Mat("mat.glitch", new Color(0f, 1f, 0.4f, 0.5f), true);

            _looks = new GameObject[Decoys.Capacity, 3];
            for (int i = 0; i < Decoys.Capacity; i++)
            {
                var slot = EnemyKit.Child("Decoy" + i, transform, Vector3.zero);
                _looks[i, (int)DecoyKind.Silhouette] = EnemyKit.MeshObj("Silhouette", slot, figure, cyan).gameObject;
                _looks[i, (int)DecoyKind.Echo] = EnemyKit.MeshObj("Echo", slot, figure, green).gameObject;
                _looks[i, (int)DecoyKind.Noise] = EnemyKit.MeshObj("Noise", slot, ring, cyan).gameObject;
                for (int k = 0; k < 3; k++) _looks[i, k].SetActive(false);
            }
        }

        /// Player-sized figure (~1.8 m, feet at y = 0): legs, torso, shoulders, head.
        static Mesh Figure()
        {
            var b = new MeshBuilder();
            b.Box(new Vector3(-0.12f, 0.42f, 0f), new Vector3(0.16f, 0.84f, 0.18f), Color.white);
            b.Box(new Vector3(0.12f, 0.42f, 0f), new Vector3(0.16f, 0.84f, 0.18f), Color.white);
            b.Prism(new Vector3(0f, 1.15f, 0f), 6, 0.24f, 0.62f, Color.white, Color.white);
            b.Box(new Vector3(0f, 1.42f, 0f), new Vector3(0.62f, 0.12f, 0.2f), Color.white);
            b.Prism(new Vector3(0f, 1.66f, 0f), 6, 0.13f, 0.28f, Color.white, Color.white, 30f);
            return b.ToMesh("DecoyFigure", false);
        }

        void Update() => Decoys.Tick();

        internal void Show(int slot, DecoyKind kind, Vector3 pos)
        {
            var s = transform.GetChild(slot);
            s.SetPositionAndRotation(pos, Quaternion.identity);
            for (int k = 0; k < 3; k++) _looks[slot, k].SetActive(k == (int)kind);
            _looks[slot, (int)kind].transform.localScale = Vector3.one;
        }

        internal void Hide(int slot)
        {
            for (int k = 0; k < 3; k++) if (_looks[slot, k].activeSelf) _looks[slot, k].SetActive(false);
        }

        /// Noise rings pulse outward; figures flicker like a glitching glyph (pattern, not a new hue).
        internal void Animate(int slot, DecoyKind kind, float age)
        {
            var look = _looks[slot, (int)kind];
            if (kind == DecoyKind.Noise) look.transform.localScale = Vector3.one * (0.4f + 1.6f * Mathf.Repeat(age / NoisePulse, 1f));
            else look.transform.localScale = new Vector3(1f, (Time.frameCount & 6) == 0 ? 0.97f : 1f, 1f);
        }
    }
}
