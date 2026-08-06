using NUnit.Framework;
using Game.Combat.Core;

public class FriendlyFireTests
{
    [TestCase(1, 1)]
    [TestCase(2, 2)]
    public void SameTeam_CannotDamage(int attacker, int defender)
    {
        Assert.IsFalse(FriendlyFire.CanDamagePlayer(attacker, defender, isSelf: false));
    }

    [TestCase(1, 2)]
    [TestCase(2, 1)]
    public void OpposingHumanTeams_CanDamage(int attacker, int defender)
    {
        Assert.IsTrue(FriendlyFire.CanDamagePlayer(attacker, defender, isSelf: false));
    }

    [TestCase(3, 1)]
    [TestCase(1, 3)]
    [TestCase(3, 2)]
    [TestCase(2, 3)]
    public void AiTeamVsHumanTeam_CanDamage(int attacker, int defender)
    {
        Assert.IsTrue(FriendlyFire.CanDamagePlayer(attacker, defender, isSelf: false));
    }

    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(0, 0)]
    public void UnassignedTeamOnEitherSide_CannotDamage(int attacker, int defender)
    {
        Assert.IsFalse(FriendlyFire.CanDamagePlayer(attacker, defender, isSelf: false));
    }

    [TestCase(1, 1)]
    [TestCase(1, 2)]
    [TestCase(2, 1)]
    public void Self_CannotDamage_RegardlessOfTeam(int attacker, int defender)
    {
        Assert.IsTrue(!FriendlyFire.CanDamagePlayer(attacker, defender, isSelf: true));
    }
}
