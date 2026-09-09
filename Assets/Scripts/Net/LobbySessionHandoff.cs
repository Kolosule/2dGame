using System.Collections.Generic;

/// <summary>
/// Session-owned projection of lobby choices for gameplay spawning, not an authoritative roster.
/// Team, nickname and loadout arrive independently; recording one never replaces the other fields.
/// The owner keeps this across scene loads and reconnect attempts, and resets it at session boundaries.
/// Player ids are plain ints; the owner handles Fusion conversion and authoritative validation.
/// </summary>
public sealed class LobbySessionHandoff
{
    private sealed class Entry
    {
        public bool HasTeam;
        public int Team;
        public bool HasNickname;
        public string Nickname;
        public bool HasLoadout;
        public byte[] Loadout;
    }

    private readonly Dictionary<int, Entry> players = new Dictionary<int, Entry>();

    public void SetTeam(int playerId, int team)
    {
        Entry entry = GetOrCreate(playerId);
        entry.Team = team;
        entry.HasTeam = true;
    }

    public bool TryGetTeam(int playerId, out int team)
    {
        if (players.TryGetValue(playerId, out Entry entry) && entry.HasTeam)
        {
            team = entry.Team;
            return true;
        }

        team = default;
        return false;
    }

    /// <summary>Records the resulting roster name supplied by the owner's authoritative nickname path.</summary>
    public void SetNickname(int playerId, string nickname)
    {
        Entry entry = GetOrCreate(playerId);
        entry.Nickname = nickname;
        entry.HasNickname = true;
    }

    public bool TryGetNickname(int playerId, out string nickname)
    {
        if (players.TryGetValue(playerId, out Entry entry) && entry.HasNickname)
        {
            nickname = entry.Nickname;
            return true;
        }

        nickname = null;
        return false;
    }

    public void SetLoadout(int playerId, byte[] order)
    {
        Entry entry = GetOrCreate(playerId);
        entry.Loadout = order;
        entry.HasLoadout = true;
    }

    /// <summary>False means no choice was submitted; spawning keeps the configured default loadout.</summary>
    public bool TryGetLoadout(int playerId, out byte[] order)
    {
        if (players.TryGetValue(playerId, out Entry entry) && entry.HasLoadout)
        {
            order = entry.Loadout;
            return true;
        }

        order = null;
        return false;
    }

    public void RemovePlayer(int playerId) => players.Remove(playerId);

    public void Reset() => players.Clear();

    private Entry GetOrCreate(int playerId)
    {
        if (!players.TryGetValue(playerId, out Entry entry))
        {
            entry = new Entry();
            players.Add(playerId, entry);
        }

        return entry;
    }
}
