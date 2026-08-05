using System.Collections.Generic;
using NUnit.Framework;
using Game.Combat.Core;
using Game.Buffs.Core;

public class TerritorialCombatTests
{
    [Test]
    public void ReceivedMultiplier_AtOwnBase_IsAlwaysNeutral()
    {
        Assert.AreEqual(1.0f, TerritorialCombat.ReceivedMultiplier(0f, 0), 1e-4f);
        Assert.AreEqual(1.0f, TerritorialCombat.ReceivedMultiplier(0f, 1), 1e-4f);
        Assert.AreEqual(1.0f, TerritorialCombat.ReceivedMultiplier(0f, 2), 1e-4f);
    }

    // At max distance, tier 0 = full +150% malus (x2.5), tier 1 = half (x1.75), tier 2 = none (x1.0).
    [TestCase(0, 2.5f)]
    [TestCase(1, 1.75f)]
    [TestCase(2, 1.0f)]
    public void ReceivedMultiplier_AtMaxDistance_ScalesWithVanguardTier(int tier, float expected)
    {
        Assert.AreEqual(expected, TerritorialCombat.ReceivedMultiplier(1f, tier), 1e-4f);
    }

    [Test]
    public void ReceivedMultiplier_ScalesLinearlyWithDistance()
    {
        Assert.AreEqual(1.75f, TerritorialCombat.ReceivedMultiplier(0.5f, 0), 1e-4f);
        Assert.AreEqual(1.375f, TerritorialCombat.ReceivedMultiplier(0.5f, 1), 1e-4f);
        Assert.AreEqual(1.0f, TerritorialCombat.ReceivedMultiplier(0.5f, 2), 1e-4f);
    }

    [TestCase(-1f)]
    [TestCase(-0.5f)]
    public void ReceivedMultiplier_ClampsNegativeDistanceToZero(float distance01)
    {
        Assert.AreEqual(1.0f, TerritorialCombat.ReceivedMultiplier(distance01, 0), 1e-4f);
    }

    [TestCase(1.5f)]
    [TestCase(99f)]
    public void ReceivedMultiplier_ClampsOverlongDistanceToOne(float distance01)
    {
        Assert.AreEqual(2.5f, TerritorialCombat.ReceivedMultiplier(distance01, 0), 1e-4f);
    }

    [TestCase(-1)]
    [TestCase(3)]
    [TestCase(99)]
    public void ReceivedMultiplier_ClampsTierOutOfRange(int tier)
    {
        float value = TerritorialCombat.ReceivedMultiplier(1f, tier);
        Assert.GreaterOrEqual(value, 1.0f - 1e-4f);
        Assert.LessOrEqual(value, 2.5f + 1e-4f);
    }

    // End-to-end pacing: team score -> Vanguard tier -> damage TAKEN far from own base.
    // A 10-player team, so the {12, 45} per-player averages are absolute scores of 120 and 450.
    private static readonly List<int> VanguardThresholds = new List<int> { 12, 45 };

    [TestCase(0, 2.5f)]      // match start: full malus at max distance
    [TestCase(119, 2.5f)]    // just short of T1
    [TestCase(120, 1.75f)]   // T1: half the malus removed
    [TestCase(449, 1.75f)]
    [TestCase(450, 1.0f)]    // T2: malus fully removed
    [TestCase(550, 1.0f)]    // typical end-state (~55 per player)
    public void MaxDistanceDamageTaken_TracksTeamEconomy(int teamScore, float expectedMultiplier)
    {
        int tier = TeamBuffUnlock.TeamTier(VanguardThresholds, teamScore, rosterSize: 10, maxTier: 2);
        float received = TerritorialCombat.ReceivedMultiplier(1f, tier);
        Assert.AreEqual(expectedMultiplier, received, 1e-4f);
    }

    // Standing on your own base is never penalised, however poor the team's economy is.
    [TestCase(0)]
    [TestCase(550)]
    public void OwnBaseDamageTaken_IsAlwaysNeutral(int teamScore)
    {
        int tier = TeamBuffUnlock.TeamTier(VanguardThresholds, teamScore, rosterSize: 10, maxTier: 2);
        Assert.AreEqual(1.0f, TerritorialCombat.ReceivedMultiplier(0f, tier), 1e-4f);
    }
}
