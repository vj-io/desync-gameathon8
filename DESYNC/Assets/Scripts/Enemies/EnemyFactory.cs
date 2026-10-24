using TMPro;
using UnityEngine;
using UnityEngine.AI;
using Boss = Desync.Enemies.Auditor;

namespace Desync.Enemies
{
    /// CONTRACT: LevelBuilder (editor) builds rooms by calling these. Signatures are frozen; the Enemies workstream
    /// owns the bodies. Each returns the root GameObject, already parented, positioned and fully wired
    /// (colliders on the right GameLayers, materials from AssetLib, IResettable where it has state).
    /// Works at edit time (serialized fields only, nothing configured in Start) and at runtime.
    /// ORIENTATION: every `yaw` is the direction the object FACES (+Z): a camera looks along it; terminal/pad screens,
    /// billboards and the exit label are read by a player standing on that side, looking back along -yaw.
    /// AssetLib models (model.*) are fitted by size and assumed to face +Z; null keys fall back to procedural meshes.
    public static partial class EnemyFactory
    {
        public const float FaceHeight = 7f, FaceSize = 5f, ShardScale = 1.5f;

        static readonly string[] Slogans =
        {
            "PRODUCTIVITY IS PEACE", "YOUR DATA IS SAFE WITH US", "SMILE. YOU ARE RENDERED.",
            "THE BOARD CARES", "COMPLIANCE IS COMFORT", "A BETTER REALITY",
        };

        static Color Magenta => GameConfig.I.magenta;
        static Color Cyan => GameConfig.I.cyan;

        /// Wall/ceiling security camera. yaw = sweep centre (world deg), pitch = downward tilt (deg). phase offsets the sweep (s).
        /// Classic-room stats from GameConfig (60 deg x 8 m, sweeping +/-45 deg over 3 s one way).
        public static GameObject Camera(Transform parent, Vector3 pos, float yaw, float pitch = 20f, float phase = 0f)
        {
            var c = GameConfig.I;
            return BuildCamera(parent, pos, yaw, pitch, phase, c.cameraConeAngle, c.cameraConeRange, -1f, -1f);
        }

        /// Generated-room fixed camera (spec: 60 deg cone, 12 m, TRACE after the usual window). Never moves.
        public static GameObject FixedCamera(Transform parent, Vector3 pos, float yaw, float pitch = 20f, float angle = 60f, float range = 12f) =>
            BuildCamera(parent, pos, yaw, pitch, 0f, angle, range, 0f, -1f);

        /// Generated-room sweep camera (spec: 90 deg arc, 8 s period, 10 m). `arc` = the whole angle the cone covers over a
        /// sweep (the generator validates against that wedge), so the cone's centre travels +/-(arc - coneAngle)/2;
        /// `period` = there and back.
        public static GameObject SweepCamera(Transform parent, Vector3 pos, float yaw, float pitch = 20f, float phase = 0f,
                                             float arc = 90f, float period = 8f, float range = 10f, float coneAngle = 30f) =>
            BuildCamera(parent, pos, yaw, pitch, phase, coneAngle, range, Mathf.Max(0f, (arc - coneAngle) * 0.5f), period * 0.5f);

        static GameObject BuildCamera(Transform parent, Vector3 pos, float yaw, float pitch, float phase, float coneAngle, float range,
                                      float halfAngle, float oneWay)
        {
            var root = EnemyKit.Root("SecurityCamera", parent, pos, yaw);
            EnsureTraceSystem(root.transform);
            var sweep = root.AddComponent<CameraSweep>();
            sweep.halfAngle = halfAngle;
            sweep.oneWay = oneWay;
            var pivot = EnemyKit.Child("Pivot", root.transform, Vector3.zero);
            pivot.localRotation = Quaternion.Euler(0f, sweep.OffsetAt(phase), 0f);
            var tilt = EnemyKit.Child("Tilt", pivot, Vector3.zero);
            tilt.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            var world = EnemyKit.World;
            float lensZ = 0.48f;
            if (EnemyKit.Model("model.camera", tilt, 0.55f, world, false, out var mb)) lensZ = mb.max.z + 0.02f;
            else
            {
                var b = new MeshBuilder();
                b.Box(new Vector3(0f, 0f, 0.2f), new Vector3(0.26f, 0.22f, 0.5f), EnemyKit.Navy);
                b.Box(new Vector3(0f, 0.125f, 0.22f), new Vector3(0.32f, 0.03f, 0.58f), Magenta);   // visor
                b.Box(new Vector3(0f, 0f, 0.46f), new Vector3(0.15f, 0.15f, 0.04f), Cyan);         // lens
                EnemyKit.MeshObj("Housing", tilt, b.ToMesh("CameraHousing"), world);
                var m = new MeshBuilder();
                m.Box(new Vector3(0f, 0f, -0.12f), new Vector3(0.07f, 0.07f, 0.26f), EnemyKit.Charcoal);   // arm to the wall
                m.Box(new Vector3(0f, 0f, -0.26f), new Vector3(0.22f, 0.22f, 0.04f), EnemyKit.Charcoal);   // wall plate
                EnemyKit.MeshObj("Mount", root.transform, m.ToMesh("CameraMount"), world);
            }
            var eye = EnemyKit.Child("Eye", tilt, new Vector3(0f, 0f, lensZ));
            sweep.cone = AddCone(eye, coneAngle, range, true, false);

            sweep.pivot = pivot;
            sweep.phase = phase;
            return root;
        }

