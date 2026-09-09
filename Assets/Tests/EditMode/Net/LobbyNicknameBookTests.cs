using NUnit.Framework;

/// <summary>
/// Regression tests for the host's display name reaching the SCOREBOARD, not just the lobby screen.
///
/// The bug: a display name lives in two places — LobbyServerState (the roster the lobby screen
/// renders) and the session's LobbySessionHandoff (the projection that
/// survives the MainMenu -> Gameplay scene load and becomes MatchStatsManager.DisplayName on the
/// scoreboard). The host wrote only the first, so they played the whole match as "Player 1".
/// </summary>
public class LobbyNicknameBookTests
{
    private LobbyServerState lobby;
    private LobbyNicknameBook book;
    private LobbySessionHandoff handoff;

    [SetUp]
    public void SetUp()
    {
        lobby = new LobbyServerState();
        book = new LobbyNicknameBook();
        handoff = new LobbySessionHandoff();
    }

    /// <summary>
    /// The body of GameNetworkManager.ServerSetNickname, minus the Fusion guards. Every nickname
    /// write in the game goes through it: the host shortcut, the client NAME message, and the
    /// reconnect restore. Keep it in sync with the real one — it is the whole point of the fix that
    /// there is only one of these.
    /// </summary>
    private bool ServerSetNickname(int playerId, string raw)
    {
        if (!book.TryRecord(lobby, playerId, raw, out string displayName, out bool rosterChanged))
            return false;
        handoff.SetNickname(playerId, displayName);
        return rosterChanged;
    }

    /// <summary>GameNetworkManager.ServerHandleJoin's normal (non-reconnect) branch.</summary>
    private void ServerHandleJoin(int playerId, bool isLocalPlayer)
    {
        int team = lobby.PlayerJoined(playerId);        // seeds the "Player N" placeholder
        handoff.SetTeam(playerId, team);
        ServerSetNickname(playerId, book.JoinNickname(isLocalPlayer));
    }

    private string ScoreboardName(int playerId) =>
        handoff.TryGetNickname(playerId, out string name) ? name : null;

    // ---- The reported bug -------------------------------------------------------------------

    [Test]
    public void HostNickname_ReachesTheScoreboardStore_NotJustTheLobbyRoster()
    {
        ServerHandleJoin(1, isLocalPlayer: true);       // OnPlayerJoined seats the host first...
        book.RememberLocal("Ann");                      // ...then EnterLobbyUI latches + applies
        ServerSetNickname(1, "Ann");

        Assert.AreEqual("Ann", lobby.NameOf(1), "lobby screen");
        Assert.AreEqual("Ann", ScoreboardName(1), "scoreboard");
    }

    [Test]
    public void HostNickname_AppliedBeforeTheJoinCallback_SurvivesTheJoin()
    {
        // The ordering hazard: EnterLobbyUI runs after `await runner.StartGame`, which is not ordered
        // against OnPlayerJoined. Applying a nickname to an unseated player used to drop it from BOTH
        // stores, and the host never re-sends it.
        book.RememberLocal("Ann");
        Assert.IsFalse(ServerSetNickname(1, "Ann"), "not seated yet — nothing to record");
        Assert.IsNull(ScoreboardName(1));

        ServerHandleJoin(1, isLocalPlayer: true);       // ...OnPlayerJoined finally lands

        Assert.AreEqual("Ann", lobby.NameOf(1), "lobby screen");
        Assert.AreEqual("Ann", ScoreboardName(1), "scoreboard");
    }

    [Test]
    public void HostNickname_ReAppliedAfterTheJoinAlreadyStoredIt_StillMirrors()
    {
        // The exact drift that caused the bug: SetNickname returns false for "unchanged", and the old
        // code only mirrored on true. Whichever of join/apply runs second must not be a silent no-op.
        book.RememberLocal("Ann");
        ServerHandleJoin(1, isLocalPlayer: true);
        handoff.SetNickname(1, "Player 1");             // pretend the mirror went stale

        Assert.IsFalse(ServerSetNickname(1, "Ann"), "roster already says Ann");
        Assert.AreEqual("Ann", ScoreboardName(1), "mirror must not depend on rosterChanged");
    }

    [Test]
    public void EmptyHostNickname_KeepsThePlaceholderInBothStores()
    {
        book.RememberLocal("   ");                      // whitespace sanitizes to ""
        Assert.AreEqual("", book.LocalNickname);

        ServerHandleJoin(4, isLocalPlayer: true);
        ServerSetNickname(4, "");

        Assert.AreEqual("Player 4", lobby.NameOf(4));
        Assert.AreEqual("Player 4", ScoreboardName(4), "never a blank name on the scoreboard");
    }

    // ---- Everyone else is unaffected --------------------------------------------------------

    [Test]
    public void ClientNickname_StillArrivesOverTheNameMessagePath()
    {
        ServerHandleJoin(2, isLocalPlayer: false);
        Assert.AreEqual("Player 2", lobby.NameOf(2), "placeholder until their NAME message lands");
        Assert.AreEqual("Player 2", ScoreboardName(2));

        Assert.IsTrue(ServerSetNickname(2, "Bob"), "roster changed -> broadcast");
        Assert.AreEqual("Bob", lobby.NameOf(2));
        Assert.AreEqual("Bob", ScoreboardName(2));
    }

