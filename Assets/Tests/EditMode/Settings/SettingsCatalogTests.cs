using NUnit.Framework;
using Game.Settings.Core;

public class SettingsCatalogTests
{
    [Test]
    public void VolumeClampsIntoZeroOne()
    {
        Assert.AreEqual(0f, SettingsCatalog.ClampVolume(-2f), 1e-4f);
        Assert.AreEqual(1f, SettingsCatalog.ClampVolume(5f), 1e-4f);
        Assert.AreEqual(0.42f, SettingsCatalog.ClampVolume(0.42f), 1e-4f);
    }

    [Test]
    public void CameraShakeClampsIntoZeroTwo()
    {
        Assert.AreEqual(0f, SettingsCatalog.ClampCameraShake(-1f), 1e-4f);
        Assert.AreEqual(2f, SettingsCatalog.ClampCameraShake(9f), 1e-4f);
        Assert.AreEqual(1f, SettingsCatalog.ClampCameraShake(1f), 1e-4f);
    }

    [Test]
    public void VSyncIsNormalisedToZeroOrOne()
    {
        Assert.AreEqual(0, SettingsCatalog.ClampVSync(0));
        Assert.AreEqual(1, SettingsCatalog.ClampVSync(1));
        Assert.AreEqual(1, SettingsCatalog.ClampVSync(4));
        Assert.AreEqual(1, SettingsCatalog.ClampVSync(-3));
    }

    [Test]
    public void FpsCapZeroMeansUncappedAndSurvivesClamping()
    {
        Assert.AreEqual(0, SettingsCatalog.ClampFpsCap(0));
        Assert.AreEqual(0, SettingsCatalog.ClampFpsCap(-120));
    }

    [Test]
    public void FpsCapBelowMinimumIsRaisedNotZeroed()
    {
        // A 3fps cap would be indistinguishable from a hang; the floor is a usability guard.
        Assert.AreEqual(30, SettingsCatalog.ClampFpsCap(3));
        Assert.AreEqual(30, SettingsCatalog.ClampFpsCap(29));
    }

    [Test]
    public void FpsCapIsBounded()
    {
        Assert.AreEqual(144, SettingsCatalog.ClampFpsCap(144));
        Assert.AreEqual(1000, SettingsCatalog.ClampFpsCap(99999));
    }

    [Test]
    public void DisplayModeOutOfEnumRangeFallsBackToDefault()
    {
        Assert.AreEqual(0, SettingsCatalog.ClampDisplayMode(0));
        Assert.AreEqual(3, SettingsCatalog.ClampDisplayMode(3));
        Assert.AreEqual(SettingsCatalog.DisplayModeDefault, SettingsCatalog.ClampDisplayMode(7));
        Assert.AreEqual(SettingsCatalog.DisplayModeDefault, SettingsCatalog.ClampDisplayMode(-1));
    }

    [Test]
    public void FlagIsNormalisedToZeroOrOne()
    {
        Assert.AreEqual(0, SettingsCatalog.ClampFlag(0));
        Assert.AreEqual(1, SettingsCatalog.ClampFlag(1));
        Assert.AreEqual(1, SettingsCatalog.ClampFlag(255));
    }

    [Test]
    public void AllKeysCoversEveryDeclaredKeyAndNothingElse()
    {
        // AllKeys drives the reset path. A key missing here survives a reset and silently
        // resurrects an old value; a stray key here could delete something else's data.
        CollectionAssert.AreEquivalent(
            new[]
            {
                SettingsCatalog.VersionKey,
                SettingsCatalog.MasterVolumeKey,
                SettingsCatalog.MusicVolumeKey,
                SettingsCatalog.SfxVolumeKey,
                SettingsCatalog.UiVolumeKey,
                SettingsCatalog.WidthKey,
                SettingsCatalog.HeightKey,
                SettingsCatalog.DisplayModeKey,
                SettingsCatalog.VSyncKey,
                SettingsCatalog.FpsCapKey,
                SettingsCatalog.CameraShakeKey,
                SettingsCatalog.DamageNumbersKey,
            },
            SettingsCatalog.AllKeys);
    }

    [Test]
    public void EveryKeyIsNamespacedUnderTheSettingsPrefix()
    {
        // Guards the reset path against ever deleting lobby.nickname or the identity token.
        foreach (string key in SettingsCatalog.AllKeys)
            Assert.IsTrue(key.StartsWith(SettingsCatalog.KeyPrefix), key + " is not namespaced");
    }
}
