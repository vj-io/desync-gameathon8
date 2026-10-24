using UnityEngine;

namespace Desync.Enemies
{
    /// A light's own intensity, recorded the first time BLACKOUT dims it, so later refreshes scale from it instead of compounding.
    public class LightBase : MonoBehaviour
    {
        public float intensity;
    }
}
