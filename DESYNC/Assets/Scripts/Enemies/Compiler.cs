using UnityEngine;

namespace Desync.Enemies
{
    /// COMPILER, the Act 2 boss: the room recompiles (EnemyFactory.Compiler at the centre of Content/bosses/compiler.json).
    /// The arena's 4 terminals are the nodes, each inside a cell of swap walls; a scanner ring on the core (2 RENDER
    /// VisionCones, TRACE as usual) turns on RoomClock; two PatrolHunters walk the core loop in SOURCE from the first flip on.
    ///   P1  the swap walls trade solidity between the layers every SwapPeriod s (HeatSwapPeriod under EnemyFlags.BossHeat)
    ///       after a SwapTelegraph s flicker. Group 0 (the cells' N/S walls) starts solid in RENDER, group 1 (E/W) solid in
    ///       SOURCE, so every cell is open from one side in either layer. Swaps run until the boss is beaten.
    ///   P2  (<= 2 nodes left) every GRAZE compiles one Splitter ICE on the core loop (CompileWindup s wind-up; at most
    ///       CompiledMax out, a spent one goes back to the pool) and SOURCE melt rises +2 %/s (BossEvents.MeltSource,
    ///       folded into Mods by the run manager).
    ///   P3  (all nodes, or ExitOpened) the arena halves every HalvePeriod s (Regions, HalveTelegraph s of warning): outside
    ///       the live region = dead (DeathCause.Glitch, killer "COMPILER"); after the last region nothing is left. The exit
    ///       exists only in the layer you are not in: stand in it (ExitSpots[Step]) and flip into it -> ExitReached, phase 4.
    /// Swap walls are a per-group override of layer + material, never a disabled collider: solid in RENDER = WallRender
    /// (LayerState handles it, mat.world); solid in SOURCE = Default layer (green glow material), a trigger while the player
    /// is in RENDER (ghost material, not in the sight mask: the scanners see through it there). A player inside a wall that
    /// turns solid around them is pushed out by the CharacterController's overlap recovery. The factory switches the arena's
    /// exit portal off: in the arena data it only marks ExitSpots[0] for the validator.
    public class Compiler : MonoBehaviour, IResettable
    {
        public const float SwapPeriod = 6f, HeatSwapPeriod = 4f, SwapTelegraph = 1f, HalvePeriod = 10f, HalveTelegraph = 1f,
                           CompileWindup = 0.3f, ExitRadius = 1.2f, CoreTurn = 30f, ScanHeight = 2.6f, ScanRadius = 1.3f,
                           ScanPitch = 14f, ScanAngle = 40f, ScanRange = 10f, CellHalf = 2f, WallHeight = 3.5f, P2Melt = 2f;
        public const int Nodes = 4, CompiledMax = 2;   // 2 pillar ICE + 2 split Splitters = the 6-agent budget
        public const string Id = "compiler", KillerId = "COMPILER";

        /// Arena centre in room space (Content/bosses/compiler.json, 48 x 48 tiles: x east, z = -south). Offsets below are
        /// (x east, z north) from it and follow the parent's yaw; the tile map marks the loop ('I' = a hunter's start, 'P').
        public static readonly Vector2 ArenaCenter = new Vector2(12f, -12f);
        /// Node cells: a terminal at the centre, swap walls on lines CellHalf m out (0.5 m thick).
        public static readonly Vector2[] Cells =
            { new Vector2(-6.75f, 6.75f), new Vector2(6.75f, 6.75f), new Vector2(-6.75f, -6.75f), new Vector2(6.75f, -6.75f) };
        public static readonly Vector2[] Loop =
            { new Vector2(-3.25f, 3.25f), new Vector2(3.25f, 3.25f), new Vector2(3.25f, -3.25f), new Vector2(-3.25f, -3.25f) };
        /// P3 exit after k halvings (ExitSpots[0] = the arena's Exit entity).
        public static readonly Vector2[] ExitSpots =
            { new Vector2(0.25f, -8.25f), new Vector2(-6.75f, 10.25f), new Vector2(10.25f, 2.25f), new Vector2(2.25f, 10.25f) };
        /// P3 live region after k halvings as (xMin, zMin, xMax, zMax); after the last one nothing is live.
        public static readonly Vector4[] Regions =
        {
            new Vector4(-11.5f, -11.5f, 11.5f, 11.5f), new Vector4(-11.5f, 0f, 11.5f, 11.5f),
            new Vector4(0f, 0f, 11.5f, 11.5f), new Vector4(0f, 5.75f, 11.5f, 11.5f),
        };

