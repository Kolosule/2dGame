using UnityEngine;
using Game.Buffs.Core;

/// <summary>
/// Single project-wide registry + tuning. Definitions are looked up by BuffId, not priority.
/// Every buff in the fixed BuffUpgradePath must have a definition here.
/// </summary>
[CreateAssetMenu(menuName = "Buffs/Loadout Config", fileName = "BuffLoadoutConfig")]
public class BuffLoadoutConfig : ScriptableObject
{
    [Header("Every approved buff (one asset per buff)")]
    [SerializeField] private BuffDefinition[] allBuffs;

    [Header("Cumulative deposited-value thresholds — exactly maxTier x buffCount entries, ascending")]
    [SerializeField] private int[] thresholds =
        { 5, 10, 16, 24, 34, 46, 62, 80, 110, 150, 200, 260 };

    [Header("Highest tier any buff can reach. AUTHORED, not derived — see IsCurveValid.")]
    [SerializeField] private int maxTier = 3;

    public BuffDefinition[] AllBuffs => allBuffs;
    public int[] Thresholds => thresholds;
    public int BuffCount => allBuffs != null ? allBuffs.Length : 0;
    public int MaxTier => maxTier;

    /// <summary>
    /// The threshold list must hold exactly one entry per (buff x tier) cell. Authoring a new
    /// buff without extending the curve used to silently demote every buff a whole tier; now it
    /// is a loud error instead.
    /// </summary>
    public bool IsCurveValid =>
        BuffUnlock.IsCurveComplete(thresholds != null ? thresholds.Length : 0, BuffCount, maxTier);

    [System.NonSerialized] private bool curveErrorLogged;

    /// <summary>Logs the curve error at most once per session (callers hit this per player spawn).</summary>
    public void LogIfCurveInvalid()
    {
        if (IsCurveValid || curveErrorLogged) return;
        curveErrorLogged = true;
        Debug.LogError(
            $"BuffLoadoutConfig '{name}': thresholds.Length ({(thresholds != null ? thresholds.Length : 0)}) " +
            $"must equal maxTier ({maxTier}) x buffCount ({BuffCount}). Every buff tier is wrong until this is fixed.");
    }

    private void OnValidate()
    {
        curveErrorLogged = false;
        LogIfCurveInvalid();
    }

    public BuffDefinition GetById(BuffId id)
    {
        if (allBuffs == null) return null;
        for (int i = 0; i < allBuffs.Length; i++)
            if (allBuffs[i] != null && allBuffs[i].Id == id) return allBuffs[i];
        return null;
    }
}
