using System;
using System.Collections;
using Desync.Enemies;
using Desync.Gen;
using Desync.Levels;
using Desync.Net;
using Desync.Player;
using Desync.Rules;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Desync.Flow.Tests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Desync.Flow.PlayTests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Desync.UI.Tests")]

namespace Desync.Flow
{
    /// Time-Attack: one fixed room (TimeAttackRooms), start -> exit, against the clock and your own ghost. No exploits,
    /// mutators or Backups: a death is the classic recap + REBOOT and R restarts in place (RoomManager, < 300 ms); the HUD
    /// timer and medal ticks run on the room's T_dev. Results: [R] retry, [SPACE] back to the select screen (SelectRequested).
    ///
    /// Every attempt is recorded at 20 Hz (feet, yaw, layer; fixed buffer). A clear faster than the stored ghost (or the
    /// first one) becomes the room's ghost; the ghost replays on RoomClock as a wireframe figure in its own layer's colour.
    /// RoomManager asks this class for the room's id / targets / previous best / share text and hands it Continue.
    /// Entry point: Play(id), from the select screen's click or key handler (the pointer lock needs that gesture).
    public sealed class TimeAttack : MonoBehaviour
    {
        public static TimeAttack I;
        /// Flow -> UI: open the Time-Attack select screen (raised once the title is back after a room).
        public static Action SelectRequested;
        /// The ghost is hidden this long after its recording ends.
        public const float GhostLinger = 0.5f;

        /// A Time-Attack room is being built or played.
        public static bool Live => I && I.room != null;
        /// The room being played, or null.
        public static TimeAttackRooms.Room Current => I ? I.room : null;

        TimeAttackRooms.Room room;
        RoomSpec spec;
        uint layout;
        GameObject stageRoot;
        IDisposable handle;
        int token;
        readonly GhostTrack rec = new GhostTrack(GhostTrack.RecordCapacity), ghost = new GhostTrack(GhostTrack.MaxStored);
        GhostFigure figure;
        bool recording;
        float nextSample;

