using UnityEngine;
using Game.Buffs.Core;

/// <summary>
/// Passive. T1 +10% move speed while carrying the enemy flag, T2 +20%, T3 +20% and dashing is
/// permitted while carrying. T3 deliberately mirrors Stealth's T3 (UsableWhileCarryingFlag) so
/// "top tier lifts the flag restriction" reads the same way across the catalog.
/// </summary>
[CreateAssetMenu(menuName = "Buffs/Flag Runner", fileName = "FlagRunnerBuff")]
public class FlagRunnerBuffDefinition : BuffDefinition
{
    [Header("Move-speed multiplier while carrying the flag (index 0 = tier 1)")]
    [SerializeField] private float[] carrySpeedMultipliers = { 1.1f, 1.2f, 1.2f };

    [Header("Tier at which carrying the flag stops blocking dash")]
    [SerializeField] private int dashWhileCarryingFromTier = 3;

    public override void ContributeStats(ref EffectiveStats stats, int tierLevel)
    {
        if (tierLevel <= 0) return;
        int idx = Mathf.Clamp(tierLevel - 1, 0, carrySpeedMultipliers.Length - 1);
        stats.CarrySpeedMultiplier *= carrySpeedMultipliers[idx];
        if (tierLevel >= dashWhileCarryingFromTier) stats.CanDashWhileCarryingFlag = true;
    }
}
