using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Game.Buffs.Core;
using UnityEditor;
using UnityEngine;

public class BuffUpgradePathTests
{
    private static readonly int[] Thresholds =
        { 5, 10, 16, 24, 34, 46, 62, 80, 110, 150, 200, 260 };

    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void Order_IsJumpDashInvisibilityThenFlagCarrying()
    {
        CollectionAssert.AreEqual(new[]
        {
            BuffId.ExtraJump, BuffId.QuickerDash, BuffId.Stealth, BuffId.FlagRunner
        }, BuffUpgradePath.Order);
    }

    [Test]
    public void Order_CannotBeMutatedThroughACollectionCast()
    {
        var collection = (IList<BuffId>)BuffUpgradePath.Order;
        Assert.IsTrue(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => collection[0] = BuffId.FlagRunner);
        Assert.Throws<NotSupportedException>(() => collection.Add(BuffId.FlagRunner));
        Assert.AreEqual(BuffId.ExtraJump, BuffUpgradePath.Order[0]);
        Assert.AreEqual(4, BuffUpgradePath.Order.Count);
    }

    [TestCase(BuffId.ExtraJump, 0)]
    [TestCase(BuffId.QuickerDash, 1)]
    [TestCase(BuffId.Stealth, 2)]
    [TestCase(BuffId.FlagRunner, 3)]
    [TestCase((BuffId)255, -1)]
    public void PositionOf_UsesApprovedPriority_NotSerializedId(BuffId id, int expected)
    {
        Assert.AreEqual(expected, BuffUpgradePath.PositionOf(id));
    }

    [TestCase(0, 0, 0, 0, 0)]
    [TestCase(4, 0, 0, 0, 0)]
    [TestCase(5, 1, 0, 0, 0)]
    [TestCase(9, 1, 0, 0, 0)]
    [TestCase(10, 1, 1, 0, 0)]
    [TestCase(15, 1, 1, 0, 0)]
    [TestCase(16, 1, 1, 1, 0)]
    [TestCase(23, 1, 1, 1, 0)]
    [TestCase(24, 1, 1, 1, 1)]
    [TestCase(33, 1, 1, 1, 1)]
    [TestCase(34, 2, 1, 1, 1)]
    [TestCase(45, 2, 1, 1, 1)]
    [TestCase(46, 2, 2, 1, 1)]
    [TestCase(61, 2, 2, 1, 1)]
    [TestCase(62, 2, 2, 2, 1)]
    [TestCase(79, 2, 2, 2, 1)]
    [TestCase(80, 2, 2, 2, 2)]
    [TestCase(109, 2, 2, 2, 2)]
    [TestCase(110, 3, 2, 2, 2)]
    [TestCase(149, 3, 2, 2, 2)]
    [TestCase(150, 3, 3, 2, 2)]
    [TestCase(199, 3, 3, 2, 2)]
    [TestCase(200, 3, 3, 3, 2)]
    [TestCase(259, 3, 3, 3, 2)]
    [TestCase(260, 3, 3, 3, 3)]
    [TestCase(int.MaxValue, 3, 3, 3, 3)]
    public void Tiers_FollowApprovedPathAtEveryThreshold(
        int deposited, int jump, int dash, int invisibility, int flagCarrying)
    {
        int steps = BuffUnlock.UnlockedSteps(Thresholds, deposited);
        var expected = new[] { jump, dash, invisibility, flagCarrying };
        for (int i = 0; i < BuffUpgradePath.Order.Count; i++)
        {
            BuffId id = BuffUpgradePath.Order[i];
            Assert.AreEqual(expected[i], BuffUnlock.ResolveTier(
                steps, BuffUpgradePath.PositionOf(id), BuffUpgradePath.Order.Count, 3, false), id.ToString());
        }
    }

