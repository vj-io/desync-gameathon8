using System;
using UnityEngine;

namespace Desync
{
    public enum DeathCause { Ice, Melt, Alarm, Drone, Architect, TraceTimer }

    public struct RoomResult
    {
        public int room;
        public float time;
        public int traces;
        public int flips;
        public int bestCombo;
        public char grade;   // 'S','A','B','C'
    }

    /// Global event bus. Raise with GameEvents.X?.Invoke(...). Subscribe in OnEnable, unsubscribe in OnDisable.
    /// Feedback systems (audio, post-FX, UI, juice) listen here; gameplay systems raise.
    public static class GameEvents
    {
        // Layer flip (player-initiated only; resets use LayerState.Changed)
        public static Action<bool> Flipped;                 // toSource
        public static Action FlipRejected;                  // tried to return to RENDER inside a wall

        // Surveillance
        public static Action<Vector3> CameraTick;           // sweep end, world position (spatial tick)
        public static Action<Vector3> Spotted;              // a cone started dwelling on the player
        public static Action<int> TraceStarted;             // TRACE count in this room (1..3)
        public static Action TraceEnded;
        public static Action Alarm;                         // 3rd TRACE -> restart follows

        // ICE
        public static Action<Vector3> IceWindup;            // 0.3 s before spawn, vent position
        public static Action<Vector3> IceSpawned;

        // Player state
        public static Action<DeathCause> PlayerKilled;
        public static Action<bool> MeltWarning;             // crossed meltWarn upward(true)/downward(false)
        public static Action Footstep;                      // read LayerState.InSource for dry/crushed

        // Terminals
        public static Action HackStarted;
        public static Action<int> HackTick;                 // correct key, step index from 0 (pitch +1 semitone per step)
        public static Action HackFailed;                    // wrong key -> reset + lockout
        public static Action HackCancelled;
        public static Action<string> AccessGranted;         // story line to show

        // Hijack / glitch
        public static Action<bool> Hijacked;                // true on start, false on end
        public static Action<bool> GlitchZone;              // entered(true)/left(false); GameState.GlitchCountdown holds time left
        public static Action GlitchForcedFlip;

        // Room flow
        public static Action<int> RoomStarted;              // room index 0..5
        public static Action ExitOpened;                    // all terminals done
        public static Action ExitReached;                   // player entered the open exit portal
        public static Action<RoomResult> RoomCleared;
        public static Action Rebooting;                     // restart wipe begins (0.2 s)
        public static Action RoomReset;                     // restart finished, identical state
        public static Action<int> ComboChanged;             // GHOST xN (0 = reset)
        public static Action<bool> Paused;
        public static Action FinaleStarted;                 // Room 6 after third shield terminal
        public static Action GameCompleted;                 // "SIMULATION: COMPROMISED"

        // Juice requests (any system may raise; JuiceUtil executes, honouring Settings)
        public static Action<float> ShakeRequest;           // strength 0..1
        public static Action<float> HitStopRequest;         // seconds (unscaled)
        public static Action<string, Color> PopText;        // big centre pop text e.g. "ACCESS GRANTED", "GHOST x3"
    }
}
