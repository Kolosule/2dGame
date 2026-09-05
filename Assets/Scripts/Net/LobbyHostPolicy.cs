using System.Collections.Generic;

/// <summary>
/// Pure lobby decisions. The "host-client" (the player who gets the Start button) is the server's
/// own player when the server is itself a player (GameMode.Host), and otherwise the lowest-id active
/// player — so a dedicated server's lobby belongs to the first joiner and re-designates when that
/// player leaves. CanStart is the alpha stress-test gate: any non-empty lobby may start — teams are
/// auto-assigned on join (see LobbyServerState), so nobody can block the match by failing to choose.
/// </summary>
public static class LobbyHostPolicy
{
    public const int NoHost = -1;

    /// <summary>
    /// <paramref name="serverPlayerId"/> is the server's OWN player id when the server is also a
    /// player (GameMode.Host), or <see cref="NoHost"/> for a dedicated server, which is not a player
    /// at all.
    ///
    /// It cannot be inferred from the id list. Fusion assigns the server player the LAST PlayerRef
    /// index — "the server player (if one exists), always gets the last index no matter how many
    /// clients are connected" — so a plain lowest-id rule hands a host's own lobby to the first
    /// client that joins it.
    /// </summary>
    public static int DesignateHostId(IReadOnlyList<int> activePlayerIds, int serverPlayerId)
    {
        if (activePlayerIds == null) return NoHost;

        // The host owns its lobby for as long as it is seated, whatever index Fusion handed it.
        if (serverPlayerId != NoHost)
        {
            for (int i = 0; i < activePlayerIds.Count; i++)
                if (activePlayerIds[i] == serverPlayerId) return serverPlayerId;
        }

        int host = NoHost;
        for (int i = 0; i < activePlayerIds.Count; i++)
        {
            int id = activePlayerIds[i];
            if (host == NoHost || id < host) host = id;
        }
        return host;
    }

    public static bool CanStart(int activePlayerCount) => activePlayerCount >= 1;
}
