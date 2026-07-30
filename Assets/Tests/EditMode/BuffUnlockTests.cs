using System.Collections.Generic;
using NUnit.Framework;
using Game.Buffs.Core;

public class BuffUnlockTests
{
    private static readonly List<int> Thresholds =
        new List<int> { 5, 10, 15, 30, 45, 60, 120, 180, 240 };

    [TestCase(0, 0)]
    [TestCase(4, 0)]
    [TestCase(5, 1)]
    [TestCase(14, 2)]
    [TestCase(15, 3)]
    [TestCase(60, 6)]
    [TestCase(240, 9)]
    [TestCase(9999, 9)]
    public void UnlockedSteps_CountsThresholdsAtOrBelowTotal(int total, int expected)
    {
        Assert.AreEqual(expected, BuffUnlock.UnlockedSteps(Thresholds, total));
    }

    // Order [Jump=pos0, Stealth=pos1, Dash=pos2]. After 4 steps: Jump T2, Stealth T1, Dash T1.
    [TestCase(4, 0, 2)]
    [TestCase(4, 1, 1)]
    [TestCase(4, 2, 1)]
    [TestCase(1, 0, 1)]
    [TestCase(1, 1, 0)]
    [TestCase(0, 0, 0)]
    [TestCase(9, 0, 3)]
    [TestCase(9, 2, 3)]
    [TestCase(3, 2, 1)]
    public void TierLevel_RoundRobinsAcrossPriority(int steps, int position, int expected)
    {
        Assert.AreEqual(expected, BuffUnlock.TierLevel(steps, position, buffCount: 3, maxTier: 3));
    }

    [Test]
    public void TierLevel_ClampsToMaxTier()
    {
        Assert.AreEqual(3, BuffUnlock.TierLevel(9, 0, buffCount: 3, maxTier: 3));
    }

    // Sudden Death: every buff resolves to maxTier regardless of deposited value or position.
    [TestCase(0, 0)]
    [TestCase(0, 2)]
    [TestCase(9, 0)]
    [TestCase(4, 1)]
    public void ResolveTier_AllUnlocked_ReturnsMaxTierRegardlessOfStepsOrPosition(int steps, int position)
    {
        Assert.AreEqual(3, BuffUnlock.ResolveTier(steps, position, buffCount: 3, maxTier: 3,
                                                  allUnlocked: true));
    }

    // Not Sudden Death: identical to TierLevel, so normal play is untouched.
    [TestCase(4, 0, 2)]
    [TestCase(4, 1, 1)]
    [TestCase(1, 1, 0)]
    [TestCase(0, 0, 0)]
    [TestCase(9, 2, 3)]
    public void ResolveTier_NotUnlocked_MatchesTierLevel(int steps, int position, int expected)
    {
        Assert.AreEqual(expected, BuffUnlock.ResolveTier(steps, position, buffCount: 3, maxTier: 3,
                                                         allUnlocked: false));
        Assert.AreEqual(BuffUnlock.TierLevel(steps, position, 3, 3),
                        BuffUnlock.ResolveTier(steps, position, 3, 3, allUnlocked: false));
    }

    // A misconfigured maxTier must not produce a negative tier under the override.
    [Test]
    public void ResolveTier_AllUnlocked_ClampsNonPositiveMaxTierToZero()
    {
        Assert.AreEqual(0, BuffUnlock.ResolveTier(5, 0, buffCount: 3, maxTier: 0, allUnlocked: true));
        Assert.AreEqual(0, BuffUnlock.ResolveTier(5, 0, buffCount: 3, maxTier: -2, allUnlocked: true));
    }

    // A buff with no loadout (buffCount <= 0) has no tier to force to max, all-unlocked or not:
    // the allUnlocked branch must honour the same buffCount <= 0 guard TierLevel already has.
    [Test]
    public void ResolveTier_AllUnlocked_HonoursBuffCountGuard()
    {
        Assert.AreEqual(0, BuffUnlock.ResolveTier(5, 0, buffCount: 0, maxTier: 3, allUnlocked: true));
    }

    // The curve must contain exactly one threshold per (buff x tier) cell. Anything else
    // silently mis-tiers every buff, which is the footgun this rule exists to catch.
    [TestCase(9, 3, 3, true)]    // the pre-Flag-Runner catalog
    [TestCase(12, 4, 3, true)]   // the four-buff catalog
    [TestCase(9, 4, 3, false)]   // 4th buff added, thresholds forgotten — the exact footgun
    [TestCase(13, 4, 3, false)]  // one threshold too many
    [TestCase(0, 0, 3, false)]   // no buffs authored
    [TestCase(0, 4, 0, false)]   // maxTier left at zero (an unserialized field deserializes to 0)
    public void IsCurveComplete_RequiresOneThresholdPerBuffPerTier(
        int thresholdCount, int buffCount, int maxTier, bool expected)
    {
        Assert.AreEqual(expected, BuffUnlock.IsCurveComplete(thresholdCount, buffCount, maxTier));
    }
}
