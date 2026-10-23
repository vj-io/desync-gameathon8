using UnityEngine;

namespace Desync
{
    /// Locator for the first-person player (registered by the player on Awake).
    public static class PlayerRef
    {
        public static Transform Root;   // CharacterController object (feet at root.position)
        public static Transform Head;   // camera pivot (pitch); vision raycasts target Head and Chest
        public static Transform Chest;
        public static Camera Cam;

        public static bool Exists => Root;
        public static Vector3 Position => Root ? Root.position : Vector3.zero;

        public static void Kill(DeathCause cause)
        {
            if (!GameState.Alive || GameState.Cinematic) return;
            GameState.Alive = false;
            GameEvents.PlayerKilled?.Invoke(cause);
        }
    }
}