        /// Hovering guard drone looping through floor-level waypoints (hover height from GameConfig). Kills on body touch.
        /// Kinematic, RoomClock-driven loop (see DronePatrol). Hijackable through a HijackPad (HijackedDrone component).
        public static GameObject Drone(Transform parent, Vector3[] waypoints, float phase = 0f)
        {
            var c = GameConfig.I;
            return BuildDrone(parent, waypoints, phase, -1f, c.droneConeAngle, c.droneConeRange);
        }

        /// Generated-room guard drone (spec: patrol 3 m/s, 10 m cone; 45 deg). Same drone as above with its own stats.
        public static GameObject Drone(Transform parent, Vector3[] waypoints, float phase, float speed = 3f, float coneRange = 10f, float coneAngle = 45f) =>
            BuildDrone(parent, waypoints, phase, speed, coneAngle, coneRange);

        /// lantern = Lantern Drone: lamp body, no hijack link, its cone on a Beam pivot that LanternBeam spins (see Lantern()).
        static GameObject BuildDrone(Transform parent, Vector3[] waypoints, float phase, float speed, float coneAngle, float coneRange,
                                     bool lantern = false)
        {
            var c = GameConfig.I;
            var wps = waypoints != null && waypoints.Length > 0 ? (Vector3[])waypoints.Clone()
                                                                 : new[] { parent ? parent.position : Vector3.zero };
            var root = EnemyKit.Root(lantern ? "LanternDrone" : "Drone", parent, Vector3.zero, 0f);
            EnsureTraceSystem(root.transform);
            root.layer = GameLayers.Drone;
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var col = root.AddComponent<SphereCollider>();
            col.radius = DronePatrol.BodyRadius;

            var body = EnemyKit.Child("Body", root.transform, Vector3.zero);
            var droneMat = EnemyKit.Mat("mat.drone", EnemyKit.Navy);
            Renderer[] bodyRenderers;
            var model = lantern ? null : EnemyKit.Model("model.drone", body, 0.95f, droneMat, false, out _);
            if (model) bodyRenderers = model.GetComponentsInChildren<Renderer>(true);
            else
            {
                var b = new MeshBuilder();
                // light vertex colours: mat.drone / mat.hijacked multiply them, so the hijack tint reads
                Color hull = new Color(0.75f, 0.75f, 0.8f), dark = new Color(0.45f, 0.45f, 0.5f);
                b.Prism(Vector3.zero, 6, 0.42f, 0.2f, hull, Magenta);                     // hex hull
                if (lantern)
                {
                    b.Prism(new Vector3(0f, -0.3f, 0f), 6, 0.16f, 0.36f, Cyan, dark, 30f);   // hanging lamp
                    b.Prism(new Vector3(0f, 0.2f, 0f), 3, 0.08f, 0.2f, dark, Magenta);      // mast
                }
                else
                {
                    b.Prism(new Vector3(0f, 0.15f, 0f), 6, 0.22f, 0.1f, dark, Cyan, 30f);     // dome
                    b.Prism(new Vector3(0f, -0.13f, 0f), 6, 0.3f, 0.06f, dark, hull);         // belly
                    b.Box(new Vector3(0f, -0.02f, 0.4f), new Vector3(0.18f, 0.1f, 0.08f), c.danger);         // eye
                }
                bodyRenderers = new Renderer[] { EnemyKit.MeshObj("Hull", body, b.ToMesh(lantern ? "LanternHull" : "DroneHull"), droneMat) };
            }
            var beam = lantern ? EnemyKit.Child("Beam", root.transform, new Vector3(0f, -0.3f, 0f)) : root.transform;
            var eye = EnemyKit.Child("Eye", beam, new Vector3(0f, 0f, lantern ? 0.2f : 0.44f));
            eye.localRotation = Quaternion.Euler(lantern ? LanternBeam.Pitch : 12f, 0f, 0f);
            var cone = AddCone(eye, coneAngle, coneRange, false, false);

            var hijack = lantern ? null : root.AddComponent<HijackedDrone>();
            var patrol = root.AddComponent<DronePatrol>();
            patrol.waypoints = wps;
            patrol.phase = phase;
            patrol.speed = speed;
            patrol.cone = cone;
            patrol.body = body;
            patrol.colliders = new Collider[] { col };
            patrol.hijack = hijack;
            if (hijack)
            {
                hijack.patrol = patrol;
                hijack.cone = cone;
                hijack.bodyRenderers = bodyRenderers;
                hijack.normalMat = droneMat;
                hijack.hijackMat = EnemyKit.Mat("mat.hijacked", Cyan);
                hijack.colliders = new Collider[] { col };
            }
            else
            {
                var spin = root.AddComponent<LanternBeam>();
                spin.beam = beam;
                spin.phase = phase;
            }

            float d = phase * patrol.Speed;
            Vector3 p = DronePatrol.Evaluate(wps, d), ahead = DronePatrol.Evaluate(wps, d + 0.1f) - p;
            ahead.y = 0f;
            root.transform.SetPositionAndRotation(p, ahead.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(ahead) : Quaternion.identity);
            return root;
        }

