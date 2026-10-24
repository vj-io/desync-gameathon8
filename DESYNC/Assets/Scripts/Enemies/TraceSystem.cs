using UnityEngine;

namespace Desync.Enemies
{
    /// Enemies-side EXPLOIT flags (D-013), read from GameState.Flags once per room and cached as bools (the mutator flags live
    /// in EnemyFlags). Refreshed on RoomStarted and RoomReset. Values match exploits.json (headline numbers, like EnemyFlags).
    /// Conditions in the data ("chain>=5", "melt>=80") are documentation; the game checks live GameState where it consumes them.
    public static class ExploitFlags
    {
        public const float LoopbackWindow = 0.5f, StaticVeilWarn = 0.5f, NullTrailFaster = 0.5f,
                           OverheatRadius = 3f, OverheatStun = 1f, CrosstalkDerez = 4f, BurningEffigyRadius = 3f,
                           PuppetThreadExtra = 4f, OverclockedPuppetMelt = 2f, GhostPingDrone = 3f,
                           MirrorChaseRange = 8f, ProxyChainMinChain = 5, InvisibleInkMinChain = 6;

        public static bool SpoofedBadge, Loopback, ProxyChain, InvisibleInk, Honeypot, WrongNumber, FalsePositive,
                           SysadminKey, StaticVeil, NullTrail, OverheatHalo, MirrorShard, Crosstalk, BurningEffigy,
                           PuppetThread, OverclockedPuppet, GhostPing, HallOfMirrors, OverclockHack, BruteForce;
        /// Heat mutator Scrambled Prompts ("flag:" or "mut:scrambled_prompts"): HackStation.ScrambledShare of hacks mirrored.
        public static bool ScrambledPrompts;

        /// Per-room consumption: Spoofed Badge ignores the FIRST camera detection only.
        public static bool SpoofUsed;

        /// True while Invisible Ink holds TRACE steady (chain >= 6).
        public static bool TraceFrozen => InvisibleInk && GameState.Chain >= InvisibleInkMinChain;
        /// True while Proxy Chain stops cameras adding TRACE (chain >= 5).
        public static bool CameraTraceOff => ProxyChain && GameState.Chain >= ProxyChainMinChain;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            GameEvents.RoomStarted -= OnRoomStarted;
            GameEvents.RoomStarted += OnRoomStarted;
            GameEvents.RoomReset -= Refresh;
            GameEvents.RoomReset += Refresh;
            Refresh();
        }

        static void OnRoomStarted(int _) => Refresh();

        public static void Refresh()
        {
            SpoofedBadge = Has("spoofed_badge");
            Loopback = Has("loopback");
            ProxyChain = Has("proxy_chain");
            InvisibleInk = Has("invisible_ink");
            Honeypot = Has("honeypot");
            WrongNumber = Has("wrong_number");
            FalsePositive = Has("false_positive");
            SysadminKey = Has("sysadmin_key");
            StaticVeil = Has("static_veil");
            NullTrail = Has("null_trail");
            OverheatHalo = Has("overheat_halo");
            MirrorShard = Has("mirror_shard");
            Crosstalk = Has("crosstalk");
            BurningEffigy = Has("burning_effigy");
            PuppetThread = Has("puppet_thread");
            OverclockedPuppet = Has("overclocked_puppet");
            GhostPing = Has("ghost_ping");
            HallOfMirrors = Has("hall_of_mirrors");
            OverclockHack = Has("overclock_hack");
            BruteForce = Has("brute_force");
            ScrambledPrompts = Has("scrambled_prompts") || GameState.Flags.Contains("mut:scrambled_prompts");
            SpoofUsed = false;
        }

