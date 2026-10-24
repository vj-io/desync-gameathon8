using System;
using System.Collections.Generic;
using UnityEngine;

namespace Desync.Player
{
    /// Exploit and run-mutator behaviour the player side implements. The run manager fills these once per room from the
    /// held loadout (D-013: nothing looks a flag up by string per frame). Everything is off outside a BREACH run.
    /// Flow raises MeltAdjust (echo_flip, clock_skew, double_tap, burnout) and MeltMeter applies it; flag data values are
    /// written here by RunManager.PrepareRoom (OVERCLOCK values already doubled when Thermal Runaway is held).
    public static class RunPerks
    {
        public static float HeatSinkTo = -1f;   // heat_sink: the first 100 % melt of a room attempt becomes this (-1 = off)
        public static float CoolantLeak;        // coolant_leak: melt removed on each hack completion
        public static float HeatExchange;       // heat_exchange: melt removed on each GRAZE
        public static bool PacketLoss;          // packet_loss: a hack completion is also a chain step (ScoreKeeper)

        // Melt on hack completion (MeltMeter): turbo_compile always; overclock_hack only when the hack was in SOURCE.
        public static float TurboCompileMelt;   // turbo_compile: melt added on each hack completion
        public static float OverclockHackMelt;  // overclock_hack: melt added on a hack completed in SOURCE

        // Survive-at-100 (MeltMeter). Heat Sink is above; only one fires per death.
        public static float KernelPanicGrace;   // kernel_panic: seconds of grace at 100 % to flip to RENDER (0 = off)
        public static float DeadMansReviveTo = -1f;   // dead_mans_switch: melt death revives here, once per run (-1 = off)

        // Chain-driven melt refunds (ScoreKeeper raises MeltAdjust). Values are % melt per event.
        public static int EchoFlipInterval;     // echo_flip: refund every Nth clean flip (0 = off)
        public const float EchoFlipRefund = 5f;
        public static float ClockSkewMelt;      // clock_skew: melt refunded per chain step (0 = off)
        public static bool PhaseMemory;         // phase_memory: first chain break per room forgiven (ScoreKeeper)
        public static bool SpectralFever;       // spectral_fever: a clean flip at melt >= 80 counts as two chain steps

        // Decoys / flip combat (ScoreKeeper spawns, needs the Enemies assembly). Seconds of decoy life; 0 = off.
        public static float AfterimageLife;     // afterimage: clean flip -> Silhouette decoy
        public static float PoltergeistLife;    // poltergeist: every clean flip -> decoy
        public static float MirrorShardLife;    // mirror_shard: flip -> SOURCE Echo decoy ICE chase
        public static float BurnoutPulseMelt;   // burnout_pulse: flip to RENDER above 60 % spends this much melt to derez ICE

        public static float DoubleTapMelt;      // double_tap: melt added on a double flip (0 = off)
        public const float DoubleTapWindow = 0.25f, DoubleTapBlink = 2f;

        public static float OverclockedPuppetMelt;   // overclocked_puppet: melt/s while a hijack is active (HijackedDrone ends it on flip)

        /// Melt delta from Flow-side exploits (negative = refund). MeltMeter applies it, honouring God / cinematic / death.
        public static Action<float> MeltAdjust;
        /// Dead Man's Switch consumed its revive: the run marks it used so it never fires again this run.
        public static Action DeadMansFired;

        public static readonly List<Bounds> DeadZones = new List<Bounds>();   // DEAD_ZONES: world boxes where SPACE does nothing

        public static void Clear()
        {
            HeatSinkTo = -1f;
            CoolantLeak = HeatExchange = 0f;
            PacketLoss = false;
            TurboCompileMelt = OverclockHackMelt = 0f;
            KernelPanicGrace = 0f;
            DeadMansReviveTo = -1f;
            EchoFlipInterval = 0;
            ClockSkewMelt = 0f;
            PhaseMemory = SpectralFever = false;
            AfterimageLife = PoltergeistLife = MirrorShardLife = BurnoutPulseMelt = 0f;
            DoubleTapMelt = OverclockedPuppetMelt = 0f;
            DeadZones.Clear();
        }

        public static bool InDeadZone(Vector3 p)
        {
            for (int i = 0; i < DeadZones.Count; i++)
                if (DeadZones[i].Contains(p)) return true;
            return false;
        }
    }
}
