using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Desync.Audio.Tests
{
    /// The bank produced by Desync.Audio.Editor.AudioSetup.Run must be complete and the music stems must be loop-compatible.
    public class AudioBankTests
    {
        static AudioBank Bank()
        {
            var b = Resources.Load<AudioBank>("AudioBank");
            Assert.IsNotNull(b, "Resources/AudioBank missing - run Desync.Audio.Editor.AudioSetup.Run");
            return b;
        }

        [Test]
        public void EveryClipIsAssigned()
        {
            var b = Bank();
            foreach (var f in typeof(AudioBank).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType == typeof(AudioClip)) Assert.IsNotNull(f.GetValue(b) as AudioClip, "bank field " + f.Name);
                else if (f.FieldType == typeof(AudioClip[]))
                {
                    var a = f.GetValue(b) as AudioClip[];
                    Assert.IsTrue(a != null && a.Length >= 3, "bank array " + f.Name);
                    foreach (var c in a) Assert.IsNotNull(c, "bank array " + f.Name);
                }
            }
            Assert.AreEqual(b.footstepRender.Length, b.footstepSource.Length);
        }

        [Test]
        public void MusicStemsAreEqualLengthAndLoopOnABarLine()
        {
            var b = Bank();
            var stems = new[] { b.musicRender, b.musicSource, b.musicRenderLp, b.musicSourceLp };
            foreach (var s in stems)
            {
                Assert.AreEqual(stems[0].samples, s.samples, s.name + " length");
                Assert.AreEqual(stems[0].frequency, s.frequency, s.name + " rate");
            }
            double barSamples = 4.0 * 60.0 / GameConfig.I.bpm * stems[0].frequency;
            double bars = stems[0].samples / barSamples;
            Assert.AreEqual(System.Math.Round(bars), bars, 1e-6, "loop is a whole number of bars");
            Assert.GreaterOrEqual(bars, 8);
        }

        [Test]
        public void ContractKeysAreRegistered()
        {
            foreach (var k in new[] { "music.render", "music.source", "music.render.lp", "music.source.lp", "sfx.flip.source", "sfx.hack.tick", "sfx.heartbeat" })
                Assert.IsNotNull(AssetLib.Get<AudioClip>(k), k);
        }
    }
}
