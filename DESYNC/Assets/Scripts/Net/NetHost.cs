using UnityEngine;
using UnityEngine.SceneManagement;

namespace Desync.Net
{
    /// Persistent host for everything that touches the outside world: the profile (SaveService), the clipboard, the page hook
    /// (window.__DESYNC__) and the analytics call sites. Self-bootstraps; it only listens to GameEvents, never calls other folders.
    public sealed class NetHost : MonoBehaviour
    {
        const float PublishInterval = 0.25f;   // page hook refresh, ~4 Hz

        static NetHost I;
        static bool ready, copyPending;
        float acc;
        int frames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I) return;
            var go = new GameObject("DesyncNet");
            DontDestroyOnLoad(go);
            I = go.AddComponent<NetHost>();
        }

        void Awake()
        {
            SaveService.Load();   // parse + migrate now, not in the middle of a run
            WebBridge.HookInit();
            Debug.Log("[DESYNC] " + Diagnostics.Stamp());
        }

        void OnEnable()
        {
            GameEvents.Flipped += OnFlipped;
            GameEvents.PlayerKilled += OnKilled;
            GameEvents.StartRequested += OnStart;
            GameEvents.GameCompleted += OnCompleted;
            GameEvents.RoomStarted += OnRoomStarted;
            GameEvents.Rebooting += OnRebooting;
            GameEvents.RoomReportReady += OnReport;
            GameEvents.CopyRequested += OnCopy;
            GameEvents.Paused += OnPaused;
        }

        void OnDisable()
        {
            GameEvents.Flipped -= OnFlipped;
            GameEvents.PlayerKilled -= OnKilled;
            GameEvents.StartRequested -= OnStart;
            GameEvents.GameCompleted -= OnCompleted;
            GameEvents.RoomStarted -= OnRoomStarted;
            GameEvents.Rebooting -= OnRebooting;
            GameEvents.RoomReportReady -= OnReport;
            GameEvents.CopyRequested -= OnCopy;
            GameEvents.Paused -= OnPaused;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            // Held in memory only: it reaches storage with the next write (death, room record, pause, tab hidden), not every 2 s.
            if (GameState.Phase == GameState.PhaseKind.Playing && !GameState.Paused) SaveService.Data.stats.playSeconds += dt;
            SaveService.Tick();
            if (copyPending) PollCopy();

            frames++; acc += dt;
            if (acc >= PublishInterval) Publish();
        }

        void Publish()
        {
            if (!ready && SceneManager.GetActiveScene().name != SceneFlow.Boot)
            {
                ready = true;
                Analytics.Track("game_loaded", ("build", BuildInfo.Hash));
            }
            float fps = frames / acc;
            frames = 0; acc = 0f;
            int state = (int)GameState.Phase;                                  // Title, Playing, Paused, Recap, Results, Ending
            if (GameState.Phase == GameState.PhaseKind.Playing && GameState.Paused) state = (int)GameState.PhaseKind.Paused;
            WebBridge.HookUpdate(ready, state, GameState.RoomIndex, unchecked((int)GameState.Seed), GameState.LastRestartMs, fps);
        }

        // A hidden tab or a closing page is the last chance to reach IndexedDB.
        void OnApplicationPause(bool paused) { if (paused) SaveService.Flush(); }
        void OnApplicationFocus(bool focused) { if (!focused) SaveService.Flush(); }
        void OnApplicationQuit() => SaveService.Flush();

        // ---- GameEvents listeners (static so tests can call them directly) ----

        static string RoomId(int index) => "r" + (index + 1);

        public static void OnFlipped(bool toSource) => SaveService.Data.stats.flips++;   // persisted with the next write

        public static void OnKilled(DeathCause cause)
        {
            if (GameState.God) return;
            SaveService.Data.stats.deaths++;
            SaveService.Save();
        }

        public static void OnStart(int room)
        {
            SaveService.Data.stats.runs++;
            SaveService.Save();
        }

        public static void OnCompleted()
        {
            SaveService.Data.stats.wins++;
            SaveService.Save();
        }

        public static void OnRoomStarted(int room) => Analytics.Track("room_start", ("room", RoomId(room)), ("seed", GameState.Seed));

        public static void OnRebooting() => Analytics.Track("restart", ("room", RoomId(GameState.RoomIndex)));

        public static void OnReport(RoomReport r)
        {
            if (r == null) return;
            if (r.room < 0) { Analytics.Track("room_end", ("room", "generated"), ("time", r.time), ("score", r.total), ("grade", r.grade.ToString())); return; }   // generated BREACH rooms have no classic best record
            string id = RoomId(r.room);
            SaveService.RecordRoom(r, id);
            Analytics.Track("room_end", ("room", id), ("time", r.time), ("score", r.total), ("grade", r.grade.ToString()), ("medal", r.medal.ToString()));
        }

        static void OnPaused(bool paused) => SaveService.Flush();   // settings (PlayerPrefs) change while the pause menu is up

        public static void OnCopy(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            WebBridge.CopyText(text);   // async in the browser: PollCopy reports the outcome
            copyPending = true;
#else
            GUIUtility.systemCopyBuffer = text;
            Copied();
#endif
        }

        static void PollCopy()
        {
            int s = WebBridge.CopyStatus();
            if (s < 2) return;
            copyPending = false;
            if (s == 2) Copied();
            else Debug.LogWarning("[DESYNC] the browser refused the clipboard write");
        }

        static void Copied()
        {
            GameEvents.PopText?.Invoke("COPIED", GameConfig.I.green);
            Analytics.Track("share_copied");
        }
    }
}
