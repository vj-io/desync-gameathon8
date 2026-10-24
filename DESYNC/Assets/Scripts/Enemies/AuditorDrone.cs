using UnityEngine;

namespace Desync.Enemies
{
    /// Auditor Drone (spec enemy 8, RENDER). Not placed: when a TRACE starts in a room flagged auditor_drones (TRACE 2 and up)
    /// or under auditor_protocol (TRACE 1 and up, any room), one of a pooled pair under the room's TraceSystem is dispatched
    /// (EnemyEvents.AuditorDrone true). It appears just out of sight of the last-seen point (the longest clear line, up to
    /// ArriveDistance, of 8 compass directions) and flies there at Speed; it re-targets wherever its own cone sees the player
    /// and to every new Last-Known-Position (flip to SOURCE). Never through walls: with the line blocked it holds and scans.
    /// Kills on touch in RENDER; hidden and harmless in SOURCE. Recalled (EnemyEvents.AuditorDrone false) when the TRACE ends.
    [DefaultExecutionOrder(10)]   // after its cone: dwell and player position from the same frame
    public class AuditorDrone : MonoBehaviour, IResettable
    {
        public const float Speed = 5f, ArriveDistance = 6f, KillRadius = 0.55f, ScanTurn = 150f, ConeAngle = 45f, ConeRange = 8f;
        public const string KillerId = "AUDITOR-DRONE";

        static TraceSystem _owner;
        static AuditorDrone[] _pool;

        public VisionCone cone;
        public Transform body;

        Vector3 _target;
        float _last, _yaw;
        int _lkpSeen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            GameEvents.TraceStarted -= OnTrace;
            GameEvents.TraceStarted += OnTrace;
        }

        /// TRACE level `level` (1..3) just started.
        static void OnTrace(int level)
        {
            if (!EnemyFlags.AuditorDrones && !EnemyFlags.AuditorProtocol) return;
            var ts = TraceSystem.I;
            if (level < (EnemyFlags.AuditorProtocol ? 1 : 2) || !ts || !PlayerRef.Exists) return;
            if (_owner != ts || _pool == null) { _owner = ts; _pool = EnemyFactory.AuditorDrones(ts.transform); }   // once per room
            foreach (var d in _pool)
                if (d && !d.gameObject.activeSelf) { d.Dispatch(PlayerRef.Position); return; }
        }

        void Dispatch(Vector3 seenFeet)
        {
            _target = seenFeet + Vector3.up * GameConfig.I.droneHover;
            Vector3 from = _target;
            float best = -1f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float dist = Physics.Raycast(_target, dir, out var hit, ArriveDistance, EnemyKit.SightMask, QueryTriggerInteraction.Ignore)
                    ? hit.distance - 0.5f : ArriveDistance;
                if (dist <= best) continue;
                best = dist;
                from = _target + dir * Mathf.Max(0f, dist);
            }
            var ts = TraceSystem.I;
            _lkpSeen = ts ? ts.LkpVersion : 0;
            _last = RoomClock.Time;
            Vector3 d = _target - from;
            _yaw = d.sqrMagnitude > 1e-4f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : 0f;
            transform.SetPositionAndRotation(from, Quaternion.Euler(0f, _yaw, 0f));
            gameObject.SetActive(true);
            EnemyEvents.AuditorDrone?.Invoke(from, true);
        }

        void Update()
        {
            if (!GameState.TraceActive) { Recall(); return; }
            float dt = EnemyKit.Step(ref _last);
            if (GameState.Cinematic) dt = 0f;
            float hover = GameConfig.I.droneHover;
            var ts = TraceSystem.I;
            if (cone && cone.Dwell > 0f) _target = PlayerRef.Position + Vector3.up * hover;
            else if (ts && ts.LkpVersion != _lkpSeen) { _lkpSeen = ts.LkpVersion; _target = ts.Lkp + Vector3.up * hover; }

            Vector3 p = transform.position, d = _target - p;
            if (d.sqrMagnitude > 1e-4f && !Physics.Linecast(p, _target, EnemyKit.SightMask, QueryTriggerInteraction.Ignore))
            {
                p = Vector3.MoveTowards(p, _target, Speed * dt);
                if (d.x * d.x + d.z * d.z > 1e-4f) _yaw = Mathf.MoveTowardsAngle(_yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, DronePatrol.TurnRate * dt);
            }
            else _yaw += ScanTurn * dt;
            transform.SetPositionAndRotation(p, Quaternion.Euler(0f, _yaw, 0f));

            bool render = !LayerState.InSource;
            if (body && body.gameObject.activeSelf != render) body.gameObject.SetActive(render);
            if (render && GameState.Alive && !GameState.Cinematic && EnemyKit.TouchesPlayer(p, KillRadius))
            {
                PlayerRef.LastKillerId = KillerId;
                PlayerRef.LastKillerPos = p;
                PlayerRef.Kill(DeathCause.Drone);
            }
        }

        void Recall()
        {
            gameObject.SetActive(false);
            EnemyEvents.AuditorDrone?.Invoke(transform.position, false);
        }

        public void ResetState() { if (gameObject.activeSelf) gameObject.SetActive(false); }   // a REBOOT: no recall cue
    }
}
