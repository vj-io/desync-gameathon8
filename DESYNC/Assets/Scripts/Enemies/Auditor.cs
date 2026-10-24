using UnityEngine;

namespace Desync.Enemies
{
    /// AUDITOR, the Act 1 boss (built by EnemyFactory.Auditor at the centre of the arena in Content/bosses/auditor.json).
    /// A scanner ring hangs under the ceiling and turns on RoomClock; its heads are RENDER-only VisionCones (TRACE as usual).
    /// The room builder places the 3 terminals and the uplink (the arena's exit portal); this component runs the phases:
    ///   P1  ring at BaseTurn deg/s; two PatrolHunters (three under ICE SWARM) walk the pillars in SOURCE, out from the
    ///       player's first flip on.
    ///   P2  (<= 2 terminals left) ring x P2Turn; the glitch zones switch on (force-flip after 1.0 s, 0.5 s telegraph).
    ///   P3  (final terminal, or ExitOpened) a forced flip every FlipPeriod s (FlipTelegraph s of screen tear) and
    ///       UplinkTime s to reach the uplink (ExitReached -> phase 4, done). Time out: PlayerRef.Kill(TraceTimer), killer "AUDITOR".
    /// Heat variant (EnemyFlags.BossHeat): the heads also see SOURCE for HeatWindow s at the end of every HeatPeriod s.
    /// Phase changes raise EnemyEvents.AuditorPhase and a PopText. HUD reads Current.Phase / Current.UplinkRemaining.
    public class Auditor : MonoBehaviour, IResettable
    {
        public const float BaseTurn = 36f, P2Turn = 1.4f, FlipPeriod = 4f, FlipTelegraph = 0.5f, UplinkTime = 20f,
                           HeatPeriod = 6f, HeatWindow = 1f;
        public const float RingHeight = 3.2f, RingRadius = 2.5f, ScanPitch = 22f, ScanAngle = 50f, ScanRange = 7f, GlitchRadius = 1.5f;
        public const string KillerId = "AUDITOR";

        /// Arena centre in room space (Content/bosses/auditor.json, 48 x 48 tiles: x east, z = -south).
        public static readonly Vector2 ArenaCenter = new Vector2(12f, -12f);
        /// Offsets (x, z) from the centre; the arena's tile map marks them 'G' (glitch) and 'I'/'P' (hunter loops).
        public static readonly Vector2[] GlitchOffsets = { new Vector2(-3.25f, 0.25f), new Vector2(3.25f, 0.25f), new Vector2(0.25f, 3.25f) };
        public static readonly Vector2[][] Loops =
        {
            new[] { new Vector2(-7.75f, 3.75f), new Vector2(-2.75f, 3.75f), new Vector2(-2.75f, -2.75f), new Vector2(-7.75f, -2.75f) },
            new[] { new Vector2(7.75f, 3.75f), new Vector2(2.75f, 3.75f), new Vector2(2.75f, -2.75f), new Vector2(7.75f, -2.75f) },
        };

        public static Auditor Current;

        public Transform ring;
        public VisionCone[] scanners;
        public GameObject[] glitchZones;
        public PatrolHunter[] hunters;   // [0], [1] always; [2] only under ICE SWARM
        public int terminals = 3;

        public int Phase { get; private set; } = 1;
        /// Seconds left to reach the uplink in P3; -1 otherwise.
        public float UplinkRemaining { get; private set; } = -1f;
        public float Turn => BaseTurn * (Phase >= 2 ? P2Turn : 1f);
        public float RingAngle => _angle;

        readonly ForcedFlipClock _flips = new ForcedFlipClock();
        int _hacked;
        float _angle, _last;

        void Awake() => IceHunter.ResetIds();

        void OnEnable()
        {
            Current = this;
            GameEvents.AccessGranted += OnAccessGranted;
            GameEvents.ExitOpened += OnExitOpened;
            GameEvents.ExitReached += OnExitReached;
        }

        void OnDisable()
        {
            if (Current == this) Current = null;
            GameEvents.AccessGranted -= OnAccessGranted;
            GameEvents.ExitOpened -= OnExitOpened;
            GameEvents.ExitReached -= OnExitReached;
            _flips.Stop();
        }

        void OnAccessGranted(string _)
        {
            _hacked++;
            if (_hacked >= terminals) Enter(3);
            else if (terminals - _hacked <= 2) Enter(2);
        }

        void OnExitOpened() => Enter(3);

        void OnExitReached()
        {
            if (Phase != 3) return;
            Phase = 4;
            UplinkRemaining = -1f;
            _flips.Stop();
            foreach (var s in scanners) if (s) s.Suppressed = true;
            EnemyEvents.AuditorPhase?.Invoke(4);
        }

        void Enter(int phase)
        {
            if (phase <= Phase || Phase >= 4) return;
            Phase = phase;
            foreach (var z in glitchZones) if (z) z.SetActive(true);
            if (phase == 3)
            {
                UplinkRemaining = UplinkTime;
                _flips.Start(RoomClock.Time, FlipPeriod, FlipTelegraph);
            }
            EnemyEvents.AuditorPhase?.Invoke(phase);
            GameEvents.PopText?.Invoke(phase == 2 ? "AUDIT ESCALATED" : "UPLINK OPEN // 20 S", phase == 2 ? GameConfig.I.magenta : GameConfig.I.danger);
            GameEvents.ShakeRequest?.Invoke(0.25f);
        }

        void Update()
        {
            float dt = EnemyKit.Step(ref _last);
            if (LayerState.InSource)
                for (int i = 0; i < hunters.Length; i++)
                {
                    var h = hunters[i];
                    if (h && !h.gameObject.activeSelf && (i < 2 || EnemyFlags.IceSwarm)) h.Spawn(Post(h));
                }
            if (GameState.Cinematic || Phase >= 4) return;

            _angle = Mathf.Repeat(_angle + Turn * dt, 360f);
            if (ring) ring.localRotation = Quaternion.Euler(0f, _angle, 0f);
            bool heat = EnemyFlags.BossHeat && Mathf.Repeat(RoomClock.Time, HeatPeriod) >= HeatPeriod - HeatWindow;
            foreach (var s in scanners) if (s) s.bothLayers = heat;

            if (Phase != 3) return;
            _flips.Tick(RoomClock.Time, transform.position);
            if (!GameState.Alive || UplinkRemaining <= 0f) return;   // times out once (god mode survives it)
            UplinkRemaining = Mathf.Max(0f, UplinkRemaining - dt);
            if (UplinkRemaining > 0f) return;
            PlayerRef.LastKillerId = KillerId;
            PlayerRef.LastKillerPos = ring ? ring.position : transform.position;
            PlayerRef.Kill(DeathCause.TraceTimer);
        }

        static Vector3 Post(PatrolHunter h) =>
            h.patrol != null && h.patrol.Length > 0 ? h.patrol[h.startIndex % h.patrol.Length] : h.transform.position;

        public void ResetState()
        {
            IceHunter.ResetIds();
            Phase = 1;
            UplinkRemaining = -1f;
            _hacked = 0;
            _angle = 0f;
            _last = 0f;
            _flips.Stop();
            if (ring) ring.localRotation = Quaternion.identity;
            foreach (var s in scanners) if (s) { s.bothLayers = false; s.Suppressed = false; }
            foreach (var z in glitchZones) if (z) z.SetActive(false);
            foreach (var h in hunters) if (h) h.Despawn();
        }
    }
}
