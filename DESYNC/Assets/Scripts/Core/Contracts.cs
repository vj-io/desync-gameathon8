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
}
