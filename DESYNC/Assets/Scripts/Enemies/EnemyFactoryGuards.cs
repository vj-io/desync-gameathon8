using TMPro;
using UnityEngine;

namespace Desync.Enemies
{
    /// HALCYON security officers (see Guard). Same contract as EnemyFactory.cs: returns the root, parented, positioned and wired;
    /// works at edit time (LevelBuilder, classic rooms) and at runtime (RoomBuilder, EntityKind.Guard).
    public static partial class EnemyFactory
    {
        static readonly Color SeeingAmber = new Color(1f, 0.62f, 0.1f);   // VisionCone's "seeing" colour: the "?" matches the cone

        /// An officer on a closed patrol loop through floor-level `waypoints` (two points = back and forth, one = a post where he
        /// stands facing `yaw`), `phase` seconds in. Body: the model.guard prefab (animated, mat.world), or a static stand-in.
        public static GameObject Guard(Transform parent, Vector3[] waypoints, float phase = 0f, float yaw = 0f)
        {
            var c = GameConfig.I;
            var wps = waypoints != null && waypoints.Length > 0 ? (Vector3[])waypoints.Clone()
                                                                 : new[] { parent ? parent.position : Vector3.zero };
            var root = EnemyKit.Root("Guard", parent, wps[0], yaw);
            root.SetActive(false);   // Guard.Awake reads the wiring below
            EnsureTraceSystem(root.transform);
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var bodyT = EnemyKit.Child("Body", root.transform, Vector3.zero);
            bodyT.gameObject.layer = GameLayers.Drone;   // solid to the player in RENDER, like a drone
            var cap = bodyT.gameObject.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 0.9f, 0f);
            cap.height = 1.8f;
            cap.radius = global::Desync.Enemies.Guard.Radius;

            var visual = EnemyKit.Child("Visual", root.transform, Vector3.zero);
            Animation anim = null;
            Transform head = null, tip = null;
            var prefab = AssetLib.Get<GameObject>("model.guard");
            if (prefab)
            {
                var m = Object.Instantiate(prefab, visual, false);
                m.name = "model.guard";
                anim = m.GetComponent<Animation>();
                head = Find(m.transform, "Head");
                tip = Find(m.transform, "BatonTip");
            }
            else StandIn(visual);

            var eye = EnemyKit.Child("Eye", root.transform, new Vector3(0f, global::Desync.Enemies.Guard.EyeHeight, 0.15f));
            eye.localRotation = Quaternion.Euler(global::Desync.Enemies.Guard.ConePitch, 0f, 0f);
            var cone = AddCone(eye, global::Desync.Enemies.Guard.ConeAngle, global::Desync.Enemies.Guard.ConeRange, false, false);
            EnemyKit.Kill(cone.spot);   // the cone mesh is his flashlight; rooms keep their spot-light budget for cameras and drones
            cone.spot = null;

            var glow = EnemyKit.Child("Glow", tip ? tip : visual, tip ? Vector3.zero : new Vector3(0.3f, 1.1f, 0.35f));
            var gb = new MeshBuilder();
            uint s = 0x6A12u;
            gb.Bipyramid(Vector3.zero, Quaternion.identity, 6, 0.08f, 0.16f, 0.16f, Color.white, ref s, 0.35f);
            EnemyKit.MeshObj("Spark", glow, gb.ToMesh("GuardSpark", false), EnemyKit.Mat("mat.cone.trace", c.danger, true));
            glow.gameObject.SetActive(false);

            var q = Marker(root.transform, "Suspicious", "?", SeeingAmber);
            var x = Marker(root.transform, "Alert", "!", c.danger);

            var g = root.AddComponent<Guard>();
            g.waypoints = wps;
            g.phase = phase;
            g.postYaw = yaw;
            g.cone = cone;
            g.visual = visual;
            g.anim = anim;
            g.head = head;
            g.glow = glow.gameObject;
            g.markQ = q;
            g.markX = x;
            g.body = cap;

            if (wps.Length > 1)   // where the patrol stands at room start (the component recomputes the same on reset)
            {
                float hover = c.droneHover, d = phase * global::Desync.Enemies.Guard.PatrolSpeed;
                Vector3 p = DronePatrol.Evaluate(wps, d) - Vector3.up * hover, ahead = DronePatrol.Evaluate(wps, d + 0.1f) - Vector3.up * hover - p;
                ahead.y = 0f;
                root.transform.SetPositionAndRotation(p, ahead.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(ahead) : Quaternion.Euler(0f, yaw, 0f));
            }
            root.SetActive(true);
            return root;
        }

        /// Generated rooms (RoomBuilder, EntityKind.Guard): `path` = x,z pairs in `room` space (EntitySpawn.path); without one
        /// he stands his post at pos facing yaw.
        public static GameObject Guard(Transform parent, Transform room, Vector3 pos, float yaw, float[] path)
        {
            if (path == null || path.Length < 2) return Guard(parent, new[] { pos }, 0f, yaw);
            var wps = new Vector3[path.Length / 2];
            for (int i = 0; i < wps.Length; i++)
                wps[i] = room ? room.TransformPoint(new Vector3(path[2 * i], 0f, path[2 * i + 1])) : new Vector3(path[2 * i], pos.y, path[2 * i + 1]);
            return Guard(parent, wps, 0f, yaw);
        }

        static GameObject Marker(Transform root, string name, string glyph, Color color)
        {
            var t = EnemyKit.Text(name, root, new Vector3(0f, 2.3f, 0f), new Vector2(1.2f, 1.2f), 9f, "font.hud", color, false);
            t.text = glyph;
            t.fontStyle = FontStyles.Bold;
            t.gameObject.SetActive(false);
            return t.gameObject;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = Find(t.GetChild(i), name);
                if (f) return f;
            }
            return null;
        }

        /// Uniform-coloured blocks in the officer's colours when model.guard is not registered (no animation).
        static void StandIn(Transform visual)
        {
            Color navy = new Color(0.10f, 0.11f, 0.19f), trousers = new Color(0.07f, 0.075f, 0.12f), skin = new Color(0.62f, 0.47f, 0.36f);
            var b = new MeshBuilder();
            b.Box(new Vector3(-0.11f, 0.45f, 0f), new Vector3(0.16f, 0.9f, 0.2f), trousers);
            b.Box(new Vector3(0.11f, 0.45f, 0f), new Vector3(0.16f, 0.9f, 0.2f), trousers);
            b.Box(new Vector3(0f, 1.2f, 0f), new Vector3(0.46f, 0.62f, 0.26f), navy);
            b.Box(new Vector3(-0.3f, 1.15f, 0f), new Vector3(0.12f, 0.6f, 0.14f), navy);
            b.Box(new Vector3(0.3f, 1.15f, 0f), new Vector3(0.12f, 0.6f, 0.14f), navy);
            b.Box(new Vector3(0f, 1.5f, 0.02f), new Vector3(0.3f, 0.06f, 0.24f), Cyan);       // collar
            b.Box(new Vector3(0f, 1.3f, 0.135f), new Vector3(0.06f, 0.3f, 0.02f), Magenta);    // tie
            b.Box(new Vector3(0f, 1.66f, 0f), new Vector3(0.22f, 0.26f, 0.24f), skin);
            b.Box(new Vector3(0.3f, 0.65f, 0.12f), new Vector3(0.05f, 0.05f, 0.5f), EnemyKit.Charcoal);   // baton
            EnemyKit.MeshObj("StandIn", visual, b.ToMesh("GuardStandIn"), EnemyKit.World);
        }
    }
}
