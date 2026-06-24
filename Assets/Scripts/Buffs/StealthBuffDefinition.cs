using UnityEngine;
using Game.Buffs.Core;

/// <summary>Active. T1 1s, T2 3s, T3 10s + usable while carrying the flag. Flat 20s cooldown.</summary>
[CreateAssetMenu(menuName = "Buffs/Stealth", fileName = "StealthBuff")]
public class StealthBuffDefinition : BuffDefinition
{
    [Header("Duration per tier (index 0 = tier 1)")]
    [SerializeField] private float[] durations = { 1f, 3f, 10f };
    [SerializeField] private float cooldown = 20f;
    [SerializeField] private int flagUsableFromTier = 3;

    public override ActiveBuffParams GetActiveParams(int tierLevel)
    {
        if (tierLevel <= 0) return default;
        int idx = Mathf.Clamp(tierLevel - 1, 0, durations.Length - 1);
        return new ActiveBuffParams
        {
            Unlocked = true,
            Duration = durations[idx],
            Cooldown = cooldown,
            UsableWhileCarryingFlag = tierLevel >= flagUsableFromTier,
        };
    }
}
