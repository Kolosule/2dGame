using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class DirectionalMeleeConfigurationTests
{
    private const string PlayerPrefabPath = "Assets/Scripts/Player/PlayerPrefab.prefab";

    [Test]
    public void VerticalHitboxesAreRotatedCopiesOfSideAndKeepTheProjectileMuzzle()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.IsNotNull(prefab);
        var combat = new SerializedObject(FindComponent(prefab, "PlayerCombat"));
        Vector2 side = combat.FindProperty("sideAttackArea").vector2Value;
        Vector2 rotated = new Vector2(side.y, side.x);
        Assert.AreEqual(rotated, combat.FindProperty("upAttackArea").vector2Value);
        Assert.AreEqual(rotated, combat.FindProperty("downAttackArea").vector2Value);
        Assert.Greater(rotated.x, 0f);
        Assert.Greater(rotated.y, 0f);

        Transform sidePoint = Point(combat, "sideAttackPoint");
        Transform upPoint = Point(combat, "upAttackPoint");
        Transform downPoint = Point(combat, "downAttackPoint");
        Assert.AreEqual(new Vector3(0f, sidePoint.localPosition.x, 0f), upPoint.localPosition);
        Assert.AreEqual(-upPoint.localPosition, downPoint.localPosition);
        Assert.AreSame(sidePoint, combat.FindProperty("projectileSpawnPoint").objectReferenceValue);
        Assert.AreSame(prefab.transform, sidePoint.parent);
        Assert.AreSame(prefab.transform, upPoint.parent);
        Assert.AreSame(prefab.transform, downPoint.parent);
    }

    [Test]
    public void CombatUsesMovementGroundingAndSpikeMatchesTheFallSpeedBudget()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var combat = new SerializedObject(FindComponent(prefab, "PlayerCombat"));
        var movement = new SerializedObject(FindComponent(prefab, "PlayerMovement"));
        Assert.IsNull(combat.FindProperty("groundCheck"), "Do not reintroduce an independent ground probe.");
        Assert.IsNotNull(movement.FindProperty("groundCheck").objectReferenceValue);
        Assert.AreNotEqual(0, movement.FindProperty("groundLayer").intValue);

        float spikeSpeed = combat.FindProperty("downAttackSpikeSpeed").floatValue;
        var stats = new SerializedObject(movement.FindProperty("stats").objectReferenceValue);
        Assert.Greater(spikeSpeed, 0f);
        Assert.LessOrEqual(spikeSpeed, stats.FindProperty("maxFallSpeed").floatValue);
        Assert.IsNull(combat.FindProperty("useGroundPound"));
        Assert.IsNull(combat.FindProperty("groundPoundForce"));
    }

    [Test]
    public void WeaponFacingIsSeparateFromGameplayAttackPoints()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var animator = new SerializedObject(FindComponent(prefab, "PlayerAnimator"));
        Transform pivot = (Transform)animator.FindProperty("weaponFacingRoot").objectReferenceValue;
        Animator weapon = (Animator)animator.FindProperty("weaponAnim").objectReferenceValue;
        Animator body = (Animator)animator.FindProperty("anim").objectReferenceValue;
        Assert.IsNotNull(pivot);
        Assert.IsNotNull(weapon);
        Assert.IsNotNull(body);
        Assert.AreSame(prefab.transform, pivot.parent);
        Assert.AreSame(pivot, weapon.transform.parent);
        Assert.AreSame(prefab.transform, body.transform.parent);
        Assert.AreEqual(Vector3.zero, pivot.localPosition);
        Assert.AreEqual(Vector3.one, pivot.localScale);
        Assert.IsNull(animator.FindProperty("groundPoundDuration"));
    }

    private static Transform Point(SerializedObject combat, string name)
    {
        Transform point = (Transform)combat.FindProperty(name).objectReferenceValue;
        Assert.IsNotNull(point, name);
        return point;
    }

    private static Component FindComponent(GameObject gameObject, string name)
    {
        foreach (Component component in gameObject.GetComponents<Component>())
            if (component != null && component.GetType().FullName == name)
                return component;
        Assert.Fail("Missing component: " + name);
        return null;
    }
}