    [TestCase(0, 5, 10, 16, 24)]
    [TestCase(10, 34, 46, 16, 24)]
    [TestCase(24, 34, 46, 62, 80)]
    [TestCase(80, 110, 150, 200, 260)]
    [TestCase(260, 0, 0, 0, 0)]
    public void NextUnlockThresholds_FollowTheSamePath(
        int deposited, int jump, int dash, int invisibility, int flagCarrying)
    {
        var expected = new[] { jump, dash, invisibility, flagCarrying };
        for (int i = 0; i < BuffUpgradePath.Order.Count; i++)
        {
            BuffId id = BuffUpgradePath.Order[i];
            Assert.AreEqual(expected[i], BuffProgress.NextThresholdFor(
                Thresholds, deposited, BuffUpgradePath.PositionOf(id), BuffUpgradePath.Order.Count), id.ToString());
        }
    }

    [Test]
    public void SuddenDeath_StillMaxesEveryApprovedBuff()
    {
        foreach (BuffId id in BuffUpgradePath.Order)
            Assert.AreEqual(3, BuffUnlock.ResolveTier(
                0, BuffUpgradePath.PositionOf(id), BuffUpgradePath.Order.Count, 3, true), id.ToString());
    }

    [Test]
    public void ShippedConfig_HasAllApprovedBuffsAndUnchangedThresholds_WithoutAnEditableOrder()
    {
        ScriptableObject config = LoadConfig();
        var serialized = new SerializedObject(config);
        Assert.IsNull(serialized.FindProperty("defaultOrder"));

        SerializedProperty definitions = serialized.FindProperty("allBuffs");
        var ids = new List<BuffId>();
        for (int i = 0; i < definitions.arraySize; i++)
        {
            UnityEngine.Object definition = definitions.GetArrayElementAtIndex(i).objectReferenceValue;
            Assert.IsNotNull(definition);
            ids.Add((BuffId)definition.GetType().GetProperty("Id").GetValue(definition));
        }
        CollectionAssert.AreEquivalent(BuffUpgradePath.Order, ids);
        CollectionAssert.AreEqual(Thresholds, (int[])config.GetType().GetProperty("Thresholds").GetValue(config));
        Assert.AreEqual(3, config.GetType().GetProperty("MaxTier").GetValue(config));
        Assert.AreEqual(true, config.GetType().GetProperty("IsCurveValid").GetValue(config));
    }

    [Test]
    public void Lobby_ShowsApprovedOrder_AndHidesAllLegacyReorderControls()
    {
        var root = new GameObject(nameof(BuffUpgradePathTests));
        root.SetActive(false);
        try
        {
            Type lobbyType = Type.GetType("LobbyScreenUI, Assembly-CSharp", true);
            Component lobby = root.AddComponent(lobbyType);
            lobbyType.GetField("buffConfig", InstanceMembers).SetValue(lobby, LoadConfig());
            Type labelType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro", true);
            Array labels = CreateControls(lobby, "slotLabels", 5, labelType);
            Array ups = CreateControls(lobby, "slotUpButtons", 4);
            Array downs = CreateControls(lobby, "slotDownButtons", 4);

            lobbyType.GetMethod("Start", InstanceMembers).Invoke(lobby, null);

            var expected = new[] { "1. Jump Buffs", "2. Quicker Dash", "3. Stealth", "4. Flag Runner", "" };
            for (int i = 0; i < labels.Length; i++)
            {
                object label = labels.GetValue(i);
                Assert.AreEqual(expected[i], label.GetType().GetProperty("text").GetValue(label));
            }
            foreach (Array buttons in new[] { ups, downs })
            {
                foreach (Component button in buttons)
                {
                    Assert.IsFalse(button.gameObject.activeSelf);
                    Assert.AreEqual(false, button.GetType().GetProperty("interactable").GetValue(button));
                }
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static ScriptableObject LoadConfig()
    {
        var config = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Settings/Buffs/BuffLoadoutConfig.asset");
        Assert.IsNotNull(config);
        return config;
    }

    private static Array CreateControls(Component owner, string fieldName, int count, Type componentType = null)
    {
        FieldInfo field = owner.GetType().GetField(fieldName, InstanceMembers);
        Type elementType = field.FieldType.GetElementType();
        Array controls = Array.CreateInstance(elementType, count);
        for (int i = 0; i < count; i++)
        {
            var child = new GameObject($"{fieldName}{i}", typeof(RectTransform));
            child.transform.SetParent(owner.transform, false);
            controls.SetValue(child.AddComponent(componentType ?? elementType), i);
        }
        field.SetValue(owner, controls);
        return controls;
    }
}
