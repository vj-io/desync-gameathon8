using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Desync.Enemies.Editor
{
    /// Dev check: builds every enemy factory object in an unsaved scene and renders PNGs to .work/enemies/ (never Assets/Scenes).
    /// Run: tools/unity.sh exec enemies Desync.Enemies.Editor.EnemyPreview.Shoot
    public static class EnemyPreview
    {
        const string Out = "../.work/enemies/";

        public static void Shoot()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cfg = GameConfig.I;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = cfg.fogStart;
            RenderSettings.fogEndDistance = cfg.fogEnd;
            RenderSettings.fogColor = cfg.fogColor;
            RenderSettings.ambientLight = new Color(0.25f, 0.25f, 0.35f);

            var unlit = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.08f, 0.09f, 0.16f) };
            var floor = Box(new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f), unlit, GameLayers.Floor);
            var wall = Box(new Vector3(0f, 1.75f, 8f), new Vector3(20f, 3.5f, 0.4f), unlit, GameLayers.WallRender);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            sun.intensity = 0.6f;

            var root = new GameObject("Enemies").transform;
            EnemyFactory.Camera(root, new Vector3(-3f, 3f, 7.7f), 180f, 30f, 1.5f);
            EnemyFactory.Drone(root, new[] { new Vector3(3f, 0f, 5f), new Vector3(6f, 0f, 5f) }, 0f);
            EnemyFactory.Terminal(root, new Vector3(0f, 0f, 7.4f), 180f, 4, 6);
            var pad = EnemyFactory.HijackPad(root, new Vector3(-6f, 0f, 7.4f), 180f, null, 3);
            EnemyFactory.IceVent(root, new Vector3(2f, 0f, 2f));
            EnemyFactory.GlitchZone(root, new Vector3(-3f, 0f, 2f), 1.6f);
            EnemyFactory.ExitPortal(root, new Vector3(7f, 0f, 7.6f), 180f);
            EnemyFactory.Billboard(root, new Vector3(0f, 2.8f, 7.78f), 180f, new Vector2(3.2f, 2f));
            EnemyFactory.Architect(root, new Vector3(0f, 0f, 22f));
            var ts = Object.FindFirstObjectByType<TraceSystem>(FindObjectsInactive.Include);
            if (ts) { ts.lkpMarker.gameObject.SetActive(true); ts.lkpMarker.position = new Vector3(-1f, 0f, 3f); }

            var cam = new GameObject("PreviewCam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = cfg.fogColor;
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 80f;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;

            LayerState.Set(false);
            Shot(cam, rt, new Vector3(0f, 1.6f, -3f), new Vector3(0f, 1.2f, 6f), "overview_render");
            Shot(cam, rt, new Vector3(0.2f, 1.5f, 5.6f), new Vector3(0f, 1.25f, 7.4f), "terminal_close");
            Shot(cam, rt, new Vector3(-5.6f, 1.5f, 5.9f), new Vector3(-6f, 1.1f, 7.4f), "pad_close");
            Shot(cam, rt, new Vector3(0f, 1.6f, 12f), new Vector3(0f, 7f, 22f), "architect");
            LayerState.Set(true);
            Shot(cam, rt, new Vector3(0f, 1.6f, -3f), new Vector3(0f, 1.2f, 6f), "overview_source");
            Shot(cam, rt, new Vector3(0f, 1.6f, 12f), new Vector3(0f, 7f, 22f), "architect_source");
            LayerState.Set(false);
            Debug.Log("[DESYNC] ENEMIES preview written to " + Path.GetFullPath(Out));
            Object.DestroyImmediate(rt);
        }

        static GameObject Box(Vector3 pos, Vector3 size, Material m, int layer)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.transform.position = pos;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
            g.layer = layer;
            return g;
        }

        static void Shot(Camera cam, RenderTexture rt, Vector3 from, Vector3 at, string name)
        {
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
            foreach (var t in Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsInactive.Exclude)) t.ForceMeshUpdate();
            // apply the VisionCone / visibility state the components would set in play mode
            foreach (var v in Object.FindObjectsByType<VisionCone>(FindObjectsInactive.Exclude))
            {
                bool on = v.Active;
                if (v.coneRenderer) v.coneRenderer.enabled = on;
                if (v.spot) v.spot.enabled = on;
            }
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(Out);
            File.WriteAllBytes(Out + name + ".png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
