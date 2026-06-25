using UnityEngine;

/// <summary>
/// Pure leash math for zone-bound enemy AI. No Unity runtime dependencies beyond
/// Vector2, so it is unit-testable in EditMode.
/// </summary>
public static class EnemyAILeash
{
    /// <summary>
    /// Clamps a desired steer target so it never lies outside the leash circle
    /// (radius <paramref name="leashRadius"/> around <paramref name="home"/>).
    /// </summary>
    public static Vector2 ClampToLeash(Vector2 home, Vector2 target, float leashRadius)
    {
        Vector2 offset = target - home;
        if (offset.sqrMagnitude <= leashRadius * leashRadius)
        {
            return target;
        }
        return home + offset.normalized * leashRadius;
    }

    /// <summary>
    /// True when the enemy should stop chasing: the target has left detection
    /// range, or has moved outside the guarded zone (leash radius from home).
    /// </summary>
    public static bool ShouldDisengage(Vector2 enemyPos, Vector2 home, Vector2 target,
                                       float detectionRange, float leashRadius)
    {
        if ((target - enemyPos).sqrMagnitude > detectionRange * detectionRange)
        {
            return true;
        }
        if ((target - home).sqrMagnitude > leashRadius * leashRadius)
        {
            return true;
        }
        return false;
    }
}