        static bool Has(string id) => GameState.Flags.Contains("flag:" + id);
    }

    /// Per-room surveillance state (one per room scene, created by EnemyFactory; lazily at runtime if missing).
    /// TRACE: any cone dwelling >= traceDwell starts a TRACE (count+1) or, while one runs, holds it at traceDuration.
    /// The 3rd TRACE in a room raises Alarm. Also owns the Last-Known-Position: on a RENDER->SOURCE flip within
    /// LkpWindow seconds of being seen (or during a TRACE) the cyan marker drops at the last seen spot and drones investigate.
    /// Exploits: Log Wipe (decay), Invisible Ink (freeze), Honeypot (hacking drops a level), Loopback (forget a glance on flip),
    /// Wrong Number (investigations go to your last decoy).
    public class TraceSystem : MonoBehaviour, IResettable
    {
        public const float LkpWindow = 2f, LkpLife = 6f;

        public static TraceSystem I;

        public Transform lkpMarker;    // child shown at the LKP (mat.lkp)
        public Transform lkpSpin;

        /// Bumped every time a new LKP is placed; drones compare against the last version they handled.
        [System.NonSerialized] public int LkpVersion;
        [System.NonSerialized] public Vector3 Lkp;

        float _last, _seenAt = -1e9f, _lkpUntil = -1f;
        Vector3 _seenPos;

        void OnEnable() { I = this; GameEvents.Flipped += OnFlipped; GameEvents.HackFinished += OnHackFinished; }
        void OnDisable() { if (I == this) I = null; GameEvents.Flipped -= OnFlipped; GameEvents.HackFinished -= OnHackFinished; }

        static TraceSystem Get() => I ? I : EnemyFactory.BuildTraceSystem(null);

        /// A cone has LOS on the player this frame.
        public static void Seen(Vector3 playerFeet)
        {
            var t = Get();
            t._seenPos = playerFeet;
            t._seenAt = RoomClock.Time;
        }

        /// A cone has dwelt >= traceDwell this frame. Invisible Ink freezes TRACE (no new level while chain >= 6).
        public static void Sustain()
        {
            if (ExploitFlags.TraceFrozen) return;
            var c = GameConfig.I;
            if (GameState.TraceActive) { GameState.TraceRemaining = c.traceDuration; return; }
            GameState.TraceActive = true;
            GameState.TraceRemaining = c.traceDuration;
            GameState.TraceCount++;
            GameEvents.TraceStarted?.Invoke(GameState.TraceCount);
            if (GameState.TraceCount == c.tracesToAlarm) GameEvents.Alarm?.Invoke();
        }


        void Update()
        {
            float dt = EnemyKit.Step(ref _last);
            if (GameState.TraceActive)
            {
                if (!ExploitFlags.TraceFrozen)   // Invisible Ink holds TRACE steady (no decay either)
                {
                    GameState.TraceRemaining -= dt * (EnemyFlags.LogWipe ? EnemyFlags.LogWipeDecay : 1f);
                    if (GameState.TraceRemaining <= 0f)
                    {
                        GameState.TraceActive = false;
                        GameState.TraceRemaining = 0f;
                        GameEvents.TraceEnded?.Invoke();
                    }
                }
            }
            if (!lkpMarker) return;
            bool show = RoomClock.Time < _lkpUntil;
            if (lkpMarker.gameObject.activeSelf != show) lkpMarker.gameObject.SetActive(show);
            if (show && lkpSpin) lkpSpin.localRotation = Quaternion.Euler(0f, Time.time * 120f, 0f);
        }

        /// Honeypot: completing a hack while a TRACE is up lowers it one level (ends the active TRACE when it reaches 0).
        void OnHackFinished(bool perfect)
        {
            if (!ExploitFlags.Honeypot || GameState.TraceCount < 1) return;
            GameState.TraceCount--;
            if (GameState.TraceActive)
            {
                GameState.TraceActive = false;
                GameState.TraceRemaining = 0f;
                GameEvents.TraceEnded?.Invoke();
            }
        }

        void OnFlipped(bool toSource)
        {
            if (!toSource || _seenAt < -1e8f) return;
            // Loopback: a glance that never became a TRACE is forgotten on the flip (no investigation).
            if (ExploitFlags.Loopback && !GameState.TraceActive && RoomClock.Time - _seenAt <= LkpWindow) return;
            if (!GameState.TraceActive && RoomClock.Time - _seenAt > LkpWindow) return;
            Vector3 drop = _seenPos;
            // Wrong Number: send the investigation to your last decoy instead of where you were seen.
            if (ExploitFlags.WrongNumber && Decoys.Nearest(_seenPos, 1e6f, out Vector3 dpos, out _)) drop = dpos;
            Lkp = drop;
            LkpVersion++;
            _lkpUntil = RoomClock.Time + LkpLife;
            if (lkpMarker) lkpMarker.position = drop;
        }

        public void ResetState()
        {
            GameState.TraceActive = false;
            GameState.TraceRemaining = 0f;
            GameState.TraceCount = 0;
            _last = 0f;
            _seenAt = -1e9f;
            _lkpUntil = -1f;
            LkpVersion = 0;
            if (lkpMarker) lkpMarker.gameObject.SetActive(false);
        }
    }
}
