using UnityEngine;

/// <summary>
/// Central hub for all game configuration - no code changes needed for tweaks!
/// </summary>
public class GameSettingsManager : MonoBehaviour
{
    public static GameSettingsManager Instance { get; private set; }

    [Header("Combat Configuration")]
    [SerializeField] private CombatConfig combatConfig;

    [Header("Game Rules")]
    [Tooltip("Enable friendly fire between team members")]
    public bool friendlyFireEnabled = false;

    [Tooltip("Enable player vs player combat")]
    public bool pvpEnabled = true;

    [Tooltip("Respawn time multiplier")]
    [Range(0.1f, 5.0f)]
    public float respawnTimeMultiplier = 1.0f;

    [Header("Difficulty Settings")]
    [Tooltip("Global enemy health multiplier")]
    [Range(0.1f, 5.0f)]
    public float enemyHealthMultiplier = 1.0f;

    [Tooltip("Global enemy damage multiplier")]
    [Range(0.1f, 5.0f)]
    public float enemyDamageMultiplier = 1.0f;

    [Tooltip("Enemy spawn rate multiplier (higher = more enemies)")]
    [Range(0.1f, 3.0f)]
    public float enemySpawnRateMultiplier = 1.0f;

    [Header("Economy Settings")]
    [Tooltip("Gold gain multiplier")]
    [Range(0.1f, 5.0f)]
    public float goldMultiplier = 1.0f;

    [Tooltip("Experience gain multiplier")]
    [Range(0.1f, 5.0f)]
    public float experienceMultiplier = 1.0f;

    [Header("Match Settings")]
    [Tooltip("Match time limit in minutes (0 = no limit)")]
    public float matchTimeLimit = 0f;

    [Tooltip("Score limit to win (0 = no limit)")]
    public int scoreLimit = 0;

    [Header("Quality of Life")]
    [Tooltip("Auto-respawn after death")]
    public bool autoRespawn = true;

    [Tooltip("Show mini-map")]
    public bool showMinimap = true;

    [Tooltip("Show damage numbers")]
    public bool showDamageNumbers = true;

    [Tooltip("Camera shake intensity")]
    [Range(0f, 2f)]
    public float cameraShakeIntensity = 1.0f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Get the combat configuration
    /// </summary>
    public CombatConfig GetCombatConfig()
    {
        return combatConfig;
    }

    /// <summary>
    /// Check if two entities can damage each other
    /// </summary>
    public bool CanDamage(string attackerTeam, string defenderTeam)
    {
        if (attackerTeam == defenderTeam)
        {
            return friendlyFireEnabled;
        }

        // Check if it's PvP
        bool bothArePlayers = !IsAITeam(attackerTeam) && !IsAITeam(defenderTeam);

        if (bothArePlayers && !pvpEnabled)
        {
            return false;
        }

        return true;
    }

    private bool IsAITeam(string teamID)
    {
        if (TeamManager.Instance == null) return false;
        return TeamManager.Instance.IsAITeam(teamID);
    }

    /// <summary>
    /// Calculate final respawn time for a team
    /// </summary>
    public float GetRespawnTime(TeamData team)
    {
        return team.respawnDelay * respawnTimeMultiplier;
    }
}