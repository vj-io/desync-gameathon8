using UnityEngine;

namespace Desync.Player
{
    /// Look ray from the camera: sets GameState.InteractPrompt and calls Interact() on E.
    public class Interactor : MonoBehaviour
    {
        Camera cam;
        Collider lastCol;
        IInteractable lastTarget;

        void Awake() => cam = GetComponent<FirstPersonController>().cam;

        void Update()
        {
            var target = GameState.InHack || GameState.Paused || GameState.Cinematic || !GameState.Alive ? null : Find();
            GameState.InteractPrompt = target?.Prompt;
            if (target != null && GameInput.Interact.WasPressedThisFrame()) target.Interact();
        }

        IInteractable Find()
        {
            int mask = GameLayers.Mask(GameLayers.Interactable) | GameLayers.Mask(GameLayers.Floor)
                     | (LayerState.InSource ? 0 : GameLayers.Mask(GameLayers.WallRender));
            var t = cam.transform;
            if (!Physics.Raycast(t.position, t.forward, out var hit, GameConfig.I.terminalRange, mask, QueryTriggerInteraction.Collide))
                return null;
            if (hit.collider != lastCol)
            {
                lastCol = hit.collider;
                lastTarget = lastCol.GetComponentInParent<IInteractable>();
            }
            return lastTarget != null && lastTarget.CanInteract ? lastTarget : null;
        }
    }
}
