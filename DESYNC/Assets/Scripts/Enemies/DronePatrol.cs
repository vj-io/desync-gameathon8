using UnityEngine;

namespace Desync.Enemies
{
    /// Guard drone. Movement is KINEMATIC and deterministic: it flies a closed loop through the waypoints at Speed,
    /// advanced by RoomClock deltas (no NavMeshAgent), hovering droneHover above them. RENDER: visible, solid (layer Drone),
    /// kills on body touch. SOURCE: hidden + harmless, but keeps flying. On a new Last-Known-Position (TraceSystem) it
    /// flies straight to it if the line is clear of walls, scans for HoldTime, flies back to where it left the loop and resumes.
    public class DronePatrol : MonoBehaviour, IResettable
    {
        public const float BodyRadius = 0.45f, KillRadius = 0.55f, HoldTime = 1.2f, RebootTime = 1.5f, MaxInvestigate = 16f;

        public Vector3[] waypoints;      // floor level, world space
        public float phase;              // seconds of patrol offset
        public VisionCone cone;
        public Transform body;           // visual root (bobs; hidden in SOURCE)
        public Collider[] colliders;
        public HijackedDrone hijack;
        public float speed = -1f;        // m/s along the loop and when investigating; <= 0 = GameConfig.droneSpeed (classic rooms)

        public float Speed => speed > 0f ? speed : GameConfig.I.droneSpeed;

        enum Mode { Patrol, Investigate, Hold, Return, Rebooting }

        Mode _mode;
        string _id;
        float _patrolT, _last, _timer, _yaw;
        int _lkpSeen;
        Vector3 _pos, _leave, _target;
        int _vis = -1;

        public bool Rebooting => _mode == Mode.Rebooting;

        void Awake() => ResetState();

        /// Position on the loop after `dist` metres (hover height applied). Shared with the factory for edit-time placement.
        public static Vector3 Evaluate(Vector3[] w, float dist)
        {
            int n = w.Length;
            float hover = GameConfig.I.droneHover;
            float len = 0f;
            for (int i = 0; i < n; i++) len += Vector3.Distance(w[i], w[(i + 1) % n]);
            if (len < 1e-3f) return w[0] + Vector3.up * hover;
            dist = Mathf.Repeat(dist, len);
            for (int i = 0; i < n; i++)
            {
                float s = Vector3.Distance(w[i], w[(i + 1) % n]);
                if (dist <= s && s > 1e-4f) return Vector3.Lerp(w[i], w[(i + 1) % n], dist / s) + Vector3.up * hover;
                dist -= s;
            }
            return w[0] + Vector3.up * hover;
        }

        Vector3 PathPos() => Evaluate(waypoints, _patrolT * Speed);

        void Update()
        {
            float dt = EnemyKit.Step(ref _last);
            if (hijack && hijack.Active) { Visibility(true, false); return; }   // HijackedDrone drives the transform
            if (GameState.Cinematic) dt = 0f;
            float step = Speed * dt;
            Vector3 prev = _pos;

            switch (_mode)
            {
                case Mode.Patrol:
                    _patrolT += dt;
                    _pos = PathPos();
                    CheckLkp();
                    break;
                case Mode.Investigate:
                    _pos = Vector3.MoveTowards(_pos, _target, step);
                    if ((_pos - _target).sqrMagnitude < 1e-4f) { _mode = Mode.Hold; _timer = HoldTime; }
                    break;
                case Mode.Hold:
                    _timer -= dt;
                    _yaw += 150f * dt;
                    if (_timer <= 0f) _mode = Mode.Return;
                    break;
                case Mode.Return:
                    _pos = Vector3.MoveTowards(_pos, _leave, step);
                    if ((_pos - _leave).sqrMagnitude < 1e-4f) _mode = Mode.Patrol;
                    break;
                case Mode.Rebooting:
                    _timer -= dt;
                    if (_timer <= 0f) { _mode = Mode.Patrol; if (cone) cone.Suppressed = false; }
                    break;
            }

            Vector3 mv = _pos - prev; mv.y = 0f;
            if (mv.sqrMagnitude > 1e-8f) _yaw = Mathf.MoveTowardsAngle(_yaw, Mathf.Atan2(mv.x, mv.z) * Mathf.Rad2Deg, 270f * dt);
            transform.SetPositionAndRotation(_pos, Quaternion.Euler(0f, _yaw, 0f));
            if (body) body.localPosition = new Vector3(0f, 0.06f * Mathf.Sin(RoomClock.Time * 3.9f), 0f);

            bool render = !LayerState.InSource;
            Visibility(render, render && _mode != Mode.Rebooting);
            if (_mode == Mode.Rebooting && body) body.gameObject.SetActive(render && (Time.frameCount & 4) == 0);
            if (render && _mode != Mode.Rebooting && GameState.Alive && !GameState.Cinematic && EnemyKit.TouchesPlayer(_pos, KillRadius))
            {
                PlayerRef.LastKillerId = Id;
                PlayerRef.LastKillerPos = _pos;
                PlayerRef.Kill(DeathCause.Drone);
            }
        }

        /// "DRONE-<n>", n = 1-based order among the drones under the same parent (LevelBuilder's order). Built on first use.
        public string Id
        {
            get
            {
                if (_id != null) return _id;
                int n = 1;
                var p = transform.parent;
                if (p) for (int i = 0, me = transform.GetSiblingIndex(); i < me; i++) if (p.GetChild(i).GetComponent<DronePatrol>()) n++;
                return _id = "DRONE-" + n;
            }
        }

        void CheckLkp()
        {
            var ts = TraceSystem.I;
            if (!ts || ts.LkpVersion == _lkpSeen) return;
            _lkpSeen = ts.LkpVersion;
            Vector3 target = ts.Lkp + Vector3.up * GameConfig.I.droneHover;
            if ((target - _pos).sqrMagnitude > MaxInvestigate * MaxInvestigate) return;
            if (Physics.Linecast(_pos, target, EnemyKit.SightMask, QueryTriggerInteraction.Ignore)) return;   // never through walls
            _leave = _pos;
            _target = target;
            _mode = Mode.Investigate;
        }

        void Visibility(bool visible, bool solid)
        {
            int v = (visible ? 1 : 0) | (solid ? 2 : 0);
            if (v == _vis) return;
            _vis = v;
            if (body) body.gameObject.SetActive(visible);
            foreach (var c in colliders) if (c) c.enabled = solid || (hijack && hijack.Active);
        }

        /// Called by HijackedDrone when the hijack ends: blink back to the frozen loop position, blind + harmless briefly.
        public void OnHijackEnded()
        {
            _mode = Mode.Rebooting;
            _timer = RebootTime;
            _pos = PathPos();
            _last = RoomClock.Time;
            _vis = -1;
            if (cone) cone.Suppressed = true;
            transform.position = _pos;
        }

        public void ResetState()
        {
            _mode = Mode.Patrol;
            _patrolT = phase;
            _last = 0f;
            _lkpSeen = 0;
            _vis = -1;
            if (waypoints == null || waypoints.Length == 0) waypoints = new[] { transform.position - Vector3.up * GameConfig.I.droneHover };
            _pos = PathPos();
            Vector3 ahead = Evaluate(waypoints, _patrolT * Speed + 0.1f) - _pos;
            _yaw = ahead.sqrMagnitude > 1e-6f ? Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg : transform.eulerAngles.y;
            transform.SetPositionAndRotation(_pos, Quaternion.Euler(0f, _yaw, 0f));
            if (body) body.gameObject.SetActive(true);
            if (cone) cone.Suppressed = false;
        }
    }
}
