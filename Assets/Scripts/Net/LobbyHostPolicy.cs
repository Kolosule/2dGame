using System.Collections.Generic;

/// <summary>
/// Pure lobby decisions. The "host-client" (the player who gets the Start button) is the lowest-id
/// active player, so designation is deterministic and re-resolves when that player leaves. CanStart
/// is the alpha stress-test gate: any non-empty lobby may start — teams are auto-assigned on join
/// (see LobbyServerState), so nobody can block the match by failing to choose.
/// </summary>
public static class LobbyHostPolicy
{
    public const int NoHost = -1;

    public static int DesignateHostId(IReadOnlyList<int> activePlayerIds)
    {
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
