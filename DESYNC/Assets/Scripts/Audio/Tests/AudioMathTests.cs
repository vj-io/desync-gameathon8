using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Desync.Audio.Tests
{
    /// The formulas behind the adaptive mix and the pitched cues (no audio device needed).
    public class AudioMathTests
    {
        [Test]
        public void TensionRunsFromFiftyToNinetyPercentMelt()
        {
            Assert.AreEqual(0f, AudioMath.Tension(0f));
            Assert.AreEqual(0f, AudioMath.Tension(50f));
            Assert.AreEqual(0.5f, AudioMath.Tension(70f), 1e-5f);
            Assert.AreEqual(1f, AudioMath.Tension(90f));
            Assert.AreEqual(1f, AudioMath.Tension(100f));
        }

        [Test]
        public void TraceLayersStepUpPerLevel()
        {
            float[] a = { 0f, 0.7f, 1f, 1f }, b = { 0f, 0f, 0.7f, 1f };
            for (int level = 0; level <= 3; level++)
            {
                Assert.AreEqual(a[level], AudioMath.TraceA(level), "A at level " + level);
                Assert.AreEqual(b[level], AudioMath.TraceB(level), "B at level " + level);
            }
            Assert.AreEqual(0f, AudioMath.TraceA(-1));
        }

        [Test]
        public void ScorePopClimbsOneSemitonePerChainStepUpToAnOctave()
        {
            Assert.AreEqual(1f, AudioMath.ScorePitch(1f), 1e-5f);
            Assert.AreEqual(Mathf.Pow(2f, 1f / 12f), AudioMath.ScorePitch(1.25f), 1e-5f, "chain 1");
            Assert.AreEqual(Mathf.Pow(2f, 4f / 12f), AudioMath.ScorePitch(2f), 1e-5f, "chain 4");
            Assert.AreEqual(2f, AudioMath.ScorePitch(4f), 1e-5f, "chain 12 = the multiplier cap");
            Assert.AreEqual(2f, AudioMath.ScorePitch(8f), 1e-5f, "the x8 exploit cap does not climb further");
            Assert.AreEqual(1f, AudioMath.ScorePitch(0.5f), 1e-5f);
        }

        [Test]
        public void DecryptTicksRiseOneOctaveInSixteenSteps()
        {
            Assert.AreEqual(16, Mathf.RoundToInt(AudioMath.DecryptTime / AudioMath.DecryptEvery));
            Assert.AreEqual(AudioMath.DecryptTicks, Mathf.RoundToInt(AudioMath.DecryptTime / AudioMath.DecryptEvery));
            Assert.AreEqual(1.0625f, AudioMath.DecryptPitch(0), 1e-5f);
            Assert.AreEqual(2f, AudioMath.DecryptPitch(AudioMath.DecryptTicks - 1), 1e-5f);
            for (int i = 1; i < AudioMath.DecryptTicks; i++) Assert.Greater(AudioMath.DecryptPitch(i), AudioMath.DecryptPitch(i - 1));
        }

        [Test]
        public void UplinkCountdownTicksOnceASecondThenTwiceThenFourTimes()
        {
            Assert.AreEqual(-1, AudioMath.UplinkTickId(0f));
            Assert.AreEqual(-1, AudioMath.UplinkTickId(-1f));
            var seen = new List<int>();
            int last = int.MinValue;
            for (float r = 20f; r > 0f; r -= 0.001f)
            {
                int id = AudioMath.UplinkTickId(r);
                if (id != last) { seen.Add(id); last = id; }
            }
            // 15 ticks while more than 5 s are left (20..6), 6 in (2, 5] s, 8 in the last two seconds
            Assert.AreEqual(15 + 6 + 8, seen.Count);
            Assert.AreEqual(20, seen[0], "the first tick is on the uplink opening");
            Assert.AreEqual(new HashSet<int>(seen).Count, seen.Count, "every tick has its own id (no stutter)");
        }

        [Test]
        public void UplinkTickPitchRisesFromOneToOneAndAHalf()
        {
            Assert.AreEqual(1f, AudioMath.UplinkPitch(20f), 1e-5f);
            Assert.AreEqual(1.25f, AudioMath.UplinkPitch(10f), 1e-5f);
            Assert.AreEqual(1.5f, AudioMath.UplinkPitch(0f), 1e-5f);
            Assert.AreEqual(1f, AudioMath.UplinkPitch(60f), 1e-5f);
        }

        [Test]
        public void ActFollowsTheRunOnlyWhileARunIsOnScreen()
        {
            Assert.AreEqual(0, AudioMath.ActIndex(false, 3), "no run on screen: act 1 music");
            Assert.AreEqual(0, AudioMath.ActIndex(true, 1));
            Assert.AreEqual(1, AudioMath.ActIndex(true, 2));
            Assert.AreEqual(2, AudioMath.ActIndex(true, 3));
            Assert.AreEqual(2, AudioMath.ActIndex(true, 9), "never past the third set");
            Assert.AreEqual(0, AudioMath.ActIndex(true, 0));
        }
    }

    /// Every music clip of every act and layer must share one length and one bar grid, and acts 2 / 3 must load on demand.
    public class AudioBankMusicTests
    {
        static AudioBank Bank()
        {
            var b = Resources.Load<AudioBank>("AudioBank");
            Assert.IsNotNull(b, "Resources/AudioBank missing - run Desync.Audio.Editor.AudioSetup.Run");
            return b;
        }

        [Test]
        public void EveryActAndLayerSharesOneLengthAndBarGrid()
        {
            var b = Bank();
            var ref0 = b.musicRender;
            double barSamples = 4.0 * 60.0 / GameConfig.I.bpm * ref0.frequency;
            for (int act = 0; act < 3; act++)
                for (int stem = 0; stem < 4; stem++)
                {
                    var c = b.Stem(act, stem);
                    Assert.IsNotNull(c, "act " + (act + 1) + " stem " + stem);
                    Assert.AreEqual(ref0.samples, c.samples, c.name + " length");
                    Assert.AreEqual(ref0.frequency, c.frequency, c.name + " rate");
                }
            foreach (var c in new[] { b.musicTension, b.musicTraceA, b.musicTraceB })
            {
                Assert.IsNotNull(c);
                Assert.AreEqual(ref0.samples, c.samples, c.name + " length");
                Assert.AreEqual(ref0.frequency, c.frequency, c.name + " rate");
                Assert.AreEqual(1, c.channels, c.name + " is a mono layer");
            }
            double bars = ref0.samples / barSamples;
            Assert.AreEqual(System.Math.Round(bars), bars, 1e-6, "a whole number of bars");
        }

        [Test]
        public void ActSetsAreDistinctClips()
        {
            var b = Bank();
            var all = new HashSet<AudioClip>();
            for (int act = 0; act < 3; act++)
                for (int stem = 0; stem < 4; stem++) Assert.IsTrue(all.Add(b.Stem(act, stem)), "act " + (act + 1) + " stem " + stem + " reuses a clip");
            Assert.AreEqual(12, all.Count);
        }

        [Test]
        public void ActTwoAndThreeStemsLoadOnDemandAndActOneStaysPreloaded()
        {
            var b = Bank();
            for (int act = 0; act < 3; act++)
                for (int stem = 0; stem < 4; stem++)
                {
                    var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(b.Stem(act, stem))) as AudioImporter;
                    Assert.IsNotNull(imp);
                    Assert.AreEqual(act == 0, imp.defaultSampleSettings.preloadAudioData, b.Stem(act, stem).name + " preload");
                    Assert.AreEqual(act != 0, imp.loadInBackground, b.Stem(act, stem).name + " background load");
                    Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, imp.defaultSampleSettings.loadType, b.Stem(act, stem).name + " load type");
                }
        }

        [Test]
        public void NewContractKeysAreRegistered()
        {
            foreach (var k in new[] { "music.render.a2", "music.source.lp.a3", "music.tension", "music.trace.a", "music.trace.b", "sfx.score.pop",
                                      "sfx.redline", "sfx.graze", "sfx.tracker.sting", "sfx.auditor.p2", "sfx.auditor.p3", "sfx.uplink.tick",
                                      "sfx.decrypt.tick", "sfx.lantern.hum", "sfx.watcher.beam" })
                Assert.IsNotNull(AssetLib.Get<AudioClip>(k), k);
        }
    }
}
