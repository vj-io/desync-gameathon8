using UnityEngine;

namespace Desync.Player
{
    /// The Time-Attack ghost: a translucent wireframe capsule silhouette (~1.8 m, feet at the pivot, a short line showing
    /// where it faces) drawn in RENDER cyan or SOURCE green by the ghost's own layer, not the player's. Palette colours
    /// only (no threat hue); additive, so it reads as see-through. Built once, moved with Show, no allocation per frame.
    public sealed class GhostFigure : MonoBehaviour
    {
        MeshRenderer _mr;
        Material _render, _source;
        Mesh _mesh;

        public bool Visible => _mr && _mr.enabled;
        public bool InSource { get; private set; }

        public static GhostFigure Create(Transform parent)
        {
            var go = new GameObject("TimeAttackGhost");
            go.transform.SetParent(parent, false);
            var f = go.AddComponent<GhostFigure>();
            var cfg = GameConfig.I;
            f._mesh = Wire();
            f._render = Mat(cfg.cyan, false);
            f._source = Mat(cfg.green, true);
            go.AddComponent<MeshFilter>().sharedMesh = f._mesh;
            f._mr = go.AddComponent<MeshRenderer>();
            f._mr.sharedMaterial = f._render;
            f._mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            f._mr.receiveShadows = false;
            f._mr.enabled = false;
            return f;
        }

        public void Show(Vector3 feet, float yaw, bool source)
        {
            transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            if (source != InSource || !_mr.enabled)
            {
                InSource = source;
                _mr.sharedMaterial = source ? _source : _render;
            }
            if (!_mr.enabled) _mr.enabled = true;
        }

        public void Hide() { if (_mr && _mr.enabled) _mr.enabled = false; }

        void OnDestroy()
        {
            if (_mesh) Destroy(_mesh);
            if (_render) Destroy(_render);
            if (_source) Destroy(_source);
        }

        /// A copy of the additive LKP marker material (Desync/FX) recoloured; SOURCE green takes the colour-blind SOURCE hue.
        static Material Mat(Color c, bool source)
        {
            var from = AssetLib.Get<Material>("mat.lkp");
            if (!from)
            {
                var sh = Shader.Find("Sprites/Default");
                return sh ? new Material(sh) { name = "Ghost (fallback)", color = new Color(c.r, c.g, c.b, 0.45f) } : null;
            }
            var m = new Material(from) { name = source ? "GhostSource" : "GhostRender" };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Intensity", 0.8f);
            m.SetColor("_SourceColor", Color.clear);              // keeps its own layer's colour whatever the player is in
            m.SetVector("_Band", Vector4.zero);
            m.SetVector("_Anim", new Vector4(3f, 0.2f, 0.08f, 0f));   // slow pulse, light flicker: reads as a recording
            m.SetVector("_Shape", new Vector4(0f, 0f, 1f, 0f));
            m.SetFloat("_CbHue", source ? 2f : 0f);
            return m;
        }

        /// Line mesh: body rings at feet, waist and shoulders joined by 8 uprights, a head ring on 4 neck lines, a facing line.
        static Mesh Wire()
        {
            const int Seg = 16;
            float[] ringY = { 0.04f, 0.95f, 1.42f }; float body = 0.27f;
            const float HeadY = 1.66f, Head = 0.15f;
            var v = new Vector3[(ringY.Length + 1) * Seg + 2];
            var idx = new int[(ringY.Length + 1) * Seg * 2 + 8 * 2 + 4 * 2 + 2];
            int vi = 0, ii = 0;
            for (int r = 0; r <= ringY.Length; r++)
            {
                float y = r < ringY.Length ? ringY[r] : HeadY, rad = r < ringY.Length ? body : Head;
                int start = vi;
                for (int s = 0; s < Seg; s++)
                {
                    float a = s * Mathf.PI * 2f / Seg;
                    v[vi++] = new Vector3(Mathf.Sin(a) * rad, y, Mathf.Cos(a) * rad);
                    idx[ii++] = start + s; idx[ii++] = start + (s + 1) % Seg;
                }
            }
            for (int k = 0; k < 8; k++)
            {
                int s = k * Seg / 8;
                idx[ii++] = s; idx[ii++] = (ringY.Length - 1) * Seg + s;   // feet -> shoulders
            }
            for (int k = 0; k < 4; k++)
            {
                int s = k * Seg / 4;
                idx[ii++] = (ringY.Length - 1) * Seg + s; idx[ii++] = ringY.Length * Seg + s;   // shoulders -> head
            }
            v[vi] = new Vector3(0f, HeadY, Head); v[vi + 1] = new Vector3(0f, HeadY, Head + 0.3f);
            idx[ii++] = vi; idx[ii++] = vi + 1;
            var white = new Color32[v.Length];
            for (int i = 0; i < white.Length; i++) white[i] = new Color32(255, 255, 255, 255);   // Desync/FX tints by vertex colour
            var mesh = new Mesh { name = "GhostWire", vertices = v, colors32 = white };
            mesh.SetIndices(idx, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
