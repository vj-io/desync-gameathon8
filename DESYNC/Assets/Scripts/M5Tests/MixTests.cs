using System.Collections;
using Desync.Audio;
using Desync.Flow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Desync.M5.Tests
{
    /// The mix pass: limiter and duck formulas (pure), then the live AudioManager (buses, duck, limiter on the listener).
    public class MixTests
    {
        const string SaveKey = "desync.save.m5mix";

        // ------------------------------------------------------------------ pure
        [Test]
        public void Limiter_LeavesAQuietMixAlone()
        {
            Assert.AreEqual(1f, MixMath.LimiterGain(MixMath.PeakEstimate(0.5f * 0.5f)));          // one cue at half level
            Assert.AreEqual(1f, MixMath.LimiterGain(MixMath.PeakEstimate(1f)));                  // one cue at full level: -1 dBFS peak, under the ceiling
            Assert.AreEqual(1f, MixMath.LimiterGain(0f));
        }

        [Test]
        public void Limiter_PullsALoudMixBackToTheCeiling_AndNeverRaisesIt()
        {
            float prev = 1f;
            for (int voices = 1; voices <= 16; voices++)
            {
                float est = MixMath.PeakEstimate(voices);   // n voices at full level: power n
                float gain = MixMath.LimiterGain(est);
                Assert.LessOrEqual(gain, 1f);
                Assert.LessOrEqual(gain, prev + 1e-6f, voices + " voices: more voices never mean more gain");
                Assert.LessOrEqual(est * gain, MixMath.Ceiling + 1e-4f, voices + " voices stay under the ceiling after the limiter");
                prev = gain;
            }
            Assert.Less(MixMath.LimiterGain(MixMath.PeakEstimate(3f)), 1f, "three full-level cues need limiting");
            Assert.AreEqual(1f, MixMath.LimiterGain(MixMath.PeakEstimate(0.55f * 0.55f + 0.8f * 0.8f)), "music under a footstep: no limiting");
        }

        [Test]
        public void Limiter_AttackIsFast_ReleaseIsSlower()
        {
            float g = 1f, target = 0.5f;
            g = MixMath.Step(g, target, 0.016f);
            Assert.Less(g, 0.6f, "one 60 Hz frame takes most of the 0.5 drop (about 30 ms attack)");
            g = MixMath.Step(g, target, 0.016f);
            Assert.AreEqual(0.5f, g, 1e-4f);

            float up = 0.5f;
            up = MixMath.Step(up, 1f, 0.1f);
            Assert.Less(up, 1f, "release is slower than 100 ms");
            Assert.Greater(up, 0.5f);
            for (int i = 0; i < 20; i++) up = MixMath.Step(up, 1f, 0.016f);
            Assert.AreEqual(1f, up, 1e-4f, "and complete within about half a second");
        }

        [Test]
        public void Duck_TargetsTheDeathCardAndStingers_QuickDownSlowUp()
        {
            Assert.AreEqual(1f, MixMath.DuckTarget(false, false));
            Assert.AreEqual(MixMath.RecapDuck, MixMath.DuckTarget(true, false));
            Assert.AreEqual(MixMath.StingerDuck, MixMath.DuckTarget(false, true));
            Assert.AreEqual(MixMath.RecapDuck, MixMath.DuckTarget(true, true), "the deeper duck wins");

            float d = 1f;
            for (int i = 0; i < 7; i++) d = MixMath.StepDuck(d, MixMath.RecapDuck, 0.016f);   // ~0.11 s
            Assert.AreEqual(MixMath.RecapDuck, d, 1e-4f, "ducks inside a tenth of a second");
            d = MixMath.StepDuck(d, 1f, 0.25f);
            Assert.Less(d, 0.8f, "comes back slowly");
            for (int i = 0; i < 40; i++) d = MixMath.StepDuck(d, 1f, 0.016f);
            Assert.AreEqual(1f, d, 1e-4f);
        }

        // ------------------------------------------------------------------ live manager
        [SetUp] public void SetUp() { Isolation.Begin(SaveKey); }

        [TearDown]
        public void TearDown()
        {
            A11y.ResetDefaults();
            GameState.Phase = GameState.PhaseKind.Title;
            Isolation.End(SaveKey);
        }

        static AudioSource PlayingPooled(string prefix)
        {
            foreach (var s in AudioManager.I.GetComponentsInChildren<AudioSource>())
                if (s.isPlaying && s.name.StartsWith(prefix)) return s;
            return null;
        }

        static void StopPooled()
        {
            foreach (var s in AudioManager.I.GetComponentsInChildren<AudioSource>())
                if (s.name.StartsWith("sfx")) s.Stop();
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator EffectsAndInterfaceBuses_ScaleThePooledCues()
        {
            yield return null;
            Assert.IsNotNull(AudioManager.I);
            StopPooled();
            GameEvents.FlipRejected?.Invoke();   // an effects cue at 0.7
            yield return null;
            var s = PlayingPooled("sfx2d");
            Assume.That(s != null, "no audio bank in this run: nothing to measure");
            float full = s.volume;
            Assert.AreEqual(0.7f, full, 1e-3f, "default buses are untouched");
            StopPooled();

            A11y.SfxVolume = 0.5f;
            GameEvents.FlipRejected?.Invoke();
            yield return null;
            Assert.AreEqual(full * 0.5f, PlayingPooled("sfx2d").volume, 1e-3f, "effects bus x0.5");
            StopPooled();

            A11y.SfxVolume = 1f; A11y.UiVolume = 0.25f;
            RunEvents.Leave?.Invoke();   // the interface click (the run ignores it outside a market)
            yield return null;
            var ui = PlayingPooled("sfx2d");
            Assert.IsNotNull(ui);
            Assert.AreEqual(0.25f, ui.volume, 1e-3f, "interface bus x0.25 on a click of volume 1");
            StopPooled();
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator ListenerVolume_IsMasterTimesLimiter_AndNeverAboveMaster()
        {
            yield return null;
            float master = Settings.Volume;
            for (int i = 0; i < 3; i++) yield return null;
            Assert.LessOrEqual(AudioListener.volume, master + 1e-4f);
            Assert.AreEqual(master * AudioManager.I.LimiterGain, AudioListener.volume, 1e-4f);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Music_DucksUnderTheDeathCard_AndComesBack()
        {
            yield return null;
            for (float t = 0f; t < 3f && AudioManager.I.MusicDuck < 0.95f; t += Time.unscaledDeltaTime) yield return null;   // let an earlier stinger's duck release
            Assert.AreEqual(1f, AudioManager.I.MusicDuck, 0.05f, "no duck in normal play");
            GameState.Phase = GameState.PhaseKind.Recap;
            for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime) yield return null;
            Assert.AreEqual(MixMath.RecapDuck, AudioManager.I.MusicDuck, 0.05f, "ducked while the death card is up");
            GameState.Phase = GameState.PhaseKind.Playing;
            for (float t = 0f; t < 1.5f; t += Time.unscaledDeltaTime) yield return null;
            Assert.AreEqual(1f, AudioManager.I.MusicDuck, 0.05f, "back at full once the card is gone");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator StingerCue_DucksTheMusicBriefly()
        {
            yield return null;
            StopPooled();
            GameEvents.Alarm?.Invoke();   // PMax stinger
            for (float t = 0f; t < 0.4f; t += Time.unscaledDeltaTime) yield return null;
            Assert.Less(AudioManager.I.MusicDuck, 0.7f, "music is under the alarm");
            for (float t = 0f; t < 3f; t += Time.unscaledDeltaTime) yield return null;
            Assert.Greater(AudioManager.I.MusicDuck, 0.95f, "and recovers");
            StopPooled();
        }
    }
}
