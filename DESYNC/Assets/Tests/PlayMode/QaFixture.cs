using System;
using System.Collections;
using System.Collections.Generic;
using Desync.Flow;
using Desync.Player;
using Desync.Rules;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Desync.QA
{
    /// Shared harness for the acceptance tests: a tiny room built at runtime from the real factories (floor, one wall,
    /// RoomInfo, first-person player) and run by the real RoomManager, plus a virtual keyboard and event capture.
    /// Every test starts from the same state and leaves nothing behind (scenes, devices, time scale, local bests).
    public abstract class QaFixture
    {
        protected Scene scene;
        protected GameObject world, player;
        protected RoomInfo info;
        protected Keyboard keyboard;

        protected readonly List<DeathInfo> deaths = new List<DeathInfo>();
        protected readonly List<DeathCause> kills = new List<DeathCause>();   // GameEvents.PlayerKilled, to tell "no death" from "no recap"
        protected int alarms;
        protected readonly List<ScorePop> pops = new List<ScorePop>();
        protected readonly List<string> hints = new List<string>();
        protected readonly List<RoomReport> reports = new List<RoomReport>();
        protected readonly List<RoomResult> cleared = new List<RoomResult>();
        protected int resets;

        InputSettings.BackgroundBehavior oldBackground;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode oldEditorInput;
#endif
        readonly Dictionary<string, object> savedPrefs = new Dictionary<string, object>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            KeepPrefs();
            oldBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;   // batchmode has no focus
#if UNITY_EDITOR
            oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;   // keyboards stay disabled without this
#endif
            keyboard = InputSystem.AddDevice<Keyboard>();   // a fresh virtual one: Keyboard.current is the real, focus-disabled device

            Time.timeScale = 1f;
            GameState.God = GameState.Paused = false;
            GameState.Mods = Modifiers.Default;
            PlayerRef.LastKillerId = null;
            PlayerRef.LastKillerPos = Vector3.zero;

            scene = SceneManager.CreateScene("QaRoom");
            SceneManager.SetActiveScene(scene);
            world = new GameObject("QaWorld");
            Box(GameLayers.Floor, new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));
            Box(GameLayers.WallRender, new Vector3(0f, 1f, 3f), new Vector3(4f, 2f, 1f));
            info = new GameObject("RoomInfo").AddComponent<RoomInfo>();
            info.index = 0;
            info.terminalCount = 1;
            info.spawn = new GameObject("Spawn").transform;
            player = PlayerFactory.Create(null, Vector3.zero, 0f);
            Physics.SyncTransforms();

            GameEvents.DeathRecap += OnDeath;
            GameEvents.PlayerKilled += OnKilled;
            GameEvents.Alarm += OnAlarm;
            GameEvents.Scored += OnScored;
            GameEvents.Hint += OnHint;
            GameEvents.RoomReportReady += OnReport;
            GameEvents.RoomCleared += OnCleared;
            GameEvents.RoomReset += OnReset;
            deaths.Clear(); kills.Clear(); alarms = 0; pops.Clear(); hints.Clear(); reports.Clear(); cleared.Clear(); resets = 0;

            RoomManager.I.Activate();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameEvents.DeathRecap -= OnDeath;
            GameEvents.PlayerKilled -= OnKilled;
            GameEvents.Alarm -= OnAlarm;
            GameEvents.Scored -= OnScored;
            GameEvents.Hint -= OnHint;
            GameEvents.RoomReportReady -= OnReport;
            GameEvents.RoomCleared -= OnCleared;
            GameEvents.RoomReset -= OnReset;

            Time.timeScale = 1f;
            GameState.God = GameState.Paused = false;
            GameState.Mods = Modifiers.Default;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior = oldBackground;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput;
#endif

            // A Continue test leaves Room_02 loaded as the only scene: park first, then unload ours and any real room.
            var park = SceneManager.GetSceneByName("QaPark");
            if (!park.IsValid()) park = SceneManager.CreateScene("QaPark");
            SceneManager.SetActiveScene(park);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s == scene || s.name.StartsWith("Room_") || s.name == "Title" || s.name == "Ending")
                    yield return SceneManager.UnloadSceneAsync(s);
            }
            RoomManager.I.Activate();   // room gone: back to the non-room state, cancels pending scene loads
            LayerState.Set(false);
            GameState.Alive = true;
            GameState.Cinematic = false;
            Time.timeScale = 1f;
            RestorePrefs();
        }

        void OnDeath(DeathInfo d) => deaths.Add(d);
        void OnKilled(DeathCause c) => kills.Add(c);
        void OnAlarm() => alarms++;
        void OnScored(ScorePop p) => pops.Add(p);
        void OnHint(string h) => hints.Add(h);
        void OnReport(RoomReport r) => reports.Add(r);
        void OnCleared(RoomResult r) => cleared.Add(r);
        void OnReset() => resets++;

        static void Box(int layer, Vector3 pos, Vector3 size)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.layer = layer;
            b.transform.position = pos;
            b.transform.localScale = size;
        }

        // ---------- helpers ----------

        protected bool Flip() => player.GetComponent<FlipController>().TryFlip();

        /// Count of captured Scored events of one type that actually paid.
        protected int Paid(Desync.Rules.EventType type)
        {
            int n = 0;
            foreach (var p in pops) if (p.type == type && p.points > 0) n++;
            return n;
        }

        /// Waits (real time, so it also works at timeScale 0) until `cond` holds; fails the test with `what` on timeout.
        protected static IEnumerator Until(Func<bool> cond, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!cond() && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(cond(), "timed out after " + seconds + " s waiting for: " + what);
        }

        /// Waits for a DeathRecap beyond the first `already`; the failure says whether the death itself happened.
        protected IEnumerator AwaitRecap(int already, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (deaths.Count <= already && Time.realtimeSinceStartup < end) yield return null;
            Assert.Greater(deaths.Count, already, $"no GameEvents.DeathRecap for {what} within {seconds} s (PlayerKilled events seen: {kills.Count}, Alarm events seen: {alarms})");
        }

        protected static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        /// Press and release one key on the virtual keyboard (two frames each way so every poller sees it).
        protected IEnumerator Tap(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null; yield return null;
        }

        /// Kill the player and wait for the recap card event.
        protected IEnumerator KillAndWaitRecap(DeathCause cause)
        {
            int n = deaths.Count;
            PlayerRef.Kill(cause);
            yield return AwaitRecap(n, 1f, "PlayerRef.Kill(" + cause + ")");
        }

        /// Any key ends the recap; waits for the restart to finish (RoomReset).
        protected IEnumerator SkipRecap(Key key = Key.Enter)
        {
            int n = resets;
            yield return Tap(key);
            yield return Until(() => resets > n, 2f, "RoomReset after skipping the recap with " + key);
        }

        // ---------- local save isolation ----------
        // Grading a room writes the developer's own bests (PlayerPrefs): snapshot and restore them around each test.

        void KeepPrefs()
        {
            savedPrefs.Clear();
            for (int i = 0; i < SceneFlow.RoomCount; i++) { Keep("bestT" + i, 'f'); Keep("bestG" + i, 'i'); }
            Keep("desync.save", 's');
            Keep("desync.save.backup", 's');
        }

        void Keep(string key, char type)
        {
            savedPrefs[key] = !PlayerPrefs.HasKey(key) ? null
                : type == 'f' ? (object)PlayerPrefs.GetFloat(key)
                : type == 'i' ? PlayerPrefs.GetInt(key)
                : PlayerPrefs.GetString(key);
        }

        void RestorePrefs()
        {
            foreach (var kv in savedPrefs)
            {
                PlayerPrefs.DeleteKey(kv.Key);
                if (kv.Value is float f) PlayerPrefs.SetFloat(kv.Key, f);
                else if (kv.Value is int n) PlayerPrefs.SetInt(kv.Key, n);
                else if (kv.Value is string s) PlayerPrefs.SetString(kv.Key, s);
            }
            PlayerPrefs.Save();
        }
    }
}