        /// ICE vent on the floor. Spawns pooled ICE hunters while the player is in SOURCE (pool prewarmed here).
        public static GameObject IceVent(Transform parent, Vector3 pos)
        {
            var c = GameConfig.I;
            var root = EnemyKit.Root("IceVent", parent, pos, 0f);
            var vent = root.AddComponent<IceVent>();
            vent.glow = VentBody(root.transform);
            vent.pool = new IceHunter[Mathf.Clamp(c.iceMaxAlive, 1, 6)];
            for (int i = 0; i < vent.pool.Length; i++) vent.pool[i] = BuildHunter<IceHunter>(root.transform, i, false);
            return root;
        }

        /// The ICE SWARM reserve hunter, built by the vent itself on its first frame under that mutator (keeps classic
        /// room contents unchanged).
        internal static IceHunter SwarmHunter(Transform vent) => BuildHunter<IceHunter>(vent, 9, false);

        /// Tracker ICE nest (spec enemy 5) on an ICE marker: after iceSpawnInterval of SOURCE time it releases one tracker
        /// that walks the player's SOURCE breadcrumb trail (last 6 s) at 4.0 m/s. Same vent body, the tracker has its own
        /// silhouette (needle + orbiting shards, fast spin, pulse) in the ICE colour.
        public static GameObject TrackerIce(Transform parent, Vector3 pos)
        {
            var root = EnemyKit.Root("TrackerNest", parent, pos, 0f);
            var nest = root.AddComponent<TrackerNest>();
            nest.glow = VentBody(root.transform);
            nest.hunter = BuildHunter<TrackerHunter>(root.transform, 0, true);
            return root;
        }

        /// Floor grate (model.vent or procedural) + the hidden wind-up glow ring.
        static Renderer VentBody(Transform root)
        {
            var c = GameConfig.I;
            var world = EnemyKit.World;
            if (!EnemyKit.Model("model.vent", root, 1.3f, world, true, out _))
            {
                var b = new MeshBuilder();
                b.Box(new Vector3(0f, 0.03f, 0f), new Vector3(1.4f, 0.06f, 1.4f), EnemyKit.Charcoal);
                for (int i = -2; i <= 2; i++) b.Box(new Vector3(i * 0.24f, 0.075f, 0f), new Vector3(0.07f, 0.05f, 1.15f), EnemyKit.Navy);
                b.Box(new Vector3(0f, 0.065f, 0.66f), new Vector3(1.4f, 0.03f, 0.06f), c.danger);
                b.Box(new Vector3(0f, 0.065f, -0.66f), new Vector3(1.4f, 0.03f, 0.06f), c.danger);
                EnemyKit.MeshObj("Grate", root, b.ToMesh("VentGrate"), world);
            }
            var gb = new MeshBuilder();
            gb.Ring(0f, 0.7f, 24, 0.11f, Color.white);
            var glow = EnemyKit.MeshObj("Glow", root, gb.ToMesh("VentGlow", false), EnemyKit.Mat("mat.ice", c.danger));
            glow.enabled = false;
            return glow;
        }

        /// shape (optional) replaces the hunter crystal (Splitter ICE and its minis); tracker = the Tracker ICE needle.
        static T BuildHunter<T>(Transform parent, int i, bool tracker, Mesh shape = null) where T : IceHunter
        {
            var c = GameConfig.I;
            var go = new GameObject((tracker ? "TRACKER_" : shape ? shape.name + "_" : "ICE_") + i);
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.layer = GameLayers.Ice;
            var agent = go.AddComponent<NavMeshAgent>();
            agent.speed = c.iceSpeed;
            agent.acceleration = 40f;
            agent.angularSpeed = 720f;
            agent.radius = IceHunter.Radius;
            agent.height = 1.3f;
            agent.stoppingDistance = 0f;
            agent.autoBraking = false;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            agent.avoidancePriority = 40 + i;
            agent.enabled = false;

            var visual = EnemyKit.Child("Crystal", go.transform, new Vector3(0f, 0.12f, 0f));
            var iceMat = EnemyKit.Mat("mat.ice", c.danger);
            if (shape) EnemyKit.MeshObj("Shard", visual, shape, iceMat);
            else if (tracker)
            {
                var b = new MeshBuilder();
                uint s = 0x7AC4u;
                b.Bipyramid(new Vector3(0f, 0.75f, 0f), Quaternion.identity, 4, 0.13f, 0.75f, 0.6f, Color.white, ref s, 0.15f);   // needle
                for (int k = 0; k < 6; k++)
                {
                    var q = Quaternion.Euler(0f, 60f * k, 0f);
                    b.Bipyramid(q * new Vector3(0f, 0.7f, 0.45f), q * Quaternion.Euler(90f, 0f, 0f), 3, 0.06f, 0.2f, 0.2f, Color.white, ref s, 0.3f);
                }
                EnemyKit.MeshObj("Shard", visual, b.ToMesh("TrackerCrystal"), iceMat);
            }
            else if (!EnemyKit.Model("model.ice", visual, 1.1f, iceMat, true, out _))
            {
                var b = new MeshBuilder();
                uint s = 0x1CEu + (uint)i * 7919u;
                b.Bipyramid(new Vector3(0f, 0.55f, 0f), Quaternion.identity, 6, 0.26f, 0.6f, 0.5f, Color.white, ref s, 0.6f);
                b.Bipyramid(new Vector3(0.2f, 0.42f, 0.05f), Quaternion.Euler(0f, 0f, -35f), 4, 0.1f, 0.42f, 0.2f, Color.white, ref s, 0.5f);
                b.Bipyramid(new Vector3(-0.16f, 0.5f, -0.08f), Quaternion.Euler(20f, 0f, 30f), 4, 0.09f, 0.36f, 0.2f, Color.white, ref s, 0.5f);
                EnemyKit.MeshObj("Shard", visual, b.ToMesh("IceCrystal"), iceMat);
            }
            var trailT = EnemyKit.Child("Trail", go.transform, new Vector3(0f, 0.6f, 0f));
            var trail = trailT.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.35f;
            trail.minVertexDistance = 0.1f;
            trail.widthMultiplier = 0.45f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.startColor = c.danger;
            trail.endColor = new Color(c.danger.r, c.danger.g, c.danger.b, 0f);
            trail.sharedMaterial = EnemyKit.Mat("mat.ice.trail", c.danger, true);
            trail.autodestruct = false;
            EnemyKit.NoShadows(trail);

            var h = go.AddComponent<T>();
            h.agent = agent;
            h.trail = trail;
            h.visual = visual;
            h.renderers = go.GetComponentsInChildren<Renderer>(true);
            return h;
        }

