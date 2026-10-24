using System;
using UnityEngine;

namespace Desync.Enemies
{
    /// Enemies-side exploit / run-mutator flags (D-013), read from GameState.Flags ONCE per room and cached as bools:
    /// refreshed on GameEvents.RoomStarted and RoomReset. A run manager that writes GameState.Flags after RoomStarted
    /// calls Refresh() itself. Each flag is accepted as "flag:<id>" or "mut:<id>".
    ///   signal_jam    drone cones see 20 % less far (VisionCone)
    ///   log_wipe      TRACE decays 25 % faster (TraceSystem)
    ///   blind_cams    camera cones off; drones still trace (VisionCone)
    ///   spotlight     camera sweeps 30 % faster (CameraSweep)
    ///   ice_swarm     +1 ICE: vents may keep one more hunter out, the AUDITOR fields a third hunter
    ///   flicker       a room-wide forced flip every 6 s with a 1 s screen-tear telegraph (RoomHazards)
    ///   gaze          an Architect-style gaze sweeps the room every 12 s, seeing both layers (RoomHazards)
    ///   boss_overclock  boss heat variant: the AUDITOR's scanners also see SOURCE 1 s in every 6 s
    ///   auditor_drones  this room dispatches an Auditor Drone at TRACE 2 (the run sets it from Act 2 room 3 on)
    ///   auditor_protocol  Deep Layer: Auditor Drones at TRACE 1, in every room
    ///   blackout      Deep Layer: RENDER lights (spots, directional) and ambient -40 % (RoomHazards); palettes kept
    /// The other heat mutators need nothing here: hardened_ice is Mods.IceSpeedMult (every ICE reads it), and
    /// double_patrol / tracker_swarm / gaze_everywhere are placements the generator makes (drones, trackers, watchers).
    public static class EnemyFlags
    {
        public const float SignalJamRange = 0.8f, LogWipeDecay = 1.25f, SpotlightSpeed = 1.3f,
                           FlickerPeriod = 6f, FlickerTelegraph = 1f, GazePeriod = 12f, BlackoutScale = 0.6f;

        public static bool SignalJam, LogWipe, BlindCams, Spotlight, IceSwarm, Flicker, Gaze, BossHeat,
                           AuditorDrones, AuditorProtocol, Blackout;

        /// Multiplier on every RENDER light this room (BLACKOUT).
        public static float LightScale => Blackout ? BlackoutScale : 1f;

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
            SignalJam = Has("signal_jam");
            LogWipe = Has("log_wipe");
            BlindCams = Has("blind_cams");
            Spotlight = Has("spotlight");
            IceSwarm = Has("ice_swarm");
            Flicker = Has("flicker");
            Gaze = Has("gaze");
            BossHeat = Has("boss_overclock");
            AuditorDrones = Has("auditor_drones");
            AuditorProtocol = Has("auditor_protocol");
            Blackout = Has("blackout");
            RoomHazards.Configure();
        }

        static bool Has(string id) => GameState.Flags.Contains("flag:" + id) || GameState.Flags.Contains("mut:" + id);
    }

    /// Perception ladder of every sensor and hunter (spec): Patrol -> Suspicious (SuspiciousTime) -> Tracing -> Hunting -> Return.
    /// Telegraphed by cone colour (VisionCone: idle pink, seeing amber, traced red) and EnemyEvents.Alerted (audio cue per state).
    public enum Alert { Patrol, Suspicious, Tracing, Hunting, Return }

    /// Enemy events with no Core slot yet (requested as GameEvents.TrackerSpawned / AuditorPhase).
    public static class EnemyEvents
    {
        public const float SuspiciousTime = 0.25f;

        public static Action<Vector3> TrackerSpawned;   // a Tracker ICE left its nest: 1 s audio sting at this position
        public static Action<int> AuditorPhase;         // AUDITOR entered phase 2 or 3; 4 = uplink reached
        public static Action<Vector3, Alert> Alerted;   // a camera, drone or hunter changed perception state (one cue per state)
        public static Action<Vector3> SentinelCharge;   // a Sentinel ICE starts its 0.6 s pulse telegraph
        public static Action<Vector3> SentinelPulse;    // ... and pulses (kills within 4 m in SOURCE)
        public static Action<Vector3> SplitterSplit;    // a grazed Splitter ICE broke into two minis
        public static Action<Vector3, bool> DaemonCrossed;   // a Daemon followed the player into RENDER (true) / fell back to SOURCE (false)
        public static Action<Vector3, bool> AuditorDrone;    // an Auditor Drone was dispatched (true) / recalled after the TRACE (false)
        public static Action<string> PuppetString;      // the player collected a puppet string (entity id); story text lands in M4
        public static Action<Vector3> WatcherSpotted;   // a Watcher Eye's beam started seeing the player (lens position)
        public static Action<Vector3> GuardAlert;       // a security officer spotted the player: "!" pop, the chase starts (his feet)
        public static Action<bool> GuardChase;          // the first officer started chasing (true) / the last one stopped (false): HUD edge pulse
        public static Action<Vector3> GuardStep;        // an officer's footstep in RENDER (his feet)
        public static Action<Vector3> GuardWindup;      // an officer raises his baton: 0.35 s strike telegraph (crackle, red glow)
        public static Action<Vector3, bool> GuardStrike;   // ... and swings: true = it landed (the player is down), false = missed
    }

    /// Forced flips on a fixed RoomClock schedule (FLICKER, AUDITOR P3): every Period s after Start a GlitchForcedFlip,
    /// preceded for Telegraph s by GameState.GlitchCountdown counting down (screen tear, beeps, HUD — as a glitch zone).
    /// FlipController applies the flip with its normal rejection rule.
    public sealed class ForcedFlipClock
    {
        public float Period, Telegraph;
        public float Next = -1f;   // room time of the next flip; < 0 = stopped
        bool _warn;

        public bool Running => Next >= 0f;

        public void Start(float now, float period, float telegraph)
        {
            Stop();
            Period = Mathf.Max(0.5f, period);
            Telegraph = telegraph;
            Next = now + Period;
        }

        public void Stop()
        {
            if (_warn) GameState.GlitchCountdown = -1f;
            _warn = false;
            Next = -1f;
        }

        /// Once per frame. True on the frame the flip is raised. `source` names the hazard if that flip melts the player.
        public bool Tick(float now, Vector3 source)
        {
            if (Next < 0f) return false;
            float left = Next - now;
            bool warn = left <= Telegraph;
            if (warn) GameState.GlitchCountdown = Mathf.Max(0f, left);
            else if (_warn) GameState.GlitchCountdown = -1f;
            _warn = warn;
            if (left > 0f) return false;
            Next += Period;
            if (Next <= now) Next = now + Period;   // the clock jumped: one flip, then back on schedule
            GameState.GlitchCountdown = -1f;
            _warn = false;
            if (!GameState.Alive || GameState.Cinematic) return false;
            PlayerRef.LastKillerPos = source;
            GameEvents.GlitchForcedFlip?.Invoke();
            return true;
        }
    }
}
