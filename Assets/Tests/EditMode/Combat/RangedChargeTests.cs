using System;
using NUnit.Framework;
using Game.Combat.Core;

public class RangedChargeTests
{
    private static RangedCharge Ready() => new RangedCharge(true, 0, false, 0);

    [TestCase(-1, 5f)]
    [TestCase(0, 5f)]
    [TestCase(1, 5.2734375f)]
    [TestCase(64, 22.5f)]
    [TestCase(127, 39.7265625f)]
    [TestCase(128, 40f)]
    [TestCase(129, 40f)]
    [TestCase(640, 40f)]
    public void Speed_Reaches_Existing_Maximum_At_Two_Seconds(int ticks, float expected)
    {
        Assert.AreEqual(expected, RangedCharge.ResolveSpeed(ticks, 1f / 64f, 5f, 40f, 2f), 0.00001f);
    }

    [TestCase(32)]
    [TestCase(64)]
    [TestCase(128)]
    public void Charge_Uses_Simulation_Time_Not_Render_Frames(int tickRate)
    {
        Assert.AreEqual(22.5f, RangedCharge.ResolveSpeed(tickRate, 1f / tickRate, 5f, 40f, 2f));
        Assert.AreEqual(40f, RangedCharge.ResolveSpeed(tickRate * 2, 1f / tickRate, 5f, 40f, 2f));
    }

    [Test]
    public void Spawn_Baselines_Input_Instead_Of_Replaying_Old_Clicks()
    {
        var charge = new RangedCharge();
        Assert.IsNull(charge.Sample(100, 13, true));
        Assert.IsFalse(charge.IsCharging);
        Assert.IsNull(charge.Sample(101, 14, false));
        Assert.IsNull(charge.Sample(102, 15, true));
        Assert.IsTrue(charge.IsCharging);
    }

    [Test]
    public void Holding_Does_Not_Fire_Even_After_Full_Charge()
    {
        RangedCharge charge = Ready();
        Assert.IsNull(charge.Sample(100, 1, true));
        Assert.IsNull(charge.Sample(228, 1, true));
        Assert.IsNull(charge.Sample(740, 1, true));
        Assert.IsTrue(charge.IsCharging);
        Assert.AreEqual(100, charge.StartTick);
        Assert.AreEqual(640, charge.Sample(740, 2, false));
    }

    [Test]
    public void Release_Fires_Once_And_Clears_Charge()
    {
        RangedCharge charge = Ready();
        charge.Sample(100, 1, true);
        Assert.AreEqual(64, charge.Sample(164, 2, false));
        Assert.IsFalse(charge.IsCharging);
        Assert.AreEqual(0, charge.StartTick);
        Assert.IsNull(charge.Sample(165, 2, false));
    }

    [Test]
    public void Consecutive_Shots_Do_Not_Wait_For_A_Cooldown()
    {
        RangedCharge charge = Ready();
        charge.Sample(100, 1, true);
        Assert.AreEqual(1, charge.Sample(101, 2, false));
        charge.Sample(102, 3, true);
        Assert.AreEqual(1, charge.Sample(103, 4, false));
    }

    [Test]
    public void Complete_Tap_Between_Polls_Fires_Minimum_Charge_Once()
    {
        RangedCharge charge = Ready();
        Assert.AreEqual(0, charge.Sample(100, 2, false));
        Assert.IsFalse(charge.IsCharging);
        Assert.IsNull(charge.Sample(101, 2, false));
    }

    [Test]
    public void Release_And_Repress_Fires_Old_Charge_And_Starts_New_Charge()
    {
        RangedCharge charge = Ready();
        charge.Sample(100, 1, true);
        Assert.AreEqual(32, charge.Sample(132, 3, true));
        Assert.IsTrue(charge.IsCharging);
        Assert.AreEqual(132, charge.StartTick);
        Assert.AreEqual(32, charge.Sample(164, 4, false));
    }

    [Test]
    public void Tap_Then_Hold_Between_Polls_Fires_Tap_And_Starts_Hold()
    {
        RangedCharge charge = Ready();
        Assert.AreEqual(0, charge.Sample(100, 3, true));
        Assert.IsTrue(charge.IsCharging);
        Assert.AreEqual(100, charge.StartTick);
        Assert.AreEqual(64, charge.Sample(164, 4, false));
    }

