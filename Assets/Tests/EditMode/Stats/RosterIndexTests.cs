using NUnit.Framework;
using Game.Stats.Core;

public class RosterIndexTests
{
    [Test]
    public void ValidPlayerIdResolvesToItself()
    {
        Assert.IsTrue(RosterIndex.TryResolve(5, 20, out int index));
        Assert.AreEqual(5, index);
    }

    [Test]
    public void PlayerIdAtCapacityIsOutOfRange()
    {
        Assert.IsFalse(RosterIndex.TryResolve(20, 20, out _));
    }

    [Test]
    public void PlayerIdOneBelowCapacityIsInRange()
    {
        Assert.IsTrue(RosterIndex.TryResolve(19, 20, out int index));
        Assert.AreEqual(19, index);
    }

    [Test]
    public void NegativePlayerIdIsOutOfRange()
    {
        Assert.IsFalse(RosterIndex.TryResolve(-1, 20, out _));
    }

    [Test]
    public void ZeroCapacityRejectsEverything()
    {
        Assert.IsFalse(RosterIndex.TryResolve(0, 0, out _));
    }
}
