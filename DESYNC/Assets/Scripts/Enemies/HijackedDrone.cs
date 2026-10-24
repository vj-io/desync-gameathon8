using UnityEngine;

namespace Desync.Enemies
{
    /// Hijack state of a guard drone (added to every drone by EnemyFactory.Drone; HijackPad calls Begin()).
    /// For hijackDuration (RoomClock): cyan (mat.hijacked), layer HijackedDrone, orbits the player facing outward in both
    /// layers, never detects or kills, its cone blinds any camera cone it overlaps, and body-blocks ICE (IceHunter pushes
    /// itself out of BlockRadius). EXPIRY: the drone blinks back to the loop position it was taken from and stays blind
    /// and harmless for DronePatrol.RebootTime before patrolling again; its pad can then be hacked again.
    public class HijackedDrone : MonoBehaviour, IResettable
    {
        public const float OrbitRadius = 1.8f, OrbitPeriod = 3.2f, BlockRadius = 1.1f, CatchUpSpeed = 9f;

        public static HijackedDrone Current;

        public DronePatrol patrol;
        public VisionCone cone;
        public Renderer[] bodyRenderers;
        public Material normalMat, hijackMat;
        public Collider[] colliders;

        public bool Active { get; private set; }

        float _end, _angle, _last;
        bool _untilFlip;   // overclocked_puppet: no timer, ends on the next player flip

        void OnEnable() => GameEvents.Flipped += OnFlipped;

        /// Overclocked Puppet: the hijack holds until the player flips (then the drone reverts, as on a timer expiry).
        void OnFlipped(bool _) { if (Active && _untilFlip) End(true); }

        public void Begin()
        {
            if (Active) return;
            Active = true;
            Current = this;
            _last = RoomClock.Time;
            _untilFlip = ExploitFlags.OverclockedPuppet;
            float extra = ExploitFlags.PuppetThread ? ExploitFlags.PuppetThreadExtra : 0f;   // Puppet Thread: +4 s
            _end = _untilFlip ? float.MaxValue : RoomClock.Time + GameConfig.I.hijackDuration + extra;
            Vector3 d = transform.position - PlayerRef.Position;
            _angle = Mathf.Atan2(d.z, d.x);
            Apply(true);
            GameState.HijackActive = true;
            GameState.HijackRemaining = GameConfig.I.hijackDuration;
            GameEvents.Hijacked?.Invoke(true);
        }

        void Update()
        {
            if (!Active) return;
            float dt = EnemyKit.Step(ref _last);
            float left = _end - RoomClock.Time;
            GameState.HijackRemaining = _untilFlip ? GameConfig.I.hijackDuration : Mathf.Max(0f, left);
            if (left <= 0f || !PlayerRef.Exists) { End(true); return; }
            if (GameState.Cinematic) return;
            _angle += dt * 2f * Mathf.PI / OrbitPeriod;
            Vector3 c = PlayerRef.Position + Vector3.up * GameConfig.I.droneHover;
            var outward = new Vector3(Mathf.Cos(_angle), 0f, Mathf.Sin(_angle));
            Vector3 p = Vector3.MoveTowards(transform.position, c + outward * OrbitRadius, CatchUpSpeed * dt);
            transform.SetPositionAndRotation(p, Quaternion.LookRotation(outward));
        }

        /// True while this hijacked drone jams `cam`: one cone's apex (or the drone cone's mid-axis) is inside the other.
        public static bool Jams(VisionCone cam)
        {
            var h = Current;
            if (!h || !h.Active || !h.cone) return false;
            Transform jt = h.cone.transform, ct = cam.transform;
            Vector3 jp = jt.position, cp = ct.position, mid = jp + jt.forward * (h.cone.range * 0.5f);
            return EnemyKit.InCone(cp, jp, jt.forward, h.cone.angle, h.cone.range)
                || EnemyKit.InCone(jp, cp, ct.forward, cam.angle, cam.range)
                || EnemyKit.InCone(mid, cp, ct.forward, cam.angle, cam.range);
        }

        void End(bool raise)
        {
            Active = false;
            _untilFlip = false;
            if (Current == this) Current = null;
            Apply(false);
            GameState.HijackActive = false;
            GameState.HijackRemaining = 0f;
            if (!raise) return;
            GameEvents.Hijacked?.Invoke(false);
            if (patrol) patrol.OnHijackEnded();
        }

        void Apply(bool on)
        {
            foreach (var c in colliders) if (c) c.gameObject.layer = on ? GameLayers.HijackedDrone : GameLayers.Drone;
            var m = on ? hijackMat : normalMat;
            if (m)
                foreach (var r in bodyRenderers)
                {
                    if (!r) continue;
                    var mats = r.sharedMaterials;   // once per hijack, not per frame
                    for (int i = 0; i < mats.Length; i++) mats[i] = m;
                    r.sharedMaterials = mats;
                }
            if (cone) cone.Jammer = on;
        }

        void OnDisable() { GameEvents.Flipped -= OnFlipped; if (Current == this) Current = null; }

        public void ResetState() { if (Active) End(false); }
    }
}
