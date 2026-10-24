using UnityEngine;
using UnityEngine.AI;

namespace Desync.Enemies
{
    /// M3 enemies (spec enemies 6-11 + the puppet string pickup). Same contract as EnemyFactory.cs: each returns the root,
    /// parented, positioned and wired; works at edit time; the room builder maps Gen.EntityKind onto these.
    public static partial class EnemyFactory
    {
        public const float WatcherAngle = 30f, WatcherRange = 12f;

        /// Lantern Drone (RENDER): a slow guard drone on a patrol loop whose beam turns a full circle every LanternBeam.Period
        /// s (range 7 m), whatever the body does. Not hijackable.
        public static GameObject Lantern(Transform parent, Vector3[] waypoints, float phase = 0f, float speed = LanternBeam.Speed,
                                         float range = LanternBeam.Range, float beamAngle = LanternBeam.Angle)
        {
            var root = BuildDrone(parent, waypoints, phase, speed, beamAngle, range, true);
            var spin = root.GetComponent<LanternBeam>();
            spin.beam.rotation = Quaternion.Euler(0f, spin.YawAt(0f), 0f);   // the pose at room start
            return root;
        }

        /// Sentinel ICE (SOURCE) on the floor at pos: stationary crystal, 4 m kill pulse every 2.5 s after a 0.6 s growing-ring
        /// telegraph; the rim marks the kill radius in both layers. phase offsets its schedule (s).
        public static GameObject Sentinel(Transform parent, Vector3 pos, float phase = 0f)
        {
            var c = GameConfig.I;
            var root = EnemyKit.Root("SentinelIce", parent, pos, 0f);
            root.layer = GameLayers.Ice;
            var mat = EnemyKit.Mat("mat.ice", c.danger);
            var crystal = EnemyKit.Child("Crystal", root.transform, Vector3.zero);
            var b = new MeshBuilder();
            uint s = 0x5E47u;
            b.Bipyramid(new Vector3(0f, 1.05f, 0f), Quaternion.identity, 5, 0.3f, 0.95f, 0.85f, Color.white, ref s, 0.25f);
            for (int k = 0; k < 4; k++)
            {
                var q = Quaternion.Euler(0f, 90f * k + 45f, 0f);
                b.Bipyramid(q * new Vector3(0f, 0.55f, 0.5f), q * Quaternion.Euler(-30f, 0f, 0f), 3, 0.07f, 0.28f, 0.2f, Color.white, ref s, 0.3f);
            }
            var body = EnemyKit.MeshObj("Shard", crystal, b.ToMesh("SentinelCrystal"), mat);

            var rb = new MeshBuilder();
            rb.Ring(global::Desync.Enemies.Sentinel.PulseRadius - 0.06f, global::Desync.Enemies.Sentinel.PulseRadius, 64, 0.03f, Color.white);
            EnemyKit.MeshObj("Rim", root.transform, rb.ToMesh("SentinelRim", false), mat);
            var charge = EnemyKit.Child("Charge", root.transform, Vector3.zero);
            var cb = new MeshBuilder();
            cb.Ring(0.85f, 1f, 48, 0.05f, Color.white);
            EnemyKit.MeshObj("Ring", charge, cb.ToMesh("SentinelCharge", false), mat);
            charge.gameObject.SetActive(false);

            var sen = root.AddComponent<Sentinel>();
            sen.phase = phase;
            sen.charge = charge;
            sen.crystal = crystal;
            sen.bodyRenderers = new Renderer[] { body };
            return root;
        }

        /// Splitter ICE (SOURCE) on an ICE marker: a twin-crystal hunter guarding pos, plus its two minis (pooled, inactive).
        public static GameObject SplitterIce(Transform parent, Vector3 pos)
        {
            var root = EnemyKit.Root("SplitterIce", parent, pos, 0f);
            var split = root.AddComponent<SplitterIce>();
            split.hunter = BuildHunter<SplitterHunter>(root.transform, 0, false, Twin("SplitterCrystal", 1f));
            split.hunter.patrol = new[] { pos };
            var mini = Twin("MiniCrystal", 0.55f);
            split.minis = new IceMini[2];
            for (int i = 0; i < split.minis.Length; i++) split.minis[i] = BuildHunter<IceMini>(root.transform, 1 + i, false, mini);
            return root;
        }

