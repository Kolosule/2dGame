using NUnit.Framework;
using Game.Hud.Core;

public class HealthSegmentsTests
{
    [Test]
    public void FullHealth_AllSegmentsLit()
    {
        Assert.AreEqual(10, HealthSegments.FilledSegments(100f, 100f, 10));
    }

    [Test]
    public void ZeroHealth_NoSegmentsLit()
    {
        Assert.AreEqual(0, HealthSegments.FilledSegments(0f, 100f, 10));
    }

    [Test]
    public void HalfHealth_HalfSegmentsLit()
    {
        Assert.AreEqual(5, HealthSegments.FilledSegments(50f, 100f, 10));
    }

    [Test]
    public void PartialSegment_ReportsFractionalFill()
    {
        // 55/100 over 10 segments = 5 full + 0.5 of the 6th.
        Assert.AreEqual(5, HealthSegments.FilledSegments(55f, 100f, 10));
        Assert.AreEqual(0.5f, HealthSegments.PartialFill01(55f, 100f, 10), 1e-4f);
    }

    [Test]
    public void FilledSegments_NeverExceedsCount_AndNeverNegative()
    {
        Assert.AreEqual(10, HealthSegments.FilledSegments(150f, 100f, 10));
        Assert.AreEqual(0, HealthSegments.FilledSegments(-20f, 100f, 10));
    }

    [Test]
    public void ZeroOrNegativeMax_IsSafe()
    {
        Assert.AreEqual(0, HealthSegments.FilledSegments(50f, 0f, 10));
        Assert.AreEqual(0f, HealthSegments.PartialFill01(50f, 0f, 10), 1e-4f);
    }
}
