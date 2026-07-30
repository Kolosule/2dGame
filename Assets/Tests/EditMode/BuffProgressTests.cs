using NUnit.Framework;
using Game.Buffs.Core;

/// <summary>
/// Progress math for the HUD's next-unlock fill. The 12-step individual curve
/// (4 buffs x 3 tiers) and the 2-step team curve (1 buff x 2 tiers) share it.
/// </summary>
public class BuffProgressTests
{
    private static readonly int[] Curve = { 5, 10, 16, 24, 34, 46, 62, 80, 110, 150, 200, 260 };
    private static readonly int[] Vanguard = { 12, 45 };

    [Test]
    public void NextStepIndex_IsTheNextRoundRobinStepForThisPosition()
    {
        // 6 steps unlocked, 4 buffs: position 0 already took steps 0 and 4, so its next is 8.
        Assert.AreEqual(8, BuffProgress.NextStepIndexFor(6, 0, 4, 12));
        // Position 2's steps are 2, 6, 10 — step 2 is crossed, so its next is 6.
        Assert.AreEqual(6, BuffProgress.NextStepIndexFor(6, 2, 4, 12));
        // Nothing unlocked: each position's first step is its own index.
        Assert.AreEqual(3, BuffProgress.NextStepIndexFor(0, 3, 4, 12));
    }

    [Test]
    public void NextStepIndex_IsMinusOneWhenTheCurveIsExhausted()
    {
        Assert.AreEqual(-1, BuffProgress.NextStepIndexFor(12, 0, 4, 12));
        Assert.AreEqual(-1, BuffProgress.NextStepIndexFor(12, 3, 4, 12));
    }

    [Test]
    public void NextStepIndex_GuardsNonsenseInputs()
    {
        Assert.AreEqual(-1, BuffProgress.NextStepIndexFor(0, 0, 0, 12), "buffCount 0");
        Assert.AreEqual(-1, BuffProgress.NextStepIndexFor(0, -1, 4, 12), "negative position");
    }

    [Test]
    public void HighestCrossed_IsTheLastThresholdAtOrBelowTheValue()
    {
        Assert.AreEqual(0, BuffProgress.HighestCrossed(Curve, 4));
        Assert.AreEqual(5, BuffProgress.HighestCrossed(Curve, 5), "exact boundary counts as crossed");
        Assert.AreEqual(46, BuffProgress.HighestCrossed(Curve, 55));
        Assert.AreEqual(260, BuffProgress.HighestCrossed(Curve, 999));
        Assert.AreEqual(0, BuffProgress.HighestCrossed(null, 100));
    }

    [Test]
    public void Fraction01_IsLinearBetweenBounds_AndClamps()
    {
        Assert.AreEqual(0f, BuffProgress.Fraction01(10, 10, 20), 1e-4f);
        Assert.AreEqual(0.5f, BuffProgress.Fraction01(15, 10, 20), 1e-4f);
        Assert.AreEqual(1f, BuffProgress.Fraction01(20, 10, 20), 1e-4f);
        Assert.AreEqual(0f, BuffProgress.Fraction01(3, 10, 20), 1e-4f, "below the lower bound");
        Assert.AreEqual(1f, BuffProgress.Fraction01(99, 10, 20), 1e-4f, "above the upper bound");
        Assert.AreEqual(1f, BuffProgress.Fraction01(5, 20, 20), 1e-4f, "degenerate range reads as full");
    }

    [Test]
    public void NextThresholdFor_IsTheDepositThatRaisesThisBuff()
    {
        // 55 banked -> 6 steps. Position 0 next tiers at step 8 (110); position 3 at step 7 (80).
        Assert.AreEqual(110, BuffProgress.NextThresholdFor(Curve, 55, 0, 4));
        Assert.AreEqual(80, BuffProgress.NextThresholdFor(Curve, 55, 3, 4));
        // Fully banked: nothing left for anyone.
        Assert.AreEqual(0, BuffProgress.NextThresholdFor(Curve, 260, 0, 4));
        Assert.AreEqual(0, BuffProgress.NextThresholdFor(null, 55, 0, 4));
    }

    [Test]
    public void ToNextTier01_RunsFromTheLastCrossedThresholdToThisBuffsNextOne()
    {
        // 55 banked: last crossed is 46, position 0's target is 110 -> (55-46)/(110-46).
        Assert.AreEqual(9f / 64f, BuffProgress.ToNextTier01(Curve, 55, 0, 4), 1e-4f);
        // Sitting exactly on a threshold reads as empty toward the NEXT one, not full.
        Assert.AreEqual(0f, BuffProgress.ToNextTier01(Curve, 46, 0, 4), 1e-4f);
        // One point short of the target reads nearly full.
        Assert.AreEqual(29f / 30f, BuffProgress.ToNextTier01(Curve, 109, 0, 4), 1e-4f);
    }

    [Test]
    public void ToNextTier01_IsFullWhenNothingIsLeftToUnlock()
    {
        Assert.AreEqual(1f, BuffProgress.ToNextTier01(Curve, 260, 0, 4), 1e-4f);
        Assert.AreEqual(1f, BuffProgress.ToNextTier01(Curve, 5000, 3, 4), 1e-4f);
        Assert.AreEqual(1f, BuffProgress.ToNextTier01(null, 55, 0, 4), 1e-4f);
    }

    [Test]
    public void TeamCurve_UsesTheSameMathWithBuffCountOne()
    {
        // A team averaging 30: Vanguard T1 crossed at 12, next milestone is 45.
        Assert.AreEqual(45, BuffProgress.NextThresholdFor(Vanguard, 30, 0, 1));
        Assert.AreEqual(18f / 33f, BuffProgress.ToNextTier01(Vanguard, 30, 0, 1), 1e-4f);
        // Maxed out.
        Assert.AreEqual(0, BuffProgress.NextThresholdFor(Vanguard, 45, 0, 1));
        Assert.AreEqual(1f, BuffProgress.ToNextTier01(Vanguard, 45, 0, 1), 1e-4f);
        // Nothing banked: fill runs 0 -> 12.
        Assert.AreEqual(12, BuffProgress.NextThresholdFor(Vanguard, 0, 0, 1));
        Assert.AreEqual(0f, BuffProgress.ToNextTier01(Vanguard, 0, 0, 1), 1e-4f);
    }
}
