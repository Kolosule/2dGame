using UnityEngine;
using Game.Combat.Core;

public class TeamManager : MonoBehaviour
{
    public static TeamManager Instance { get; private set; }

    [Header("Team Configuration")]
    [SerializeField] private TeamData team1Data;
    [SerializeField] private TeamData team2Data;
    [SerializeField] private TeamData team3Data; // AI/NPC team

    [Header("AI Team Behavior")]
    [Tooltip("Does Team3 (AI) use territorial advantage? If false, always uses 1.0x modifier")]
    [SerializeField] private bool aiUsesTerritory = false;

    private void Awake()
    {
        // Singleton pattern - initialize as early as possible
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Validate team data
        if (team1Data == null)
            Debug.LogError("⚠️ Team1Data not assigned in TeamManager!");

        if (team2Data == null)
            Debug.LogError("⚠️ Team2Data not assigned in TeamManager!");
    }

    // ---- Enum-keyed API. Bridges to the configured TeamData assets via TeamUtil. ----

    /// <summary>Get the TeamData asset for a Team enum value.</summary>
    public TeamData GetTeamData(Team team)
    {
        if (team == Team.None) return null;
        if (team1Data != null && TeamUtil.Normalize(team1Data.teamID) == team) return team1Data;
        if (team2Data != null && TeamUtil.Normalize(team2Data.teamID) == team) return team2Data;
        if (team3Data != null && TeamUtil.Normalize(team3Data.teamID) == team) return team3Data;
        return null;
    }

    /// <summary>
    /// Damage dealt modifier for an attacking team: x1 everywhere except the enemy third, where the
    /// quantized territorial debuff applies, lifted in halves by the team's Vanguard tier.
    /// Only quantizes GetTerritorialAdvantage's output — the advantage formula itself is unchanged.
    /// There is deliberately NO received-side counterpart: one debuff, one side, one direction.
    /// </summary>
    public float GetDamageDealtModifier(Team attacker, float territorialAdvantage, int vanguardTier)
    {
        if (attacker == Team.Team3AI && !aiUsesTerritory) return 1.0f;
        return TerritorialCombat.DealtMultiplier(territorialAdvantage, vanguardTier);
    }

    /// <summary>
    /// Distance-based territorial advantage for a team at a world position:
    /// +1 at own base, -1 at enemy base, 0 at midpoint (or when data is missing).
    /// Single source of the formula — players and the unified damage pipeline both use it.
    /// </summary>
    public float GetTerritorialAdvantage(Team team, Vector2 position)
    {
        if (team == Team.None) return 0f;

        TeamData myTeam = GetTeamData(team);
        if (myTeam == null) return 0f;

        Team opposing = team == Team.Team1 ? Team.Team2 : Team.Team1;
        TeamData enemyTeam = GetTeamData(opposing);
        if (enemyTeam == null) return 0f;

        float distToOwnBase = Vector2.Distance(position, myTeam.basePosition);
        float distToEnemyBase = Vector2.Distance(position, enemyTeam.basePosition);
        float totalDist = distToOwnBase + distToEnemyBase;
        if (totalDist < 0.01f) return 0f;

        float advantage = 1f - (2f * distToOwnBase / totalDist);
        return Mathf.Clamp(advantage, -1f, 1f);
    }

    /// <summary>PvPvE: distinct assigned teams are hostile.</summary>
    public bool AreEnemies(Team a, Team b)
    {
        return TeamUtil.AreEnemies(a, b);
    }

    /// <summary>True if the team is the AI team.</summary>
    public bool IsAITeam(Team team)
    {
        return team == Team.Team3AI;
    }

    /// <summary>The two human teams.</summary>
    public Team[] GetPlayerTeamsEnum()
    {
        return new Team[] { Team.Team1, Team.Team2 };
    }
}