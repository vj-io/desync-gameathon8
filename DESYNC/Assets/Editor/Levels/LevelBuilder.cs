using System;
using System.Collections.Generic;
using System.IO;
using Desync.Enemies;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Desync.Levels.Editor
{
    /// Generates every scene from RoomLayouts (idempotent):
    ///   tools/unity.sh exec levels Desync.Levels.Editor.LevelBuilder.BuildAll
    /// Boot (empty), Title (office vignette + camera), Breach (fog + light only: RoomBuilder builds generated rooms into it
    /// at runtime), Room_01..06 (geometry, RoomInfo, player, enemies, NavMesh), Ending (Architect head + billboards),
    /// then EditorBuildSettings in that order.
    public static class LevelBuilder
    {
        public const string SceneDir = "Assets/Scenes";
        const string LightingPath = SceneDir + "/DesyncLighting.lighting";

        [MenuItem("DESYNC/Levels/Build All")]
        public static void BuildAll()
        {
            AssetDatabase.Refresh();
            ModelLib.RegisterModels();
            GuardModel.Build();
            RoomGeo.SourceColors = ModelLib.SourceColors;
            var scenes = new List<string>();
            int failed = 0;
            void Run(string name, Action<string> build)
            {
                string path = $"{SceneDir}/{name}.unity";
                try { build(path); scenes.Add(path); }
                catch (Exception e) { failed++; Debug.LogError($"[DESYNC] LEVELS: building {name} failed: {e}"); }
            }
            Run(SceneFlow.Boot, BuildBoot);
            Run(SceneFlow.Title, p => BuildBackdrop(p, RoomLayouts.Title, true));
            Run(RoomBuilder.BreachScene, BuildBreach);
            foreach (var room in RoomLayouts.Rooms) Run(SceneFlow.Room(room.index), p => BuildRoom(p, room));
            Run(SceneFlow.Ending, p => BuildBackdrop(p, RoomLayouts.Ending, false));

            var list = new List<EditorBuildSettingsScene>();
            foreach (var p in scenes) list.Add(new EditorBuildSettingsScene(p, true));
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Debug.Log($"[DESYNC] LEVELS BuildAll: {scenes.Count} scenes in build settings, {failed} failed");
            if (failed > 0) throw new Exception($"[DESYNC] LevelBuilder: {failed} scene(s) failed");
        }

        // ------------------------------------------------------------------------------------------------ scenes

        static Scene NewScene(string path)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string dir = path.Substring(0, path.Length - ".unity".Length);   // per-scene asset folder (meshes, navmesh)
            if (AssetDatabase.IsValidFolder(dir)) AssetDatabase.DeleteAsset(dir);
            return scene;
        }

        static void Save(Scene scene, string path)
        {
            if (!EditorSceneManager.SaveScene(scene, path)) throw new Exception("SaveScene failed " + path);
        }

        static void BuildBoot(string path)
        {
            var scene = NewScene(path);
            Environment();
            var cam = MainCamera(new Vector3(0, 1, -10), Vector3.zero);
            cam.backgroundColor = Color.black;
            Save(scene, path);
        }

        /// Breach: the room environment (fog, ambient, sun) and nothing else; RunManager builds each generated room into it.
        static void BuildBreach(string path)
        {
            var scene = NewScene(path);
            Environment();
            Save(scene, path);
        }

        /// Title / Ending: geometry + props from a backdrop layout, a main camera, no gameplay.
        static void BuildBackdrop(string path, RoomDef d, bool title)
        {
            var scene = NewScene(path);
            Environment();
            var geo = new RoomGeo(d);
            var assets = new List<Mesh>();
            geo.Build(null, assets);
            var actors = new GameObject("Backdrop").transform;
            if (title)
            {
                MainCamera(new Vector3(0f, 2.4f, -9.5f), new Vector3(0f, 1.1f, 1f));
            }
            else
            {
                Vector3 core = geo.Find(Legend.Architect, out int ac, out int ar) ? geo.Center(ac, ar) : Vector3.zero;
                var head = ModelLib.Prefab("model.head");
                if (head)
                {
                    var h = (GameObject)PrefabUtility.InstantiatePrefab(head, actors);
                    h.transform.SetPositionAndRotation(core + Vector3.up * 8.5f, Quaternion.Euler(0f, 180f, 0f));
                    h.transform.localScale = Vector3.one * 6f;
                    var arch = AssetLib.Get<Material>("mat.architect");
                    if (arch) foreach (var r in h.GetComponentsInChildren<Renderer>()) { var m = new Material[r.sharedMaterials.Length]; for (int i = 0; i < m.Length; i++) m[i] = arch; r.sharedMaterials = m; }
                }
                foreach (var (c, r) in geo.Cells(Legend.Billboard))
                    Safe("Billboard", () => EnemyFactory.Billboard(actors, geo.WallMount(c, r, 4.2f, out float yaw), yaw, new Vector2(3.2f, 2f)));
                MainCamera(core + new Vector3(0f, 1.7f, -9f), core + Vector3.up * 7.5f);
            }
            SaveMeshes(path, assets);
            int tris = Tris(scene);
            Debug.Log($"[DESYNC] LEVELS {d.name}: tris {tris} (geometry {geo.TriCount})");
            Save(scene, path);
        }

        static void BuildRoom(string path, RoomDef d)
        {
            var lint = d.Lint();
            if (lint.Count > 0) throw new Exception($"{d.name} layout errors: {string.Join("; ", lint)}");
            var scene = NewScene(path);
            Environment();
            var geo = new RoomGeo(d);
            geo.Find(Legend.Spawn, out int sc, out int sr);
            Vector3 fwd = Quaternion.Euler(0f, d.spawnYaw, 0f) * Vector3.forward;
            Vector3 spawnPos = geo.Center(sc, sr) + Vector3.up * geo.LevelY(sc, sr) + fwd * d.spawnNudge;
            var assets = new List<Mesh>();
            geo.Build(path, assets);
            if (!string.IsNullOrEmpty(d.decal)) geo.Decal(spawnPos + fwd * d.decalAhead, d.spawnYaw, d.decal, assets);
            SaveMeshes(path, assets);

            var cfg = GameConfig.I;
            var enemies = new GameObject("Enemies").transform;

            // RoomInfo + spawn + player
            var infoGo = new GameObject("RoomInfo");
            var info = infoGo.AddComponent<LevelRoomInfo>();
            info.index = d.index;
            info.terminalCount = d.Count(Legend.Terminal);
            info.traceCountdown = d.traceCountdown;
            var spawn = new GameObject("Spawn").transform;
            spawn.SetParent(infoGo.transform, false);
            spawn.SetPositionAndRotation(spawnPos, Quaternion.Euler(0f, d.spawnYaw, 0f));
            info.spawn = spawn;
            Safe("Player", () => Desync.Player.PlayerFactory.Create(null, spawnPos, d.spawnYaw));

            // enemies, terminals, portal
            GameObject drone = null;
            var wps = d.Waypoints();
            if (wps.Count >= 2)
            {
                var pts = new Vector3[wps.Count];
                for (int i = 0; i < pts.Length; i++) pts[i] = geo.Center(wps[i].c, wps[i].r) + Vector3.up * geo.LevelY(wps[i].c, wps[i].r);
                drone = Safe("Drone", () => EnemyFactory.Drone(enemies, pts, d.dronePhase));
            }
            foreach (var cam in d.cams)
            {
                d.Find(cam.id, out int c, out int r);
                var p = geo.Center(c, r) + Vector3.up * (geo.LevelY(c, r) + cam.height);
                Safe("Camera", () => EnemyFactory.Camera(enemies, p, cam.yaw, cam.pitch, cam.phase));
            }
            foreach (var gd in d.guards)
            {
                var pts = new Vector3[gd.path.Length];
                for (int i = 0; i < pts.Length; i++) pts[i] = geo.Center(gd.path[i].c, gd.path[i].r) + Vector3.up * geo.LevelY(gd.path[i].c, gd.path[i].r);
                Safe("Guard", () => EnemyFactory.Guard(enemies, pts, gd.phase));
            }
            int story = d.storyStart;
            for (int r = 0; r < d.Rows; r++)
            for (int c = 0; c < d.Cols; c++)
            {
                char ch = d.At(c, r);
                Vector3 p = geo.Center(c, r) + Vector3.up * geo.LevelY(c, r);
                int cc = c, rr = r;
                switch (ch)
                {
                    case Legend.Terminal:
                    {
                        int s = story++;
                        bool backed = geo.Facing(c, r, spawnPos, out float yaw, out Vector3 away);
                        Vector3 tp = backed ? p - away * 0.4f : p;
                        Safe("Terminal", () => EnemyFactory.Terminal(enemies, tp, yaw, d.index, s));
                        break;
                    }
                    case Legend.Exit:
                    {
                        geo.Facing(c, r, spawnPos, out float yaw, out _);
                        Safe("ExitPortal", () => EnemyFactory.ExitPortal(enemies, p, yaw));
                        break;
                    }
                    case Legend.Vent: Safe("IceVent", () => EnemyFactory.IceVent(enemies, p)); break;
                    case Legend.Glitch: Safe("GlitchZone", () => EnemyFactory.GlitchZone(enemies, p, 1.6f)); break;
                    case Legend.HijackPad:
                    {
                        geo.Facing(c, r, spawnPos, out float yaw, out _);
                        Safe("HijackPad", () => EnemyFactory.HijackPad(enemies, p, yaw, drone, d.index));
                        break;
                    }
                    case Legend.Architect: Safe("Architect", () => EnemyFactory.Architect(enemies, p)); break;
                    case Legend.Billboard:
                        Safe("Billboard", () => EnemyFactory.Billboard(enemies, geo.WallMount(cc, rr, 4.2f, out float yaw), yaw, new Vector2(3.2f, 2f)));
                        break;
                }
            }

            int navTris = BakeNavMesh(path);
            var rates = new GridValidator.Rates
            {
                move = cfg.moveSpeed, rise = cfg.meltRiseSource, drain = cfg.meltDrainRender, spike = cfg.meltFlipSpike, hack = 3f,
            };
            var v = GridValidator.Validate(d, rates);
            int tris = Tris(scene);
            string budget = tris <= 30000 ? "OK" : "OVER BUDGET";
            Debug.Log($"[DESYNC] LEVELS R{d.index + 1} {d.name}: tris {tris} ({budget}; geometry {geo.TriCount}) navmesh tris {navTris} | validator {v}\n" + v.route);
            if (!v.ok) Debug.LogError($"[DESYNC] LEVELS R{d.index + 1} {d.name} is NOT beatable within the melt budget");
            if (d.guards.Length > 0)
            {
                // Officers are RENDER-only: wall their patrol cells off (crossable only in SOURCE) and the room must still be beatable.
                var gv = GridValidator.Validate(WallOffPatrols(d), rates);
                Debug.Log($"[DESYNC] LEVELS R{d.index + 1} {d.name}: {d.guards.Length} officer(s), routes fair (Lint) | with the patrols walled off {gv}\n" + gv.route);
                if (!gv.ok) Debug.LogError($"[DESYNC] LEVELS R{d.index + 1} {d.name} is NOT beatable with its officers' patrols in the way");
            }
            Save(scene, path);
        }

        // ------------------------------------------------------------------------------------------------ helpers

        /// The room with every open cell an officer's loop crosses turned into wall, so the validator may cross it in SOURCE only.
        static RoomDef WallOffPatrols(RoomDef d)
        {
            var rows = new char[d.Rows][];
            for (int r = 0; r < d.Rows; r++) rows[r] = d.grid[r].ToCharArray();
            foreach (var gd in d.guards)
                for (int i = 0; i < gd.path.Length; i++)
                {
                    var a = gd.path[i]; var b = gd.path[(i + 1) % gd.path.Length];
                    int n = Math.Max(Math.Abs(b.c - a.c), Math.Abs(b.r - a.r)) * 4;
                    for (int s = 0; s <= n; s++)
                    {
                        int c = (int)Math.Round(a.c + (b.c - a.c) * (n > 0 ? (float)s / n : 0f)), r = (int)Math.Round(a.r + (b.r - a.r) * (n > 0 ? (float)s / n : 0f));
                        char ch = rows[r][c];
                        if (ch == Legend.Floor || ch == Legend.Poster || ch == Legend.Glitch || ch == Legend.Vent) rows[r][c] = Legend.Wall;
                    }
                }
            var g = new string[d.Rows];
            for (int r = 0; r < d.Rows; r++) g[r] = new string(rows[r]);
            return new RoomDef { index = d.index, name = d.name, grid = g, levels = d.levels, cams = d.cams, traceCountdown = d.traceCountdown };
        }

        static GameObject Safe(string what, Func<GameObject> make)
        {
            try { return make(); }
            catch (Exception e) { Debug.LogError($"[DESYNC] LEVELS: factory {what} threw (owner: that factory's workstream): {e}"); return null; }
        }

        internal static void Environment()
        {
            var cfg = GameConfig.I;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = cfg.fogStart;
            RenderSettings.fogEndDistance = cfg.fogEnd;
            RenderSettings.fogColor = cfg.fogColor;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.22f, 0.24f, 0.36f);
            Lightmapping.lightingSettings = LightingAsset();

            var sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.None;
            sun.intensity = 0.9f;
            sun.color = new Color(0.82f, 0.88f, 1f);
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
        }

        static LightingSettings LightingAsset()
        {
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingPath);
            if (ls) return ls;
            ls = new LightingSettings { name = "DesyncLighting", bakedGI = false, realtimeGI = false, autoGenerate = false };
            AssetDatabase.CreateAsset(ls, LightingPath);
            return ls;
        }

        static Camera MainCamera(Vector3 pos, Vector3 lookAt)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos));
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = GameConfig.I.fogColor;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 80f;
            go.AddComponent<AudioListener>();
            return cam;
        }

        static void SaveMeshes(string scenePath, List<Mesh> meshes)
        {
            if (meshes.Count == 0) return;
            string dir = scenePath.Substring(0, scenePath.Length - ".unity".Length);
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Path.GetDirectoryName(dir), Path.GetFileName(dir));
            string file = dir + "/Geometry.asset";
            var main = AssetDatabase.LoadMainAssetAtPath(file);
            for (int i = 0; i < meshes.Count; i++)
            {
                if (AssetDatabase.Contains(meshes[i])) continue;
                if (!main) { AssetDatabase.CreateAsset(meshes[i], file); main = meshes[i]; }
                else AssetDatabase.AddObjectToAsset(meshes[i], main);
            }
            AssetDatabase.SaveAssets();
        }

        static int BakeNavMesh(string scenePath)
        {
            var go = new GameObject("NavMesh");
            var surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = 1 << GameLayers.Floor;
            surface.agentTypeID = 0;
            surface.BuildNavMesh();
            if (!surface.navMeshData) { Debug.LogError("[DESYNC] LEVELS: NavMesh bake produced no data"); return 0; }
            string dir = scenePath.Substring(0, scenePath.Length - ".unity".Length);
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Path.GetDirectoryName(dir), Path.GetFileName(dir));
            surface.navMeshData.name = "NavMesh";
            AssetDatabase.CreateAsset(surface.navMeshData, dir + "/NavMesh.asset");
            var tri = NavMesh.CalculateTriangulation();
            return tri.indices.Length / 3;
        }

        static int Tris(Scene scene)
        {
            long n = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh && mf.GetComponent<MeshRenderer>()) n += Count(mf.sharedMesh);
                foreach (var sk in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (sk.sharedMesh) n += Count(sk.sharedMesh);
            }
            return (int)n;
            long Count(Mesh m) { long t = 0; for (int s = 0; s < m.subMeshCount; s++) t += m.GetIndexCount(s) / 3; return t; }
        }
    }
}
