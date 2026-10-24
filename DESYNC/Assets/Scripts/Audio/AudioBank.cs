using UnityEngine;

namespace Desync.Audio
{
    /// Every clip AudioManager plays. Lives at Assets/Audio/Resources/AudioBank.asset; filled by Desync.Audio.Editor.AudioSetup.Run
    /// (matches clips under Assets/Audio by file name), so there is no runtime lookup by string.
    [CreateAssetMenu(menuName = "DESYNC/AudioBank")]
    public class AudioBank : ScriptableObject
    {
        [Header("Music stems, act 1 (identical length, loop on a bar line)")]
        public AudioClip musicRender, musicSource, musicRenderLp, musicSourceLp;

        [Header("Music stems, act 2 and act 3 (same length and bar grid as act 1; loaded on demand)")]
        public AudioClip musicRenderA2, musicSourceA2, musicRenderLpA2, musicSourceLpA2;
        public AudioClip musicRenderA3, musicSourceA3, musicRenderLpA3, musicSourceLpA3;

        [Header("Music layers (same length as the stems; fade in with melt / TRACE level)")]
        public AudioClip musicTension, musicTraceA, musicTraceB;

        [Header("Flip")]
        public AudioClip flipSource, flipRender, flipReject;

        [Header("Threats")]
        public AudioClip cameraTick, traceAlarm, iceWindup, iceShriek, iceHum;   // iceHum: seamless loop

        [Header("Hacking")]
        public AudioClip hackTick, hackWrong, hackStart, hackCancel, accessGranted;

        [Header("Melt / death / flow")]
        public AudioClip heartbeat, flatline, deathCrunch, roomClear, reboot, portalOpen, finaleSting;

        [Header("Misc")]
        public AudioClip hijackOn, hijackOff, glitchBeep, uiClick, uiPop;

        [Header("Scoring")]
        public AudioClip scorePop, redlineWhoosh, grazeWhoosh;

        [Header("Run flow")]
        public AudioClip mapOpen, mapChoose, decryptTick, draftPick, marketBuy, cooldown;

        [Header("AUDITOR and TRACKER")]
        public AudioClip trackerSting, auditorP2, auditorP3, uplinkTick;

        [Header("M3 enemies")]
        public AudioClip lanternHum, sentinelCharge, sentinelPulse, splitterSplit, daemonCross, watcherBeam;   // lanternHum: seamless loop

        [Header("Perception states and COMPILER")]
        public AudioClip alertSuspicious, alertHunting, alertReturn, wallSwap;

        [Header("Footsteps (office floor / same clips bit-crushed)")]
        public AudioClip[] footstepRender, footstepSource;

        /// Base stem `stem` (0 RENDER, 1 SOURCE, 2 RENDER low-passed, 3 SOURCE low-passed) of act `act` (0..2).
        public AudioClip Stem(int act, int stem)
        {
            switch (act)
            {
                case 1: return stem == 0 ? musicRenderA2 : stem == 1 ? musicSourceA2 : stem == 2 ? musicRenderLpA2 : musicSourceLpA2;
                case 2: return stem == 0 ? musicRenderA3 : stem == 1 ? musicSourceA3 : stem == 2 ? musicRenderLpA3 : musicSourceLpA3;
                default: return stem == 0 ? musicRender : stem == 1 ? musicSource : stem == 2 ? musicRenderLp : musicSourceLp;
            }
        }
    }
}
