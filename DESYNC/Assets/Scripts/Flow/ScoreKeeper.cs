using System.Collections;
using System.Collections.Generic;
using Desync.Rules;
using UnityEngine;
using EventType = Desync.Rules.EventType;

namespace Desync.Flow
{
    /// Live room score. Logs every RunEvent of the current attempt and feeds it through Rules.ScoreTally, the same code
    /// Scoring.ScoreFromEvents replays, so the log alone reproduces GameState.Score/Chain/Multiplier. Decides clean flips,
    /// chain breaks, GRAZE and REDLINE from GameEvents; RoomManager calls Begin (room start / REBOOT) and Finish (exit).
    /// Also the home of the chain exploits (Echo Flip, Clock Skew, Phase Memory, Spectral Fever) and flip-triggered combat
    /// exploits (Afterimage, Poltergeist, Mirror Shard decoys; Burnout Pulse ICE derez), since those key off flips and chain.
    [DefaultExecutionOrder(-90)]
    public class ScoreKeeper : MonoBehaviour
    {
        /// A flip is clean when no Spotted / TraceStarted / FlipRejected / death follows within this many room seconds.
        public const float CleanWindow = 1f;
        public const float RedlineSlowSeconds = 0.35f, RedlineTimeScale = 0.5f;
        public const float BurnoutRange = 6f, BurnoutDerez = 3f, BurnoutCooldown = 20f;   // burnout_pulse

        public static ScoreKeeper I;

        /// This attempt's events, in order (the future server payload).
        public readonly List<RunEvent> Log = new List<RunEvent>(512);
        public float PeakMultiplier { get; private set; } = 1f;
        public int BestChain { get; private set; }

        readonly ScoreTally tally = new ScoreTally();
        readonly float[] pending = new float[16];   // flip times awaiting their clean window (ring buffer)
        int pendHead, pendCount;
        float lastClean;
        bool live;
        string roomId;
        int echoCount;        // echo_flip: clean flips so far this room
        bool phaseUsed;       // phase_memory: the room's one forgiven break is spent
        float burnoutReady;   // burnout_pulse: room time the cooldown next allows a pulse

        static readonly string[] RoomIds = { "r1", "r2", "r3", "r4", "r5", "r6" };
        public static string RoomId(int index) => index >= 0 && index < RoomIds.Length ? RoomIds[index] : "r" + (index + 1);

        void Awake() => I = this;

        void OnEnable()
        {
            GameEvents.Flipped += OnFlipped;
            GameEvents.Spotted += OnSpotted;
            GameEvents.FlipRejected += VoidPending;
            GameEvents.TraceStarted += OnTrace;
            GameEvents.HackFailed += OnHackFailed;
            GameEvents.HackFinished += OnHackFinished;
            GameEvents.Grazed += OnGrazed;
            GameEvents.DeathRecap += OnDeath;
            Enemies.EnemyEvents.PuppetString += OnPuppetString;
        }

        void OnDisable()
        {
            GameEvents.Flipped -= OnFlipped;
            GameEvents.Spotted -= OnSpotted;
            GameEvents.FlipRejected -= VoidPending;
            GameEvents.TraceStarted -= OnTrace;
            GameEvents.HackFailed -= OnHackFailed;
            GameEvents.HackFinished -= OnHackFinished;
            GameEvents.Grazed -= OnGrazed;
            GameEvents.DeathRecap -= OnDeath;
            Enemies.EnemyEvents.PuppetString -= OnPuppetString;
        }

        /// Fresh attempt of room `index` (negative: no room, stop scoring).
        public void Begin(int index) => Begin(index >= 0 ? RoomId(index) : null);

        /// Fresh attempt of the room with this id (a run room uses its node id; null: no room, stop scoring).
        public void Begin(string id)
        {
            Log.Clear();
            tally.Reset();
            pendHead = pendCount = 0;
            lastClean = 0f;
            echoCount = 0;
            phaseUsed = false;
            burnoutReady = 0f;
            PeakMultiplier = 1f;
            BestChain = 0;
            live = id != null;
            roomId = id;
            Publish();
        }

