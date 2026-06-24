using UnityEngine;
using Game.Buffs.Core;

public enum BuffKind { Passive, Active }

/// <summary>
/// One buff, described by a 3-entry tier table in the concrete subclass. Passive buffs
/// contribute to EffectiveStats; active buffs expose per-tier ActiveBuffParams. Adding a
/// buff = new subclass + asset added to BuffLoadoutConfig.AllBuffs. No core-loop edits.
/// </summary>
public abstract class BuffDefinition : ScriptableObject
{
    [SerializeField] private BuffId id;
    [SerializeField] private string displayName;
    [SerializeField] private Sprite icon;
    [SerializeField] private BuffKind kind;

    public BuffId Id => id;
    public string DisplayName => displayName;
    public Sprite Icon => icon;
    public BuffKind Kind => kind;

    /// <summary>Highest tier this buff defines (3 for all v1 buffs).</summary>
    public virtual int MaxTier => 3;

    /// <summary>Passive contribution. tierLevel 0 = locked (contribute nothing).</summary>
    public virtual void ContributeStats(ref EffectiveStats stats, int tierLevel) { }

    /// <summary>Active params at the given tier. Default: locked/zero.</summary>
    public virtual ActiveBuffParams GetActiveParams(int tierLevel) => default;
}
