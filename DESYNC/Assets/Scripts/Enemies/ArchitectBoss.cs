using UnityEngine;

namespace Desync.Enemies
{
    /// ARCHITECT, the Act 3 boss (EnemyFactory.ArchitectBoss at the face block of Content/bosses/architect.json). The classic
    /// Room 6 Architect is a different component and stays as it is. A wireframe face sits on a solid plinth; its gaze turns
    /// SweepHalf deg either side of south and sees both layers (walls and solids block it, like any sight line):
    ///   P1  in SOURCE the gaze kills after KillDwell s (DeathCause.Architect, killer "ARCHITECT"); in RENDER every TRACE window
    ///       (Mods.TraceWindow, 0.5 s) under it is +1 TRACE, the third being the alarm. Hack the 3 puppet-string terminals
    ///       behind it; each one severs its string (BossEvents.StringSevered; strings show in SOURCE).
    ///   P2  (first string severed) the gaze turns P2Sweep x faster; the remaining terminals' prompts are scrambled in both
    ///       layers (glyph noise, the true arrows flash every HackStation.FlashPeriod); two Daemons take their posts.
    ///   P3  (all strings, or ExitOpened) "Desync": both layers on screen at once (global _Desync for the post-FX split: left
    ///       RENDER, right SOURCE) and melt +3 %/s in both layers (BossEvents.MeltSource/MeltRender); survive SurviveTime s.
    ///       Then the final string ties the face to you and for FinalTime s the gaze shuts for GapTime s every GapPeriod s
    ///       (first after GapLead s; the HUD warns GapWarn s ahead from GapIn): a flip in a gap severs it -> Exposed; a flip
    ///       outside one is +1 TRACE; no flip in a gap before the timer ends -> Merged. Either ending raises
    ///       BossEvents.Ending, then ExitReached (the room's results); phase 4.
    /// Heat (EnemyFlags.BossHeat): the gaze turns HeatSweep x faster and the gaps last HeatGapTime s.
    /// The factory switches the arena's exit portal off: in the arena data it only gives the validator an exit.
    public class ArchitectBoss : MonoBehaviour, IResettable
    {
        public const float SweepHalf = 70f, SweepPeriod = 9f, P2Sweep = 1.25f, HeatSweep = 1.3f, GazeAngle = 30f, GazeRange = 22f,
                           GazePitch = 6f, EyeHeight = 2.4f, EyeForward = 1.3f, FaceSize = 3f, KillDwell = 0.25f,
                           SurviveTime = 30f, FinalTime = 10f, GapLead = 2f, GapPeriod = 3f, GapTime = 0.4f, HeatGapTime = 0.3f,
                           GapWarn = 0.6f, MeltBonus = 3f;
        public const int Terminals = 3;
        public const string Id = "architect", KillerId = "ARCHITECT";

        /// True ending: Flow sets it as an ARCHITECT room starts, once the player has earned it. In the final window a third
        /// way opens: a glyph behind the face (DesyncAt, boss space); touching it in either layer ends the fight as DesyncEnding.
        public static bool OfferDesync;
        public const string DesyncEnding = "Desync";
        public const float DesyncRadius = 0.8f;
        public static readonly Vector3 DesyncAt = new Vector3(0f, 1.2f, 2.75f);

        /// The face block's centre in room space (Content/bosses/architect.json, 48 x 48 tiles: x east, z = -south). Offsets
        /// are (x east, z north) from it and follow the parent's yaw; the tile map marks the Daemon posts 'I'.
        public static readonly Vector2 ArenaCenter = new Vector2(12f, -7f);
        public static readonly Vector2[] Posts = { new Vector2(-8.75f, -5.25f), new Vector2(8.75f, -5.25f) };

        static readonly int DesyncId = Shader.PropertyToID("_Desync");
        const string Arrows = "↑→↓←", Noise = "#%&@$?!/\\|<>=+*0123456789";

        public static ArchitectBoss Current;

        public Transform gazePivot;        // turns (yaw) about the eye
        public Transform eye;              // sight origin, looks along +Z, pitched GazePitch down
        public MeshRenderer gazeCone;
        public Material gazeMat, gazeTraceMat;
        public Terminal[] terminals;       // the puppet-string terminals; strings[i] runs to terminals[i]
        public LineRenderer[] strings;     // one per terminal, then the final string (face -> player)
        public Daemon[] daemons;           // one per post, inactive until P2
        public Transform desyncGlyph;      // the third way, shown through the final window when OfferDesync

