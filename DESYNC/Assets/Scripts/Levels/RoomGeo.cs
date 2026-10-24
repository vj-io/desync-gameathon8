using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Desync.Levels
{
    /// Turns one RoomDef grid into scene geometry: walls / racks / railings (mat.world, WallRender boxes), floor / upper
    /// floor / ramps (mat.floor, Floor boxes), furniture combined into one mesh (mat.world, WallRender boxes), posters
    /// (mat.poster + TMP) and invisible Default-layer bounds around void cells. Every mesh goes through MeshPrep.
    public sealed class RoomGeo
    {
        public readonly RoomDef d;
        public int TriCount;
        readonly float cs = RoomDef.Cell, H, uh;
        readonly Vector3 origin;
        static readonly int[] DC = { 0, 1, 0, -1 }, DR = { -1, 0, 1, 0 };   // N E S W in grid steps

        // palette (sRGB, like GameConfig's colours); shared with RoomBuilder
        internal static readonly Color NavyDark = new Color(0.03f, 0.035f, 0.08f), Navy = new Color(0.085f, 0.1f, 0.21f),
            Cap = new Color(0.07f, 0.075f, 0.11f), FloorA = new Color(0.05f, 0.06f, 0.12f), FloorB = new Color(0.042f, 0.05f, 0.1f),
            RackBody = new Color(0.035f, 0.04f, 0.065f), Pilaster = new Color(0f, 0.42f, 0.52f);
        readonly Color cyan, magenta, green;

        public RoomGeo(RoomDef def)
        {
            d = def; H = d.wallHeight; uh = d.upperHeight;
            origin = new Vector3(-(d.Cols - 1) * cs * 0.5f, 0f, (d.Rows - 1) * cs * 0.5f);
            var cfg = GameConfig.I; cyan = cfg.cyan; magenta = cfg.magenta; green = cfg.green;
        }

        // ------------------------------------------------------------------------------------------------ queries

        public Vector3 Center(int c, int r) => origin + new Vector3(c * cs, 0f, -r * cs);
        static Vector3 Dir(int k) => new Vector3(DC[k], 0f, -DR[k]);
        char Ch(int c, int r) => d.At(c, r);
        bool Wall(int c, int r) => Ch(c, r) == Legend.Wall;
        bool Void(int c, int r) => Legend.IsVoid(Ch(c, r));
        bool Open(int c, int r) => !Wall(c, r) && !Void(c, r);
        bool Raised(int c, int r) => !Wall(c, r) && !Void(c, r) && d.LevelAt(c, r) == Legend.Upper;
        int Ramp(int c, int r) => Wall(c, r) || Void(c, r) ? 0 : Legend.RampDir(d.LevelAt(c, r));

        /// Floor height at a cell centre (ground 0, upper floor uh, ramps interpolated).
        public float LevelY(int c, int r)
        {
            if (Raised(c, r)) return uh;
            int dir = Ramp(c, r);
            if (dir == 0) return 0f;
            RampRun(c, r, dir, out int i, out int len);
            return uh * (i + 0.5f) / len;
        }

        /// Position of ramp cell (c,r) along its run: i = steps from the low end, len = run length.
        void RampRun(int c, int r, int dir, out int i, out int len)
        {
            int lo = c; while (Ramp(lo - dir, r) == dir) lo -= dir;
            int hi = c; while (Ramp(hi + dir, r) == dir) hi += dir;
            len = Math.Abs(hi - lo) + 1; i = Math.Abs(c - lo);
        }

        public bool Find(char ch, out int c, out int r) => d.Find(ch, out c, out r);

        public IEnumerable<(int c, int r)> Cells(char ch)
        {
            for (int r = 0; r < d.Rows; r++) for (int c = 0; c < d.Cols; c++) if (Ch(c, r) == ch) yield return (c, r);
        }

        /// First adjacent wall (N, E, S, W); `away` points from it into the room. False when free-standing.
        bool WallSide(int c, int r, out Vector3 away)
        {
            for (int k = 0; k < 4; k++) if (Wall(c + DC[k], r + DR[k])) { away = -Dir(k); return true; }
            away = Vector3.back; return false;
        }

        /// Facing for wall-backed things (away from the wall), else toward `toward`. Returns true when backed.
        public bool Facing(int c, int r, Vector3 toward, out float yaw, out Vector3 away)
        {
            bool backed = WallSide(c, r, out away);
            if (!backed)
            {
                var to = toward - Center(c, r); to.y = 0f;
                away = to.sqrMagnitude > 0.01f ? to.normalized : Vector3.back;
            }
            yaw = Yaw(away);
            return backed;
        }

        /// Point on the face of the adjacent wall at `height` above the cell floor, facing into the room.
        public Vector3 WallMount(int c, int r, float height, out float yaw)
        {
            WallSide(c, r, out var away);
            yaw = Yaw(away);
            return Center(c, r) - away * (cs * 0.5f - 0.06f) + Vector3.up * (LevelY(c, r) + height);
        }

        static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
        static int Hash(int c, int r) => (c * 73856093) ^ (r * 19349663);

        // ------------------------------------------------------------------------------------------------ build

        public void Build(string scenePath, List<Mesh> assets)
        {
            var structure = new MeshKit(); var floor = new MeshKit(); var props = new MeshKit(); var posters = new MeshKit();
            var wallCols = Node("Walls", GameLayers.WallRender);
            var floorCols = Node("FloorColliders", GameLayers.Floor);
            var propCols = Node("PropColliders", GameLayers.WallRender);
            var bounds = Node("Bounds", GameLayers.Default);

            Walls(structure, wallCols);
            Floors(floor, structure, floorCols);
            Railings(structure, wallCols);
            Racks(structure, propCols);
            Furniture(props, propCols);
            var posterRoot = PosterWall(posters);
            VoidBounds(bounds);

            Emit("Structure", structure, "mat.world", GameLayers.WallRender, assets);
            Emit("Floor", floor, "mat.floor", GameLayers.Floor, assets);
            Emit("Props", props, "mat.world", GameLayers.WallRender, assets);
            var pm = Emit("PosterPanels", posters, "mat.poster", GameLayers.Default, assets);
            if (pm) pm.transform.SetParent(posterRoot, true);
        }

        GameObject Emit(string name, MeshKit kit, string matKey, int layer, List<Mesh> assets)
        {
            if (kit.Empty) return null;
            var mesh = kit.ToMesh(name);
            assets.Add(mesh);
            for (int s = 0; s < mesh.subMeshCount; s++) TriCount += (int)(mesh.GetIndexCount(s) / 3);
            return MeshObject(null, name, mesh, matKey, layer);
        }

        /// Static, shadowless renderer for one prepared mesh (material from AssetLib, mat.world fallback).
        internal static GameObject MeshObject(Transform parent, string name, Mesh mesh, string matKey, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            if (parent) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = AssetLib.Get<Material>(matKey) ?? AssetLib.Get<Material>("mat.world");
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            MarkStatic(go);
            return go;
        }

        internal static Transform Node(string name, int layer, Transform parent = null)
        {
            var go = new GameObject(name) { layer = layer };
            if (parent) go.transform.SetParent(parent, false);
            MarkStatic(go);
            return go.transform;
        }

        /// Editor: static-batching flag (scenes built by LevelBuilder). Players get no flag: runtime rooms are already one mesh per layer.
        internal static void MarkStatic(GameObject go)
        {
#if UNITY_EDITOR
            UnityEditor.GameObjectUtility.SetStaticEditorFlags(go, UnityEditor.StaticEditorFlags.BatchingStatic);
#endif
        }

        internal static void BoxCol(Transform t, Vector3 center, Vector3 size)
        {
            var b = t.gameObject.AddComponent<BoxCollider>();
            b.center = center; b.size = size;
        }

        List<RectInt> Rects(Func<int, int, bool> pred) => Rects(d.Cols, d.Rows, pred);

        /// Greedy rectangles (x = column, y = row) over the cells of a cols x rows grid matching `pred`.
        internal static List<RectInt> Rects(int cols, int rows, Func<int, int, bool> pred)
        {
            var used = new bool[cols, rows];
            var list = new List<RectInt>();
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (used[c, r] || !pred(c, r)) continue;
                int w = 1; while (c + w < cols && !used[c + w, r] && pred(c + w, r)) w++;
                int h = 1;
                for (bool grow = true; grow && r + h < rows; )
                {
                    for (int x = c; x < c + w; x++) if (used[x, r + h] || !pred(x, r + h)) { grow = false; break; }
                    if (grow) h++;
                }
                for (int y = r; y < r + h; y++) for (int x = c; x < c + w; x++) used[x, y] = true;
                list.Add(new RectInt(c, r, w, h));
            }
            return list;
        }

        Vector3 RectCenter(RectInt q) => (Center(q.xMin, q.yMin) + Center(q.xMax - 1, q.yMax - 1)) * 0.5f;

        // ------------------------------------------------------------------------------------------------ walls

        void Walls(MeshKit k, Transform cols)
        {
            float body0 = 0.16f, body1 = H - 0.45f;
            float stripe = H > 4f ? (d.levels != null ? uh : 3.4f) : -1f;
            for (int r = 0; r < d.Rows; r++)
            for (int c = 0; c < d.Cols; c++)
            {
                if (!Wall(c, r)) continue;
                var ctr = Center(c, r);
                for (int dir = 0; dir < 4; dir++)
                {
                    int nc = c + DC[dir], nr = r + DR[dir];
                    if (!Open(nc, nr)) continue;
                    var n = Dir(dir);
                    var t = Vector3.Cross(Vector3.up, n) * (cs * 0.5f);
                    var fc = ctr + n * (cs * 0.5f);
                    Vector3 p0 = fc - t, p1 = fc + t;
                    float shade = dir % 2 == 0 ? 1f : 0.8f;
                    k.Face(p0, p1, 0f, 0.08f, n, cyan, cyan);
                    k.Face(p0, p1, 0.08f, body0, n, NavyDark, NavyDark);
                    if (stripe > 0f)
                    {
                        k.Face(p0, p1, body0, stripe, n, NavyDark * shade, Color.Lerp(NavyDark, Navy, 0.5f) * shade);
                        k.Face(p0, p1, stripe, stripe + 0.07f, n, cyan, cyan);
                        k.Face(p0, p1, stripe + 0.07f, body1, n, Color.Lerp(NavyDark, Navy, 0.5f) * shade, Navy * shade);
                    }
                    else k.Face(p0, p1, body0, body1, n, NavyDark * shade, Navy * shade);
                    k.Face(p0, p1, body1, body1 + 0.08f, n, magenta, magenta);
                    k.Face(p0, p1, body1 + 0.08f, H, n, Cap, Cap);

                    // pilaster trim on every other cell boundary inside a straight run
                    int ac = c + DC[(dir + 1) % 4], ar = r + DR[(dir + 1) % 4];
                    bool runContinues = Wall(ac, ar) && Open(ac + DC[dir], ar + DR[dir]);
                    if (runContinues && ((dir % 2 == 0 ? c : r) % 2 == 0))
                    {
                        var e = fc + t + n * 0.015f; var w = Vector3.Cross(Vector3.up, n) * 0.05f;
                        k.Face(e - w, e + w, body0, body1, n, Pilaster, Pilaster);
                    }
                }
                var top = Vector3.up * H; var hx = new Vector3(cs * 0.5f, 0f, 0f); var hz = new Vector3(0f, 0f, cs * 0.5f);
                k.Quad(ctr - hx + hz + top, ctr + hx + hz + top, ctr + hx - hz + top, ctr - hx - hz + top, Vector3.up, Cap * 0.8f);
            }
            foreach (var q in Rects(Wall))
                BoxCol(cols, RectCenter(q) + Vector3.up * (H * 0.5f), new Vector3(q.width * cs, H, q.height * cs));
        }

        // ------------------------------------------------------------------------------------------------ floors

        void Floors(MeshKit k, MeshKit sides, Transform cols)
        {
            var hx = new Vector3(cs * 0.5f, 0f, 0f); var hz = new Vector3(0f, 0f, cs * 0.5f);
            for (int r = 0; r < d.Rows; r++)
            for (int c = 0; c < d.Cols; c++)
            {
                if (Void(c, r)) continue;
                var p = Center(c, r);
                var col = ((c + r) & 1) == 0 ? FloorA : FloorB;
                k.Quad(p - hx + hz, p + hx + hz, p + hx - hz, p - hx - hz, Vector3.up, col);

                if (Raised(c, r))
                {
                    var top = Vector3.up * uh; var bot = Vector3.up * (uh - 0.25f);
                    k.Quad(p - hx + hz + top, p + hx + hz + top, p + hx - hz + top, p - hx - hz + top, Vector3.up, col * 1.25f);
                    k.Quad(p - hx + hz + bot, p + hx + hz + bot, p + hx - hz + bot, p - hx - hz + bot, Vector3.down, NavyDark);
                    for (int dir = 0; dir < 4; dir++)
                    {
                        int nc = c + DC[dir], nr = r + DR[dir];
                        if (Raised(nc, nr) || Wall(nc, nr) || Void(nc, nr) || RampMeets(nc, nr, dir)) continue;
                        var n = Dir(dir); var t = Vector3.Cross(Vector3.up, n) * (cs * 0.5f); var fc = p + n * (cs * 0.5f);
                        k.Face(fc - t, fc + t, uh - 0.25f, uh, n, cyan, cyan);
                    }
                }
                int rd = Ramp(c, r);
                if (rd != 0) RampCell(k, sides, cols, c, r, rd);
            }
            foreach (var q in Rects((c, r) => !Void(c, r)))
                BoxCol(cols, RectCenter(q) + Vector3.down * 0.25f, new Vector3(q.width * cs, 0.5f, q.height * cs));
            foreach (var q in Rects(Raised))
                BoxCol(cols, RectCenter(q) + Vector3.up * (uh - 0.125f), new Vector3(q.width * cs, 0.25f, q.height * cs));
        }

        /// True when ramp cell (c,r) rises toward the raised cell it was reached from via `dir`, i.e. connects to it.
        bool RampMeets(int c, int r, int dir)
        {
            int rd = Ramp(c, r);
            return rd != 0 && DR[dir] == 0 && DC[dir] == -rd;
        }

        /// Slope in the floor mesh; wedge sides in `sides` (mat.world: mat.floor's world grid would light a face lying on a grid line).
        void RampCell(MeshKit k, MeshKit sides, Transform cols, int c, int r, int dir)
        {
            RampRun(c, r, dir, out int i, out int len);
            float h0 = uh * i / len, h1 = uh * (i + 1) / len;
            var p = Center(c, r);
            var ax = new Vector3(dir * cs * 0.5f, 0f, 0f);   // toward the high end
            var hz = new Vector3(0f, 0f, cs * 0.5f);
            Vector3 lo = p - ax, hi = p + ax;
            var n = new Vector3(-dir * uh / (len * cs), 1f, 0f).normalized;
            k.Quad(lo + hz + Vector3.up * h0, hi + hz + Vector3.up * h1, hi - hz + Vector3.up * h1, lo - hz + Vector3.up * h0, n, FloorA * 1.25f);
            for (int side = 0; side < 4; side += 2)   // north/south sides of the wedge
            {
                if (Ramp(c + DC[side], r + DR[side]) == dir) continue;
                var s = Dir(side) * (cs * 0.5f);
                sides.Quad(lo + s, lo + s + Vector3.up * h0, hi + s + Vector3.up * h1, hi + s, Dir(side), NavyDark, Navy, Navy, NavyDark);
            }
            if (Ramp(c + dir, r) != dir)
                sides.Quad(hi + hz, hi + hz + Vector3.up * h1, hi - hz + Vector3.up * h1, hi - hz, new Vector3(dir, 0, 0), NavyDark);

            // walkable slope: one rotated slab per run (built from its low cell); blockers keep players out from under it
            if (i == 0)
            {
                float run = len * cs, ang = Mathf.Atan2(uh, run) * Mathf.Rad2Deg;
                var slope = new GameObject("RampSlope") { layer = GameLayers.Floor };
                slope.transform.SetParent(cols, false);
                slope.transform.SetPositionAndRotation(lo + new Vector3(dir * run * 0.5f, uh * 0.5f, 0f), Quaternion.Euler(0f, 0f, dir * ang));
                BoxCol(slope.transform, new Vector3(0f, -0.15f, 0f), new Vector3(Mathf.Sqrt(run * run + uh * uh), 0.3f, cs));
                MarkStatic(slope);
            }
            if (h0 > 0.1f) BoxCol(cols, p + Vector3.up * ((h0 - 0.05f) * 0.5f), new Vector3(cs, h0 - 0.05f, cs));
        }

        // ------------------------------------------------------------------------------------------------ railings

        void Railings(MeshKit k, Transform cols)
        {
            if (d.levels == null) return;
            const float th = 0.08f, ht = 1.05f;
            for (int r = 0; r < d.Rows; r++)
            for (int c = 0; c < d.Cols; c++)
            {
                if (!Raised(c, r)) continue;
                for (int dir = 0; dir < 4; dir++)
                {
                    int nc = c + DC[dir], nr = r + DR[dir];
                    if (Raised(nc, nr) || Wall(nc, nr) || Void(nc, nr) || RampMeets(nc, nr, dir)) continue;
                    var n = Dir(dir);
                    var mid = Center(c, r) + n * (cs * 0.5f - 0.06f) + Vector3.up * uh;
                    var along = Vector3.Cross(Vector3.up, n);
                    var ext = along * (cs * 0.5f) + n * (th * 0.5f);
                    var min = Vector3.Min(mid - ext, mid + ext); var max = Vector3.Max(mid - ext, mid + ext);
                    min.y = uh; max.y = uh + ht;
                    k.Box(min, new Vector3(max.x, uh + ht - 0.1f, max.z), Navy * 1.4f, magenta);
                    k.Box(new Vector3(min.x, uh + ht - 0.1f, min.z), max, magenta, magenta);
                    BoxCol(cols, (min + max) * 0.5f, max - min);
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ racks

        void Racks(MeshKit k, Transform cols)
        {
            const float inset = 0.45f, ht = 2.4f;
            Color[] leds = { cyan, green * 0.9f, magenta, cyan * 0.8f };
            for (int r = 0; r < d.Rows; r++)
            for (int c = 0; c < d.Cols; c++)
            {
                if (Ch(c, r) != Legend.Rack) continue;
                var p = Center(c, r) + Vector3.up * LevelY(c, r);
                bool n = Ch(c, r - 1) == Legend.Rack, e = Ch(c + 1, r) == Legend.Rack, s = Ch(c, r + 1) == Legend.Rack, w = Ch(c - 1, r) == Legend.Rack;
                float h = cs * 0.5f;
                var min = p + new Vector3(w ? -h : -h + inset, 0f, s ? -h : -h + inset);
                var max = p + new Vector3(e ? h : h - inset, ht, n ? h : h - inset);
                k.Box(min, max, RackBody, Cap, new[] { n, e, s, w });
                // LED strips on the exposed long faces
                bool[] open = { !n, !e, !s, !w };
                for (int dir = 0; dir < 4; dir++)
                {
                    if (!open[dir]) continue;
                    var nn = Dir(dir);
                    var along = Vector3.Cross(Vector3.up, nn);
                    float half = Mathf.Abs(Vector3.Dot(max - min, along)) * 0.5f - 0.12f;
                    if (half < 0.15f) continue;
                    var fc = (min + max) * 0.5f; fc.y = p.y;
                    fc += nn * (Mathf.Abs(Vector3.Dot(max - min, nn)) * 0.5f + 0.012f);
                    for (int i = 0; i < 7; i++)
                    {
                        float y = 0.35f + i * 0.28f;
                        var col = leds[(Hash(c, r) + i + dir) & 3];
                        float len = half * (0.55f + 0.45f * (((Hash(c, r) >> i) & 3) / 3f));
                        k.Face(fc - along * half, fc - along * (half - len * 2f), y, y + 0.045f, nn, col, col);
                    }
                }
                BoxCol(cols, (min + max) * 0.5f, max - min);
            }
        }

        // ------------------------------------------------------------------------------------------------ furniture

        static Color Grade(Color c)
        {
            float l = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            float sat = mx > 0.001f ? (mx - mn) / mx : 0f;
            var baseCol = Color.Lerp(new Color(0.045f, 0.05f, 0.1f), new Color(0.2f, 0.22f, 0.33f), Mathf.Pow(l, 1.2f));
            var o = Color.Lerp(baseCol, c * 0.45f, sat > 0.35f ? 0.35f : 0.1f);
            o.a = 1f;
            return o;
        }

        void Furniture(MeshKit k, Transform cols)
        {
            for (int r = 0; r < d.Rows; r++)
            for (int c = 0; c < d.Cols; c++)
            {
                char ch = Ch(c, r);
                if (ch != Legend.Desk && ch != Legend.Plant && ch != Legend.Cabinet) continue;
                var p = Center(c, r) + Vector3.up * LevelY(c, r);
                bool backed = WallSide(c, r, out var away);
                var b = new Bounds(p, Vector3.zero);
                if (ch == Legend.Desk)
                {
                    if (!backed) away = (r & 1) == 0 ? Vector3.back : Vector3.forward;
                    float yaw = Yaw(away);
                    var deskB = Place(k, "model.desk", p - away * 0.15f, yaw, ref b);
                    float top = deskB.max.y;
                    Place(k, "model.monitor", new Vector3(p.x, top, p.z) - away * 0.4f, yaw, ref b);
                    Place(k, "model.chair", p + away * 0.75f, yaw + 180f + ((Hash(c, r) & 7) - 3.5f) * 4f, ref b);
                }
                else if (ch == Legend.Plant)
                    Place(k, "model.plant", p + (backed ? -away * 0.45f : Vector3.zero), (Hash(c, r) & 255) * 1.4f, ref b);
                else
                    Place(k, "model.cabinet", p - away * (backed ? 0.55f : 0f), Yaw(away), ref b);
                if (b.size.sqrMagnitude > 0.01f) BoxCol(cols, b.center, b.size);
            }
        }

        /// Editor hook: base colours of a model's source submeshes when its import has no vertex colours (LevelBuilder sets it).
        public static Func<string, Mesh, Color[]> SourceColors;

        /// Appends the model prefab's (AssetLib model.*) meshes at pos/yaw; returns the placed bounds and grows `all`.
        internal static Bounds Place(MeshKit k, string key, Vector3 pos, float yaw, ref Bounds all)
        {
            var prefab = AssetLib.Get<GameObject>(key);
            var placed = new Bounds(pos, Vector3.zero);
            if (!prefab) return placed;
            var root = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), Vector3.one) * prefab.transform.worldToLocalMatrix;
            bool first = true;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (!mesh) continue;
                var m = root * mf.transform.localToWorldMatrix;
                k.Append(mesh, m, mesh.colors.Length == mesh.vertexCount ? null : SourceColors?.Invoke(key, mesh), Grade);
                var mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, ((i & 4) >> 1) - 1)));
                    if (first) { placed = new Bounds(corner, Vector3.zero); first = false; } else placed.Encapsulate(corner);
                }
            }
            if (all.size == Vector3.zero) all = placed; else all.Encapsulate(placed);
            return placed;
        }

        // ------------------------------------------------------------------------------------------------ posters

        Transform PosterWall(MeshKit k)
        {
            var root = new GameObject("Posters").transform;
            var slogans = d.posters ?? Posters.Default;
            var font = AssetLib.Get<TMP_FontAsset>("font.title");
            int n = 0;
            for (int r = 0; r < d.Rows; r++)
            for (int c = 0; c < d.Cols; c++)
            {
                if (Ch(c, r) != Legend.Poster || !WallSide(c, r, out var away)) continue;
                var ctr = Center(c, r) - away * (cs * 0.5f - 0.03f) + Vector3.up * (LevelY(c, r) + 1.95f);
                Poster(k, root, ctr, away, font, (n & 1) == 0 ? magenta : cyan, slogans[n % slogans.Length]);
                n++;
            }
            return root;
        }

        /// The session's first TextMeshPro costs 30-60 ms (TMP settings, font tables, mesh buffers); the builder logs showed it
        /// as the first room's poster-stage hitch. Paid once here at boot instead: one hidden poster with every slogan.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void WarmPosterText()
        {
            var font = AssetLib.Get<TMP_FontAsset>("font.title");
            if (!font) return;
            var go = new GameObject("PosterTextWarmup");
            Poster(new MeshKit(), go.transform, Vector3.down * 100f, Vector3.forward, font, Color.clear, string.Join("\n", Posters.Default));
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.enabled = false;
            UnityEngine.Object.Destroy(go);
        }

        /// One HALCYON poster panel (mat.poster quad into `k`) centred on `ctr` on a wall face looking along `away`,
        /// with its slogan as TMP text under `root` (skipped without a font).
        internal static void Poster(MeshKit k, Transform root, Vector3 ctr, Vector3 away, TMP_FontAsset font, Color color, string slogan)
        {
            const float w = 1.2f, h = 1.8f;
            var right = Vector3.Cross(Vector3.up, -away).normalized * (w * 0.5f);   // viewer's right, facing the wall
            var up = Vector3.up * (h * 0.5f);
            k.Quad(ctr - right - up, ctr - right + up, ctr + right + up, ctr + right - up, away, Color.white, Color.white, Color.white, Color.white,
                   new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
            if (!font) return;
            var go = new GameObject("PosterText");
            go.transform.SetParent(root, false);
            // text slot = the empty panel in the lower third of tex_halcyon (v 0.06..0.25); TMP reads along +Z
            go.transform.SetPositionAndRotation(ctr + away * 0.012f - Vector3.up * (h * 0.342f), Quaternion.LookRotation(-away));
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = font;
            tmp.rectTransform.sizeDelta = new Vector2(w * 0.78f, h * 0.16f);
            tmp.fontSize = 1.2f;
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.2f; tmp.fontSizeMax = 1.6f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.color = color;
            tmp.text = slogan;
            var tr = tmp.GetComponent<MeshRenderer>();
            tr.shadowCastingMode = ShadowCastingMode.Off; tr.receiveShadows = false;
            tmp.ForceMeshUpdate();
        }

        /// Room 1 floor decal: a quad with mat.decal (ART bakes the text into its texture); TMP text if the material is missing.
        public void Decal(Vector3 pos, float yaw, string text, List<Mesh> assets)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var mat = AssetLib.Get<Material>("mat.decal");
            if (mat)
            {
                var kit = new MeshKit();
                const float w = 4.6f, h = w / 6.4f;
                var right = rot * Vector3.right * (w * 0.5f); var fwd = rot * Vector3.forward * (h * 0.5f);
                var p = pos + Vector3.up * 0.02f;
                kit.Quad(p - right - fwd, p - right + fwd, p + right + fwd, p + right - fwd, Vector3.up, Color.white, Color.white, Color.white, Color.white,
                         new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
                var mesh = kit.ToMesh("Decal");
                assets.Add(mesh);
                var go = new GameObject("FloorDecal");
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                return;
            }
            var font = AssetLib.Get<TMP_FontAsset>("font.title");
            if (!font) { Debug.LogWarning("[DESYNC] LEVELS: no mat.decal and no font.title; floor decal skipped"); return; }
            var t = new GameObject("FloorDecal");
            t.SetActive(false);
            t.transform.SetPositionAndRotation(pos + Vector3.up * 0.02f, rot * Quaternion.Euler(90f, 0f, 0f));
            var tmp = t.AddComponent<TextMeshPro>();
            tmp.font = font;
            tmp.rectTransform.sizeDelta = new Vector2(5f, 1f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.1f; tmp.fontSizeMax = 4f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = cyan;
            tmp.text = text;
            t.SetActive(true);
        }

        // ------------------------------------------------------------------------------------------------ bounds

        /// Invisible Default-layer walls over void cells and around the grid so SOURCE walks cannot leave the room.
        void VoidBounds(Transform t)
        {
            const float ht = 12f;
            foreach (var q in Rects(Void))
                BoxCol(t, RectCenter(q) + Vector3.up * (ht * 0.5f - 1f), new Vector3(q.width * cs, ht, q.height * cs));
            float w = d.Cols * cs, l = d.Rows * cs;
            var mid = (Center(0, 0) + Center(d.Cols - 1, d.Rows - 1)) * 0.5f + Vector3.up * (ht * 0.5f - 1f);
            BoxCol(t, mid + new Vector3(0f, 0f, l * 0.5f + 1f), new Vector3(w + 4f, ht, 2f));
            BoxCol(t, mid - new Vector3(0f, 0f, l * 0.5f + 1f), new Vector3(w + 4f, ht, 2f));
            BoxCol(t, mid + new Vector3(w * 0.5f + 1f, 0f, 0f), new Vector3(2f, ht, l + 4f));
            BoxCol(t, mid - new Vector3(w * 0.5f + 1f, 0f, 0f), new Vector3(2f, ht, l + 4f));
        }
    }
}