        /// Hackable terminal. room = 0..5 (sets prompt length + mirrored/scrambled SOURCE modes), storyIndex into GameConfig.terminalLines.
        public static GameObject Terminal(Transform parent, Vector3 pos, float yaw, int room, int storyIndex)
        {
            var root = EnemyKit.Root("Terminal", parent, pos, yaw);
            var tmp = Console(root.transform, new Vector2(0.72f, 0.46f), 1.05f, 0.7f, "model.terminal", Magenta, 1.6f);
            var t = root.AddComponent<Terminal>();
            t.room = room;
            t.storyIndex = storyIndex;
            t.seed = 1 + room * 1009 + storyIndex * 7919;
            t.screen = tmp;
            t.title = "HALCYON//NODE-" + (storyIndex + 1).ToString("00");
            tmp.text = "<size=55%><color=#00F0FF>" + t.title + "</color></size>\n<size=50%>[E] JACK IN</size>";
            return root;
        }

        /// Hijack pad linked to a drone created by Drone(). Hacking it turns that drone into a HijackedDrone for GameConfig.hijackDuration.
        public static GameObject HijackPad(Transform parent, Vector3 pos, float yaw, GameObject drone, int room)
        {
            var root = EnemyKit.Root("HijackPad", parent, pos, yaw);
            var tmp = Console(root.transform, new Vector2(0.52f, 0.34f), 0.95f, 0.36f, null, Cyan, 1.2f);
            var rb = new MeshBuilder();
            rb.Ring(0.62f, 0.8f, 32, 0.02f, Color.white);
            EnemyKit.MeshObj("PadRing", root.transform, rb.ToMesh("PadRing", false), EnemyKit.Mat("mat.hijacked", Cyan));
            var pad = root.AddComponent<HijackPad>();
            pad.room = room;
            pad.seed = 31337 + room * 1009 + EnemyKit.Hash(pos) % 997;
            pad.screen = tmp;
            pad.title = "DRONE LINK";
            pad.drone = drone ? drone.GetComponent<HijackedDrone>() : null;
            tmp.text = "<size=55%><color=#00F0FF>DRONE LINK</color></size>\n<size=50%>[E] JACK IN</size>";
            return root;
        }

        /// Shared console body: pedestal (WallRender, solid in RENDER), Interactable look-at trigger, angled screen + TMP.
        static TextMeshPro Console(Transform root, Vector2 screen, float height, float width, string modelKey, Color trim, float fontSize)
        {
            var world = EnemyKit.World;
            float screenY = height + screen.y * 0.35f, screenZ = 0.04f;
            Bounds mb = default;
            GameObject model = modelKey != null ? EnemyKit.Model(modelKey, root, 1.15f, world, true, out mb) : null;
            if (model)
            {
                screenZ = mb.max.z + 0.03f;
                screenY = Mathf.Max(screenY, mb.max.y - screen.y * 0.4f);
            }
            else
            {
                var b = new MeshBuilder();
                b.Box(new Vector3(0f, height * 0.5f, -0.05f), new Vector3(width, height, 0.4f), EnemyKit.Navy);
                b.Box(new Vector3(0f, 0.03f, -0.05f), new Vector3(width + 0.1f, 0.06f, 0.5f), trim);
                b.Box(new Vector3(0f, height - 0.02f, 0.15f), new Vector3(width + 0.02f, 0.03f, 0.03f), trim);
                b.Box(new Vector3(0f, screenY, 0f), new Vector3(screen.x + 0.08f, screen.y + 0.08f, 0.06f), Quaternion.Euler(-20f, 0f, 0f), EnemyKit.Charcoal);
                b.Box(new Vector3(0f, (height + screenY) * 0.5f, -0.06f), new Vector3(0.12f, screenY - height + 0.1f, 0.08f), EnemyKit.Charcoal);
                EnemyKit.MeshObj("Body", root, b.ToMesh("ConsoleBody"), world);
            }
            float top = screenY + screen.y * 0.5f + 0.05f;
            EnemyKit.Box("Solid", root, new Vector3(0f, height * 0.5f, -0.05f), new Vector3(width, height, 0.4f), GameLayers.WallRender);
            EnemyKit.Box("HackTarget", root, new Vector3(0f, top * 0.5f, 0f), new Vector3(Mathf.Max(width, screen.x) + 0.3f, top, 0.75f),
                         GameLayers.Interactable, true);

            var st = EnemyKit.Child("Screen", root, new Vector3(0f, screenY, screenZ));
            st.localRotation = Quaternion.Euler(-20f, 0f, 0f);
            float hx = screen.x * 0.5f, hy = screen.y * 0.5f;
            var q = new MeshBuilder();
            q.Quad(new Vector3(hx, -hy, 0f), new Vector3(-hx, -hy, 0f), new Vector3(-hx, hy, 0f), new Vector3(hx, hy, 0f), Vector3.forward, Color.white);
            EnemyKit.MeshObj("Glass", st, q.ToMesh("ScreenGlass", false), EnemyKit.Mat("mat.screen", new Color(0.02f, 0.05f, 0.09f)));
            return EnemyKit.Text("Text", st, new Vector3(0f, 0f, 0.006f), screen * 0.92f, fontSize, "font.term", Color.white);
        }

