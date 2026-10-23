namespace Desync.Levels
{
    /// Core's RoomInfo is declared in Contracts.cs; Unity only serialises a MonoBehaviour whose file name matches its
    /// class, so a plain RoomInfo saves into scenes as a missing script. LevelBuilder places this subclass instead:
    /// it *is* a RoomInfo (FindObjectOfType<RoomInfo>, RoomInfo.Current, GetComponent<RoomInfo> all work).
    public class LevelRoomInfo : RoomInfo
    {
        void Awake() { Current = this; }
        void OnDestroy() { if (Current == this) Current = null; }
    }
}