        public static Compiler Current;

        public Transform ring;
        public VisionCone[] scanners;
        public Transform[] wallGroups;       // [0] N/S walls, [1] E/W walls: BoxCollider children
        public MeshRenderer[] wallRenderers; // one merged mesh per group
        public Material renderSolidMat, sourceSolidMat, ghostMat;
        public PatrolHunter[] hunters;
        public SplitterIce[] compiled;       // inactive until compiled; posts on the core loop
        public GameObject[] voids;           // voids[k - 1]: the area gone after k halvings
        public Transform exit;               // the P3 layer exit

        public int Phase { get; private set; } = 1;
        public int NodesLeft => Mathf.Max(0, Nodes - _hacked);
        /// Wall swaps so far this attempt; group g is solid in RENDER while (g + Swaps) is even.
        public int Swaps { get; private set; }
        /// Seconds to the next wall swap (telegraph while <= SwapTelegraph).
        public float SwapIn { get; private set; } = SwapPeriod;
        /// P3 halvings so far; HalveIn = seconds to the next one (-1 outside P3 / when nothing is left).
        public int Step { get; private set; }
        public float HalveIn { get; private set; } = -1f;
        public float Period => EnemyFlags.BossHeat ? HeatSwapPeriod : SwapPeriod;
        public bool RenderSolid(int group) => ((group + Swaps) & 1) == 0;

        readonly int[] _applied = { -1, -1 };
        readonly bool[] _out = new bool[CompiledMax];
        BoxCollider[][] _cols;
        int _hacked, _killedStep = -1;
        float _p3Start, _compileAt = -1f;

        void Awake() => IceHunter.ResetIds();

        void OnEnable()
        {
            Current = this;
            GameEvents.AccessGranted += OnAccessGranted;
            GameEvents.ExitOpened += OnExitOpened;
            GameEvents.ExitReached += OnExitReached;
            GameEvents.Grazed += OnGrazed;
            GameEvents.Flipped += OnFlipped;
        }

        void OnDisable()
        {
            if (Current == this) Current = null;
            GameEvents.AccessGranted -= OnAccessGranted;
            GameEvents.ExitOpened -= OnExitOpened;
            GameEvents.ExitReached -= OnExitReached;
            GameEvents.Grazed -= OnGrazed;
            GameEvents.Flipped -= OnFlipped;
            BossEvents.SetMelt(0f, 0f);
        }

        void OnAccessGranted(string _)
        {
            _hacked++;
            if (_hacked >= Nodes) Enter(3);
            else if (Nodes - _hacked <= 2) Enter(2);
        }

        void OnExitOpened() => Enter(3);

        void Enter(int phase)
        {
            if (phase <= Phase || Phase >= 4) return;
            Phase = phase;
            BossEvents.SetMelt(P2Melt, 0f);   // P3 keeps P2's melt
            if (phase == 3)
            {
                _p3Start = RoomClock.Time;
                Step = 0;
                HalveIn = HalvePeriod;
                if (exit)
                {
                    exit.position = Offset(ExitSpots[0]);
                    exit.gameObject.SetActive(true);
                }
            }
            BossEvents.Phase?.Invoke(Id, phase);
            GameEvents.PopText?.Invoke(phase == 2 ? "GRAZES COMPILE ICE" : "ARENA COLLAPSING // FLIP INTO THE EXIT",
                                      phase == 2 ? GameConfig.I.magenta : GameConfig.I.danger);
            GameEvents.ShakeRequest?.Invoke(0.25f);
        }

