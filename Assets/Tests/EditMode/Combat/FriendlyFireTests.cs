using NUnit.Framework;
using Game.Combat.Core;
using UnityEditor;
using UnityEngine;

public class FriendlyFireTests
{
    [TestCase(1, 1)]
    [TestCase(2, 2)]
    public void SameTeam_CannotDamage(int attacker, int defender)
    {
        Assert.IsFalse(FriendlyFire.CanDamagePlayer(attacker, defender, isSelf: false));
    }

    [TestCase(1, 2)]
    [TestCase(2, 1)]
    public void OpposingHumanTeams_CanDamage(int attacker, int defender)
    {
        Assert.IsTrue(FriendlyFire.CanDamagePlayer(attacker, defender, isSelf: false));
    }

    [TestCase(3, 1)]
    [TestCase(1, 3)]
    [TestCase(3, 2)]
    [TestCase(2, 3)]
    public void AiTeamVsHumanTeam_CanDamage(int attacker, int defender)
    {
        Assert.IsTrue(FriendlyFire.CanDamagePlayer(attacker, defender, isSelf: false));
    }

    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(0, 0)]
    public void UnassignedTeamOnEitherSide_CannotDamage(int attacker, int defender)
    {
        Assert.IsFalse(FriendlyFire.CanDamagePlayer(attacker, defender, isSelf: false));
    }

    [TestCase(1, 1)]
    [TestCase(1, 2)]
    [TestCase(2, 1)]
    public void Self_CannotDamage_RegardlessOfTeam(int attacker, int defender)
    {
        Assert.IsTrue(!FriendlyFire.CanDamagePlayer(attacker, defender, isSelf: true));
    }

    [TestCase(1, 1, true)]
    [TestCase(2, 2, true)]
    [TestCase(1, 2, false)]
    [TestCase(2, 1, false)]
    [TestCase(0, 1, false)]
    [TestCase(1, 0, false)]
    public void FriendlyCollision_OnlyIgnoresAssignedTeammates(int first, int second, bool expected)
    {
        Assert.AreEqual(expected, FriendlyCollisionRules.ShouldIgnore(first, second));
    }

    [Test]
    public void TeamChangeTracker_ReportsInitialAndLaterAssignmentsOnce()
    {
        var tracker = new TeamChangeTracker();

        Assert.IsFalse(tracker.Observe(0), "Unassigned state must not trigger collision setup.");
        Assert.IsTrue(tracker.Observe(1), "Initial team assignment must trigger collision setup.");
        Assert.IsFalse(tracker.Observe(1), "Unchanged team must not repeat collision setup.");
        Assert.IsTrue(tracker.Observe(2), "A later team change must trigger collision setup.");
        Assert.IsFalse(tracker.Observe(2), "The new stable team must not repeat collision setup.");
    }

    [Test]
    public void PlayerPrefab_SeparatesCombatPointsFromWeaponAnimation()
    {
        const string prefabPath = "Assets/Scripts/Player/PlayerPrefab.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        Assert.IsNotNull(prefab, $"Missing player prefab at {prefabPath}.");

        Transform attackPoint = prefab.transform.Find("SideAttackTransform");
        Transform weaponVisual = prefab.transform.Find("WeaponVisual");
        Assert.IsNotNull(attackPoint);
        Assert.IsNotNull(weaponVisual);
        Assert.IsNull(attackPoint.GetComponent<Animator>(),
            "Authoritative combat geometry must not be driven by the weapon Animator.");
        Assert.IsNotNull(weaponVisual.GetComponent<Animator>());
        Assert.AreEqual(attackPoint.localPosition, weaponVisual.localPosition);
        Assert.AreEqual(attackPoint.localRotation, weaponVisual.localRotation);
        Assert.AreEqual(attackPoint.localScale, weaponVisual.localScale);

        UnityEngine.Object sideAttackPoint = null;
        UnityEngine.Object projectileSpawnPoint = null;
        foreach (MonoBehaviour behaviour in prefab.GetComponents<MonoBehaviour>())
        {
            if (behaviour == null) continue;
            var serialized = new SerializedObject(behaviour);
            SerializedProperty side = serialized.FindProperty("sideAttackPoint");
            SerializedProperty projectile = serialized.FindProperty("projectileSpawnPoint");
            if (side == null || projectile == null) continue;

            sideAttackPoint = side.objectReferenceValue;
            projectileSpawnPoint = projectile.objectReferenceValue;
            break;
        }

        Assert.IsNotNull(sideAttackPoint);
        Assert.IsNotNull(projectileSpawnPoint);
        Assert.AreSame(attackPoint, sideAttackPoint);
        Assert.AreSame(attackPoint, projectileSpawnPoint);
    }
}
