using UnityEngine;

namespace Desync.Enemies
{
    /// Tracker ICE nest (EntityKind.TrackerIce). Lays the player's SOURCE breadcrumbs from room start (Breadcrumbs.Tick);
    /// like a vent it counts SOURCE time only, and after iceSpawnInterval winds up for iceWindup (IceWindup + glow) and
    /// releases its ONE TrackerHunter: IceSpawned plus EnemyEvents.TrackerSpawned (the tracker's 1 s audio sting).
    /// The tracker stays out until the room resets.
    public class TrackerNest : MonoBehaviour, IResettable
    {
        public TrackerHunter hunter;
        public Renderer glow;

        float _t, _last;
        bool _winding;

        void Awake() { Breadcrumbs.Clear(); IceHunter.ResetIds(); }

        void Update()
        {
            Breadcrumbs.Tick();
            float dt = EnemyKit.Step(ref _last);
            if (!hunter || hunter.gameObject.activeSelf) return;
            var c = GameConfig.I;
            bool live = LayerState.InSource && GameState.Alive && !GameState.Cinematic && PlayerRef.Exists;
            if (live) _t += dt;
            bool wind = live && _t >= c.iceSpawnInterval - c.iceWindup;
            if (wind && !_winding) GameEvents.IceWindup?.Invoke(transform.position);
            _winding = wind;
            if (glow) glow.enabled = wind && (Time.frameCount & 2) == 0;
            if (!wind || _t < c.iceSpawnInterval) return;

            _t = 0f;
            _winding = false;
            if (glow) glow.enabled = false;
            hunter.Spawn(transform.position);
            GameEvents.IceSpawned?.Invoke(transform.position);
            EnemyEvents.TrackerSpawned?.Invoke(transform.position);
        }

        public void ResetState()
        {
            Breadcrumbs.Clear();
            IceHunter.ResetIds();
            _t = 0f;
            _last = 0f;
            _winding = false;
            if (glow) glow.enabled = false;
            if (hunter) hunter.Despawn();
        }
    }
}
