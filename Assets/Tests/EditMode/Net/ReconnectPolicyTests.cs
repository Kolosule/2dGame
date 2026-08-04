using NUnit.Framework;

public class ReconnectPolicyTests
{
    [Test]
    public void KnownToken_IsAlwaysAdmitted_ItIsReclaimingItsOwnReservedSeat()
    {
        // Session completely full on both counts: the holder still gets back in.
        Assert.IsTrue(ReconnectPolicy.CanAdmit(knownToken: true, activeCount: 19, heldCount: 1, maxPlayers: 20));
        Assert.IsTrue(ReconnectPolicy.CanAdmit(knownToken: true, activeCount: 20, heldCount: 0, maxPlayers: 20));
    }

    [Test]
    public void UnknownToken_IsRefusedWhenHeldSlotsFillTheCap()
    {
        // 19 playing + 1 holding a reserved seat = full, even though Fusion freed its own slot.
        Assert.IsFalse(ReconnectPolicy.CanAdmit(knownToken: false, activeCount: 19, heldCount: 1, maxPlayers: 20));
    }

    [Test]
    public void UnknownToken_IsAdmittedWhileThereIsRoom()
    {
        Assert.IsTrue(ReconnectPolicy.CanAdmit(knownToken: false, activeCount: 18, heldCount: 1, maxPlayers: 20));
        Assert.IsTrue(ReconnectPolicy.CanAdmit(knownToken: false, activeCount: 0, heldCount: 0, maxPlayers: 20));
    }

    [Test]
    public void UnknownToken_IsRefusedWhenActivePlayersAloneFillTheCap()
    {
        Assert.IsFalse(ReconnectPolicy.CanAdmit(knownToken: false, activeCount: 20, heldCount: 0, maxPlayers: 20));
    }
}
