using UnityEngine;

public class TeamManager : MonoBehaviour
{
    public static TeamManager Instance { get; private set; }

    [Header("Team Configuration")]
    [SerializeField] private TeamData team1Data;
    [SerializeField] private TeamData team2Data;
    [SerializeField] private TeamData team3Data; // AI/NPC team

    [Header("Damage Scaling")]
    [Tooltip("Minimum damage multiplier when at enemy base (default: 0.5 = 50%)")]
    [SerializeField] private float minDamageMultiplier = 0.5f;

    [Tooltip("Maximum damage multiplier when at own base (default: 1.5 = 150%)")]
    [SerializeField] private float maxDamageMultiplier = 1.5f;

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

    /// <summary>Damage dealt modifier for an attacking team given its territorial advantage.</summary>
    public float GetDamageDealtModifier(Team attacker, float territorialAdvantage)
    {
        if (attacker == Team.Team3AI && !aiUsesTerritory) return 1.0f;
        territorialAdvantage = Mathf.Clamp(territorialAdvantage, -1f, 1f);
        float normalizedValue = (territorialAdvantage + 1f) / 2f;
        return Mathf.Lerp(minDamageMultiplier, maxDamageMultiplier, normalizedValue);
    }

    /// <summary>Damage received modifier for a defending team given its territorial advantage.</summary>
    public float GetDamageReceivedModifier(Team defender, float territorialAdvantage)
    {
        if (defender == Team.Team3AI && !aiUsesTerritory) return 1.0f;
        return GetDamageDealtModifier(defender, -territorialAdvantage);
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