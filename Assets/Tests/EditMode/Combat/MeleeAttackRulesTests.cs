using System;
using Game.Combat.Core;
using NUnit.Framework;

public class MeleeAttackRulesTests
{
    [TestCase(0, true, true)]
    [TestCase(0, false, true)]
    [TestCase(1, true, true)]
    [TestCase(1, false, true)]
    [TestCase(-1, true, false)]
    [TestCase(-1, false, true)]
    public void OnlyDownRequiresAirborne(int aim, bool grounded, bool expected)
    {
        Assert.AreEqual(expected, MeleeAttackRules.CanStart(aim, grounded));
    }

    [TestCase(100)]
    [TestCase(103)]
    [TestCase(107)]
    public void LandingCancelsDownHitsWithoutChangingSwingLifetime(int landingTick)
    {
        bool cancelled = MeleeAttackRules.CancelDownHits(-1, true, false);
        Assert.IsTrue(cancelled);
        Assert.IsTrue(MeleeAttackRules.CancelDownHits(-1, false, cancelled),
            "Leaving ground again must not reactivate the same swing.");
        Assert.AreNotEqual(SwingPhaseKind.None, SwingPhase.Resolve(landingTick, 100, 3, 5, 10));
        Assert.AreEqual(SwingPhaseKind.Recovery, SwingPhase.Resolve(117, 100, 3, 5, 10));
        Assert.AreEqual(SwingPhaseKind.None, SwingPhase.Resolve(118, 100, 3, 5, 10));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void LandingDoesNotCancelSideOrUp(int aim)
    {
        Assert.IsFalse(MeleeAttackRules.CancelDownHits(aim, true, false));
    }

    [TestCase(25f, -20f)]
    [TestCase(0f, -20f)]
    [TestCase(-5f, -20f)]
    [TestCase(-30f, -30f)]
    public void SpikeImmediatelyDrivesDownAndNeverSlowsAFasterFall(float current, float expected)
    {
        Assert.AreEqual(expected, MeleeAttackRules.SpikeVelocity(current, 20f));
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void InvalidSpikeSpeedIsNotSilentlyAccepted(float speed)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MeleeAttackRules.SpikeVelocity(0f, speed));
    }
}
