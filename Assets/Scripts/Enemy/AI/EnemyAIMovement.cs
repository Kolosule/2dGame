using UnityEngine;

/// <summary>
/// Pure movement math for zone-bound enemy AI. Isolates the flight-vs-ground velocity
/// decision so it is unit-testable in EditMode (no Rigidbody2D/MonoBehaviour needed),
/// mirroring <see cref="EnemyAILeash"/>.
/// </summary>
public static class EnemyAIMovement
{
    /// <summary>
    /// Velocity to steer toward <paramref name="target"/> at <paramref name="moveSpeed"/>.
    /// Flying enemies drive both axes; grounded enemies drive X only and keep their
    /// gravity-owned Y (<paramref name="currentVelocityY"/>).
    /// </summary>
    public static Vector2 SteeringVelocity(Vector2 from, Vector2 target, float moveSpeed,
                                           float currentVelocityY, bool canFly)
    {
        Vector2 direction = (target - from).normalized; // zero when from == target (no NaN)
        if (canFly)
        {
            return direction * moveSpeed;
        }
        return new Vector2(direction.x * moveSpeed, currentVelocityY);
    }

    /// <summary>
    /// Velocity that halts movement. Flying enemies stop on both axes (gravityScale 0 will
    /// not bleed off residual Y); grounded enemies keep their gravity-owned Y.
    /// </summary>
    public static Vector2 StopVelocity(float currentVelocityY, bool canFly)
    {
        return canFly ? Vector2.zero : new Vector2(0f, currentVelocityY);
    }
}
