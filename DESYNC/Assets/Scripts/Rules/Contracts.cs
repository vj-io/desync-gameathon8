using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        public Modifiers Clamped()
        {
            var c = this;
            c.FlipCost = Math.Max(2f, c.FlipCost);
            c.SourceRise = Math.Max(8f, c.SourceRise);
            c.TraceWindow = Math.Min(1f, c.TraceWindow);
            c.MoveSpeedMult = Math.Min(1.3f, c.MoveSpeedMult);
            c.ScoreMult = Math.Min(8f, c.ScoreMult);
            return c;
        }
    }

    public static class Melt
    {
        public static MeltState Step(MeltState s, float dt) => Step(s, dt, Modifiers.Default);
        /// SOURCE: +SourceRise/s, RENDER: -RenderDrain/s; clamp [0,100]; Dead when >= 100. Modifiers are clamped first.
        public static MeltState Step(MeltState s, float dt, in Modifiers m)
        {
            var c = m.Clamped();
            s.Melt = Clamp100(s.Melt + (s.Layer == Layer.Source ? c.SourceRise : -c.RenderDrain) * dt);
            s.Dead |= s.Melt >= 100f;
            return s;
        }
        public static MeltState Flip(MeltState s) => Flip(s, Modifiers.Default);
        /// Adds FlipCost BEFORE switching layer; Dead if that reaches 100.
        public static MeltState Flip(MeltState s, in Modifiers m)
        {
            s.Melt = Clamp100(s.Melt + m.Clamped().FlipCost);
            s.Dead |= s.Melt >= 100f;
            s.Layer = s.Layer == Layer.Source ? Layer.Render : Layer.Source;
            return s;
        }

        static float Clamp100(float v) => v < 0f ? 0f : v > 100f ? 100f : v;
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
        public static float GhostMultiplier(int chain, float cap = 4f) => Math.Min(cap, 1f + 0.25f * Math.Max(0, chain));
        /// score/par >= 1.0 'S', >= 0.75 'A', >= 0.5 'B', else 'C'.
        public static char Grade(int score, int par)
        {
            double r = par > 0 ? (double)score / par : double.PositiveInfinity;   // no par yet: any clear meets it
            return r >= 1.0 ? 'S' : r >= 0.75 ? 'A' : r >= 0.5 ? 'B' : 'C';
        }
        /// Replays the log in order: CleanFlip adds 1 to chain then pays at the new multiplier, ChainBreak/Detected/Death reset it; each scoring event pays
        /// points * GhostMultiplier(chain, m.ChainCap) * m.ScoreMult. Graze capped per (room, ice). Exit pays Redline
        /// only when Melt >= m.RedlineThreshold. Pure function: the server recomputes with it.
        public static int ScoreFromEvents(IReadOnlyList<RunEvent> events, in Modifiers m)
        {
            var tally = new ScoreTally();
            if (events != null) for (int i = 0; i < events.Count; i++) tally.Apply(events[i], m);
            return tally.Total;
        }
        /// max(0, gold - t) * 40
        public static int TimeBonus(float t, float gold) => (int)Math.Round(Math.Max(0f, gold - t) * 40.0, MidpointRounding.AwayFromZero);
    }

    public sealed class Mulberry32
    {
        uint _a;
        public Mulberry32(uint seed) { _a = seed; }
        public uint NextUInt()
        {
            unchecked
            {
                _a += 0x6D2B79F5;
                uint t = _a;
                t = (t ^ (t >> 15)) * (t | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                return t ^ (t >> 14);
            }
        }
        /// [0,1)
        public double NextDouble() => NextUInt() / 4294967296.0;
        public int Range(int minInclusive, int maxExclusive) =>
            maxExclusive <= minInclusive ? minInclusive : (int)(minInclusive + (long)(NextDouble() * ((long)maxExclusive - minInclusive)));
        public float Range(float min, float max) => min + (float)(NextDouble() * (max - min));
    }

    public static class Rng
    {
        /// FNV-1a 32-bit.
        public static uint HashStr(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                if (s != null) foreach (char ch in s) { h ^= ch; h *= 16777619; }
                return h;
            }
        }
        /// Independent stream per name: new Mulberry32(seed ^ HashStr(name)).
        public static Mulberry32 Substream(uint seed, string name) => new Mulberry32(seed ^ HashStr(name));
        /// HashStr of the UTC date "yyyy-MM-dd".
        public static uint DailySeed(DateTime utc) =>
            HashStr((utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    public enum Medal { None, Bronze, Silver, Gold, Sync }

    public static class Medals
    {
        /// sync <= tDev*1.00, gold <= tDev*1.25, silver <= tDev*1.6, else bronze (a clear).
        public static Medal For(float time, float tDev) =>
            time <= Threshold(Medal.Sync, tDev) ? Medal.Sync
            : time <= Threshold(Medal.Gold, tDev) ? Medal.Gold
            : time <= Threshold(Medal.Silver, tDev) ? Medal.Silver
            : Medal.Bronze;
        /// Slowest time that still earns `m`; Bronze (any clear) and None have no limit.
        public static float Threshold(Medal m, float tDev) =>
            m == Medal.Sync ? tDev : m == Medal.Gold ? tDev * 1.25f : m == Medal.Silver ? tDev * 1.6f : float.PositiveInfinity;
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
        public static SaveV3 Default() => new SaveV3();
        /// v1 {version:1, best:{room:medal}, frags} -> v3; v2 -> v3; corrupt/empty -> Default() (never throws);
        /// version > 3 -> NewerSaveException; v3 -> same content (idempotent).
        /// v2 (never shipped) is v1 with the v3 names: medals{room:medal}, fragments. Every later field defaults.
        public static SaveV3 Migrate(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Default();
            JObject o;
            int v;
            try
            {
                o = JObject.Parse(json);
                var ver = o["version"];
                if (ver == null || ver.Type != JTokenType.Integer) return Default();
                v = (int)ver;
            }
            catch (Exception) { return Default(); }   // not JSON, not an object, version out of range
            if (v > Version) throw new NewerSaveException(v);
            if (v < 1) return Default();
            try
            {
                if (v == 1)
                {
                    Rename(o, "best", "medals");      // v1 best = room -> medal letter
                    Rename(o, "frags", "fragments");
                }
                o["version"] = Version;
                return Complete(o.ToObject<SaveV3>());
            }
            catch (Exception) { return Default(); }   // fields of the wrong shape
        }

        public static string Serialize(SaveV3 s) => JsonConvert.SerializeObject(s);

        static void Rename(JObject o, string from, string to)
        {
            var t = o[from];
            o.Remove(from);
            if (t != null) o[to] = t;
        }

        /// Fields missing or null in the stored JSON get their defaults, so callers never null-check.
        static SaveV3 Complete(SaveV3 s)
        {
            if (s == null) return Default();
            s.version = Version;
            s.playerId ??= "";
            s.unlocks ??= new List<string>();
            s.shards ??= new List<string>();
            s.medals ??= new Dictionary<string, string>();
            s.best ??= new Dictionary<string, RoomBest>();
            s.settings ??= new Dictionary<string, string>();
            s.stats ??= new RunStats();
            s.dailyBest ??= new Dictionary<string, int>();
            return s;
        }
    }
}
