using UnityEngine;

namespace Desync.Enemies
{
    /// Sensor cone on cameras, drones and the Architect. LOS = raycasts from this transform (the eye) to PlayerRef.Head and
    /// PlayerRef.Chest, blocked by WallRender|Floor. Seen for traceDwell seconds -> TraceSystem. Visuals: additive cone mesh
    /// (mat.cone / mat.cone.trace) + a shadowless spot light coloured by state. Active only in RENDER unless bothLayers.
    public class VisionCone : MonoBehaviour, IResettable
    {
        public float angle = 60f;          // full cone angle, degrees
        public float range = 8f;
        public bool bothLayers;            // Architect: also active in SOURCE
        public bool blindable = true;      // cameras: a hijacked drone's cone overlapping this one blinds it
        public MeshRenderer coneRenderer;
        public Light spot;
        public Material normalMat, traceMat;

        /// Owner switches: Suppressed = hidden + harmless (drone rebooting, finale). Jammer = hijacked drone: cyan, never detects.
        [System.NonSerialized] public bool Suppressed, Jammer;

        static readonly Color Idle = new Color(1f, 0.55f, 0.9f), Seeing = new Color(1f, 0.62f, 0.1f), Blind = new Color(0f, 0.55f, 0.6f);

        float _dwell, _last, _mult = 1f, _spotRange = -1f;
        int _state = -1, _kind = -1;

        /// 1 = security camera (CameraSweep above it), 2 = guard drone (DronePatrol above it), 0 = anything else. Found on
        /// first use, so cones in prebuilt scenes are classified too.
        int Kind => _kind >= 0 ? _kind : _kind = GetComponentInParent<CameraSweep>() ? 1 : GetComponentInParent<DronePatrol>() ? 2 : 0;

        /// Cameras and drones see CameraRangeMult further (the Architect's gaze is not a camera); SIGNAL JAM shortens drones.
        float Mult => bothLayers ? 1f : GameState.Mods.CameraRangeMult * (EnemyFlags.SignalJam && Kind == 2 ? EnemyFlags.SignalJamRange : 1f);

        /// Effective detection range this frame.
        public float Range => range * Mult;

        /// BLIND CAMS switches camera cones off (drones and other sensors still see).
        public bool Active => !Suppressed && !(EnemyFlags.BlindCams && Kind == 1) && (bothLayers || Jammer || !LayerState.InSource);
        public float Dwell => _dwell;

        void Update()
        {
            float dt = EnemyKit.Step(ref _last);
            bool active = Active;
            bool blinded = active && blindable && !Jammer && HijackedDrone.Jams(this);
            if (active && !Jammer && !blinded && CanSeePlayer())
            {
                if (_dwell <= 0f) GameEvents.Spotted?.Invoke(transform.position);
                _dwell += dt + 1e-6f;
                TraceSystem.Seen(PlayerRef.Position);
                if (_dwell >= GameState.Mods.Clamped().TraceWindow) TraceSystem.Sustain();
            }
            else _dwell = 0f;
            Show(active, blinded);
            float mult = Mult;
            if (mult != _mult) Rescale(mult);
        }

        bool CanSeePlayer()
        {
            if (GameState.DemoMode || GameState.Cinematic || !GameState.Alive || !PlayerRef.Exists) return false;
            return Sees(PlayerRef.Head) || Sees(PlayerRef.Chest);
        }

        bool Sees(Transform target)
        {
            if (!target) return false;
            Vector3 eye = transform.position, d = target.position - eye;
            float m = d.magnitude;
            if (m > Range || Vector3.Angle(transform.forward, d) > angle * 0.5f) return false;
            return m < 1e-3f || !Physics.Raycast(eye, d / m, m, EnemyKit.SightMask, QueryTriggerInteraction.Ignore);
        }

        /// 0 hidden, 1 idle, 2 seeing, 3 traced, 4 blinded (flickers), 5 jammer.
        void Show(bool active, bool blinded)
        {
            int s = !active ? 0 : Jammer ? 5 : blinded ? 4 : GameState.TraceActive ? 3 : _dwell > 0f ? 2 : 1;
            if (s == 4 && coneRenderer) coneRenderer.enabled = (Time.frameCount & 4) == 0;
            if (s == _state) return;
            _state = s;
            if (coneRenderer)
            {
                coneRenderer.enabled = s != 0;
                var m = s == 3 ? traceMat : normalMat;
                if (m) coneRenderer.sharedMaterial = m;
            }
            if (!spot) return;
            spot.enabled = s != 0;
            var cfg = GameConfig.I;
            spot.color = s == 2 ? Seeing : s == 3 ? cfg.danger : s == 4 ? Blind : s == 5 ? cfg.cyan : Idle;
        }

        /// The drawn cone and its light follow the detection range (uniform scale keeps the angle).
        void Rescale(float mult)
        {
            _mult = mult;
            if (coneRenderer) coneRenderer.transform.localScale = Vector3.one * mult;
            if (!spot) return;
            if (_spotRange < 0f) _spotRange = spot.range;   // the factory's range, read before the first change
            spot.range = _spotRange * mult;
        }

        public void ResetState() { _dwell = 0f; _last = 0f; _state = -1; Suppressed = Jammer = false; }
    }
}