        /// Test seams: the attempt being recorded and the ghost being replayed.
        internal GhostTrack Recording => rec;
        internal GhostTrack Ghost => ghost;
        internal GhostFigure Figure => figure;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I) return;
            var go = new GameObject("TimeAttackFlow");
            DontDestroyOnLoad(go);
            I = go.AddComponent<TimeAttack>();
            I.figure = GhostFigure.Create(go.transform);
        }

        void OnEnable()
        {
            GameEvents.RoomStarted += OnRoomStarted;
            GameEvents.RoomReset += OnRoomReset;
            GameEvents.DeathRecap += OnDeath;
            GameEvents.RoomReportReady += OnReport;
            GameEvents.QuitRequested += OnOtherFlow;
            GameEvents.StartRequested += OnClassicStart;
            RunEvents.StartRun += OnRunStart;
            DeepRun.StartRequested += OnDeepStart;
        }

        void OnDisable()
        {
            GameEvents.RoomStarted -= OnRoomStarted;
            GameEvents.RoomReset -= OnRoomReset;
            GameEvents.DeathRecap -= OnDeath;
            GameEvents.RoomReportReady -= OnReport;
            GameEvents.QuitRequested -= OnOtherFlow;
            GameEvents.StartRequested -= OnClassicStart;
            RunEvents.StartRun -= OnRunStart;
            DeepRun.StartRequested -= OnDeepStart;
        }

        // ---------- start / leave ----------

        /// Starts room `id` (TimeAttackRooms ids). False when the id is unknown or Time-Attack is locked for this profile.
        public static bool Play(string id)
        {
            var r = TimeAttackRooms.Find(id);
            if (!I || r == null || !TimeAttackRooms.Unlocked(SaveService.Data)) return false;
            I.Begin(r);
            return true;
        }

        void Begin(TimeAttackRooms.Room r)
        {
            if (RunManager.I) RunManager.I.Abandon();   // a run (or its results screen) ends here
            Stop();
            room = r;
            GameState.Mods = Modifiers.Default;           // no exploits, mutators or Backups
            GameState.Flags.Clear();
            RunPerks.Clear();
            RoomManager.StartMelt = 0f;
            Cursor.lockState = CursorLockMode.Locked;     // inside the click / key handler that chose the room
            spec = TimeAttackRooms.Spec(r);
            layout = TimeAttackRooms.Layout(spec);
            if (r.Classic)
            {
                GameState.Seed = 0;
                SceneFlow.LoadRoom(r.classic);            // RoomManager activates on load and asks Targets
                return;
            }
            StartCoroutine(Enter(token));
        }

        IEnumerator Enter(int tk)
        {
            var stage = SceneManager.GetSceneByName(RunManager.StageScene);
            if (SceneManager.GetActiveScene().name != RunManager.StageScene)
            {
                if (stage.IsValid() && stage.isLoaded) SceneManager.SetActiveScene(stage);
                else if (Application.CanStreamedLevelBeLoaded(RunManager.StageScene)) yield return SceneManager.LoadSceneAsync(RunManager.StageScene);
                else SceneManager.SetActiveScene(SceneManager.CreateScene(RunManager.StageScene));   // not in Build Settings (tests)
            }
            if (tk != token) yield break;
            GameState.Seed = spec.seed;
            if (RunRules.AuditorDrones(new RunNode { type = NodeType.Infil, act = room.act, index = room.Node }, spec))
                GameState.Flags.Add(RunRules.AuditorDronesFlag);   // the same room the campaign would build at this act / node
            EnemyFlags.Refresh();
            stageRoot = new GameObject("TimeAttackRoom");   // in the stage scene; room space = world space
            RunManager.BuildRoom(spec, stageRoot.transform, h => OnBuilt(h, tk));
        }

        void OnBuilt(IDisposable h, int tk)
        {
            if (tk != token || !stageRoot) { h?.Dispose(); return; }
            handle = h;
            if (!RoomInfo.Current) { Debug.LogError("[DESYNC] the time-attack room has no RoomInfo"); return; }
            Cursor.lockState = CursorLockMode.Locked;   // the stage load freed it; the browser applies it on the next input
            RoomManager.I.Activate();
        }

        /// RoomManager, ContinueRequested on the results: back to the title and the select screen.
        internal void Leave()
        {
            Stop();
            Cursor.lockState = CursorLockMode.None;
            StartCoroutine(BackToSelect());
        }

        IEnumerator BackToSelect()
        {
            yield return SceneManager.LoadSceneAsync(SceneFlow.Title);
            SelectRequested?.Invoke();
        }

        /// Ends the Time-Attack room (if any): the room, its ghost and recording go; RoomManager detaches.
        internal void Stop()
        {
            token++;
            StopAllCoroutines();
            recording = false;
            ghost.Clear();
            if (figure) figure.Hide();
            if (room == null) return;
            room = null;
            spec = null;
            if (RoomManager.I) RoomManager.I.Detach();
            if (stageRoot) RoomInfo.Current = null;   // a disposed generated room must not stay current until Destroy lands
            handle?.Dispose();
            handle = null;
            if (stageRoot)
            {
                foreach (var h in stageRoot.GetComponentsInChildren<RoomHandle>(true)) h.Dispose();   // still building
                Destroy(stageRoot);
            }
            stageRoot = null;
        }

        void OnOtherFlow() => Stop();                 // quit to title
        void OnClassicStart(int index) => Stop();     // F2 level select
        void OnRunStart(RunMode mode) => Stop();
        void OnDeepStart(int level, System.Collections.Generic.IReadOnlyList<Content.MutatorPick> picks) => Stop();

        // ---------- RoomManager hooks ----------

        /// Room id, T_dev and par score of the room being played (RoomManager.Activate).
        internal static void Targets(RoomInfo info, out string id, out float tDev, out int parScore)
        {
            var r = I.room;
            id = r.id;
            if (r.Classic) RoomReports.RoomTargets(info.index, info.terminalCount, out tDev, out parScore);
            else
            {
                tDev = TimeAttackRooms.TDev(r);
                parScore = RoomReports.FallbackPar(info.terminalCount);
            }
        }

        /// The saved best before this clear (RoomManager, as it builds the report).
        internal static RoomBest PreviousBest()
        {
            try { return SaveService.GetBest(I.room.id); }
            catch (Exception e) { Debug.LogException(e); return null; }   // a broken profile must not block the results
        }

        /// DESYNC // 2026-10-24 // TIME-ATTACK A1-07 GOLD // 0:41.27 // GHOST x5 // desync-nine.vercel.app
        internal static string ShareText(RoomReport r, int bestChain)
        {
            int m = (int)r.medal;
            string medal = m == 4 ? "SYNC" : m == 3 ? "GOLD" : m == 2 ? "SILVER" : m == 1 ? "BRONZE" : "CLEAR";
            return RoomReports.ShareText(DateTime.UtcNow, "TIME-ATTACK " + I.room.Label + " " + medal, r.time, bestChain);
        }

        // ---------- attempts, recording, ghost ----------

        void OnRoomStarted(int index)
        {
            if (!Live) return;
            ghost.Decode(TimeAttackRooms.LoadGhost(SaveService.Data, room.id), layout);   // empty when none / another layout
            BeginAttempt();
        }

        void OnRoomReset() { if (Live) BeginAttempt(); }

        void BeginAttempt()
        {
            rec.Clear();
            nextSample = 0f;
            recording = true;
        }

        void OnDeath(DeathInfo d) => recording = false;

        /// Saves the clear (generated rooms: best + medal key) and, when it beats the ghost, makes it the room's ghost.
        void OnReport(RoomReport r)
        {
            if (!Live || r == null) return;
            recording = false;
            var save = SaveService.Data;
            TimeAttackRooms.Record(save, room, r);
            if (rec.Count > 0 && (ghost.Count == 0 || r.time < ghost.RunTime))
            {
                string blob = rec.Encode(layout, r.time);
                TimeAttackRooms.StoreGhost(save, room.id, blob);
                ghost.Decode(blob, layout);   // the next attempt races this run
            }
            SaveService.Save();
        }

        void Update()
        {
            if (!Live || !RoomInfo.Current) { figure.Hide(); return; }
            float t = RoomClock.Time;
            if (recording && PlayerRef.Exists && GameState.Phase == GameState.PhaseKind.Playing)
                while (t >= nextSample)
                {
                    if (!rec.Add(PlayerRef.Position, PlayerRef.Root.eulerAngles.y, LayerState.InSource)) { recording = false; break; }
                    nextSample += GhostTrack.Step;
                }
            if (ghost.Count > 0 && t <= ghost.Duration + GhostLinger && ghost.Sample(t, out var feet, out float yaw, out bool source))
                figure.Show(feet, yaw, source);
            else figure.Hide();
        }
    }
}
