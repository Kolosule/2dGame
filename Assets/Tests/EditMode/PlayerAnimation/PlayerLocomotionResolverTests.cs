using NUnit.Framework;
using Game.PlayerAnimation.Core;

public class PlayerLocomotionResolverTests
{
    private static LocomotionTuning Tuning => LocomotionTuning.Default;

    // ---- Resolve: grounded Idle/Walk with hysteresis ----

    [Test]
    public void GroundedAndSlow_ResolvesToIdle()
    {
        Assert.AreEqual(AnimState.Idle,
            LocomotionResolver.Resolve(AnimState.Idle, grounded: true, velocityX: 0.01f, velocityY: 0f, Tuning));
    }

    [Test]
    public void GroundedAndFast_ResolvesToWalk()
    {
        Assert.AreEqual(AnimState.Walk,
            LocomotionResolver.Resolve(AnimState.Idle, grounded: true, velocityX: 3f, velocityY: 0f, Tuning));
    }

    [Test]
    public void WalkDirectionIsIgnored_NegativeSpeedStillWalks()
    {
        Assert.AreEqual(AnimState.Walk,
            LocomotionResolver.Resolve(AnimState.Idle, grounded: true, velocityX: -3f, velocityY: 0f, Tuning));
    }

    [Test]
    public void InHysteresisBand_WhileWalking_StaysWalk()
    {
        // 0.10 is between WalkStopSpeed (0.05) and WalkEnterSpeed (0.15): a walker keeps walking.
        Assert.AreEqual(AnimState.Walk,
            LocomotionResolver.Resolve(AnimState.Walk, grounded: true, velocityX: 0.10f, velocityY: 0f, Tuning));
    }

    [Test]
    public void InHysteresisBand_WhileIdle_StaysIdle()
    {
        // Same 0.10 speed but coming from Idle: does not yet exceed the enter threshold, stays Idle.
        Assert.AreEqual(AnimState.Idle,
            LocomotionResolver.Resolve(AnimState.Idle, grounded: true, velocityX: 0.10f, velocityY: 0f, Tuning));
    }

    [Test]
    public void WalkingThenSpeedDropsBelowStop_ReturnsToIdle()
    {
        Assert.AreEqual(AnimState.Idle,
            LocomotionResolver.Resolve(AnimState.Walk, grounded: true, velocityX: 0.02f, velocityY: 0f, Tuning));
    }

    // ---- Resolve: airborne Jump/Fall ----

    [Test]
    public void AirborneAndRising_ResolvesToJump()
    {
        Assert.AreEqual(AnimState.Jump,
            LocomotionResolver.Resolve(AnimState.Idle, grounded: false, velocityX: 0f, velocityY: 5f, Tuning));
    }

    [Test]
    public void AirborneAndFalling_ResolvesToFall()
    {
        Assert.AreEqual(AnimState.Fall,
            LocomotionResolver.Resolve(AnimState.Jump, grounded: false, velocityX: 0f, velocityY: -5f, Tuning));
    }

    [Test]
    public void AtJumpApex_HoldsJumpPose_WhenPreviouslyJumping()
    {
        // Near-zero vertical speed but still airborne: keep the Jump pose rather than flipping to a
        // grounded pose. Prevents a one-frame Idle/Walk pop at the top of a jump.
        Assert.AreEqual(AnimState.Jump,
            LocomotionResolver.Resolve(AnimState.Jump, grounded: false, velocityX: 2f, velocityY: 0f, Tuning));
    }

    [Test]
    public void Airborne_NeverResolvesToAGroundedPose()
    {
        AnimState s = LocomotionResolver.Resolve(AnimState.Fall, grounded: false, velocityX: 4f, velocityY: 0.01f, Tuning);
        Assert.IsTrue(s == AnimState.Jump || s == AnimState.Fall);
    }

    // ---- Step: dwell gating on grounded Walk↔Idle ----

    [Test]
    public void Step_BriefWalkBlipUnderDwell_DoesNotCommit()
    {
        var r = new LocomotionResolver();
        // Prime as grounded Idle.
        r.Step(grounded: true, velocityX: 0f, velocityY: 0f, deltaSeconds: 0f, Tuning);
        // A single short frame of Walk-speed below the dwell time must not flip to Walk.
        AnimState s = r.Step(grounded: true, velocityX: 3f, velocityY: 0f, deltaSeconds: 0.02f, Tuning);
        Assert.AreEqual(AnimState.Idle, s);
    }

    [Test]
    public void Step_SustainedWalkPastDwell_Commits()
    {
        var r = new LocomotionResolver();
        r.Step(true, 0f, 0f, 0f, Tuning); // prime Idle
        r.Step(true, 3f, 0f, 0.03f, Tuning);
        AnimState s = r.Step(true, 3f, 0f, 0.03f, Tuning); // total 0.06 >= dwell
        Assert.AreEqual(AnimState.Walk, s);
    }

    [Test]
    public void Step_AirborneTransition_CommitsImmediately()
    {
        var r = new LocomotionResolver();
        r.Step(true, 0f, 0f, 0f, Tuning); // prime grounded Idle
        // Leaving the ground rising must show Jump on the very next frame (no dwell).
        AnimState s = r.Step(grounded: false, velocityX: 0f, velocityY: 5f, deltaSeconds: 0.016f, Tuning);
        Assert.AreEqual(AnimState.Jump, s);
    }

    [Test]
    public void Step_Landing_CommitsImmediately()
    {
        var r = new LocomotionResolver();
        r.Step(false, 0f, 5f, 0f, Tuning);   // prime airborne Jump
        AnimState s = r.Step(grounded: true, velocityX: 0f, velocityY: 0f, deltaSeconds: 0.016f, Tuning);
        Assert.AreEqual(AnimState.Idle, s); // air->ground is not a Walk<->Idle flip, so no dwell
    }
}
