using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Desync.Enemies
{
    /// Build helpers shared by EnemyFactory (edit time and runtime) plus a few runtime utilities.
    static class EnemyKit
    {
        /// What blocks sight: walls/solid props and floors. Never the player, triggers or enemies.
        public const int SightMask = (1 << GameLayers.WallRender) | (1 << GameLayers.Floor);

        public static readonly Color Navy = new Color(0.10f, 0.11f, 0.19f);
        public static readonly Color Charcoal = new Color(0.16f, 0.16f, 0.20f);

        /// RoomClock seconds since `last` (0 while the clock is stopped or right after it was reset).
        public static float Step(ref float last)
        {
            float t = RoomClock.Time, dt = t - last;
            last = t;
            return dt > 0f ? dt : 0f;
        }

        /// True when a sphere (centre p, radius r) overlaps the player's capsule.
        public static bool TouchesPlayer(Vector3 p, float r)
        {
            if (!PlayerRef.Exists) return false;
            var c = GameConfig.I;
            Vector3 feet = PlayerRef.Position;
            float pr = c.playerRadius;
            var q = new Vector3(feet.x, Mathf.Clamp(p.y, feet.y + pr, feet.y + c.playerHeight - pr), feet.z);
            float d = r + pr;
            return (p - q).sqrMagnitude < d * d;
        }

        /// Point within an (apex, forward, full angle, range) cone.
        public static bool InCone(Vector3 p, Vector3 apex, Vector3 fwd, float angle, float range)
        {
            Vector3 d = p - apex;
            float m = d.magnitude;
            return m <= range && (m < 1e-3f || Vector3.Angle(fwd, d) <= angle * 0.5f);
        }

        public static void Kill(Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        // ---------- materials ----------

        /// AssetLib material, or a plain coloured stand-in (ART registers keys concurrently).
        public static Material Mat(string key, Color fallback, bool transparent = false)
        {
            var m = AssetLib.Get<Material>(key);
            if (m) return m;
            var sh = Shader.Find(transparent ? "Sprites/Default" : "Universal Render Pipeline/Unlit");
            if (!sh) sh = Shader.Find("Sprites/Default");
            return new Material(sh) { name = key + " (fallback)", color = fallback };
        }

        public static Material World => Mat("mat.world", Charcoal);

        // ---------- objects ----------

        public static GameObject Root(string name, Transform parent, Vector3 pos, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            return go;
        }

        public static Transform Child(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        public static MeshRenderer MeshObj(string name, Transform parent, Mesh mesh, Material mat)
        {
            var t = Child(name, parent, Vector3.zero);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = t.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            NoShadows(r);
            return r;
        }

        public static void NoShadows(Renderer r)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        public static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject, layer);
        }

        public static BoxCollider Box(string name, Transform parent, Vector3 center, Vector3 size, int layer, bool trigger = false)
        {
            var t = Child(name, parent, Vector3.zero);
            t.gameObject.layer = layer;
            var b = t.gameObject.AddComponent<BoxCollider>();
            b.center = center;
            b.size = size;
            b.isTrigger = trigger;
            return b;
        }

        /// Instantiates AssetLib model `key` under parent: colliders stripped, largest dimension = size, centred on the
        /// parent (base on the parent when `bottom`), every renderer using `mat` (if given). Null if the key is missing.
        /// `bounds` = the fitted model's bounds in parent space.
        public static GameObject Model(string key, Transform parent, float size, Material mat, bool bottom, out Bounds bounds)
        {
            bounds = default;
            var prefab = AssetLib.Get<GameObject>(key);
            if (!prefab) return null;
            var go = Object.Instantiate(prefab);
            go.name = key;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Kill(c);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Kill(mb);
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { Kill(go); return null; }
            Bounds b = rs[0].bounds;
            foreach (var r in rs)
            {
                b.Encapsulate(r.bounds);
                NoShadows(r);
                if (!mat) continue;
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
            float s = size / Mathf.Max(b.size.x, b.size.y, b.size.z, 1e-4f);
            Vector3 offset = -b.center * s;
            if (bottom) offset.y = -b.min.y * s;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * s;
            bounds = new Bounds(b.center * s + offset, b.size * s);
            return go;
        }

        public static TextMeshPro Text(string name, Transform parent, Vector3 localPos, Vector2 size, float fontSize,
                                       string fontKey, Color color, bool faceForward = true)
        {
            var t = Child(name, parent, localPos);
            // TMP reads along +Z (viewer behind it); flip so it reads for a viewer standing on the parent's +Z side.
            if (faceForward) t.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var tmp = t.gameObject.AddComponent<TextMeshPro>();
            var font = AssetLib.Get<TMP_FontAsset>(fontKey);
            if (font) tmp.font = font;
            tmp.rectTransform.sizeDelta = size;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.richText = true;
            NoShadows(tmp.GetComponent<MeshRenderer>());
            return tmp;
        }

        /// Deterministic per-position hash (stable across edit-time builds).
        public static int Hash(Vector3 p) =>
            (Mathf.RoundToInt(p.x * 13f) * 73856093 ^ Mathf.RoundToInt(p.y * 13f) * 19349663 ^ Mathf.RoundToInt(p.z * 13f) * 83492791) & 0x7FFFFFFF;
    }

    /// Flat-shaded procedural mesh builder (vertex colours, uv0, normals). Faces are auto-wound to face `outward`.
    sealed class MeshBuilder
    {
        readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        readonly List<Color> c = new List<Color>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();

        public void Tri(Vector3 a, Vector3 b, Vector3 d, Vector3 outward, Color ca, Color cb, Color cd)
        {
            Vector3 nrm = Vector3.Cross(b - a, d - a);
            if (Vector3.Dot(nrm, outward) < 0f) { (b, d) = (d, b); (cb, cd) = (cd, cb); nrm = -nrm; }
            nrm.Normalize();
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(d);
            n.Add(nrm); n.Add(nrm); n.Add(nrm);
            c.Add(ca); c.Add(cb); c.Add(cd);
            uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(0f, 1f));
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 d, Vector3 outward, Color col) => Tri(a, b, d, outward, col, col, col);

        /// Quad a-b-d-e (in order around the edge).
        public void Quad(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Vector3 outward, Color col)
        {
            Vector2 ub = new Vector2(1f, 0f), ue = new Vector2(0f, 1f);   // uv stays with its corner when the winding flips
            Vector3 nrm = Vector3.Cross(b - a, d - a);
            if (Vector3.Dot(nrm, outward) < 0f) { (b, e) = (e, b); (ub, ue) = (ue, ub); nrm = -nrm; }
            nrm.Normalize();
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(d); v.Add(e);
            for (int k = 0; k < 4; k++) { n.Add(nrm); c.Add(col); }
            uv.Add(new Vector2(0f, 0f)); uv.Add(ub); uv.Add(new Vector2(1f, 1f)); uv.Add(ue);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        public void Box(Vector3 center, Vector3 size, Color col) => Box(center, size, Quaternion.identity, col);

        public void Box(Vector3 center, Vector3 size, Quaternion rot, Color col)
        {
            Vector3 h = size * 0.5f;
            for (int axis = 0; axis < 3; axis++)
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 N = Vector3.zero; N[axis] = s;
                Vector3 U = Vector3.zero; U[(axis + 1) % 3] = 1f;
                Vector3 W = Vector3.zero; W[(axis + 2) % 3] = 1f;
                Vector3 o = Vector3.Scale(N, h);
                Vector3 u = Vector3.Scale(U, h), w = Vector3.Scale(W, h);
                Quad(center + rot * (o - u - w), center + rot * (o + u - w), center + rot * (o + u + w), center + rot * (o - u + w),
                     rot * N, col);
            }
        }

        /// Vertical prism (axis Y), `sides` faces, flat-shaded with caps.
        public void Prism(Vector3 center, int sides, float radius, float height, Color side, Color cap, float rotDeg = 0f)
        {
            float y0 = -height * 0.5f, y1 = height * 0.5f;
            for (int i = 0; i < sides; i++)
            {
                float a0 = (rotDeg + 360f * i / sides) * Mathf.Deg2Rad, a1 = (rotDeg + 360f * (i + 1) / sides) * Mathf.Deg2Rad;
                var p0 = new Vector3(Mathf.Sin(a0) * radius, 0f, Mathf.Cos(a0) * radius);
                var p1 = new Vector3(Mathf.Sin(a1) * radius, 0f, Mathf.Cos(a1) * radius);
                Vector3 mid = (p0 + p1) * 0.5f;
                Quad(center + p0 + Vector3.up * y0, center + p1 + Vector3.up * y0, center + p1 + Vector3.up * y1, center + p0 + Vector3.up * y1, mid, side);
                Tri(center + Vector3.up * y1, center + p0 + Vector3.up * y1, center + p1 + Vector3.up * y1, Vector3.up, cap);
                Tri(center + Vector3.up * y0, center + p0 + Vector3.up * y0, center + p1 + Vector3.up * y0, Vector3.down, cap);
            }
        }

        /// Double cone along `axis` with a jittered ring (crystals, LKP diamond).
        public void Bipyramid(Vector3 center, Quaternion rot, int sides, float radius, float top, float bottom, Color col, ref uint seed, float jitter)
        {
            var ring = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = 360f * i / sides * Mathf.Deg2Rad;
                float r = radius * (1f + jitter * (Rand(ref seed) - 0.5f));
                float y = radius * jitter * 0.6f * (Rand(ref seed) - 0.5f);
                ring[i] = center + rot * new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r);
            }
            Vector3 up = center + rot * (Vector3.up * top), dn = center + rot * (Vector3.down * bottom);
            for (int i = 0; i < sides; i++)
            {
                Vector3 p0 = ring[i], p1 = ring[(i + 1) % sides];
                Tri(up, p0, p1, (up + p0 + p1) / 3f - center, col);
                Tri(dn, p0, p1, (dn + p0 + p1) / 3f - center, col);
            }
        }

        /// Flat annulus in XZ facing up (inner = 0 gives a disc).
        public void Ring(float inner, float outer, int seg, float y, Color col)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = 360f * i / seg * Mathf.Deg2Rad, a1 = 360f * (i + 1) / seg * Mathf.Deg2Rad;
                Vector3 d0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)), d1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
                Vector3 h = Vector3.up * y;
                if (inner <= 0f) Tri(h, h + d0 * outer, h + d1 * outer, Vector3.up, col);
                else Quad(h + d0 * inner, h + d0 * outer, h + d1 * outer, h + d1 * inner, Vector3.up, col);
            }
        }

        public Mesh ToMesh(string name, bool prep = true)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetColors(c);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return prep ? Desync.Art.MeshPrep.Process(m) : m;
        }

        public static float Rand(ref uint s)
        {
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            return (s & 0xFFFFFF) / 16777216f;
        }

        // ---------- ready-made shapes ----------

        /// Spotlight cone: apex at origin opening along +Z, slant length = range. Alpha fades apex -> rim.
        public static Mesh Cone(float angle, float range, int seg = 24)
        {
            var b = new MeshBuilder();
            float half = angle * 0.5f * Mathf.Deg2Rad;
            float z = range * Mathf.Cos(half), r = range * Mathf.Sin(half);
            Color apex = new Color(1f, 1f, 1f, 1f), rim = new Color(1f, 1f, 1f, 0.1f);
            for (int i = 0; i < seg; i++)
            {
                float a0 = 360f * i / seg * Mathf.Deg2Rad, a1 = 360f * (i + 1) / seg * Mathf.Deg2Rad;
                var p0 = new Vector3(Mathf.Cos(a0) * r, Mathf.Sin(a0) * r, z);
                var p1 = new Vector3(Mathf.Cos(a1) * r, Mathf.Sin(a1) * r, z);
                Vector3 mid = (p0 + p1) * 0.5f;
                b.Tri(Vector3.zero, p0, p1, new Vector3(mid.x, mid.y, 0f), apex, rim, rim);
            }
            return b.ToMesh("VisionCone", false);
        }

        /// Low-poly stylised head (~240 tris), ~1 m tall, facing +Z, base at y = 0.
        public static Mesh Head(Color col)
        {
            const int lon = 12, lat = 10;
            var p = new Vector3[(lat + 1) * lon];
            for (int j = 0; j <= lat; j++)
            for (int i = 0; i < lon; i++)
            {
                float th = Mathf.PI * j / lat, ph = 2f * Mathf.PI * i / lon;
                var d = new Vector3(Mathf.Sin(th) * Mathf.Sin(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Cos(ph));
                var q = new Vector3(d.x * 0.38f, d.y * 0.5f, d.z * 0.44f);
                if (d.y < 0f) { q.x *= 1f + 0.35f * d.y; q.z *= 1f + 0.15f * d.y; }        // narrower jaw
                if (d.z > 0.8f && Mathf.Abs(d.y) < 0.35f && Mathf.Abs(d.x) < 0.3f) q.z += 0.09f;   // nose
                if (d.z > 0.5f && d.y > 0.05f && d.y < 0.35f && Mathf.Abs(d.x) > 0.25f) q.z -= 0.05f; // eye sockets
                p[j * lon + i] = q + Vector3.up * 0.5f;
            }
            var b = new MeshBuilder();
            var centre = Vector3.up * 0.5f;
            for (int j = 0; j < lat; j++)
            for (int i = 0; i < lon; i++)
            {
                Vector3 a = p[j * lon + i], bb = p[j * lon + (i + 1) % lon], d = p[(j + 1) * lon + (i + 1) % lon], e = p[(j + 1) * lon + i];
                Vector3 o1 = (a + bb + d) / 3f - centre, o2 = (a + d + e) / 3f - centre;
                if (j > 0) b.Tri(a, bb, d, o1, col);
                if (j < lat - 1) b.Tri(a, d, e, o2, col);
            }
            return b.ToMesh("Head");
        }
    }
}
