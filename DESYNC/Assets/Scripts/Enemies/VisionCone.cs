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

        float _dwell, _last;
        int _state = -1;

        public bool Active => !Suppressed && (bothLayers || Jammer || !LayerState.InSource);
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
                if (_dwell >= GameConfig.I.traceDwell) TraceSystem.Sustain();
            }
            else _dwell = 0f;
            Show(active, blinded);
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
            if (m > range || Vector3.Angle(transform.forward, d) > angle * 0.5f) return false;
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

        public void ResetState() { _dwell = 0f; _last = 0f; _state = -1; Suppressed = Jammer = false; }
    }
}
