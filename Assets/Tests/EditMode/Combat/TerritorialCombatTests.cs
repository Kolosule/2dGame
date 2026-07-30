using System.Collections.Generic;
using NUnit.Framework;
using Game.Combat.Core;
using Game.Buffs.Core;

public class TerritorialCombatTests
{
    // The boundary is the enemy THIRD, not the midline: advantage is +1 at own base,
    // -1 at the enemy base, 0 at the midpoint.
    [TestCase(1.0f, false)]    // own base
    [TestCase(0.0f, false)]    // midfield is clean and neutral
    [TestCase(-0.32f, false)]  // just outside the enemy third
    [TestCase(-0.33f, false)]  // exactly on the boundary: NOT debuffed (>= -0.33 is clear)
    [TestCase(-0.34f, true)]   // just inside the enemy third
    [TestCase(-1.0f, true)]    // enemy base
    public void InEnemyThird_SplitsAtMinusOneThird(float advantage, bool expected)
    {
        Assert.AreEqual(expected, TerritorialCombat.InEnemyThird(advantage));
    }

    // dealt = 1 - 0.67 * (1 - 0.5 * tier)  =>  0.33 / 0.665 / 1.00
    [TestCase(0, 0.33f)]
    [TestCase(1, 0.665f)]
    [TestCase(2, 1.0f)]
    public void DebuffWithVanguard_LiftsTheDebuffInHalves(int tier, float expected)
    {
        Assert.AreEqual(expected, TerritorialCombat.DebuffWithVanguard(tier), 1e-4f);
    }

    [TestCase(-1)]
    [TestCase(3)]
    [TestCase(99)]
    public void DebuffWithVanguard_ClampsTierOutOfRange(int tier)
    {
        float value = TerritorialCombat.DebuffWithVanguard(tier);
        Assert.GreaterOrEqual(value, 0.33f - 1e-4f);
        Assert.LessOrEqual(value, 1.0f + 1e-4f);
    }

    // Outside the enemy third nothing is applied, at any tier: the debuff is one-sided.
    [TestCase(0.5f, 0, 1.0f)]
    [TestCase(0.5f, 2, 1.0f)]
    [TestCase(-0.33f, 0, 1.0f)]
    [TestCase(-0.5f, 0, 0.33f)]
    [TestCase(-0.5f, 1, 0.665f)]
    [TestCase(-0.5f, 2, 1.0f)]
    public void DealtMultiplier_AppliesOnlyInsideTheEnemyThird(float advantage, int tier, float expected)
    {
        Assert.AreEqual(expected, TerritorialCombat.DealtMultiplier(advantage, tier), 1e-4f);
    }

    // The whole point of quantizing: the total swing is ~3x, not the old 9x.
    // FullDebuff is the display-friendly 0.33 (not the exact fraction 1/3), so the true
    // ratio is 1/0.33 = 3.0303..., ~0.03 off from a literal 3.0 -- hence the wider tolerance
    // than the 1e-4f used elsewhere in this file (deviation from brief's 1e-2f, which was
    // too tight for this constant; see tv-task-1-report.md).
    [Test]
    public void FullSwingIsThreeTimes()
    {
        float clear = TerritorialCombat.DealtMultiplier(0f, 0);
        float debuffed = TerritorialCombat.DealtMultiplier(-1f, 0);
        Assert.AreEqual(3.0f, clear / debuffed, 4e-2f);
    }

    // End-to-end pacing: team score -> Vanguard tier -> damage dealt deep in the enemy third.
    // A 10-player team, so the {12, 45} per-player averages are absolute scores of 120 and 450.
    private static readonly List<int> VanguardThresholds = new List<int> { 12, 45 };

    [TestCase(0, 0.33f)]     // match start: full debuff
    [TestCase(119, 0.33f)]   // just short of T1
    [TestCase(120, 0.665f)]  // T1: half the debuff removed
    [TestCase(449, 0.665f)]
    [TestCase(450, 1.0f)]    // T2: fully lifted
    [TestCase(550, 1.0f)]    // typical end-state (~55 per player)
    public void EnemyThirdDamage_TracksTeamEconomy(int teamScore, float expectedMultiplier)
    {
        int tier = TeamBuffUnlock.TeamTier(VanguardThresholds, teamScore, rosterSize: 10, maxTier: 2);
        float dealt = TerritorialCombat.DealtMultiplier(-0.8f, tier);
        Assert.AreEqual(expectedMultiplier, dealt, 1e-4f);
    }

    // Own half is never debuffed, however poor the team's economy is.
    [TestCase(0)]
    [TestCase(550)]
    public void OwnTerritoryDamage_IsAlwaysNeutral(int teamScore)
    {
        int tier = TeamBuffUnlock.TeamTier(VanguardThresholds, teamScore, rosterSize: 10, maxTier: 2);
        Assert.AreEqual(1.0f, TerritorialCombat.DealtMultiplier(0.5f, tier), 1e-4f);
    }
}
