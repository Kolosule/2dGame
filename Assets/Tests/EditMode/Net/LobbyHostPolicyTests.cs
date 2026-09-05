using NUnit.Framework;

public class LobbyHostPolicyTests
{
    const int NoServerPlayer = LobbyHostPolicy.NoHost; // dedicated server: not a player

    [Test]
    public void DesignateHostId_Empty_ReturnsNoHost()
    {
        Assert.AreEqual(LobbyHostPolicy.NoHost, LobbyHostPolicy.DesignateHostId(new int[0], NoServerPlayer));
    }

    [Test]
    public void DesignateHostId_Null_ReturnsNoHost()
    {
        Assert.AreEqual(LobbyHostPolicy.NoHost, LobbyHostPolicy.DesignateHostId(null, NoServerPlayer));
    }

    [Test]
    public void DesignateHostId_SinglePlayer_ReturnsThatPlayer()
    {
        Assert.AreEqual(3, LobbyHostPolicy.DesignateHostId(new[] { 3 }, NoServerPlayer));
    }

    [Test]
    public void DesignateHostId_ReturnsLowestId_RegardlessOfOrder()
    {
        Assert.AreEqual(1, LobbyHostPolicy.DesignateHostId(new[] { 4, 1, 7, 2 }, NoServerPlayer));
    }

    [Test]
    public void DesignateHostId_AfterLowestLeaves_ReturnsNextLowest()
    {
        // host (id 1) left; remaining roster re-designates to id 2
        Assert.AreEqual(2, LobbyHostPolicy.DesignateHostId(new[] { 4, 2, 7 }, NoServerPlayer));
    }

    [Test]
    public void DesignateHostId_ServerPlayerWins_EvenWithLowerClientIds()
    {
        // Fusion seats the server player at the LAST index (19 of a 20-slot room), so the lowest-id
        // rule alone would give the Start button to the first client instead of the host.
        Assert.AreEqual(19, LobbyHostPolicy.DesignateHostId(new[] { 0, 1, 19 }, 19));
    }

    [Test]
    public void DesignateHostId_ServerPlayerAlone_ReturnsServerPlayer()
    {
        Assert.AreEqual(19, LobbyHostPolicy.DesignateHostId(new[] { 19 }, 19));
    }

    [Test]
    public void DesignateHostId_ServerPlayerNotSeated_FallsBackToLowestId()
    {
        // The host disconnected: its id is no longer in the roster, so the lobby re-designates.
        Assert.AreEqual(1, LobbyHostPolicy.DesignateHostId(new[] { 4, 1, 7 }, 19));
    }

    [Test]
    public void DesignateHostId_ServerPlayerButEmptyRoster_ReturnsNoHost()
    {
        Assert.AreEqual(LobbyHostPolicy.NoHost, LobbyHostPolicy.DesignateHostId(new int[0], 19));
    }

    [Test]
    public void CanStart_NoPlayers_False()
    {
        Assert.IsFalse(LobbyHostPolicy.CanStart(0));
    }

    [Test]
    public void CanStart_OnePlayer_True()
    {
        Assert.IsTrue(LobbyHostPolicy.CanStart(1));
    }

    [Test]
    public void CanStart_FullLobby_True()
    {
        Assert.IsTrue(LobbyHostPolicy.CanStart(20));
    }
}