        static Mesh Twin(string name, float k)
        {
            var b = new MeshBuilder();
            uint s = 0x5917u;
            b.Bipyramid(new Vector3(-0.15f, 0.55f, 0f) * k, Quaternion.Euler(0f, 0f, 14f), 5, 0.2f * k, 0.55f * k, 0.45f * k, Color.white, ref s, 0.4f);
            b.Bipyramid(new Vector3(0.15f, 0.55f, 0f) * k, Quaternion.Euler(0f, 0f, -14f), 5, 0.2f * k, 0.55f * k, 0.45f * k, Color.white, ref s, 0.4f);
            return b.ToMesh(name);
        }

        /// Daemon (BOTH) at its post: a SOURCE hunter that crosses into RENDER when the player flips near it.
        public static GameObject Daemon(Transform parent, Vector3 pos)
        {
            var c = GameConfig.I;
            var root = EnemyKit.Root("Daemon", parent, pos, 0f);
            root.SetActive(false);   // the agent is added off the NavMesh; it is placed on its first frame
            root.layer = GameLayers.Ice;
            var agent = root.AddComponent<NavMeshAgent>();
            agent.speed = c.iceSpeed;
            agent.acceleration = 40f;
            agent.angularSpeed = 720f;
            agent.radius = IceHunter.Radius;
            agent.height = 1.6f;
            agent.stoppingDistance = 0f;
            agent.autoBraking = false;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.enabled = false;

            var visual = EnemyKit.Child("Body", root.transform, Vector3.zero);
            var b = new MeshBuilder();
            uint s = 0xDAE0u;
            b.Bipyramid(new Vector3(0f, 0.95f, 0f), Quaternion.identity, 4, 0.3f, 0.95f, 0.75f, Color.white, ref s, 0.2f);
            for (int k = 0; k < 3; k++)
            {
                var q = Quaternion.Euler(0f, 120f * k, 0f);
                b.Bipyramid(q * new Vector3(0f, 1.25f, 0.55f), q * Quaternion.Euler(90f, 0f, 0f), 3, 0.07f, 0.3f, 0.3f, Color.white, ref s, 0.3f);
            }
            var r = EnemyKit.MeshObj("Shard", visual, b.ToMesh("DaemonBody"), EnemyKit.Mat("mat.ice", c.danger));

            var d = root.AddComponent<Daemon>();
            d.agent = agent;
            d.visual = visual;
            d.renderers = new Renderer[] { r };
            d.post = pos;
            root.SetActive(true);
            return root;
        }

        /// Watcher Eye (BOTH): a fixed eye on a wall mount (yaw = facing, pitch = downward tilt) whose beam sees both layers
        /// and shows in both. Not a camera: BLIND CAMS, Wider Lens and hijacked drones leave it alone.
        public static GameObject Watcher(Transform parent, Vector3 pos, float yaw, float pitch = 30f, float angle = WatcherAngle,
                                         float range = WatcherRange)
        {
            var c = GameConfig.I;
            var root = EnemyKit.Root("WatcherEye", parent, pos, yaw);
            EnsureTraceSystem(root.transform);
            var tilt = EnemyKit.Child("Tilt", root.transform, Vector3.zero);
            tilt.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            var b = new MeshBuilder();
            uint s = 1u;
            b.Box(new Vector3(0f, 0f, -0.05f), new Vector3(0.6f, 0.6f, 0.1f), EnemyKit.Navy);                                  // back plate
            b.Bipyramid(new Vector3(0f, 0f, 0.02f), Quaternion.Euler(90f, 0f, 0f), 8, 0.3f, 0.16f, 0.02f, Magenta, ref s, 0f);   // lid
            b.Bipyramid(new Vector3(0f, 0f, 0.1f), Quaternion.Euler(90f, 0f, 0f), 8, 0.15f, 0.1f, 0.02f, c.green, ref s, 0f);    // iris
            b.Box(new Vector3(0f, 0f, 0.2f), new Vector3(0.07f, 0.07f, 0.04f), c.danger);                                    // pupil
            EnemyKit.MeshObj("Eye", tilt, b.ToMesh("WatcherEye"), EnemyKit.World);
            var eye = EnemyKit.Child("Lens", tilt, new Vector3(0f, 0f, 0.24f));
            root.AddComponent<WatcherEye>().cone = AddCone(eye, angle, range, false, true);
            return root;
        }

