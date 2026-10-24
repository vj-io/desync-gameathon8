#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Desync.Enemies
{
    /// Editor only. Builds model.guard (prefab + skinned mesh + legacy clips) from Assets/ThirdParty/Models/Characters/GuardSuit.json:
    /// the CC0 "Man in Suit" (Quaternius) already in Unity space, recoloured as HALCYON security and holding a baton
    /// (converted by .work/guards/tools/glb2guard.py; see the LICENSE.txt next to it). LevelBuilder.BuildAll runs it before the
    /// rooms; idempotent, and the mesh, clips and prefab keep their GUIDs across rebuilds.
    /// Prefab: root (Animation: idle, walk, run, attack) / Rig (bones; "Head", and "BatonTip" under "Palm.R") / Body
    /// (SkinnedMeshRenderer on mat.world, so SOURCE draws it as wireframe). Feet at the origin, facing +Z, 1.8 m tall.
    ///   tools/unity.sh exec guards Desync.Enemies.GuardModel.Snapshot   (renders .work/guards/shots/*.png to check it)
    public static class GuardModel
    {
        public const string Dir = "Assets/ThirdParty/Models/Characters/", Source = Dir + "GuardSuit.json",
                            DataPath = Dir + "GuardSuit.asset", PrefabPath = Dir + "model.guard.prefab", Key = "model.guard";

        [Serializable] class ClipData { public string name; public float length; public int frames; public float[] pos, rot; }

        [Serializable]
        class Data
        {
            public string[] boneNames;
            public int[] boneParent, boneIndex, triangles;
            public float[] bonePos, boneRot, vertices, normals, boneWeight, triColors, tip;
            public int head, hand;
            public ClipData[] clips;
        }

        public static GameObject Build()
        {
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(Source);
            if (!ta) { Debug.LogError("[DESYNC] ENEMIES: guard model source missing: " + Source); return null; }
            var d = JsonUtility.FromJson<Data>(ta.text);
            var root = new GameObject(Key);
            try
            {
                int nb = d.boneNames.Length;
                var rig = new GameObject("Rig").transform;
                rig.SetParent(root.transform, false);
                var bones = new Transform[nb];
                for (int i = 0; i < nb; i++) bones[i] = new GameObject(d.boneNames[i]).transform;
                for (int i = 0; i < nb; i++)
                {
                    bones[i].SetParent(d.boneParent[i] < 0 ? rig : bones[d.boneParent[i]], false);
                    bones[i].localPosition = V3(d.bonePos, i);
                    bones[i].localRotation = Q(d.boneRot, i);
                }
                var tip = new GameObject("BatonTip").transform;
                tip.SetParent(bones[d.hand], false);
                tip.position = V3(d.tip, 0);

                var bodyGo = new GameObject("Body");
                bodyGo.transform.SetParent(root.transform, false);
                var mesh = BuildMesh(d, bones, bodyGo.transform);
                var smr = bodyGo.AddComponent<SkinnedMeshRenderer>();
                var mat = AssetLib.Get<Material>("mat.world");
                if (mat) smr.sharedMaterial = mat;
                else Debug.LogWarning("[DESYNC] ENEMIES: mat.world missing; the guard keeps the default material (rerun after ART)");
                EnemyKit.NoShadows(smr);
                smr.skinnedMotionVectors = false;
                smr.quality = SkinQuality.Bone2;
                smr.updateWhenOffscreen = false;
                smr.bones = bones;
                smr.rootBone = bones[0];
                smr.localBounds = new Bounds(bones[0].InverseTransformPoint(new Vector3(0f, 0.95f, 0f)), Vector3.one * 2.6f);   // arms swing out

                var clips = new AnimationClip[d.clips.Length];
                for (int i = 0; i < clips.Length; i++) clips[i] = BuildClip(d.clips[i], root.transform, bones);

                // Save into the existing assets when there are some, so scenes and the prefab keep their references.
                var meshAsset = AssetDatabase.LoadAssetAtPath<Mesh>(DataPath);
                if (meshAsset) { EditorUtility.CopySerialized(mesh, meshAsset); Object.DestroyImmediate(mesh); }
                else { AssetDatabase.CreateAsset(mesh, DataPath); meshAsset = mesh; }
                var subs = AssetDatabase.LoadAllAssetsAtPath(DataPath);
                for (int i = 0; i < clips.Length; i++)
                {
                    AnimationClip old = null;
                    foreach (var s in subs) if (s is AnimationClip c && c.name == clips[i].name) old = c;
                    if (!old) { AssetDatabase.AddObjectToAsset(clips[i], DataPath); continue; }
                    EditorUtility.CopySerialized(clips[i], old);
                    Object.DestroyImmediate(clips[i]);
                    clips[i] = old;
                }
                smr.sharedMesh = meshAsset;
                AssetDatabase.SaveAssets();

                var anim = root.AddComponent<Animation>();
                anim.playAutomatically = false;
                anim.cullingType = AnimationCullingType.BasedOnRenderers;
                foreach (var c in clips) anim.AddClip(c, c.name);
                anim.clip = clips[0];

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetLib.Put(Key, prefab);
                Debug.Log($"[DESYNC] ENEMIES guard model: {meshAsset.triangles.Length / 3} tris, {nb} bones, {clips.Length} clips -> {PrefabPath}");
                return prefab;
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// One vertex per triangle corner (flat colour per triangle), MeshPrep for mat.world's edges and SOURCE wireframe, then the
        /// skin: MeshPrep keeps the corner order, so corner i keeps its bone weights.
        static Mesh BuildMesh(Data d, Transform[] bones, Transform body)
        {
            int nt = d.triangles.Length / 3, nv = nt * 3;
            var pos = new Vector3[nv];
            var nrm = new Vector3[nv];
            var col = new Color[nv];
            var bw = new BoneWeight[nv];
            var idx = new int[nv];
            for (int t = 0; t < nt; t++)
            {
                var c = new Color(d.triColors[t * 3], d.triColors[t * 3 + 1], d.triColors[t * 3 + 2], 1f);
                int a = d.triangles[t * 3], b = d.triangles[t * 3 + 1], e = d.triangles[t * 3 + 2];
                Vector3 face = Vector3.Cross(V3(d.vertices, b) - V3(d.vertices, a), V3(d.vertices, e) - V3(d.vertices, a)).normalized;
                for (int k = 0; k < 3; k++)
                {
                    int v = d.triangles[t * 3 + k], o = t * 3 + k;
                    pos[o] = V3(d.vertices, v);
                    var n = V3(d.normals, v);
                    nrm[o] = n.sqrMagnitude > 0.5f ? n : face;   // the baton is authored without normals
                    col[o] = c;
                    idx[o] = o;
                    bw[o] = new BoneWeight
                    {
                        boneIndex0 = d.boneIndex[v * 4], boneIndex1 = d.boneIndex[v * 4 + 1],
                        boneIndex2 = d.boneIndex[v * 4 + 2], boneIndex3 = d.boneIndex[v * 4 + 3],
                        weight0 = d.boneWeight[v * 4], weight1 = d.boneWeight[v * 4 + 1],
                        weight2 = d.boneWeight[v * 4 + 2], weight3 = d.boneWeight[v * 4 + 3],
                    };
                }
            }
            var mesh = new Mesh { name = "GuardBody" };
            mesh.vertices = pos;
            mesh.normals = nrm;
            mesh.colors = col;
            mesh.triangles = idx;
            Desync.Art.MeshPrep.Process(mesh);
            Creases(mesh, col);
            mesh.boneWeights = bw;
            var bind = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) bind[i] = bones[i].worldToLocalMatrix * body.localToWorldMatrix;
            mesh.bindposes = bind;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// mat.world outlines every edge MeshPrep keeps, and on a character that is nearly all of them. Keep creases (faces more
        /// than CreaseAngle apart) and colour boundaries only, so the uniform reads as cloth with trims; SOURCE still draws every
        /// edge (the hidden ones at the diagonal weight), so the statue stays a full wireframe.
        const float CreaseAngle = 35f;

        static void Creases(Mesh mesh, Color[] col)
        {
            var uv = new System.Collections.Generic.List<Vector4>();
            mesh.GetUVs(Desync.Art.MeshPrep.Channel, uv);
            var pos = mesh.vertices;
            int nt = pos.Length / 3;
            var face = new Vector3[nt];
            for (int t = 0; t < nt; t++) face[t] = Vector3.Cross(pos[t * 3 + 1] - pos[t * 3], pos[t * 3 + 2] - pos[t * 3]).normalized;
            float cos = Mathf.Cos(CreaseAngle * Mathf.Deg2Rad);
            var first = new System.Collections.Generic.Dictionary<(Vector3Int, Vector3Int), int>(nt * 2);
            var flags = new int[nt];
            for (int t = 0; t < nt; t++)
            for (int e = 0; e < 3; e++)
            {
                Vector3Int p = Quant(pos[t * 3 + (e + 1) % 3]), q = Quant(pos[t * 3 + (e + 2) % 3]);
                var key = p.x < q.x || (p.x == q.x && (p.y < q.y || (p.y == q.y && p.z < q.z))) ? (p, q) : (q, p);
                if (!first.TryGetValue(key, out int o)) { first[key] = t * 3 + e; continue; }
                int t0 = o / 3;
                if (Vector3.Dot(face[t0], face[t]) < cos || col[t0 * 3] != col[t * 3]) continue;
                flags[t0] |= 1 << (o % 3);
                flags[t] |= 1 << e;
            }
            for (int i = 0; i < uv.Count; i++) { var u = uv[i]; u.w = 8 + flags[i / 3]; uv[i] = u; }
            mesh.SetUVs(Desync.Art.MeshPrep.Channel, uv);
        }

        static Vector3Int Quant(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1e4f), Mathf.RoundToInt(p.y * 1e4f), Mathf.RoundToInt(p.z * 1e4f));

        /// Legacy clip, keyed at each sample with linear tangents. A bone whose position (or rotation) never leaves its rest value
        /// gets no curve for it: legacy playback leaves it at the prefab's rest pose.
        static AnimationClip BuildClip(ClipData c, Transform root, Transform[] bones)
        {
            var clip = new AnimationClip { name = c.name, legacy = true, frameRate = 30f };
            clip.wrapMode = c.name == "attack" ? WrapMode.ClampForever : WrapMode.Loop;
            int nb = bones.Length, nf = c.frames;
            var t = new float[nf];
            for (int f = 0; f < nf; f++) t[f] = c.length * f / (nf - 1);
            var v = new float[nf];
            for (int b = 0; b < nb; b++)
            {
                string path = AnimationUtility.CalculateTransformPath(bones[b], root);
                Vector3 rp = bones[b].localPosition;
                Quaternion rr = bones[b].localRotation;
                if (Moves(c.pos, nb, nf, b, 3, k => rp[k]))
                    for (int k = 0; k < 3; k++)
                    {
                        for (int f = 0; f < nf; f++) v[f] = c.pos[(f * nb + b) * 3 + k];
                        clip.SetCurve(path, typeof(Transform), "localPosition." + "xyz"[k], Linear(t, v));
                    }
                if (Moves(c.rot, nb, nf, b, 4, k => rr[k]))
                    for (int k = 0; k < 4; k++)
                    {
                        for (int f = 0; f < nf; f++) v[f] = c.rot[(f * nb + b) * 4 + k];
                        clip.SetCurve(path, typeof(Transform), "localRotation." + "xyzw"[k], Linear(t, v));
                    }
            }
            clip.EnsureQuaternionContinuity();
            return clip;
        }

        /// Any sample of bone b's `dim`-component channel away from its rest value (a quaternion and its negation are equal).
        static bool Moves(float[] a, int nb, int nf, int b, int dim, Func<int, float> rest)
        {
            for (int f = 0; f < nf; f++)
            {
                float same = 0f, flip = 0f;
                for (int k = 0; k < dim; k++)
                {
                    float x = a[(f * nb + b) * dim + k], r = rest(k);
                    same = Mathf.Max(same, Mathf.Abs(x - r));
                    flip = Mathf.Max(flip, Mathf.Abs(x + r));
                }
                if (Mathf.Min(same, dim == 4 ? flip : same) > 1e-4f) return true;
            }
            return false;
        }

        static AnimationCurve Linear(float[] t, float[] v)
        {
            var keys = new Keyframe[t.Length];
            for (int i = 0; i < t.Length; i++)
            {
                float inT = i > 0 ? (v[i] - v[i - 1]) / (t[i] - t[i - 1]) : 0f;
                float outT = i < t.Length - 1 ? (v[i + 1] - v[i]) / (t[i + 1] - t[i]) : inT;
                keys[i] = new Keyframe(t[i], v[i], i > 0 ? inT : outT, outT);
            }
            return new AnimationCurve(keys);
        }

        static Vector3 V3(float[] a, int i) => new Vector3(a[i * 3], a[i * 3 + 1], a[i * 3 + 2]);
        static Quaternion Q(float[] a, int i) => new Quaternion(a[i * 4], a[i * 4 + 1], a[i * 4 + 2], a[i * 4 + 3]);

        // ------------------------------------------------------------------------------------------------ verification renders

        static string Out => Path.GetFullPath(Path.Combine(Application.dataPath, "../../.work/guards/shots"));

        /// Builds the model, then renders it front / side in RENDER and SOURCE at a few poses (idle, walk, run, the strike) and
        /// a factory-built officer with his flashlight and "!" to .work/guards/shots. Not part of the build.
        public static void Snapshot()
        {
            var prefab = Build();
            if (!prefab) return;
            Directory.CreateDirectory(Out);
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.22f, 0.24f, 0.36f);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = GameConfig.I.fogColor;
            cam.fieldOfView = 35f;
            var rt = new RenderTexture(640, 640, 24);
            cam.targetTexture = rt;

            var go = Object.Instantiate(prefab);
            var anim = go.GetComponent<Animation>();
            // No frame loop runs in a batch call, so the skinned mesh would render in its bind pose: bake each pose to a mesh.
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            smr.enabled = false;
            var baked = new Mesh();
            var stat = new GameObject("Baked");
            stat.AddComponent<MeshFilter>().sharedMesh = baked;
            stat.AddComponent<MeshRenderer>().sharedMaterial = smr.sharedMaterial;
            (string clip, float t)[] poses = { ("idle", 0f), ("walk", 0.26f), ("run", 0.22f), ("attack", 0.2f), ("attack", 0.37f), ("attack", 0.6f) };
            foreach (var (clipName, time) in poses)
            {
                anim[clipName].clip.SampleAnimation(go, time);
                smr.BakeMesh(baked);
                stat.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                foreach (bool src in new[] { false, true })
                {
                    if (src) Shader.EnableKeyword("_SOURCE"); else Shader.DisableKeyword("_SOURCE");
                    Shader.SetGlobalFloat("_Layer", src ? 1f : 0f);
                    Shoot(cam, rt, new Vector3(0.6f, 1.3f, 4.2f), $"{clipName}_{time:0.00}_front{(src ? "_source" : "")}");
                    if (!src) Shoot(cam, rt, new Vector3(4.2f, 1.3f, 0.4f), $"{clipName}_{time:0.00}_side");
                }
            }
            Shader.DisableKeyword("_SOURCE");
            Shader.SetGlobalFloat("_Layer", 0f);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(stat);
            Object.DestroyImmediate(baked);

            // the full officer from the factory: flashlight cone and the "!" marker
            var g = EnemyFactory.Guard(null, new[] { Vector3.zero, new Vector3(0f, 0f, 6f) });
            g.GetComponent<Guard>().markX.SetActive(true);
            Shoot(cam, rt, new Vector3(3.5f, 2.6f, 7.5f), "officer_cone", new Vector3(0f, 1.2f, 2.5f));
            Object.DestroyImmediate(g);
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Debug.Log("[DESYNC] ENEMIES guard snapshots written to " + Out);
        }

        /// Every officer in the classic rooms, seen from 3 m beside him at eye height, in RENDER and SOURCE (rooms as LevelBuilder saved them).
        ///   tools/unity.sh exec guards Desync.Enemies.GuardModel.SnapshotRooms
        public static void SnapshotRooms()
        {
            Directory.CreateDirectory(Out);
            for (int room = 1; room <= 6; room++)
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene($"Assets/Scenes/{SceneFlow.Room(room - 1)}.unity");
                var guards = Object.FindObjectsByType<Guard>(FindObjectsInactive.Include);
                if (guards.Length == 0) continue;
                var cam = new GameObject("ShotCam").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = GameConfig.I.fogColor;
                cam.fieldOfView = 75f;
                var rt = new RenderTexture(960, 540, 24);
                cam.targetTexture = rt;
                for (int i = 0; i < guards.Length; i++)
                {
                    var t = guards[i].transform;
                    Vector3 eye = t.position + t.right * 3.2f + t.forward * 0.8f + Vector3.up * 1.5f;   // beside him, outside his flashlight
                    foreach (bool src in new[] { false, true })
                    {
                        if (src) Shader.EnableKeyword("_SOURCE"); else Shader.DisableKeyword("_SOURCE");
                        Shader.SetGlobalFloat("_Layer", src ? 1f : 0f);
                        Shoot(cam, rt, eye, $"room{room}_guard{i + 1}{(src ? "_source" : "")}", t.position + Vector3.up * 1.2f);
                    }
                }
                Shader.DisableKeyword("_SOURCE");
                Shader.SetGlobalFloat("_Layer", 0f);
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(cam.gameObject);
            }
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            Debug.Log("[DESYNC] ENEMIES guard room snapshots written to " + Out);
        }

        static void Shoot(Camera cam, RenderTexture rt, Vector3 eye, string name, Vector3? look = null)
        {
            cam.transform.position = eye;
            cam.transform.rotation = Quaternion.LookRotation((look ?? new Vector3(0f, 0.95f, 0f)) - eye);
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(Out, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
#endif
