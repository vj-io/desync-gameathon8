using UnityEngine;

namespace Desync.Enemies
{
    /// ICE vent: accumulates RoomClock time only while the player is in SOURCE; every iceSpawnInterval
    /// (iceSpawnIntervalTraced during a TRACE) of SOURCE time it winds up for iceWindup (IceWindup + vent glow) and spawns a
    /// pooled hunter (IceSpawned), unless iceMaxAlive hunters are already out. The pool is prewarmed by EnemyFactory.
    /// Also publishes GameState.IceAlive / NearestIce every frame (IceHunter.Publish) for the spatial ICE hum.
    public class IceVent : MonoBehaviour, IResettable
    {
        public IceHunter[] pool;
        public Renderer glow;

        float _t, _last;
        bool _winding;

        void Update()
        {
            IceHunter.Publish();
            float dt = EnemyKit.Step(ref _last);
            var c = GameConfig.I;
            bool live = LayerState.InSource && GameState.Alive && !GameState.Cinematic && PlayerRef.Exists;
            float interval = GameState.TraceActive ? c.iceSpawnIntervalTraced : c.iceSpawnInterval;
            IceHunter free = Free();
            bool canSpawn = free && IceHunter.Alive < c.iceMaxAlive;
            if (live) _t += dt;
            if (!canSpawn) _t = Mathf.Min(_t, interval - c.iceWindup);   // a wind-up always precedes a spawn

            bool wind = live && canSpawn && _t >= interval - c.iceWindup;
            if (wind && !_winding) GameEvents.IceWindup?.Invoke(transform.position);
            _winding = wind;
            if (glow) glow.enabled = wind && (Time.frameCount & 2) == 0;

            if (wind && _t >= interval)
            {
                _t -= interval;
                free.Spawn(transform.position);
                GameEvents.IceSpawned?.Invoke(transform.position);
            }
        }

        IceHunter Free()
        {
            foreach (var h in pool) if (h && !h.gameObject.activeSelf) return h;
            return null;
        }

        public void ResetState()
        {
            _t = 0f;
            _last = 0f;
            _winding = false;
            if (glow) glow.enabled = false;
            foreach (var h in pool) if (h) h.Despawn();
        }
    }
}
