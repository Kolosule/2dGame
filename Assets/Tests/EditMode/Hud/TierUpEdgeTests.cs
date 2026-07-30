using NUnit.Framework;
using Game.Hud.Core;

/// <summary>
/// Toasts fire on a genuine tier-up only. Detection is client-side, so it must survive the two
/// ways a client legitimately sees a tier "appear": the first paint after binding, and a late
/// joiner receiving mid-match state.
/// </summary>
public class TierUpEdgeTests
{
    [Test]
    public void TheFirstObservationNeverFires()
    {
        var edge = new TierUpEdge();
        Assert.IsFalse(edge.Observe(0), "bind at tier 0");
    }

    [Test]
    public void ALateJoinerAtAHighTierDoesNotToastOnArrival()
    {
        var edge = new TierUpEdge();
        Assert.IsFalse(edge.Observe(3), "first paint already at tier 3");
        Assert.IsFalse(edge.Observe(3), "repaint at the same tier");
    }

    [Test]
    public void ARiseFires_Once()
    {
        var edge = new TierUpEdge();
        edge.Observe(0);
        Assert.IsTrue(edge.Observe(1));
        Assert.IsFalse(edge.Observe(1), "a repaint at the same tier is not a new unlock");
        Assert.IsTrue(edge.Observe(2));
    }

    [Test]
    public void AJumpOfSeveralTiersFiresOnce()
    {
        var edge = new TierUpEdge();
        edge.Observe(0);
        Assert.IsTrue(edge.Observe(3), "a big deposit crossing several steps is one moment");
    }

    [Test]
    public void AFallNeverFires()
    {
        var edge = new TierUpEdge();
        edge.Observe(3);
        Assert.IsFalse(edge.Observe(0), "leaving Sudden Death / rematch reset");
        Assert.IsTrue(edge.Observe(1), "and the next genuine rise still fires");
    }

    [Test]
    public void ResetReprimes()
    {
        var edge = new TierUpEdge();
        edge.Observe(0);
        edge.Reset();
        Assert.IsFalse(edge.Observe(3), "after Unbind/rebind the first paint is silent again");
    }
}
