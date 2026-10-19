using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Game.Combat.Core;

public class ShootInputBufferTests
{
    [Test]
    public void Holds_And_Repeated_Polls_Do_Not_Create_Extra_Edges()
    {
        var input = new ShootInputBuffer();
        input.SetHeld(true);
        input.SetHeld(true);
        Assert.IsTrue(input.Held);
        Assert.AreEqual(1u, input.Sequence);
        input.SetHeld(false);
        input.SetHeld(false);
        Assert.IsFalse(input.Held);
        Assert.AreEqual(2u, input.Sequence);
    }

    [Test]
    public void A_Complete_Tap_Retains_Both_Edges()
    {
        var input = new ShootInputBuffer();
        input.SetHeld(true);
        input.SetHeld(false);
        Assert.IsFalse(input.Held);
        Assert.AreEqual(2u, input.Sequence);
        var charge = new RangedCharge(true, 0, false, 0);
        Assert.AreEqual(0, charge.Sample(100, input.Sequence, input.Held));
        Assert.IsNull(charge.Sample(101, input.Sequence, input.Held));
    }

    [Test]
    public void Cancellation_Suppresses_A_Held_Button_Until_Released()
    {
        var input = new ShootInputBuffer();
        input.SetHeld(true);
        input.Cancel();
        input.SetHeld(true);
        Assert.IsFalse(input.Held);
        Assert.AreEqual(1u, input.Sequence);
        input.SetHeld(false);
        Assert.AreEqual(1u, input.Sequence);
        input.SetHeld(true);
        Assert.IsTrue(input.Held);
        Assert.AreEqual(2u, input.Sequence);
    }

    [Test]
    public void Cancellation_Baselines_An_Already_Released_Tap()
    {
        var input = new ShootInputBuffer();
        input.SetHeld(true);
        input.SetHeld(false);
        input.Cancel();
        var charge = new RangedCharge(true, 0, false, 0);
        charge.Cancel(input.Sequence);
        Assert.IsNull(charge.Sample(100, input.Sequence, input.Held));
        input.SetHeld(true);
        charge.Sample(101, input.Sequence, input.Held);
        input.SetHeld(false);
        Assert.AreEqual(1, charge.Sample(102, input.Sequence, input.Held));
    }

    [Test]
    public void Cancellation_Remains_In_Subsequent_Polls_Without_Repeated_Increments()
    {
        var input = new ShootInputBuffer();
        input.SetHeld(true);
        input.Cancel();
        input.Cancel();
        Assert.AreEqual(1u, input.CancellationSequence);
        input.SetHeld(false);
        input.SetHeld(true);
        Assert.AreEqual(1u, input.CancellationSequence);
        input.Cancel();
        Assert.AreEqual(2u, input.CancellationSequence);
    }
}

public class ShootInputBindingTests
{
    private GameObject owner;
    private InputAction action;
    private ShootInputBuffer buffer;
    private Mouse mouse;
    private Keyboard keyboard;
    private Gamepad gamepad;
    private InputSettings originalSettings;
    private InputSettings testSettings;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return new EnterPlayMode();
        originalSettings = InputSystem.settings;
        testSettings = UnityEngine.Object.Instantiate(originalSettings);
        testSettings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings = testSettings;
        mouse = InputSystem.AddDevice<Mouse>();
        keyboard = InputSystem.AddDevice<Keyboard>();
        gamepad = InputSystem.AddDevice<Gamepad>();
        Type providerType = Type.GetType("NetworkInputProvider, Assembly-CSharp");
        Assert.IsNotNull(providerType);
        owner = new GameObject("Shoot input test");
        Component provider = owner.AddComponent(providerType);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo actionField = providerType.GetField("shootAction", flags);
        action = (InputAction)actionField.GetValue(provider);
        Assert.IsNotNull(action);
        Assert.IsTrue(action.enabled);
        buffer = (ShootInputBuffer)providerType.GetField("shootInput", flags).GetValue(provider);
        InputSystem.Update();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
        if (mouse != null) InputSystem.RemoveDevice(mouse);
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        if (gamepad != null) InputSystem.RemoveDevice(gamepad);
        if (originalSettings != null) InputSystem.settings = originalSettings;
        if (testSettings != null) UnityEngine.Object.DestroyImmediate(testSettings);
        yield return new ExitPlayMode();
    }

    [TestCase("mouse")]
    [TestCase("keyboard")]
    [TestCase("gamepad")]
    public void Existing_Bindings_Charge_And_Release(string device)
    {
        ButtonControl button = device == "mouse" ? mouse.rightButton :
                               device == "keyboard" ? keyboard.leftAltKey : gamepad.buttonWest;
        Set(button, true);
        Assert.IsTrue(buffer.Held);
        Assert.AreEqual(1u, buffer.Sequence);
        Set(button, false);
        Assert.IsFalse(buffer.Held);
        Assert.AreEqual(2u, buffer.Sequence);
    }

    [Test]
    public void Press_And_Release_In_One_Input_Update_Are_Preserved()
    {
        Queue(mouse.rightButton, true);
        Queue(mouse.rightButton, false);
        InputSystem.Update();
        Assert.IsFalse(buffer.Held);
        Assert.AreEqual(2u, buffer.Sequence);
    }

    [Test]
    public void Releasing_One_Binding_Does_Not_Release_Another_Held_Binding()
    {
        Set(mouse.rightButton, true);
        Set(keyboard.leftAltKey, true);
        Set(mouse.rightButton, false);
        Assert.IsTrue(buffer.Held);
        Assert.AreEqual(1u, buffer.Sequence);
        Set(keyboard.leftAltKey, false);
        Assert.IsFalse(buffer.Held);
        Assert.AreEqual(2u, buffer.Sequence);
    }

    [Test]
    public void Release_And_Repress_In_One_Update_Are_Preserved()
    {
        Set(mouse.rightButton, true);
        Queue(mouse.rightButton, false);
        Queue(mouse.rightButton, true);
        InputSystem.Update();
        Assert.IsTrue(buffer.Held);
        Assert.AreEqual(3u, buffer.Sequence);
    }

    private static void Set(ButtonControl button, bool held)
    {
        Queue(button, held);
        InputSystem.Update();
    }

    private static void Queue(ButtonControl button, bool held)
    {
        using (StateEvent.From(button.device, out InputEventPtr eventPtr))
        {
            button.WriteValueIntoEvent(held ? 1f : 0f, eventPtr);
            InputSystem.QueueEvent(eventPtr);
        }
    }
}