        /// Floor glitch volume with a visible countdown ring; force-flips the player when it expires.
        public static GameObject GlitchZone(Transform parent, Vector3 pos, float radius)
        {
            var root = EnemyKit.Root("GlitchZone", parent, pos, 0f);
            var mat = EnemyKit.Mat("mat.glitch", new Color(0f, 1f, 0.4f, 0.5f), true);
            var edge = new MeshBuilder();
            edge.Ring(radius - 0.07f, radius, 48, 0.025f, Color.white);
            EnemyKit.MeshObj("Edge", root.transform, edge.ToMesh("GlitchEdge", false), mat);
            var ringT = EnemyKit.Child("Countdown", root.transform, Vector3.zero);
            var ring = new MeshBuilder();
            ring.Ring(Mathf.Max(0.05f, radius - 0.35f), radius - 0.1f, 48, 0.045f, Color.white);
            EnemyKit.MeshObj("Ring", ringT, ring.ToMesh("GlitchRing", false), mat);
            var z = root.AddComponent<GlitchZone>();
            z.radius = radius;
            z.countdownRing = ringT;
            return root;
        }

        /// Generated-room glitch zone (spec: force-flip after 1.0 s inside, 0.5 s screen-tear telegraph).
        public static GameObject GlitchZone(Transform parent, Vector3 pos, float radius, float delay = 1f, float telegraph = 0.5f)
        {
            var root = GlitchZone(parent, pos, radius);
            var z = root.GetComponent<GlitchZone>();
            z.delay = delay;
            z.telegraph = telegraph;
            return root;
        }

        /// Room exit. Inactive (visibly locked) until GameEvents.ExitOpened; on player enter raises the room-clear flow.
        public static GameObject ExitPortal(Transform parent, Vector3 pos, float yaw)
        {
            const float W = 2.4f, H = 3f, T = 0.25f, D = 0.4f;
            var c = GameConfig.I;
            var root = EnemyKit.Root("ExitPortal", parent, pos, yaw);
            var b = new MeshBuilder();
            b.Box(new Vector3(-W * 0.5f, H * 0.5f, 0f), new Vector3(T, H, D), EnemyKit.Navy);
            b.Box(new Vector3(W * 0.5f, H * 0.5f, 0f), new Vector3(T, H, D), EnemyKit.Navy);
            b.Box(new Vector3(0f, H + T * 0.5f, 0f), new Vector3(W + T, T, D), EnemyKit.Navy);
            b.Box(new Vector3(-W * 0.5f + T * 0.5f + 0.02f, H * 0.5f, 0f), new Vector3(0.04f, H, D + 0.02f), c.green);
            b.Box(new Vector3(W * 0.5f - T * 0.5f - 0.02f, H * 0.5f, 0f), new Vector3(0.04f, H, D + 0.02f), c.green);
            b.Box(new Vector3(0f, H - 0.02f, 0f), new Vector3(W - T, 0.04f, D + 0.02f), c.green);
            EnemyKit.MeshObj("Frame", root.transform, b.ToMesh("PortalFrame"), EnemyKit.World);
            EnemyKit.Box("PostL", root.transform, new Vector3(-W * 0.5f, H * 0.5f, 0f), new Vector3(T, H, D), GameLayers.WallRender);
            EnemyKit.Box("PostR", root.transform, new Vector3(W * 0.5f, H * 0.5f, 0f), new Vector3(T, H, D), GameLayers.WallRender);

            var surfT = EnemyKit.Child("Surface", root.transform, new Vector3(0f, H * 0.5f, 0f));
            var s = new MeshBuilder();
            s.Box(Vector3.zero, new Vector3(W - T, H, 0.04f), Color.white);
            var locked = EnemyKit.Mat("mat.portal.locked", new Color(0.5f, 0.05f, 0.35f, 0.5f), true);
            var surface = EnemyKit.MeshObj("Field", surfT, s.ToMesh("PortalField", false), locked);
            var label = EnemyKit.Text("Label", root.transform, new Vector3(0f, H + 0.6f, 0.1f), new Vector2(3.2f, 0.6f), 4f, "font.hud", Magenta);
            label.text = "EXIT // LOCKED";

            var p = root.AddComponent<ExitPortal>();
            p.surface = surface;
            p.lockedMat = locked;
            p.openMat = EnemyKit.Mat("mat.portal", new Color(0f, 1f, 0.4f, 0.6f), true);
            p.label = label;
            p.halfWidth = W * 0.5f - 0.1f;
            return root;
        }