        /// The exit exists only in the other layer: flipping while standing in it is the way out.
        void OnFlipped(bool _)
        {
            if (Phase != 3 || !exit || !exit.gameObject.activeSelf || !GameState.Alive || !PlayerRef.Exists) return;
            Vector3 d = PlayerRef.Position - exit.position;
            d.y = 0f;
            if (d.sqrMagnitude <= ExitRadius * ExitRadius) GameEvents.ExitReached?.Invoke();
        }

        void OnExitReached()
        {
            if (Phase != 3) return;
            Phase = 4;
            HalveIn = -1f;
            BossEvents.SetMelt(0f, 0f);
            foreach (var s in scanners) if (s) s.Suppressed = true;
            BossEvents.Phase?.Invoke(Id, 4);
        }

        void OnGrazed(string _)
        {
            if (Phase < 2 || Phase >= 4 || _compileAt >= 0f || !Free()) return;
            _compileAt = RoomClock.Time + CompileWindup;
            GameEvents.IceWindup?.Invoke(CompileSpot);
        }

        Vector3 CompileSpot => Free() ? Free().transform.position : transform.position;

        SplitterIce Free()
        {
            for (int i = 0; compiled != null && i < compiled.Length; i++) if (compiled[i] && !compiled[i].gameObject.activeSelf) return compiled[i];
            return null;
        }

        void Compile()
        {
            _compileAt = -1f;
            var s = Free();
            if (!s || Phase >= 4) return;
            s.gameObject.SetActive(true);   // its hunter comes out at its post (the player is in SOURCE: a GRAZE just happened)
            GameEvents.IceSpawned?.Invoke(s.transform.position);
        }

        /// A compiled Splitter whose hunter was out and is gone (split, minis expired) goes back to the pool.
        void Recycle()
        {
            for (int i = 0; compiled != null && i < compiled.Length; i++)
            {
                var s = compiled[i];
                if (!s || !s.gameObject.activeSelf || !s.hunter) continue;
                if (s.hunter.gameObject.activeSelf) { _out[i] = true; continue; }
                if (!_out[i] || s.Split) continue;
                _out[i] = false;
                s.ResetState();
                s.gameObject.SetActive(false);
            }
        }

        void Update()
        {
            float t = RoomClock.Time;
            if (LayerState.InSource)
                foreach (var h in hunters)
                    if (h && !h.gameObject.activeSelf) h.Spawn(h.patrol != null && h.patrol.Length > 0 ? h.patrol[h.startIndex % h.patrol.Length] : h.transform.position);
            if (ring) ring.localRotation = Quaternion.Euler(0f, CoreTurn * t, 0f);
            if (Phase >= 4) return;
            UpdateWalls(t);
            if (_compileAt >= 0f && t >= _compileAt) Compile();
            Recycle();
            if (Phase == 3) Collapse(t);
        }

        void UpdateWalls(float t)
        {
            float p = Period;
            int swaps = Mathf.FloorToInt(t / p);
            SwapIn = (swaps + 1) * p - t;
            if (swaps != Swaps)
            {
                Swaps = swaps;
                BossEvents.WallSwap?.Invoke();
                GameEvents.ShakeRequest?.Invoke(0.1f);
            }
            bool flash = SwapIn <= SwapTelegraph && (Mathf.FloorToInt(t * 8f) & 1) == 0;   // telegraph: flicker to the coming look
            for (int g = 0; g < wallGroups.Length; g++) Apply(g, RenderSolid(g), flash);
        }

