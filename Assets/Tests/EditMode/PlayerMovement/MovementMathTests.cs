using NUnit.Framework;
using Game.PlayerMovement.Core;

public class MovementMathTests
{
    // walkSpeed 5, accel 1.25/tick (4 ticks), decel ~1.667/tick (3 ticks), momentum decay 0.5/tick
    private static MoveParams Ground() => new MoveParams
    {
        WalkSpeed = 5f, AccelPerTick = 1.25f, DecelPerTick = 5f / 3f, MomentumDecayPerTick = 0.5f
    };

    [Test]
    public void Accelerates_Toward_Target_By_AccelPerTick()
    {
        float vx = MovementMath.StepHorizontalVelocity(0f, 1, Ground());
        Assert.AreEqual(1.25f, vx, 1e-4f);
    }

    [Test]
    public void Arrives_Exactly_At_Target_Without_Overshoot()
    {
        float vx = MovementMath.StepHorizontalVelocity(4.5f, 1, Ground());
        Assert.AreEqual(5f, vx, 1e-4f);
    }

    [Test]
    public void Decelerates_To_Zero_With_No_Input()
    {
        float vx = MovementMath.StepHorizontalVelocity(1f, 0, Ground());
        Assert.AreEqual(0f, vx, 1e-4f);
    }

    [Test]
    public void Counter_Input_Brakes_Through_Zero_At_Accel_Rate()
    {
        float vx = MovementMath.StepHorizontalVelocity(0.5f, -1, Ground());
        Assert.AreEqual(-0.75f, vx, 1e-4f);
    }

    [Test]
    public void OverSpeed_SameDirection_Input_Bleeds_By_MomentumDecay_Only()
    {
        float vx = MovementMath.StepHorizontalVelocity(9.75f, 1, Ground());
        Assert.AreEqual(9.25f, vx, 1e-4f);
    }

    [Test]
    public void OverSpeed_Neutral_Input_Bleeds_By_MomentumDecay_Only()
    {
        float vx = MovementMath.StepHorizontalVelocity(9.75f, 0, Ground());
        Assert.AreEqual(9.25f, vx, 1e-4f);
    }

    [Test]
    public void OverSpeed_Bleed_Floors_At_WalkSpeed()
    {
        float vx = MovementMath.StepHorizontalVelocity(5.2f, 1, Ground());
        Assert.AreEqual(5f, vx, 1e-4f);
    }

    [Test]
    public void OverSpeed_Counter_Input_Brakes_At_Accel_Rate_On_Full_Velocity()
    {
        // Braking is always effective: normal MoveToward, not the gentle momentum bleed.
        float vx = MovementMath.StepHorizontalVelocity(9.75f, -1, Ground());
        Assert.AreEqual(8.5f, vx, 1e-4f);
    }

    [Test]
    public void Negative_Direction_Is_Symmetric()
    {
        float vx = MovementMath.StepHorizontalVelocity(-9.75f, -1, Ground());
        Assert.AreEqual(-9.25f, vx, 1e-4f);

        vx = MovementMath.StepHorizontalVelocity(-0.5f, 1, Ground());
        Assert.AreEqual(0.75f, vx, 1e-4f);
    }

    [Test]
    public void At_Exactly_WalkSpeed_With_Input_Holds_WalkSpeed()
    {
        float vx = MovementMath.StepHorizontalVelocity(5f, 1, Ground());
        Assert.AreEqual(5f, vx, 1e-4f);
    }

    // ---- Gravity / fast-fall (apexThreshold 1.5, apexMult 0.5, fallMult 1.7) ----

    [Test]
    public void Gravity_Grounded_Is_Neutral()
    {
        Assert.AreEqual(1f, MovementMath.SelectGravityMultiplier(
            grounded: true, vy: 0f, apexThreshold: 1.5f,
            jumping: false, jumpCut: false, fastFalling: false,
            apexMultiplier: 0.5f, fallMultiplier: 1.7f), 1e-4f);
    }

    [Test]
    public void Gravity_Rising_Is_Neutral()
    {
        Assert.AreEqual(1f, MovementMath.SelectGravityMultiplier(
            false, 8f, 1.5f, true, false, false, 0.5f, 1.7f), 1e-4f);
    }

    [Test]
    public void Gravity_Rising_After_JumpCut_Uses_FallMultiplier()
    {
        Assert.AreEqual(1.7f, MovementMath.SelectGravityMultiplier(
            false, 8f, 1.5f, true, true, false, 0.5f, 1.7f), 1e-4f);
    }

    [Test]
    public void Gravity_Apex_Window_While_Jumping_Hangs()
    {
        Assert.AreEqual(0.5f, MovementMath.SelectGravityMultiplier(
            false, 0.5f, 1.5f, true, false, false, 0.5f, 1.7f), 1e-4f);
        Assert.AreEqual(0.5f, MovementMath.SelectGravityMultiplier(
            false, -1.4f, 1.5f, true, false, false, 0.5f, 1.7f), 1e-4f);
    }

    [Test]
    public void Gravity_Apex_Window_Not_Jumping_Falls()
    {
        // Walking off a ledge (vy ~0, Jumping false) must NOT hang.
        Assert.AreEqual(1.7f, MovementMath.SelectGravityMultiplier(
            false, 0f, 1.5f, false, false, false, 0.5f, 1.7f), 1e-4f);
    }

    [Test]
    public void Gravity_Apex_Window_FastFalling_Falls()
    {
        Assert.AreEqual(1.7f, MovementMath.SelectGravityMultiplier(
            false, -1f, 1.5f, true, false, true, 0.5f, 1.7f), 1e-4f);
    }

    [Test]
    public void Gravity_Below_Apex_Window_Falls()
    {
        Assert.AreEqual(1.7f, MovementMath.SelectGravityMultiplier(
            false, -3f, 1.5f, true, false, false, 0.5f, 1.7f), 1e-4f);
    }

    [Test]
    public void FastFall_Starts_Airborne_Past_Apex_On_Down()
    {
        Assert.IsTrue(MovementMath.ShouldStartFastFall(
            grounded: false, downPressed: true, vy: 0.5f, apexThreshold: 1.5f, alreadyFastFalling: false));
    }

    [Test]
    public void FastFall_Does_Not_Start_While_Rising_Fast()
    {
        Assert.IsFalse(MovementMath.ShouldStartFastFall(false, true, 8f, 1.5f, false));
    }

    [Test]
    public void FastFall_Does_Not_Start_Grounded_Or_Twice()
    {
        Assert.IsFalse(MovementMath.ShouldStartFastFall(true, true, 0f, 1.5f, false));
        Assert.IsFalse(MovementMath.ShouldStartFastFall(false, true, -5f, 1.5f, true));
    }

    [Test]
    public void ClampFallSpeed_Limits_Downward_Only()
    {
        Assert.AreEqual(-20f, MovementMath.ClampFallSpeed(-35f, 20f), 1e-4f);
        Assert.AreEqual(-5f, MovementMath.ClampFallSpeed(-5f, 20f), 1e-4f);
        Assert.AreEqual(12f, MovementMath.ClampFallSpeed(12f, 20f), 1e-4f);
    }
}