        /// Room 6 boss: giant wireframe face above the core, gaze cone sweeping BOTH layers, puppet strings, finale sequence.
        /// pos = the core floor point (face hung FaceHeight above it); if pos.y >= 3 it is taken as the face position itself.
        /// Needs open sky between the face and the floor (no ceiling / Floor collider over the core) or the gaze sees nothing.
        public static GameObject Architect(Transform parent, Vector3 pos)
        {
            var c = GameConfig.I;
            float lift = pos.y < 3f ? FaceHeight : 0f;
            var root = EnemyKit.Root("Architect", parent, pos, 0f);
            EnsureTraceSystem(root.transform);
            var pivot = EnemyKit.Child("HeadPivot", root.transform, Vector3.up * lift);
            var archMat = EnemyKit.Mat("mat.architect", c.green);

            var face = EnemyKit.Child("Face", pivot, Vector3.zero);
            if (!EnemyKit.Model("model.head", face, FaceSize, archMat, false, out _))
            {
                var r = EnemyKit.MeshObj("Head", face, MeshBuilder.Head(Color.white), archMat);
                r.transform.localScale = Vector3.one * FaceSize;
                r.transform.localPosition = Vector3.down * (FaceSize * 0.5f);
            }

            float drop = Mathf.Max(lift > 0f ? lift : pos.y, 4f);
            float basePitch = Mathf.Atan2(drop, 5f) * Mathf.Rad2Deg;
            var tilt = EnemyKit.Child("GazeTilt", pivot, new Vector3(0f, -FaceSize * 0.12f, FaceSize * 0.36f));
            tilt.localRotation = Quaternion.Euler(basePitch, 0f, 0f);
            var gaze = AddCone(tilt, 32f, Mathf.Sqrt(drop * drop + 81f) + 3f, false, true);

            var strings = EnemyKit.Child("Strings", pivot, Vector3.zero);
            var sm = EnemyKit.Mat("mat.strings", c.green, true);
            for (int i = 0; i < 6; i++)
            {
                float a = 2f * Mathf.PI * i / 6f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var lr = EnemyKit.Child("String" + i, strings, Vector3.zero).gameObject.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.positionCount = 2;
                lr.SetPosition(0, dir * (FaceSize * 0.3f) + Vector3.up * (FaceSize * 0.35f));
                lr.SetPosition(1, dir * (FaceSize * 0.9f) + Vector3.up * 30f);
                lr.widthMultiplier = 0.05f;
                lr.sharedMaterial = sm;
                lr.startColor = lr.endColor = c.green;
                EnemyKit.NoShadows(lr);
            }
            strings.gameObject.SetActive(false);

            var shards = new Transform[5];
            var headMesh = MeshBuilder.Head(Color.white);
            for (int i = 0; i < shards.Length; i++)
            {
                var s = EnemyKit.MeshObj("BoardFace" + i, root.transform, headMesh, archMat).transform;
                s.localPosition = Vector3.up * lift;
                s.localScale = Vector3.one * ShardScale;
                s.gameObject.SetActive(false);
                shards[i] = s;
            }

            var arch = root.AddComponent<Architect>();
            arch.headPivot = pivot;
            arch.gazeTilt = tilt;
            arch.basePitch = basePitch;
            arch.gaze = gaze;
            arch.face = face.gameObject;
            arch.strings = strings.gameObject;
            arch.shards = shards;
            arch.shardScale = ShardScale;
            return root;
        }

