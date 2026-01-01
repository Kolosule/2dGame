using UnityEngine;

/// <summary>
/// CORRECTED VERSION - Attach this to player characters to define their team
/// This version removes the TerritoryZone dependency and uses TeamManager for all calculations
/// </summary>
public class PlayerTeamComponent : MonoBehaviour
{
    [Header("Team Assignment")]
    [Tooltip("Which team this player belongs to (Team1 or Team2)")]
    public string teamID = "";

    [Header("Visual Feedback (Optional)")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private bool colorizePlayer = true;

    private bool visualsInitialized = false;

    /// <summary>
    /// Call this whenever the team is updated
    /// This will refresh the visual appearance
    /// </summary>
    public void OnTeamChanged()
    {
        if (string.IsNullOrEmpty(teamID))
        {
            Debug.LogWarning("Team ID is empty - cannot initialize visuals");
            return;
        }

        InitializeVisuals();
    }

    /// <summary>
    /// Sets up the visual appearance based on team
    /// </summary>
    private void InitializeVisuals()
    {
        if (visualsInitialized) return;

        // Optional: Color the player based on team
        if (colorizePlayer && spriteRenderer != null && TeamManager.Instance != null)
        {
            TeamData teamData = TeamManager.Instance.GetTeamData(teamID);
            if (teamData != null)
            {
                spriteRenderer.color = teamData.teamColor;
                Debug.Log($"✅ Player colored for team: {teamData.teamName}");
            }
        }

        visualsInitialized = true;
    }

    /// <summary>
    /// Get the damage modifier for this player when attacking based on current position
    /// </summary>
    public float GetDamageDealtModifier()
    {
        if (TeamManager.Instance == null)
        {
            Debug.LogWarning("TeamManager not found! Returning 1.0 damage modifier.");
            return 1f;
        }

        if (string.IsNullOrEmpty(teamID))
        {
            Debug.LogWarning("Player has no team assigned! Returning 1.0 damage modifier.");
            return 1f;
        }

        float territorialAdvantage = CalculateTerritorialAdvantage();
        return TeamManager.Instance.GetDamageDealtModifier(teamID, territorialAdvantage);
    }

    /// <summary>
    /// Get the damage modifier for this player when receiving damage based on current position
    /// </summary>
    public float GetDamageReceivedModifier()
    {
        if (TeamManager.Instance == null)
        {
            Debug.LogWarning("TeamManager not found! Returning 1.0 damage modifier.");
            return 1f;
        }

        if (string.IsNullOrEmpty(teamID))
        {
            Debug.LogWarning("Player has no team assigned! Returning 1.0 damage modifier.");
            return 1f;
        }

        float territorialAdvantage = CalculateTerritorialAdvantage();
        return TeamManager.Instance.GetDamageReceivedModifier(teamID, territorialAdvantage);
    }

    /// <summary>
    /// Calculate territorial advantage based on distance from team bases
    /// Returns -1 at enemy base, 0 at midpoint, +1 at own base
    /// </summary>
    private float CalculateTerritorialAdvantage()
    {
        if (TeamManager.Instance == null)
        {
            return 0f; // Neutral if no TeamManager
        }

        if (string.IsNullOrEmpty(teamID))
        {
            return 0f; // Neutral if no team assigned
        }

        TeamData myTeam = TeamManager.Instance.GetTeamData(teamID);
        if (myTeam == null)
        {
            return 0f;
        }

        // Get opposing team (ignore Team3/AI)
        string[] playerTeams = TeamManager.Instance.GetPlayerTeams();
        string opposingTeamID = null;

        foreach (string team in playerTeams)
        {
            if (team != teamID)
            {
                opposingTeamID = team;
                break;
            }
        }

        if (string.IsNullOrEmpty(opposingTeamID))
        {
            return 0f; // Neutral if can't find opposing team
        }

        TeamData enemyTeam = TeamManager.Instance.GetTeamData(opposingTeamID);
        if (enemyTeam == null)
        {
            return 0f;
        }

        // Calculate distances from both bases
        float distToOwnBase = Vector2.Distance(transform.position, myTeam.basePosition);
        float distToEnemyBase = Vector2.Distance(transform.position, enemyTeam.basePosition);
        float totalDist = distToOwnBase + distToEnemyBase;

        if (totalDist < 0.01f) // Avoid division by zero
            return 0f;

        // Calculate advantage: +1 at own base, -1 at enemy base, 0 at midpoint
        float advantage = 1f - (2f * distToOwnBase / totalDist);

        return Mathf.Clamp(advantage, -1f, 1f);
    }

    /// <summary>
    /// Get current territorial advantage (for UI display or debugging)
    /// </summary>
    public float GetCurrentTerritorialAdvantage()
    {
        return CalculateTerritorialAdvantage();
    }

    /// <summary>
    /// Check if this player is on the same team as another player
    /// </summary>
    public bool IsSameTeam(PlayerTeamComponent otherPlayer)
    {
        if (otherPlayer == null)
            return false;

        return this.teamID == otherPlayer.teamID;
    }

    /// <summary>
    /// Check if this player is on a specific team
    /// </summary>
    public bool IsOnTeam(string checkTeamID)
    {
        return this.teamID == checkTeamID;
    }
}