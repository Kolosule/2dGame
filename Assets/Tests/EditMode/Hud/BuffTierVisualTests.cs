using NUnit.Framework;
using Game.Hud.Core;

public class BuffTierVisualTests
{
    [Test]
    public void Tier0_IsLocked()
    {
        Assert.IsTrue(BuffTierVisual.IsLocked(0));
    }

    [Test]
    public void PositiveTier_IsNotLocked()
    {
        Assert.IsFalse(BuffTierVisual.IsLocked(1));
        Assert.IsFalse(BuffTierVisual.IsLocked(3));
    }

    [Test]
    public void Intensity_LockedTier_IsZero()
    {
        Assert.AreEqual(0f, BuffTierVisual.Intensity01(0, 3), 1e-4f);
    }

    [Test]
    public void Intensity_MaxTier_IsOne()
    {
        Assert.AreEqual(1f, BuffTierVisual.Intensity01(3, 3), 1e-4f);
    }

    [Test]
    public void Intensity_MidTier_IsProportional()
    {
        Assert.AreEqual(1f / 3f, BuffTierVisual.Intensity01(1, 3), 1e-4f);
        Assert.AreEqual(2f / 3f, BuffTierVisual.Intensity01(2, 3), 1e-4f);
    }

    [Test]
    public void Intensity_AboveMax_ClampsToOne()
    {
        Assert.AreEqual(1f, BuffTierVisual.Intensity01(5, 3), 1e-4f);
    }

    [Test]
    public void Intensity_ZeroOrNegativeMax_IsSafe()
    {
        Assert.AreEqual(0f, BuffTierVisual.Intensity01(2, 0), 1e-4f);
    }

    [Test]
    public void PipFilled_FillsExactlyTierPips()
    {
        Assert.IsFalse(BuffTierVisual.PipFilled(0, 0), "tier 0 fills nothing");
        Assert.IsTrue(BuffTierVisual.PipFilled(0, 1));
        Assert.IsFalse(BuffTierVisual.PipFilled(1, 1));
        Assert.IsTrue(BuffTierVisual.PipFilled(2, 3), "top pip at max tier");
        Assert.IsFalse(BuffTierVisual.PipFilled(-1, 3), "negative index is never filled");
    }
}
