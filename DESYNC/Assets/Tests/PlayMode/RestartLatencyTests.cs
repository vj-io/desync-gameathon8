using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Desync.QA
{
    /// Spec non-negotiable 8: restart (R or after the recap) returns control in < 300 ms, no scene reload, reset in place.
    public class RestartLatencyTests : QaFixture
    {
        [UnityTest]
        public IEnumerator Restart_P95_UnderThreeHundredMs_Over20Kills()
        {
            const int N = 20;
            var ms = new List<float>(N);
            for (int i = 0; i < N; i++)
            {
                GameState.LastRestartMs = -1f;
                yield return KillAndWaitRecap(DeathCause.Melt);
                Assert.AreEqual(GameState.PhaseKind.Recap, GameState.Phase, $"kill {i}: the recap card is up");
                yield return SkipRecap(i % 2 == 0 ? Key.Enter : Key.R);   // any key skips; R is the key players actually press
                Assert.AreEqual(GameState.PhaseKind.Playing, GameState.Phase, $"kill {i}: back in the room");
                Assert.IsTrue(GameState.Alive, $"kill {i}: alive again");
                Assert.AreEqual(1f, Time.timeScale, $"kill {i}: time runs again");
                Assert.AreEqual(scene, UnityEngine.SceneManagement.SceneManager.GetActiveScene(), $"kill {i}: reset in place, no scene reload");
                Assert.Greater(GameState.LastRestartMs, 0f, $"kill {i}: RoomManager must write GameState.LastRestartMs");
                ms.Add(GameState.LastRestartMs);
            }
            var sorted = new List<float>(ms);
            sorted.Sort();
            float p95 = sorted[Mathf.CeilToInt(0.95f * N) - 1];   // nearest rank
            Debug.Log($"[DESYNC] QA restart latency over {N} kills: p50={sorted[N / 2]:0.0} ms, p95={p95:0.0} ms, max={sorted[N - 1]:0.0} ms");
            Assert.Less(p95, 300f, "restart latency p95 (ms)");
        }
    }
}
