using System.Collections.Generic;

/// <summary>
/// Server-side lobby roster + rules. Pure C# — player ids are plain ints (mapping from
/// Fusion's PlayerRef.PlayerId is GameNetworkManager's job) so this is unit-testable.
/// Owns: balanced team auto-assign on join (smaller team wins, tie -> team 1), optional
/// team switching, nickname storage, host designation (LobbyHostPolicy: lowest id) and
/// the start gate (>= 1 player).
/// </summary>
public class LobbyServerState
{
    private class Entry
    {
        public string Name;
        public int Team;
    }

    // Sorted so BuildSnapshot emits a stable ascending-id roster.
    private readonly SortedDictionary<int, Entry> players = new SortedDictionary<int, Entry>();

    public int PlayerCount => players.Count;

    public bool HasPlayer(int id) => players.ContainsKey(id);

    /// <summary>
    /// Adds the player with a placeholder name on the smaller team (tie -> team 1) and returns
    /// the assigned team. A re-join returns the existing team without changing anything.
    /// </summary>
    public int PlayerJoined(int id)
    {
        if (players.TryGetValue(id, out var existing)) return existing.Team;
        int team = CountTeam(1) <= CountTeam(2) ? 1 : 2;
        players[id] = new Entry { Name = LobbyProtocol.PlaceholderName(id), Team = team };
        return team;
    }

    public void PlayerLeft(int id) => players.Remove(id);

    /// <summary>Sanitizes and stores; an empty sanitize result keeps the current name. True if changed.</summary>
    public bool SetNickname(int id, string raw)
    {
        if (!players.TryGetValue(id, out var e)) return false;
        string clean = LobbyProtocol.SanitizeNickname(raw);
        if (clean.Length == 0 || clean == e.Name) return false;
        e.Name = clean;
        return true;
    }

    /// <summary>True only if the player exists, team is 1|2, and it differs from their current team.</summary>
    public bool SwitchTeam(int id, int team)
    {
        if (team != 1 && team != 2) return false;
        if (!players.TryGetValue(id, out var e) || e.Team == team) return false;
        e.Team = team;
        return true;
    }

    public int TeamOf(int id) => players.TryGetValue(id, out var e) ? e.Team : 0;

    public int CurrentHostId()
    {
        var ids = new List<int>(players.Keys);
        return LobbyHostPolicy.DesignateHostId(ids);
    }

    public LobbyStateSnapshot BuildSnapshot(int maxPlayers)
    {
        var s = new LobbyStateSnapshot
        {
            CanStart = LobbyHostPolicy.CanStart(players.Count),
            MaxPlayers = maxPlayers,
            HostId = CurrentHostId(),
        };
        foreach (var kv in players)
            s.Players.Add(new LobbyPlayerEntry(kv.Key, kv.Value.Name, kv.Value.Team));
        return s;
    }

    private int CountTeam(int team)
    {
        int n = 0;
        foreach (var kv in players)
            if (kv.Value.Team == team) n++;
        return n;
    }
}