        /// AUDITOR boss (Act 1) at the arena centre (floor level; room space Auditor.ArenaCenter of Content/bosses/auditor.json):
        /// the ceiling scanner ring with its 3 RENDER scanner heads, the P2 glitch zones (inactive) and the pillar ICE hunters
        /// (two, plus one for ICE SWARM). Terminals, uplink (exit portal), spawn and geometry come from the arena's RoomSpec.
        /// Offsets follow the parent's yaw.
        public static GameObject Auditor(Transform parent, Vector3 center)
        {
            var root = EnemyKit.Root("Auditor", parent, center, parent ? parent.eulerAngles.y : 0f);
            EnsureTraceSystem(root.transform);
            var ring = EnemyKit.Child("Ring", root.transform, Vector3.up * Boss.RingHeight);
            var world = EnemyKit.World;
            var b = new MeshBuilder();
            for (int i = 0; i < 24; i++)
            {
                var q = Quaternion.Euler(0f, 15f * i, 0f);
                b.Box(q * new Vector3(0f, 0f, Boss.RingRadius), new Vector3(0.62f, 0.16f, 0.2f), q, i % 2 == 0 ? EnemyKit.Navy : Magenta);
            }
            for (int i = 0; i < 3; i++)   // spokes to the hub
            {
                var q = Quaternion.Euler(0f, 120f * i + 60f, 0f);
                b.Box(q * new Vector3(0f, 0.05f, Boss.RingRadius * 0.5f), new Vector3(0.1f, 0.08f, Boss.RingRadius), q, EnemyKit.Charcoal);
            }
            b.Prism(new Vector3(0f, 0.1f, 0f), 6, 0.35f, 0.3f, EnemyKit.Navy, Magenta);
            EnemyKit.MeshObj("RingBody", ring, b.ToMesh("AuditorRing"), world);

            var scanners = new VisionCone[3];
            for (int i = 0; i < scanners.Length; i++)
            {
                var q = Quaternion.Euler(0f, 120f * i, 0f);
                var head = EnemyKit.Child("Scanner" + i, ring, q * new Vector3(0f, -0.12f, Boss.RingRadius));
                head.localRotation = q;
                var hb = new MeshBuilder();
                hb.Box(new Vector3(0f, -0.05f, 0.1f), new Vector3(0.3f, 0.26f, 0.4f), EnemyKit.Navy);
                hb.Box(new Vector3(0f, -0.08f, 0.31f), new Vector3(0.16f, 0.16f, 0.04f), Magenta);
                EnemyKit.MeshObj("Head", head, hb.ToMesh("AuditorHead"), world);
                var eye = EnemyKit.Child("Eye", head, new Vector3(0f, -0.08f, 0.34f));
                eye.localRotation = Quaternion.Euler(Boss.ScanPitch, 0f, 0f);
                scanners[i] = AddCone(eye, Boss.ScanAngle, Boss.ScanRange, false, false);
            }

            var t = root.transform;
            var zones = new GameObject[Boss.GlitchOffsets.Length];
            for (int i = 0; i < zones.Length; i++)
            {
                Vector2 o = Boss.GlitchOffsets[i];
                zones[i] = GlitchZone(t, t.TransformPoint(new Vector3(o.x, 0f, o.y)), Boss.GlitchRadius, 1f, 0.5f);
                zones[i].SetActive(false);
            }

            var hunters = new PatrolHunter[3];
            for (int i = 0; i < hunters.Length; i++)
            {
                var loop = Boss.Loops[i % Boss.Loops.Length];
                var h = BuildHunter<PatrolHunter>(t, i, false);
                h.patrol = new Vector3[loop.Length];
                for (int k = 0; k < loop.Length; k++) h.patrol[k] = t.TransformPoint(new Vector3(loop[k].x, 0f, loop[k].y));
                h.startIndex = i < Boss.Loops.Length ? 0 : loop.Length / 2;   // the ICE SWARM hunter starts opposite
                hunters[i] = h;
            }

            var a = root.AddComponent<Boss>();
            a.ring = ring;
            a.scanners = scanners;
            a.glitchZones = zones;
            a.hunters = hunters;
            return root;
        }

        /// GAZE run mutator: an Architect-style gaze (both layers) looking down-out from `at`, turning on `pivot` (RoomHazards).
        internal static VisionCone Gaze(Transform parent, Vector3 at, float pitch, float angle, float range, out Transform pivot)
        {
            pivot = EnemyKit.Child("Gaze", parent, Vector3.zero);
            pivot.position = at;
            var tilt = EnemyKit.Child("Tilt", pivot, Vector3.zero);
            tilt.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            return AddCone(tilt, angle, range, false, true);
        }

        /// Wall billboard (HALCYON propaganda); in the finale it shows a board member's face. Faces +Z of yaw.
        public static GameObject Billboard(Transform parent, Vector3 pos, float yaw, Vector2 size)
        {
            var c = GameConfig.I;
            var root = EnemyKit.Root("Billboard", parent, pos, yaw);
            float hx = size.x * 0.5f, hy = size.y * 0.5f;
            var q = new MeshBuilder();
            q.Quad(new Vector3(hx, -hy, 0f), new Vector3(-hx, -hy, 0f), new Vector3(-hx, hy, 0f), new Vector3(hx, hy, 0f), Vector3.forward, Color.white);
            EnemyKit.MeshObj("Poster", root.transform, q.ToMesh("Poster"), EnemyKit.Mat("mat.poster", EnemyKit.Navy));

            int h = EnemyKit.Hash(pos);
            string slogan = Slogans[h % Slogans.Length];
            // mat.poster carries the HALCYON emblem in its top two-thirds: the slogan sits in the empty lower panel
            var text = EnemyKit.Text("Text", root.transform, new Vector3(0f, -hy * 0.62f, 0.03f), new Vector2(size.x * 0.86f, size.y * 0.3f),
                                     size.y * 2f, "font.title", Color.white);
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.2f;
            text.fontSizeMax = size.y * 3f;
            text.text = slogan;

            var face = EnemyKit.Child("Face", root.transform, new Vector3(0f, -hy * 0.15f, 0.35f));
            float fs = Mathf.Min(size.x, size.y) * 0.75f;
            var fr = EnemyKit.MeshObj("Head", face, MeshBuilder.Head(Color.white), EnemyKit.Mat("mat.architect", c.green));
            fr.transform.localScale = Vector3.one * fs;
            fr.transform.localPosition = Vector3.down * (fs * 0.45f);
            face.gameObject.SetActive(false);

            var bb = root.AddComponent<Billboard>();
            bb.text = text;
            bb.face = face.gameObject;
            bb.slogan = slogan;
            bb.corrupt = Corrupt(slogan, (uint)h + 1u);
            return root;
        }

