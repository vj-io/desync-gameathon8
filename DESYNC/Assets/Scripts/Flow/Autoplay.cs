using System;
using System.Globalization;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using Desync.Gen;
using Desync.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Desync.Flow
{
    /// Browser acceptance hook (E2E-04): open the page with ?autoplay=1&seed=2026&timescale=4&act=1 and the build plays a whole run
    /// on that seed by itself: starts the run from the title, takes the first node on every map, picks the first exploit of every
    /// draft, leaves markets, takes the reroll at cooldowns, and wins each room through the same debug path the unit tests use
    /// (god mode, open the exit, reach it) so the flow it exercises is the real one: run graph, room build, boss arena, results,
    /// drafts, run results. It proves the run can be finished end to end on a fixed seed; it does not measure difficulty (that is
    /// the bot-sim). act=1 plays QUICK (act 1 and its AUDITOR); any other value plays the CAMPAIGN. timescale speeds the game up
    /// (1..8). Progress is published to window.__DESYNC__.autoplay { state: idle|running|done|failed, phase, rooms, won, act }.
    /// Without the query string nothing is created.
    public sealed class Autoplay : MonoBehaviour
    {
        public enum State { Idle, Running, Done, Failed }

        public struct Args { public bool on; public uint seed; public float timescale; public int act; }

        public const uint DefaultSeed = 2026;
        const float Settle = 0.5f, ActEvery = 0.1f, StuckSeconds = 90f, PublishEvery = 0.25f;

        public static State Status { get; private set; }
        public static string Failure { get; private set; }
        public static int RoomsCleared => RunState.Grades.Count;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void Desync_AutoplayStatus(int state, int phase, int rooms, int won, int act);
#endif

        Args args;
        bool started, exitSent;
        string stuckKey, roomKey;
        float stuckSince, roomReady = -1f, nextAct, nextPublish;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var a = Parse(Application.absoluteURL);
            if (!a.on || FindAnyObjectByType<Autoplay>()) return;
            var go = new GameObject("DesyncAutoplay");
            DontDestroyOnLoad(go);
            go.AddComponent<Autoplay>().args = a;
        }

        /// The autoplay settings in a page URL. `on` only for autoplay=1 / true. Unknown or malformed values fall back to the defaults.
        public static Args Parse(string url)
        {
            var a = new Args { seed = DefaultSeed, timescale = 1f, act = 1 };
            int q = url == null ? -1 : url.IndexOf('?');
            if (q < 0) return a;
            string query = url.Substring(q + 1);
            int hash = query.IndexOf('#');
            if (hash >= 0) query = query.Substring(0, hash);
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                string k = (eq < 0 ? part : part.Substring(0, eq)).ToLowerInvariant(), v = eq < 0 ? "" : Uri.UnescapeDataString(part.Substring(eq + 1));
                switch (k)
                {
                    case "autoplay": a.on = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                    case "seed": if (uint.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var s)) a.seed = s; break;
                    case "timescale": if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && !float.IsNaN(t)) a.timescale = Mathf.Clamp(t, 1f, 8f); break;
                    case "act": if (int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1) a.act = n; break;
                }
            }
            return a;
        }

        /// QUICK for act 1, else the CAMPAIGN.
        public static RunMode ModeFor(Args a) => a.act <= 1 ? RunMode.Quick : RunMode.Campaign;

        void Update()
        {
            if (Status == State.Done || Status == State.Failed) return;
            if (!started)
            {
                // the title scene, with the managers up and a moment to settle
                if (SceneManager.GetActiveScene().name != SceneFlow.Title || !RunManager.I || !GameManager.I || Time.unscaledTime < 1.5f) return;
                Begin(args);
            }
            if (Time.timeScale == 1f && !GameState.Paused && args.timescale > 1f) Time.timeScale = args.timescale;
            Publish(false);
            if (Time.unscaledTime < nextAct) return;
            nextAct = Time.unscaledTime + ActEvery;
            Step();
        }

        /// Starts the run (also called by tests): god mode on, story prompt answered, run begun on args.seed.
        public void Begin(Args a)
        {
            args = a;
            started = true;
            Status = State.Running; Failure = null;
            stuckKey = null; roomKey = null; roomReady = -1f; exitSent = false;
            GameState.God = true;
            SaveService.Data.settings["storySeen"] = "1";   // the title's first-time story prompt (UI/StoryScreen.cs) must not sit over the run
            Debug.Log("[DESYNC] autoplay: " + ModeFor(a) + " seed=" + a.seed + " timescale=" + a.timescale);
            RunManager.I.Begin(ModeFor(a), a.seed);
        }

        /// One decision for the current phase (public for tests).
        public void Step()
        {
            if (Watchdog()) return;
            switch (RunState.Phase)
            {
                case RunPhase.Map: ChooseNode(); break;
                case RunPhase.Room: PlayRoom(); break;
                case RunPhase.Results: roomReady = -1f; exitSent = false; GameEvents.ContinueRequested?.Invoke(); break;
                case RunPhase.Draft: RunEvents.PickExploit?.Invoke(RunState.Offer.Count > 0 ? 0 : -1); break;
                case RunPhase.Market: RunEvents.Leave?.Invoke(); break;
                case RunPhase.Cooldown: RunEvents.ChooseCooldown?.Invoke(2); break;   // the reroll token: always on offer
                case RunPhase.Over: Finish(State.Done); break;
                case RunPhase.None: Fail("the run ended without results"); break;
            }
        }

        void ChooseNode()
        {
            var nodes = RunState.Graph.acts[RunState.Act - 1];
            var cur = RunState.NodeId == null ? null : nodes.Find(n => n.id == RunState.NodeId);
            var pick = cur == null ? nodes.Find(n => n.index == 0) : cur.next != null && cur.next.Count > 0 ? RunManager.Choice(cur.next[0]) : null;
            if (pick == null) { Fail("no node to choose after " + RunState.NodeId); return; }
            RunEvents.ChooseNode?.Invoke(pick.id);
        }

        void PlayRoom()
        {
            bool live = RunManager.I.RoomLive && RoomInfo.Current && RoomManager.I && GameState.Phase == GameState.PhaseKind.Playing && GameState.Alive;
            if (!live) { roomReady = -1f; return; }
            if (roomReady < 0f) roomReady = Time.unscaledTime;
            if (exitSent || Time.unscaledTime - roomReady < Settle) return;
            exitSent = true;
            RoomManager.I.OpenExit();
            GameEvents.ExitReached?.Invoke();
        }

        /// No progress for StuckSeconds: fail with where it stuck (the page test reads this instead of timing out blind).
        bool Watchdog()
        {
            string key = RunState.Phase + ":" + RunState.NodeId + ":" + RunState.Act + ":" + RunState.Grades.Count;
            if (key != stuckKey) { stuckKey = key; stuckSince = Time.unscaledTime; Debug.Log("[DESYNC] autoplay: " + key); return false; }
            if (Time.unscaledTime - stuckSince < StuckSeconds) return false;
            Fail("stuck in " + key);
            return true;
        }

        void Fail(string why) { Failure = why; Debug.LogError("[DESYNC] autoplay failed: " + why); Finish(State.Failed); }

        void Finish(State s)
        {
            Status = s;
            Time.timeScale = GameState.Paused ? 0f : 1f;
            Debug.Log("[DESYNC] autoplay " + s + ": rooms=" + RoomsCleared + " won=" + RunState.Won);
            Publish(true);
        }

        void Publish(bool force)
        {
            if (!force && Time.unscaledTime < nextPublish) return;
            nextPublish = Time.unscaledTime + PublishEvery;
#if UNITY_WEBGL && !UNITY_EDITOR
            Desync_AutoplayStatus((int)Status, (int)RunState.Phase, RoomsCleared, RunState.Won ? 1 : 0, RunState.Act);
#endif
        }

        /// Forgets a previous run's outcome (tests).
        public static void ResetStatus() { Status = State.Idle; Failure = null; }
    }
}