        void Apply(int g, bool renderSolid, bool flash)
        {
            bool src = LayerState.InSource;
            int key = (renderSolid ? 1 : 0) | (src ? 2 : 0);
            if (_applied[g] != key)
            {
                _applied[g] = key;
                int layer = renderSolid ? GameLayers.WallRender : GameLayers.Default;
                bool trigger = !renderSolid && !src;
                foreach (var c in Cols(g))
                {
                    c.gameObject.layer = layer;
                    c.isTrigger = trigger;
                }
            }
            var r = g < wallRenderers.Length ? wallRenderers[g] : null;
            if (!r) return;
            bool solidLook = flash ? !renderSolid : renderSolid;
            var m = solidLook ? renderSolidMat : src ? sourceSolidMat : ghostMat;
            if (m && r.sharedMaterial != m) r.sharedMaterial = m;
        }

        BoxCollider[] Cols(int g)
        {
            if (_cols == null)
            {
                _cols = new BoxCollider[wallGroups.Length][];
                for (int i = 0; i < _cols.Length; i++)
                    _cols[i] = wallGroups[i] ? wallGroups[i].GetComponentsInChildren<BoxCollider>(true) : new BoxCollider[0];
            }
            return _cols[g];
        }

        void Collapse(float t)
        {
            int last = Regions.Length;
            float u = t - _p3Start;
            int step = Mathf.Min(last, Mathf.FloorToInt(u / HalvePeriod));
            if (step != Step)
            {
                Step = step;
                for (int k = 0; k < step && k < voids.Length; k++) if (voids[k]) voids[k].SetActive(true);
                if (exit)
                {
                    if (step < last) exit.position = Offset(ExitSpots[step]);
                    else exit.gameObject.SetActive(false);
                }
                GameEvents.PopText?.Invoke(step < last ? "RECOMPILED // HALF THE ARENA IS GONE" : "COMPILED OUT", GameConfig.I.danger);
                GameEvents.ShakeRequest?.Invoke(0.3f);
            }
            HalveIn = step >= last ? -1f : (step + 1) * HalvePeriod - u;
            if (step < last && step < voids.Length && voids[step])   // the next area to go flickers
                voids[step].SetActive(HalveIn <= HalveTelegraph && (Mathf.FloorToInt(t * 8f) & 1) == 0);

            if (step == 0 || _killedStep == step || !GameState.Alive || GameState.Cinematic || !PlayerRef.Exists) return;
            if (step < last && Inside(Regions[step], BossEvents.Local(transform, PlayerRef.Position))) return;
            _killedStep = step;   // once per halving (god mode survives it)
            PlayerRef.LastKillerId = KillerId;
            PlayerRef.LastKillerPos = PlayerRef.Position;
            PlayerRef.Kill(DeathCause.Glitch);
        }

        public static bool Inside(Vector4 r, Vector2 p) => p.x >= r.x && p.x <= r.z && p.y >= r.y && p.y <= r.w;

        /// The area that goes at halving `step` (1..Regions.Length) as (xMin, zMin, xMax, zMax); the last takes what was left.
        public static Vector4 Removed(int step)
        {
            Vector4 a = Regions[step - 1];
            if (step >= Regions.Length) return a;
            Vector4 b = Regions[step];
            if (b.x > a.x) return new Vector4(a.x, a.y, b.x, a.w);   // west part
            if (b.z < a.z) return new Vector4(b.z, a.y, a.z, a.w);   // east part
            if (b.y > a.y) return new Vector4(a.x, a.y, a.z, b.y);   // south part
            return new Vector4(a.x, b.w, a.z, a.w);                   // north part
        }

        Vector3 Offset(Vector2 o) => transform.TransformPoint(new Vector3(o.x, 0f, o.y));

