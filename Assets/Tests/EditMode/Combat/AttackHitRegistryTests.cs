using Game.Combat.Core;
using NUnit.Framework;

public class AttackHitRegistryTests
{
    [Test]
    public void SameTargetCanOnlyRegisterOncePerAttack()
    {
        var registry = new AttackHitRegistry();

        Assert.IsTrue(registry.TryRegister(101));
        Assert.IsFalse(registry.TryRegister(101));
    }

    [Test]
    public void ClearAllowsTargetInNextSwing()
    {
        var registry = new AttackHitRegistry();
        registry.TryRegister(101);

        registry.Clear();

        Assert.IsTrue(registry.TryRegister(101));
    }

    [Test]
    public void SwingAndDashKeepIndependentTargetSets()
    {
        var swing = new AttackHitRegistry();
        var dash = new AttackHitRegistry();

        Assert.IsTrue(swing.TryRegister(101));
        Assert.IsTrue(dash.TryRegister(101));
    }

    [Test]
    public void PlayerAndEnemySourcesCannotRegisterSameNetworkTargetTwice()
    {
        var registry = new AttackHitRegistry();

        Assert.IsTrue(registry.TryRegister(101), "First query source should register the target.");
        Assert.IsFalse(registry.TryRegister(101), "A second query source must not duplicate damage.");
    }
}
