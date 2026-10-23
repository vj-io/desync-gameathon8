using UnityEngine;

namespace Desync
{
    /// Polled shared state (HUD, post-FX, audio read it every frame). Writers noted per field.
    public static class GameState
    {
        public static int RoomIndex = -1;          // RoomManager
        public static float Melt;                  // 0..100, MeltMeter
        public static bool Alive = true;           // PlayerRef.Kill / RoomManager reset
        public static bool TraceActive;            // TraceSystem
        public static float TraceRemaining;        // TraceSystem, seconds
        public static int TraceCount;              // TraceSystem, this room
        public static float TraceCountdown = -1f;  // RoomManager (Room 5 timer), -1 = off
        public static int Flips;                   // FlipController, this room
        public static int Combo;                   // RoomManager (GHOST xN)
        public static int TerminalsDone;           // RoomManager
        public static int TerminalsTotal;          // RoomManager
        public static bool ExitOpen;               // RoomManager
        public static bool Paused;                 // GameManager
        public static bool InHack;                 // Terminal (player movement frozen while true)
        public static bool Cinematic;              // finale / scripted camera (player input disabled)
        public static bool DemoMode;               // F9: trace-free
        public static bool HijackActive;           // HijackedDrone
        public static float GlitchCountdown = -1f; // GlitchZone, -1 = not inside one
        public static string InteractPrompt;       // Interactor: non-null while looking at an IInteractable
        public static RoomResult[] Results = new RoomResult[6];   // RoomManager, per room best/last result (UI ending screen)
        public static int IceAlive;                // IceVent/IceHunter: live hunters this frame
        public static Vector3 NearestIce;          // IceHunter: position of the hunter nearest the player (valid when IceAlive > 0)
        public static float Pulse;                 // FlipController/JuiceUtil: 0..1 post-FX pulse
        public static bool God;                    // DebugBridge "god on": RoomManager ignores PlayerKilled, MeltMeter frozen
        public static float HijackRemaining;       // HijackedDrone, seconds left while HijackActive (HUD hijack timer)
    }
}
