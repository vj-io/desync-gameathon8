using UnityEngine;

namespace Desync.Enemies
{
    /// CONTRACT: LevelBuilder (editor) builds rooms by calling these. Signatures are frozen; the Enemies workstream
    /// owns the bodies. Each returns the root GameObject, already parented, positioned and fully wired
    /// (colliders on the right GameLayers, materials from AssetLib, IResettable where it has state).
    /// Stub bodies only create a named marker so scenes can be generated before the real implementations land.
    public static class EnemyFactory
    {
        /// Wall/ceiling security camera. yaw = sweep centre (world deg), pitch = downward tilt (deg). phase offsets the sweep (s).
        public static GameObject Camera(Transform parent, Vector3 pos, float yaw, float pitch = 20f, float phase = 0f) => Stub("Camera", parent, pos, yaw);

        /// Hovering guard drone looping through floor-level waypoints (hover height from GameConfig). Kills on body touch.
        public static GameObject Drone(Transform parent, Vector3[] waypoints, float phase = 0f) => Stub("Drone", parent, waypoints[0], 0f);

        /// ICE vent on the floor. Spawns pooled ICE hunters while the player is in SOURCE.
        public static GameObject IceVent(Transform parent, Vector3 pos) => Stub("IceVent", parent, pos, 0f);

        /// Hackable terminal. room = 0..5 (sets prompt length + mirrored/scrambled SOURCE modes), storyIndex into GameConfig.terminalLines.
        public static GameObject Terminal(Transform parent, Vector3 pos, float yaw, int room, int storyIndex) => Stub("Terminal", parent, pos, yaw);

        /// Hijack pad linked to a drone created by Drone(). Hacking it turns that drone into a HijackedDrone for GameConfig.hijackDuration.
        public static GameObject HijackPad(Transform parent, Vector3 pos, float yaw, GameObject drone, int room) => Stub("HijackPad", parent, pos, yaw);

        /// Floor glitch volume with a visible countdown ring; force-flips the player when it expires.
        public static GameObject GlitchZone(Transform parent, Vector3 pos, float radius) => Stub("GlitchZone", parent, pos, 0f);

        /// Room exit. Inactive (visibly locked) until GameEvents.ExitOpened; on player enter raises the room-clear flow.
        public static GameObject ExitPortal(Transform parent, Vector3 pos, float yaw) => Stub("ExitPortal", parent, pos, yaw);

        /// Room 6 boss: giant wireframe face above the core, gaze cone sweeping BOTH layers, puppet strings, finale sequence.
        public static GameObject Architect(Transform parent, Vector3 pos) => Stub("Architect", parent, pos, 0f);

        /// Wall billboard (HALCYON propaganda); in the finale it shows a board member's face.
        public static GameObject Billboard(Transform parent, Vector3 pos, float yaw, Vector2 size) => Stub("Billboard", parent, pos, yaw);

        static GameObject Stub(string name, Transform parent, Vector3 pos, float yaw)
        {
            var go = new GameObject(name + " (stub)");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            return go;
        }
    }
}
