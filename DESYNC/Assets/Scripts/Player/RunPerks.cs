using System.Collections.Generic;
using UnityEngine;

namespace Desync.Player
{
    /// Exploit and run-mutator behaviour the player side implements. The run manager fills these once per room from the
    /// held loadout (D-013: nothing looks a flag up by string per frame). Everything is off outside a BREACH run.
    public static class RunPerks
    {
        public static float HeatSinkTo = -1f;   // heat_sink: the first 100 % melt of a room attempt becomes this (-1 = off)
        public static float CoolantLeak;        // coolant_leak: melt removed on each hack completion
        public static float HeatExchange;       // heat_exchange: melt removed on each GRAZE
        public static bool PacketLoss;          // packet_loss: a hack completion is also a chain step (ScoreKeeper)
        public static readonly List<Bounds> DeadZones = new List<Bounds>();   // DEAD_ZONES: world boxes where SPACE does nothing

        public static void Clear()
        {
            HeatSinkTo = -1f;
            CoolantLeak = HeatExchange = 0f;
            PacketLoss = false;
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
