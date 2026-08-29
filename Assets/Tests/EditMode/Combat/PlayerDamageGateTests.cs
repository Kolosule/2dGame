using Game.Combat.Core;
using NUnit.Framework;

public class PlayerDamageGateTests
{
    [Test]
    public void LiveVulnerableAuthorityTargetAcceptsDamage()
    {
        Assert.AreEqual(
            DamageApplyResult.Applied,
            PlayerDamageGate.EvaluatePreCooldown(true, false, false));
    }

    [Test]
    public void NonAuthorityCannotApplyDamage()
    {
        Assert.AreEqual(
            DamageApplyResult.RejectedNoStateAuthority,
            PlayerDamageGate.EvaluatePreCooldown(false, false, false));
    }

    [Test]
    public void DeadPlayerRejectsDamage()
    {
        Assert.AreEqual(
            DamageApplyResult.RejectedDead,
            PlayerDamageGate.EvaluatePreCooldown(true, true, false));
    }

    [Test]
    public void SpawnImmunePlayerRejectsDamage()
    {
        Assert.AreEqual(
            DamageApplyResult.RejectedSpawnImmunity,
            PlayerDamageGate.EvaluatePreCooldown(true, false, true));
    }

    [TestCase(DamageApplyResult.RejectedNoStateAuthority)]
    [TestCase(DamageApplyResult.RejectedDead)]
    [TestCase(DamageApplyResult.RejectedSpawnImmunity)]
    [TestCase(DamageApplyResult.RejectedHitCooldown)]
    public void RejectedDamageCannotApplyKnockbackOrHitFeedback(DamageApplyResult result)
    {
        Assert.IsFalse(PlayerDamageGate.AllowsSecondaryEffects(result));
    }

    [Test]
    public void AcceptedHistoricalHitCanApplyCurrentBodyKnockback()
    {
        Assert.IsTrue(PlayerDamageGate.AllowsSecondaryEffects(DamageApplyResult.Applied));
    }
}
