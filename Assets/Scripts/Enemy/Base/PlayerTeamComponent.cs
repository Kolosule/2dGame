using UnityEngine;

/// <summary>
/// Attach this to player characters to define their team and get territorial bonuses
/// INTEGRATED WITH COIN SYSTEM BUFFS - ROBUST INITIALIZATION!
/// </summary>
public class PlayerTeamComponent : MonoBehaviour
{
    [Header("Team Assignment")]
    [Tooltip("Which team this player belongs to (Team1 or Team2)")]
    public string teamID = "Team1";

    [Header("Visual Feedback (Optional)")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private bool colorizePlayer = true;

    //[Header("Debug")]
    [SerializeField] private bool enableDetailedLogging = true;

    private bool isInitialized = false;
    private TeamScoreManager scoreManager; // Cache the score manager reference

    private void Start()
    {
        // Don't initialize visuals until team is properly set
        StartCoroutine(WaitForTeamAssignment());

        // Try to find TeamScoreManager
        FindScoreManager();
    }

    /// <summary>
    /// Find and cache the TeamScoreManager reference
    /// </summary>
    private void FindScoreManager()
    {
        if (scoreManager == null)
        {
            scoreManager = FindObjectOfType<TeamScoreManager>();

            if (scoreManager == null)
            {
                //Debug.LogError($"⚠️ TeamScoreManager not found in scene! Coin system buffs won't work. Make sure TeamScoreManager exists in your gameplay scene.");
            }
            else
            {
                //Debug.Log($"✓ TeamScoreManager found and cached for {gameObject.name}");
            }
        }
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
                //Debug.Log($"✓ Player colored for team: {teamData.teamName}");
            }
        }

