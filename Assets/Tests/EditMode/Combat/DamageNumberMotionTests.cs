using NUnit.Framework;
using Game.Combat.Core;

public class DamageNumberMotionTests
{
    [Test]
    public void YOffset_GrowsLinearlyWithTime()
    {
        Assert.AreEqual(0f, DamageNumberMotion.YOffset(0f, 1f), 1e-4f);
        Assert.AreEqual(0.5f, DamageNumberMotion.YOffset(0.5f, 1f), 1e-4f);
    }

    [Test]
    public void Alpha_AtStart_IsOne()
    {
        Assert.AreEqual(1f, DamageNumberMotion.Alpha(0f, 0.7f), 1e-4f);
    }

    [Test]
    public void Alpha_AtEnd_IsZero()
    {
        Assert.AreEqual(0f, DamageNumberMotion.Alpha(0.7f, 0.7f), 1e-4f);
    }

    [Test]
    public void Alpha_PastEnd_ClampsToZero()
    {
        Assert.AreEqual(0f, DamageNumberMotion.Alpha(2f, 0.7f), 1e-4f);
    }

    [Test]
    public void Alpha_ZeroOrNegativeLifetime_IsZero()
    {
        Assert.AreEqual(0f, DamageNumberMotion.Alpha(0f, 0f), 1e-4f);
    }
}
