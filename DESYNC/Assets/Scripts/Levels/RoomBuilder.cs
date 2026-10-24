using System;
using System.Collections;
using System.Collections.Generic;
using Desync.Enemies;
using Desync.Gen;
using Desync.Rules;
using TMPro;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Desync.Levels.Editor")]

namespace Desync.Levels
{
    /// Builds a generated BREACH room (Desync.Gen RoomSpec) at runtime, spread over frames.
    /// Room space is the spec's: origin = north-west corner, +x east, -z south, yaw 0 = north (+Z), tiles 0.5 m.
    /// Tiles: '#' and 'A' render walls (WallRender, 3.5 m; 'A' flickers, see AnomalyFlicker), 'X' solid in both layers
    /// (Floor layer, not walkable for the NavMesh), ' ' void (invisible Default-layer bounds), everything else floor
    /// (floor also runs under walls: SOURCE walks through them). Walls are greedy-merged face runs and collider boxes;
    /// walls, ceiling, floor, props and posters are one mesh each (mat.world / mat.floor / mat.poster).
    /// Entities go through EnemyFactory / PlayerFactory, using the generated-room overloads (their defaults are the spec
    /// stats: fixed camera 60 deg x 12 m, sweep camera 90 deg arc / 8 s / 10 m, drone 3 m/s x 10 m, glitch 1.0 s + 0.5 s
    /// telegraph); kinds without a factory are skipped with a warning. Entities stay inactive until the NavMesh is baked,
    /// then the room wakes in one frame and RoomInfo.Current points at it.
    public static class RoomBuilder
    {
        public const string BreachScene = "Breach";
        public const float Tile = 0.5f, WallHeight = 3.5f, GlitchRadius = 1.5f, CamHeight = 3.05f, CamPitch = 30f;
        /// Work per frame before yielding; one work item (a mesh, a factory call) can overrun it.
        public const float SliceMs = 4f;
        const float PropChance = 0.5f;

        const int Open = 0, RenderWall = 1, Solid = 2, Anomaly = 3, Void = 4;
        static readonly int[] DC = { 0, 1, 0, -1 }, DR = { -1, 0, 1, 0 };   // N E S W in tile steps

        /// Builds `spec` under `parent` over several frames and calls `done` with the ready room.
        /// Rooms that fell back to a classic room (tiles null) are not built here: load spec.fallbackRoom instead.
        /// Work starts next frame, so a previous room disposed in this frame is gone first (sensors share one TraceSystem per scene).
        public static void BuildAsync(RoomSpec spec, Transform parent, Action<RoomHandle> done)
        {
            if (spec?.tiles == null || spec.tiles.Length == 0)
                throw new ArgumentException("RoomSpec has no tiles; load its fallbackRoom (" + spec?.fallbackRoom + ") instead");
            var go = new GameObject("BreachRoom " + spec.seed);
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<RoomHandle>();
            h.Spec = spec;
            h.StartCoroutine(h.Run(Work(spec, h), done));
        }

        /// Editor tools (LevelSnapshots): the same build in one call; the NavMesh is not waited for.
        internal static RoomHandle BuildNow(RoomSpec spec, Transform parent)
        {
            var go = new GameObject("BreachRoom " + spec.seed);
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<RoomHandle>();
            h.Spec = spec;
            var work = Work(spec, h);
            while (work.MoveNext()) { }
            return h;
        }

