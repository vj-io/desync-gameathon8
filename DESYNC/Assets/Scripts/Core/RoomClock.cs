namespace Desync
{
    /// Deterministic room time. All sensor schedules (camera sweeps, drone patrols, vents) read RoomClock.Time, never Time.time.
    public static class RoomClock
    {
        public static float Time;
        public static bool Running;
        public static void Reset() { Time = 0f; }
        public static void Tick(float dt) { if (Running) Time += dt; }   // RoomManager.Update, scaled deltaTime
    }
}
