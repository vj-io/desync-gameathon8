using UnityEngine;

namespace Desync.Levels
{
    /// Marks a room's anomaly wall ('A' tiles, its own mesh): every `period` seconds it shows the glyph material for two frames.
    public class AnomalyFlicker : MonoBehaviour
    {
        public MeshRenderer target;
        public Material normal, glyph;
        public float period = 1.6f;

        float _next;
        int _until = -1;

        void Update()
        {
            if (!target || !glyph) return;
            if (Time.time >= _next) { _next = Time.time + period; _until = Time.frameCount + 2; }
            var m = Time.frameCount < _until ? glyph : normal;
            if (target.sharedMaterial != m) target.sharedMaterial = m;
        }
    }
}