        /// The build, one work item per step (RoomHandle.Run decides where frames end). Yields an AsyncOperation to wait on it.
        static IEnumerator Work(RoomSpec spec, RoomHandle h)
        {
            var t = spec.tiles;
            int W = t[0].Length, H = t.Length;
            var root = h.transform;

            h.Step = "asset snapshot";
            // Everything loaded so far stays: AssetLib pulls in every model.* prefab mesh, which entities share.
            AssetLib.Get<Material>("mat.world");
            h.AssetMeshes = new HashSet<Mesh>(Resources.FindObjectsOfTypeAll<Mesh>());
            yield return null;

            // ---- geometry
            h.Step = "walls";
            var geo = RoomGeo.Node("Geometry", GameLayers.Default, root);
            var walls = new MeshKit();
            var anomaly = new MeshKit();
            var runs = new List<Run>();
            WallFaces(t, walls, anomaly, runs);
            Emit(geo, "Structure", walls, "mat.world", GameLayers.WallRender);
            yield return null;

            if (!anomaly.Empty)
            {
                h.Step = "anomaly wall";
                var a = Emit(geo, "AnomalyWall", anomaly, "mat.world", GameLayers.WallRender);
                var flick = a.AddComponent<AnomalyFlicker>();
                flick.target = a.GetComponent<MeshRenderer>();
                flick.normal = flick.target.sharedMaterial;
                flick.glyph = AssetLib.Get<Material>("mat.glyphrain") ?? AssetLib.Get<Material>("mat.glitch");
                yield return null;
            }

            h.Step = "floor";
            var floor = new MeshKit();
            for (int cls = 0; cls < 2; cls++)   // 2 m checker like the classic rooms' cells
            {
                var col = cls == 0 ? RoomGeo.FloorA : RoomGeo.FloorB;
                foreach (var q in RoomGeo.Rects(W, H, (c, r) => Kind(t, c, r) != Void && (((c >> 2) + (r >> 2)) & 1) == cls))
                    RectQuad(floor, q, 0f, Vector3.up, col);
            }
            Emit(geo, "Floor", floor, "mat.floor", GameLayers.Floor);
            yield return null;

            h.Step = "ceiling";
            var ceiling = new MeshKit();
            foreach (var q in RoomGeo.Rects(W, H, (c, r) => Kind(t, c, r) != Void)) RectQuad(ceiling, q, WallHeight, Vector3.down, RoomGeo.NavyDark);
            Emit(geo, "Ceiling", ceiling, "mat.world", GameLayers.Default);
            yield return null;

            h.Step = "colliders";
            var floorCols = RoomGeo.Node("FloorColliders", GameLayers.Floor, geo);
            foreach (var q in RoomGeo.Rects(W, H, (c, r) => Kind(t, c, r) != Void)) RoomGeo.BoxCol(floorCols, Center(q, -0.25f), Size(q, 0.5f));
            var wallCols = RoomGeo.Node("Walls", GameLayers.WallRender, geo);
            foreach (var q in RoomGeo.Rects(W, H, (c, r) => { int k = Kind(t, c, r); return k == RenderWall || k == Anomaly; }))
                RoomGeo.BoxCol(wallCols, Center(q, WallHeight * 0.5f), Size(q, WallHeight));
            var solidCols = RoomGeo.Node("Solid", GameLayers.Floor, geo);   // Floor layer collides with the player in both layers
            var notWalkable = solidCols.gameObject.AddComponent<NavMeshModifier>();
            notWalkable.overrideArea = true;
            notWalkable.area = 1;   // built-in "Not Walkable"
            foreach (var q in RoomGeo.Rects(W, H, (c, r) => Kind(t, c, r) == Solid)) RoomGeo.BoxCol(solidCols, Center(q, WallHeight * 0.5f), Size(q, WallHeight));
            var bounds = RoomGeo.Node("Bounds", GameLayers.Default, geo);
            const float bh = 12f;
            foreach (var q in RoomGeo.Rects(W, H, (c, r) => Kind(t, c, r) == Void)) RoomGeo.BoxCol(bounds, Center(q, bh * 0.5f - 1f), Size(q, bh));
            float w = W * Tile, l = H * Tile;
            RoomGeo.BoxCol(bounds, new Vector3(w * 0.5f, bh * 0.5f - 1f, 1f), new Vector3(w + 4f, bh, 2f));
            RoomGeo.BoxCol(bounds, new Vector3(w * 0.5f, bh * 0.5f - 1f, -l - 1f), new Vector3(w + 4f, bh, 2f));
            RoomGeo.BoxCol(bounds, new Vector3(-1f, bh * 0.5f - 1f, -l * 0.5f), new Vector3(2f, bh, l + 4f));
            RoomGeo.BoxCol(bounds, new Vector3(w + 1f, bh * 0.5f - 1f, -l * 0.5f), new Vector3(2f, bh, l + 4f));
            yield return null;

            h.Step = "props";
            var props = new MeshKit();
            Props(spec, props, RoomGeo.Node("PropColliders", GameLayers.WallRender, geo));
            Emit(geo, "Props", props, "mat.world", GameLayers.WallRender);
            yield return null;

            h.Step = "posters";   // one per work item: TMP auto-sizing costs a few ms each
            var posters = new MeshKit();
            var posterRoot = RoomGeo.Node("Posters", GameLayers.Default, geo);
            var font = AssetLib.Get<TMP_FontAsset>("font.title");
            var spots = PosterSpots(spec, runs, out int slogan);
            for (int i = 0; i < spots.Count; i++)
            {
                Ends(spots[i], out var p0, out var p1, out var nrm);
                var ctr = (p0 + p1) * 0.5f + nrm * 0.03f + Vector3.up * 1.95f;
                RoomGeo.Poster(posters, posterRoot, ctr, nrm, font, (i & 1) == 0 ? GameConfig.I.magenta : GameConfig.I.cyan,
                               Posters.Default[(slogan + i) % Posters.Default.Length]);
                yield return null;
            }
            Emit(posterRoot, "PosterPanels", posters, "mat.poster", GameLayers.Default);
            yield return null;

            // ---- room info, player, entities (inactive until the NavMesh exists)
            var ents = new GameObject("Entities");
            ents.SetActive(false);
            ents.transform.SetParent(root, false);
            var infoGo = new GameObject("RoomInfo");
            infoGo.SetActive(false);
            infoGo.transform.SetParent(root, false);
            var info = infoGo.AddComponent<LevelRoomInfo>();
            info.index = -1;
            info.traceCountdown = -1f;
            h.Info = info;

            int terminals = 0, room = TerminalRoom(spec.act, spec.node);
            ulong warned = 0;
            GameObject lastDrone = null;
            foreach (var e in spec.entities)
            {
                var p = root.TransformPoint(new Vector3(e.x, 0f, e.z));
                float yaw = e.yaw + root.eulerAngles.y;
                h.Step = e.id;
                switch (e.kind)
                {
                    case EntityKind.Spawn:
                        var spawn = new GameObject("Spawn").transform;
                        spawn.SetParent(infoGo.transform, false);
                        spawn.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
                        info.spawn = spawn;
                        h.Player = Safe(e, () => Desync.Player.PlayerFactory.Create(ents.transform, p, yaw));
                        break;
                    case EntityKind.Exit: Safe(e, () => EnemyFactory.ExitPortal(ents.transform, p, yaw)); break;
                    case EntityKind.Terminal:
                        int story = terminals++ % 6;   // classic memo lines until story shards land
                        Safe(e, () => EnemyFactory.Terminal(ents.transform, p, yaw, room, story));
                        break;
                    case EntityKind.CameraFixed: Safe(e, () => EnemyFactory.FixedCamera(ents.transform, p + Vector3.up * CamHeight, yaw, CamPitch)); break;
                    case EntityKind.CameraSweep: Safe(e, () => EnemyFactory.SweepCamera(ents.transform, p + Vector3.up * CamHeight, yaw, CamPitch)); break;
                    case EntityKind.Drone:
                        if (e.path == null || e.path.Length < 4) { Debug.LogWarning($"[DESYNC] LEVELS: drone {e.id} has no patrol loop; skipped"); break; }
                        var wps = new Vector3[e.path.Length / 2];
                        for (int i = 0; i < wps.Length; i++) wps[i] = root.TransformPoint(new Vector3(e.path[2 * i], 0f, e.path[2 * i + 1]));
                        lastDrone = Safe(e, () => EnemyFactory.Drone(ents.transform, wps, 0f, speed: 3f));
                        break;
                    case EntityKind.IceVent: Safe(e, () => EnemyFactory.IceVent(ents.transform, p)); break;
                    case EntityKind.TrackerIce: Safe(e, () => EnemyFactory.TrackerIce(ents.transform, p)); break;
                    case EntityKind.Glitch: Safe(e, () => EnemyFactory.GlitchZone(ents.transform, p, GlitchRadius, delay: 1f)); break;
                    case EntityKind.HijackPad:
                        var drone = lastDrone;
                        Safe(e, () => EnemyFactory.HijackPad(ents.transform, p, yaw, drone, room));
                        break;
                    case EntityKind.AnomalyWall: break;   // geometry: the 'A' tiles' AnomalyFlicker
                    default:
                        ulong bit = 1UL << (int)e.kind;
                        if ((warned & bit) == 0) Debug.LogWarning($"[DESYNC] LEVELS: no factory for {e.kind} yet; {e.id} (and any more) skipped");
                        warned |= bit;
                        break;
                }
                yield return null;
            }
            info.terminalCount = terminals;
            if (!h.Player) Debug.LogError($"[DESYNC] LEVELS: room {spec.seed} has no player spawn");

            h.Step = "navmesh";
            // ---- NavMesh over the Floor layer (walls stay out: ICE crosses them in SOURCE; 'X' marked not walkable)
            var surface = root.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = 1 << GameLayers.Floor;
            surface.agentTypeID = 0;
            h.NavData = new NavMeshData(0) { name = "NavMesh", position = root.position, rotation = root.rotation };
            surface.navMeshData = h.NavData;
            surface.AddData();
            yield return surface.UpdateNavMesh(h.NavData);

            h.Step = "wake";
            ents.SetActive(true);
            infoGo.SetActive(true);   // RoomInfo.Current = this room
            yield return null;

            h.Step = "measure";
            Measure(root, out int tris, out int draws);
            h.Tris = tris;
            h.DrawCalls = draws;
        }

