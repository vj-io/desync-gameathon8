using System.Collections;
using System.Text.RegularExpressions;
using Desync.Enemies;
using Desync.Flow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Desync.QA
{
    /// Death recap (spec "Core rules engine": freeze 1.2 s, card shows the real cause, any key skips, hint after 3 deaths).
    /// DeathInfo shape asserted here:
    ///   ICE    cause Ice,   killerId "ICE-<n>", killerPos at the hunter
    ///   MELT   cause Melt,  killerId null
    ///   TRACE  cause Alarm, killerId null            (third TRACE raises GameEvents.Alarm)
    ///   GLITCH cause Glitch, killerId "GLITCH", killerPos at the zone  (a forced flip that melts the player; MeltMeter.GlitchKiller)
    public class DeathRecapTests : QaFixture
    {
        static void AssertKiller(DeathInfo d, string pattern, string label)
        {
            if (pattern == null) Assert.IsNull(d.killerId, label + ": no killer id expected");
            else
            {
                Assert.IsNotNull(d.killerId, label + ": killerId must be set");
                StringAssert.IsMatch(pattern, d.killerId, label + ": killerId shape");
            }
        }

        [UnityTest]
        public IEnumerator Ice_CauseKillerIdAndPosition()
        {
            var vent = EnemyFactory.IceVent(world.transform, new Vector3(30f, 0f, 30f)).GetComponent<IceVent>();   // parked far away; we spawn the hunter by hand
            Assert.IsTrue(Flip(), "into SOURCE");
            vent.pool[0].Spawn(new Vector3(3f, 0f, 0f));
            yield return AwaitRecap(0, 5f, "the ICE hunter reaching the player");
            var d = deaths[0];
            Assert.AreEqual(DeathCause.Ice, d.cause);
            AssertKiller(d, "^ICE-[0-9]+$", "ICE");
            Assert.Less(Vector3.Distance(d.killerPos, PlayerRef.Position), 2.5f, "killerPos is where the hunter caught the player");
            Assert.Greater(d.roomTime, 0.2f, "roomTime = room clock at death");
            Assert.AreEqual(GameState.PhaseKind.Recap, GameState.Phase);
        }

        [UnityTest]
        public IEnumerator Melt_CauseAndNoKiller()
        {
            Assert.IsTrue(Flip(), "into SOURCE");
            GameState.Melt = 99f;
            yield return AwaitRecap(0, 4f, "a melt death");
            var d = deaths[0];
            Assert.AreEqual(DeathCause.Melt, d.cause);
            AssertKiller(d, null, "MELT");
            Assert.AreEqual(GameState.PhaseKind.Recap, GameState.Phase);
        }

        [UnityTest]
        public IEnumerator Trace_ThirdTraceIsALockdown()
        {
            int alarms = 0;
            System.Action onAlarm = () => alarms++;
            GameEvents.Alarm += onAlarm;
            try
            {
                for (int i = 0; i < GameConfig.I.tracesToAlarm; i++)
                {
                    Assert.AreEqual(0, alarms, "no alarm before the last TRACE");
                    GameState.TraceActive = false;   // the previous TRACE ran out
                    TraceSystem.Sustain();
                }
            }
            finally { GameEvents.Alarm -= onAlarm; }
            Assert.AreEqual(1, alarms);
            yield return AwaitRecap(0, 2f, "the TRACE lockdown");
            var d = deaths[0];
            Assert.AreEqual(DeathCause.Alarm, d.cause);
            AssertKiller(d, null, "TRACE");
            Assert.AreEqual(GameConfig.I.tracesToAlarm, GameState.TraceCount);
        }

        [UnityTest]
        public IEnumerator Glitch_ForcedFlipIntoMeltNamesTheZone()
        {
            var zonePos = new Vector3(1f, 0f, 0f);
            EnemyFactory.GlitchZone(world.transform, zonePos, 2f);   // the player (at the origin) stands inside
            float end = Time.realtimeSinceStartup + GameConfig.I.glitchZoneTime + 4f;
            while (deaths.Count == 0 && Time.realtimeSinceStartup < end)
            {
                if (GameState.Alive && !LayerState.InSource) GameState.Melt = 96f;   // so the forced +flip cost reaches 100
                yield return null;
            }
            Assert.AreEqual(1, deaths.Count, $"the glitch zone's forced flip must kill at melt 96 and raise a recap (PlayerKilled seen: {kills.Count}, GlitchZone left: {GameState.GlitchCountdown})");
            var d = deaths[0];
            Assert.AreEqual(DeathCause.Glitch, d.cause);
            AssertKiller(d, "^GLITCH$", "GLITCH");
            Assert.Less(Vector3.Distance(d.killerPos, zonePos), 0.5f, "killerPos marks the zone");
        }

        [UnityTest]
        public IEnumerator Recap_FreezesThenReboots_WithoutAKey()
        {
            float t0 = Time.realtimeSinceStartup;
            yield return KillAndWaitRecap(DeathCause.Melt);
            yield return null;
            Assert.AreEqual(0f, Time.timeScale, "recap freezes time");
            Assert.AreEqual(GameState.PhaseKind.Recap, GameState.Phase);
            Assert.IsFalse(GameState.Alive);
            yield return Until(() => resets > 0, 3f, "the recap to end by itself");
            float took = Time.realtimeSinceStartup - t0;
            Assert.That(took, Is.InRange(1.0f, 2.2f), "freeze is 1.2 s, then the 0.2 s wipe");
            Assert.AreEqual(1f, Time.timeScale);
            Assert.AreEqual(GameState.PhaseKind.Playing, GameState.Phase);
            Assert.IsTrue(GameState.Alive);
        }

        [UnityTest]
        public IEnumerator NearMiss_NeverFabricatesAnExit()
        {
            yield return KillAndWaitRecap(DeathCause.Melt);   // exit still locked
            var n = deaths[0].nearMiss;
            Assert.IsTrue(n == null || (n.Length > 0 && !n.ToLowerInvariant().Contains("exit")), "locked exit must not appear as a near miss: '" + n + "'");
        }

        [UnityTest]
        public IEnumerator NearMiss_ReportsTheRealExitDistance()
        {
            EnemyFactory.ExitPortal(world.transform, new Vector3(0f, 0f, -2.3f), 0f);
            GameEvents.HackFinished?.Invoke(true);
            GameEvents.AccessGranted?.Invoke("MEMO");
            Assert.IsTrue(GameState.ExitOpen);
            PlayerRef.LastKillerId = "ICE-9";
            PlayerRef.LastKillerPos = new Vector3(2f, 0f, 0f);
            yield return KillAndWaitRecap(DeathCause.Ice);
            var n = deaths[0].nearMiss;
            Assert.IsNotNull(n, "an open exit 2.3 m away is a true near miss");
            StringAssert.Contains("exit", n.ToLowerInvariant());
            StringAssert.IsMatch(@"2\.[2-4]", n, "distance as measured");
        }

        [UnityTest]
        public IEnumerator Hint_AppearsOnTheThirdDeathInOneRoom()
        {
            bool hasHints = Resources.Load<TextAsset>("Content/hints") != null;
            for (int i = 1; i <= 3; i++)
            {
                yield return KillAndWaitRecap(DeathCause.Melt);
                yield return SkipRecap();
                Assert.AreEqual(i, GameState.DeathsInRoom, "deaths counted per room");
                if (hasHints) Assert.AreEqual(i < 3 ? 0 : 1, hints.Count, "one hint, and only from the third death");
            }
            if (!hasHints) Assert.Inconclusive("Resources/Content/hints.json not present yet: hint text cannot be checked");
            Assert.IsFalse(string.IsNullOrWhiteSpace(hints[0]));
        }

        // ---------- the page hook's kill command (E2E-03 depends on it) ----------

        IEnumerator BridgeKill(string what, string pattern, params DeathCause[] causes)
        {
            var bridge = Object.FindAnyObjectByType<DebugBridge>();
            Assert.IsNotNull(bridge, "the DesyncDebug bridge exists in every build");
            bridge.Cmd("kill " + what);
            yield return AwaitRecap(0, 4f, "'kill " + what + "'");
            Assert.GreaterOrEqual(System.Array.IndexOf(causes, deaths[0].cause), 0, "kill " + what + ": cause was " + deaths[0].cause);
            AssertKiller(deaths[0], pattern, "kill " + what);
        }

        [UnityTest] public IEnumerator Bridge_KillIce() { yield return BridgeKill("ice", "^ICE-[0-9]+$", DeathCause.Ice); }
        [UnityTest] public IEnumerator Bridge_KillMelt() { yield return BridgeKill("melt", null, DeathCause.Melt); }
        [UnityTest] public IEnumerator Bridge_KillTrace() { yield return BridgeKill("trace", null, DeathCause.Alarm); }
        [UnityTest] public IEnumerator Bridge_KillGlitch() { yield return BridgeKill("glitch", "^GLITCH$", DeathCause.Glitch); }
    }
}
