using UnityEngine;

/// <summary>
/// ScriptableObject that holds coin value data for different teams.
/// This allows easy balancing through the Unity Inspector without changing code.
/// NOW COMPATIBLE WITH 3-TEAM SYSTEM (Team1, Team2, Team3)
/// </summary>
[CreateAssetMenu(fileName = "New Coin Data", menuName = "Game/Coin Data")]
public class CoinData : ScriptableObject
{
    [Header("Coin Identity")]
    [Tooltip("The team/color this coin came from (Team1, Team2, Team3)")]
    public string coinTeam; // e.g., "Team1", "Team2", "Team3"

    [Header("Point Values")]
    [Tooltip("Points awarded to Team1/Blue team when they collect this coin")]
    [SerializeField] private int team1Value = 1;

    [Tooltip("Points awarded to Team2/Red team when they collect this coin")]
    [SerializeField] private int team2Value = 1;

    

    /// <summary>
    /// Gets the point value for a specific team
    /// Handles three team system:
    /// - "Team1" or "Blue" or "team1" = Team 1
    /// - "Team2" or "Red" or "team2" = Team 2
    /// - "Team3" or "team3" = AI team (neutral/enemy to both player teams)
    /// </summary>
    /// <param name="collectingTeam">The team collecting the coin</param>
    /// <returns>The point value for that team</returns>
    public int GetValueForTeam(string collectingTeam)
    {
        if (string.IsNullOrEmpty(collectingTeam))
        {
            Debug.LogWarning($"CoinData.GetValueForTeam called with empty team name!");
            return 0;
        }

        // Normalize the team name to handle different conventions
        string normalizedTeam = collectingTeam.ToLower().Trim();

        // Team 1 variants: "Team1", "team1", "Blue", "blue"
        if (normalizedTeam == "team1" || normalizedTeam == "blue")
        {
            return team1Value;
        }
        // Team 2 variants: "Team2", "team2", "Red", "red"
        else if (normalizedTeam == "team2" || normalizedTeam == "red")
        {
            return team2Value;
        }
        // Team 3 (AI) - should not collect coins, but return 0 if they somehow do
        else if (normalizedTeam == "team3")
        {
            Debug.LogWarning($"Team3 (AI) is trying to collect a coin - AI should not collect coins!");
            return 0;
        }

        // If no match found, log warning and return 0
        Debug.LogWarning($"Unrecognized team: '{collectingTeam}' (normalized: '{normalizedTeam}'). Expected Team1, Team2, Team3, Blue, or Red.");
        return 0;
    }

    /// <summary>
    /// Helper method to get the opposite team's value
    /// Useful for cross-team coin mechanics
    /// </summary>
    public int GetOppositeTeamValue(string collectingTeam)
    {
        string normalizedTeam = collectingTeam.ToLower().Trim();

        // If Team1/Blue is collecting, return Team2 value
        if (normalizedTeam == "team1" || normalizedTeam == "blue")
        {
            return team2Value;
        }
        // If Team2/Red is collecting, return Team1 value
        else if (normalizedTeam == "team2" || normalizedTeam == "red")
        {
            return team1Value;
        }

        return 0;
    }
}