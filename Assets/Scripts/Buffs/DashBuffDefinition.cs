using UnityEngine;
using Game.Buffs.Core;

/// <summary>Passive(+on-dash). T1 cooldown x0.5, T2 cooldown x0, T3 also deals melee damage while dashing.</summary>
[CreateAssetMenu(menuName = "Buffs/Quicker Dash", fileName = "QuickerDashBuff")]
public class DashBuffDefinition : BuffDefinition
{
    [Header("Dash cooldown multiplier per tier (index 0 = tier 1)")]
    [SerializeField] private float[] cooldownMultipliers = { 0.5f, 0f, 0f };
    [SerializeField] private int dashDamageFromTier = 3;

    public override void ContributeStats(ref EffectiveStats stats, int tierLevel)
    {
        if (tierLevel <= 0) return;
        int idx = Mathf.Clamp(tierLevel - 1, 0, cooldownMultipliers.Length - 1);
        stats.DashCooldownMultiplier *= cooldownMultipliers[idx];
        if (tierLevel >= dashDamageFromTier) stats.DashDealsDamage = true;
    }
}