        isInitialized = true;
    }

    /// <summary>
    /// Get the damage modifier for this player when attacking based on current position
    /// APPLIES BUFF IN ENEMY TERRITORY TO ENCOURAGE PUSHES!
    /// </summary>
    public float GetDamageDealtModifier()
    {
        if (TeamManager.Instance == null)
        {
            //Debug.LogWarning("TeamManager not found! Returning 1.0 damage modifier.");
            return 1f;
        }

        if (string.IsNullOrEmpty(teamID))
        {
            //Debug.LogWarning("Player has no team assigned! Returning 1.0 damage modifier.");
            return 1f;
        }

        // Calculate where we are on the map (-1 to +1)
        float territorialAdvantage = CalculateTerritorialAdvantage();

        // Get base modifier from TeamManager (1.5x at +1, 0.5x at -1)
        float baseModifier = TeamManager.Instance.GetDamageDealtModifier(teamID, territorialAdvantage);

        if (enableDetailedLogging)
        {
            //Debug.Log($"=== DAMAGE MODIFIER DEBUG ===");
            //Debug.Log($"Team: {teamID}");
            //Debug.Log($"Territorial Advantage: {territorialAdvantage:F2}");
            //Debug.Log($"Base Modifier (from TeamManager): {baseModifier:F2}x");
        }

        // Check if we're in ENEMY territory (negative advantage)
        if (territorialAdvantage < 0)
        {
            if (enableDetailedLogging)
            {
                //Debug.Log($"In ENEMY territory - checking for damage buff...");
            }

            // Try to find scoreManager if it's null (lazy initialization)
            if (scoreManager == null)
            {
                FindScoreManager();
            }

            // Check if TeamScoreManager exists
            if (scoreManager == null)
            {
                // Don't spam errors - just return base modifier
                if (enableDetailedLogging)
                {
                    //Debug.LogWarning("⚠️ ScoreManager still null - buffs disabled. Create TeamScoreManager in scene!");
                }
                return baseModifier;
            }

            // Get buff multiplier from coin system
            float buffMultiplier = scoreManager.GetTerritoryDamageMultiplier(teamID);

            if (enableDetailedLogging)
            {
                //Debug.Log($"Buff Multiplier (from TeamScoreManager): {buffMultiplier:F2}");
                //Debug.Log($"Team Score: Team1={scoreManager.Team1Score}, Team2={scoreManager.Team2Score}");
            }

            // Apply buff if unlocked (buffMultiplier >= 1.0)
            if (buffMultiplier >= 1.0f)
            {
                float oldModifier = baseModifier;
                baseModifier = Mathf.Max(baseModifier, 1.0f);

               // Debug.Log($"✓ DAMAGE BUFF ACTIVE! {oldModifier:F2}x → {baseModifier:F2}x");
            }
            else
            {
                if (enableDetailedLogging)
                {
                   // Debug.Log($"⚠️ Damage buff NOT active yet (need 50+ points)");
                }
            }
        }
        else if (territorialAdvantage > 0)
        {
            if (enableDetailedLogging)
            {
                //Debug.Log($"In OWN territory - buffs don't apply here");
            }
        }
        else
        {
            if (enableDetailedLogging)
            {
                //Debug.Log($"At MIDPOINT - no buffs needed");
            }
        }

        if (enableDetailedLogging)
        {
           // Debug.Log($"FINAL Damage Modifier: {baseModifier:F2}x");
          //  Debug.Log($"=== END DEBUG ===");
        }

        return baseModifier;
    }

    /// <summary>
    /// Get the damage modifier for this player when receiving damage based on current position
    /// APPLIES BUFF IN ENEMY TERRITORY TO ENCOURAGE PUSHES!
    /// </summary>
    public float GetDamageReceivedModifier()
    {
        if (TeamManager.Instance == null)
        {
            //Debug.LogWarning("TeamManager not found! Returning 1.0 damage modifier.");
            return 1f;
        }

        if (string.IsNullOrEmpty(teamID))
        {
            //Debug.LogWarning("Player has no team assigned! Returning 1.0 damage modifier.");
            return 1f;
        }

        // Calculate where we are on the map (-1 to +1)
        float territorialAdvantage = CalculateTerritorialAdvantage();

        // Get base modifier from TeamManager
        // Defense uses inverse: 0.5x at own base (+1), 1.5x at enemy base (-1)
        float baseModifier = TeamManager.Instance.GetDamageReceivedModifier(teamID, territorialAdvantage);

        // Check if we're in ENEMY territory (negative advantage)
        if (territorialAdvantage < 0)
        {
            // Try to find scoreManager if it's null (lazy initialization)
            if (scoreManager == null)
            {
                FindScoreManager();
            }

            if (scoreManager != null)
            {
                // Get buff multiplier from coin system
                float buffMultiplier = scoreManager.GetTerritoryDefenseMultiplier(teamID);

                // Apply buff if unlocked (buffMultiplier >= 1.0)
                if (buffMultiplier >= 1.0f)
                {
                    float oldModifier = baseModifier;
                    baseModifier = Mathf.Min(baseModifier, 1.0f);

                    if (enableDetailedLogging)
                    {
                        //Debug.Log($"✓ DEFENSE BUFF ACTIVE! {oldModifier:F2}x → {baseModifier:F2}x");
                    }
                }
            }
        }

        return baseModifier;
    }

    /// <summary>
    /// Calculate territorial advantage based on distance from team bases
    /// Returns -1 at enemy base, 0 at midpoint, +1 at own base
    /// </summary>
    private float CalculateTerritorialAdvantage()
    {
        if (TeamManager.Instance == null)
        {
            //Debug.LogWarning("TeamManager not found in CalculateTerritorialAdvantage!");
            return 0f;
        }

        if (string.IsNullOrEmpty(teamID))
        {
            //Debug.LogWarning("teamID is empty in CalculateTerritorialAdvantage!");
            return 0f;
        }

        TeamData myTeam = TeamManager.Instance.GetTeamData(teamID);
        if (myTeam == null)
        {
            //Debug.LogWarning($"Could not find team data for teamID: '{teamID}'");
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
            //Debug.LogWarning("Could not determine opposing team!");
            return 0f;
        }

        TeamData enemyTeam = TeamManager.Instance.GetTeamData(opposingTeamID);
        if (enemyTeam == null)
        {
           // Debug.LogWarning($"Could not find enemy team data for: {opposingTeamID}");
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

    /// <summary>
    /// Check if damage buff is active (for UI display)
    /// </summary>
    public bool IsDamageBuffActive()
    {
        if (scoreManager == null)
        {
            FindScoreManager();
        }
        if (scoreManager == null) return false;
        return scoreManager.GetTerritoryDamageMultiplier(teamID) >= 1.0f;
    }

    /// <summary>
    /// Check if defense buff is active (for UI display)
    /// </summary>
    public bool IsDefenseBuffActive()
    {
        if (scoreManager == null)
        {
            FindScoreManager();
        }
        if (scoreManager == null) return false;
        return scoreManager.GetTerritoryDefenseMultiplier(teamID) >= 1.0f;
    }
}