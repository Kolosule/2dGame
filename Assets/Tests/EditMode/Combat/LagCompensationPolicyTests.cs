using Game.Combat.Core;
using NUnit.Framework;

public class LagCompensationPolicyTests
{
    [Test]
    public void EnabledWithHistoryAndAuthorityUsesHistoricalPlayers()
    {
        Assert.AreEqual(
            PlayerHitQueryMode.Historical,
            LagCompensationPolicy.Resolve(true, true, true));
    }

    [TestCase(false, true, true)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(false, false, false)]
    public void MissingRequirementUsesCurrentTickFallback(
        bool enabled,
        bool managerAvailable,
        bool validAuthority)
    {
        Assert.AreEqual(
            PlayerHitQueryMode.CurrentTick,
            LagCompensationPolicy.Resolve(enabled, managerAvailable, validAuthority));
    }
}