        public void ResetState()
        {
            IceHunter.ResetIds();
            Phase = 1;
            Swaps = Step = _hacked = 0;
            HalveIn = -1f;
            _killedStep = -1;
            _compileAt = -1f;
            _applied[0] = _applied[1] = -1;
            BossEvents.SetMelt(0f, 0f);
            foreach (var s in scanners) if (s) s.Suppressed = false;
            foreach (var v in voids) if (v) v.SetActive(false);
            if (exit) exit.gameObject.SetActive(false);
            foreach (var h in hunters) if (h) h.Despawn();
            for (int i = 0; compiled != null && i < compiled.Length; i++)
            {
                _out[i] = false;
                if (!compiled[i]) continue;
                compiled[i].ResetState();
                compiled[i].gameObject.SetActive(false);
            }
            if (ring) ring.localRotation = Quaternion.identity;
            UpdateWalls(0f);
        }

        // ---------- build (EnemyFactory.Compiler) ----------

        internal static GameObject Build(Transform parent, Vector3 center)
        {
            var c = GameConfig.I;
            var root = EnemyKit.Root("Compiler", parent, center, parent ? parent.eulerAngles.y : 0f);
            var t = root.transform;
            EnemyFactory.BossTraceSystem(t);
            BossExitsOff(parent);
            var world = EnemyKit.World;

            var ring = EnemyKit.Child("Ring", t, Vector3.up * ScanHeight);
            var rb = new MeshBuilder();
            rb.Prism(Vector3.zero, 8, ScanRadius, 0.25f, EnemyKit.Navy, c.cyan);
            EnemyKit.MeshObj("RingBody", ring, rb.ToMesh("CompilerRing"), world);
            var scanners = new VisionCone[2];
            for (int i = 0; i < scanners.Length; i++)
            {
                var q = Quaternion.Euler(0f, 180f * i, 0f);
                var head = EnemyKit.Child("Scanner" + i, ring, q * new Vector3(0f, 0f, ScanRadius));
                head.localRotation = q;
                var hb = new MeshBuilder();
                hb.Box(new Vector3(0f, 0f, 0.1f), new Vector3(0.3f, 0.26f, 0.4f), EnemyKit.Navy);
                hb.Box(new Vector3(0f, -0.03f, 0.31f), new Vector3(0.16f, 0.16f, 0.04f), c.magenta);
                EnemyKit.MeshObj("Head", head, hb.ToMesh("CompilerHead"), world);
                var eye = EnemyKit.Child("Eye", head, new Vector3(0f, -0.03f, 0.34f));
                eye.localRotation = Quaternion.Euler(ScanPitch, 0f, 0f);
                scanners[i] = EnemyFactory.BossCone(eye, ScanAngle, ScanRange);
            }

            var groups = new Transform[2];
            var rends = new MeshRenderer[2];
            for (int g = 0; g < groups.Length; g++)
            {
                groups[g] = EnemyKit.Child(g == 0 ? "SwapWallsNS" : "SwapWallsEW", t, Vector3.zero);
                var mb = new MeshBuilder();
                foreach (var cell in Cells)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector3 ctr = g == 0 ? new Vector3(cell.x, WallHeight * 0.5f, cell.y + s * CellHalf)
                                             : new Vector3(cell.x + s * CellHalf, WallHeight * 0.5f, cell.y);
                        Vector3 size = g == 0 ? new Vector3(2f * CellHalf + 0.5f, WallHeight, 0.5f) : new Vector3(0.5f, WallHeight, 2f * CellHalf - 0.5f);
                        mb.Box(ctr, size, EnemyKit.Navy);
                        mb.Box(ctr + Vector3.up * (WallHeight * 0.5f - 0.14f), size + new Vector3(0.02f, -WallHeight + 0.3f, 0.02f), g == 0 ? c.magenta : c.cyan);
                        EnemyKit.Box("Wall", groups[g], ctr, size, GameLayers.WallRender);
                    }
                rends[g] = EnemyKit.MeshObj("Mesh", groups[g], mb.ToMesh(g == 0 ? "CompilerWallsNS" : "CompilerWallsEW"), world);
            }
            var srcMat = new Material(EnemyKit.Mat("mat.drone", EnemyKit.Navy)) { name = "CompilerWall (source)" };
            srcMat.SetColor("_BaseColor", new Color(0.05f, 0.16f, 0.1f));
            srcMat.SetColor("_RimColor", new Color(c.green.r, c.green.g, c.green.b, 1.1f));
            srcMat.SetColor("_EdgeColor", c.green);

