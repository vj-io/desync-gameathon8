using UnityEngine;

namespace Desync.Enemies
{
    /// ICE hunter on a patrol loop (AUDITOR pillars; spec enemy 4): walks its loop at WalkMult x ICE speed in SOURCE,
    /// chases at full ICE speed once the player is within SightRange, and loses track LoseTime s after the player leaves
    /// that range, then walks back to the nearest loop point. Never despawns: frozen and harmless in RENDER.
    public class PatrolHunter : IceHunter
    {
        public const float SightRange = 6f, LoseTime = 2f, WalkMult = 0.55f, Reach = 0.4f;

        public Vector3[] patrol;   // floor-level loop, world space
        public int startIndex;

        int _wp;
        bool _hunting;
        float _seenAt;

        public bool Hunting => _hunting;
        public int Waypoint => _wp;

        protected override float Speed => base.Speed * (_hunting ? 1f : WalkMult);
        protected override bool DespawnsInRender => false;

        protected override void OnSpawned()
        {
            _hunting = false;
            _wp = patrol != null && patrol.Length > 0 ? (startIndex + 1) % patrol.Length : 0;
        }

        protected override bool Target(out Vector3 target)
        {
            Vector3 p = transform.position, me = PlayerRef.Position;
            float dx = me.x - p.x, dz = me.z - p.z;
            if (dx * dx + dz * dz <= SightRange * SightRange && Mathf.Abs(me.y - p.y) < 2.5f)
            {
                if (!_hunting) Retarget();
                _hunting = true;
                _seenAt = RoomClock.Time;
            }
            else if (_hunting && RoomClock.Time - _seenAt > LoseTime)
            {
                _hunting = false;
                _wp = Nearest(p);
                Retarget();
            }
            if (_hunting) { target = me; return true; }

            if (patrol == null || patrol.Length == 0) { target = p; return false; }
            target = patrol[_wp];
            float tx = target.x - p.x, tz = target.z - p.z;
            if (tx * tx + tz * tz < Reach * Reach)
            {
                _wp = (_wp + 1) % patrol.Length;
                target = patrol[_wp];
                Retarget();
            }
            return true;
        }

        int Nearest(Vector3 p)
        {
            int best = 0;
            float bd = float.MaxValue;
            for (int i = 0; patrol != null && i < patrol.Length; i++)
            {
                float d = (patrol[i] - p).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }
    }
}
