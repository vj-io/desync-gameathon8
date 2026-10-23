namespace Desync
{
    /// Physics layer indices (set up by ProjectSetup; collision matrix per PRD 10.3).
    public static class GameLayers
    {
        public const int Default = 0;
        public const int Player = 6;
        public const int WallRender = 7;     // walls, doors, solid props: solid in RENDER only
        public const int Floor = 8;          // solid in both layers, NavMesh-walkable
        public const int Ice = 9;
        public const int Triggers = 10;      // trigger volumes (portal, glitch zone, kill volumes)
        public const int Drone = 11;
        public const int HijackedDrone = 12;
        public const int Interactable = 13;  // raycast-only targets (terminals, hijack pads); collides with nothing

        public static readonly string[] Names =
            { null, null, null, null, null, null, "Player", "WallRender", "Floor", "ICE", "Triggers", "Drone", "HijackedDrone", "Interactable" };

        public static int Mask(int layer) => 1 << layer;
    }
}
