using System;
using UnityEngine;

namespace Desync
{
    /// The RENDER/SOURCE world state. Set() is the only writer; FlipController calls it after its CheckCapsule pre-check.
    public static class LayerState
    {
        public const string Keyword = "_SOURCE";
        public static bool InSource { get; private set; }

        /// Raised on every state change, including resets. Use for state (visuals, enemies, music). For feedback (SFX, juice) use GameEvents.Flipped.
        public static Action<bool> Changed;

        public static void Set(bool source)
        {
            InSource = source;
            if (source) Shader.EnableKeyword(Keyword); else Shader.DisableKeyword(Keyword);
            Shader.SetGlobalFloat("_Layer", source ? 1f : 0f);
            Physics.IgnoreLayerCollision(GameLayers.Player, GameLayers.WallRender, source);
            Changed?.Invoke(source);
        }
    }
}
