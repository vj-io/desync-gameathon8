using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Desync.Enemies;
using Desync.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Desync.Flow
{
    /// Persistent per-room flow: room start, RoomClock, death recap -> REBOOT (identical restart, in place), terminals ->
    /// exit/finale -> results (RoomReport) -> continue / retry, legacy GHOST combo, Room 5 trace countdown.
    /// Scoring lives in ScoreKeeper (same GameObject). Self-bootstraps; activates whenever a scene with a RoomInfo loads.
    /// In a BREACH run (RunManager builds the room, then calls Activate) the run decides deaths, continue and targets.
    [DefaultExecutionOrder(-100)]
    public class RoomManager : MonoBehaviour
    {
        public static RoomManager I;

        /// Legacy combo (GameState.Combo): a flip counts if no TRACE start / rejection / death within this many room seconds.
        public const float CleanFlipWindow = 2f;
        /// Death recap: time frozen this long (unscaled) unless a key/click skips it; a near miss is reported within NearMissRange m.
        public const float RecapSeconds = 1.2f, NearMissRange = 6f;
        const float CompletedDelay = 4f;   // "SIMULATION: COMPROMISED" before the room 6 results

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// Melt every attempt of the room starts with, REBOOT included: the run's carried melt (RunManager), 0 in CLASSIC.
        public static float StartMelt;

        RoomInfo room;
        ScoreKeeper score;
        readonly List<IResettable> resettables = new List<IResettable>();
        string roomId;
        bool run;   // a BREACH run room: RunManager owns deaths (Backups), continue, targets; R does not restart
        bool rebooting, clearing, exitDone, recap, leaving;
        int bestCombo, pendingFlips, recapFrame, roomPar, finaleScore;
        float lastFlipTime, recapEnd, finaleTime;
        double restartFrom;
        Coroutine finaleResults;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I) return;
            var go = new GameObject("RoomManager");
            DontDestroyOnLoad(go);
            go.AddComponent<ScoreKeeper>();
            I = go.AddComponent<RoomManager>();
            I.Activate();   // the first scene loaded before we existed
        }

        void Awake() => score = GetComponent<ScoreKeeper>();

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            GameEvents.PlayerKilled += OnKilled;
            GameEvents.Alarm += OnAlarm;
            GameEvents.RestartRequested += OnRestartRequested;
            GameEvents.ContinueRequested += OnContinue;
            GameEvents.StartRequested += OnStartRequested;
            GameEvents.AccessGranted += OnAccessGranted;
            GameEvents.ExitReached += OnExitReached;
            GameEvents.GameCompleted += OnGameCompleted;
            GameEvents.Flipped += OnFlipped;
            GameEvents.FlipRejected += BreakCombo;
            GameEvents.TraceStarted += OnTraceStarted;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            GameEvents.PlayerKilled -= OnKilled;
            GameEvents.Alarm -= OnAlarm;
            GameEvents.RestartRequested -= OnRestartRequested;
            GameEvents.ContinueRequested -= OnContinue;
            GameEvents.StartRequested -= OnStartRequested;
            GameEvents.AccessGranted -= OnAccessGranted;
            GameEvents.ExitReached -= OnExitReached;
            GameEvents.GameCompleted -= OnGameCompleted;
            GameEvents.Flipped -= OnFlipped;
            GameEvents.FlipRejected -= BreakCombo;
            GameEvents.TraceStarted -= OnTraceStarted;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Activate();

        static void OnStartRequested(int index) => RoomReports.ClearRun();

        /// Start the room in RoomInfo.Current if it changed (runs on every scene load; tests call it directly).
        public void Activate()
        {
            var info = RoomInfo.Current;
            if (ReferenceEquals(info, room)) return;   // duplicate notification for the room already running
            StopAllCoroutines();
            if (recap) Time.timeScale = 1f;            // left mid-recap: release the freeze
            rebooting = clearing = recap = leaving = false;
            finaleResults = null;
            room = info;
            run = room && RunManager.I && RunManager.I.RoomLive;
            resettables.Clear();
            GameState.RoomIndex = room ? room.index : -1;
            GameState.DeathsInRoom = 0;
            GameState.RoomTDev = 0f;
            roomId = null;
            if (run) RunManager.I.Targets(room, out roomId, out GameState.RoomTDev, out roomPar);
            else if (room)
            {
                if (room.index >= 0) roomId = ScoreKeeper.RoomId(room.index);   // a generated room outside a run is not scored
                RoomReports.RoomTargets(room.index, room.terminalCount, out GameState.RoomTDev, out roomPar);
            }
            ResetRoomState(false);   // a freshly loaded scene is already in its initial state (and Start hasn't run yet)
            if (!room)
            {
                RoomClock.Running = false;
                GameState.Phase = SceneManager.GetActiveScene().name == SceneFlow.Ending ? GameState.PhaseKind.Ending : GameState.PhaseKind.Title;
                return;
            }
            foreach (var go in room.gameObject.scene.GetRootGameObjects())
                resettables.AddRange(go.GetComponentsInChildren<IResettable>(true));
            RoomClock.Running = true;
            GameEvents.RoomStarted?.Invoke(room.index);
        }

        /// RunManager removed the room (next node, run over): drop it and stop everything room-bound until the next Activate.
        public void Detach()
        {
            StopAllCoroutines();
            if (recap) Time.timeScale = 1f;
            rebooting = clearing = recap = leaving = run = false;
            finaleResults = null;
            room = null;
            roomId = null;
            resettables.Clear();
            GameState.RoomIndex = -1;
            ResetRoomState(false);   // back to RENDER, room fields cleared: what a scene without a room gets
            RoomClock.Running = false;
            GameState.Phase = GameState.PhaseKind.Title;   // menus (run map, draft, market) until the next room
        }

        /// LayerState, clock, every room field of GameState, scoring, then (on REBOOT) every IResettable in the room.
        void ResetRoomState(bool resetObjects)
        {
            LayerState.Set(false);
            RoomClock.Reset();
            GameState.Melt = StartMelt;
            GameState.Alive = true;
            GameState.TraceActive = false;
            GameState.TraceRemaining = 0f;
            GameState.TraceCount = 0;
            GameState.TraceCountdown = room && room.traceCountdown > 0f ? room.traceCountdown : -1f;
            GameState.Flips = 0;
            GameState.TerminalsDone = 0;
            GameState.TerminalsTotal = room ? TerminalsNeeded(room.terminalCount, Backdoor()) : 0;
            GameState.ExitOpen = false;
            GameState.InHack = false;
            GameState.Cinematic = false;
            GameState.HijackActive = false;
            GameState.HijackRemaining = 0f;
            GameState.GlitchCountdown = -1f;
            GameState.InteractPrompt = null;
            GameState.Pulse = 0f;
            if (room) GameState.Phase = GameState.PhaseKind.Playing;
            PlayerRef.LastKillerId = null;
            if (finaleResults != null) { StopCoroutine(finaleResults); finaleResults = null; }
            exitDone = clearing = false;
            bestCombo = 0;
            BreakCombo();
            score.Begin(roomId);
            if (!resetObjects) return;
            foreach (var r in resettables)
            {
                if (r is Object o && !o) continue;   // destroyed since room start
                try { r.ResetState(); }
                catch (Exception e) { Debug.LogException(e); }   // one broken component must not stall the restart
            }
            GameState.Melt = StartMelt;   // MeltMeter.ResetState zeroed it
        }

        /// Backdoor: rooms have no locked doors, so the exploit opens the exit one terminal early (never below one).
        public static int TerminalsNeeded(int terminals, bool backdoor) => backdoor && terminals > 1 ? terminals - 1 : terminals;

        /// Read once per room start / REBOOT (D-013). Not on a boss node: its uplink phases count every terminal.
        bool Backdoor() => run && GameState.Flags.Contains("flag:backdoor")
                           && RunManager.I.CurrentNode != null && RunManager.I.CurrentNode.type != Gen.NodeType.Boss;

        void Update()
        {
            if (!room) return;
            float dt = Time.deltaTime;
            RoomClock.Tick(dt);
            if (recap)
            {
                if (Time.realtimeSinceStartup >= recapEnd || RecapSkipped()) EndRecap();
                return;
            }
            if (rebooting || clearing) return;

            if (!run && GameInput.Restart.WasPressedThisFrame() && !GameState.Paused && !GameState.Cinematic) { Reboot(); return; }

            if (pendingFlips > 0 && RoomClock.Time - lastFlipTime >= CleanFlipWindow) CreditPendingFlips();

            if (GameState.TraceCountdown > 0f && GameState.Alive && !GameState.Cinematic && !GameState.DemoMode)
            {
                GameState.TraceCountdown = Mathf.Max(0f, GameState.TraceCountdown - dt);
                if (GameState.TraceCountdown <= 0f) GameEvents.Alarm?.Invoke();
            }
        }

        // ---------- death recap -> REBOOT ----------

        void OnKilled(DeathCause cause)
        {
            if (GameState.God) { GameState.Alive = true; return; }
            StartRecap(cause, PlayerRef.LastKillerId);
        }

        /// Third TRACE (TraceSystem) or the Room 5 countdown running out (TraceCountdown held at 0).
        void OnAlarm() => StartRecap(GameState.TraceCountdown == 0f ? DeathCause.TraceTimer : DeathCause.Alarm, null);

        /// Freeze (timeScale 0) on the moment of death for RecapSeconds unscaled with the DeathInfo card, then REBOOT.
        void StartRecap(DeathCause cause, string killerId)
        {
            if (!room || recap || rebooting || clearing || leaving) return;
            recap = true;
            RoomClock.Running = false;
            GameState.Alive = false;   // an alarm is a death too: nothing moves or kills under the card
            pendingFlips = 0;
            var info = new DeathInfo
            {
                cause = cause,
                killerId = killerId,
                killerPos = killerId != null ? PlayerRef.LastKillerPos : Vector3.zero,
                roomTime = RoomClock.Time,
                nearMiss = NearMiss(),
            };
            PlayerRef.LastKillerId = null;   // consumed: the next death names its own killer
            GameState.DeathsInRoom++;
            if (run) RunManager.I.OnDeath();   // spends a Backup or marks the run lost, before the card reads RunState
            GameState.Phase = GameState.PhaseKind.Recap;
            Time.timeScale = 0f;
            recapFrame = Time.frameCount;
            recapEnd = Time.realtimeSinceStartup + RecapSeconds;
            GameEvents.DeathRecap?.Invoke(info);
            if (GameState.DeathsInRoom % 3 == 0)
            {
                string hint = RoomReports.Hint(cause, killerId, GameState.DeathsInRoom / 3 - 1);
                if (hint != null) GameEvents.Hint?.Invoke(hint);
            }
        }

        /// One TRUE near-miss stat or null: the open exit's distance when it was under NearMissRange.
        static string NearMiss()
        {
            if (!GameState.ExitOpen || !PlayerRef.Exists) return null;
            var exit = Object.FindAnyObjectByType<ExitPortal>();
            if (!exit) return null;
            float d = Vector3.Distance(PlayerRef.Position, exit.transform.position);
            return d < NearMissRange ? "exit was " + d.ToString("0.0", Inv) + " m away" : null;
        }

        /// Any key, click or pad button after the frame the recap began.
        bool RecapSkipped()
        {
            if (Time.frameCount == recapFrame) return false;
            var k = Keyboard.current;
            var m = Mouse.current;
            var g = Gamepad.current;
            return (k != null && k.anyKey.wasPressedThisFrame)
                || (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame))
                || (g != null && (g.buttonSouth.wasPressedThisFrame || g.startButton.wasPressedThisFrame));
        }

        void EndRecap()
        {
            recap = false;
            Time.timeScale = GameState.Paused ? 0f : 1f;
            if (run && RunManager.I.RunLost) { RunManager.I.EndAfterDeath(); return; }   // no Backup left: run results
            Reboot();   // in a run this is the Backup respawn: room start, chain reset
        }

        /// Pause-menu restart / results Retry. In a run only the recap skip counts: a free restart would dodge the Backup cost.
        void OnRestartRequested()
        {
            if (run && !recap) return;
            Reboot();
        }

        /// R, pause-menu restart, recap end, results Retry. Control returns RoomReset; the time it took is GameState.LastRestartMs.
        public void Reboot()
        {
            if (!room || rebooting || leaving) return;
            if (recap) { EndRecap(); return; }   // a restart request during the recap skips it
            rebooting = true;
            restartFrom = Time.realtimeSinceStartupAsDouble;
            RoomClock.Running = false;
            pendingFlips = 0;
            GameEvents.Rebooting?.Invoke();
            StartCoroutine(RebootRoutine());
        }

        IEnumerator RebootRoutine()
        {
            yield return new WaitForSecondsRealtime(GameConfig.I.restartWipe);
            ResetRoomState(true);
            RoomClock.Running = true;
            rebooting = false;
            if (!GameState.Paused) Time.timeScale = 1f;   // no freeze or slow-mo outlives a restart
            GameState.LastRestartMs = (float)((Time.realtimeSinceStartupAsDouble - restartFrom) * 1000.0);
            GameEvents.RoomReset?.Invoke();
        }

        // ---------- terminals / exit / results ----------

        void OnAccessGranted(string line)
        {
            if (!room || clearing) return;
            GameState.TerminalsDone++;
            AddCombo(1);   // PRD 5.4: hack complete = combo +1
            if (GameState.TerminalsDone >= GameState.TerminalsTotal) OpenExit();
        }

        /// All terminals done: open the exit, or start the finale in the last room. Also the debug "win" command.
        public void OpenExit()
        {
            if (!room || exitDone) return;
            exitDone = true;
            GameState.TerminalsDone = GameState.TerminalsTotal;
            if (!run && room.index == SceneFlow.RoomCount - 1)
            {
                finaleTime = RoomClock.Time;   // the last room is cleared as the finale starts; results follow it
                finaleScore = score.Finish(GameState.TraceCount);
                GameEvents.FinaleStarted?.Invoke();
                return;
            }
            GameState.ExitOpen = true;
            GameEvents.ExitOpened?.Invoke();
        }

        void OnExitReached()
        {
            if (!room || clearing || rebooting || recap) return;
            clearing = true;
            GameState.Cinematic = true;   // freeze input + make the player unkillable on the results
            GameState.InteractPrompt = null;
            RoomClock.Running = false;    // the room timer stops at the exit
            ShowResults(RoomClock.Time, score.Finish(GameState.TraceCount), true);
        }

        void OnGameCompleted()
        {
            if (!room || clearing) return;
            clearing = true;
            finaleResults = StartCoroutine(FinaleResults());
        }

        IEnumerator FinaleResults()
        {
            yield return new WaitForSecondsRealtime(CompletedDelay);
            finaleResults = null;
            ShowResults(finaleTime, finaleScore, false);
        }

        /// Builds the RoomReport (previous best read before SaveService records this one), stores the legacy RoomResult,
        /// then waits on the results: ContinueRequested -> next room / Ending, RestartRequested -> retry in place.
        void ShowResults(float time, int eventsScore, bool raiseCleared)
        {
            var report = RoomReports.Build(room.index, time, eventsScore, score.PeakMultiplier, GameState.RoomTDev, roomPar,
                                           run ? null : PreviousBest(room.index), GameState.TraceCount, GameState.Mods.Clamped().ScoreMult);
            report.shareText = run ? RunManager.I.RoomCleared(report, score.BestChain)
                                   : RoomReports.AddToRun(report, score.BestChain, DateTime.UtcNow);
            var legacy = StoreResult(report);
            if (raiseCleared) GameEvents.RoomCleared?.Invoke(legacy);   // kept for listeners of the old grade card
            GameState.Phase = GameState.PhaseKind.Results;
            GameEvents.RoomReportReady?.Invoke(report);
        }

        static Rules.RoomBest PreviousBest(int index)
        {
            try { return SaveService.GetBest(ScoreKeeper.RoomId(index)); }
            catch (Exception e) { Debug.LogException(e); return null; }   // a broken profile must not block the results
        }

        void OnContinue()
        {
            if (!room || leaving || GameState.Phase != GameState.PhaseKind.Results) return;
            leaving = true;
            if (run) { RunManager.I.ContinueFromResults(); return; }   // draft, map, next act or run results
            SceneFlow.LoadRoom(room.index + 1);   // index >= RoomCount loads Ending
        }

        RoomResult StoreResult(RoomReport report)
        {
            if (pendingFlips > 0) CreditPendingFlips();
            int i = room.index;
            var r = new RoomResult
            {
                room = i,
                score = report.total,
                medal = report.medal,
                grade = report.grade,   // one grade per clear: results, level select and ending agree
                time = report.time,
                traces = GameState.TraceCount,
                flips = GameState.Flips,
                bestCombo = bestCombo,
            };
            if (run) return r;   // run rooms are generated (or played under exploits): no classic best, no ending row
            GameState.Results[i] = r;
            BestTimes.Record(r);
            return r;
        }

        // ---------- pre-BREACH grading, no longer used by the flow (RoomReport.grade = Scoring.Grade); kept for its tests ----------

        /// 100 - 25 per TRACE - 5 per flip over par - 5 per 10% of par time over par.
        public static float Points(float time, int traces, int flips, float parTime, int parFlips) =>
            100f - 25f * traces - 5f * Mathf.Max(0, flips - parFlips) - 50f * Mathf.Max(0f, time - parTime) / Mathf.Max(1f, parTime);

        /// S >= 90, A >= 70, B >= 45, else C.
        public static char Grade(float points) => points >= 90f ? 'S' : points >= 70f ? 'A' : points >= 45f ? 'B' : 'C';

        public static char Grade(float time, int traces, int flips, float parTime, int parFlips) =>
            Grade(Points(time, traces, flips, parTime, parFlips));

        // ---------- legacy GHOST combo (GameState.Combo / ComboChanged; the scored chain is GameState.Chain) ----------

        void OnFlipped(bool toSource)
        {
            if (!room || rebooting || clearing || recap) return;
            pendingFlips++;
            lastFlipTime = RoomClock.Time;
        }

        void OnTraceStarted(int count) => BreakCombo();

        void CreditPendingFlips()
        {
            int n = pendingFlips;
            pendingFlips = 0;
            AddCombo(n);
        }

        void AddCombo(int n)
        {
            GameState.Combo += n;
            if (GameState.Combo > bestCombo) bestCombo = GameState.Combo;
            GameEvents.ComboChanged?.Invoke(GameState.Combo);
        }

        void BreakCombo()
        {
            pendingFlips = 0;
            if (GameState.Combo == 0) return;
            GameState.Combo = 0;
            GameEvents.ComboChanged?.Invoke(0);
        }
    }
}
