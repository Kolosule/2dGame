using UnityEngine;
using Game.Combat.Core;

public class TeamManager : MonoBehaviour
{
    public static TeamManager Instance { get; private set; }

    [Header("Team Configuration")]
    [SerializeField] private TeamData team1Data;
    [SerializeField] private TeamData team2Data;
    [SerializeField] private TeamData team3Data; // AI/NPC team

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
    /// Damage-received modifier for a DEFENDING team. The rule is keyed on identity, not
    /// intent: any defender that is not Team1 or Team2 is exempt — always x1.0 — because those
    /// are the only teams with a meaningful home base to measure distance from. In practice
    /// that means Team3AI and Team.None today, but the exemption is NOT structurally tied to
    /// "being an enemy" or "being Team3AI"; a future team value that isn't Team1/Team2 is
    /// exempt too, by construction. Human defenders (Team1/Team2) take the own-base-distance
    /// vulnerability, reduced by their team's Vanguard tier.
    /// </summary>
    public float GetDamageReceivedModifier(Team defender, float ownBaseDistance01, int vanguardTier)
    {
        if (defender != Team.Team1 && defender != Team.Team2) return 1.0f;
        return TerritorialCombat.ReceivedMultiplier(ownBaseDistance01, vanguardTier);
    }

    /// <summary>
    /// A team's distance from their OWN base, normalized 0 (at base) to 1 (at or beyond the
    /// enemy base, clamped). The reference distance is the gap between the two human bases —
    /// no separate arena-bounds asset needed. Returns 0 when team/opposing data is missing.
    /// Single source of the formula — the damage pipeline and the HUD percentage readout both use it.
    /// </summary>
    public float GetOwnBaseDistance01(Team team, Vector2 position)
    {
        if (team == Team.None) return 0f;

        TeamData myTeam = GetTeamData(team);
        if (myTeam == null) return 0f;

        Team opposing = team == Team.Team1 ? Team.Team2 : Team.Team1;
        TeamData enemyTeam = GetTeamData(opposing);
        if (enemyTeam == null) return 0f;

        float maxDistance = Vector2.Distance(myTeam.basePosition, enemyTeam.basePosition);
        if (maxDistance < 0.01f) return 0f;

        float distToOwnBase = Vector2.Distance(position, myTeam.basePosition);
        return Mathf.Clamp01(distToOwnBase / maxDistance);
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
