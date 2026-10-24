using NUnit.Framework;
using UnityEngine;

namespace Desync.Enemies.Tests
{
    /// Heat mutator Scrambled Prompts: about 30 % of hack stations read mirrored, chosen per station and room seed.
    public class ScrambledPromptsEditTests
    {
        GameObject _go;
        uint _seed;

        [SetUp] public void SetUp() { _go = new GameObject("ScrambleTerminal"); _seed = GameState.Seed; }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            GameState.Flags.Clear();
            GameState.Seed = _seed;
            ExploitFlags.Refresh();
        }

        int MirroredOf(Terminal t, int stations)
        {
            int n = 0;
            for (int s = 1; s <= stations; s++) { t.seed = s; if (t.Mirrored) n++; }
            return n;
        }

        [Test]
        public void NoMutator_NothingMirrored()
        {
            GameState.Flags.Clear();
            ExploitFlags.Refresh();
            Assert.AreEqual(0, MirroredOf(_go.AddComponent<Terminal>(), 500));
        }

        [TestCase("mut:scrambled_prompts")]
        [TestCase("flag:scrambled_prompts")]
        public void ThirtyPercentMirrored_StablePerStationAndRoom(string flag)
        {
            GameState.Flags.Clear();
            GameState.Flags.Add(flag);
            ExploitFlags.Refresh();
            Assert.IsTrue(ExploitFlags.ScrambledPrompts);
            var t = _go.AddComponent<Terminal>();
            GameState.Seed = 4242u;
            int n = MirroredOf(t, 2000);
            Assert.That(n / 2000f, Is.InRange(0.27f, 0.33f), "share mirrored");

            t.seed = 7;
            bool first = t.Mirrored;
            for (int i = 0; i < 5; i++) Assert.AreEqual(first, t.Mirrored, "the same station stays the same (restarts)");

            int differs = 0;
            for (uint room = 1; room <= 200; room++) { GameState.Seed = room; if (t.Mirrored != first) differs++; }
            Assert.Greater(differs, 0, "another room re-rolls the station");
        }
    }
}