        public int Phase { get; private set; } = 1;
        public int StringsLeft => Mathf.Max(0, Terminals - _hacked);
        /// Degrees either side of south the gaze points at now.
        public float GazeYaw { get; private set; }
        public bool Seeing => _seen;
        /// P3: seconds left to survive (-1 before P3), then seconds left in the final window (-1 before it starts).
        public float SurviveRemaining { get; private set; } = -1f;
        public float FinalRemaining { get; private set; } = -1f;
        /// Final window: the gaze is shut now / seconds until it next shuts (-1 when no gap is left).
        public bool GapOpen { get; private set; }
        public float GapIn { get; private set; } = -1f;
        public string EndingName { get; private set; }

        float _last, _sweep, _dwell, _finalStart;
        bool _seen, _dwellSource;
        int _hacked;
        bool[] _severed;
        int[] _drawKey;
        readonly char[] _buf = new char[512];
        int _len;

        void OnEnable()
        {
            Current = this;
            GameEvents.AccessGranted += OnAccessGranted;
            GameEvents.ExitOpened += OnExitOpened;
            GameEvents.Flipped += OnFlipped;
            LayerState.Changed += OnLayer;
        }

        void OnDisable()
        {
            if (Current == this) Current = null;
            GameEvents.AccessGranted -= OnAccessGranted;
            GameEvents.ExitOpened -= OnExitOpened;
            GameEvents.Flipped -= OnFlipped;
            LayerState.Changed -= OnLayer;
            BossEvents.SetMelt(0f, 0f);
            Shader.SetGlobalFloat(DesyncId, 0f);
        }

        bool[] Severed => _severed ??= new bool[strings != null ? strings.Length : 0];

        void OnAccessGranted(string _)
        {
            _hacked++;
            for (int i = 0; terminals != null && i < terminals.Length && i < Severed.Length; i++)
                if (terminals[i] && terminals[i].Done && !Severed[i])
                {
                    Severed[i] = true;
                    BossEvents.StringSevered?.Invoke(i);
                }
            Enter(_hacked >= Terminals ? 3 : 2);
            ShowStrings();
        }

        void OnExitOpened() => Enter(3);

        void OnLayer(bool _) => ShowStrings();

        void Enter(int phase)
        {
            if (phase <= Phase || Phase >= 4) return;
            Phase = phase;
            for (int i = 0; daemons != null && i < daemons.Length; i++)
                if (daemons[i] && !daemons[i].gameObject.activeSelf)
                {
                    daemons[i].gameObject.SetActive(true);
                    GameEvents.IceSpawned?.Invoke(daemons[i].post);
                }
            if (phase == 3)
            {
                SurviveRemaining = SurviveTime;
                FinalRemaining = -1f;
                BossEvents.SetMelt(MeltBonus, MeltBonus);
                Shader.SetGlobalFloat(DesyncId, 1f);
            }
            ShowStrings();
            BossEvents.Phase?.Invoke(Id, phase);
            GameEvents.PopText?.Invoke(phase == 2 ? "THE ARCHITECT SEES YOU" : "DESYNC // SURVIVE", phase == 2 ? GameConfig.I.green : GameConfig.I.danger);
            GameEvents.ShakeRequest?.Invoke(0.3f);
        }

        void OnFlipped(bool _)
        {
            if (Phase != 3 || FinalRemaining < 0f || !GameState.Alive) return;
            if (GapOpen) { End(BossEvents.Exposed); return; }
            BossEvents.AddTrace(PlayerRef.Position);
            GameEvents.PopText?.Invoke("MISSED THE GAP", GameConfig.I.danger);
        }

