using UnityEngine;
using UnityEngine.AI;

namespace Desync.Enemies
{
    /// Daemon (spec enemy 10, BOTH): a SOURCE hunter at its post. In SOURCE it chases the player on sight (SightRange, same
    /// floor) at ICE speed (x Hardened ICE), loses track LoseTime s after, walks back to the post and waits; kills on touch.
    /// Flip to RENDER within CrossRange of it and it crosses with you (EnemyEvents.DaemonCrossed): solid and lethal in RENDER,
    /// chasing only along a clear line (walls stop it there), back to SOURCE-only LoseTime s after it lost you or when you
    /// flip to SOURCE. Otherwise RENDER keeps it frozen, flickering and harmless, like ICE. Moves on the floor NavMesh when
    /// one is under its post, else in straight lines. EnemyEvents.Alerted: Hunting / Return / Patrol (home).
    public class Daemon : MonoBehaviour, IResettable
    {
        public const float SightRange = 6f, LoseTime = 2f, CrossRange = 4f, Radius = 0.4f, WalkMult = 0.55f, Reach = 0.4f,
                           Repath = 0.2f;
        static readonly Vector3 Centre = new Vector3(0f, 0.8f, 0f);

        public NavMeshAgent agent;
        public Transform visual;
        public Renderer[] renderers;
        public Vector3 post;

        bool _hunting, _crossed, _placed, _shown = true;
        float _last, _seenAt, _repath;
        Alert _alert;
        string _id;

        public bool Hunting => _hunting;
        public bool Crossed => _crossed;
        public string Id => _id ??= EnemyKit.SiblingId<Daemon>(this, "DAEMON-");

        float Speed => GameConfig.I.iceSpeed * GameState.Mods.IceSpeedMult * (_hunting ? 1f : WalkMult);

        void OnEnable() => GameEvents.Flipped += OnFlipped;
        void OnDisable() => GameEvents.Flipped -= OnFlipped;

        void OnFlipped(bool toSource)
        {
            if (toSource) { Cross(false); return; }   // SOURCE is home
            if (!PlayerRef.Exists || !GameState.Alive) return;
            Vector3 d = PlayerRef.Position - transform.position;
            if (Mathf.Abs(d.y) >= 2.5f || d.x * d.x + d.z * d.z > CrossRange * CrossRange) return;
            _hunting = true;
            _seenAt = RoomClock.Time;
            Cross(true);
        }

        void Cross(bool on)
        {
            if (on == _crossed) return;
            _crossed = on;
            EnemyEvents.DaemonCrossed?.Invoke(transform.position, on);
        }

        void Update()
        {
            float dt = EnemyKit.Step(ref _last);
            if (!_placed) Place();
            bool source = LayerState.InSource;
            if (!source && !_crossed) { Stop(); Show((Time.frameCount & 4) == 0); return; }
            Show(true);
            if (GameState.Cinematic || !GameState.Alive || !PlayerRef.Exists || dt <= 0f) { Stop(); return; }

            Vector3 p = transform.position, me = PlayerRef.Position, d = me - p;
            Vector3 eye = PlayerRef.Head ? PlayerRef.Head.position : me + Vector3.up * 1.6f;
            bool clear = source || !Physics.Linecast(p + Centre, eye, EnemyKit.SightMask, QueryTriggerInteraction.Ignore);
            if (clear && Mathf.Abs(d.y) < 2.5f && d.x * d.x + d.z * d.z <= SightRange * SightRange)
            {
                _hunting = true;
                _seenAt = RoomClock.Time;
            }
            else if (_hunting && RoomClock.Time - _seenAt > LoseTime)
            {
                _hunting = false;
                Cross(false);
            }

            Vector3 home = post - p;
            bool atPost = home.x * home.x + home.z * home.z <= Reach * Reach;
            if (_hunting) Move(me, clear, dt);
            else Move(post, !atPost, dt);
            Telegraph(_hunting ? Alert.Hunting : atPost ? Alert.Patrol : Alert.Return);
            if (visual) visual.Rotate(0f, (_hunting ? 300f : 90f) * dt, 0f, Space.Self);

            if (EnemyKit.TouchesPlayer(transform.position + Centre, Radius))
            {
                PlayerRef.LastKillerId = Id;
                PlayerRef.LastKillerPos = transform.position;
                PlayerRef.Kill(DeathCause.Ice);
            }
        }

        void Move(Vector3 target, bool moving, float dt)
        {
            float speed = Speed;
            if (agent && agent.enabled && agent.isOnNavMesh)
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
        }

        void Stop()
        {
            if (agent && agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
        }

        /// To the post, onto the NavMesh when there is one (rooms wake their entities after the bake).
        void Place()
        {
            _placed = true;
            transform.position = post;
            if (!agent) return;
            bool onMesh = NavMesh.SamplePosition(post, out var hit, 2f, NavMesh.AllAreas);
            agent.enabled = onMesh;
            if (onMesh) agent.Warp(hit.position);
        }

        void Telegraph(Alert a)
        {
            if (a == _alert) return;
            _alert = a;
            EnemyEvents.Alerted?.Invoke(transform.position, a);
        }

        void Show(bool on)
        {
            if (on == _shown) return;
            _shown = on;
            foreach (var r in renderers) if (r) r.enabled = on;
        }

        public void ResetState()
        {
            _hunting = _crossed = _placed = false;
            _alert = Alert.Patrol;
            _last = _repath = 0f;
            if (agent && agent.enabled) agent.enabled = false;
            transform.position = post;
            Show(true);
        }
    }
}
