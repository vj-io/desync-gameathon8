using System.Collections.Generic;
using Desync.Content;
using Desync.Player;
using UnityEngine;

namespace Desync.Flow
{
    /// Maps the held loadout to RunPerks once per room (D-013). Values come from the exploit data; OVERCLOCK flag values are
    /// doubled when Thermal Runaway is held (ContentImpl folds its stat effects, the game doubles the flag values). A flag is
    /// only applied when it survived the loadout build (built.flags), so an exclusive-group / Daily exclusion drops it here too.
    /// Ghost Cache (exit reroll) and Dead Man's Switch (once per run) are run-level and tracked here, not per room.
    public static class PerkBinder
    {
        public static bool GhostCache;      // ghost_cache held this room (RunManager grants the reroll on a chain >= 10 exit)
        public static bool DeadManUsed;     // dead_mans_switch already revived this run

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            RunEvents.StartRun -= OnStartRun;
            RunEvents.StartRun += OnStartRun;
            RunPerks.DeadMansFired -= OnDeadMansFired;
            RunPerks.DeadMansFired += OnDeadMansFired;
        }

        static void OnStartRun(RunMode _) => DeadManUsed = false;
        static void OnDeadMansFired() => DeadManUsed = true;

        /// Fills RunPerks for the room. `flags` is the built loadout's active flag set. Leaves RunPerks.DeadZones alone.
        public static void Bind(IReadOnlyList<Exploit> held, HashSet<string> flags)
        {
            RunPerks.Clear();
            GhostCache = flags.Contains("flag:ghost_cache");
            bool thermalRunaway = flags.Contains("flag:thermal_runaway");
            foreach (var e in held)
            {
                float k = thermalRunaway && e.archetype == Archetype.Overclock ? 2f : 1f;   // OVERCLOCK numeric effects x2
                foreach (var f in e.effects)
                {
                    if (f.stat == null || !f.stat.StartsWith("flag:", System.StringComparison.Ordinal) || !flags.Contains(f.stat)) continue;
                    float v = f.value * k;
                    switch (f.stat)
                    {
                        case "flag:heat_sink": RunPerks.HeatSinkTo = f.value; break;
                        case "flag:coolant_leak": RunPerks.CoolantLeak = v; break;
                        case "flag:heat_exchange": RunPerks.HeatExchange = v; break;
                        case "flag:packet_loss": RunPerks.PacketLoss = true; break;
                        case "flag:turbo_compile": RunPerks.TurboCompileMelt = v; break;
                        case "flag:overclock_hack": RunPerks.OverclockHackMelt = v; break;
                        case "flag:kernel_panic": RunPerks.KernelPanicGrace = v; break;
                        case "flag:dead_mans_switch": if (!DeadManUsed) RunPerks.DeadMansReviveTo = f.value; break;
                        case "flag:echo_flip": RunPerks.EchoFlipInterval = Mathf.RoundToInt(f.value); break;
                        case "flag:clock_skew": RunPerks.ClockSkewMelt = f.value; break;
                        case "flag:phase_memory": RunPerks.PhaseMemory = true; break;
                        case "flag:spectral_fever": RunPerks.SpectralFever = true; break;
                        case "flag:afterimage": RunPerks.AfterimageLife = f.value; break;
                        case "flag:poltergeist": RunPerks.PoltergeistLife = f.value; break;
                        case "flag:mirror_shard": RunPerks.MirrorShardLife = f.value; break;
                        case "flag:burnout_pulse": RunPerks.BurnoutPulseMelt = v; break;
                        case "flag:double_tap": RunPerks.DoubleTapMelt = v; break;
                        case "flag:overclocked_puppet": RunPerks.OverclockedPuppetMelt = f.value; break;
                    }
                }
            }
        }
    }
}
