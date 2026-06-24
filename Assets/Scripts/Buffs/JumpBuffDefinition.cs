using UnityEngine;
using Game.Buffs.Core;

/// <summary>Passive. T1 +1 air jump, T2 +2 air jumps, T3 unlimited air jumps.</summary>
[CreateAssetMenu(menuName = "Buffs/Extra Jump", fileName = "ExtraJumpBuff")]
public class JumpBuffDefinition : BuffDefinition
{
    [Header("Air jumps granted per tier (index 0 = tier 1)")]
    [SerializeField] private int[] bonusAirJumps = { 1, 2, 0 };
    [SerializeField] private int unlimitedAtTier = 3;

    public override void ContributeStats(ref EffectiveStats stats, int tierLevel)
    {
        if (tierLevel <= 0) return;
        if (tierLevel >= unlimitedAtTier) { stats.UnlimitedAirJumps = true; return; }
        int idx = Mathf.Clamp(tierLevel - 1, 0, bonusAirJumps.Length - 1);
        stats.BonusAirJumps += bonusAirJumps[idx];
    }
}