        // ---------- shared pieces ----------

        static VisionCone AddCone(Transform eye, float angle, float range, bool blindable, bool bothLayers)
        {
            var cone = eye.gameObject.AddComponent<VisionCone>();
            cone.angle = angle;
            cone.range = range;
            cone.blindable = blindable;
            cone.bothLayers = bothLayers;
            cone.normalMat = EnemyKit.Mat("mat.cone", new Color(1f, 0.35f, 0.85f, 0.12f), true);
            cone.traceMat = EnemyKit.Mat("mat.cone.trace", new Color(1f, 0.15f, 0.2f, 0.18f), true);
            cone.coneRenderer = EnemyKit.MeshObj("Cone", eye, MeshBuilder.Cone(angle, range), cone.normalMat);
            var l = eye.gameObject.AddComponent<Light>();
            l.type = LightType.Spot;
            l.spotAngle = angle;
            l.innerSpotAngle = angle * 0.6f;
            l.range = range * 1.3f;
            l.intensity = 2.5f;
            l.shadows = LightShadows.None;
            l.color = new Color(1f, 0.55f, 0.9f);
            cone.spot = l;
            return cone;
        }

        static void EnsureTraceSystem(Transform anyInRoom)
        {
            foreach (var r in anyInRoom.gameObject.scene.GetRootGameObjects())
                if (r.GetComponentInChildren<TraceSystem>(true)) return;
            BuildTraceSystem(anyInRoom.parent);
        }

        /// The room's TraceSystem + LKP marker (mat.lkp). Called once per room by the sensor factories, or lazily at runtime.
        internal static TraceSystem BuildTraceSystem(Transform parent)
        {
            var go = new GameObject("TraceSystem");
            go.transform.SetParent(parent, false);
            var ts = go.AddComponent<TraceSystem>();
            var mat = EnemyKit.Mat("mat.lkp", GameConfig.I.cyan);
            var marker = EnemyKit.Child("LKP", go.transform, Vector3.zero);
            var spin = EnemyKit.Child("Spin", marker, Vector3.up * 1.1f);
            uint s = 7u;
            var d = new MeshBuilder();
            d.Bipyramid(Vector3.zero, Quaternion.identity, 4, 0.22f, 0.35f, 0.35f, Color.white, ref s, 0f);
            EnemyKit.MeshObj("Diamond", spin, d.ToMesh("LkpDiamond"), mat);
            var r = new MeshBuilder();
            r.Ring(0.45f, 0.6f, 32, 0.03f, Color.white);
            r.Box(new Vector3(0f, 1.3f, 0f), new Vector3(0.03f, 2.6f, 0.03f), Color.white);
            EnemyKit.MeshObj("Ring", marker, r.ToMesh("LkpRing", false), mat);
            marker.gameObject.SetActive(false);
            ts.lkpMarker = marker;
            ts.lkpSpin = spin;
            return ts;
        }

        static string Corrupt(string s, uint seed)
        {
            const string noise = "#%&@$!?/\\|<>*01";
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (char.IsLetter(chars[i]) && MeshBuilder.Rand(ref seed) < 0.45f)
                    chars[i] = noise[(int)(MeshBuilder.Rand(ref seed) * noise.Length) % noise.Length];
            return new string(chars);
        }

        // Bosses (M3) ------------------------------------------------------------------------------------------------------

        /// COMPILER boss (Act 2) at the arena centre (floor level; room space Compiler.ArenaCenter of Content/bosses/compiler.json):
        /// the core scanner ring, the two swap-wall groups around the 4 node cells, two ICE on the core loop, the compiled
        /// Splitter ICE pool, the P3 void panels and the layer exit. Terminals, spawn and static geometry come from the arena's RoomSpec;
        /// its exit portal is switched off. Offsets follow the parent's yaw.
        public static GameObject Compiler(Transform parent, Vector3 center) => global::Desync.Enemies.Compiler.Build(parent, center);

        /// ARCHITECT boss (Act 3) at its face block (floor level; room space ArchitectBoss.ArenaCenter of
        /// Content/bosses/architect.json): face, gaze, puppet strings to every Terminal already under `parent`, the final
        /// string and the two P2 Daemons. Build it after the arena's terminals; its exit portal is switched off.
        public static GameObject ArchitectBoss(Transform parent, Vector3 center) => global::Desync.Enemies.ArchitectBoss.Build(parent, center);

        internal static T BossHunter<T>(Transform parent, int i, bool tracker) where T : IceHunter => BuildHunter<T>(parent, i, tracker);
        internal static VisionCone BossCone(Transform eye, float angle, float range) => AddCone(eye, angle, range, false, false);
        internal static void BossTraceSystem(Transform anyInRoom) => EnsureTraceSystem(anyInRoom);
        // end Bosses (M3)
    }
}
