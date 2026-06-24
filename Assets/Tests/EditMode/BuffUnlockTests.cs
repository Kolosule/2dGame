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
}
