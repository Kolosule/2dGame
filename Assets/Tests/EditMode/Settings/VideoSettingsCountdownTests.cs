using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class VideoSettingsCountdownTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private const string UntouchedText = "assignment marker";

    private readonly Dictionary<FieldInfo, object> originalStoreFields =
        new Dictionary<FieldInfo, object>();

    private Type sectionType;
    private Type storeType;
    private Type textType;
    private PropertyInfo textProperty;
    private GameObject root;
    private GameObject confirmPanel;
    private Component section;
    private Component label;

    [OneTimeSetUp]
    public void FindPresentationMembers()
    {
        // Reuse the existing test assembly without adding references to Assembly-CSharp or TMP.
        sectionType = Type.GetType("VideoSettingsSection, Assembly-CSharp", true);
        storeType = Type.GetType("SettingsStore, Assembly-CSharp", true);
        textType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro", true);
        textProperty = textType.GetProperty("text");
    }

    [SetUp]
    public void CreateSection()
    {
        // Seed only memory, as in PlayerIdentityTests. Zero-sized previews/reverts do not change
        // the editor's display, and null controls ensure Keep never writes real PlayerPrefs.
        SeedStoreField("loaded", true);
        SeedStoreField("resolutionWidth", 0);
        SeedStoreField("resolutionHeight", 0);
        SeedStoreField("displayMode", (int)FullScreenMode.Windowed);

        root = new GameObject(nameof(VideoSettingsCountdownTests));
        root.SetActive(false);
        section = root.AddComponent(sectionType);
        confirmPanel = new GameObject("Confirm panel", typeof(RectTransform));
        confirmPanel.transform.SetParent(root.transform, false);
        confirmPanel.SetActive(false);
        var labelObject = new GameObject("Countdown", typeof(RectTransform));
        labelObject.transform.SetParent(confirmPanel.transform, false);
        label = labelObject.AddComponent(textType);
        SetField("confirmPanel", confirmPanel);
        SetField("confirmCountdownLabel", label);
    }

    [TearDown]
    public void RestoreState()
    {
        UnityEngine.Object.DestroyImmediate(root);
        foreach (var entry in originalStoreFields)
            entry.Key.SetValue(null, entry.Value);
        originalStoreFields.Clear();
    }

    [TestCase(10f, "Keep these display settings? Reverting in 10s")]
    [TestCase(1.01f, "Keep these display settings? Reverting in 2s")]
    [TestCase(0.01f, "Keep these display settings? Reverting in 1s")]
    [TestCase(0f, "Keep these display settings? Reverting in 0s")]
    [TestCase(-0.01f, "Keep these display settings? Reverting in 0s")]
    public void BeginConfirm_DisplaysCorrectTextImmediately(float seconds, string expected)
    {
        BeginCountdown(seconds);

        Assert.IsTrue(confirmPanel.activeSelf);
        Assert.IsTrue(GetField<bool>("awaitingConfirm"));
        Assert.AreEqual(expected, GetText());
    }

    [Test]
    public void UnchangedSecond_DoesNotAssignText()
    {
        BeginCountdown(2.9f);
        // TMP itself ignores equal-text assignments; a marker exposes redundant assignments.
        SetText(UntouchedText);
        SetField("confirmRemaining", 2.1f);

        Invoke("UpdateConfirmCountdownLabel");

        Assert.AreEqual(UntouchedText, GetText());
    }

    [Test]
    public void SecondBoundary_UpdatesText()
    {
        BeginCountdown(2.01f);
        SetField("confirmRemaining", 2f);

        Invoke("UpdateConfirmCountdownLabel");

        Assert.AreEqual("Keep these display settings? Reverting in 2s", GetText());
    }

    [Test]
    public void RestartWhilePending_RefreshesMatchingSecondAndPreservesRevertTarget()
    {
        BeginCountdown(10f);
        SetText(UntouchedText);
        SetField("confirmRemaining", 9.1f);
        storeType.GetField("displayMode", PrivateStatic)
            .SetValue(null, (int)FullScreenMode.FullScreenWindow);

        BeginCountdown(10f);

        Assert.AreEqual(10f, GetField<float>("confirmRemaining"));
        Assert.AreEqual((int)FullScreenMode.Windowed, GetField<int>("previousDisplayMode"));
        Assert.AreEqual("Keep these display settings? Reverting in 10s", GetText());
    }

    [TestCase("KeepPendingDisplay")]
    [TestCase("CancelPendingConfirm")]
    [TestCase("OnDisable")]
    public void ReopenAfterDismissal_RefreshesMatchingSecond(string dismissMethod)
    {
        BeginCountdown(10f);

        Invoke(dismissMethod);

        Assert.IsFalse(GetField<bool>("awaitingConfirm"));
        Assert.IsFalse(confirmPanel.activeSelf);
        float remaining = GetField<float>("confirmRemaining");
        Invoke("Update");
        Assert.AreEqual(remaining, GetField<float>("confirmRemaining"));

        SetText(UntouchedText);
        BeginCountdown(10f);

        Assert.IsTrue(GetField<bool>("awaitingConfirm"));
        Assert.IsTrue(confirmPanel.activeSelf);
        Assert.AreEqual("Keep these display settings? Reverting in 10s", GetText());
    }

    [Test]
    public void UnchangedSecond_StillAdvancesUnscaledCountdown()
    {
        BeginCountdown(6f);
        float before = 5.25f + Time.unscaledDeltaTime;
        SetField("confirmRemaining", before);
        SetText(UntouchedText);

        Invoke("Update");

        Assert.AreEqual(before - Time.unscaledDeltaTime, GetField<float>("confirmRemaining"));
        Assert.IsTrue(GetField<bool>("awaitingConfirm"));
        Assert.AreEqual(UntouchedText, GetText());
    }

    [Test]
    public void ExpiredCountdown_RevertsEvenWhenDisplayedZeroIsUnchanged()
    {
        BeginCountdown(0f);
        SetText(UntouchedText);

        Invoke("Update");

        Assert.LessOrEqual(GetField<float>("confirmRemaining"), 0f);
        Assert.IsFalse(GetField<bool>("awaitingConfirm"));
        Assert.IsFalse(confirmPanel.activeSelf);
        Assert.AreEqual(UntouchedText, GetText());
        Invoke("OnDisable");
        Assert.IsFalse(GetField<bool>("awaitingConfirm"));
    }

    [Test]
    public void ExpiredCountdown_WithoutLabel_StillReverts()
    {
        SetField("confirmCountdownLabel", null);
        BeginCountdown(0f);

        Invoke("Update");

        Assert.IsFalse(GetField<bool>("awaitingConfirm"));
        Assert.IsFalse(confirmPanel.activeSelf);
    }

    private void BeginCountdown(float seconds)
    {
        SetField("confirmSeconds", seconds);
        sectionType.GetMethod("BeginConfirm", PrivateInstance)
            .Invoke(section, new object[] { 0, 0, (int)FullScreenMode.Windowed });
    }

    private void Invoke(string method)
    {
        sectionType.GetMethod(method, PrivateInstance | BindingFlags.Public).Invoke(section, null);
    }

    private void SetField(string field, object value)
    {
        sectionType.GetField(field, PrivateInstance).SetValue(section, value);
    }

    private T GetField<T>(string field)
    {
        return (T)sectionType.GetField(field, PrivateInstance).GetValue(section);
    }

    private void SeedStoreField(string name, object value)
    {
        FieldInfo field = storeType.GetField(name, PrivateStatic);
        originalStoreFields.Add(field, field.GetValue(null));
        field.SetValue(null, value);
    }

    private string GetText() => (string)textProperty.GetValue(label);

    private void SetText(string text) => textProperty.SetValue(label, text);
}
