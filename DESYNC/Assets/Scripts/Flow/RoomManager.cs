using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Desync.Flow
{
    /// Persistent per-room flow: room start, RoomClock, REBOOT (identical restart), terminals -> exit/finale, grading,
    /// GHOST combo, Room 5 trace countdown. Self-bootstraps; activates whenever a scene with a RoomInfo loads.
    [DefaultExecutionOrder(-100)]
    public class RoomManager : MonoBehaviour
    {
        public static RoomManager I;

        /// A flip counts as clean if no TRACE start / rejection / death happens within this many room seconds after it.
        public const float CleanFlipWindow = 2f;
        const float ClearedDelay = 2.5f, CompletedDelay = 4f;

        RoomInfo room;
        readonly List<IResettable> resettables = new List<IResettable>();
        bool rebooting, clearing, exitDone;
        int bestCombo, pendingFlips;
        float lastFlipTime;
        static readonly string[] ghostText = new string[64];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I) return;
            var go = new GameObject("RoomManager");
            DontDestroyOnLoad(go);
            I = go.AddComponent<RoomManager>();
            I.Activate();   // the first scene loaded before we existed
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            GameEvents.PlayerKilled += OnKilled;
            GameEvents.Alarm += Reboot;
            GameEvents.RestartRequested += Reboot;
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
            GameEvents.Alarm -= Reboot;
            GameEvents.RestartRequested -= Reboot;
            GameEvents.AccessGranted -= OnAccessGranted;
            GameEvents.ExitReached -= OnExitReached;
            GameEvents.GameCompleted -= OnGameCompleted;
            GameEvents.Flipped -= OnFlipped;
            GameEvents.FlipRejected -= BreakCombo;
            GameEvents.TraceStarted -= OnTraceStarted;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Activate();

        /// Start the room in RoomInfo.Current if it changed (runs on every scene load; tests call it directly).
        public void Activate()
        {
            var info = RoomInfo.Current;
            if (ReferenceEquals(info, room)) return;   // duplicate notification for the room already running
            StopAllCoroutines();
            rebooting = clearing = false;
            room = info;
            resettables.Clear();
            GameState.RoomIndex = room ? room.index : -1;
            ResetRoomState(false);   // a freshly loaded scene is already in its initial state (and Start hasn't run yet)
            if (!room) { RoomClock.Running = false; return; }
            foreach (var go in room.gameObject.scene.GetRootGameObjects())
                resettables.AddRange(go.GetComponentsInChildren<IResettable>(true));
            RoomClock.Running = true;
            GameEvents.RoomStarted?.Invoke(room.index);
        }

        /// LayerState, clock, every room field of GameState, then (on REBOOT) every IResettable in the room.
        void ResetRoomState(bool resetObjects)
        {
            LayerState.Set(false);
            RoomClock.Reset();
            GameState.Melt = 0f;
            GameState.Alive = true;
            GameState.TraceActive = false;
            GameState.TraceRemaining = 0f;
            GameState.TraceCount = 0;
            GameState.TraceCountdown = room && room.traceCountdown > 0f ? room.traceCountdown : -1f;
            GameState.Flips = 0;
            GameState.TerminalsDone = 0;
            GameState.TerminalsTotal = room ? room.terminalCount : 0;
            GameState.ExitOpen = false;
            GameState.InHack = false;
            GameState.Cinematic = false;
            GameState.HijackActive = false;
            GameState.HijackRemaining = 0f;
            GameState.GlitchCountdown = -1f;
            GameState.InteractPrompt = null;
            GameState.Pulse = 0f;
            exitDone = false;
            bestCombo = 0;
            BreakCombo();
            if (!resetObjects) return;
            foreach (var r in resettables)
            {
                if (r is Object o && !o) continue;   // destroyed since room start
                try { r.ResetState(); }
                catch (System.Exception e) { Debug.LogException(e); }   // one broken component must not stall the restart
            }
        }

        void Update()
        {
            if (!room) return;
            float dt = Time.deltaTime;
            RoomClock.Tick(dt);
            if (rebooting || clearing) return;

            if (GameInput.Restart.WasPressedThisFrame() && !GameState.Paused && !GameState.Cinematic) { Reboot(); return; }

            if (pendingFlips > 0 && RoomClock.Time - lastFlipTime >= CleanFlipWindow) CreditPendingFlips(true);

            if (GameState.TraceCountdown > 0f && GameState.Alive && !GameState.Cinematic && !GameState.DemoMode)
            {
                GameState.TraceCountdown = Mathf.Max(0f, GameState.TraceCountdown - dt);
                if (GameState.TraceCountdown <= 0f) GameEvents.Alarm?.Invoke();
            }
        }

        // ---------- REBOOT ----------

        void OnKilled(DeathCause cause)
        {
            if (GameState.God) { GameState.Alive = true; return; }
            Reboot();
        }

        public void Reboot()
        {
            if (!room || rebooting || clearing) return;
            rebooting = true;
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
            GameEvents.RoomReset?.Invoke();
        }

        // ---------- terminals / exit ----------

        void OnAccessGranted(string line)
        {
            if (!room || clearing) return;
            GameState.TerminalsDone++;
            AddCombo(1, false);   // PRD 5.4: hack complete = combo +1 (no GHOST pop: ACCESS GRANTED owns the centre)
            if (GameState.TerminalsDone >= GameState.TerminalsTotal) OpenExit();
        }

        /// All terminals done: open the exit, or start the finale in the last room. Also the debug "win" command.
        public void OpenExit()
        {
            if (!room || exitDone) return;
            exitDone = true;
            GameState.TerminalsDone = GameState.TerminalsTotal;
            if (room.index == SceneFlow.RoomCount - 1)
            {
                StoreResult();   // the finale has no exit portal; grade the room as the cinematic starts
                GameEvents.FinaleStarted?.Invoke();
                return;
            }
            GameState.ExitOpen = true;
            GameEvents.ExitOpened?.Invoke();
        }

        void OnExitReached()
        {
            if (!room || clearing || rebooting) return;
            clearing = true;
            GameState.Cinematic = true;   // freeze input + make the player unkillable during the grade card
            GameState.InteractPrompt = null;
            GameEvents.RoomCleared?.Invoke(StoreResult());
            StartCoroutine(LoadAfter(ClearedDelay, room.index + 1));
        }

        void OnGameCompleted() { if (room) StartCoroutine(LoadAfter(CompletedDelay, SceneFlow.RoomCount)); }

        static IEnumerator LoadAfter(float seconds, int roomIndex)
        {
            yield return new WaitForSecondsRealtime(seconds);
            SceneFlow.LoadRoom(roomIndex);   // index >= RoomCount loads Ending
        }

        RoomResult StoreResult()
        {
            if (pendingFlips > 0) CreditPendingFlips(false);
            var cfg = GameConfig.I;
            int i = room.index;
            var r = new RoomResult
            {
                room = i,
                time = RoomClock.Time,
                traces = GameState.TraceCount,
                flips = GameState.Flips,
                bestCombo = bestCombo,
            };
            r.grade = Grade(r.time, r.traces, r.flips, cfg.parTime[i], cfg.parFlips[i]);
            GameState.Results[i] = r;
            return r;
        }

        // ---------- grading ----------

        /// 100 - 25 per TRACE - 5 per flip over par - 5 per 10% of par time over par.
        public static float Points(float time, int traces, int flips, float parTime, int parFlips) =>
            100f - 25f * traces - 5f * Mathf.Max(0, flips - parFlips) - 50f * Mathf.Max(0f, time - parTime) / Mathf.Max(1f, parTime);

        /// S >= 90, A >= 70, B >= 45, else C.
        public static char Grade(float points) => points >= 90f ? 'S' : points >= 70f ? 'A' : points >= 45f ? 'B' : 'C';

        public static char Grade(float time, int traces, int flips, float parTime, int parFlips) =>
            Grade(Points(time, traces, flips, parTime, parFlips));

        // ---------- GHOST combo ----------

        void OnFlipped(bool toSource)
        {
            if (!room || rebooting || clearing) return;
            pendingFlips++;
            lastFlipTime = RoomClock.Time;
        }

        void OnTraceStarted(int count) => BreakCombo();

        void CreditPendingFlips(bool pop)
        {
            int n = pendingFlips;
            pendingFlips = 0;
            AddCombo(n, pop);
        }

        void AddCombo(int n, bool pop)
        {
            GameState.Combo += n;
            if (GameState.Combo > bestCombo) bestCombo = GameState.Combo;
            GameEvents.ComboChanged?.Invoke(GameState.Combo);
            if (pop && GameState.Combo >= 2) GameEvents.PopText?.Invoke(GhostText(GameState.Combo), GameConfig.I.magenta);
        }

        void BreakCombo()
        {
            pendingFlips = 0;
            if (GameState.Combo == 0) return;
            GameState.Combo = 0;
            GameEvents.ComboChanged?.Invoke(0);
        }

        static string GhostText(int n)
        {
            if (n >= ghostText.Length) return "GHOST ×" + n;
            return ghostText[n] ??= "GHOST ×" + n;   // built once per value, never per frame
        }
    }
}
