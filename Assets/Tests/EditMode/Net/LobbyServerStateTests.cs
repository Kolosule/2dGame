using NUnit.Framework;

public class LobbyServerStateTests
{
    [Test]
    public void PlayerJoined_BalancesTeams_TieGoesToTeam1()
    {
        var s = new LobbyServerState();
        Assert.AreEqual(1, s.PlayerJoined(1)); // 0-0 tie -> team 1
        Assert.AreEqual(2, s.PlayerJoined(2)); // 1-0 -> team 2
        Assert.AreEqual(1, s.PlayerJoined(3)); // 1-1 tie -> team 1
        Assert.AreEqual(2, s.PlayerJoined(4));
    }

    [Test]
    public void PlayerJoined_Rejoin_KeepsExistingTeam()
    {
        var s = new LobbyServerState();
        s.PlayerJoined(1);            // team 1
        s.PlayerJoined(2);            // team 2
        Assert.AreEqual(1, s.PlayerJoined(1));
        Assert.AreEqual(2, s.PlayerCount);
    }

    [Test]
    public void PlayerLeft_RemovesAndRebalancesFutureJoins()
    {
        var s = new LobbyServerState();
        s.PlayerJoined(1);            // team 1
        s.PlayerJoined(2);            // team 2
        s.PlayerLeft(1);
        Assert.AreEqual(1, s.PlayerCount);
        Assert.AreEqual(1, s.PlayerJoined(3)); // team 1 is now smaller
    }

    [Test]
    public void SwitchTeam_Validates()
    {
        var s = new LobbyServerState();
        s.PlayerJoined(1); // team 1
        Assert.IsTrue(s.SwitchTeam(1, 2));
        Assert.IsFalse(s.SwitchTeam(1, 2)); // already on team 2
        Assert.IsFalse(s.SwitchTeam(1, 3)); // invalid team
        Assert.IsFalse(s.SwitchTeam(99, 1)); // unknown player
    }

    [Test]
    public void SetNickname_SanitizesAndKeepsPlaceholderOnEmpty()
    {
        var s = new LobbyServerState();
        s.PlayerJoined(7);
        Assert.IsFalse(s.SetNickname(7, "   "));   // empty after sanitize -> keep placeholder
        Assert.IsTrue(s.SetNickname(7, "  Ann  "));
        Assert.IsFalse(s.SetNickname(7, "Ann"));   // unchanged
        Assert.IsFalse(s.SetNickname(99, "Bob"));  // unknown player
        var snap = s.BuildSnapshot(20, LobbyHostPolicy.NoHost);
        Assert.AreEqual("Ann", snap.Players[0].Name);
    }

    [Test]
    public void CurrentHostId_LowestId_ReresolvesOnLeave()
    {
        var s = new LobbyServerState();
        Assert.AreEqual(LobbyHostPolicy.NoHost, s.CurrentHostId(LobbyHostPolicy.NoHost));
        s.PlayerJoined(4);
        s.PlayerJoined(2);
        s.PlayerJoined(7);
        Assert.AreEqual(2, s.CurrentHostId(LobbyHostPolicy.NoHost));
        s.PlayerLeft(2);
        Assert.AreEqual(4, s.CurrentHostId(LobbyHostPolicy.NoHost));
    }

    [Test]
    public void CurrentHostId_HostMode_ServerPlayerKeepsLobbyDespiteHigherId()
    {
        // Fusion seats the host player at the LAST index, so the lowest-id rule alone would hand a
        // host's own lobby to the first client that joined it.
        var s = new LobbyServerState();
        s.PlayerJoined(19); // the host itself
        Assert.AreEqual(19, s.CurrentHostId(19));
        s.PlayerJoined(0);  // first client
        Assert.AreEqual(19, s.CurrentHostId(19));

        // ...and once the host is gone the lobby falls back to the lowest remaining id.
        s.PlayerLeft(19);
        Assert.AreEqual(0, s.CurrentHostId(19));
    }

    [Test]
    public void BuildSnapshot_ContentsAndGate()
    {
        var s = new LobbyServerState();
        var empty = s.BuildSnapshot(20, LobbyHostPolicy.NoHost);
        Assert.IsFalse(empty.CanStart);
        Assert.AreEqual(20, empty.MaxPlayers);

        s.PlayerJoined(5);
        s.PlayerJoined(3);
        var snap = s.BuildSnapshot(20, LobbyHostPolicy.NoHost);
        Assert.IsTrue(snap.CanStart);
        Assert.AreEqual(3, snap.HostId);
        Assert.AreEqual(2, snap.Players.Count);
        Assert.AreEqual(3, snap.Players[0].Id); // ascending id order
        Assert.AreEqual(5, snap.Players[1].Id);
        Assert.AreEqual("Player 5", snap.Players[1].Name);
    }

    [Test]
    public void BuildSnapshot_HostMode_ReportsServerPlayerAsHost()
    {
        var s = new LobbyServerState();
        s.PlayerJoined(19);
        s.PlayerJoined(0);
        var snap = s.BuildSnapshot(20, 19);
        Assert.AreEqual(19, snap.HostId);
    }

    [Test]
    public void PlayerJoinedOnTeam_SeatsAReconnectorOnTheirHeldTeam_BypassingAutoBalance()
    {
        var s = new LobbyServerState();
        s.PlayerJoined(1); // team 1
        s.PlayerJoined(2); // team 2
        s.PlayerJoined(3); // team 1  -> auto-balance would put id 4 on team 2

        Assert.AreEqual(1, s.PlayerJoinedOnTeam(4, 1));
        Assert.AreEqual(1, s.TeamOf(4));
        Assert.AreEqual(4, s.PlayerCount);
    }

    [Test]
    public void PlayerJoinedOnTeam_InvalidTeam_FallsBackToBalancedAutoAssign()
    {
        var s = new LobbyServerState();
        Assert.AreEqual(1, s.PlayerJoinedOnTeam(1, 0));  // 0-0 tie -> team 1
        Assert.AreEqual(2, s.PlayerJoinedOnTeam(2, 7));  // 1-0 -> team 2
    }

    [Test]
    public void PlayerJoinedOnTeam_ExistingPlayer_IsMovedToTheHeldTeam()
    {
        var s = new LobbyServerState();
        s.PlayerJoined(1);                 // team 1
        Assert.AreEqual(2, s.PlayerJoinedOnTeam(1, 2));
        Assert.AreEqual(2, s.TeamOf(1));
        Assert.AreEqual(1, s.PlayerCount); // not duplicated
    }
}
