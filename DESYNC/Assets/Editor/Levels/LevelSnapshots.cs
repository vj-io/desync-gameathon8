using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Desync.Levels.Editor
{
    /// Verification renders (not part of the build): top-down + first-person RENDER/SOURCE views of every generated
    /// scene, and a sheet of the model prefabs, written as PNGs to .work/levels/shots/.
    ///   tools/unity.sh exec levels Desync.Levels.Editor.LevelSnapshots.Run
    public static class LevelSnapshots
    {
        static string Out => Path.GetFullPath(Path.Combine(Application.dataPath, "../../.work/levels/shots"));

        /// BuildAll then Run in one Unity session.
        public static void BuildAndShoot() { LevelBuilder.BuildAll(); Run(); }

        public static void Run()
        {
            Directory.CreateDirectory(Out);
            Models();
            foreach (var s in EditorBuildSettings.scenes)
            {
                var scene = EditorSceneManager.OpenScene(s.path, OpenSceneMode.Single);
                string name = scene.name;
                if (name == SceneFlow.Boot) continue;
                var info = Object.FindFirstObjectByType<RoomInfo>();
                Debug.Log($"[DESYNC] LEVELS snapshot {name} roomInfo={(info ? info.index.ToString() : "none")}");
                Bounds b = SceneBounds();
                // top-down, fog off so the whole layout reads
                bool fog = RenderSettings.fog; RenderSettings.fog = false;
                Shoot($"{name}_top", b.center + Vector3.up * 60f, Quaternion.Euler(90f, 0f, 0f), 40f, Mathf.Max(b.extents.z, b.extents.x * 9f / 16f) + 1f, 1600, 900);
                RenderSettings.fog = fog;
                Vector3 eye; Quaternion rot;
                if (info && info.spawn) { eye = info.spawn.position + Vector3.up * 1.6f; rot = info.spawn.rotation; }
                else { var mc = GameObject.FindWithTag("MainCamera"); eye = mc ? mc.transform.position : Vector3.up * 2f; rot = mc ? mc.transform.rotation : Quaternion.identity; }
                Shoot($"{name}_fp", eye, rot, 75f, 0f, 1280, 720);
                Shader.EnableKeyword("_SOURCE"); Shader.SetGlobalFloat("_Layer", 1f);
                Shoot($"{name}_fp_source", eye, rot, 75f, 0f, 1280, 720);
                Shader.DisableKeyword("_SOURCE"); Shader.SetGlobalFloat("_Layer", 0f);
                if (info) // a second view from the far side of the room back toward the spawn
                {
                    var far = b.center + (b.center - info.spawn.position).normalized * b.extents.magnitude * 0.45f;
                    far.y = info.spawn.position.y + 1.7f;
                    Shoot($"{name}_back", far, Quaternion.LookRotation(info.spawn.position + Vector3.up - far), 75f, 0f, 1280, 720);
                }
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Debug.Log("[DESYNC] LEVELS snapshots written to " + Out);
        }

        /// Each model.* prefab alone, seen from its front (+Z side, raised 3/4 view) and from its right (+X side).
        static void Models()
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            RenderSettings.fog = false;
            string[] keys = { "model.desk", "model.chair", "model.monitor", "model.serverRack", "model.plant", "model.cabinet",
                              "model.camera", "model.drone", "model.ice", "model.terminal", "model.head", "model.vent" };
            foreach (var key in keys)
            {
                var p = ModelLib.Prefab(key);
                if (!p) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(p);
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) { Object.DestroyImmediate(go); continue; }
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float dist = b.extents.magnitude * 3.2f;
                var front = b.center + new Vector3(0.35f, 0.45f, 1f).normalized * dist;
                var side = b.center + new Vector3(1f, 0.3f, 0f).normalized * dist;
                Shoot($"model_{key}_front", front, Quaternion.LookRotation(b.center - front), 40f, 0f, 400, 400);
                Shoot($"model_{key}_side", side, Quaternion.LookRotation(b.center - side), 40f, 0f, 400, 400);
                Object.DestroyImmediate(go);
            }
        }

        static Bounds SceneBounds()
        {
            var b = new Bounds(Vector3.zero, Vector3.one);
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (mr.gameObject.name == "Floor") b.Encapsulate(mr.bounds);
            return b;
        }

        static void Shoot(string file, Vector3 pos, Quaternion rot, float fov, float ortho, int w, int h)
        {
            var go = new GameObject("SnapshotCam");
            var cam = go.AddComponent<Camera>();
            go.transform.SetPositionAndRotation(pos, rot);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = GameConfig.I.fogColor;
            cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f;
            if (ortho > 0f) { cam.orthographic = true; cam.orthographicSize = ortho; }
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(Out, file + ".png"), tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(tex); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
        }
    }
}