        // ------------------------------------------------------------------------------------------------ entities

        static GameObject Safe(EntitySpawn e, Func<GameObject> make)
        {
            try { return make(); }
            catch (Exception ex) { Debug.LogError($"[DESYNC] LEVELS: factory for {e.kind} {e.id} threw (owner: that factory's workstream): {ex}"); return null; }
        }

        /// Terminal difficulty (prompt length, SOURCE presentation) on the classic 0..5 scale: acts 1/2/3 -> 0-1 / 2-3 / 4-5.
        internal static int TerminalRoom(int act, int node) => Mathf.Clamp((act - 1) * 2 + (node >= 2 ? 1 : 0), 0, 5);

        // ------------------------------------------------------------------------------------------------ tiles

        static int Kind(string[] t, int c, int r)
        {
            if (r < 0 || r >= t.Length || c < 0 || c >= t[r].Length) return Void;
            switch (t[r][c])
            {
                case '#': return RenderWall;
                case 'X': return Solid;
                case 'A': return Anomaly;
                case ' ': return Void;
                default: return Open;
            }
        }

        static bool IsWall(int k) => k == RenderWall || k == Solid || k == Anomaly;

        static Vector3 Center(RectInt q, float y) => new Vector3((q.xMin + q.xMax) * 0.5f * Tile, y, -(q.yMin + q.yMax) * 0.5f * Tile);
        static Vector3 Size(RectInt q, float height) => new Vector3(q.width * Tile, height, q.height * Tile);

