using UnityEngine;
using Game.Buffs.Core;

/// <summary>
/// Passive(+on-dash), cumulative across tiers:
///   T1 +50% dash range (longer dash duration), T2 also halves dash cooldown,
///   T3 also deals melee damage in the front swing box while dashing.
/// </summary>
[CreateAssetMenu(menuName = "Buffs/Quicker Dash", fileName = "QuickerDashBuff")]
public class DashBuffDefinition : BuffDefinition
{
    [Header("Tier 1: dash range (range = dashSpeed x dashTime, so this extends dash duration)")]
    [SerializeField] private int rangeFromTier = 1;
    [SerializeField] private float rangeMultiplier = 1.5f;

    [Header("Tier 2: dash cooldown")]
    [SerializeField] private int cooldownFromTier = 2;
    [SerializeField] private float cooldownMultiplier = 0.5f;

    [Header("Tier 3: dash deals damage in front of the dasher")]
    [SerializeField] private int dashDamageFromTier = 3;

    public override void ContributeStats(ref EffectiveStats stats, int tierLevel)
    {
        if (tierLevel <= 0) return;
        if (tierLevel >= rangeFromTier) stats.DashTimeMultiplier *= rangeMultiplier;
        if (tierLevel >= cooldownFromTier) stats.DashCooldownMultiplier *= cooldownMultiplier;
        if (tierLevel >= dashDamageFromTier) stats.DashDealsDamage = true;
    }
}
