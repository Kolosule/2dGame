using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

public class RangedChargeConfigurationTests
{
    private const string PlayerPrefabPath = "Assets/Scripts/Player/PlayerPrefab.prefab";

    [Test]
    public void Player_Prefab_Preserves_Maximum_Speed_And_Old_Cooldown_As_Charge_Time()
    {
        Component combat = LoadCombat();
        var serialized = new SerializedObject(combat);
        Assert.AreEqual(5f, serialized.FindProperty("projectileMinimumSpeed").floatValue);
        Assert.AreEqual(40f, serialized.FindProperty("projectileSpeed").floatValue);
        Assert.AreEqual(2f, serialized.FindProperty("projectileChargeTime").floatValue);
        Assert.AreEqual(15, serialized.FindProperty("projectileDamage").intValue);
        Assert.AreEqual(1f, serialized.FindProperty("projectileScale").floatValue);
        Assert.IsNotNull(serialized.FindProperty("projectilePrefab").objectReferenceValue);
        Assert.IsNotNull(serialized.FindProperty("projectileSpawnPoint").objectReferenceValue);
        Assert.IsNull(serialized.FindProperty("projectileCooldown"));
    }

    [Test]
    public void Existing_Serialized_Cooldown_Overrides_Migrate_To_Charge_Time()
    {
        FieldInfo field = LoadCombat().GetType().GetField(
            "projectileChargeTime", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        object[] attributes = field.GetCustomAttributes(typeof(FormerlySerializedAsAttribute), false);
        Assert.IsTrue(Array.Exists(attributes,
            attribute => ((FormerlySerializedAsAttribute)attribute).oldName == "projectileCooldown"));
    }

    private static Component LoadCombat()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.IsNotNull(prefab);
        Component combat = prefab.GetComponent("PlayerCombat");
        Assert.IsNotNull(combat);
        return combat;
    }
}
