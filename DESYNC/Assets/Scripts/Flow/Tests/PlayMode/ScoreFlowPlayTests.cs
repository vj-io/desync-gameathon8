using System;
using System.Collections;
using System.Collections.Generic;
using Desync.Enemies;
using Desync.Net;
using Desync.Player;
using Desync.Rules;
using Desync.TestKit;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EventType = Desync.Rules.EventType;

namespace Desync.Flow.Tests
{
    /// Scoring, recap and results on a tiny generated room run by the real RoomManager + ScoreKeeper.
    public class ScoreFlowPlayTests
    {
        static readonly string[] Keys = { "bestT0", "bestG0", "desync.save", "desync.save.backup" };

        Scene scene;
        GameObject player;
        readonly Dictionary<string, string> prefs = new Dictionary<string, string>();
        readonly List<ScorePop> pops = new List<ScorePop>(256);   // sized up front: the zero-GC test counts every allocation
        readonly List<string> texts = new List<string>(256);
        readonly List<DeathInfo> deaths = new List<DeathInfo>(64);
        readonly List<RoomReport> reports = new List<RoomReport>(64);
        readonly List<string> grazes = new List<string>(64);
        int resets;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            prefs.Clear();
            foreach (var k in Keys) prefs[k] = !PlayerPrefs.HasKey(k) ? null : k == "bestT0" ? "f" + PlayerPrefs.GetFloat(k).ToString("R") : k == "bestG0" ? "i" + PlayerPrefs.GetInt(k) : "s" + PlayerPrefs.GetString(k);
            foreach (var k in Keys) PlayerPrefs.DeleteKey(k);
            SaveService.Unload();
            Time.timeScale = 1f;
            GameState.God = GameState.Paused = false;
            GameState.Mods = Modifiers.Default;
            RoomManager.I.enabled = ScoreKeeper.I.enabled = true;   // an earlier test may have switched the persistent managers off

            scene = SceneManager.CreateScene("DesyncScoreTest");
            SceneManager.SetActiveScene(scene);
            Box(GameLayers.Floor, new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));
            var info = new GameObject("RoomInfo").AddComponent<RoomInfo>();
            info.index = 0;
            info.terminalCount = 1;
            info.spawn = new GameObject("Spawn").transform;
            player = PlayerFactory.Create(null, Vector3.zero, 0f);