        void End(string ending)
        {
            Phase = 4;
            EndingName = ending;
            if (desyncGlyph) desyncGlyph.gameObject.SetActive(false);
            GapOpen = false;
            GapIn = -1f;
            _seen = false;
            BossEvents.SetMelt(0f, 0f);
            Shader.SetGlobalFloat(DesyncId, 0f);
            if (gazeCone) gazeCone.enabled = false;
            bool exposed = ending == BossEvents.Exposed;
            if (exposed) BossEvents.StringSevered?.Invoke(Terminals);
            ShowStrings();
            BossEvents.Ending?.Invoke(ending);
            BossEvents.Phase?.Invoke(Id, 4);
            bool desync = ending == DesyncEnding;
            GameEvents.PopText?.Invoke(exposed ? "EXPOSED // THE BOARD IS ON AIR" : desync ? "DESYNC // NO LAYER HOLDS YOU" : "MERGED // YOU ARE THE ARCHITECT NOW",
                                      exposed || desync ? GameConfig.I.green : GameConfig.I.magenta);
            GameEvents.ShakeRequest?.Invoke(0.35f);
            GameEvents.ExitReached?.Invoke();
        }

        void Update()
        {
            float t = RoomClock.Time, dt = EnemyKit.Step(ref _last);
            if (Phase >= 4) return;
            _sweep += dt * (Phase >= 2 ? P2Sweep : 1f) * (EnemyFlags.BossHeat ? HeatSweep : 1f) / SweepPeriod;
            GazeYaw = SweepHalf * Mathf.Sin(2f * Mathf.PI * _sweep);
            if (gazePivot) gazePivot.localRotation = Quaternion.Euler(0f, 180f + GazeYaw, 0f);
            if (Phase == 3)
            {
                Desync(t, dt);
                if (Phase >= 4) return;
            }
            if (gazeCone && gazeCone.enabled == GapOpen) gazeCone.enabled = !GapOpen;
            Gaze(!GapOpen, dt);
        }

        void Desync(float t, float dt)
        {
            if (FinalRemaining < 0f)
            {
                SurviveRemaining = Mathf.Max(0f, SurviveRemaining - dt);
                if (SurviveRemaining > 0f) return;
                _finalStart = t;
                FinalRemaining = FinalTime;
                ShowStrings();
                bool third = OfferDesync && desyncGlyph;
                if (third) desyncGlyph.gameObject.SetActive(true);
                GameEvents.PopText?.Invoke(third ? "SEVER IT // OR WALK BEHIND THE FACE" : "FLIP IN THE GAZE GAP // SEVER IT", GameConfig.I.green);
            }
            float u = t - _finalStart;
            FinalRemaining = Mathf.Max(0f, FinalTime - u);
            float g = u - GapLead;
            int k = Mathf.FloorToInt(g / GapPeriod);
            GapOpen = g >= 0f && g - k * GapPeriod < (EnemyFlags.BossHeat ? HeatGapTime : GapTime);
            GapIn = GapOpen ? 0f : (k + 1) * GapPeriod - g;
            if (GapIn > FinalRemaining) GapIn = -1f;
            var fin = FinalString;
            if (fin && PlayerRef.Chest) fin.SetPosition(1, transform.InverseTransformPoint(PlayerRef.Chest.position));
            if (desyncGlyph && desyncGlyph.gameObject.activeSelf)
            {
                desyncGlyph.localRotation = Quaternion.Euler(0f, u * 120f, 0f);
                if (GameState.Alive && EnemyKit.TouchesPlayer(desyncGlyph.position, DesyncRadius)) { End(DesyncEnding); return; }
            }
            if (FinalRemaining <= 0f) End(BossEvents.Merged);
        }

        LineRenderer FinalString => strings != null && strings.Length > Terminals ? strings[Terminals] : null;

        void Gaze(bool on, float dt)
        {
            bool src = LayerState.InSource;
            if (src != _dwellSource) { _dwellSource = src; _dwell = 0f; }
            bool sees = on && CanSee();
            if (sees != _seen)
            {
                _seen = sees;
                if (sees) GameEvents.Spotted?.Invoke(eye.position);
                if (gazeCone && gazeMat && gazeTraceMat) gazeCone.sharedMaterial = sees ? gazeTraceMat : gazeMat;
            }
            if (!sees) { _dwell = 0f; return; }
            _dwell += dt;
            TraceSystem.Seen(PlayerRef.Position);
            if (src)
            {
                if (_dwell < KillDwell) return;
                _dwell = 0f;
                PlayerRef.LastKillerId = KillerId;
                PlayerRef.LastKillerPos = eye.position;
                PlayerRef.Kill(DeathCause.Architect);
                return;
            }
            float window = GameState.Mods.Clamped().TraceWindow;
            if (_dwell < window) return;
            _dwell -= window;
            BossEvents.AddTrace(PlayerRef.Position);
        }

