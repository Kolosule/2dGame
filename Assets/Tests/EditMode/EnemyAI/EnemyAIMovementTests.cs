using NUnit.Framework;
using UnityEngine;

public class EnemyAIMovementTests
{
    [Test]
    public void SteeringVelocity_Flying_DrivesBothAxesTowardTarget()
    {
        // dir to (3,4) from origin is (0.6, 0.8); at speed 10 => (6, 8).
        var result = EnemyAIMovement.SteeringVelocity(
            from: Vector2.zero, target: new Vector2(3f, 4f),
            moveSpeed: 10f, currentVelocityY: 99f, canFly: true);

        Assert.AreEqual(6f, result.x, 0.0001f);
        Assert.AreEqual(8f, result.y, 0.0001f, "flying must drive Y toward the target, ignoring current Y");
    }

    [Test]
    public void SteeringVelocity_Grounded_DrivesXOnly_PreservesY()
    {
        var result = EnemyAIMovement.SteeringVelocity(
            from: Vector2.zero, target: new Vector2(3f, 4f),
            moveSpeed: 10f, currentVelocityY: -7f, canFly: false);

        Assert.AreEqual(6f, result.x, 0.0001f);
        Assert.AreEqual(-7f, result.y, 0.0001f, "grounded must preserve the gravity-owned Y velocity");
    }

    [Test]
    public void SteeringVelocity_Grounded_NegativeDirection()
    {
        var result = EnemyAIMovement.SteeringVelocity(
            from: Vector2.zero, target: new Vector2(-1f, 0f),
            moveSpeed: 5f, currentVelocityY: 2f, canFly: false);

        Assert.AreEqual(-5f, result.x, 0.0001f);
        Assert.AreEqual(2f, result.y, 0.0001f);
    }

    [Test]
    public void SteeringVelocity_ZeroLength_NoNaN()
    {
        var flying = EnemyAIMovement.SteeringVelocity(
            from: new Vector2(2f, 2f), target: new Vector2(2f, 2f),
            moveSpeed: 10f, currentVelocityY: 3f, canFly: true);

        Assert.AreEqual(0f, flying.x, 0.0001f);
        Assert.AreEqual(0f, flying.y, 0.0001f);
        Assert.IsFalse(float.IsNaN(flying.x) || float.IsNaN(flying.y));
    }

    [Test]
    public void StopVelocity_Flying_ReturnsZero()
    {
        var result = EnemyAIMovement.StopVelocity(currentVelocityY: 5f, canFly: true);
        Assert.AreEqual(Vector2.zero, result, "flyer must stop on both axes (gravity 0 won't bleed off Y)");
    }

    [Test]
    public void StopVelocity_Grounded_PreservesY()
    {
        var result = EnemyAIMovement.StopVelocity(currentVelocityY: 5f, canFly: false);
        Assert.AreEqual(0f, result.x, 0.0001f);
        Assert.AreEqual(5f, result.y, 0.0001f);
    }
}
