using Desync.Rules;
using NUnit.Framework;

namespace Desync.Flow.Tests
{
    /// M3 integration rules with no scene: Ghost Cache under Fewer Choices, ARCHITECT endings in the save.
    public class M3FixesTests
    {
        [Test]
        public void GhostCache_PaysEveryExit_ButEverySecondUnderFewerChoices()
        {
            for (int n = 1; n <= 6; n++) Assert.IsTrue(RunRules.GhostCacheReroll(n, false), "exit " + n);
            int paid = 0;
            for (int n = 1; n <= 6; n++) if (RunRules.GhostCacheReroll(n, true)) paid++;
            Assert.AreEqual(3, paid, "six Ghost Cache exits pay three tokens under Fewer Choices");
            Assert.IsFalse(RunRules.GhostCacheReroll(1, true), "the first one pays nothing");
        }

        [Test]
        public void ArchitectEndings_AreRecordedOnce_AsUnlocks()
        {
            var save = SaveMigrator.Default();
            Assert.IsTrue(RunUnlocks.RecordEnding(save, "Exposed"));
            Assert.IsFalse(RunUnlocks.RecordEnding(save, "Exposed"), "already recorded");
            Assert.IsTrue(RunUnlocks.RecordEnding(save, "Merged"));
            Assert.IsFalse(RunUnlocks.RecordEnding(save, null));
            CollectionAssert.AreEqual(new[] { "ending_exposed", "ending_merged" }, save.unlocks);
            var back = SaveMigrator.Migrate(SaveMigrator.Serialize(save));
            CollectionAssert.AreEqual(save.unlocks, back.unlocks, "survives a save round trip");
        }
    }
}