        static void RectQuad(MeshKit k, RectInt q, float y, Vector3 n, Color col)
        {
            float x0 = q.xMin * Tile, x1 = q.xMax * Tile, z0 = -q.yMin * Tile, z1 = -q.yMax * Tile;
            k.Quad(new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), n, col);
        }

        static GameObject Emit(Transform parent, string name, MeshKit kit, string matKey, int layer) =>
            kit.Empty ? null : RoomGeo.MeshObject(parent, name, kit.ToMesh(name), matKey, layer);

        /// A wall face run: tiles a..b (inclusive) of row/column `line` whose `d` side (N E S W) faces open floor.
        struct Run
        {
            public int d, line, a, b;
            public Run(int d, int line, int a, int b) { this.d = d; this.line = line; this.a = a; this.b = b; }
            public int Len => b - a + 1;
            public int Col(int i) => d % 2 == 0 ? i : line;
            public int Row(int i) => d % 2 == 0 ? line : i;
        }

        /// Face runs of every wall kind toward open floor, merged along rows (N/S faces) and columns (E/W faces).
        /// Render walls keep the classic bands (cyan foot, navy body, magenta band, cap); 'X' reads heavier with a green band
        /// (solid in SOURCE too); anomaly faces go to their own mesh. Runs >= 2 m with clear floor in front are poster spots.
        static void WallFaces(string[] t, MeshKit walls, MeshKit anomaly, List<Run> posterRuns)
        {
            int rows = t.Length, cols = t[0].Length;
            var cfg = GameConfig.I;
            for (int d = 0; d < 4; d++)
            {
                bool alongRow = d % 2 == 0;
                int lines = alongRow ? rows : cols, len = alongRow ? cols : rows;
                for (int line = 0; line < lines; line++)
                    for (int i = 0; i < len; i++)
                    {
                        var run = new Run(d, line, i, i);
                        int k = Kind(t, run.Col(i), run.Row(i));
                        if (!IsWall(k) || Kind(t, run.Col(i) + DC[d], run.Row(i) + DR[d]) != Open) continue;
                        while (i + 1 < len && Kind(t, run.Col(i + 1), run.Row(i + 1)) == k
                               && Kind(t, run.Col(i + 1) + DC[d], run.Row(i + 1) + DR[d]) == Open) i++;
                        run.b = i;
                        Ends(run, out var p0, out var p1, out var n);
                        float shade = d % 2 == 0 ? 1f : 0.8f;
                        const float H = WallHeight, body0 = 0.16f, body1 = H - 0.45f;
                        var kit = k == Anomaly ? anomaly : walls;
                        if (k == Solid)
                        {
                            kit.Face(p0, p1, 0f, body0, n, RoomGeo.Cap, RoomGeo.Cap);
                            kit.Face(p0, p1, body0, body1, n, RoomGeo.RackBody * shade, RoomGeo.NavyDark * shade);
                            kit.Face(p0, p1, body1, body1 + 0.08f, n, cfg.green * 0.7f, cfg.green * 0.7f);
                            kit.Face(p0, p1, body1 + 0.08f, H, n, RoomGeo.Cap, RoomGeo.Cap);
                        }
                        else
                        {
                            kit.Face(p0, p1, 0f, 0.08f, n, cfg.cyan, cfg.cyan);
                            kit.Face(p0, p1, 0.08f, body0, n, RoomGeo.NavyDark, RoomGeo.NavyDark);
                            kit.Face(p0, p1, body0, body1, n, RoomGeo.NavyDark * shade, RoomGeo.Navy * shade);
                            kit.Face(p0, p1, body1, body1 + 0.08f, n, cfg.magenta, cfg.magenta);
                            kit.Face(p0, p1, body1 + 0.08f, H, n, RoomGeo.Cap, RoomGeo.Cap);
                        }
                        if (k == Anomaly || run.Len < 4) continue;
                        bool clear = true;
                        for (int j = run.a; j <= run.b && clear; j++) clear = t[run.Row(j) + DR[d]][run.Col(j) + DC[d]] == '.';
                        if (clear) posterRuns.Add(run);
                    }
            }
        }

