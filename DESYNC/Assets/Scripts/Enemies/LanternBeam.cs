using System.Collections.Generic;
using UnityEngine;

namespace Desync.Enemies
{
    /// Lantern Drone (spec enemy 6, RENDER): a guard drone (DronePatrol: loop, perception ladder, kill on touch) whose cone
    /// sits on `beam`, turned a full circle every Period s of RoomClock in world space whatever the body does, so its
    /// Range m beam sweeps all around the loop. The turning beam is its tell (no new hue).
    public class LanternBeam : MonoBehaviour
    {
        public const float Period = 6f, Range = 7f, Angle = 40f, Pitch = 8f, Speed = 2f;

        static readonly List<LanternBeam> All = new List<LanternBeam>(8);

        public Transform beam;
        public float phase;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// World yaw of the beam at room time t.
        public float YawAt(float t) => 360f * Mathf.Repeat((t + phase) / Period, 1f);

        /// The active lantern nearest `from` (the audio hum polls this). False when the room has none. Zero GC.
        public static bool LanternNearest(Vector3 from, out Vector3 pos)
        {
            pos = default;
            float best = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                Vector3 p = All[i].transform.position;
                float d = (p - from).sqrMagnitude;
                if (d < best) { best = d; pos = p; }
            }
            return All.Count > 0;
        }

        void LateUpdate()   // after DronePatrol turned the body
        {
            if (beam) beam.rotation = Quaternion.Euler(0f, YawAt(RoomClock.Time), 0f);
        }
    }
}
