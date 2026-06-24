using UnityEngine;
using Game.Buffs.Core;

/// <summary>
/// Single project-wide registry + tuning. AllBuffs index is irrelevant to the network
/// (LoadoutOrder serializes BuffId), but every buff a player can equip must be listed here.
/// </summary>
[CreateAssetMenu(menuName = "Buffs/Loadout Config", fileName = "BuffLoadoutConfig")]
public class BuffLoadoutConfig : ScriptableObject
{
    [Header("Every equippable buff (one asset per buff)")]
    [SerializeField] private BuffDefinition[] allBuffs;

    [Header("Cumulative deposited-value thresholds for the 9 unlock steps")]
    [SerializeField] private int[] thresholds = { 5, 10, 15, 30, 45, 60, 120, 180, 240 };

    [Header("Default priority order if a player submits none")]
    [SerializeField] private BuffId[] defaultOrder = { BuffId.ExtraJump, BuffId.Stealth, BuffId.QuickerDash };

    public BuffDefinition[] AllBuffs => allBuffs;
    public int[] Thresholds => thresholds;
    public BuffId[] DefaultOrder => defaultOrder;
    public int BuffCount => allBuffs != null ? allBuffs.Length : 0;
    public int MaxTier => thresholds != null && BuffCount > 0 ? thresholds.Length / BuffCount : 3;

    public BuffDefinition GetById(BuffId id)
    {
        if (allBuffs == null) return null;
        for (int i = 0; i < allBuffs.Length; i++)
            if (allBuffs[i] != null && allBuffs[i].Id == id) return allBuffs[i];
        return null;
    }
}