        /// The run's face segment on the ground (p0 -> p1) and its outward normal.
        static void Ends(Run u, out Vector3 p0, out Vector3 p1, out Vector3 n)
        {
            n = new Vector3(DC[u.d], 0f, -DR[u.d]);
            float a = u.a * Tile, b = (u.b + 1) * Tile;
            switch (u.d)
            {
                case 0: p0 = new Vector3(a, 0f, -u.line * Tile); p1 = new Vector3(b, 0f, p0.z); break;          // north face
                case 2: p0 = new Vector3(a, 0f, -(u.line + 1) * Tile); p1 = new Vector3(b, 0f, p0.z); break;    // south face
                case 1: p0 = new Vector3((u.line + 1) * Tile, 0f, -a); p1 = new Vector3(p0.x, 0f, -b); break;   // east face
                default: p0 = new Vector3(u.line * Tile, 0f, -a); p1 = new Vector3(p0.x, 0f, -b); break;        // west face
            }
        }

        static Vector3 TileCenter(int c, int r) => new Vector3((c + 0.5f) * Tile, 0f, -(r + 0.5f) * Tile);

        // ------------------------------------------------------------------------------------------------ dressing

        /// Sparingly: at most one prop per chunk cell (p = PropChance), only inside a square of plain floor that leaves
        /// >= 1.1 m around it (3.5 m for a plant / cabinet / rack, 4.5 m for a desk set), so it never closes a route;
        /// 2.5 m from every entity and 1.5 m from every patrol leg. Seeded from the room seed.
        static void Props(RoomSpec spec, MeshKit k, Transform cols)
        {
            var t = spec.tiles;
            var rng = Rng.Substream(spec.seed, "props");
            var cand = new List<int>();
            for (int cr = 0; cr < spec.gridH; cr++)
            for (int cc = 0; cc < spec.gridW; cc++)
            {
                if (rng.NextDouble() >= PropChance) continue;
                cand.Clear();
                for (int r = cr * 16 + 3; r < cr * 16 + 13; r++)
                for (int c = cc * 16 + 3; c < cc * 16 + 13; c++)
                    if (PlainSquare(t, c, r, 3) && FarFromEntities(spec, TileCenter(c, r))) cand.Add(r * 1024 + c);
                if (cand.Count == 0) continue;
                int pick = cand[rng.Range(0, cand.Count)];
                var p = TileCenter(pick % 1024, pick / 1024);
                bool roomy = PlainSquare(t, pick % 1024, pick / 1024, 4);
                float yaw = rng.Range(0, 4) * 90f;
                var away = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                var b = new Bounds(p, Vector3.zero);
                switch (rng.Range(roomy ? 0 : 1, 4))
                {
                    case 0:
                        var desk = RoomGeo.Place(k, "model.desk", p - away * 0.15f, yaw, ref b);
                        RoomGeo.Place(k, "model.monitor", new Vector3(p.x, desk.max.y, p.z) - away * 0.4f, yaw, ref b);
                        RoomGeo.Place(k, "model.chair", p + away * 0.75f, yaw + 180f, ref b);
                        break;
                    case 1: RoomGeo.Place(k, "model.plant", p, yaw, ref b); break;
                    case 2: RoomGeo.Place(k, "model.cabinet", p, yaw, ref b); break;
                    default: RoomGeo.Place(k, "model.serverRack", p, yaw, ref b); break;
                }
                if (b.size.sqrMagnitude > 0.01f) RoomGeo.BoxCol(cols, b.center, b.size);
            }
        }

