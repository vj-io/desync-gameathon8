using System.Collections.Generic;
using UnityEngine;

namespace Desync.Enemies
{
    /// Guard drone (and Lantern Drone). Movement is KINEMATIC and deterministic: it flies a closed loop through the waypoints
    /// at Speed, advanced by RoomClock deltas (no NavMeshAgent), hovering droneHover above them. RENDER: visible, solid
    /// (layer Drone), kills on body touch. SOURCE: hidden + harmless, but keeps flying.
    /// Perception ladder (spec FSM; EnemyEvents.Alerted on every change, cone colour by VisionCone):
    ///   Patrol      flies the loop. A new Last-Known-Position (TraceSystem: flip to SOURCE right after being seen) -> Hunting.
    ///   Suspicious  its cone sees the player: stops on the loop and turns toward them for SuspiciousTime.
    ///   Tracing     still seen: holds there, keeping the player in its cone (the cone's dwell starts the TRACE).
    ///   Hunting     sight lost while a TRACE runs (or an LKP, or a Noise decoy): flies straight to the last-seen point if
    ///               the line is clear of walls and within MaxInvestigate, scans for HoldTime; sees the player -> Tracing.
    ///   Return      flies back to where it left the loop, then Patrol; sees the player -> Suspicious.
    /// Runs after the default order so its cone's dwell and the player's position are this frame's.
    [DefaultExecutionOrder(10)]
    public class DronePatrol : MonoBehaviour, IResettable
    {
        public const float BodyRadius = 0.45f, KillRadius = 0.55f, HoldTime = 1.2f, RebootTime = 1.5f, MaxInvestigate = 16f,
                           TurnRate = 270f;

        static readonly List<DronePatrol> All = new List<DronePatrol>(16);

        public Vector3[] waypoints;      // floor level, world space
        public float phase;              // seconds of patrol offset
        public VisionCone cone;
        public Transform body;           // visual root (bobs; hidden in SOURCE)
        public Collider[] colliders;
        public HijackedDrone hijack;     // null on a Lantern Drone
        public float speed = -1f;        // m/s along the loop and when investigating; <= 0 = GameConfig.droneSpeed (classic rooms)

        public float Speed => speed > 0f ? speed : GameConfig.I.droneSpeed;

        enum Mode { Patrol, Suspicious, Tracing, Investigate, Hold, Return, Rebooting }

        Mode _mode;
        Alert _alert;
        string _id;
        float _patrolT, _last, _timer, _yaw, _until = -1f;
        int _lkpSeen;
        Vector3 _pos, _leave, _target, _seen;
        int _vis = -1;

        public bool Rebooting => _mode == Mode.Rebooting;
        public Alert Alert => _alert;

        void Awake() => ResetState();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

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
            bool sees = cone && cone.Dwell > 0f && _mode != Mode.Rebooting;   // a cone only dwells in RENDER
            if (sees) _seen = PlayerRef.Position;

            switch (_mode)
            {
                case Mode.Patrol:
                    if (sees) { Suspect(); break; }
                    _patrolT += dt;
                    _pos = PathPos();
                    CheckLkp();
                    break;
                case Mode.Suspicious:
                    if (!sees) { _mode = Mode.Return; break; }
                    Face(_seen, dt);
                    _timer -= dt;
                    if (_timer <= 0f) _mode = Mode.Tracing;
                    break;
                case Mode.Tracing:
                    if (sees) Face(_seen, dt);
                    else if (!GameState.TraceActive || !Hunt(_seen, -1f)) _mode = Mode.Return;
                    break;
                case Mode.Investigate:
                    if (sees) { _mode = Mode.Tracing; break; }
                    _pos = Vector3.MoveTowards(_pos, _target, step);
                    if ((_pos - _target).sqrMagnitude < 1e-4f) { _mode = Mode.Hold; _timer = Mathf.Max(HoldTime, _until - RoomClock.Time); }
                    break;
                case Mode.Hold:
                    if (sees) { _mode = Mode.Tracing; break; }
                    _timer -= dt;
                    _yaw += 150f * dt;
                    if (_timer <= 0f) _mode = Mode.Return;
                    break;
                case Mode.Return:
                    if (sees) { Suspect(); break; }
                    _pos = Vector3.MoveTowards(_pos, _leave, step);
                    if ((_pos - _leave).sqrMagnitude < 1e-4f) _mode = Mode.Patrol;
                    break;
                case Mode.Rebooting:
                    _timer -= dt;
                    if (_timer <= 0f) { _mode = Mode.Patrol; if (cone) cone.Suppressed = false; }
                    break;
            }

