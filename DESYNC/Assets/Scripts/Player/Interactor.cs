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
            float range = GameConfig.I.terminalRange;
            if (Physics.Raycast(t.position, t.forward, out var hit, range, mask, QueryTriggerInteraction.Collide))
            {
                if (hit.collider != lastCol)
                {
                    lastCol = hit.collider;
                    lastTarget = lastCol.GetComponentInParent<IInteractable>();
                }
                if (lastTarget != null && lastTarget.CanInteract) return lastTarget;
            }
            return InViewCone(t, range);
        }

        // Fallback so a terminal below the eye line still counts: nearest-to-centre interactable within range and 40° (yaw).
        static readonly Collider[] buf = new Collider[8];
        static readonly float coneCos = Mathf.Cos(40f * Mathf.Deg2Rad);

        IInteractable InViewCone(Transform t, float range)
        {
            int n = Physics.OverlapSphereNonAlloc(t.position, range, buf, GameLayers.Mask(GameLayers.Interactable), QueryTriggerInteraction.Collide);
            Vector3 fwd = t.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) return null;
            fwd.Normalize();
            IInteractable best = null;
            float bestDot = coneCos;
            for (int i = 0; i < n; i++)
            {
                Vector3 d = buf[i].bounds.center - t.position; d.y = 0f;
                float dist = d.magnitude;
                if (dist < 0.01f) continue;
                float dot = Vector3.Dot(fwd, d / dist);
                if (dot < bestDot) continue;
                var it = buf[i].GetComponentInParent<IInteractable>();
                if (it == null || !it.CanInteract) continue;
                // In RENDER walls are solid: no hacking through them (the target's own solid body may be what we hit).
                if (!LayerState.InSource && Physics.Linecast(t.position, buf[i].bounds.center, out var block, GameLayers.Mask(GameLayers.WallRender), QueryTriggerInteraction.Ignore)
                    && block.collider.GetComponentInParent<IInteractable>() != it) continue;
                best = it; bestDot = dot;
            }
            return best;
        }
    }
}
