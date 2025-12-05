using UnityEngine;

/// <summary>
/// Attach this to player characters to define their team and get territorial bonuses
/// </summary>
public class PlayerTeamComponent : MonoBehaviour
{
    [Header("Team Assignment")]
    [Tooltip("Which team this player belongs to (Team1 or Team2)")]
    public string teamID = "Team1";

    [Header("Visual Feedback (Optional)")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private bool colorizePlayer = true;

    private bool isInitialized = false;

    private void Start()
    {
        // Don't initialize visuals until team is properly set
        StartCoroutine(WaitForTeamAssignment());
    }

    private System.Collections.IEnumerator WaitForTeamAssignment()
    {
        // Wait for team to be assigned by NetworkPlayerWrapper
        int waitCount = 0;
        while (string.IsNullOrEmpty(teamID) && waitCount < 50)
        {
            yield return new WaitForSeconds(0.1f);
            waitCount++;
        }

        if (!string.IsNullOrEmpty(teamID) && teamID != "Team1")
        {
            InitializeVisuals();
        }
    }

    private void InitializeVisuals()
    {
        if (isInitialized) return;

        // Optional: Color the player based on team
        if (colorizePlayer && spriteRenderer != null && TeamManager.Instance != null)
        {
            TeamData teamData = TeamManager.Instance.GetTeamData(teamID);
            if (teamData != null)
            {
                spriteRenderer.color = teamData.teamColor;
                Debug.Log($"✓ Player colored for team: {teamData.teamName}");
            }
        }

        isInitialized = true;
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
    /// </summary>
    private float CalculateTerritorialAdvantage()
    {
        if (TeamManager.Instance == null)
        {
            Debug.LogWarning("TeamManager not found in CalculateTerritorialAdvantage!");
            return 0f;
        }

        if (string.IsNullOrEmpty(teamID))
        {
            Debug.LogWarning("teamID is empty in CalculateTerritorialAdvantage!");
            return 0f;
        }

        TeamData myTeam = TeamManager.Instance.GetTeamData(teamID);
        if (myTeam == null)
        {
            Debug.LogWarning($"Could not find team data for teamID: '{teamID}'");
            return 0f;
        }

        // Get opposing team
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
            Debug.LogWarning("Could not determine opposing team!");
            return 0f;
        }

        TeamData enemyTeam = TeamManager.Instance.GetTeamData(opposingTeamID);
        if (enemyTeam == null)
        {
            Debug.LogWarning($"Could not find enemy team data for: {opposingTeamID}");
            return 0f;
        }

        // Calculate distances
        float distToOwnBase = Vector2.Distance(transform.position, myTeam.basePosition);
        float distToEnemyBase = Vector2.Distance(transform.position, enemyTeam.basePosition);
        float totalDist = distToOwnBase + distToEnemyBase;

        if (totalDist < 0.01f) // Avoid division by zero
            return 0f;

        // Calculate advantage: -1 at enemy base, +1 at own base, 0 at midpoint
        float advantage = 1f - (2f * distToOwnBase / totalDist);

        return Mathf.Clamp(advantage, -1f, 1f);
    }

    /// <summary>
    /// Get current territorial advantage (for UI display)
    /// </summary>
    public float GetCurrentTerritorialAdvantage()
    {
        return CalculateTerritorialAdvantage();
    }
}