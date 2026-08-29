using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class LagCompensationConfigurationTests
{
    private const string PlayerPrefabPath = "Assets/Scripts/Player/PlayerPrefab.prefab";
    private const string CombatConfigPath =
        "Assets/Scripts/ScriptableObjects/CombatConfig.asset";

    [Test]
    public void PlayerPrefabHasOneThinFusionBodyHitboxAndKeepsBox2D()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.IsNotNull(prefab);

        BoxCollider2D body2D = prefab.GetComponent<BoxCollider2D>();
        Assert.IsNotNull(body2D, "Box2D movement/collision must remain intact.");

        Component hitboxRoot = FindComponent(prefab, "Fusion.HitboxRoot");
        Component hitbox = FindComponent(prefab, "Fusion.Hitbox");
        Assert.IsNotNull(hitboxRoot);
        Assert.IsNotNull(hitbox);
        Assert.AreEqual(1, CountComponents(prefab, "Fusion.HitboxRoot"));
        Assert.AreEqual(1, CountComponents(prefab, "Fusion.Hitbox"));
        Assert.AreEqual(LayerMask.NameToLayer("Player"), hitbox.gameObject.layer);

        var hitboxSerialized = new SerializedObject(hitbox);
        Vector3 offset = hitboxSerialized.FindProperty("Offset").vector3Value;
        Vector3 extents = hitboxSerialized.FindProperty("BoxExtents").vector3Value;
        Assert.AreEqual(1, hitboxSerialized.FindProperty("Type").enumValueIndex);
        Assert.AreEqual(body2D.offset.x, offset.x, 0.0001f);
        Assert.AreEqual(body2D.offset.y, offset.y, 0.0001f);
        Assert.AreEqual(body2D.size.x * 0.5f, extents.x, 0.0001f);
        Assert.AreEqual(body2D.size.y * 0.5f, extents.y, 0.0001f);
        Assert.Greater(extents.z, 0f);
        Assert.LessOrEqual(extents.z, 0.25f, "The 3D representation must stay thin.");

        var rootSerialized = new SerializedObject(hitboxRoot);
        Vector3 rootOffset = rootSerialized.FindProperty("Offset").vector3Value;
        float broadRadius = rootSerialized.FindProperty("BroadRadius").floatValue;
        SerializedProperty hitboxes = rootSerialized.FindProperty("Hitboxes");
        Assert.AreEqual(offset.x, rootOffset.x, 0.0001f);
        Assert.AreEqual(offset.y, rootOffset.y, 0.0001f);
        Assert.GreaterOrEqual(broadRadius, extents.magnitude);
        Assert.AreEqual(1, hitboxes.arraySize);
        Assert.AreSame(hitbox, hitboxes.GetArrayElementAtIndex(0).objectReferenceValue);

        Component networkObject = FindComponent(prefab, "Fusion.NetworkObject");
        var networkSerialized = new SerializedObject(networkObject);
        Assert.IsTrue(
            ArrayContains(
                networkSerialized.FindProperty("NetworkedBehaviours"), hitboxRoot),
            "HitboxRoot must be baked into the NetworkObject behaviour list.");
    }

    [Test]
    public void CombatConfigDefaultsToSafeFallbackWithQuietDiagnostics()
    {
        ScriptableObject config =
            AssetDatabase.LoadAssetAtPath<ScriptableObject>(CombatConfigPath);
        Assert.IsNotNull(config);

        var serialized = new SerializedObject(config);
        Assert.IsFalse(serialized.FindProperty("enableLagCompensation").boolValue);
        Assert.IsFalse(serialized.FindProperty("logLagCompensationDiagnostics").boolValue);
        Assert.GreaterOrEqual(
            serialized.FindProperty("lagCompensationDiagnosticInterval").floatValue, 5f);
    }

    [Test]
    public void FusionHistoryMatchesDocumentedPlayerBudget()
    {
        string path = Path.Combine(
            Application.dataPath,
            "Photon/Fusion/Resources/NetworkProjectConfig.fusion");
        string config = File.ReadAllText(path);

        StringAssert.Contains("\"Enabled\": true", config);
        StringAssert.Contains("\"HitboxBufferLengthInMs\": 200", config);
        StringAssert.Contains("\"HitboxDefaultCapacity\": 32", config);
        StringAssert.Contains("\"CachedStaticCollidersSize\": 0", config);
    }

    private static Component FindComponent(GameObject gameObject, string fullTypeName)
    {
        foreach (Component component in gameObject.GetComponents<Component>())
        {
            if (component != null && component.GetType().FullName == fullTypeName)
                return component;
        }
        return null;
    }

    private static int CountComponents(GameObject gameObject, string fullTypeName)
    {
        int count = 0;
        foreach (Component component in gameObject.GetComponents<Component>())
        {
            if (component != null && component.GetType().FullName == fullTypeName)
                count++;
        }
        return count;
    }

    private static bool ArrayContains(SerializedProperty array, Component expected)
    {
        for (int i = 0; i < array.arraySize; i++)
        {
            if (array.GetArrayElementAtIndex(i).objectReferenceValue == expected)
                return true;
        }
        return false;
    }
}
