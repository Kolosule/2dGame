using UnityEngine;
using Game.Buffs.Core;

/// <summary>
/// Read-only effective-stats view: base PlayerStats SO combined with the player's current buff
/// tiers from PlayerBuffs. NEVER mutates the shared SO. PlayerMovement/PlayerCombat read through
/// this; if PlayerBuffs is absent it returns the unbuffed base values.
/// </summary>
public class PlayerStatModifiers : MonoBehaviour
{
    [SerializeField] private PlayerStats stats;
    [Tooltip("Air-jump count reported when a buff grants unlimited air jumps.")]
    [SerializeField] private int unlimitedAirJumpSentinel = 99;

    private PlayerBuffs buffs;

    private void Awake()
    {
        buffs = GetComponent<PlayerBuffs>();
        if (stats == null) Debug.LogError("PlayerStatModifiers: PlayerStats not assigned.");
    }

    private EffectiveStats Current()
    {
        EffectiveStats es = EffectiveStats.Default();
        if (buffs != null) buffs.BuildEffectiveStats(ref es);
        return es;
    }

    public bool UnlimitedAirJumps => Current().UnlimitedAirJumps;

    public int EffectiveMaxAirJumps
    {
        get
        {
            EffectiveStats es = Current();
            return es.UnlimitedAirJumps ? unlimitedAirJumpSentinel : stats.maxAirJumps + es.BonusAirJumps;
        }
    }

    public float EffectiveDashCooldown => stats.dashCooldown * Current().DashCooldownMultiplier;
    public float EffectiveDashTime => stats.dashTime * Current().DashTimeMultiplier;
    public bool DashDealsDamage => Current().DashDealsDamage;

    /// <summary>Walk speed for this tick. The carry bonus applies ONLY while carrying the flag.</summary>
    public float EffectiveWalkSpeed(bool carryingFlag) =>
        carryingFlag ? stats.walkSpeed * Current().CarrySpeedMultiplier : stats.walkSpeed;

    public bool CanDashWhileCarryingFlag => Current().CanDashWhileCarryingFlag;
}
