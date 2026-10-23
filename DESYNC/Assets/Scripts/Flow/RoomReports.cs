using System;
using System.Globalization;
using System.Text;
using Desync.Rules;
using UnityEngine;

namespace Desync.Flow
{
    /// Results maths (pure: RoomReport, next target, share text, clock format) plus the content files it reads:
    /// Resources/Content/rooms.json (tDev, parScore per room) and Resources/Content/hints.json (death hints by cause).
    /// Both files are optional: rooms fall back to GameConfig.parTime / 1.25 and a formula par, hints to none.
    public static class RoomReports
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly string[] MedalNames = { "", "BRONZE", "SILVER", "GOLD", "SYNC" };

        /// Results of one clear. `prev` = the saved best before this clear (null when none).
        public static RoomReport Build(int room, float time, int eventsScore, float peakMultiplier, float tDev, int parScore,
                                       RoomBest prev, int traces, float scoreMult)
        {
            int bonus = Scoring.TimeBonus(time, Medals.Threshold(Medal.Gold, tDev));
            int total = eventsScore + bonus;
            float prevTime = prev != null && prev.time > 0f ? prev.time : 0f;
            int prevScore = prev?.score ?? 0;
            return new RoomReport
            {
                room = room,
                eventsScore = eventsScore,
                peakMultiplier = peakMultiplier,
                timeBonus = bonus,
                total = total,
                time = time,
                medal = Medals.For(time, tDev),
                grade = Scoring.Grade(total, parScore),
                prevBestTime = prevTime,
                prevBestScore = prevScore,
                nextTarget = NextTarget(prevTime > 0f ? Mathf.Min(time, prevTime) : time, tDev, Mathf.Max(total, prevScore), parScore,
                                        traces, Mathf.RoundToInt(Scoring.NoTraceRoomPts * scoreMult)),
            };
        }

        /// The next medal from the best time ("0.8 s to GOLD"), else what S still needs ("S needs: no TRACE"), else null.
        public static string NextTarget(float bestTime, float tDev, int bestScore, int parScore, int traces, int noTraceValue)
        {
            var medal = Medals.For(bestTime, tDev);
            if (tDev > 0f && medal != Medal.Sync)
            {
                var next = medal + 1;
                double gap = Math.Max(0.1, Math.Ceiling(Math.Round((bestTime - Medals.Threshold(next, tDev)) * 10.0, 3)) / 10.0);   // up to 0.1 s, float noise aside
                return gap.ToString("0.0", Inv) + " s to " + MedalNames[(int)next];
            }
            if (Scoring.Grade(bestScore, parScore) == 'S') return null;
            int need = parScore - bestScore;
            return traces > 0 && need <= noTraceValue ? "S needs: no TRACE" : "S needs: +" + need.ToString(Inv) + " pts";
        }

        /// DESYNC // 2026-10-24 // S A S B S A // 3:41.27 // GHOST x27 // desync-nine.vercel.app
        public static string ShareText(DateTime utc, string grades, float totalTime, int bestChain) =>
            "DESYNC // " + utc.ToString("yyyy-MM-dd", Inv) + " // " + grades + " // " + Clock(totalTime)
            + " // GHOST x" + bestChain.ToString(Inv) + " // desync-nine.vercel.app";

        /// Seconds as m:ss.cc ("3:41.27").
        public static string Clock(float seconds)
        {
            long cs = (long)Math.Round(Math.Max(0f, seconds) * 100.0, MidpointRounding.AwayFromZero);
            return (cs / 6000).ToString(Inv) + ":" + (cs / 100 % 60).ToString("00", Inv) + "." + (cs % 100).ToString("00", Inv);
        }

        // ---------- this session's run (share text) ----------

        static readonly char[] runGrades = new char[SceneFlow.RoomCount];
        static readonly float[] runTimes = new float[SceneFlow.RoomCount];
        static readonly int[] runChains = new int[SceneFlow.RoomCount];

        /// A new run (title / level select): the share text starts over.
        public static void ClearRun()
        {
            Array.Clear(runGrades, 0, runGrades.Length);
            Array.Clear(runTimes, 0, runTimes.Length);
            Array.Clear(runChains, 0, runChains.Length);
        }

        /// Records the room's latest clear in the run and returns the share text so far.
        public static string AddToRun(RoomReport r, int bestChain, DateTime utc)
        {
            if (r.room >= 0 && r.room < runGrades.Length) { runGrades[r.room] = r.grade; runTimes[r.room] = r.time; runChains[r.room] = bestChain; }
            var g = new StringBuilder(16);
            float t = 0f;
            int chain = 0;
            for (int i = 0; i < runGrades.Length; i++)
            {
                if (runGrades[i] == '\0') continue;
                if (g.Length > 0) g.Append(' ');
                g.Append(runGrades[i]);
                t += runTimes[i];
                chain = Math.Max(chain, runChains[i]);
            }
            return ShareText(utc, g.ToString(), t, chain);
        }

        // ---------- content ----------

        [Serializable] class RoomsFile { public RoomRow[] rooms; }
        [Serializable] class RoomRow { public string id; public int index; public string name; public float tDev; public int parScore; }
        [Serializable] class HintsFile { public string[] ice, melt, trace, glitch, drone, architect; }

        static RoomsFile rooms;
        static HintsFile hints;
        static bool roomsLoaded, hintsLoaded;

        /// tDev and parScore of room `index`; missing file or row: parTime / 1.25 and the BALANCE.md par formula
        /// (ROOM_CLEAR + NO_TRACE + terminals x 300 x 1.5 + 4 clean flips x 50 x 1.5).
        public static void RoomTargets(int index, int terminals, out float tDev, out int parScore)
        {
            if (!roomsLoaded) { roomsLoaded = true; rooms = Load<RoomsFile>("Content/rooms"); }
            if (rooms?.rooms != null)
                foreach (var r in rooms.rooms)
                    if (r != null && r.index == index && r.tDev > 0f) { tDev = r.tDev; parScore = r.parScore > 0 ? r.parScore : FallbackPar(terminals); return; }
            var par = GameConfig.I.parTime;
            tDev = par != null && index >= 0 && index < par.Length ? par[index] / 1.25f : 0f;
            parScore = FallbackPar(terminals);
        }

        static int FallbackPar(int terminals) => Scoring.RoomClearPts + Scoring.NoTraceRoomPts + terminals * 450 + 300;

        /// The n-th hint (0-based, wraps) for a death, or null when hints.json or that cause's list is missing.
        public static string Hint(DeathCause cause, string killerId, int n)
        {
            if (!hintsLoaded) { hintsLoaded = true; hints = Load<HintsFile>("Content/hints"); }
            if (hints == null) return null;
            bool glitch = cause == DeathCause.Glitch || (killerId != null && killerId.StartsWith(Player.MeltMeter.GlitchKiller, StringComparison.Ordinal));
            string[] list = glitch ? hints.glitch : cause switch
            {
                DeathCause.Ice => hints.ice,
                DeathCause.Melt => hints.melt,
                DeathCause.Drone => hints.drone,
                DeathCause.Architect => hints.architect,
                _ => hints.trace,   // Alarm, TraceTimer
            };
            return list == null || list.Length == 0 ? null : list[(n % list.Length + list.Length) % list.Length];
        }

        static T Load<T>(string path) where T : class
        {
            var ta = Resources.Load<TextAsset>(path);
            if (!ta) return null;
            try { return JsonUtility.FromJson<T>(ta.text); }
            catch (Exception e) { Debug.LogWarning("[DESYNC] " + path + ".json unreadable: " + e.Message); return null; }
        }
    }
}