            var hunters = new PatrolHunter[2];
            for (int i = 0; i < hunters.Length; i++)
            {
                var h = EnemyFactory.BossHunter<PatrolHunter>(t, i, false);
                h.patrol = new Vector3[Loop.Length];
                for (int k = 0; k < Loop.Length; k++) h.patrol[k] = t.TransformPoint(new Vector3(Loop[k].x, 0f, Loop[k].y));
                h.startIndex = i * 2;   // opposite corners of the loop
                hunters[i] = h;
            }
            var compiled = new SplitterIce[CompiledMax];
            for (int i = 0; i < compiled.Length; i++)
            {
                var l = Loop[(i & 1) * 2];
                compiled[i] = EnemyFactory.SplitterIce(t, t.TransformPoint(new Vector3(l.x, 0f, l.y))).GetComponent<SplitterIce>();
                compiled[i].gameObject.SetActive(false);
            }

            var voidMat = EnemyKit.Mat("mat.cone.trace", new Color(1f, 0.15f, 0.2f, 0.18f), true);
            var voids = new GameObject[Regions.Length];
            for (int k = 1; k <= voids.Length; k++)
            {
                Vector4 r = Removed(k);
                var vb = new MeshBuilder();
                vb.Quad(new Vector3(r.x, 0.04f, r.y), new Vector3(r.z, 0.04f, r.y), new Vector3(r.z, 0.04f, r.w), new Vector3(r.x, 0.04f, r.w), Vector3.up, Color.white);
                voids[k - 1] = EnemyKit.MeshObj("Void" + k, t, vb.ToMesh("CompilerVoid", false), voidMat).gameObject;
                voids[k - 1].SetActive(false);
            }

            var exit = EnemyKit.Child("LayerExit", t, new Vector3(ExitSpots[0].x, 0f, ExitSpots[0].y));
            var er = new MeshBuilder();
            er.Ring(ExitRadius - 0.15f, ExitRadius, 40, 0.05f, Color.white);
            EnemyKit.MeshObj("Ring", exit, er.ToMesh("CompilerExitRing", false), EnemyKit.Mat("mat.portal", new Color(0f, 1f, 0.4f, 0.6f), true));
            var fb = new MeshBuilder();
            fb.Box(new Vector3(0f, 1.5f, 0f), new Vector3(2.2f, 3f, 0.04f), Color.white);
            EnemyKit.MeshObj("Field", exit, fb.ToMesh("CompilerExitField", false), EnemyKit.Mat("mat.portal.locked", new Color(0.5f, 0.05f, 0.35f, 0.5f), true));
            var label = EnemyKit.Text("Label", exit, new Vector3(0f, 3.25f, 0f), new Vector2(3.4f, 0.6f), 3.5f, "font.hud", c.green);
            label.text = "EXIT // FLIP INTO IT";
            exit.gameObject.SetActive(false);

            var boss = root.AddComponent<Compiler>();
            boss.ring = ring;
            boss.scanners = scanners;
            boss.wallGroups = groups;
            boss.wallRenderers = rends;
            boss.renderSolidMat = world;
            boss.sourceSolidMat = srcMat;
            boss.ghostMat = EnemyKit.Mat("mat.glitch", new Color(0f, 1f, 0.4f, 0.5f), true);
            boss.hunters = hunters;
            boss.compiled = compiled;
            boss.voids = voids;
            boss.exit = exit;
            return root;
        }

        /// The arena's exit portal only marks the boss's way out for the validator: the boss owns the real one.
        internal static void BossExitsOff(Transform parent)
        {
            if (!parent) return;
            foreach (var p in parent.GetComponentsInChildren<ExitPortal>(true)) p.gameObject.SetActive(false);
        }
    }
}