            pops.Clear(); texts.Clear(); deaths.Clear(); reports.Clear(); grazes.Clear(); resets = 0;
            RoomReports.ClearRun();
            GameEvents.Scored += OnScored;
            GameEvents.PopText += OnPop;
            GameEvents.DeathRecap += OnDeath;
            GameEvents.RoomReportReady += OnReport;
            GameEvents.RoomReset += OnReset;
            GameEvents.Grazed += OnGrazed;
            RoomManager.I.Activate();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameEvents.Scored -= OnScored;
            GameEvents.PopText -= OnPop;
            GameEvents.DeathRecap -= OnDeath;
            GameEvents.RoomReportReady -= OnReport;
            GameEvents.RoomReset -= OnReset;
            GameEvents.Grazed -= OnGrazed;
            GameState.Mods = Modifiers.Default;
            yield return SceneManager.UnloadSceneAsync(scene);
            RoomManager.I.Activate();
            Time.timeScale = 1f;
            foreach (var kv in prefs)
            {
                PlayerPrefs.DeleteKey(kv.Key);
                if (kv.Value == null) continue;
                string v = kv.Value.Substring(1);
                if (kv.Value[0] == 'f') PlayerPrefs.SetFloat(kv.Key, float.Parse(v));
                else if (kv.Value[0] == 'i') PlayerPrefs.SetInt(kv.Key, int.Parse(v));
                else PlayerPrefs.SetString(kv.Key, v);
            }
            PlayerPrefs.Save();
            SaveService.Unload();   // drop the in-memory profile this test wrote to
        }

        void OnScored(ScorePop p) => pops.Add(p);
        void OnPop(string s, Color c) => texts.Add(s);
        void OnDeath(DeathInfo d) => deaths.Add(d);
        void OnReport(RoomReport r) => reports.Add(r);
        void OnReset() => resets++;
        void OnGrazed(string id) => grazes.Add(id);

        static void Box(int layer, Vector3 pos, Vector3 size)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.layer = layer;
            b.transform.position = pos;
            b.transform.localScale = size;
        }

        bool Flip() => player.GetComponent<FlipController>().TryFlip();

        static IEnumerator RoomSeconds(float s)
        {
            float end = RoomClock.Time + s, giveUp = Time.realtimeSinceStartup + 4f * s + 2f;
            while (RoomClock.Time < end)
            {
                Assert.Less(Time.realtimeSinceStartup, giveUp, "the room clock stopped");
                yield return null;
            }
        }

        static IEnumerator Until(Func<bool> cond, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!cond() && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(cond(), "timed out waiting for " + what);
        }

        static void AssertScoreIsReplayOfLog() =>
            Assert.AreEqual(Scoring.ScoreFromEvents(ScoreKeeper.I.Log, GameState.Mods), GameState.Score, "the log alone reproduces the live score");

        int Paid(EventType t) { int n = 0; foreach (var p in pops) if (p.type == t && p.points > 0) n++; return n; }

        [UnityTest]
        public IEnumerator CleanFlipsBuildTheChain_DetectionVoidsThem_BreaksReset()
        {
            Assert.IsTrue(Flip());
            yield return RoomSeconds(ScoreKeeper.CleanWindow + 0.1f);
            Assert.AreEqual(1, GameState.Chain, "an undisturbed flip is clean after 1 s");
            Assert.AreEqual(1.25f, GameState.Multiplier, 1e-5f);
            Assert.AreEqual(63, GameState.Score, "50 x 1.25 = 62.5, rounded once");
            Assert.AreEqual(1, Paid(EventType.CleanFlip));

            Assert.IsTrue(Flip());
            GameEvents.Spotted?.Invoke(Vector3.zero);   // seen inside the window: not clean
            yield return RoomSeconds(ScoreKeeper.CleanWindow + 0.1f);
            Assert.AreEqual(1, GameState.Chain, "spotted flip pays nothing");

            Assert.IsTrue(Flip());
            yield return RoomSeconds(ScoreKeeper.CleanWindow + 0.1f);
            Assert.AreEqual(2, GameState.Chain);
            GameEvents.HackFailed?.Invoke();               // wrong arrow breaks the chain
            Assert.AreEqual(0, GameState.Chain);
            Assert.AreEqual(1f, GameState.Multiplier);

            Assert.IsTrue(Flip());
            yield return RoomSeconds(ScoreKeeper.CleanWindow + 0.1f);
            Assert.AreEqual(1, GameState.Chain);
            yield return RoomSeconds(GameState.Mods.ChainWindow + 0.1f);   // no clean flip for a chain window
            Assert.AreEqual(0, GameState.Chain, "chain lapses after ChainWindow");
            Assert.AreEqual(1.5f, ScoreKeeper.I.PeakMultiplier, 1e-5f, "peak chain was 2");
            AssertScoreIsReplayOfLog();
        }

        [UnityTest]
        public IEnumerator GrazePaysThreeTimesPerIce()
        {
            for (int i = 0; i < 4; i++) GameEvents.Grazed?.Invoke("ICE-1");
            GameEvents.Grazed?.Invoke("ICE-2");
            Assert.AreEqual(4, Paid(EventType.Graze), "3 for ICE-1, 1 for ICE-2");
            Assert.AreEqual(4 * Scoring.GrazePts, GameState.Score);
            AssertScoreIsReplayOfLog();
            yield break;
        }

        [UnityTest]
        public IEnumerator IceRaisesGrazeForANearPassWithoutContact()
        {
            var vent = EnemyFactory.IceVent(null, new Vector3(30f, 0f, 30f)).GetComponent<IceVent>();   // parked; spawned by hand
            var fpc = player.GetComponent<FirstPersonController>();
            Assert.IsTrue(Flip(), "ICE hunts in SOURCE");
            var ice = vent.pool[0];
            ice.Spawn(new Vector3(1.5f, 0f, 0f));
            Assert.AreEqual("ICE-1", ice.Id);

            float end = RoomClock.Time + 0.3f, giveUp = Time.realtimeSinceStartup + 3f;   // keep the hunter 1.5 m away (0.8 m gap)
            while (RoomClock.Time < end)
            {
                Assert.Less(Time.realtimeSinceStartup, giveUp, "the room clock stopped");
                Vector3 h = ice.transform.position;
                fpc.Teleport(new Vector3(h.x - 1.5f, 0f, h.z));
                yield return null;
            }
            Assert.AreEqual(0, grazes.Count, "the graze lands when the pass ends");
            fpc.Teleport(new Vector3(-10f, 0f, 0f));
            yield return null;
            yield return null;
            Assert.AreEqual(1, grazes.Count);
            Assert.AreEqual("ICE-1", grazes[0]);
            Assert.AreEqual(1, Paid(EventType.Graze));

            yield return Until(() => deaths.Count > 0, 5f, "the hunter to catch the player");
            Assert.AreEqual(1, grazes.Count, "a pass that ends in contact is no graze");
            Assert.AreEqual(DeathCause.Ice, deaths[0].cause);
            Assert.AreEqual("ICE-1", deaths[0].killerId);
        }

        [UnityTest]
        public IEnumerator RedlinePaysAtTheThresholdWithSlowMo()
        {
            Assert.IsTrue(Flip());
            GameState.Melt = 89.9f;
            Assert.IsTrue(Flip());
            Assert.AreEqual(0, Paid(EventType.Redline), "89.9 is not a redline");

            Assert.IsTrue(Flip());
            GameState.Melt = 90f;
            Assert.IsTrue(Flip());
            Assert.AreEqual(1, Paid(EventType.Redline));
            Assert.AreEqual(Scoring.RedlinePts, GameState.Score);
            Assert.Contains("REDLINE", texts);
            Assert.AreEqual(ScoreKeeper.RedlineTimeScale, Time.timeScale, "0.5x time");
            Assert.AreEqual(95f, GameState.Melt, 0.2f, "flip cost after the redline check");
            AssertScoreIsReplayOfLog();
            yield return new WaitForSecondsRealtime(ScoreKeeper.RedlineSlowSeconds + 0.1f);
            Assert.AreEqual(1f, Time.timeScale, "slow-mo ends after 350 ms");

            var m = Modifiers.Default;
            m.RedlineThreshold = 95f;
            GameState.Mods = m;
            Assert.IsTrue(Flip());
            GameState.Melt = 92f;
            Assert.IsTrue(Flip());
            Assert.AreEqual(1, Paid(EventType.Redline), "the threshold comes from Mods");
        }

        [UnityTest]
        public IEnumerator RecapEndsByItselfThenRebootsUnder300ms()
        {
            float t0 = Time.realtimeSinceStartup;
            PlayerRef.Kill(DeathCause.Melt);
            Assert.AreEqual(1, deaths.Count);
            Assert.AreEqual(GameState.PhaseKind.Recap, GameState.Phase);
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsNull(deaths[0].killerId);
            yield return Until(() => resets > 0, 3f, "RoomReset after the recap");
            Assert.That(Time.realtimeSinceStartup - t0, Is.InRange(RoomManager.RecapSeconds, RoomManager.RecapSeconds + 0.6f));
            Assert.Less(GameState.LastRestartMs, 300f);
            Assert.Greater(GameState.LastRestartMs, 0f);
            Assert.AreEqual(GameState.PhaseKind.Playing, GameState.Phase);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsTrue(GameState.Alive);
            Assert.AreEqual(1, GameState.DeathsInRoom);

            PlayerRef.LastKillerId = "DRONE-1";
            PlayerRef.LastKillerPos = new Vector3(1f, 0f, 2f);
            PlayerRef.Kill(DeathCause.Drone);
            Assert.AreEqual("DRONE-1", deaths[1].killerId);
            Assert.AreEqual(new Vector3(1f, 0f, 2f), deaths[1].killerPos);
            Assert.IsNull(PlayerRef.LastKillerId, "consumed by the recap");
            yield return null;
            GameEvents.RestartRequested?.Invoke();   // pause-menu restart skips the recap
            yield return Until(() => resets > 1, 1f, "RoomReset after a restart request");
            Assert.Less(GameState.LastRestartMs, 300f);
            Assert.AreEqual(2, GameState.DeathsInRoom);
        }

        static object _sink;

        /// Zero managed allocations per frame from Flow + Player while scoring and during the recap (other systems paused here, restored after):
        /// every Flow / Player Update + LateUpdate, counted exactly (Desync.TestKit.Allocs: a real frame also carries the runner's own).
        [UnityTest]
        public IEnumerator FlowAndPlayerDoNotAllocatePerFrame()
        {
            var off = new List<MonoBehaviour>();
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            {
                string ns = mb.GetType().Namespace ?? "";
                if (mb.enabled && ns.StartsWith("Desync") && !ns.StartsWith("Desync.Flow") && !ns.StartsWith("Desync.Player")) { mb.enabled = false; off.Add(mb); }
            }
            try
            {
                yield return null;
                var frame = Allocs.Frame("Desync.Flow", "Desync.Player");
                frame.Run();   // the bound calls' first run
                Assert.Greater(Allocs.Count(() => _sink = new byte[64]), 0, "the instrument sees a known allocation");
                // The same play runs twice: the first warms every path it reaches (a method's first run builds its string
                // literals), then the room restarts and the second is counted.
                int extra = 0;
                for (int pass = 0; pass < 2; pass++)
                {
                    Assert.IsTrue(Flip());
                    for (int f = 0; f < 120; f++)   // spans the 1 s clean-flip credit and a REDLINE-free flip back
                    {
                        if (f == 30) Flip();
                        if (pass == 0) frame.Run(); else extra += frame.Count();
                    }
                    Assert.GreaterOrEqual(ScoreKeeper.I.Log.Count, 3, "flips were logged");
                    PlayerRef.Kill(DeathCause.Melt);
                    for (int f = 0; f < 30; f++) { if (pass == 0) frame.Run(); else extra += frame.Count(); }
                    Assert.AreEqual(GameState.PhaseKind.Recap, GameState.Phase);
                    if (pass == 1) break;
                    int r = resets;
                    GameEvents.RestartRequested?.Invoke();
                    yield return Until(() => resets > r, 2f, "RoomReset after a restart request");
                    yield return null;
                }
                Debug.Log("[DESYNC] flow+player: allocations in 150 frames = " + extra);
                Assert.AreEqual(0, extra, "managed allocations in Flow + Player frames: " + frame.Offenders);
            }
            finally { foreach (var mb in off) if (mb) mb.enabled = true; }
        }

        [UnityTest]
        public IEnumerator ExitShowsResults_RetryRestartsInPlace()
        {
            GameEvents.HackFinished?.Invoke(true);
            GameEvents.AccessGranted?.Invoke("MEMO");
            yield return RoomSeconds(0.2f);
            GameEvents.ExitReached?.Invoke();
            Assert.AreEqual(1, reports.Count);
            var r = reports[0];
            RoomReports.RoomTargets(0, 1, out float tDev, out int par);
            Assert.AreEqual(GameState.PhaseKind.Results, GameState.Phase);
            Assert.IsTrue(GameState.Cinematic);
            Assert.AreEqual(Scoring.HackPerfectPts + Scoring.NoTraceRoomPts + Scoring.RoomClearPts, r.eventsScore);
            Assert.AreEqual(r.eventsScore, Scoring.ScoreFromEvents(ScoreKeeper.I.Log, GameState.Mods));
            Assert.AreEqual(Medals.For(r.time, tDev), r.medal);
            Assert.AreEqual(Scoring.TimeBonus(r.time, Medals.Threshold(Medal.Gold, tDev)), r.timeBonus);
            Assert.AreEqual(r.eventsScore + r.timeBonus, r.total);
            Assert.AreEqual(Scoring.Grade(r.total, par), r.grade);
            StringAssert.StartsWith("DESYNC // " + DateTime.UtcNow.ToString("yyyy-MM-dd"), r.shareText);
            StringAssert.Contains("// " + r.grade + " //", r.shareText);

            yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(GameState.PhaseKind.Results, GameState.Phase, "results wait: no auto-advance");
            Assert.AreEqual(scene, SceneManager.GetActiveScene());

            GameEvents.RestartRequested?.Invoke();   // [R] RETRY
            yield return Until(() => resets > 0, 1f, "RoomReset after Retry");
            Assert.Less(GameState.LastRestartMs, 300f);
            Assert.AreEqual(GameState.PhaseKind.Playing, GameState.Phase);
            Assert.IsFalse(GameState.Cinematic);
            Assert.IsFalse(GameState.ExitOpen);
            Assert.AreEqual(0, GameState.Score);
            Assert.AreEqual(0, ScoreKeeper.I.Log.Count);
            Assert.AreEqual(scene, SceneManager.GetActiveScene(), "retry is in place");
        }
    }
}
