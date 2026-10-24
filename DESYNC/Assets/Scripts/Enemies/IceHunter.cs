using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Desync.Enemies
{
    /// Pooled ICE hunter. SOURCE: NavMeshAgent chase at iceSpeed on the floor-only NavMesh (straight-line chase when it
    /// spawned off the mesh), kills on touch. RENDER: frozen + flickering, harmless, despawns after iceDespawn seconds.
    /// Body-blocked by a hijacked drone. Alive = active hunters across the room (OnEnable/OnDisable).
    /// Every spawn gets a new id "ICE-<n>" (n counts spawns this attempt). GRAZE: a pass that spends >= GrazeTime in SOURCE
    /// within GrazeRange of the player without touching raises GameEvents.Grazed(id) when it ends.
    /// Subclasses change where it heads (Target), how fast (Speed) and whether it waits frozen in RENDER instead of
    /// despawning (DespawnsInRender): TrackerHunter (breadcrumb trail), PatrolHunter (AUDITOR pillars).
    public class IceHunter : MonoBehaviour, IResettable
    {
        public const float Radius = 0.35f, Repath = 0.2f, GrazeRange = 1f, GrazeTime = 0.15f;
        static readonly Vector3 Centre = new Vector3(0f, 0.6f, 0f);

        static readonly List<IceHunter> Active = new List<IceHunter>(8);
        public static int Alive => Active.Count;

        static readonly string[] Ids = MakeIds(128);   // built once: no per-spawn strings
        static int _spawns;

        public NavMeshAgent agent;
        public TrailRenderer trail;
        public Transform visual;
        public Renderer[] renderers;

        public string Id { get; private set; } = Ids[0];

        float _last, _repath, _renderSince = -1f, _graze;
        bool _shown = true;

        static string[] MakeIds(int n)
        {
            var a = new string[n];
            for (int i = 0; i < n; i++) a[i] = "ICE-" + i;
            return a;
        }

        /// Ids restart at ICE-1 (IceVent calls this on room load and on REBOOT).
        public static void ResetIds() => _spawns = 0;

        /// Move speed this frame (m/s).
        protected virtual float Speed => GameConfig.I.iceSpeed * GameState.Mods.IceSpeedMult;
        /// Where to head this frame; false = hold still (still lethal on touch). Hunters chase the player.
        protected virtual bool Target(out Vector3 target) { target = PlayerRef.Position; return true; }
        /// Hunters vanish iceDespawn s after the player returns to RENDER; persistent ICE waits there frozen.
        protected virtual bool DespawnsInRender => true;
        protected virtual void OnSpawned() { }
        protected virtual void Animate(float dt) { if (visual) visual.Rotate(0f, 240f * dt, 0f, Space.Self); }
        /// Re-issue the NavMesh destination on the next frame (the target moved to a new point).
        protected void Retarget() => _repath = 0f;

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
                agent.speed = Speed;
                agent.isStopped = false;
            }
            if (trail) trail.Clear();
            Id = Ids[Mathf.Min(++_spawns, Ids.Length - 1)];
            _graze = 0f;
            _last = RoomClock.Time;
            _repath = 0f;
            _renderSince = -1f;
            Show(true);
            OnSpawned();
        }

        public void Despawn()
        {
            if (agent && agent.enabled) agent.enabled = false;
            if (trail) trail.Clear();
            _graze = 0f;
            gameObject.SetActive(false);
        }

        void Update()
        {
            float dt = EnemyKit.Step(ref _last);
            bool chasing = agent.enabled && agent.isOnNavMesh;

            if (!LayerState.InSource)
            {
                EndPass();   // back in RENDER: a near pass that lasted counts as a graze
                if (_renderSince < 0f) _renderSince = RoomClock.Time;
                if (chasing) agent.isStopped = true;
                Show((Time.frameCount & 4) == 0);
                if (DespawnsInRender && RoomClock.Time - _renderSince >= GameConfig.I.iceDespawn) Despawn();
                return;
            }
            if (_renderSince >= 0f) { _renderSince = -1f; Show(true); }

            bool frozen = GameState.Cinematic || !GameState.Alive || !PlayerRef.Exists || dt <= 0f;
            if (chasing) agent.isStopped = frozen;
            if (frozen) { if (!GameState.Alive) _graze = 0f; return; }

            bool moving = Target(out Vector3 target);
            float speed = Speed;
            if (chasing)
            {
                agent.isStopped = !moving;
                agent.speed = speed;
                _repath -= dt;
                if (moving && _repath <= 0f) { _repath = Repath; agent.SetDestination(target); }
            }
            else if (moving)
            {
                var p = transform.position;
                transform.position = Vector3.MoveTowards(p, new Vector3(target.x, p.y, target.z), speed * dt);
            }
            BodyBlock(chasing);
            Animate(dt);
            Vector3 c = transform.position + Centre;
            if (EnemyKit.TouchesPlayer(c, Radius))
            {
                _graze = 0f;   // contact: no graze
                PlayerRef.LastKillerId = Id;
                PlayerRef.LastKillerPos = transform.position;
                PlayerRef.Kill(DeathCause.Ice);
            }
            else if (EnemyKit.TouchesPlayer(c, Radius + GrazeRange)) _graze += dt;
            else EndPass();
        }

        void EndPass()
        {
            if (_graze >= GrazeTime && GameState.Alive) GameEvents.Grazed?.Invoke(Id);
            _graze = 0f;
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
