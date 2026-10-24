using System.Collections.Generic;
using UnityEngine;

namespace Desync.Enemies
{
    /// HALCYON security officer: a person, so a RENDER threat; flipping to SOURCE is the escape (BREACH non-negotiables 1-2).
    /// Kinematic and RoomClock-driven like the drones. Walls stop him: he never uses the room NavMesh, which is baked over the
    /// Floor layer only (ICE crosses walls on it). He walks straight legs his body fits along (a sphere cast at knee height and a
    /// line at chest height against WallRender|Floor); when the leg to his goal is blocked he heads for the newest point of the
    /// player's RENDER trail (GuardTrail: where the player walked, so walkable) that he can reach in a straight leg, and his own
    /// breadcrumbs lead him back to his loop. Whatever still gets in the way he slides along.
    /// Perception ladder (EnemyEvents.Alerted on each change; cone colour by VisionCone; "?" / "!" over his head):
    ///   Patrol      his loop at PatrolSpeed (one waypoint = a post: he stands there scanning +/-30 deg). Hears a Noise decoy
    ///               within HearRange -> Investigate.
    ///   Suspicious  his flashlight (VisionCone 70 deg x 10 m, x Mods.CameraRangeMult; TRACE like a camera; BLIND CAMS leaves
    ///               people alone) sees the player: he stops and turns body and head to them for SuspiciousTime. Lost -> Return.
    ///   Chase       "!" (GuardAlert) and ChaseSpeed after the player (who walks 5 m/s); out of sight for LoseTime -> Search.
    ///   Windup      within StrikeRange on a clear line: WindupTime baton raise (GuardWindup, red glow, crackle), then the strike
    ///               lands if the player is still within HitRange on a clear line (Kill(DeathCause.Guard)), else Recover.
    ///   Search      to the last-known position (TraceSystem's LKP when newer than his own sighting), scans, then Return.
    ///   Investigate to a Noise decoy, listens until it ends, then Return.
    ///   Return      back along his breadcrumbs to where he left the loop, then Patrol.
    /// SOURCE: frozen mid-pose, timers and all (mat.world draws him as a glitched wireframe statue), cannot see or strike, and his
    /// body lets the player through. Back in RENDER he carries on where he stopped, so flipping back next to one is dangerous.
    /// A hijacked drone body-blocks him. At most MaxActive per room are awake. Runs after VisionCone so its dwell is this frame's.
    [DefaultExecutionOrder(10)]
    public class Guard : MonoBehaviour, IResettable
    {
        public const float PatrolSpeed = 1.8f, ChaseSpeed = 4.2f, SearchSpeed = 2.6f, SuspiciousTime = 0.4f, LoseTime = 3f,
                           SearchTime = 2.5f, StrikeRange = 1.6f, HitRange = 1.8f, WindupTime = 0.35f, RecoverTime = 0.5f,
                           ConeAngle = 70f, ConeRange = 10f, ConePitch = 8f, EyeHeight = 1.65f, HearRange = 12f,
                           Radius = 0.3f, Height = 1.75f, StepUp = 0.4f, Knee = 0.55f, Chest = 1.3f, Skin = 0.03f,
                           PatrolTurn = 360f, ChaseTurn = 540f, ScanArc = 70f, PostArc = 30f, PostPeriod = 6f,
                           Replan = 0.2f, StuckTime = 0.8f, CrumbSpacing = 0.5f, HitStop = 0.08f, HitShake = 0.8f;
        public const int MaxActive = 3;

        /// Natural ground speed of the walk / run clips (m/s): their playback speed follows the real speed.
        public const float WalkClipSpeed = 1.34f, RunClipSpeed = 2.39f;
        public static readonly string[] Clips = { "idle", "walk", "run", "attack" };

        const int MoveMask = (1 << GameLayers.WallRender) | (1 << GameLayers.Floor);
        const int Crumbs = 96;

        static readonly List<Guard> All = new List<Guard>(4);
        static int _chasing;

