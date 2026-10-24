using UnityEngine;

namespace Desync.Enemies
{
    public enum DecoyKind { Silhouette, Echo, Noise }

    /// CONTRACT (M3): decoys that cameras, drones and ICE can lock onto (PHANTOM exploits, Poltergeist, Afterimage...).
    /// Signatures frozen; the enemies workstream implements (pooled, zero GC).
    /// Implementation: Capacity slots with visuals under one DontDestroyOnLoad DecoyHost (built on the first Spawn) —
    /// Silhouette = player-shaped glyph figure (mat.hijacked), Echo = the same figure in SOURCE green (mat.glitch),
    /// Noise = a pulsing floor ring (mat.hijacked). Lifetimes run on RoomClock. A decoy also ends when the room it was
    /// spawned in is gone (RoomInfo.Current changed), and all are cleared on Rebooting / RoomReset / RoomStarted.
    /// A Noise decoy pulls the nearest drone that can reach it for its lifetime (DronePatrol.PullNearest); what cameras and
    /// ICE do with decoys lives in VisionCone / IceHunter (they call Nearest).
    public static class Decoys
    {
        public const int Capacity = 8;

        static readonly Vector3[] Pos = new Vector3[Capacity];
        static readonly float[] Born = new float[Capacity], Until = new float[Capacity];
        static readonly DecoyKind[] Kinds = new DecoyKind[Capacity];
        static readonly bool[] Used = new bool[Capacity];
        static readonly RoomInfo[] Rooms = new RoomInfo[Capacity];
        static int _serial;
        static DecoyHost _host;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            GameEvents.Rebooting -= ClearAll;
            GameEvents.Rebooting += ClearAll;
            GameEvents.RoomReset -= ClearAll;
            GameEvents.RoomReset += ClearAll;
            GameEvents.RoomStarted -= OnRoomStarted;
            GameEvents.RoomStarted += OnRoomStarted;
        }

        static void OnRoomStarted(int _) => ClearAll();

        /// Spawns a decoy at pos for life seconds; returns its id (or -1 when the pool is full).
        public static int Spawn(Vector3 pos, float life, DecoyKind kind)
        {
            int slot = -1;
            for (int i = 0; i < Capacity && slot < 0; i++) if (!Alive(i)) slot = i;
            if (slot < 0 || life <= 0f) return -1;
            float now = RoomClock.Time;
            Used[slot] = true;
            Pos[slot] = pos;
            Kinds[slot] = kind;
            Born[slot] = now;
            Until[slot] = now + life;
            Rooms[slot] = RoomInfo.Current;
            if (!_host) _host = DecoyHost.Create();
            _host.Show(slot, kind, pos);
            if (kind == DecoyKind.Noise) DronePatrol.PullNearest(pos, now + life);
            return ++_serial;
        }

        /// Nearest live decoy within range of from (any kind).
        public static bool Nearest(Vector3 from, float range, out Vector3 pos, out DecoyKind kind)
        {
            pos = default;
            kind = default;
            float best = range * range;
            bool found = false;
            for (int i = 0; i < Capacity; i++)
            {
                if (!Alive(i)) continue;
                float d = (Pos[i] - from).sqrMagnitude;
                if (d > best) continue;
                best = d;
                pos = Pos[i];
                kind = Kinds[i];
                found = true;
            }
            return found;
        }

        /// Slot 0..Capacity-1 when it holds a live decoy: where, what, and RoomClock seconds left (HUD markers). Zero GC.
        public static bool TryGet(int slot, out Vector3 pos, out DecoyKind kind, out float lifeLeft)
        {
            bool live = slot >= 0 && slot < Capacity && Alive(slot);
            pos = live ? Pos[slot] : default;
            kind = live ? Kinds[slot] : default;
            lifeLeft = live ? Until[slot] - RoomClock.Time : 0f;
            return live;
        }

        /// Removes every decoy (room reset / dispose).
        public static void ClearAll()
        {
            for (int i = 0; i < Capacity; i++) Release(i);
        }

        public static int Count
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Capacity; i++) if (Alive(i)) n++;
                return n;
            }
        }

        /// Live = spawned, not expired (RoomClock), clock not reset below its birth, and its room still current.
        static bool Alive(int i)
        {
            float now = RoomClock.Time;
            return Used[i] && now < Until[i] && now >= Born[i] && ReferenceEquals(Rooms[i], RoomInfo.Current);
        }

        /// DecoyHost, every frame: hides dead slots, animates live ones.
        internal static void Tick()
        {
            float now = RoomClock.Time;
            for (int i = 0; i < Capacity; i++)
            {
                if (!Used[i]) continue;
                if (!Alive(i)) { Release(i); continue; }
                _host.Animate(i, Kinds[i], now - Born[i]);
            }
        }

        static void Release(int i)
        {
            Used[i] = false;
            Rooms[i] = null;
            if (_host) _host.Hide(i);
        }
    }
}
