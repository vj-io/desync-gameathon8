using UnityEngine;

namespace Desync.Enemies
{
    /// Room-wide run-mutator hazards that belong to no placed enemy. One DontDestroyOnLoad host, created the first time a
    /// room needs it and reconfigured by EnemyFlags.Refresh on every room start / reset:
    ///   FLICKER  a forced flip every EnemyFlags.FlickerPeriod s, FlickerTelegraph s of screen tear before each.
    ///   GAZE     an Architect-style gaze (VisionCone, both layers) hung GazeHeight above the room's floor centre turns a full
    ///            circle in GazeSweep s once every EnemyFlags.GazePeriod s; hidden and harmless in between.
    /// Idle while no room clock runs (title, death recap, reboot).
    public class RoomHazards : MonoBehaviour
    {
        public const float GazeSweep = 3f, GazeHeight = 3f, GazeAngle = 32f;

        static RoomHazards _i;

        readonly ForcedFlipClock _flicker = new ForcedFlipClock();
        Transform _gazePivot;
        VisionCone _gaze;
        RoomInfo _gazeRoom;

        internal static void Configure()
        {
            if (!Application.isPlaying) return;
            if (!_i)
            {
                if (!EnemyFlags.Flicker && !EnemyFlags.Gaze) return;
                var go = new GameObject("RoomHazards");
                DontDestroyOnLoad(go);
                _i = go.AddComponent<RoomHazards>();
            }
            _i.Apply();
        }

        void Apply()
        {
            if (EnemyFlags.Flicker) _flicker.Start(RoomClock.Time, EnemyFlags.FlickerPeriod, EnemyFlags.FlickerTelegraph);
            else _flicker.Stop();
            var room = RoomInfo.Current;
            if (_gazePivot && (!EnemyFlags.Gaze || _gazeRoom != room)) DropGaze();   // kept across REBOOTs, rebuilt per room
            if (EnemyFlags.Gaze && !_gazePivot) { BuildGaze(); _gazeRoom = room; }
            if (_gaze) _gaze.Suppressed = true;
        }

        void DropGaze()
        {
            if (_gaze && _gaze.coneRenderer) Destroy(_gaze.coneRenderer.GetComponent<MeshFilter>().sharedMesh);
            Destroy(_gazePivot.gameObject);
            _gazePivot = null;
            _gaze = null;
        }

        void Update()
        {
            if (!RoomClock.Running) return;
            float t = RoomClock.Time;
            _flicker.Tick(t, PlayerRef.Position);
            if (!_gaze) return;
            float s = Mathf.Repeat(t, EnemyFlags.GazePeriod);
            bool on = t >= EnemyFlags.GazePeriod && s < GazeSweep && !GameState.Cinematic;
            _gaze.Suppressed = !on;
            if (on) _gazePivot.localRotation = Quaternion.Euler(0f, 360f * s / GazeSweep, 0f);
        }

        /// Over the centre of the room's Floor colliders, aimed so the cone's axis lands ~60 % of the way to the far wall.
        void BuildGaze()
        {
            bool any = false;
            var b = new Bounds();
            foreach (var c in FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (c.gameObject.layer != GameLayers.Floor) continue;
                if (any) b.Encapsulate(c.bounds); else { b = c.bounds; any = true; }
            }
            if (!any) return;
            float r = Mathf.Max(3f, Mathf.Max(b.extents.x, b.extents.z));
            var at = new Vector3(b.center.x, b.max.y + GazeHeight, b.center.z);
            float pitch = Mathf.Atan2(GazeHeight, r * 0.6f) * Mathf.Rad2Deg;
            _gaze = EnemyFactory.Gaze(transform, at, pitch, GazeAngle, Mathf.Sqrt(GazeHeight * GazeHeight + r * r), out _gazePivot);
        }

        void OnDestroy()
        {
            _flicker.Stop();
            if (_i == this) _i = null;
        }
    }
}