        static bool PlainSquare(string[] t, int c, int r, int rad)
        {
            for (int y = r - rad; y <= r + rad; y++)
            for (int x = c - rad; x <= c + rad; x++)
                if (y < 0 || y >= t.Length || x < 0 || x >= t[y].Length || t[y][x] != '.') return false;
            return true;
        }

        static bool FarFromEntities(RoomSpec spec, Vector3 p)
        {
            var q = new Vector2(p.x, p.z);
            foreach (var e in spec.entities)
            {
                if ((new Vector2(e.x, e.z) - q).sqrMagnitude < 2.5f * 2.5f) return false;
                if (e.path == null) continue;
                int n = e.path.Length / 2;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    var a = new Vector2(e.path[2 * i], e.path[2 * i + 1]);
                    var ab = new Vector2(e.path[2 * j], e.path[2 * j + 1]) - a;
                    float s = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
                    if ((a + ab * s - q).sqrMagnitude < 1.5f * 1.5f) return false;
                }
            }
            return true;
        }

        /// Poster spots: wall runs >= 2 m facing plain floor, at most one per chunk cell, max(2, cells / 3) per room;
        /// `slogan` = first slogan index. Seeded from the room seed.
        static List<Run> PosterSpots(RoomSpec spec, List<Run> runs, out int slogan)
        {
            var rng = Rng.Substream(spec.seed, "posters");
            slogan = rng.Range(0, Posters.Default.Length);
            for (int i = runs.Count - 1; i > 0; i--) { int j = rng.Range(0, i + 1); (runs[i], runs[j]) = (runs[j], runs[i]); }
            var spots = new List<Run>();
            var cells = new HashSet<int>();
            int max = Math.Max(2, spec.gridW * spec.gridH / 3);
            foreach (var u in runs)
            {
                if (spots.Count >= max) break;
                int mid = (u.a + u.b) / 2;
                if (cells.Add(u.Row(mid) / 16 * 64 + u.Col(mid) / 16)) spots.Add(u);
            }
            return spots;
        }

        /// Triangles of every enabled renderer's mesh, and draw calls as an upper bound: one per material slot of every
        /// enabled renderer (no credit taken for batching).
        static void Measure(Transform root, out int tris, out int draws)
        {
            long n = 0;
            draws = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                draws += Math.Max(1, r.sharedMaterials.Length);
                Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r is MeshRenderer ? r.GetComponent<MeshFilter>()?.sharedMesh : null;
                if (m) for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i) / 3;
            }
            tris = (int)n;
        }
    }
}
