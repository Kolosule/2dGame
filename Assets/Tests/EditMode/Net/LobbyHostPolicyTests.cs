using System.Collections.Generic;
using NUnit.Framework;

public class LobbyHostPolicyTests
{
    [Test]
    public void DesignateHostId_Empty_ReturnsNoHost()
    {
        Assert.AreEqual(LobbyHostPolicy.NoHost, LobbyHostPolicy.DesignateHostId(new int[0]));
    }

    [Test]
    public void DesignateHostId_SinglePlayer_ReturnsThatPlayer()
    {
        Assert.AreEqual(3, LobbyHostPolicy.DesignateHostId(new[] { 3 }));
    }

    [Test]
    public void DesignateHostId_ReturnsLowestId_RegardlessOfOrder()
    {
        Assert.AreEqual(1, LobbyHostPolicy.DesignateHostId(new[] { 4, 1, 7, 2 }));
    }

    [Test]
    public void DesignateHostId_AfterLowestLeaves_ReturnsNextLowest()
    {
        // host (id 1) left; remaining roster re-designates to id 2
        Assert.AreEqual(2, LobbyHostPolicy.DesignateHostId(new[] { 4, 2, 7 }));
    }

    [Test]
    public void CanStart_NoPlayers_False()
    {
        Assert.IsFalse(LobbyHostPolicy.CanStart(new int[0], _ => true));
    }

    [Test]
    public void CanStart_AllChosen_True()
    {
        var chosen = new HashSet<int> { 1, 2, 5 };
        Assert.IsTrue(LobbyHostPolicy.CanStart(new[] { 1, 2, 5 }, chosen.Contains));
    }

    [Test]
    public void CanStart_OneMissing_False()
    {
        var chosen = new HashSet<int> { 1, 5 };
        Assert.IsFalse(LobbyHostPolicy.CanStart(new[] { 1, 2, 5 }, chosen.Contains));
    }
}