        /// Exit reached: credits flips still inside their window, pays NO_TRACE_ROOM (no TRACE) and ROOM_CLEAR, stops scoring.
        /// Returns the room's event score.
        public int Finish(int traces)
        {
            if (!live) return tally.Total;
            while (pendCount > 0) CreditOldest();
            if (traces == 0) Add(EventType.NoTraceRoom);
            Add(EventType.RoomClear);
            live = false;
            return tally.Total;
        }

        void Update()
        {
            if (!live) return;
            float now = RoomClock.Time;
            while (pendCount > 0 && now - pending[pendHead] >= CleanWindow) CreditOldest();
            if (tally.Chain > 0 && pendCount == 0 && now - lastClean >= GameState.Mods.ChainWindow)
            {
                if (ForgiveBreak()) lastClean = now;   // Phase Memory restarts the window instead of breaking the chain
                else Add(EventType.ChainBreak);
            }
        }

        void CreditOldest()
        {
            pendHead = (pendHead + 1) % pending.Length;
            pendCount--;
            lastClean = RoomClock.Time;
            StepChain(true);
        }

        void VoidPending() => pendCount = 0;

        void OnFlipped(bool toSource)
        {
            if (!live) { FlipExploits(toSource); return; }   // outside scoring (e.g. finale) decoys/burnout still fire
            Add(EventType.Flip);
            if (!toSource && Add(EventType.Exit, melt: GameState.Melt) > 0) Redline();   // FlipController: melt before the flip cost
            if (pendCount == pending.Length) { pendHead = (pendHead + 1) % pending.Length; pendCount--; }
            pending[(pendHead + pendCount++) % pending.Length] = RoomClock.Time;
            FlipExploits(toSource);
        }

        void OnSpotted(Vector3 _) => VoidPending();

        void OnTrace(int count)
        {
            VoidPending();
            if (live) Add(EventType.Detected);
        }

        void OnHackFailed() { if (live && !ForgiveBreak()) Add(EventType.ChainBreak); }
        void OnHackFinished(bool perfect)
        {
            if (!live) return;
            Add(perfect ? EventType.HackPerfect : EventType.Hack);
            // Packet Loss: the hack is a chain step. CleanFlip is the only event that steps the chain, so it is logged as one
            // (the replay then agrees with the live score).
            if (Player.RunPerks.PacketLoss) { lastClean = RoomClock.Time; StepChain(true); }
        }
        void OnGrazed(string ice) { if (live) Add(EventType.Graze, ice); }
        void OnPuppetString(string id) { if (live) Add(EventType.PuppetString); }   // PUPPET_STRING 400 x multiplier (Scoring)

        void OnDeath(DeathInfo _)
        {
            VoidPending();
            if (!live) return;
            Add(EventType.Death);
            live = false;   // nothing scores during the recap; REBOOT begins a new attempt
        }

        // ---------- exploits ----------

        /// A chain step, counted twice at melt >= 80 under Spectral Fever (an extra CleanFlip keeps replay == live).
        void StepChain(bool allowDouble)
        {
            LogCleanFlip();
            if (allowDouble && Player.RunPerks.SpectralFever && GameState.Melt >= 80f) LogCleanFlip();
        }

        void LogCleanFlip()
        {
            Add(EventType.CleanFlip);
            if (Player.RunPerks.ClockSkewMelt > 0f) Player.RunPerks.MeltAdjust?.Invoke(-Player.RunPerks.ClockSkewMelt);   // Clock Skew
            int iv = Player.RunPerks.EchoFlipInterval;
            if (iv > 0 && ++echoCount % iv == 0) Player.RunPerks.MeltAdjust?.Invoke(-Player.RunPerks.EchoFlipRefund);       // Echo Flip
        }

        /// Phase Memory: forgive the first chain break per room (timeout or wrong hack arrow). Only consumed when a chain exists.
        bool ForgiveBreak()
        {
            if (!Player.RunPerks.PhaseMemory || phaseUsed || tally.Chain <= 0) return false;
            phaseUsed = true;
            return true;
        }

