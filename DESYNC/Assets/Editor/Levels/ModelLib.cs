using UnityEditor;
using UnityEngine;

namespace Desync.Levels.Editor
{
    /// Wraps the CC0 models in Assets/ThirdParty/Models as normalised prefabs and registers the model.* AssetLib keys.
    /// Prefab convention: front = +Z, up = +Y, metres, every renderer on mat.world. Pivot: bottom centre, except
    /// model.camera (top = ceiling mount), model.drone and model.head (centre). Sizes below are the fitted dimension.
    public static class ModelLib
    {
        public const string Root = "Assets/ThirdParty/Models/", PrefabDir = Root + "Prefabs/";
        enum Pivot { Bottom, Center, Top }
        enum Fit { Height, Footprint }   // footprint = largest horizontal dimension

        struct Spec
        {
            public string key, file; public float size, yaw; public Fit fit; public Pivot pivot; public bool layFlat;
            public Spec(string key, string file, float size, Fit fit, Pivot pivot = Pivot.Bottom, float yaw = 0f, bool layFlat = false)
            { this.key = key; this.file = file; this.size = size; this.fit = fit; this.pivot = pivot; this.yaw = yaw; this.layFlat = layFlat; }
        }

        // yaw = correction so the model's front faces +Z (checked with LevelSnapshots.Models).
        static readonly Spec[] Specs =
        {
            new Spec("model.desk", "KenneyFurnitureKit/desk.fbx", 1.6f, Fit.Footprint, yaw: 180f),
            new Spec("model.chair", "KenneySpaceKit/desk_chair.fbx", 1.05f, Fit.Height, yaw: 180f),
            new Spec("model.monitor", "KenneyFurnitureKit/computerScreen.fbx", 0.62f, Fit.Footprint, yaw: 180f),
            new Spec("model.serverRack", "Quaternius/Computer.obj", 2.2f, Fit.Height),
            new Spec("model.plant", "KenneyFurnitureKit/pottedPlant.fbx", 1.3f, Fit.Height),
            new Spec("model.cabinet", "KenneyFurnitureKit/bookcaseClosedDoors.fbx", 2.0f, Fit.Height, yaw: 180f),
            new Spec("model.camera", "iPoly3D/SpotLight.obj", 0.7f, Fit.Height, Pivot.Top),
            new Spec("model.drone", "Quaternius/RobotEnemyFlying.obj", 1.0f, Fit.Footprint, Pivot.Center),
            new Spec("model.ice", "iPoly3D/Crystal.obj", 1.2f, Fit.Height),
            new Spec("model.terminal", "Quaternius/ScifiComputer.obj", 1.6f, Fit.Height),
            new Spec("model.head", "PolygonalMind/ManHead.obj", 1.0f, Fit.Height, Pivot.Center),
            new Spec("model.vent", "Quaternius/Vent.obj", 1.2f, Fit.Footprint, layFlat: true),
        };

        public static GameObject Prefab(string key) => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + key + ".prefab");

        /// Idempotent: rebuilds every prefab from its model and (re)registers the AssetLib key.
        public static void RegisterModels()
        {
            if (!AssetDatabase.IsValidFolder(PrefabDir.TrimEnd('/'))) AssetDatabase.CreateFolder(Root.TrimEnd('/'), "Prefabs");
            var world = AssetLib.Get<Material>("mat.world");
            if (!world) Debug.LogWarning("[DESYNC] LEVELS: mat.world missing; model prefabs keep their import materials (rerun after ART)");
            int ok = 0;
            foreach (var s in Specs)
            {
                var prefab = Build(s, world);
                if (!prefab) continue;
                AssetLib.Put(s.key, prefab);
                ok++;
            }
            Debug.Log($"[DESYNC] LEVELS models: {ok}/{Specs.Length} prefabs registered in AssetLib");
        }

        static GameObject Build(Spec s, Material world)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + s.file);
            if (!model) { Debug.LogError($"[DESYNC] LEVELS: model missing {Root + s.file}"); return null; }
            var root = new GameObject(s.key);
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.transform.SetParent(root.transform, false);
                inst.transform.localRotation = Quaternion.Euler(s.layFlat ? -90f : 0f, s.yaw, 0f);
                var b = Bounds(root);
                float dim = s.fit == Fit.Height ? b.size.y : Mathf.Max(b.size.x, b.size.z);
                float k = s.size / Mathf.Max(dim, 1e-4f);
                inst.transform.localScale *= k;
                b = Bounds(root);
                var off = new Vector3(-b.center.x, s.pivot == Pivot.Bottom ? -b.min.y : s.pivot == Pivot.Top ? -b.max.y : -b.center.y, -b.center.z);
                inst.transform.localPosition += off;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    if (!world) continue;
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = world;
                    r.sharedMaterials = mats;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + s.key + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Bounds Bounds(GameObject root)
        {
            var rs = root.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// Base colours of the source model's submeshes for `mesh` (fallback when the import has no vertex colours).
        public static Color[] SourceColors(string key, Mesh mesh)
        {
            foreach (var s in Specs)
            {
                if (s.key != key) continue;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + s.file);
                if (!model) break;
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh != mesh) continue;
                    var mats = mf.GetComponent<Renderer>().sharedMaterials;
                    var cols = new Color[mesh.subMeshCount];
                    for (int i = 0; i < cols.Length; i++)
                    {
                        var m = i < mats.Length ? mats[i] : null;
                        cols[i] = !m ? Color.gray : m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.color : Color.gray;
                    }
                    return cols;
                }
            }
            var gray = new Color[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < gray.Length; i++) gray[i] = Color.gray;
            return gray;
        }
    }
}
