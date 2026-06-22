/// <summary>
/// The single place team string aliases (Blue/Red/team1/team2/ai) and conversions live.
/// Pure static, no scene or MonoBehaviour dependency. Serialized authoring fields stay strings
/// and are Normalize()d here at read time.
/// </summary>
public static class TeamUtil
{
    /// <summary>Maps any authored/legacy team string to the canonical Team. Unknown/empty -> None.</summary>
    public static Team Normalize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return Team.None;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "team1":
            case "blue":
            case "1":
                return Team.Team1;
            case "team2":
            case "red":
            case "2":
                return Team.Team2;
            case "team3":
            case "ai":
            case "3":
                return Team.Team3AI;
            default:
                return Team.None;
        }
    }

    /// <summary>Team -> legacy team number (Team1 -> 1, Team2 -> 2, Team3AI -> 3, None -> 0).</summary>
    public static int ToNumber(Team team)
    {
        return (int)team;
    }

    /// <summary>Legacy team number -> Team. Anything outside 1..3 -> None.</summary>
    public static Team FromNumber(int number)
    {
        switch (number)
        {
            case 1: return Team.Team1;
            case 2: return Team.Team2;
            case 3: return Team.Team3AI;
            default: return Team.None;
        }
    }

    /// <summary>Canonical id string used by assets (TeamData.teamID, CoinData.coinTeam). None -> "".</summary>
    public static string ToId(Team team)
    {
        switch (team)
        {
            case Team.Team1: return "Team1";
            case Team.Team2: return "Team2";
            case Team.Team3AI: return "Team3";
            default: return "";
        }
    }

    /// <summary>Human-readable name for UI/logs.</summary>
    public static string DisplayName(Team team)
    {
        switch (team)
        {
            case Team.Team1: return "Blue";
            case Team.Team2: return "Red";
            case Team.Team3AI: return "AI";
            default: return "Unknown";
        }
    }

    /// <summary>PvPvE: two distinct, assigned teams are always hostile.</summary>
    public static bool AreEnemies(Team a, Team b)
    {
        if (a == Team.None || b == Team.None) return false;
        return a != b;
    }

    /// <summary>True for the two human teams (not None, not AI).</summary>
    public static bool IsPlayerTeam(Team team)
    {
        return team == Team.Team1 || team == Team.Team2;
    }
}