        /// Officers chasing right now (Chase, Windup, Recover). EnemyEvents.GuardChase fires when this leaves / returns to 0.
        public static int Chasing => _chasing;

        public Vector3[] waypoints;      // floor level, world space; one point = a post
        public float phase;              // seconds of patrol offset
        public float postYaw;            // facing at a post
        public VisionCone cone;
        public Transform visual;         // model root (jitters while frozen)
        public Animation anim;           // legacy clips idle / walk / run / attack (null on the stand-in body)
        public Transform head;           // head bone: the "?" head turn
        public GameObject glow;          // baton tip, lit during the wind-up
        public GameObject markQ, markX;  // "?" and "!"
        public Collider body;            // capsule on the Drone layer: blocks the player in RENDER

        enum Mode { Patrol, Suspicious, Chase, Windup, Recover, Search, Investigate, Return }

        Mode _mode;
        Alert _alert;
        string _id;
        float _last, _patrolT, _timer, _seenAt, _lkpAt, _replan, _stuckT, _noiseUntil, _scanYaw, _yaw, _pop, _animSpeed, _stepDist;
        int _lkpVer, _crumbN, _clip = -1;
        bool _frozen, _dormant, _scanning, _isChasing;
        Vector3 _pos, _seen, _lkp, _searchPos, _noisePos, _sub, _leave;
        float _searchCut;
        readonly Vector3[] _crumbs = new Vector3[Crumbs];
        AnimationState[] _states;

