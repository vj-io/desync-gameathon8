using UnityEngine;

namespace Desync.Enemies
{
    /// Tracker ICE (spec enemy 5). Walks the player's SOURCE breadcrumb trail (Breadcrumbs: last 6 s) crumb by crumb at
    /// TrailSpeed — through every wall the player phased through — instead of cutting straight at the player. When the
    /// trail is cold (the player stayed in RENDER until every crumb expired) it holds where it is. Never despawns:
    /// in RENDER it waits frozen and harmless like any ICE. Released by a TrackerNest.
    public class TrackerHunter : IceHunter
    {
        public const float TrailSpeed = 4f, Reach = 0.35f;

        int _seq;   // next crumb to visit (Breadcrumbs sequence number)

        protected override float Speed => TrailSpeed * GameState.Mods.IceSpeedMult;
        protected override bool DespawnsInRender => false;
        protected override void OnSpawned() => _seq = Breadcrumbs.Oldest;

        protected override bool Target(out Vector3 target)
        {
            if (_seq < Breadcrumbs.Oldest) { _seq = Breadcrumbs.Oldest; Retarget(); }
            Vector3 p = transform.position;
            for (; _seq < Breadcrumbs.Next; _seq++, Retarget())
            {
                target = Breadcrumbs.Get(_seq);
                float dx = target.x - p.x, dz = target.z - p.z;
                if (dx * dx + dz * dz > Reach * Reach) return true;
            }
            target = p;
            return false;
        }

        /// Distinct from a hunter by motion, not hue: a fast spin and a 2 Hz pulse.
        protected override void Animate(float dt)
        {
            if (!visual) return;
            visual.Rotate(0f, 540f * dt, 0f, Space.Self);
            visual.localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(RoomClock.Time * 4f * Mathf.PI));
        }
    }

    /// The player's SOURCE trail, shared by every tracker in the room: a crumb at the player's feet every Interval s of
    /// RoomClock while in SOURCE, each living Life s. Live crumbs are sequence numbers [Oldest, Next). Fixed ring, no allocations.
    public static class Breadcrumbs
    {
        public const float Interval = 0.1f, Life = 6f;
        const int Cap = 64;   // > Life / Interval

        static readonly Vector3[] Pos = new Vector3[Cap];
        static readonly float[] At = new float[Cap];
        static float _lastAt = -1e9f;

        public static int Oldest { get; private set; }
        public static int Next { get; private set; }

        public static Vector3 Get(int seq) => Pos[seq % Cap];

        public static void Clear()
        {
            Oldest = Next = 0;
            _lastAt = -1e9f;
        }

        /// Expire old crumbs and lay a new one when due. Safe to call several times per frame.
        public static void Tick()
        {
            float now = RoomClock.Time;
            while (Oldest < Next && now - At[Oldest % Cap] > Life) Oldest++;
            if (!LayerState.InSource || !GameState.Alive || !PlayerRef.Exists) return;
            if (now >= _lastAt && now - _lastAt < Interval) return;
            _lastAt = now;
            Pos[Next % Cap] = PlayerRef.Position;
            At[Next % Cap] = now;
            Next++;
            if (Next - Oldest > Cap) Oldest = Next - Cap;
        }
    }
}
