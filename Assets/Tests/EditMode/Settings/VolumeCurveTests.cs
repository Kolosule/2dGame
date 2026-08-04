using NUnit.Framework;
using Game.Settings.Core;

public class VolumeCurveTests
{
    [Test]
    public void FullVolumeIsZeroDecibels()
    {
        Assert.AreEqual(0f, VolumeCurve.LinearToDecibels(1f), 1e-4f);
    }

    [Test]
    public void HalfVolumeIsAboutMinusSixDecibels()
    {
        Assert.AreEqual(-6.0206f, VolumeCurve.LinearToDecibels(0.5f), 1e-3f);
    }

    [Test]
    public void FloorLinearIsMinusEighty()
    {
        Assert.AreEqual(-80f, VolumeCurve.LinearToDecibels(0.0001f), 1e-3f);
    }

    [Test]
    public void ZeroIsSilent()
    {
        Assert.AreEqual(-80f, VolumeCurve.LinearToDecibels(0f), 1e-4f);
    }

    [Test]
    public void NegativeIsSilentNotNaN()
    {
        Assert.AreEqual(-80f, VolumeCurve.LinearToDecibels(-0.5f), 1e-4f);
    }

    [Test]
    public void BelowFloorClampsToMinusEighty()
    {
        Assert.AreEqual(-80f, VolumeCurve.LinearToDecibels(0.00000001f), 1e-3f);
    }
}
