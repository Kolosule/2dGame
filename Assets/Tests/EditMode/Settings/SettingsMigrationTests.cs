using NUnit.Framework;
using Game.Settings.Core;

public class SettingsMigrationTests
{
    [Test]
    public void MatchingVersionNeedsNoAction()
    {
        Assert.AreEqual(SettingsMigrationAction.None, SettingsMigration.Resolve(1, 1));
    }

    [Test]
    public void MissingVersionResetsToDefaults()
    {
        // A store written before settings existed reads back 0 from PlayerPrefs.
        Assert.AreEqual(SettingsMigrationAction.ResetToDefaults, SettingsMigration.Resolve(0, 1));
    }

    [Test]
    public void FutureVersionResetsToDefaults()
    {
        Assert.AreEqual(SettingsMigrationAction.ResetToDefaults, SettingsMigration.Resolve(99, 1));
    }

    [Test]
    public void CorruptNegativeVersionResetsToDefaults()
    {
        Assert.AreEqual(SettingsMigrationAction.ResetToDefaults, SettingsMigration.Resolve(-7, 1));
    }
}
