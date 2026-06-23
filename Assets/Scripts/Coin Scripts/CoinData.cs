using UnityEngine;
using Fusion;

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

    [Header("Drop On Death")]
    [Tooltip("Networked coin prefab to spawn in the world when a player carrying this coin dies. " +
             "Must have a NetworkObject + NetworkedCoinPickup configured with this same CoinData.")]
    public NetworkObject coinPrefab;

    /// <summary>
    /// Gets the point value for a specific team
    /// Handles three team system:
    /// - "Team1" or "Blue" or "team1" = Team 1
    /// - "Team2" or "Red" or "team2" = Team 2
    /// - "Team3" or "team3" = AI team (neutral/enemy to both player teams)
    /// </summary>
    /// <param name="collectingTeam">The team collecting the coin</param>
    /// <returns>The point value for that team</returns>
    public int GetValueForTeam(Team collectingTeam)
    {
        switch (collectingTeam)
        {
            case Team.Team1: return team1Value;
            case Team.Team2: return team2Value;
            case Team.Team3AI:
                Debug.LogWarning("Team3 (AI) tried to collect a coin - AI should not collect coins!");
                return 0;
            default:
                Debug.LogWarning("CoinData.GetValueForTeam called with no team!");
                return 0;
        }
    }
}