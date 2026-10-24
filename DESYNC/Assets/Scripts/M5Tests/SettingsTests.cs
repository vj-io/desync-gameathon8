using System.Collections;
using System.Linq;
using Desync.Flow;
using Desync.Net;
using Desync.Rules;
using Desync.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Desync.M5.Tests
{
    /// The settings store (A11y), Assist mode and key remapping.
    public class SettingsTests
    {
        const string SaveKey = "desync.save.m5set";

        [SetUp]
        public void SetUp() { Isolation.Begin(SaveKey); Rebinds.ResetAll(); AssistHost.Reset(); GameState.Mods = Modifiers.Default; }

        [TearDown]
        public void TearDown()
        {
            Rebinds.ResetAll();
            Rebinds.Cancel();
            Assist.On = false; AssistHost.Reset();
            GameState.Mods = Modifiers.Default;
            GameState.Phase = GameState.PhaseKind.Title;
            Isolation.End(SaveKey);
        }

        // ------------------------------------------------------------------ store
        [Test]
        public void Store_PersistsUnderItsPrefixes_Clamps_AndResets()
        {
            A11y.UiScale = 1.2f; A11y.SubtitleScale = 1.4f; A11y.MusicVolume = 0.4f; A11y.SfxVolume = 0.6f; A11y.UiVolume = 0.2f;
            A11y.HighContrast = true; A11y.TabToggle = true; Assist.On = true;
            var s = SaveService.Data.settings;
            Assert.AreEqual("1.20", s["a11y_ui_scale"]); Assert.AreEqual("0.40", s["a11y_vol_music"]); Assert.AreEqual("1", s["a11y_hicon"]); Assert.AreEqual("1", s["assist_on"]);
            foreach (var k in s.Keys)
                if (k != "legacyBestImported") StringAssert.IsMatch("^(a11y_|assist_)", k, "the keys this workstream writes carry its prefixes: " + k);

            SaveService.Flush();
            SaveService.Unload(SaveKey); Isolation.Reopen();   // a fresh session reads the same profile
            Assert.AreEqual(1.2f, A11y.UiScale, 1e-4f); Assert.AreEqual(0.4f, A11y.MusicVolume, 1e-4f);
            Assert.IsTrue(A11y.HighContrast); Assert.IsTrue(A11y.TabToggle); Assert.IsTrue(Assist.On);

            A11y.UiScale = 5f; Assert.AreEqual(A11y.UiScaleMax, A11y.UiScale);
            A11y.UiScale = 0.1f; Assert.AreEqual(A11y.UiScaleMin, A11y.UiScale);
            A11y.SubtitleScale = 0.2f; Assert.AreEqual(A11y.SubtitleScaleMin, A11y.SubtitleScale);
            A11y.MusicVolume = 7f; Assert.AreEqual(1f, A11y.MusicVolume);

            A11y.ResetDefaults();
            Assert.AreEqual(1f, A11y.UiScale); Assert.AreEqual(1f, A11y.MusicVolume); Assert.IsFalse(A11y.HighContrast); Assert.IsFalse(Assist.On);
            Assert.IsFalse(SaveService.Data.settings.ContainsKey("a11y_hicon"));
        }

        [Test]
        public void Store_IgnoresGarbageInTheSave()
        {
            SaveService.Data.settings["a11y_ui_scale"] = "not a number";
            SaveService.Data.settings["a11y_vol_sfx"] = "NaN";
            SaveService.Save(); SaveService.Flush();
            SaveService.Unload(SaveKey); Isolation.Reopen();   // a fresh session reads them back
            Assert.AreEqual(1f, A11y.UiScale); Assert.AreEqual(1f, A11y.SfxVolume);
        }

        // ------------------------------------------------------------------ assist
        [Test]
        public void Assist_Apply_EasesExactlyThreeNumbers()
        {
            var b = Modifiers.Default;
            var a = Assist.Apply(b);
            Assert.AreEqual(b.SourceRise * 0.75f, a.SourceRise, 1e-5f); Assert.AreEqual(9f, a.SourceRise, 1e-5f);
            Assert.AreEqual(b.TraceWindow + 0.2f, a.TraceWindow, 1e-5f);
            Assert.AreEqual(b.IceSpeedMult * 0.85f, a.IceSpeedMult, 1e-5f);
            Assert.AreEqual(b.FlipCost, a.FlipCost); Assert.AreEqual(b.RenderDrain, a.RenderDrain); Assert.AreEqual(b.ChainWindow, a.ChainWindow);
            Assert.AreEqual(b.ScoreMult, a.ScoreMult); Assert.AreEqual(b.RedlineThreshold, a.RedlineThreshold); Assert.AreEqual(b.MoveSpeedMult, a.MoveSpeedMult);
        }

        [Test]
        public void Assist_ThroughTheRulesEngine_BuysTimeInSource()
        {
            var plain = new MeltState(0f, Layer.Source);
            var eased = plain;
            var m = Modifiers.Default; var am = Assist.Apply(m);
            for (int i = 0; i < 84; i++) { plain = Melt.Step(plain, 0.1f, m); eased = Melt.Step(eased, 0.1f, am); }   // 8.4 s in SOURCE
            Assert.IsTrue(plain.Dead, "MELT-04: dead at 8.4 s without Assist");
            Assert.IsFalse(eased.Dead, "alive at 8.4 s with Assist");
            Assert.AreEqual(75.6f, eased.Melt, 0.1f);
            Assert.GreaterOrEqual(am.SourceRise, 8f, "above the engine's own SourceRise clamp, so the clamp never eats the easing");
            Assert.LessOrEqual(am.TraceWindow, 1f, "and under the TraceWindow clamp");
        }

        [Test]
        public void AssistHost_FoldsWhatTheRunWrites_AndNeverCompounds()
        {
            Assist.On = true;
            AssistHost.Fold();
            Assert.AreEqual(9f, GameState.Mods.SourceRise, 1e-5f);
            for (int i = 0; i < 5; i++) AssistHost.Fold();
            Assert.AreEqual(9f, GameState.Mods.SourceRise, 1e-5f, "folding again and again does not stack");

            var run = Modifiers.Default; run.FlipCost = 3f; run.SourceRise = 10f; run.IceSpeedMult = 1.2f;   // the run rewrites Mods (new room / conditional exploit)
            GameState.Mods = run;
            AssistHost.Fold();
            Assert.AreEqual(3f, GameState.Mods.FlipCost, "other numbers pass through");
            Assert.AreEqual(7.5f, GameState.Mods.SourceRise, 1e-5f);
            Assert.AreEqual(1.02f, GameState.Mods.IceSpeedMult, 1e-5f);

            Assist.On = false;
            AssistHost.Fold();
            Assert.AreEqual(10f, GameState.Mods.SourceRise, 1e-5f, "off again: the run's own numbers");
            Assert.AreEqual(1.2f, GameState.Mods.IceSpeedMult, 1e-5f);
            AssistHost.Fold();
            Assert.AreEqual(10f, GameState.Mods.SourceRise, 1e-5f);
        }

        [Test]
        public void Assist_Flagged_LatchesForTheAttempt_AndClearsAtTheTitle()
        {
            Assert.IsFalse(Assist.Flagged);
            Assist.On = true;
            Assert.IsTrue(Assist.Flagged, "on = flagged");
            GameState.Phase = GameState.PhaseKind.Playing; GameState.Paused = false;
            AssistHost.Latch();
            Assist.On = false;
            Assert.IsTrue(Assist.Used); Assert.IsTrue(Assist.Flagged, "switching it off before the last room does not clean the attempt");
            RunState.Phase = RunPhase.Map;
            GameState.Phase = GameState.PhaseKind.Title;
            AssistHost.Latch();
            Assert.IsTrue(Assist.Used, "the title between rooms of a run is not the end of the attempt");
            RunState.Phase = RunPhase.None;
            AssistHost.Latch();
            Assert.IsFalse(Assist.Used); Assert.IsFalse(Assist.Flagged);
        }

        [Test]
        public void Assist_TouchesNoUnlockOrRecord()
        {
            var before = SaveService.Data.unlocks.Count; var meds = SaveService.Data.medals.Count;
            Assist.On = true;
            AssistHost.Fold();
            Assert.AreEqual(before, SaveService.Data.unlocks.Count);
            Assert.AreEqual(meds, SaveService.Data.medals.Count);
            Assert.AreEqual(1f, GameState.Mods.ScoreMult, "scores are not scaled either");
        }

        // ------------------------------------------------------------------ rebinding
        static string Kb(string id) => Rebinds.Current(Rebinds.Find(id), false);
        static string Pad(string id) => Rebinds.Current(Rebinds.Find(id), true);

        [Test]
        public void Rebinds_StartAtTheShippedDefaults()
        {
            Assert.AreEqual("<Keyboard>/w", Kb("up")); Assert.AreEqual("<Keyboard>/a", Kb("left")); Assert.AreEqual("<Keyboard>/s", Kb("down")); Assert.AreEqual("<Keyboard>/d", Kb("right"));
            Assert.AreEqual("<Keyboard>/space", Kb("flip")); Assert.AreEqual("<Gamepad>/buttonSouth", Pad("flip"));
            Assert.AreEqual("<Keyboard>/e", Kb("hack")); Assert.AreEqual("<Gamepad>/buttonWest", Pad("hack"));
            Assert.AreEqual("<Keyboard>/r", Kb("restart")); Assert.AreEqual("<Gamepad>/select", Pad("restart"));
            Assert.AreEqual("<Keyboard>/escape", Kb("pause")); Assert.AreEqual("<Gamepad>/start", Pad("pause"));
            Assert.AreEqual("<Keyboard>/tab", Kb("map")); Assert.IsNull(Pad("map")); Assert.IsNull(Pad("up"));
            Assert.AreEqual("WASD", Rebinds.MoveLabel());
        }

        [Test]
        public void Rebind_Flip_IsAppliedSavedAndReloaded()
        {
            Assert.IsTrue(Rebinds.Set(Rebinds.Find("flip"), false, "<Keyboard>/f", out var swapped));
            Assert.IsNull(swapped);
            Assert.AreEqual("<Keyboard>/f", Kb("flip"));
            Assert.AreEqual("<Keyboard>/f", A11y.Get("a11y_bind_flip_k"), "saved under the workstream's prefix");
            Assert.AreEqual("<Gamepad>/buttonSouth", Pad("flip"), "the pad button is its own slot");
            Assert.AreEqual("F", Rebinds.Label(Rebinds.Find("flip"), false));

            GameInput.Flip.RemoveAllBindingOverrides();   // a fresh boot has no overrides applied yet
            Assert.AreEqual("<Keyboard>/space", Kb("flip"));
            Rebinds.LoadAll();
            Assert.AreEqual("<Keyboard>/f", Kb("flip"), "LoadAll restores the saved key");

            Rebinds.Reset(Rebinds.Find("flip"), false);
            Assert.AreEqual("<Keyboard>/space", Kb("flip"));
            Assert.IsNull(A11y.Get("a11y_bind_flip_k"), "a default needs no stored key");
        }

        [Test]
        public void Rebind_AKeyInUse_Swaps_SoFlipIsNeverDuplicated()
        {
            Assert.IsTrue(Rebinds.Set(Rebinds.Find("flip"), false, "<Keyboard>/e", out var swapped));   // E is HACK's key
            Assert.AreEqual("hack", swapped.Id);
            Assert.AreEqual("<Keyboard>/e", Kb("flip")); Assert.AreEqual("<Keyboard>/space", Kb("hack"));
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var s in Rebinds.Slots) Assert.IsTrue(seen.Add(Kb(s.Id)), "no key is bound to two verbs: " + Kb(s.Id));

            // pad too
            Assert.IsTrue(Rebinds.Set(Rebinds.Find("restart"), true, "<Gamepad>/buttonSouth", out swapped));
            Assert.AreEqual("flip", swapped.Id);
            Assert.AreEqual("<Gamepad>/select", Pad("flip"));
        }

        [Test]
        public void Rebind_RefusesWhatItShould()
        {
            var flip = Rebinds.Find("flip");
            Assert.IsFalse(Rebinds.Set(flip, false, "<Keyboard>/escape", out _), "Esc stays the pause / cancel key");
            Assert.IsFalse(Rebinds.Set(flip, false, "<Mouse>/leftButton", out _));
            Assert.IsFalse(Rebinds.Set(flip, false, "<Gamepad>/buttonNorth", out _), "a pad button on the keyboard column");
            Assert.IsFalse(Rebinds.Set(flip, true, "<Keyboard>/f", out _), "a key on the pad column");
            Assert.IsFalse(Rebinds.Set(flip, true, "<Gamepad>/leftStick/up", out _));
            Assert.IsFalse(Rebinds.Set(flip, true, "<Gamepad>/leftTrigger", out _));
            Assert.IsFalse(Rebinds.Set(Rebinds.Find("up"), true, "<Gamepad>/buttonSouth", out _), "move has no pad slot: the stick is fixed");
            Assert.IsFalse(Rebinds.Set(flip, false, "", out _));
            Assert.AreEqual("<Keyboard>/space", Kb("flip"), "nothing changed");

            // Pause sits on Esc; taking Flip's key would hand Esc to Flip: refused, and nothing moves
            Assert.IsFalse(Rebinds.Set(Rebinds.Find("pause"), false, "<Keyboard>/space", out _));
            Assert.AreEqual("<Keyboard>/escape", Kb("pause")); Assert.AreEqual("<Keyboard>/space", Kb("flip"));
            // but Pause can move to a free key and come back
            Assert.IsTrue(Rebinds.Set(Rebinds.Find("pause"), false, "<Keyboard>/p", out _));
            Rebinds.Reset(Rebinds.Find("pause"), false);
            Assert.AreEqual("<Keyboard>/escape", Kb("pause"));
        }

        [Test]
        public void Rebind_MoveDirections_AreCompositeParts()
        {
            Assert.IsTrue(Rebinds.Set(Rebinds.Find("up"), false, "<Keyboard>/i", out _));
            Assert.AreEqual("<Keyboard>/i", Kb("up"));
            Assert.AreEqual("I A S D", Rebinds.MoveLabel());
            int parts = 0;
            foreach (var b in GameInput.Move.bindings) if (b.isPartOfComposite && b.name == "Up") { Assert.AreEqual("<Keyboard>/i", b.effectivePath); parts++; }
            Assert.AreEqual(1, parts);
            Rebinds.ResetAll();
            Assert.AreEqual("WASD", Rebinds.MoveLabel());
            Assert.AreEqual(0, SaveService.Data.settings.Keys.Count(k => k.StartsWith("a11y_bind_")), "ResetAll clears every stored binding");
        }

        [Test]
        public void Rebind_Labels()
        {
            Assert.AreEqual("SPACE", Rebinds.Label("<Keyboard>/space"));
            Assert.AreEqual("A", Rebinds.Label("<Gamepad>/buttonSouth"));
            Assert.AreEqual("START", Rebinds.Label("<Gamepad>/start"));
            Assert.AreEqual("-", Rebinds.Label((string)null));
            Assert.IsNotEmpty(Rebinds.Label("<Keyboard>/leftArrow"));
        }

        // ------------------------------------------------------------------ capture (real devices)
        Keyboard kb; Gamepad pad; InputSettings.BackgroundBehavior bg;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode gv;
#endif
        void BeginInput()
        {
            bg = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            gv = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            kb = InputSystem.AddDevice<Keyboard>(); InputSystem.EnableDevice(kb);
            pad = InputSystem.AddDevice<Gamepad>();
        }

        void EndInput()
        {
            if (kb != null) InputSystem.RemoveDevice(kb);
            if (pad != null) InputSystem.RemoveDevice(pad);
            kb = null; pad = null;
            InputSystem.settings.backgroundBehavior = bg;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = gv;
#endif
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Capture_TakesTheNextKey_IgnoresOtherDevices_AndEscCancels()
        {
            BeginInput();
            try
            {
                InputSystem.QueueStateEvent(kb, new KeyboardState()); InputSystem.QueueStateEvent(pad, new GamepadState());
                yield return null; yield return null;   // start from a clean device state
                Assert.AreEqual("<Keyboard>/space", Rebinds.PathOf(kb.spaceKey, false));
                Assert.AreEqual("<Gamepad>/buttonSouth", Rebinds.PathOf(pad.buttonSouth, true));
                Assert.IsNull(Rebinds.PathOf(kb.spaceKey, true), "a key is not a pad button");
                Assert.IsNull(Rebinds.PathOf(pad.buttonSouth, false));

                Rebinds.Capture(false);
                Assert.IsTrue(Rebinds.Capturing);
                InputSystem.QueueStateEvent(pad, new GamepadState(GamepadButton.South));   // wrong device kind: ignored
                yield return null; yield return null;
                Assert.IsFalse(Rebinds.TakeCapture(out _));
                InputSystem.QueueStateEvent(pad, new GamepadState());
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.G));
                yield return null; yield return null;
                Assert.IsTrue(Rebinds.TakeCapture(out var path), "last seen: " + Rebinds.LastSeen);
                Assert.AreEqual("<Keyboard>/g", path);
                Assert.IsFalse(Rebinds.Capturing, "one press ends the capture");
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                yield return null;

                Rebinds.Capture(false);
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Escape));
                yield return null; yield return null;
                Assert.IsTrue(Rebinds.TakeCapture(out path));
                Assert.AreEqual("", path, "Esc = cancelled");
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                yield return null;

                Rebinds.Capture(true);
                InputSystem.QueueStateEvent(pad, new GamepadState(GamepadButton.North));
                yield return null; yield return null;
                Assert.IsTrue(Rebinds.TakeCapture(out path));
                Assert.AreEqual("<Gamepad>/buttonNorth", path);
            }
            finally { Rebinds.Cancel(); EndInput(); }
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Rebind_ChangesWhatTheGameReads()
        {
            BeginInput();
            try
            {
                Assert.IsTrue(Rebinds.Set(Rebinds.Find("flip"), false, "<Keyboard>/f", out _));
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Space));
                yield return null; yield return null;
                Assert.IsFalse(GameInput.Flip.IsPressed(), "Space no longer flips");
                InputSystem.QueueStateEvent(kb, new KeyboardState(Key.F));
                yield return null; yield return null;
                Assert.IsTrue(GameInput.Flip.IsPressed(), "F does");
                InputSystem.QueueStateEvent(kb, new KeyboardState());
                yield return null;
            }
            finally { EndInput(); }
        }
    }
}
