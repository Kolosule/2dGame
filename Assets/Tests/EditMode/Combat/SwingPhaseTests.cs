using NUnit.Framework;
using Game.Combat.Core;

public class SwingPhaseTests
{
    // startup 3, active 5, recovery 10; swing starts at tick 100
    private static SwingPhaseKind At(int tick) => SwingPhase.Resolve(tick, 100, 3, 5, 10);

    [Test]
    public void No_Swing_When_StartTick_Unset()
    {
        Assert.AreEqual(SwingPhaseKind.None, SwingPhase.Resolve(100, 0, 3, 5, 10));
    }

    [Test]
    public void Before_Start_Is_None()
    {
        Assert.AreEqual(SwingPhaseKind.None, At(99));
    }

    [Test]
    public void Startup_Window_Is_Exact()
    {
        Assert.AreEqual(SwingPhaseKind.Startup, At(100));
        Assert.AreEqual(SwingPhaseKind.Startup, At(102));
    }

    [Test]
    public void Active_Window_Is_Exact()
    {
        Assert.AreEqual(SwingPhaseKind.Active, At(103));
        Assert.AreEqual(SwingPhaseKind.Active, At(107));
    }

    [Test]
    public void Recovery_Window_Is_Exact()
    {
        Assert.AreEqual(SwingPhaseKind.Recovery, At(108));
        Assert.AreEqual(SwingPhaseKind.Recovery, At(117));
    }

    [Test]
    public void After_Recovery_Is_None()
    {
        Assert.AreEqual(SwingPhaseKind.None, At(118));
    }

    [Test]
    public void Zero_Startup_Is_Active_Immediately()
    {
        Assert.AreEqual(SwingPhaseKind.Active, SwingPhase.Resolve(100, 100, 0, 5, 10));
    }

    [Test]
    public void FirstActiveTick_Detects_Exactly_One_Tick()
    {
        Assert.IsFalse(SwingPhase.IsFirstActiveTick(102, 100, 3));
        Assert.IsTrue(SwingPhase.IsFirstActiveTick(103, 100, 3));
        Assert.IsFalse(SwingPhase.IsFirstActiveTick(104, 100, 3));
        Assert.IsFalse(SwingPhase.IsFirstActiveTick(103, 0, 3)); // no swing
    }
}
