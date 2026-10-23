using System;
using System.Collections.Generic;

namespace Desync.Rules
{
    // Pure, deterministic game rules (no UnityEngine). Shared by the game, the bot-sim and (later) the score server.
    // CONTRACT: signatures frozen; change only through docs/DECISIONS.md.

    public enum Layer { Render, Source }

    public struct MeltState
    {
        public float Melt;      // 0..100
        public Layer Layer;
        public bool Dead;
        public MeltState(float melt, Layer layer, bool dead = false) { Melt = melt; Layer = layer; Dead = dead; }
    }

    /// Every tunable a run can modify (exploits, mutators, assist). Default = the base game.
    public struct Modifiers
    {
        public float FlipCost, SourceRise, RenderDrain, TraceWindow, ChainWindow, ChainCap, ScoreMult,
                     MoveSpeedMult, IceSpeedMult, RedlineThreshold, CameraRangeMult;
        public int HackLengthDelta;

        public static Modifiers Default => new Modifiers
        {
            FlipCost = 5f, SourceRise = 12f, RenderDrain = 4f, TraceWindow = 0.5f, ChainWindow = 3f, ChainCap = 4f,
            ScoreMult = 1f, MoveSpeedMult = 1f, IceSpeedMult = 1f, RedlineThreshold = 90f, CameraRangeMult = 1f, HackLengthDelta = 0,
        };

        /// Clamps: FlipCost >= 2, SourceRise >= 8, TraceWindow <= 1.0, MoveSpeedMult <= 1.3, ScoreMult <= 8.
        public Modifiers Clamped() => throw new NotImplementedException();
    }

    public static class Melt
    {
        public static MeltState Step(MeltState s, float dt) => Step(s, dt, Modifiers.Default);
        /// SOURCE: +SourceRise/s, RENDER: -RenderDrain/s; clamp [0,100]; Dead when >= 100. Modifiers are clamped first.
        public static MeltState Step(MeltState s, float dt, in Modifiers m) => throw new NotImplementedException();
        public static MeltState Flip(MeltState s) => Flip(s, Modifiers.Default);
        /// Adds FlipCost BEFORE switching layer; Dead if that reaches 100.
        public static MeltState Flip(MeltState s, in Modifiers m) => throw new NotImplementedException();
    }

    public enum EventType { Flip, CleanFlip, ChainBreak, Graze, Redline, Hack, HackPerfect, Detected, Exit, Death, RoomClear, NoTraceRoom, PuppetString }

    /// One entry of the run event log (also the future server payload). T = room-clock seconds.
    public struct RunEvent
    {
        public float T;
        public EventType Type;
        public string Room;   // room id, e.g. "r1"
        public string Ice;    // ICE id for Graze
        public float Melt;    // melt at the event (Exit/Redline)
        public RunEvent(float t, EventType type, string room = null, string ice = null, float melt = 0f) { T = t; Type = type; Room = room; Ice = ice; Melt = melt; }
    }

    public static class Scoring
    {
        public const int CleanFlipPts = 50, GrazePts = 150, RedlinePts = 250, HackPerfectPts = 300, HackPts = 120,
                         NoTraceRoomPts = 500, RoomClearPts = 1000, PuppetStringPts = 400, GrazeCapPerIce = 3;

        /// 1 + 0.25 * chain, capped at cap (default 4.0).
        public static float GhostMultiplier(int chain, float cap = 4f) => throw new NotImplementedException();
        /// score/par >= 1.0 'S', >= 0.75 'A', >= 0.5 'B', else 'C'.
        public static char Grade(int score, int par) => throw new NotImplementedException();
        /// Replays the log in order: CleanFlip adds 1 to chain then pays at the new multiplier, ChainBreak/Detected/Death reset it; each scoring event pays
        /// points * GhostMultiplier(chain, m.ChainCap) * m.ScoreMult. Graze capped per (room, ice). Exit pays Redline
        /// only when Melt >= m.RedlineThreshold. Pure function: the server recomputes with it.
        public static int ScoreFromEvents(IReadOnlyList<RunEvent> events, in Modifiers m) => throw new NotImplementedException();
        /// max(0, gold - t) * 40
        public static int TimeBonus(float t, float gold) => throw new NotImplementedException();
    }

    public sealed class Mulberry32
    {
        uint _a;
        public Mulberry32(uint seed) { _a = seed; }
        public uint NextUInt() => throw new NotImplementedException();
        /// [0,1)
        public double NextDouble() => throw new NotImplementedException();
        public int Range(int minInclusive, int maxExclusive) => throw new NotImplementedException();
        public float Range(float min, float max) => throw new NotImplementedException();
    }

    public static class Rng
    {
        /// FNV-1a 32-bit.
        public static uint HashStr(string s) => throw new NotImplementedException();
        /// Independent stream per name: new Mulberry32(seed ^ HashStr(name)).
        public static Mulberry32 Substream(uint seed, string name) => throw new NotImplementedException();
        /// HashStr of the UTC date "yyyy-MM-dd".
        public static uint DailySeed(DateTime utc) => throw new NotImplementedException();
    }

    public enum Medal { None, Bronze, Silver, Gold, Sync }

    public static class Medals
    {
        /// sync <= tDev*1.00, gold <= tDev*1.25, silver <= tDev*1.6, else bronze (a clear).
        public static Medal For(float time, float tDev) => throw new NotImplementedException();
        public static float Threshold(Medal m, float tDev) => throw new NotImplementedException();
    }

    public sealed class RoomBest { public float time; public int score; public string grade; }
    public sealed class RunStats { public int runs, wins, deaths, flips; public double playSeconds; }

    public sealed class SaveV3
    {
        public int version = 3;
        public string playerId = "";
        public int fragments;
        public List<string> unlocks = new List<string>();
        public List<string> shards = new List<string>();
        public Dictionary<string, string> medals = new Dictionary<string, string>();   // roomId -> "B"|"S"|"G"|"SYNC"
        public Dictionary<string, RoomBest> best = new Dictionary<string, RoomBest>();
        public int deepLayerMax;
        public Dictionary<string, string> settings = new Dictionary<string, string>();
        public RunStats stats = new RunStats();
        public Dictionary<string, int> dailyBest = new Dictionary<string, int>();      // "yyyy-MM-dd" -> score
    }

    public sealed class NewerSaveException : Exception { public NewerSaveException(int v) : base("newer save version " + v) { } }

    public static class SaveMigrator
    {
        public const int Version = 3;
        public static SaveV3 Default() => throw new NotImplementedException();
        /// v1 {version:1, best:{room:medal}, frags} -> v3; v2 -> v3; corrupt/empty -> Default() (never throws);
        /// version > 3 -> NewerSaveException; v3 -> same content (idempotent).
        public static SaveV3 Migrate(string json) => throw new NotImplementedException();
        public static string Serialize(SaveV3 s) => throw new NotImplementedException();
    }
}
