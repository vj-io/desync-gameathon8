using UnityEngine;

namespace Desync.Flow
{
    /// A story shard in a generated room: a floating knot on a puppet string, seen in both layers. Touching it in either layer
    /// collects it (ShardRun.Collect): saved at once, so a death or REBOOT never takes it back, and it does not come back.
    public sealed class ShardPickup : MonoBehaviour
    {
        public const float Height = 1.2f, Radius = 0.7f, StringTop = 3.4f, Size = 0.22f;

        public string id;
        Transform _glyph;

        static Mesh _mesh;
        static Material _mat;

        /// Builds the shard at world position `pos` (its knot's centre) under `parent`.
        public static ShardPickup Create(Transform parent, Vector3 pos, string id)
        {
            var go = new GameObject("Shard " + id);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var mat = Mat();

            var glyph = new GameObject("Glyph");
            glyph.transform.SetParent(go.transform, false);
            glyph.AddComponent<MeshFilter>().sharedMesh = Knot();
            Quiet(glyph.AddComponent<MeshRenderer>(), mat);

            var thread = new GameObject("String").AddComponent<LineRenderer>();
            thread.transform.SetParent(go.transform, false);
            thread.useWorldSpace = false;
            thread.positionCount = 2;
            thread.SetPosition(0, Vector3.up * Size);
            thread.SetPosition(1, Vector3.up * (StringTop - Height));
            thread.widthMultiplier = 0.02f;
            thread.startColor = thread.endColor = GameConfig.I.green;
            Quiet(thread, mat);

            var p = go.AddComponent<ShardPickup>();
            p.id = id;
            p._glyph = glyph.transform;
            return p;
        }

        void Update()
        {
            float t = Time.time;
            _glyph.SetLocalPositionAndRotation(new Vector3(0f, 0.08f * Mathf.Sin(t * 2f), 0f), Quaternion.Euler(0f, t * 90f, 0f));
            if (!GameState.Alive || GameState.Cinematic || !PlayerRef.Exists) return;
            Vector3 p = PlayerRef.Position, me = transform.position;
            float dx = p.x - me.x, dz = p.z - me.z;
            if (dx * dx + dz * dz > Radius * Radius || me.y < p.y || me.y > p.y + GameConfig.I.playerHeight + Radius) return;
            gameObject.SetActive(false);
            ShardRun.Collect(id, true);
        }

        static void Quiet(Renderer r, Material m)
        {
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// The puppet strings' own material (ART's mat.strings), or a plain unlit stand-in.
        static Material Mat()
        {
            if (_mat) return _mat;
            _mat = AssetLib.Get<Material>("mat.strings");
            if (_mat) return _mat;
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (!sh) sh = Shader.Find("Sprites/Default");
            return _mat = new Material(sh) { name = "mat.strings (fallback)", color = GameConfig.I.green };
        }

        /// An octahedron (white vertex colour: the FX shader tints by it), shared by every shard.
        static Mesh Knot()
        {
            if (_mesh) return _mesh;
            float s = Size, r = Size * 0.6f;
            var v = new[]
            {
                new Vector3(0f, s, 0f), new Vector3(r, 0f, 0f), new Vector3(0f, 0f, r),
                new Vector3(-r, 0f, 0f), new Vector3(0f, 0f, -r), new Vector3(0f, -s, 0f),
            };
            var tri = new[] { 0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 1, 4, 5, 1, 2, 5, 2, 3, 5, 3, 4, 5, 4, 1 };
            var col = new Color32[v.Length];
            for (int i = 0; i < col.Length; i++) col[i] = new Color32(255, 255, 255, 255);
            _mesh = new Mesh { name = "ShardKnot", vertices = v, colors32 = col };
            _mesh.SetTriangles(tri, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            return _mesh;
        }
    }
}
