using NUnit.Framework;
using Game.Combat.Core;

public class FlashCurveTests
{
    [Test]
    public void Intensity_AtStart_IsOne()
    {
        Assert.AreEqual(1f, FlashCurve.Intensity(0f, 0.1f), 1e-4f);
    }

    [Test]
    public void Intensity_AtHalf_IsHalf()
    {
        Assert.AreEqual(0.5f, FlashCurve.Intensity(0.05f, 0.1f), 1e-4f);
    }

    [Test]
    public void Intensity_AtEnd_IsZero()
    {
        Assert.AreEqual(0f, FlashCurve.Intensity(0.1f, 0.1f), 1e-4f);
    }

    [Test]
    public void Intensity_PastEnd_ClampsToZero()
    {
        Assert.AreEqual(0f, FlashCurve.Intensity(0.5f, 0.1f), 1e-4f);
    }

    [Test]
    public void Intensity_ZeroOrNegativeDuration_IsZero()
    {
        Assert.AreEqual(0f, FlashCurve.Intensity(0f, 0f), 1e-4f);
    }
}
