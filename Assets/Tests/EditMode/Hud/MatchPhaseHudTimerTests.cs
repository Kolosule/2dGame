using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class MatchPhaseHudTimerTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string UntouchedText = "assignment marker";

    private Type hudType;
    private Type textType;
    private Type phaseType;
    private MethodInfo updateTimerText;
    private PropertyInfo textProperty;
    private GameObject root;
    private Component hud;
    private Component countdownText;
    private Component matchTimerText;
    private Component returnCountdownText;

    [OneTimeSetUp]
    public void FindPresentationMembers()
    {
        // The existing test asmdef cannot reference Assembly-CSharp or its TMP/Fusion dependencies.
        hudType = Type.GetType("MatchPhaseHud, Assembly-CSharp", true);
        textType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro", true);
        textProperty = textType.GetProperty("text");
        updateTimerText = hudType.GetMethod("UpdateTimerText", PrivateInstance);
        Assert.IsNotNull(updateTimerText);
        phaseType = updateTimerText.GetParameters()[0].ParameterType;
    }

    [SetUp]
    public void CreateHud()
    {
        root = new GameObject(nameof(MatchPhaseHudTimerTests));
        root.SetActive(false);
        hud = root.AddComponent(hudType);
        countdownText = CreateLabel("countdownText");
        matchTimerText = CreateLabel("matchTimerText");
        returnCountdownText = CreateLabel("returnCountdownText");
        SetField("matchTimerRoot", matchTimerText.gameObject);
    }

    [TearDown]
    public void DestroyHud()
    {
        UnityEngine.Object.DestroyImmediate(root);
    }

    [TestCase("Countdown", 3f, "3")]
    [TestCase("Countdown", 3.01f, "4")]
    [TestCase("Countdown", 0.01f, "1")]
    [TestCase("Countdown", 0f, "0")]
    [TestCase("Countdown", -0.01f, "0")]
    [TestCase("Live", 61.01f, "1:02")]
    [TestCase("Live", 60f, "1:00")]
    [TestCase("Live", 0.01f, "0:01")]
    [TestCase("Live", 0f, "0:00")]
    [TestCase("Live", -0.01f, "0:00")]
    [TestCase("Live", 3600f, "60:00")]
    [TestCase("SuddenDeath", 59.01f, "1:00")]
    [TestCase("PostMatch", 20f, "Returning to lobby in 20\u2026")]
    [TestCase("PostMatch", 0.01f, "Returning to lobby in 1\u2026")]
    [TestCase("PostMatch", 0f, "Returning to lobby in 0\u2026")]
    [TestCase("PostMatch", -0.01f, "Returning to lobby in 0\u2026")]
    public void FirstDisplay_PreservesRoundingClampingAndFormatting(
        string phase, float remaining, string expected)
    {
        ShowTimer(phase, remaining);

        Assert.AreEqual(expected, GetText(LabelFor(phase)));
    }

    [TestCase("Countdown", "0")]
    [TestCase("PostMatch", "Returning to lobby in 0\u2026")]
    public void MissingCountdownTimer_DisplaysZero(string phase, string expected)
    {
        ShowTimer(phase, null);

        Assert.AreEqual(expected, GetText(LabelFor(phase)));
    }

    [TestCase("Live")]
    [TestCase("SuddenDeath")]
    public void MissingClockTimer_HidesClockWithoutChangingText(string phase)
    {
        SetText(matchTimerText, UntouchedText);

        ShowTimer(phase, null);

        Assert.IsFalse(matchTimerText.gameObject.activeSelf);
        Assert.AreEqual(UntouchedText, GetText(matchTimerText));
    }

    [TestCase("Countdown")]
    [TestCase("Live")]
    [TestCase("SuddenDeath")]
    [TestCase("PostMatch")]
    public void UnchangedSecond_DoesNotAssignText(string phase)
    {
        ShowTimer(phase, 2.9f);
        Component label = LabelFor(phase);
        // TMP itself ignores equal-text assignments; a marker makes redundant assignments observable.
        SetText(label, UntouchedText);

        ShowTimer(phase, 2.1f);

        Assert.AreEqual(UntouchedText, GetText(label));
    }

    [TestCase("Countdown", "2")]
    [TestCase("Live", "0:02")]
    [TestCase("SuddenDeath", "0:02")]
    [TestCase("PostMatch", "Returning to lobby in 2\u2026")]
    public void SecondBoundary_UpdatesText(string phase, string expected)
    {
        ShowTimer(phase, 2.01f);

        ShowTimer(phase, 2f);

        Assert.AreEqual(expected, GetText(LabelFor(phase)));
    }

    [Test]
    public void EqualSeconds_OnDifferentLabels_UpdateIndependently()
    {
        ShowTimer("Countdown", 2.1f);
        ShowTimer("Live", 2.1f);
        ShowTimer("PostMatch", 2.1f);

        Assert.AreEqual("3", GetText(countdownText));
        Assert.AreEqual("0:03", GetText(matchTimerText));
        Assert.AreEqual("Returning to lobby in 3\u2026", GetText(returnCountdownText));
    }

    [TestCase("Render")]
    [TestCase("OnEnable")]
    public void PhaseBindingOrEnableRefresh_InvalidatesAllLabelCaches(string refreshMethod)
    {
        ShowTimer("Countdown", 2.1f);
        ShowTimer("Live", 2.1f);
        ShowTimer("PostMatch", 2.1f);
        SetText(countdownText, "Get ready\u2026");
        SetText(matchTimerText, UntouchedText);
        SetText(returnCountdownText, UntouchedText);

        // Render is the shared phase-change/rebinding callback. Keep Fusion unbound for this
        // local presentation test, then supply the next timer snapshot directly.
        Invoke(refreshMethod);
        ShowTimer("Countdown", 2.1f);
        ShowTimer("SuddenDeath", 2.1f);
        ShowTimer("PostMatch", 2.1f);

        Assert.AreEqual("3", GetText(countdownText));
        Assert.AreEqual("0:03", GetText(matchTimerText));
        Assert.AreEqual("Returning to lobby in 3\u2026", GetText(returnCountdownText));
    }

    [TestCase("Live")]
    [TestCase("SuddenDeath")]
    public void ClockRestart_AfterMissingTimer_RefreshesMatchingSecond(string phase)
    {
        ShowTimer(phase, 2.1f);
        ShowTimer(phase, null);
        SetText(matchTimerText, UntouchedText);

        ShowTimer(phase, 2.1f);

        Assert.IsTrue(matchTimerText.gameObject.activeSelf);
        Assert.AreEqual("0:03", GetText(matchTimerText));
    }

    [TestCase("Live")]
    [TestCase("SuddenDeath")]
    public void UnchangedClockSecond_StillEvaluatesVisibility(string phase)
    {
        ShowTimer(phase, 2.9f);
        matchTimerText.gameObject.SetActive(false);
        SetText(matchTimerText, UntouchedText);

        ShowTimer(phase, 2.1f);

        Assert.IsTrue(matchTimerText.gameObject.activeSelf);
        Assert.AreEqual(UntouchedText, GetText(matchTimerText));
    }

    private Component CreateLabel(string fieldName)
    {
        var gameObject = new GameObject(fieldName, typeof(RectTransform));
        gameObject.transform.SetParent(root.transform, false);
        Component label = gameObject.AddComponent(textType);
        SetField(fieldName, label);
        return label;
    }

    private Component LabelFor(string phase)
    {
        if (phase == "Countdown") return countdownText;
        if (phase == "PostMatch") return returnCountdownText;
        return matchTimerText;
    }

    private void ShowTimer(string phase, float? remaining)
    {
        updateTimerText.Invoke(hud, new object[] { Enum.Parse(phaseType, phase), remaining });
    }

    private void Invoke(string method)
    {
        hudType.GetMethod(method, PrivateInstance).Invoke(hud, null);
    }

    private void SetField(string field, object value)
    {
        hudType.GetField(field, PrivateInstance).SetValue(hud, value);
    }

    private string GetText(Component label) => (string)textProperty.GetValue(label);

    private void SetText(Component label, string text) => textProperty.SetValue(label, text);
}
