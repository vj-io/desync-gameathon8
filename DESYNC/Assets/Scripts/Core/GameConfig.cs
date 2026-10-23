using UnityEngine;

namespace Desync
{
    /// All tunables (PRD Appendix A). Lives at Resources/GameConfig.asset; falls back to these defaults.
    [CreateAssetMenu(menuName = "DESYNC/GameConfig")]
    public class GameConfig : ScriptableObject
    {
        static GameConfig _i;
        public static GameConfig I => _i ? _i : (_i = Resources.Load<GameConfig>("GameConfig") ?? CreateInstance<GameConfig>());

        [Header("Player")]
        public float moveSpeed = 5f;
        public float fov = 90f;
        public float lookSensitivity = 0.12f;   // deg per mouse count, scaled by Settings.Sensitivity
        public float playerHeight = 1.8f;
        public float playerRadius = 0.35f;
        public float eyeHeight = 1.6f;

        [Header("Melt (% per second)")]
        public float meltRiseSource = 12f;
        public float meltDrainRender = 4f;
        public float meltFlipSpike = 5f;
        public float meltWarn = 70f;
        public float meltDeathDrip = 0.6f;

        [Header("Flip / juice")]
        public float flipPulse = 0.15f;
        public float hitStop = 0.07f;
        public float shakeDuration = 0.15f;
        public float restartWipe = 0.2f;

        [Header("Trace")]
        public float traceDwell = 0.5f;
        public float traceDuration = 6f;
        public int tracesToAlarm = 3;

        [Header("Security camera")]
        public float cameraConeAngle = 60f;
        public float cameraConeRange = 8f;
        public float cameraSweepAngle = 45f;   // +/- from centre yaw
        public float cameraSweepPeriod = 3f;   // seconds per one-way sweep

        [Header("Drone")]
        public float droneSpeed = 2.5f;
        public float droneConeAngle = 45f;
        public float droneConeRange = 6f;
        public float droneHover = 1.6f;

        [Header("ICE")]
        public float iceSpeed = 4.5f;
        public float iceSpawnInterval = 4f;
        public float iceSpawnIntervalTraced = 2f;
        public int iceMaxAlive = 4;
        public float iceWindup = 0.3f;
        public float iceDespawn = 1f;

        [Header("Terminals")]
        public float terminalRange = 2.5f;
        public float terminalWrongLockout = 0.4f;
        public int[] promptLengthByRoom = { 3, 3, 4, 4, 5, 5 };

        [Header("Hijack / glitch")]
        public float hijackDuration = 10f;
        public float glitchZoneTime = 3f;

        [Header("World")]
        public float fogStart = 10f;
        public float fogEnd = 40f;
        public Color fogColor = new Color(0.035f, 0.04f, 0.11f);
        public Color magenta = new Color(1f, 0.169f, 0.839f);   // #FF2BD6
        public Color cyan = new Color(0f, 0.941f, 1f);          // #00F0FF
        public Color green = new Color(0f, 1f, 0.4f);           // #00FF66
        public Color danger = new Color(1f, 0.15f, 0.2f);

        [Header("Rooms (index 0..5)")]
        public string[] roomNames = { "BOOT", "EYES", "ICE", "HIJACK", "UNSTABLE", "ROOT" };
        public float[] parTime = { 25f, 50f, 70f, 90f, 100f, 120f };
        public int[] parFlips = { 2, 4, 6, 8, 10, 12 };

        [Header("Audio")]
        public float bpm = 120f;
        public float stemCrossfade = 0.12f;
        public float hackDuckDb = -4f;

        [Header("Story")]
        public string[] introLines =
        {
            "You process tickets at HALCYON Corp. Your city is a render. Your boss is a puppet.",
            "Every terminal you breach leaks a line of the Architects' memo: the city is a training set, and you are the data.",
            "Reach the Root, tear off their masks in SOURCE, and broadcast their real faces before your brain melts.",
        };

        /// Terminal story lines in breach order across the run (rooms 1-4 = the six memo logs, room 5 = route, room 6 = shields).
        public string[] terminalLines =
        {
            "MEMO 01: HALCYON-CITY renders at 98% fidelity. Nobody has noticed.",
            "MEMO 02: Employees are not staff. They are labelled data. Keep them processing.",
            "MEMO 03: Managers run on puppet scripts v4.2. Do not patch the strings.",
            "MEMO 04: Any agent who sees the code must melt before they can testify.",
            "MEMO 05: Hunter-ICE lives in SOURCE. Keep the render pretty and they stay blind.",
            "MEMO 06: The Board is the Architect. The Architect is the Board. Hide their faces.",
            "SECTOR 5 STABILISED. ROOT ACCESS ROUTE OPEN.",
            "GLITCH FIREWALL BYPASSED. ROOT IS LISTENING.",
            "SHIELD 1/3 OFFLINE. THE FACE FLICKERS.",
            "SHIELD 2/3 OFFLINE. YOU CAN SEE THE STRINGS.",
            "SHIELD 3/3 OFFLINE. BROADCASTING THEIR REAL FACES.",
        };
    }
}