        /// Flip-keyed combat: decoys (Afterimage/Poltergeist silhouettes, Mirror Shard echo) and the Burnout Pulse ICE derez.
        /// Decoy spawns are inert until the enemies decoy pool is implemented; spawned on the flip (position-correct) rather than
        /// on the 1 s-late clean confirmation. Zero allocations (Vector3 / float by value).
        void FlipExploits(bool toSource)
        {
            Vector3 p = PlayerRef.Exists ? PlayerRef.Position : Vector3.zero;
            if (Player.RunPerks.AfterimageLife > 0f) Enemies.Decoys.Spawn(p, Player.RunPerks.AfterimageLife, Enemies.DecoyKind.Silhouette);
            if (Player.RunPerks.PoltergeistLife > 0f) Enemies.Decoys.Spawn(p, Player.RunPerks.PoltergeistLife, Enemies.DecoyKind.Silhouette);
            if (Player.RunPerks.MirrorShardLife > 0f) Enemies.Decoys.Spawn(p, Player.RunPerks.MirrorShardLife, Enemies.DecoyKind.Echo);
            if (!toSource && Player.RunPerks.BurnoutPulseMelt > 0f && GameState.Melt > 60f && RoomClock.Time >= burnoutReady)
            {
                burnoutReady = RoomClock.Time + BurnoutCooldown;
                Player.RunPerks.MeltAdjust?.Invoke(Player.RunPerks.BurnoutPulseMelt);   // Burnout Pulse spends melt
                Enemies.IceHunter.DerezWithin(p, BurnoutRange, BurnoutDerez);
            }
        }

        /// Logs one event, updates GameState and raises Scored when it paid. Returns what it paid.
        double Add(EventType type, string ice = null, float melt = 0f)
        {
            var e = new RunEvent(RoomClock.Time, type, roomId, ice, melt);
            Log.Add(e);
            double paid = tally.Apply(e, GameState.Mods);
            Publish();
            if (paid > 0)
            {
                var shown = type == EventType.Exit ? EventType.Redline : type;
                GameEvents.Scored?.Invoke(new ScorePop
                {
                    type = shown, points = (int)System.Math.Round(paid, System.MidpointRounding.AwayFromZero),
                    multiplier = GameState.Multiplier, label = Label(shown),
                });
            }
            return paid;
        }

        void Publish()
        {
            GameState.Score = tally.Total;
            GameState.Chain = tally.Chain;
            GameState.Multiplier = Scoring.GhostMultiplier(tally.Chain, GameState.Mods.ChainCap);
            if (GameState.Multiplier > PeakMultiplier) PeakMultiplier = GameState.Multiplier;
            if (tally.Chain > BestChain) BestChain = tally.Chain;
        }

        void Redline()
        {
            GameEvents.PopText?.Invoke("REDLINE", GameConfig.I.danger);
            GameEvents.ShakeRequest?.Invoke(0.4f);   // JuiceUtil drops it under Reduce FX
            StartCoroutine(SlowMo());
        }

        /// 0.5x time for 350 ms unless something else (pause, hit-stop, recap) holds the clock; never overrides them.
        static IEnumerator SlowMo()
        {
            if (Time.timeScale != 1f) yield break;
            Time.timeScale = RedlineTimeScale;
            yield return new WaitForSecondsRealtime(RedlineSlowSeconds);
            if (Time.timeScale == RedlineTimeScale) Time.timeScale = 1f;
        }

        static string Label(EventType t) => t switch
        {
            EventType.CleanFlip => "CLEAN FLIP",
            EventType.Graze => "GRAZE",
            EventType.Redline => "REDLINE",
            EventType.Hack => "HACK",
            EventType.HackPerfect => "PERFECT HACK",
            EventType.NoTraceRoom => "NO TRACE",
            EventType.RoomClear => "ROOM CLEAR",
            EventType.PuppetString => "PUPPET STRING",
            _ => "",
        };
    }
}
