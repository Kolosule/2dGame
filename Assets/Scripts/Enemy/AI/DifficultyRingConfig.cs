using UnityEngine;

/// <summary>
/// One concentric difficulty band. Bands are authored INNER -> OUTER
/// (ascending <see cref="maxDistanceFromCenter"/>). Multipliers apply to the
/// enemy's base stats at spawn; near-center bands are the toughest.
/// </summary>
[System.Serializable]
public struct RingTier
{
    [Tooltip("Upper bound (inclusive) of this band's distance-from-center range.")]
    public float maxDistanceFromCenter;
    public float healthMult;
    public float damageMult;
    public float speedMult;

    [Tooltip("Flat extra coins dropped by enemies in this band. INTEGER so total coin supply " +
             "stays exactly computable: total = kills x (coinsToDrop + coinDropBonus).")]
    public int coinDropBonus;

    /// <summary>Neutral 1.0x band used when no config/center is available.</summary>
    public static RingTier Identity => new RingTier
    {
        maxDistanceFromCenter = float.MaxValue,
        healthMult = 1f,
        damageMult = 1f,
        speedMult = 1f,
        coinDropBonus = 0
    };
}

/// <summary>
/// Shared, single-asset difficulty curve. Maps an enemy's distance from map
/// center to stat multipliers via discrete concentric rings.
/// </summary>
[CreateAssetMenu(fileName = "DifficultyRingConfig", menuName = "Enemy/Difficulty Ring Config")]
public class DifficultyRingConfig : ScriptableObject
{
    [Tooltip("Concentric bands, ordered INNER -> OUTER (ascending Max Distance From Center).")]
    public RingTier[] rings;

    /// <summary>
    /// Returns the multipliers for the given distance from center. Picks the first
    /// band whose maxDistanceFromCenter &gt;= distance (innermost match wins).
    /// Distances beyond the outermost band clamp to it. Empty config returns Identity.
    /// </summary>
    public RingTier GetRing(float distance)
    {
        if (rings == null || rings.Length == 0)
        {
            return RingTier.Identity;
        }

        for (int i = 0; i < rings.Length; i++)
        {
            if (distance <= rings[i].maxDistanceFromCenter)
            {
                return rings[i];
            }
        }

        // Beyond the outermost authored band: clamp to the outermost band.
        return rings[rings.Length - 1];
    }
}
