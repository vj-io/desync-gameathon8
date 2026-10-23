using System;
using System.Collections;
using Desync.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Desync.Flow.Tests
{
    /// Runtime smoke test of the room loop on a tiny generated room: flip, rejection, REBOOT, melt death, exit + grade.
    public class RoomFlowPlayTests
    {
        Scene scene;
        GameObject player;
        bool hadBest; float oldBestT; int oldBestG;   // grading a room writes the real PlayerPrefs best: keep the developer's own

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            hadBest = PlayerPrefs.HasKey("bestG0"); oldBestT = PlayerPrefs.GetFloat("bestT0"); oldBestG = PlayerPrefs.GetInt("bestG0");
            PlayerPrefs.DeleteKey("bestT0"); PlayerPrefs.DeleteKey("bestG0");
            scene = SceneManager.CreateScene("DesyncFlowTest");
            SceneManager.SetActiveScene(scene);
            Box(GameLayers.Floor, new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f));
            Box(GameLayers.WallRender, new Vector3(0f, 1f, 3f), new Vector3(4f, 2f, 1f));
            var info = new GameObject("RoomInfo").AddComponent<RoomInfo>();
            info.index = 0;
            info.terminalCount = 1;
            info.spawn = new GameObject("Spawn").transform;
            player = PlayerFactory.Create(null, Vector3.zero, 0f);
            RoomManager.I.Activate();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameState.God = false;
            PlayerPrefs.DeleteKey("bestT0"); PlayerPrefs.DeleteKey("bestG0");
            if (hadBest) { PlayerPrefs.SetFloat("bestT0", oldBestT); PlayerPrefs.SetInt("bestG0", oldBestG); }
            PlayerPrefs.Save();   // Record saved to disk: the cleanup must too
            yield return SceneManager.UnloadSceneAsync(scene);
            RoomManager.I.Activate();   // room gone: back to the non-room state, cancels pending scene loads
            Time.timeScale = 1f;
        }

        static void Box(int layer, Vector3 pos, Vector3 size)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.layer = layer;
            b.transform.position = pos;
            b.transform.localScale = size;
        }

        [UnityTest]
        public IEnumerator FlipRejectAndRebootRestoreIdenticalState()
        {
            int rebooting = 0, reset = 0;
            Action onReboot = () => rebooting++, onReset = () => reset++;
            GameEvents.Rebooting += onReboot;
            GameEvents.RoomReset += onReset;
            try
            {
                Assert.AreEqual(0, GameState.RoomIndex);
                var flip = player.GetComponent<FlipController>();
                Assert.IsTrue(flip.TryFlip());
                Assert.IsTrue(LayerState.InSource);
                Assert.AreEqual(1, GameState.Flips);
                Assert.AreEqual(GameConfig.I.meltFlipSpike, GameState.Melt, 0.5f);

                player.GetComponent<FirstPersonController>().Teleport(new Vector3(0f, 0f, 3f));   // inside the wall
                yield return null;
                Assert.IsFalse(flip.TryFlip(), "return flip inside a wall must be rejected");
                Assert.IsTrue(LayerState.InSource);

                PlayerRef.Kill(DeathCause.Ice);
                Assert.AreEqual(1, rebooting);
                Assert.IsFalse(GameState.Alive);
                yield return new WaitForSecondsRealtime(GameConfig.I.restartWipe + 0.15f);
                Assert.AreEqual(1, reset);
                Assert.IsTrue(GameState.Alive);
                Assert.IsFalse(LayerState.InSource);
                Assert.AreEqual(0, GameState.Flips);
                Assert.Less(GameState.Melt, 0.01f);
                Assert.Less(Vector3.Distance(player.transform.position, Vector3.zero), 0.1f, "player back at spawn");

                GameState.God = true;
                PlayerRef.Kill(DeathCause.Ice);
                Assert.IsTrue(GameState.Alive, "god mode ignores kills");
                Assert.AreEqual(1, rebooting);
            }
            finally
            {
                GameEvents.Rebooting -= onReboot;
                GameEvents.RoomReset -= onReset;
            }
        }

        [UnityTest]
        public IEnumerator GamepadStartPausesAndStartOrSouthResumes_WithoutAFlip()
        {
            var bg = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;   // batchmode has no focus
            var pad = InputSystem.AddDevice<Gamepad>();
            IEnumerator Tap(GamepadButton b)
            {
                InputSystem.QueueStateEvent(pad, new GamepadState(b));
                yield return null; yield return null;
                InputSystem.QueueStateEvent(pad, new GamepadState());
                yield return null; yield return null;
            }
            try
            {
                Assert.IsFalse(GameState.Paused);
                yield return Tap(GamepadButton.Start);
                Assert.IsTrue(GameState.Paused, "pad Start pauses");
                yield return Tap(GamepadButton.Start);
                Assert.IsFalse(GameState.Paused, "pad Start resumes (the only resume on WebGL, where the browser eats Esc)");
                yield return Tap(GamepadButton.Start);
                Assert.IsTrue(GameState.Paused);
                yield return Tap(GamepadButton.South);
                Assert.IsFalse(GameState.Paused, "pad South resumes from the pause menu");
                Assert.AreEqual(0, GameState.Flips, "the resuming South press must not also flip");
                Assert.IsFalse(LayerState.InSource);
                yield return Tap(GamepadButton.South);
                Assert.AreEqual(1, GameState.Flips, "South flips again once running");
            }
            finally
            {
                InputSystem.settings.backgroundBehavior = bg;
                InputSystem.RemoveDevice(pad);
                GameState.Paused = false; Time.timeScale = 1f;
            }
        }

        [UnityTest]
        public IEnumerator MeltAt100KillsAfterDrip()
        {
            DeathCause? cause = null;
            Action<DeathCause> onKill = c => cause = c;
            GameEvents.PlayerKilled += onKill;
            try
            {
                player.GetComponent<FlipController>().TryFlip();
                GameState.Melt = 100f;
                yield return new WaitForSeconds(GameConfig.I.meltDeathDrip * 0.5f);
                Assert.IsNull(cause, "drip hold before death");
                yield return new WaitForSeconds(GameConfig.I.meltDeathDrip);
                Assert.AreEqual(DeathCause.Melt, cause);
            }
            finally { GameEvents.PlayerKilled -= onKill; }
        }

        [UnityTest]
        public IEnumerator TerminalOpensExitAndExitGrades()
        {
            bool opened = false;
            RoomResult? cleared = null;
            Action onOpen = () => opened = true;
            Action<RoomResult> onClear = r => cleared = r;
            GameEvents.ExitOpened += onOpen;
            GameEvents.RoomCleared += onClear;
            try
            {
                GameEvents.AccessGranted?.Invoke("MEMO");
                Assert.IsTrue(opened);
                Assert.IsTrue(GameState.ExitOpen);
                Assert.AreEqual(1, GameState.Combo);
                yield return null;
                GameEvents.ExitReached?.Invoke();
                Assert.IsTrue(cleared.HasValue);
                Assert.AreEqual('S', cleared.Value.grade);
                Assert.AreEqual('S', GameState.Results[0].grade);
                Assert.AreEqual((int)'S', PlayerPrefs.GetInt("bestG0"), "a clear is saved as the local best");
                Assert.IsTrue(GameState.Cinematic, "player frozen on the grade card");
            }
            finally
            {
                GameEvents.ExitOpened -= onOpen;
                GameEvents.RoomCleared -= onClear;
            }
        }
    }
}
