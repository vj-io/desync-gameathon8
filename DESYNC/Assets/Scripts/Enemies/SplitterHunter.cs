using UnityEngine;

namespace Desync.Enemies
{
    /// The Splitter ICE's hunter: a PatrolHunter whose loop is its post (patrol[0]); at the post and not hunting it waits.
    /// EnemyEvents.Alerted: Hunting when it starts a chase, Return when it loses track.
    public class SplitterHunter : PatrolHunter
    {
        bool _was;

        protected override void OnSpawned() { base.OnSpawned(); _was = false; }

        protected override bool Target(out Vector3 target)
        {
            bool moving = base.Target(out target);
            if (Hunting != _was)
            {
                _was = Hunting;
                EnemyEvents.Alerted?.Invoke(transform.position, _was ? Alert.Hunting : Alert.Return);
            }
            if (Hunting || patrol == null || patrol.Length == 0) return moving;
            Vector3 p = transform.position, d = patrol[0] - p;
            if (d.x * d.x + d.z * d.z > Reach * Reach) return moving;
            target = p;
            return false;
        }

        /// Twin crystal: spins faster while hunting (perception shown by motion, not hue).
        protected override void Animate(float dt)
        {
            if (visual) visual.Rotate(0f, (Hunting ? 420f : 160f) * dt, 0f, Space.Self);
        }
    }
}
