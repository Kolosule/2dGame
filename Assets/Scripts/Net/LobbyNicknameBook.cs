/// <summary>
/// The single server-side rule for "record a player's display name", plus the local player's
/// latched menu nickname.
///
/// A display name has to reach TWO stores: the lobby roster (LobbyServerState -> LobbyProtocol ->
/// LobbyScreenUI) and the PlayerRef-keyed handoff dictionary that survives the MainMenu ->
/// Gameplay scene load (GameNetworkManager.LobbyNicknameChoices -> MatchStatsManager ->
/// ScoreboardPanel). They drifted once already: the host wrote only the roster, so their real name
/// showed on the lobby screen while the scoreboard kept the "Player N" placeholder. Every write now
/// goes through <see cref="TryRecord"/>, which reports the name BOTH stores must show.
///
/// Pure C# — players are plain ints, and the caller owns the PlayerRef-keyed store — so the rule is
/// unit-testable without UnityEngine or Fusion.
/// </summary>
public class LobbyNicknameBook
{
    private string localNickname = "";

    /// <summary>
    /// The local (host) player's sanitized menu nickname. The host types it in MainMenuUI instead of
    /// sending a NAME message, and never re-sends it, so it is remembered here and applied whenever
    /// the local player is seated — see <see cref="JoinNickname"/>.
    /// </summary>
    public string LocalNickname => localNickname;

    /// <summary>Latches the local player's menu nickname. Empty/whitespace clears the latch.</summary>
    public void RememberLocal(string raw) => localNickname = LobbyProtocol.SanitizeNickname(raw);

    /// <summary>Forgets the latched nickname. Session teardown only — it is re-latched on connect.</summary>
    public void Reset() => localNickname = "";

    /// <summary>
    /// The nickname to record for a player who is being seated right now. Only the LOCAL player has
    /// one waiting; every remote player supplies theirs over reliable data later, so they get "",
    /// which <see cref="TryRecord"/> treats as "keep the placeholder".
    ///
    /// Applying the latch at seat time is what makes the host path order-independent: the lobby UI
    /// (which latches) and the join callback (which seats) are not ordered against each other, and a
    /// nickname recorded while the host was still unseated would otherwise be dropped forever.
    /// </summary>
    public string JoinNickname(bool isLocalPlayer) => isLocalPlayer ? localNickname : "";

    /// <summary>
    /// Records <paramref name="raw"/> for a seated player and reports the display name both stores
    /// must now show. Returns false when the player is not in the roster — nothing to mirror.
    ///
    /// <paramref name="displayName"/> is the roster's RESULTING name, not the submitted one: an
    /// empty or whitespace nickname is a no-op that keeps "Player N" (LobbyServerState.SetNickname's
    /// rule), and the name is reported even when nothing changed. That last part is the fix —
    /// mirroring only when <paramref name="rosterChanged"/> is true is exactly how the handoff store
    /// was left holding a stale placeholder.
    /// </summary>
    public bool TryRecord(LobbyServerState lobby, int playerId, string raw,
                          out string displayName, out bool rosterChanged)
    {
        displayName = "";
        rosterChanged = false;
        if (lobby == null || !lobby.HasPlayer(playerId)) return false;

        rosterChanged = lobby.SetNickname(playerId, raw);
        displayName = lobby.NameOf(playerId);
        return true;
    }
}
