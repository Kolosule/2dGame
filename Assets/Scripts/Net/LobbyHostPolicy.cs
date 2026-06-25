using System;
using System.Collections.Generic;

/// <summary>
/// Pure lobby decisions for a dedicated-server match where the server is NOT a player.
/// The "host-client" (the player who gets the Start button) is simply the lowest-id active
/// player, so designation is deterministic and re-resolves when that player leaves. CanStart
/// mirrors the host-mode gate: every connected player must have submitted a team choice.
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

    public static bool CanStart(IReadOnlyList<int> activePlayerIds, Func<int, bool> hasChosen)
    {
        if (activePlayerIds.Count == 0) return false;
        for (int i = 0; i < activePlayerIds.Count; i++)
            if (!hasChosen(activePlayerIds[i])) return false;
        return true;
    }
}
