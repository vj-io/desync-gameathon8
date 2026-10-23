using UnityEngine;

namespace Desync
{
    /// Anything RoomManager must restore on restart (REBOOT). Called on every IResettable in the room scene.
    public interface IResettable { void ResetState(); }

    /// Look-at + E targets (terminals, hijack pads). Put the collider on GameLayers.Interactable.
    public interface IInteractable
    {
        string Prompt { get; }       // e.g. "E  HACK"
        bool CanInteract { get; }
        void Interact();
    }

    /// Per-room metadata, placed once in each Room_0N scene by LevelBuilder.
    public class RoomInfo : MonoBehaviour
    {
        public static RoomInfo Current;
        public int index;                 // 0..5
        public int terminalCount = 1;
        public float traceCountdown = -1f; // Room 5: seconds before forced alarm, -1 = off
        public Transform spawn;
        void Awake() { Current = this; }
        void OnDestroy() { if (Current == this) Current = null; }
    }
}
