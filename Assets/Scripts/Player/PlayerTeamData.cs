using UnityEngine;
using Fusion;

/// <summary>
/// FIXED VERSION - Network syncing of team data for Photon Fusion.
/// This handles NETWORK SYNCING of team data.
/// PlayerTeamComponent handles all the GAMEPLAY logic.
/// COMPATIBLE WITH FUSION 2.0+
/// </summary>
public class PlayerTeamData : NetworkBehaviour
{
    #region Networked Properties

    /// <summary>
    /// The team this player belongs to (1 or 2).
    /// [Networked] means this value is synchronized across all clients.
    /// </summary>
    [Networked]
    public int Team { get; set; }

    #endregion

    #region References

    // Reference to the existing PlayerTeamComponent
    private PlayerTeamComponent playerTeamComponent;

    // Track previous team value to detect changes
    private int previousTeam = 0;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // Get reference to PlayerTeamComponent on the same GameObject
        playerTeamComponent = GetComponent<PlayerTeamComponent>();

        if (playerTeamComponent == null)
        {
            Debug.LogError("PlayerTeamData requires PlayerTeamComponent on the same GameObject!");
        }
    }

    #endregion

    #region Fusion Lifecycle

    /// <summary>
    /// Called every network tick - we use this to detect team changes
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        // Check if team has changed
        if (Team != previousTeam && Team != 0)
        {
            Debug.Log($"[NETWORK] Player team changed from {previousTeam} to {Team}");
            UpdatePlayerTeamComponent(Team);
            previousTeam = Team;
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Call this from the server to set the player's team.
    /// Only the server should call this!
    /// </summary>
    public void SetTeam(int teamNumber)
    {
        // Validate team number
        if (teamNumber != 1 && teamNumber != 2)
        {
            Debug.LogError($"Invalid team number: {teamNumber}. Must be 1 or 2.");
            return;
        }

        // Only server can set networked properties
        if (Object.HasStateAuthority)
        {
            Team = teamNumber;
            previousTeam = teamNumber;
            Debug.Log($"[SERVER] Player team set to: {teamNumber}");

            // Update immediately on server
            UpdatePlayerTeamComponent(teamNumber);
        }
        else
        {
            Debug.LogWarning("Only the server can set team assignment!");
        }
    }

    #endregion

    #region Integration with PlayerTeamComponent

    /// <summary>
    /// Updates the existing PlayerTeamComponent with the networked team value.
    /// This bridges the network sync (PlayerTeamData) with gameplay logic (PlayerTeamComponent).
    /// </summary>
    private void UpdatePlayerTeamComponent(int teamNumber)
    {
        if (playerTeamComponent == null)
        {
            Debug.LogError("PlayerTeamComponent not found!");
            return;
        }

        // Convert team number (1 or 2) to teamID string ("Team1" or "Team2")
        string teamID = $"Team{teamNumber}";

        // Update the PlayerTeamComponent's teamID
        playerTeamComponent.teamID = teamID;

        Debug.Log($"✅ Updated PlayerTeamComponent to {teamID}");

        // CRITICAL FIX: Call OnTeamChanged to refresh visuals
        playerTeamComponent.OnTeamChanged();

        // The PlayerTeamComponent will handle:
        // - Setting sprite color based on team
        // - Calculating territorial advantages
        // - Applying damage modifiers
        // - All the existing gameplay logic
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Checks if this player is on the same team as another player.
    /// </summary>
    public bool IsSameTeam(PlayerTeamData otherPlayer)
    {
        if (otherPlayer == null)
            return false;

        return this.Team == otherPlayer.Team;
    }

    /// <summary>
    /// Checks if this player is on a specific team.
    /// </summary>
    public bool IsOnTeam(int teamNumber)
    {
        return this.Team == teamNumber;
    }

    /// <summary>
    /// Get the team ID string (e.g., "Team1" or "Team2")
    /// </summary>
    public string GetTeamID()
    {
        return $"Team{Team}";
    }

    #endregion
}