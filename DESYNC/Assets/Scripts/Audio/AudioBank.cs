using UnityEngine;

namespace Desync.Audio
{
    /// Every clip AudioManager plays. Lives at Assets/Audio/Resources/AudioBank.asset; filled by Desync.Audio.Editor.AudioSetup.Run
    /// (matches clips under Assets/Audio by file name), so there is no runtime lookup by string.
    [CreateAssetMenu(menuName = "DESYNC/AudioBank")]
    public class AudioBank : ScriptableObject
    {
        [Header("Music stems (identical length, loop on a bar line)")]
        public AudioClip musicRender, musicSource, musicRenderLp, musicSourceLp;

        [Header("Flip")]
        public AudioClip flipSource, flipRender, flipReject;

        [Header("Threats")]
        public AudioClip cameraTick, traceAlarm, iceWindup, iceShriek;

        [Header("Hacking")]
        public AudioClip hackTick, hackWrong, hackStart, hackCancel, accessGranted;

        [Header("Melt / death / flow")]
        public AudioClip heartbeat, flatline, deathCrunch, roomClear, reboot, portalOpen, finaleSting;

        [Header("Misc")]
        public AudioClip hijackOn, hijackOff, glitchBeep, uiClick, uiPop;

        [Header("Footsteps (office floor / same clips bit-crushed)")]
        public AudioClip[] footstepRender, footstepSource;
    }
}