        bool CanSee()
        {
            if (!eye || GameState.DemoMode || GameState.Cinematic || !GameState.Alive || !PlayerRef.Exists) return false;
            return Sees(PlayerRef.Head) || Sees(PlayerRef.Chest);
        }

        bool Sees(Transform target)
        {
            if (!target) return false;
            Vector3 from = eye.position, d = target.position - from;
            float m = d.magnitude;
            if (m > GazeRange || Vector3.Angle(eye.forward, d) > GazeAngle * 0.5f) return false;
            return m < 1e-3f || !Physics.Raycast(from, d / m, m, EnemyKit.SightMask, QueryTriggerInteraction.Ignore);
        }

        /// Terminal strings show in SOURCE until severed; the final string shows in both layers through the final window.
        void ShowStrings()
        {
            if (strings == null) return;
            bool src = LayerState.InSource;
            for (int i = 0; i < strings.Length; i++)
            {
                if (!strings[i]) continue;
                bool on = i < Terminals ? Phase < 4 && src && !(i < Severed.Length && Severed[i]) : Phase == 3 && FinalRemaining >= 0f;
                if (strings[i].enabled != on) strings[i].enabled = on;
            }
        }

        // ---------- P2: scrambled prompts in both layers (drawn over the terminal's own screen) ----------

        void LateUpdate()
        {
            if (Phase < 2 || Phase >= 4 || terminals == null) return;
            if (_drawKey == null || _drawKey.Length != terminals.Length)
            {
                _drawKey = new int[terminals.Length];
                for (int i = 0; i < _drawKey.Length; i++) _drawKey[i] = int.MinValue;
            }
            int tick = Mathf.FloorToInt(RoomClock.Time / HackStation.NoiseStep);
            bool flash = Mathf.Repeat(RoomClock.Time, HackStation.FlashPeriod) < HackStation.FlashLength;
            for (int i = 0; i < terminals.Length; i++)
            {
                var term = terminals[i];
                if (!term || term.Done || !term.screen) continue;
                int key = ((tick * 2 + (flash ? 1 : 0)) * 64 + term.Step) * 2 + (term.Hacking ? 1 : 0);
                if (key == _drawKey[i]) continue;   // the station redraws on its own changes too, always inside a tick
                _drawKey[i] = key;
                Scramble(term, tick, flash);
            }
        }

        void Scramble(Terminal term, int tick, bool flash)
        {
            _len = 0;
            Put("<size=55%><color=#00F0FF>");
            Put(term.title);
            Put("</color></size>\n");
            int n = term.SequenceLength;
            for (int k = 0; k < n; k++)
            {
                if (k > 0) Put(' ');
                bool past = term.Hacking && k < term.Step;
                Put(past ? "<color=#00FF66>" : term.Hacking && k == term.Step ? "<color=#FFFFFF>" : "<color=#8A93B8>");
                bool reveal = ExploitFlags.HallOfMirrors && term.Hacking && k == term.Step;   // Hall of Mirrors: the current arrow reads true
                if (past || flash || reveal) Put(Arrows[term.Expected(k)]);
                else Put(Noise[(int)(((uint)(tick * 31 + k * 17 + term.seed) * 2654435761u) >> 7) % Noise.Length]);
                Put("</color>");
            }
            Put("\n<size=50%><color=#00FF66>STRING SCRAMBLED // READ THE FLASH</color></size>");
            term.screen.SetCharArray(_buf, 0, _len);
        }

        void Put(string s) { for (int i = 0; i < s.Length && _len < _buf.Length; i++) _buf[_len++] = s[i]; }
        void Put(char c) { if (_len < _buf.Length) _buf[_len++] = c; }

        public void ResetState()
        {
            Phase = 1;
            SurviveRemaining = FinalRemaining = GapIn = -1f;
            GapOpen = _seen = false;
            EndingName = null;
            _hacked = 0;
            GazeYaw = _last = _sweep = _dwell = 0f;
            for (int i = 0; i < Severed.Length; i++) Severed[i] = false;
            _drawKey = null;
            if (desyncGlyph) desyncGlyph.gameObject.SetActive(false);
            BossEvents.SetMelt(0f, 0f);
            Shader.SetGlobalFloat(DesyncId, 0f);
            for (int i = 0; daemons != null && i < daemons.Length; i++) if (daemons[i]) daemons[i].gameObject.SetActive(false);
            if (gazePivot) gazePivot.localRotation = Quaternion.Euler(0f, 180f, 0f);
            if (gazeCone)
            {
                gazeCone.enabled = true;
                if (gazeMat) gazeCone.sharedMaterial = gazeMat;
            }
            ShowStrings();
        }

