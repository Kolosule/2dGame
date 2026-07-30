using System.Collections.Generic;
using NUnit.Framework;
using Game.Buffs.Core;

public class TeamBuffUnlockTests
{
    // PER-PLAYER-AVERAGE deposited value, not absolute team score.
    private static readonly List<int> Vanguard = new List<int> { 12, 45 };
    private const int MaxTier = 2;

    [TestCase(0, 10, 0)]
    [TestCase(119, 10, 11)]
    [TestCase(120, 10, 12)]
    [TestCase(55, 1, 55)]
    [TestCase(7, 2, 3)]     // integer floor, deterministic across peers
    [TestCase(100, 0, 0)]   // empty roster: no divide, no tier
    [TestCase(-5, 10, 0)]   // defensive: negative score never reads as progress
    public void PerPlayerAverage_FloorsAndGuardsEmptyRosters(int score, int roster, int expected)
    {
        Assert.AreEqual(expected, TeamBuffUnlock.PerPlayerAverage(score, roster));
    }

    // Solo player: the thresholds are literally the per-player numbers.
    [TestCase(0, 1, 0)]
    [TestCase(11, 1, 0)]
    [TestCase(12, 1, 1)]    // exact boundary unlocks
    [TestCase(44, 1, 1)]
    [TestCase(45, 1, 2)]    // exact boundary unlocks
    [TestCase(9999, 1, 2)]  // hard-capped at MaxTier
    public void TeamTier_SoloRoster(int score, int roster, int expected)
    {
        Assert.AreEqual(expected, TeamBuffUnlock.TeamTier(Vanguard, score, roster, MaxTier));
    }

    // 10-player team: 12 and 45 correspond to ABSOLUTE team scores of 120 and 450.
    // If these fail with tier 1/2 at scores like 12 and 45, the divisor was dropped.
    [TestCase(119, 10, 0)]
    [TestCase(120, 10, 1)]
    [TestCase(449, 10, 1)]
    [TestCase(450, 10, 2)]
    [TestCase(12, 10, 0)]   // the old failure mode: unlocked within seconds
    [TestCase(45, 10, 0)]
    public void TeamTier_TenPlayerRosterNormalises(int score, int roster, int expected)
    {
        Assert.AreEqual(expected, TeamBuffUnlock.TeamTier(Vanguard, score, roster, MaxTier));
    }

    [Test]
    public void TeamTier_EmptyRosterIsLocked()
    {
        Assert.AreEqual(0, TeamBuffUnlock.TeamTier(Vanguard, 1000, 0, MaxTier));
    }

    [Test]
    public void TeamTier_NullThresholdsIsLocked()
    {
        Assert.AreEqual(0, TeamBuffUnlock.TeamTier(null, 1000, 10, MaxTier));
    }

    // Expected pacing from the spec: typical play is around 55 per player, so a normal team
    // fully lifts the debuff around mid-match.
    [Test]
    public void TypicalTeamReachesTierTwo()
    {
        Assert.AreEqual(2, TeamBuffUnlock.TeamTier(Vanguard, 55 * 10, 10, MaxTier));
    }
}
