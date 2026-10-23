using TMPro;
using UnityEngine;

namespace Desync.Enemies
{
    /// Room exit: locked (mat.portal.locked) until GameEvents.ExitOpened (or GameState.ExitOpen), then open (mat.portal,
    /// pulsing). The player walking into the frame raises ExitReached once.
    public class ExitPortal : MonoBehaviour, IResettable
    {
        public Renderer surface;
        public Material lockedMat, openMat;
        public TextMeshPro label;
        public float halfWidth = 1.1f;

        bool _open, _reached;

        void OnEnable() => GameEvents.ExitOpened += Open;
        void OnDisable() => GameEvents.ExitOpened -= Open;
        void Start() => Apply();

        void Open() { _open = true; Apply(); }

        void Update()
        {
            if (!_open && GameState.ExitOpen) Open();
            if (!_open) return;
            if (surface) surface.transform.localScale = new Vector3(1f, 1f, 1f + 0.6f * Mathf.Sin(Time.time * 5f)) * (1f + 0.025f * Mathf.Sin(Time.time * 7f));
            if (_reached || !GameState.Alive || !PlayerRef.Exists) return;
            Vector3 l = transform.InverseTransformPoint(PlayerRef.Position);
            if (Mathf.Abs(l.x) < halfWidth && Mathf.Abs(l.z) < 0.7f && l.y > -0.6f && l.y < 2f)
            {
                _reached = true;
                GameEvents.ExitReached?.Invoke();
            }
        }

        void Apply()
        {
            if (surface)
            {
                var m = _open ? openMat : lockedMat;
                if (m) surface.sharedMaterial = m;
                surface.transform.localScale = Vector3.one;
            }
            if (label)
            {
                label.text = _open ? "EXIT // OPEN" : "EXIT // LOCKED";
                label.color = _open ? GameConfig.I.green : GameConfig.I.magenta;
            }
        }

        public void ResetState()
        {
            _open = _reached = false;
            Apply();
        }
    }
}