        /// Puppet string pickup (SOURCE-only) on the floor at pos; `id` is raised with EnemyEvents.PuppetString when taken.
        public static GameObject PuppetString(Transform parent, Vector3 pos, string id)
        {
            var c = GameConfig.I;
            var root = EnemyKit.Root("PuppetString", parent, pos, 0f);
            var visual = EnemyKit.Child("String", root.transform, Vector3.zero);
            var mat = EnemyKit.Mat("mat.strings", c.green, true);
            var lr = EnemyKit.Child("Thread", visual, Vector3.zero).gameObject.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPosition(0, Vector3.up * 3.4f);
            lr.SetPosition(1, Vector3.up * 1.25f);
            lr.widthMultiplier = 0.03f;
            lr.sharedMaterial = mat;
            lr.startColor = lr.endColor = c.green;
            EnemyKit.NoShadows(lr);
            var b = new MeshBuilder();
            uint s = 0x57A1u;
            b.Bipyramid(new Vector3(0f, 1.05f, 0f), Quaternion.identity, 4, 0.12f, 0.2f, 0.2f, Color.white, ref s, 0.2f);
            EnemyKit.MeshObj("Knot", visual, b.ToMesh("PuppetKnot", false), mat);
            visual.gameObject.SetActive(false);   // SOURCE only

            var p = root.AddComponent<PuppetStringPickup>();
            p.id = id;
            p.visual = visual;
            return root;
        }

        /// The room's two Auditor Drones (inactive) under `parent` (the TraceSystem, so they go with the room).
        internal static AuditorDrone[] AuditorDrones(Transform parent)
        {
            var pool = new AuditorDrone[2];
            var mat = EnemyKit.Mat("mat.drone", EnemyKit.Navy);
            for (int i = 0; i < pool.Length; i++)
            {
                var go = new GameObject("AuditorDrone" + i);
                go.SetActive(false);
                go.layer = GameLayers.Drone;
                go.transform.SetParent(parent, false);
                var body = EnemyKit.Child("Body", go.transform, Vector3.zero);
                var b = new MeshBuilder();
                Color hull = new Color(0.75f, 0.75f, 0.8f), dark = new Color(0.45f, 0.45f, 0.5f);
                b.Prism(Vector3.zero, 3, 0.48f, 0.18f, hull, Magenta);                     // triangular hull: the auditor's tell
                b.Prism(new Vector3(0f, 0.13f, 0f), 3, 0.22f, 0.1f, dark, Cyan, 60f);
                b.Box(new Vector3(0f, -0.02f, 0.36f), new Vector3(0.16f, 0.08f, 0.08f), GameConfig.I.danger);
                EnemyKit.MeshObj("Hull", body, b.ToMesh("AuditorDroneHull"), mat);
                var eye = EnemyKit.Child("Eye", go.transform, new Vector3(0f, 0f, 0.4f));
                eye.localRotation = Quaternion.Euler(12f, 0f, 0f);
                var cone = AddCone(eye, global::Desync.Enemies.AuditorDrone.ConeAngle, global::Desync.Enemies.AuditorDrone.ConeRange, false, false);
                RoomHazards.Dim(cone.spot);
                var a = go.AddComponent<AuditorDrone>();
                a.cone = cone;
                a.body = body;
                pool[i] = a;
            }
            return pool;
        }
    }
}
