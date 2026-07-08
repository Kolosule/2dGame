using NUnit.Framework;
using Game.Hud.Core;

public class CooldownFillTests
{
    [Test]
    public void Ready_WhenNoRemaining_IsOne()
    {
        Assert.AreEqual(1f, CooldownFill.Fill01(0f, 5f), 1e-4f);
    }

    [Test]
    public void JustUsed_FullRemaining_IsZero()
    {
        Assert.AreEqual(0f, CooldownFill.Fill01(5f, 5f), 1e-4f);
    }

    [Test]
    public void Halfway_IsHalf()
    {
        Assert.AreEqual(0.5f, CooldownFill.Fill01(2.5f, 5f), 1e-4f);
    }

    [Test]
    public void ZeroOrNegativeTotal_IsReady()
    {
        Assert.AreEqual(1f, CooldownFill.Fill01(3f, 0f), 1e-4f);
    }

    [Test]
    public void RemainingAboveTotal_ClampsToZero()
    {
        Assert.AreEqual(0f, CooldownFill.Fill01(10f, 5f), 1e-4f);
    }
}