        public string Id => _id ??= EnemyKit.SiblingId<Guard>(this, "GUARD-");
        public Alert Alert => _alert;
        public bool Frozen => _frozen;
        public bool IsChasing => _isChasing;
        public bool Dormant => _dormant;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); _chasing = 0; GuardTrail.Clear(); }

        void Awake()
        {
            if (anim)
            {
                _states = new AnimationState[Clips.Length];
                for (int i = 0; i < Clips.Length; i++) _states[i] = anim[Clips[i]];
            }
            ResetState();
        }

        void OnEnable()
        {
            All.Add(this);
            _dormant = All.Count > MaxActive;
            if (_dormant) Debug.LogWarning($"[DESYNC] ENEMIES: more than {MaxActive} security officers in this room; {name} stays asleep");
            Show(!_dormant);
        }

        void OnDisable()
        {
            All.Remove(this);
            SetChasing(false);
        }

        void OnDestroy() => SetChasing(false);   // a disabled officer still counts while chasing

        // ------------------------------------------------------------------------------------------------ frame

        void Update()
        {
            float dt = EnemyKit.Step(ref _last);
            if (_dormant) return;
            Freeze(LayerState.InSource);
            if (_frozen) { Jitter(); return; }
            if (dt <= 0f || GameState.Cinematic || !GameState.Alive || !PlayerRef.Exists) return;

            GuardTrail.Record();
            PollLkp();
            bool sees = cone && cone.Dwell > 0f;
            if (sees) { _seen = PlayerRef.Position; _seenAt = RoomClock.Time; }
            Vector3 prev = _pos;

            switch (_mode)
            {
                case Mode.Patrol:
                    if (sees) { Suspect(); break; }
                    if (Heard()) break;
                    _patrolT += dt;
                    if (waypoints.Length > 1)
                    {
                        _pos = PathPos();
                        Face(_pos - prev, PatrolTurn, dt);
                    }
                    else _yaw = Mathf.MoveTowardsAngle(_yaw, postYaw + PostArc * Mathf.Sin(_patrolT * 2f * Mathf.PI / PostPeriod), PatrolTurn * dt);
                    break;
                case Mode.Suspicious:
                    if (!sees) { StartReturn(); break; }
                    Face(_seen - _pos, PatrolTurn, dt);
                    _timer -= dt;
                    if (_timer <= 0f) StartChase();
                    break;
                case Mode.Chase:
                    if (!sees && RoomClock.Time - _seenAt > LoseTime) { StartSearch(); break; }
                    if (sees && Near(StrikeRange) && Clear(_pos, PlayerRef.Position)) { StartWindup(); break; }
                    if (Go(sees ? PlayerRef.Position : _seen, sees ? RoomClock.Time : _seenAt, ChaseSpeed, dt) && sees)
                        Face(PlayerRef.Position - _pos, ChaseTurn, dt);
                    if (!sees && _stuckT > StuckTime) StartSearch();
                    break;
                case Mode.Windup:
                    Face(PlayerRef.Position - _pos, ChaseTurn, dt);
                    _timer -= dt;
                    if (_timer <= 0f) Strike();
                    break;
                case Mode.Recover:
                    _timer -= dt;
                    if (_timer <= 0f) { if (sees) SetMode(Mode.Chase); else StartSearch(); }
                    break;
                case Mode.Search:
                    if (sees) { StartChase(); break; }
                    if (Scan(dt)) StartReturn();
                    else if (!_scanning && (Go(_searchPos, _searchCut, SearchSpeed, dt) || _stuckT > StuckTime)) BeginScan(SearchTime);
                    break;
                case Mode.Investigate:
                    if (sees) { Suspect(); break; }
                    if (RoomClock.Time >= _noiseUntil) { StartReturn(); break; }
                    if (Scan(dt)) BeginScan(SearchTime);
                    else if (!_scanning && (Go(_noisePos, RoomClock.Time, SearchSpeed, dt) || _stuckT > StuckTime)) BeginScan(SearchTime);
                    break;
                case Mode.Return:
                    if (sees) { Suspect(); break; }
                    if (Heard()) break;
                    GoBack(dt);
                    break;
            }

            if (_mode != Mode.Patrol) Separate();   // on the loop he is wherever room time puts him
            float moved = Flat(_pos - prev).magnitude;
            if (_mode != Mode.Patrol && _mode != Mode.Return) Crumb();
            Footsteps(moved);
            transform.SetPositionAndRotation(_pos, Quaternion.Euler(0f, _yaw, 0f));
            Animate(moved / dt, dt);
            Telegraph();
        }

        void LateUpdate()
        {
            if (_dormant) return;
            if (_pop > 0f && !_frozen) _pop = Mathf.Max(0f, _pop - Time.deltaTime);
            var cam = PlayerRef.Cam ? PlayerRef.Cam : Camera.main;
            Billboard(markQ, cam);
            Billboard(markX, cam);
            // "?" head turn: the head leads the body toward what he glimpsed (after the clip has posed it this frame).
            if (head && !_frozen && _mode == Mode.Suspicious)
            {
                Vector3 d = _seen - _pos;
                float off = Mathf.Clamp(Mathf.DeltaAngle(_yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg), -50f, 50f);
                head.rotation = Quaternion.AngleAxis(off, Vector3.up) * head.rotation;
            }
        }

        void Billboard(GameObject m, Camera cam)
        {
            if (!m || !m.activeSelf) return;
            var t = m.transform;
            if (cam) t.rotation = Quaternion.LookRotation(t.position - cam.transform.position);
            float k = _pop / 0.25f;
            t.localScale = Vector3.one * (1f + 0.9f * k * k);
        }

        // ------------------------------------------------------------------------------------------------ transitions

        /// Leaving the loop: remember where, start the breadcrumbs there.
        void LeaveLoop()
        {
            if (_mode != Mode.Patrol) return;
            _leave = _pos;
            _crumbN = 0;
            _crumbs[_crumbN++] = _pos;
        }

        void SetMode(Mode m)
        {
            LeaveLoop();
            _mode = m;
            _stuckT = 0f;
            _replan = 0f;
            _scanning = false;
            SetChasing(m == Mode.Chase || m == Mode.Windup || m == Mode.Recover);
        }

        void Suspect()
        {
            SetMode(Mode.Suspicious);
            _timer = SuspiciousTime;
            Mark(1);
        }

        void StartChase()
        {
            SetMode(Mode.Chase);
            Mark(2);
            EnemyEvents.GuardAlert?.Invoke(_pos);
        }

        void StartWindup()
        {
            SetMode(Mode.Windup);
            _timer = WindupTime;
            if (glow) glow.SetActive(true);
            EnemyEvents.GuardWindup?.Invoke(_pos);
        }

        void Strike()
        {
            if (glow) glow.SetActive(false);
            bool hit = GameState.Alive && !LayerState.InSource && Near(HitRange) && Clear(_pos, PlayerRef.Position);
            EnemyEvents.GuardStrike?.Invoke(_pos, hit);
            SetMode(Mode.Recover);
            _timer = RecoverTime;
            if (!hit) return;
            PlayerRef.LastKillerId = Id;
            PlayerRef.LastKillerPos = _pos;
            GameEvents.HitStopRequest?.Invoke(HitStop);
            GameEvents.ShakeRequest?.Invoke(HitShake);
            PlayerRef.Kill(DeathCause.Guard);
        }

        /// To the last-known position: TraceSystem's LKP when it is newer than his own last sighting, else that sighting.
        void StartSearch()
        {
            bool lkp = _lkpAt > _seenAt;
            _searchPos = lkp ? _lkp : _seen;
            _searchCut = lkp ? _lkpAt : _seenAt;
            SetMode(Mode.Search);
            Mark(1);
        }

        void StartReturn()
        {
            SetMode(Mode.Return);
            Mark(0);
        }

        /// A Noise decoy within HearRange pulls him (Patrol / Return).
        bool Heard()
        {
            float best = HearRange * HearRange;
            bool found = false;
            for (int i = 0; i < Decoys.Capacity; i++)
            {
                if (!Decoys.TryGet(i, out var p, out var kind, out float left) || kind != DecoyKind.Noise) continue;
                float d = Flat(p - _pos).sqrMagnitude;
                if (d > best) continue;
                best = d;
                _noisePos = p;
                _noiseUntil = RoomClock.Time + left;
                found = true;
            }
            if (!found) return false;
            SetMode(Mode.Investigate);
            Mark(1);
            return true;
        }

        void BeginScan(float seconds)
        {
            _scanning = true;
            _timer = seconds;
            _scanYaw = _yaw;
        }

        /// Looking around (+/-ScanArc) while scanning; true when the scan is over.
        bool Scan(float dt)
        {
            if (!_scanning) return false;
            _timer -= dt;
            _yaw = _scanYaw + ScanArc * Mathf.Sin((SearchTime - _timer) * 2f * Mathf.PI / SearchTime);
            return _timer <= 0f;
        }

        void PollLkp()
        {
            var ts = TraceSystem.I;
            if (!ts || ts.LkpVersion == _lkpVer) return;
            if (ts.LkpVersion > _lkpVer) { _lkp = ts.Lkp; _lkpAt = RoomClock.Time; }
            _lkpVer = ts.LkpVersion;
        }

        void SetChasing(bool on)
        {
            if (on == _isChasing) return;
            _isChasing = on;
            _chasing += on ? 1 : -1;
            if (on ? _chasing == 1 : _chasing == 0) EnemyEvents.GuardChase?.Invoke(on);
        }

        // ------------------------------------------------------------------------------------------------ moving

        Vector3 PathPos() => DronePatrol.Evaluate(waypoints, _patrolT * PatrolSpeed) - Vector3.up * GameConfig.I.droneHover;

        /// Toward `goal`: straight when the leg is clear, else via the player's trail (points no later than `cutoff`). True on arrival.
        bool Go(Vector3 goal, float cutoff, float speed, float dt)
        {
            if (Reached(goal)) { _stuckT = 0f; return true; }
            if (Direct(goal)) _sub = goal;
            else if ((_replan -= dt) <= 0f || Reached(_sub))
            {
                _replan = Replan;
                _sub = GuardTrail.Toward(this, cutoff, out var p) ? p : goal;
            }
            Step(_sub, speed, dt);
            return false;
        }

        /// Back to where he left the loop: straight if he can, else the earliest breadcrumb he can reach in a straight leg.
        void GoBack(float dt)
        {
            if (Reached(_leave))
            {
                _pos = _leave;
                _crumbN = 0;
                _mode = Mode.Patrol;
                return;
            }
            if ((_replan -= dt) <= 0f || Reached(_sub))
            {
                _replan = Replan;
                _sub = _leave;
                if (!Direct(_leave))
                {
                    int stride = Mathf.Max(1, _crumbN / 24);
                    _sub = _crumbN > 0 ? _crumbs[_crumbN - 1] : _leave;
                    for (int i = 0; i < _crumbN; i += stride)
                        if (Reachable(_crumbs[i])) { _sub = _crumbs[i]; break; }
                }
            }
            Step(_sub, PatrolSpeed, dt);
        }

        void Step(Vector3 target, float speed, float dt)
        {
            Vector3 d = target - _pos;
            float dy = d.y;
            d.y = 0f;
            float m = d.magnitude;
            if (m < 0.02f) { _pos.y = Mathf.MoveTowards(_pos.y, target.y, speed * dt); return; }
            Vector3 dir = d / m;
            float want = Mathf.Min(m, speed * dt);
            Vector3 delta = Slide(dir, want);
            float got = delta.magnitude;
            _pos += delta;
            _pos.y += dy * Mathf.Clamp01(got / m);
            _stuckT = got < want * 0.25f ? _stuckT + dt : 0f;
            Face(dir, _mode == Mode.Chase ? ChaseTurn : PatrolTurn, dt);
        }

        /// Capsule cast along dir; on a hit, the rest of the step slides along the surface (one bounce).
        Vector3 Slide(Vector3 dir, float dist)
        {
            Vector3 a = _pos + Vector3.up * (StepUp + Radius), b = _pos + Vector3.up * (Height - Radius);
            if (!Physics.CapsuleCast(a, b, Radius, dir, out var hit, dist + Skin, MoveMask, QueryTriggerInteraction.Ignore))
                return dir * dist;
            float free = Mathf.Max(0f, hit.distance - Skin);
            Vector3 moved = dir * free, n = hit.normal;
            n.y = 0f;
            if (n.sqrMagnitude < 1e-6f) return moved;
            n.Normalize();
            Vector3 rest = dir * (dist - free);
            rest -= Vector3.Dot(rest, n) * n;
            float rm = rest.magnitude;
            if (rm < 1e-5f) return moved;
            Vector3 rd = rest / rm;
            if (Physics.CapsuleCast(a + moved, b + moved, Radius, rd, out var h2, rm + Skin, MoveMask, QueryTriggerInteraction.Ignore))
                rm = Mathf.Max(0f, h2.distance - Skin);
            return moved + rd * rm;
        }

        /// Keeps clear of a hijacked drone (body-block) and of the other officers; never pushed into a wall.
        void Separate()
        {
            Vector3 before = _pos;
            var h = HijackedDrone.Current;
            if (h) Push(h.transform.position, HijackedDrone.BlockRadius);
            for (int i = 0; i < All.Count; i++)
            {
                var g = All[i];
                if (g != this && !g._dormant) Push(g._pos, Radius * 2f + 0.1f);
            }
            if (_pos != before && Physics.CheckCapsule(_pos + Vector3.up * (StepUp + Radius), _pos + Vector3.up * (Height - Radius), Radius,
                                                       MoveMask, QueryTriggerInteraction.Ignore))
                _pos = before;
        }

        void Push(Vector3 from, float radius)
        {
            Vector3 d = Flat(_pos - from);
            float m = d.magnitude;
            if (m >= radius) return;
            _pos += (m > 1e-3f ? d / m : Quaternion.Euler(0f, _yaw, 0f) * Vector3.back) * (radius - m);
        }

        void Face(Vector3 dir, float rate, float dt)
        {
            if (dir.x * dir.x + dir.z * dir.z > 1e-8f) _yaw = Mathf.MoveTowardsAngle(_yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, rate * dt);
        }

        void Crumb()
        {
            if (_crumbN > 0 && Flat(_pos - _crumbs[_crumbN - 1]).sqrMagnitude < CrumbSpacing * CrumbSpacing) return;
            if (_crumbN == Crumbs)   // full: keep every other one (the first stays: where he left the loop)
            {
                for (int i = 1; i < Crumbs / 2; i++) _crumbs[i] = _crumbs[i * 2];
                _crumbN = Crumbs / 2;
            }
            _crumbs[_crumbN++] = _pos;
        }

        bool Reached(Vector3 p) => Flat(p - _pos).sqrMagnitude < 0.09f && Mathf.Abs(p.y - _pos.y) < 1f;
        bool Near(float range) => Flat(PlayerRef.Position - _pos).sqrMagnitude <= range * range && Mathf.Abs(PlayerRef.Position.y - _pos.y) < 1f;
        bool Direct(Vector3 goal) => Mathf.Abs(goal.y - _pos.y) < 0.6f && Clear(_pos, goal);
        internal bool Reachable(Vector3 p) => Mathf.Abs(p.y - _pos.y) < 1.2f && Clear(_pos, p);

        /// His body fits along the straight leg: a sphere at knee height (desks, walls) and a line at chest height.
        internal static bool Clear(Vector3 from, Vector3 to)
        {
            Vector3 a = from + Vector3.up * Knee, d = to - from;
            float m = d.magnitude;
            if (m < 1e-3f) return true;
            if (Physics.SphereCast(a, Radius * 0.85f, d / m, out _, m, MoveMask, QueryTriggerInteraction.Ignore)) return false;
            return !Physics.Linecast(from + Vector3.up * Chest, to + Vector3.up * Chest, MoveMask, QueryTriggerInteraction.Ignore);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        // ------------------------------------------------------------------------------------------------ feedback

        void Footsteps(float moved)
        {
            if (moved <= 0f) return;
            _stepDist += moved;
            float stride = _mode == Mode.Chase ? 1.05f : 0.7f;
            if (_stepDist < stride) return;
            _stepDist -= stride;
            EnemyEvents.GuardStep?.Invoke(_pos);
        }

        /// 0 none, 1 "?", 2 "!"; each change pops.
        void Mark(int m)
        {
            bool q = m == 1, x = m == 2;
            if (markQ && markQ.activeSelf != q) markQ.SetActive(q);
            if (markX && markX.activeSelf != x) markX.SetActive(x);
            if (m != 0) _pop = 0.25f;
        }

        void Telegraph()
        {
            var a = _mode == Mode.Patrol ? Alert.Patrol : _mode == Mode.Suspicious ? Alert.Suspicious
                  : _mode == Mode.Return ? Alert.Return : Alert.Hunting;
            if (a == _alert) return;
            _alert = a;
            EnemyEvents.Alerted?.Invoke(_pos, a);
        }

        /// Clip by state and real speed (0 idle, 1 walk, 2 run, 3 attack); walk / run play at the speed they cover the ground.
        void Animate(float speed, float dt)
        {
            if (_states == null) return;
            _animSpeed = dt > 0f ? Mathf.Lerp(_animSpeed, speed, Mathf.Clamp01(dt * 12f)) : 0f;
            int want = _mode == Mode.Windup || (_mode == Mode.Recover && _timer > RecoverTime - 0.3f) ? 3
                     : _animSpeed > 2.6f ? 2 : _animSpeed > 0.25f ? 1 : 0;
            if (want != _clip && _states[want] != null)
            {
                _clip = want;
                if (want == 3) anim.Rewind(Clips[3]);
                anim.CrossFade(Clips[want], want == 3 ? 0.05f : 0.15f);
            }
            if (want == 1 && _states[1] != null) _states[1].speed = Mathf.Clamp(_animSpeed / WalkClipSpeed, 0.6f, 1.6f);
            else if (want == 2 && _states[2] != null) _states[2].speed = Mathf.Clamp(_animSpeed / RunClipSpeed, 0.8f, 1.8f);
        }

        /// SOURCE: pose held, body passable; RENDER: back to normal.
        void Freeze(bool frozen)
        {
            if (frozen == _frozen) return;
            _frozen = frozen;
            if (anim) anim.enabled = !frozen;
            if (body) body.enabled = !frozen;
            if (!frozen && visual) visual.localPosition = Vector3.zero;
        }

        /// The statue glitches: a few frames in eight it slips a few centimetres.
        void Jitter()
        {
            if (!visual) return;
            int f = Time.frameCount;
            visual.localPosition = (f & 7) < 2 ? new Vector3(((f * 37) % 7 - 3) * 0.012f, 0f, ((f * 53) % 7 - 3) * 0.012f) : Vector3.zero;
        }

        void Show(bool on)
        {
            if (visual) visual.gameObject.SetActive(on);
            if (cone) cone.Suppressed = !on;
            if (body) body.enabled = on;
        }

        // ------------------------------------------------------------------------------------------------ reset

        public void ResetState()
        {
            _mode = Mode.Patrol;
            _alert = Alert.Patrol;
            _patrolT = phase;
            _last = 0f;
            _timer = _replan = _stuckT = _pop = _animSpeed = _stepDist = 0f;
            _seenAt = _lkpAt = -1e9f;
            _lkpVer = TraceSystem.I ? TraceSystem.I.LkpVersion : 0;
            _crumbN = 0;
            _scanning = false;
            _clip = -1;
            SetChasing(false);
            Mark(0);
            if (glow) glow.SetActive(false);
            _frozen = false;
            if (anim) anim.enabled = true;
            if (body) body.enabled = !_dormant;
            if (visual) visual.localPosition = Vector3.zero;
            if (waypoints == null || waypoints.Length == 0) waypoints = new[] { transform.position };
            _pos = _leave = _sub = PathPos();
            _yaw = postYaw;
            if (waypoints.Length > 1)
            {
                Vector3 ahead = DronePatrol.Evaluate(waypoints, _patrolT * PatrolSpeed + 0.1f) - Vector3.up * GameConfig.I.droneHover - _pos;
                if (ahead.sqrMagnitude > 1e-6f) _yaw = Mathf.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg;
            }
            transform.SetPositionAndRotation(_pos, Quaternion.Euler(0f, _yaw, 0f));
            GuardTrail.Clear();
            if (_states != null && _states[0] != null) anim.Play(Clips[waypoints.Length > 1 ? 1 : 0]);
        }
    }

    /// The player's recent RENDER footprints (shared by every officer; recorded once per room tick by whichever updates first):
    /// a walkable path around whatever stands between an officer and where the player went. SOURCE moves are not recorded,
    /// so a phase through a wall leaves a gap he cannot follow.
    static class GuardTrail
    {
        const int N = 64;
        const float Spacing = 0.4f;
        const int MaxChecks = 24;

        static readonly Vector3[] P = new Vector3[N];
        static readonly float[] T = new float[N];
        static int _head, _count;
        static float _at = -1f;

        public static void Clear() { _head = _count = 0; _at = -1f; }

        public static void Record()
        {
            float now = RoomClock.Time;
            if (now == _at) return;
            if (now < _at) Clear();   // the room clock went back: a REBOOT
            _at = now;
            if (LayerState.InSource || !GameState.Alive || !PlayerRef.Exists) return;
            Vector3 p = PlayerRef.Position;
            if (_count > 0 && (p - P[(_head + N - 1) % N]).sqrMagnitude < Spacing * Spacing) return;
            P[_head] = p;
            T[_head] = now;
            _head = (_head + 1) % N;
            if (_count < N) _count++;
        }

        /// The newest footprint no later than `cutoff` that `g` reaches in a straight leg (at most MaxChecks legs tested).
        public static bool Toward(Guard g, float cutoff, out Vector3 p)
        {
            int stride = Mathf.Max(1, _count / MaxChecks);
            for (int k = 0; k < _count; k += stride)
            {
                int i = (_head + N - 1 - k) % N;
                if (T[i] > cutoff || !g.Reachable(P[i])) continue;
                p = P[i];
                return true;
            }
            p = default;
            return false;
        }
    }
}