        // ---------- build (EnemyFactory.ArchitectBoss) ----------

        internal static GameObject Build(Transform parent, Vector3 center)
        {
            var c = GameConfig.I;
            var root = EnemyKit.Root("ArchitectBoss", parent, center, parent ? parent.eulerAngles.y : 0f);
            var t = root.transform;
            EnemyFactory.BossTraceSystem(t);
            Compiler.BossExitsOff(parent);

            var archMat = EnemyKit.Mat("mat.architect", c.green);
            var face = EnemyKit.Child("Face", t, Vector3.up * 0.3f);
            face.localRotation = Quaternion.Euler(0f, 180f, 0f);   // looks south, at the arena
            if (!EnemyKit.Model("model.head", face, FaceSize, archMat, true, out _))
                EnemyKit.MeshObj("Head", face, MeshBuilder.Head(Color.white), archMat).transform.localScale = Vector3.one * FaceSize;

            var pivot = EnemyKit.Child("Gaze", t, new Vector3(0f, EyeHeight, -EyeForward));
            pivot.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var eye = EnemyKit.Child("Eye", pivot, Vector3.zero);
            eye.localRotation = Quaternion.Euler(GazePitch, 0f, 0f);
            var gazeMat = EnemyKit.Mat("mat.cone", new Color(1f, 0.35f, 0.85f, 0.12f), true);
            var cone = EnemyKit.MeshObj("Cone", eye, MeshBuilder.Cone(GazeAngle, GazeRange), gazeMat);

            var terms = parent ? parent.GetComponentsInChildren<Terminal>(true) : new Terminal[0];
            var sm = EnemyKit.Mat("mat.strings", c.green, true);
            var crown = new Vector3(0f, FaceSize + 0.2f, 0f);
            var strings = new LineRenderer[terms.Length + 1];
            for (int i = 0; i < terms.Length; i++)
                strings[i] = String("String" + i, t, sm, crown, t.InverseTransformPoint(terms[i].transform.position) + Vector3.up * 1.3f);
            strings[terms.Length] = String("FinalString", t, sm, new Vector3(0f, 1.8f, -1.1f), new Vector3(0f, 1.2f, -4f));

            var daemons = new Daemon[Posts.Length];
            for (int i = 0; i < Posts.Length; i++)
            {
                daemons[i] = EnemyFactory.Daemon(t, t.TransformPoint(new Vector3(Posts[i].x, 0f, Posts[i].y))).GetComponent<Daemon>();
                daemons[i].gameObject.SetActive(false);
            }

            var glyph = EnemyKit.Child("DesyncGlyph", t, DesyncAt);   // the third way (OfferDesync), behind the face
            var gb = new MeshBuilder();
            uint gs = 0xDE5Cu;
            gb.Bipyramid(Vector3.zero, Quaternion.identity, 4, 0.3f, 0.45f, 0.45f, Color.white, ref gs, 0.2f);
            EnemyKit.MeshObj("Knot", glyph, gb.ToMesh("DesyncGlyph", false), sm);
            glyph.gameObject.SetActive(false);

            var boss = root.AddComponent<ArchitectBoss>();
            boss.gazePivot = pivot;
            boss.eye = eye;
            boss.gazeCone = cone;
            boss.gazeMat = gazeMat;
            boss.gazeTraceMat = EnemyKit.Mat("mat.cone.trace", new Color(1f, 0.15f, 0.2f, 0.18f), true);
            boss.terminals = terms;
            boss.strings = strings;
            boss.daemons = daemons;
            boss.desyncGlyph = glyph;
            boss.ShowStrings();
            return root;
        }

        static LineRenderer String(string name, Transform parent, Material mat, Vector3 from, Vector3 to)
        {
            var lr = EnemyKit.Child(name, parent, Vector3.zero).gameObject.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.widthMultiplier = 0.05f;
            lr.sharedMaterial = mat;
            lr.startColor = lr.endColor = GameConfig.I.green;
            lr.enabled = false;
            EnemyKit.NoShadows(lr);
            return lr;
        }
    }
}
