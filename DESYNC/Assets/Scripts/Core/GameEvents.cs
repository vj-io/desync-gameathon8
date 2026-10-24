using System;
using UnityEngine;

namespace Desync
{
    public enum DeathCause { Ice, Melt, Alarm, Drone, Architect, TraceTimer, Glitch, Guard }

    public struct RoomResult
    {
        public int room;
        public int score;
        public Desync.Rules.Medal medal;
        public float time;
        public int traces;
        public int flips;
        public int bestCombo;
        public char grade;   // 'S','A','B','C'
    }

    public struct ScorePop { public Desync.Rules.EventType type; public int points; public float multiplier; public string label; }

    public struct DeathInfo
    {
        public DeathCause cause;
        public string killerId;        // e.g. "ICE-2"; null when none
        public Vector3 killerPos;      // valid when killerId != null
        public float roomTime;
        public string nearMiss;        // one TRUE near-miss stat, or null (never fabricate)
    }

    public sealed class RoomReport
    {
        public int room;
        public int eventsScore;        // sum of event points (already multiplied)
        public float peakMultiplier;
        public int timeBonus;
        public int total;
        public float time;
        public Desync.Rules.Medal medal;
        public char grade;
        public float prevBestTime;     // 0 when none
        public int prevBestScore;
        public string nextTarget;      // e.g. "0.8 s to GOLD", "S needs: no TRACE"; null when maxed
        public string shareText;
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

        // UI -> Flow requests (UI never references Flow directly)
        public static Action<int> StartRequested;           // title click / level select: lock pointer + load room index
        public static Action ResumeRequested;               // pause menu resume / click-to-resume
        public static Action RestartRequested;              // pause menu restart room
        public static Action QuitRequested;                 // pause menu quit to title
        public static Action<bool> LevelSelectToggled;      // F2 panel shown/hidden (Flow raises on F2)
        public static Action SettingsChanged;               // Settings.* written; Flow applies render scale etc.

        // Scoring / mastery (M1)
        public static Action<ScorePop> Scored;              // every scoring event (CleanFlip, Graze, Redline, Hack...), for HUD pops
        public static Action<DeathInfo> DeathRecap;         // Flow: shown for up to 1.2 s (any key skips), then REBOOT
        public static Action<RoomReport> RoomReportReady;   // Flow: results tally; UI waits for R (RestartRequested) / SPACE (ContinueRequested) / C (CopyRequested)
        public static Action ContinueRequested;             // UI -> Flow: leave results, go to next room/node
        public static Action<string> CopyRequested;         // UI -> Net: copy text to clipboard
        public static Action<string> Hint;                  // Flow: contextual hint after 3 deaths in a room
        public static Action<string> Grazed;                // IceHunter: id of the ICE that grazed the player (>= 0.15 s within graze radius in SOURCE, no contact)
        public static Action<bool> HackFinished;            // HackStation: true = perfect (no wrong arrow) — raised for terminals AND hijack pads

        // Juice requests (any system may raise; JuiceUtil executes, honouring Settings)
        public static Action<float> ShakeRequest;           // strength 0..1
        public static Action<float> HitStopRequest;         // seconds (unscaled)
        public static Action<string, Color> PopText;        // big centre pop text e.g. "ACCESS GRANTED", "GHOST x3"
    }
}