    [Test]
    public void Cancel_With_Input_Requires_A_New_Press()
    {
        RangedCharge charge = Ready();
        charge.Sample(100, 1, true);
        charge.Cancel(1);
        Assert.IsFalse(charge.IsCharging);
        Assert.IsNull(charge.Sample(164, 1, true));
        Assert.IsNull(charge.Sample(165, 2, false));
        charge.Sample(166, 3, true);
        Assert.AreEqual(1, charge.Sample(167, 4, false));
    }

    [Test]
    public void Cancel_Without_Input_Discards_Stale_Edges_When_Input_Returns()
    {
        RangedCharge charge = Ready();
        charge.Sample(100, 1, true);
        charge.Cancel(null);
        Assert.IsFalse(charge.HasInput);
        Assert.IsNull(charge.Sample(300, 4, false));
        charge.Sample(301, 5, true);
        Assert.AreEqual(1, charge.Sample(302, 6, false));
    }

    [Test]
    public void Releasing_And_Repressing_A_Cancelled_Hold_Does_Not_Fire_The_Old_Charge()
    {
        RangedCharge charge = Ready();
        charge.Sample(100, 1, true);
        charge.Cancel(1);
        Assert.IsNull(charge.Sample(164, 3, true));
        Assert.IsTrue(charge.IsCharging);
        Assert.AreEqual(164, charge.StartTick);
        Assert.AreEqual(1, charge.Sample(165, 4, false));
    }

    [Test]
    public void Cancellation_Is_Retained_If_Its_First_Input_Sample_Is_Missing()
    {
        RangedCharge charge = Ready();
        charge.Sample(100, 1, true);
        Assert.IsNull(charge.Sample(300, 3, false, 1));
        Assert.IsFalse(charge.IsCharging);
        Assert.AreEqual(1u, charge.CancellationSequence);
        Assert.IsNull(charge.Sample(301, 3, false, 1));
        charge.Sample(302, 4, true, 1);
        Assert.AreEqual(1, charge.Sample(303, 5, false, 1));
    }

    [Test]
    public void Neutral_Button_State_Without_A_Release_Does_Not_Fire()
    {
        RangedCharge charge = Ready();
        charge.Sample(100, 1, true);
        Assert.IsNull(charge.Sample(110, 1, false));
        Assert.AreEqual(64, charge.Sample(164, 2, false));
    }

    [Test]
    public void Counter_Wrap_Preserves_Press_And_Release()
    {
        var charge = new RangedCharge(true, uint.MaxValue, false, 0);
        Assert.IsNull(charge.Sample(100, 0, true));
        Assert.AreEqual(64, charge.Sample(164, 1, false));
    }

    [Test]
    public void Tick_Zero_And_Tick_Wrap_Are_Valid_Charge_Starts()
    {
        RangedCharge charge = Ready();
        charge.Sample(0, 1, true);
        Assert.AreEqual(1, charge.Sample(1, 2, false));
        charge.Sample(int.MaxValue - 1, 3, true);
        Assert.AreEqual(3, charge.Sample(int.MinValue + 1, 4, false));
    }

    [Test]
    public void Rewinding_State_Reproduces_The_Same_Released_Speed()
    {
        RangedCharge charge = Ready();
        charge.Sample(100, 1, true);
        RangedCharge snapshot = charge;
        int? first = charge.Sample(164, 2, false);
        charge = snapshot;
        Assert.AreEqual(first, charge.Sample(164, 2, false));
        Assert.IsNull(charge.Sample(165, 2, false));
    }

    [TestCase(-1f, 40f, 2f)]
    [TestCase(41f, 40f, 2f)]
    [TestCase(0f, 0f, 2f)]
    [TestCase(5f, 40f, 0f)]
    [TestCase(5f, 40f, -1f)]
    [TestCase(float.NaN, 40f, 2f)]
    [TestCase(5f, float.PositiveInfinity, 2f)]
    [TestCase(5f, 40f, float.NaN)]
    public void Invalid_Tuning_Is_Explicitly_Rejected(float minimum, float maximum, float duration)
    {
        Assert.IsFalse(RangedCharge.IsConfigurationValid(minimum, maximum, duration));
        Assert.Throws<ArgumentException>(() => RangedCharge.ResolveSpeed(64, 1f / 64f, minimum, maximum, duration));
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    public void Invalid_Tick_Duration_Is_Explicitly_Rejected(float duration)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RangedCharge.ResolveSpeed(64, duration, 5f, 40f, 2f));
    }
}
