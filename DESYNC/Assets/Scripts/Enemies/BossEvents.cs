using System;
using Desync.Rules;
using UnityEngine;

namespace Desync.Enemies
{
    /// COMPILER / ARCHITECT events and shared boss state with no Core slot (the AUDITOR keeps EnemyEvents.AuditorPhase).
    public static class BossEvents
    {
        public const string Exposed = "Exposed", Merged = "Merged";

        /// ARCHITECT ending: Exposed (flipped in a gaze gap: the final string is severed) or Merged (no flip in a gap before
        /// the final timer ran out). Raised once, right before the boss raises ExitReached (the room's results).
        public static Action<string> Ending;
        /// A boss changed phase: (Compiler.Id / ArchitectBoss.Id, phase 2 or 3; 4 = beaten).
        public static Action<string, int> Phase;
        /// COMPILER: the swap walls changed layer (audio cue; the HUD telegraph reads Compiler.Current.SwapIn).
        public static Action WallSwap;
        /// ARCHITECT: puppet string i severed (0..2 = its terminal hacked, 3 = the final string). Worth PUPPET STRING.
        public static Action<int> StringSevered;

        /// Melt the current boss phase adds, in %/s: SOURCE rise + MeltSource, RENDER drain - MeltRender (COMPILER P2: 2 / 0;
        /// ARCHITECT P3: 3 / 3). The run manager folds it into GameState.Mods with Fold, at room start and on MeltChanged.
        public static float MeltSource, MeltRender;
        public static Action MeltChanged;

        public static Modifiers Fold(Modifiers m)
        {
            m.SourceRise += MeltSource;
            m.RenderDrain -= MeltRender;
            return m;
        }

        /// A boss room went or the run ended: no phase melt, no both-layers split, no Current boss (their own OnDisable does
        /// the same, but Destroy lands only at the end of the frame).
        public static void Reset()
        {
            SetMelt(0f, 0f);
            Shader.SetGlobalFloat(DesyncId, 0f);
            Auditor.Current = null;
            Compiler.Current = null;
            ArchitectBoss.Current = null;
        }

        static readonly int DesyncId = Shader.PropertyToID("_Desync");

        internal static void SetMelt(float source, float render)
        {
            if (MeltSource == source && MeltRender == render) return;
            MeltSource = source;
            MeltRender = render;
            MeltChanged?.Invoke();
        }

        /// One TRACE step right now, whether or not a TRACE runs (TraceSystem only counts a new TRACE): count + 1, timer
        /// refilled, TraceStarted, Alarm on the count that reaches tracesToAlarm.
        internal static void AddTrace(Vector3 seenAt)
        {
            var c = GameConfig.I;
            TraceSystem.Seen(seenAt);
            GameState.TraceActive = true;
            GameState.TraceRemaining = c.traceDuration;
            GameState.TraceCount++;
            GameEvents.TraceStarted?.Invoke(GameState.TraceCount);
            if (GameState.TraceCount == c.tracesToAlarm) GameEvents.Alarm?.Invoke();
        }

        /// Player position in a boss root's local space, flattened (x east, y = z north), for the centre-offset tables.
        internal static Vector2 Local(Transform root, Vector3 world)
        {
            Vector3 l = root.InverseTransformPoint(world);
            return new Vector2(l.x, l.z);
        }
    }
}