    [Test]
    public void JoinNickname_IsOnlyOfferedToTheLocalPlayer()
    {
        book.RememberLocal("Ann");
        Assert.AreEqual("Ann", book.JoinNickname(isLocalPlayer: true));
        Assert.AreEqual("", book.JoinNickname(isLocalPlayer: false),
            "a remote joiner must never inherit the host's latched nickname");
    }

    [Test]
    public void RemoteJoiner_OnADedicatedServer_KeepsThePlaceholder()
    {
        // A dedicated server has no local player at all, so isLocalPlayer is false for everyone and
        // the (empty) latch is never consumed — no phantom seat, no borrowed name.
        ServerHandleJoin(0, isLocalPlayer: false);
        ServerHandleJoin(1, isLocalPlayer: false);

        Assert.AreEqual("Player 0", ScoreboardName(0));
        Assert.AreEqual("Player 1", ScoreboardName(1));
        Assert.AreEqual(2, lobby.PlayerCount);
    }

    // ---- Reconnect --------------------------------------------------------------------------

    [Test]
    public void ReconnectHold_CapturesTheHostsRealName_AndRestoresIt()
    {
        book.RememberLocal("Ann");
        ServerHandleJoin(1, isLocalPlayer: true);

        // ServerCaptureForReconnect reads the scoreboard store to build ReconnectHeldSlot.DisplayName.
        string heldDisplayName = ScoreboardName(1);
        Assert.AreEqual("Ann", heldDisplayName, "a dropped host must be held under their real name");

        lobby.PlayerLeft(1);
        handoff.RemovePlayer(1);

        // They rejoin under a NEW PlayerId and reclaim the hold (PlayerJoinedOnTeam + the restore).
        handoff.SetTeam(6, lobby.PlayerJoinedOnTeam(6, 2));
        ServerSetNickname(6, heldDisplayName);

        Assert.AreEqual("Ann", lobby.NameOf(6));
        Assert.AreEqual("Ann", ScoreboardName(6));
        Assert.IsNull(ScoreboardName(1), "the old PlayerId must not retain the disconnected name");
    }

    [Test]
    public void ReconnectRestore_WithNoHeldName_FallsBackToTheNewPlaceholder()
    {
        lobby.PlayerJoinedOnTeam(6, 1);
        ServerSetNickname(6, "");                       // an empty held name must not blank the row

        Assert.AreEqual("Player 6", lobby.NameOf(6));
        Assert.AreEqual("Player 6", ScoreboardName(6));
    }

    [Test]
    public void ReconnectRestore_MirrorsTheValidatedName_WithoutErasingTeamOrLoadout()
    {
        handoff.SetTeam(6, lobby.PlayerJoinedOnTeam(6, 2));
        handoff.SetLoadout(6, new byte[] { 3, 1, 2 });

        ServerSetNickname(6, "  Ann\tabelle Lee  ");

        Assert.AreEqual("Annabelle Lee", lobby.NameOf(6));
        Assert.AreEqual(lobby.NameOf(6), ScoreboardName(6));
        Assert.IsTrue(handoff.TryGetTeam(6, out int team));
        Assert.AreEqual(2, team);
        Assert.IsTrue(handoff.TryGetLoadout(6, out byte[] order));
        CollectionAssert.AreEqual(new byte[] { 3, 1, 2 }, order);
    }

    // ---- Latch semantics --------------------------------------------------------------------

    [Test]
    public void RememberLocal_SanitizesLikeEveryOtherNicknamePath()
    {
        book.RememberLocal("  Ann\tabelle Lee  ");
        Assert.AreEqual("Annabelle Lee", book.LocalNickname, "trimmed, control char dropped");

        book.RememberLocal("ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        Assert.AreEqual("ABCDEFGHIJKLMNOP", book.LocalNickname,
            "capped at LobbyProtocol.MaxNicknameChars — NetworkString<_16> cannot hold more");
        Assert.AreEqual(LobbyProtocol.MaxNicknameChars, book.LocalNickname.Length);
    }

    [Test]
    public void Reset_ForgetsTheLatchSoTheNextSessionCannotInheritIt()
    {
        book.RememberLocal("Ann");
        book.Reset();
        Assert.AreEqual("", book.LocalNickname);

        ServerHandleJoin(1, isLocalPlayer: true);
        Assert.AreEqual("Player 1", ScoreboardName(1));
    }

    [Test]
    public void TryRecord_UnseatedPlayer_ReportsNothingToMirror()
    {
        Assert.IsFalse(book.TryRecord(lobby, 3, "Ann", out string name, out bool changed));
        Assert.AreEqual("", name);
        Assert.IsFalse(changed);
        Assert.IsFalse(book.TryRecord(null, 3, "Ann", out _, out _));
    }
}
