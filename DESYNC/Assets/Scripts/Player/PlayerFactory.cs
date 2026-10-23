using UnityEngine;

namespace Desync.Player
{
    /// CONTRACT: LevelBuilder calls this once per room scene. Signature frozen; Player workstream owns the body.
    /// Must return a fully wired first-person player (CharacterController on GameLayers.Player, camera with FOV from
    /// GameConfig, AudioListener, FlipController, MeltMeter, Interactor) that registers itself in PlayerRef on Awake.
    public static class PlayerFactory
    {
        public static GameObject Create(Transform parent, Vector3 feetPos, float yaw)
        {
            var go = new GameObject("Player (stub)");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(feetPos, Quaternion.Euler(0f, yaw, 0f));
            return go;
        }
    }
}
