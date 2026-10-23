using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Desync.Enemies
{
    /// Pooled ICE hunter. SOURCE: NavMeshAgent chase at iceSpeed on the floor-only NavMesh (straight-line chase when it
    /// spawned off the mesh), kills on touch. RENDER: frozen + flickering, harmless, despawns after iceDespawn seconds.
    /// Body-blocked by a hijacked drone. Alive = active hunters across the room (OnEnable/OnDisable).
    public class IceHunter : MonoBehaviour, IResettable
    {
        public const float Radius = 0.35f, Repath = 0.2f;
        static readonly Vector3 Centre = new Vector3(0f, 0.6f, 0f);

        static readonly List<IceHunter> Active = new List<IceHunter>(8);
        public static int Alive => Active.Count;

        public NavMeshAgent agent;
        public TrailRenderer trail;
        public Transform visual;
        public Renderer[] renderers;

        float _last, _repath, _renderSince = -1f;
        bool _shown = true;

        void OnEnable() { Active.Add(this); Publish(); }
        void OnDisable() { Active.Remove(this); GameState.IceAlive = Active.Count; }

        /// Writes GameState.IceAlive / NearestIce (nearest hunter to the player; audio hum follows it). Idempotent: every IceVent
        /// calls it each frame, hunters on enable. NearestIce is left untouched while none is alive.
        public static void Publish()
        {
            int n = Active.Count;
            GameState.IceAlive = n;
            if (n == 0) return;
            Vector3 from = PlayerRef.Position;
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = Active[i].transform.position + Centre;
                float d = (p - from).sqrMagnitude;
                if (d < best) { best = d; GameState.NearestIce = p; }
            }
        }

        public void Spawn(Vector3 pos)
        {
            transform.position = pos;
            gameObject.SetActive(true);
            bool onMesh = NavMesh.SamplePosition(pos, out var hit, 2f, NavMesh.AllAreas);
            agent.enabled = onMesh;
            if (onMesh)
            {
                agent.Warp(hit.position);
                agent.speed = GameConfig.I.iceSpeed;
                agent.isStopped = false;
            }
            if (trail) trail.Clear();
            _last = RoomClock.Time;
            _repath = 0f;
            _renderSince = -1f;
            Show(true);
        }

        public void Despawn()
        {
            if (agent && agent.enabled) agent.enabled = false;
            if (trail) trail.Clear();
            gameObject.SetActive(false);
        }

        void Update()
        {
            float dt = EnemyKit.Step(ref _last);
            bool chasing = agent.enabled && agent.isOnNavMesh;

            if (!LayerState.InSource)
            {
                if (_renderSince < 0f) _renderSince = RoomClock.Time;
                if (chasing) agent.isStopped = true;
                Show((Time.frameCount & 4) == 0);
                if (RoomClock.Time - _renderSince >= GameConfig.I.iceDespawn) Despawn();
                return;
            }
            if (_renderSince >= 0f) { _renderSince = -1f; Show(true); }

            bool frozen = GameState.Cinematic || !GameState.Alive || !PlayerRef.Exists || dt <= 0f;
            if (chasing) agent.isStopped = frozen;
            if (frozen) return;

            Vector3 target = PlayerRef.Position;
            if (chasing)
            {
                _repath -= dt;
                if (_repath <= 0f) { _repath = Repath; agent.SetDestination(target); }
            }
            else
            {
                var p = transform.position;
                transform.position = Vector3.MoveTowards(p, new Vector3(target.x, p.y, target.z), GameConfig.I.iceSpeed * dt);
            }
            BodyBlock(chasing);
            if (visual) visual.Rotate(0f, 240f * dt, 0f, Space.Self);
            if (EnemyKit.TouchesPlayer(transform.position + Centre, Radius)) PlayerRef.Kill(DeathCause.Ice);
        }

        void BodyBlock(bool chasing)
        {
            var h = HijackedDrone.Current;
            if (!h) return;
            Vector3 d = transform.position - h.transform.position;
            d.y = 0f;
            float m = d.magnitude;
            if (m >= HijackedDrone.BlockRadius) return;
            Vector3 push = (m > 1e-3f ? d / m : -h.transform.forward) * (HijackedDrone.BlockRadius - m);
            if (chasing) agent.Move(push); else transform.position += push;
        }

        void Show(bool on)
        {
            if (on == _shown) return;
            _shown = on;
            foreach (var r in renderers) if (r) r.enabled = on;
        }

        public void ResetState() => Despawn();
    }
}
