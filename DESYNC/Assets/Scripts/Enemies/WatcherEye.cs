using UnityEngine;

namespace Desync.Enemies
{
    /// Watcher Eye cue: raises EnemyEvents.WatcherSpotted(lens position) each time its beam starts seeing the player.
    /// Runs after the default order so it reads the cone's dwell from the same frame.
    [DefaultExecutionOrder(10)]
    public class WatcherEye : MonoBehaviour, IResettable
    {
        public VisionCone cone;

        bool _seeing;

        void Update()
        {
            bool s = cone && cone.Dwell > 0f;
            if (s && !_seeing) EnemyEvents.WatcherSpotted?.Invoke(cone.transform.position);
            _seeing = s;
        }

        public void ResetState() => _seeing = false;
    }
}
