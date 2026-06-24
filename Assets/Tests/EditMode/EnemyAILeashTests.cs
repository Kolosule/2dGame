using NUnit.Framework;
using UnityEngine;

public class EnemyAILeashTests
{
    [Test]
    public void ClampToLeash_TargetInsideLeash_ReturnsTarget()
    {
        var home = Vector2.zero;
        var target = new Vector2(3f, 0f);
        var result = EnemyAILeash.ClampToLeash(home, target, 5f);
        Assert.AreEqual(target, result);
    }

    [Test]
    public void ClampToLeash_TargetBeyondLeash_ReturnsPointOnCircle()
    {
        var home = Vector2.zero;
        var target = new Vector2(10f, 0f);
        var result = EnemyAILeash.ClampToLeash(home, target, 5f);
        Assert.AreEqual(5f, result.x, 0.0001f);
        Assert.AreEqual(0f, result.y, 0.0001f);
    }

    [Test]
    public void ShouldDisengage_TargetWithinBothRanges_False()
    {
        var enemy = new Vector2(1f, 0f);
        var home = Vector2.zero;
        var target = new Vector2(2f, 0f);
        Assert.IsFalse(EnemyAILeash.ShouldDisengage(enemy, home, target, detectionRange: 10f, leashRadius: 8f));
    }

    [Test]
    public void ShouldDisengage_TargetBeyondDetection_True()
    {
        var enemy = new Vector2(1f, 0f);
        var home = Vector2.zero;
        var target = new Vector2(20f, 0f);
        Assert.IsTrue(EnemyAILeash.ShouldDisengage(enemy, home, target, detectionRange: 10f, leashRadius: 100f));
    }

    [Test]
    public void ShouldDisengage_TargetBeyondLeashFromHome_True()
    {
        var enemy = new Vector2(7f, 0f);
        var home = Vector2.zero;
        var target = new Vector2(9f, 0f); // close to enemy, but beyond leash from home
        Assert.IsTrue(EnemyAILeash.ShouldDisengage(enemy, home, target, detectionRange: 10f, leashRadius: 8f));
    }
}
