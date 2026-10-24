using System;
using System.Collections.Generic;
using System.IO;
using Desync.Rules;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Desync.Gen
{
    // Deterministic room & run generation from hand-authored chunks (engine-free). CONTRACT: signatures frozen.
    // Tiles: 16x16 per chunk at 0.5 m (chunk = 8 m). Legend: '#' render wall (passable in SOURCE), 'X' solid in both layers,
    // '.' floor, 'T' terminal, 'C' camera mount, 'P' patrol node, 'I' ICE spawn, 'G' glitch zone, 'A' anomaly wall, 'S' spawn, 'E' exit, ' ' void.

    public enum SocketKind { Wall, Door, SourceOnly }

    public sealed class Chunk
    {
        public string id;
        public int act;                 // 0 = any act
        public SocketKind n, e, s, w;
        public List<string> tags = new List<string>();
        public string[] tiles;          // 16 strings of 16 chars, row 0 = north
        public int budget;              // threat cost contribution
    }

    public sealed class ChunkLibrary
    {
        public List<Chunk> chunks = new List<Chunk>();
        /// Parses {"chunks":[...]} (sockets as "wall"|"door"|"source_only").
        public static ChunkLibrary FromJson(string json) =>
            JsonConvert.DeserializeObject<ChunkLibrary>(json, JsonSettings) ?? new ChunkLibrary();
        /// Loads and merges every *.json under a folder (tests, editor tools).
        public static ChunkLibrary FromFolder(string dir)
        {
            var lib = new ChunkLibrary();
            var files = Directory.GetFiles(dir, "*.json");
            Array.Sort(files, StringComparer.Ordinal);   // stable chunk order = deterministic rooms
            foreach (var f in files) lib.chunks.AddRange(FromJson(File.ReadAllText(f)).chunks);
            return lib;
        }

        static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter(new SnakeCaseNamingStrategy()) },   // "source_only" <-> SourceOnly
        };
    }

    public enum NodeType { Infil, Elite, Market, Cooldown, Anomaly, Boss }

    public sealed class RunNode
    {
        public string id;               // "a1n2"
        public NodeType type;
        public int act;                 // 1..3
        public int index;               // 0..3 regular, 4 = boss
        public int lane;                // 0..2
        public List<string> next = new List<string>();
        public uint roomSeed;
    }

    public sealed class RunGraph
    {
        public uint seed;
        public List<List<RunNode>> acts = new List<List<RunNode>>();   // acts[a] = nodes of act a+1 (4 rows x up to 3 lanes + boss)
    }

    public enum EntityKind { Spawn, Exit, Terminal, CameraFixed, CameraSweep, Drone, IceVent, TrackerIce, Glitch, HijackPad,
                             Lantern, Sentinel, Splitter, Daemon, Watcher, AnomalyWall, PuppetString, Guard }

    public struct EntitySpawn
    {
        public EntityKind kind;
        public float x, z;              // metres, room space (origin = room north-west corner, +x east, -z south)
        public float yaw;               // degrees, 0 = north
        public string id;               // stable id, e.g. "ICE-1", "CAM-2"
        public float[] path;            // drone/patrol waypoints as x,z pairs (may be null)
    }

    public sealed class RoomSpec
    {
        public uint seed;
        public int act, node, heat;
        public bool mirror;
        public int gridW, gridH;        // in chunks
        public string[][] chunks;       // [row][col] chunk ids (null = void)
        public string[] tiles;          // full room tile map (gridH*16 rows of gridW*16 chars), after mirroring
        public List<EntitySpawn> entities = new List<EntitySpawn>();
        public float par;               // seconds
        public string fallbackRoom;     // non-null when generation fell back to an original room (e.g. "Room_03")
    }

    public struct Validation
    {
        public bool reachable;
        public bool flipFreePathExists;
        public float minMeltPeak;                   // % on the best path (Dijkstra over cell, layer, 5 % melt bucket)
        public float maxCameraExposureOnBestPath;   // seconds of continuous camera exposure on that path
        public float bestPathSeconds;
        public List<string> reasons;                // e.g. "ice_near_entry", "unreachable_exit"
    }

    /// Deep Layer heat mutators that change what a room holds, placed on top of the budgeted threats (the mutator IS the
    /// extra threat, so it is not budgeted). Default = off. Placement draws from its own substream ("heat"), so a room
    /// with placement off is identical to one generated without it.
    public struct HeatPlacement
    {
        public int extraDrones;       // Double Patrol: guard drones added on free patrol loops (cells with >= 2 unused 'P')
        public float trackerShare;    // Tracker Swarm: share of ICE hunters turned into Tracker ICE (rounded half up)
        public float glitchBonus;     // Unstable Reality: extra glitch zones, as a share of the ones placed (rounded half up)
        public bool watchers;         // Gaze Everywhere: every act 3 room holds at least one Watcher Eye
        public bool Any => extraDrones > 0 || trackerShare > 0f || glitchBonus > 0f || watchers;
    }

    public static class Generator
    {
        /// 3 acts; per act 4 rows of 2-3 lane nodes + 1 boss; every node reachable; act 1 node 0 always Infil.
        public static RunGraph GenerateRun(uint seed, int heat) => RunGen.Generate(seed, heat);
        /// Spelunky-style solution path over the act grid (A1 3x3, A2 4x3, A3 4x4), socket matching, threat budget
        /// B = 10 + 6*act + 2*node + heatBonus, mirror p 0.5, validate, reseed up to 20 times, then fall back.
        public static RoomSpec GenerateRoom(uint seed, int act, int node, int heat, ChunkLibrary lib) => RoomGen.Generate(seed, act, node, heat, lib, default);
        /// The same room with Deep Layer heat placement (HeatPlacement) applied before validation.
        public static RoomSpec GenerateRoom(uint seed, int act, int node, int heat, ChunkLibrary lib, HeatPlacement placement) => RoomGen.Generate(seed, act, node, heat, lib, placement);
        public static Validation Validate(RoomSpec room) => RoomCheck.Validate(room);
    }
}