            Vector3 mv = _pos - prev; mv.y = 0f;
            if (mv.sqrMagnitude > 1e-8f) _yaw = Mathf.MoveTowardsAngle(_yaw, Mathf.Atan2(mv.x, mv.z) * Mathf.Rad2Deg, TurnRate * dt);
            transform.SetPositionAndRotation(_pos, Quaternion.Euler(0f, _yaw, 0f));
            if (body) body.localPosition = new Vector3(0f, 0.06f * Mathf.Sin(RoomClock.Time * 3.9f), 0f);
            Telegraph();

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

        void Suspect()
        {
            if (_mode == Mode.Patrol) _leave = _pos;   // from Return the loop point is already known
            _mode = Mode.Suspicious;
            _timer = EnemyEvents.SuspiciousTime;
        }

        void Face(Vector3 p, float dt)
        {
            Vector3 d = p - _pos;
            if (d.x * d.x + d.z * d.z > 1e-4f) _yaw = Mathf.MoveTowardsAngle(_yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, TurnRate * dt);
        }

        /// Fly to `feet` (hover height added) if the line is clear of walls and within MaxInvestigate; scan there until at
        /// least `until` (room time). False = out of reach, nothing changes.
        bool Hunt(Vector3 feet, float until)
        {
            Vector3 target = feet + Vector3.up * GameConfig.I.droneHover;
            if (!Reaches(target)) return false;
            if (_mode == Mode.Patrol) _leave = _pos;
            _target = target;
            _until = until;
            _mode = Mode.Investigate;
            return true;
        }

        bool Reaches(Vector3 target) => (target - _pos).sqrMagnitude <= MaxInvestigate * MaxInvestigate
                                        && !Physics.Linecast(_pos, target, EnemyKit.SightMask, QueryTriggerInteraction.Ignore);   // never through walls

        void CheckLkp()
        {
            var ts = TraceSystem.I;
            if (!ts || ts.LkpVersion == _lkpSeen) return;
            _lkpSeen = ts.LkpVersion;
            Hunt(ts.Lkp, -1f);
        }

        /// Noise decoy (Decoys.Spawn Noise): the nearest drone that is patrolling, returning or scanning and has a clear line
        /// to `feet` flies there and listens until `until` (room time). False when no drone can.
        internal static bool PullNearest(Vector3 feet, float until)
        {
            DronePatrol best = null;
            float bestSq = float.MaxValue;
            Vector3 target = feet + Vector3.up * GameConfig.I.droneHover;
            for (int i = 0; i < All.Count; i++)
            {
                var d = All[i];
                if ((d.hijack && d.hijack.Active) || (d._mode != Mode.Patrol && d._mode != Mode.Return && d._mode != Mode.Hold)) continue;
                float sq = (target - d._pos).sqrMagnitude;
                if (sq < bestSq && d.Reaches(target)) { bestSq = sq; best = d; }
            }
            return best && best.Hunt(feet, until);
        }

        void Telegraph()
        {
            var a = _mode == Mode.Suspicious ? Alert.Suspicious : _mode == Mode.Tracing ? Alert.Tracing
                  : _mode == Mode.Investigate || _mode == Mode.Hold ? Alert.Hunting : _mode == Mode.Return ? Alert.Return : Alert.Patrol;
            if (a == _alert) return;
            _alert = a;
            EnemyEvents.Alerted?.Invoke(_pos, a);
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
            _alert = Alert.Patrol;
            _patrolT = phase;
            _last = 0f;
            _lkpSeen = 0;
            _until = -1f;
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
