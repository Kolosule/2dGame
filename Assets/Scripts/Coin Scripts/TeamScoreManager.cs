using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Singleton manager that tracks team scores and unlocks territory buffs.
/// Place this on an empty GameObject in your scene (only one needed).
/// NOW COMPATIBLE WITH NETWORK TEAM NAMES (Team1/Team2)
/// </summary>
public class TeamScoreManager : MonoBehaviour
{
    [Header("Score Tracking")]
    [SerializeField] private int team1Score = 0;
    [SerializeField] private int team2Score = 0;

    [Header("Milestone Thresholds")]
    [Tooltip("Score needed to unlock damage buff (removes 0.5x territory debuff)")]
    [SerializeField] private int damageBuffThreshold = 50;

    [Tooltip("Score needed to unlock defense buff (removes 0.5x territory debuff)")]
    [SerializeField] private int defenseBuffThreshold = 100;

    [Header("Buff Status")]
    [SerializeField] private bool team1DamageBuff = false;
    [SerializeField] private bool team2DamageBuff = false;
    [SerializeField] private bool team1DefenseBuff = false;
    [SerializeField] private bool team2DefenseBuff = false;

    // Events that fire when milestones are reached (optional, for effects/UI)
    public UnityEvent<string> onDamageBuffUnlocked;
    public UnityEvent<string> onDefenseBuffUnlocked;

    // Singleton instance
    private static TeamScoreManager instance;

    private void Awake()
    {
        // Ensure only one instance exists
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            //Debug.LogWarning("Multiple TeamScoreManagers detected! Destroying duplicate.");
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Adds points to a team's score and checks for milestone unlocks
    /// Handles multiple team naming conventions: Team1/Blue and Team2/Red
    /// </summary>
    /// <param name="team">The team receiving points</param>
    /// <param name="points">Number of points to add</param>
    public void AddPoints(string team, int points)
    {
        // Normalize team name
        bool isTeam1 = IsTeam1(team);
        bool isTeam2 = IsTeam2(team);

        if (isTeam1)
        {
            team1Score += points;
            //Debug.Log($"Team1 score: {team1Score} (+{points})");
            CheckMilestones("Team1");
        }
        else if (isTeam2)
        {
            team2Score += points;
           // Debug.Log($"Team2 score: {team2Score} (+{points})");
            CheckMilestones("Team2");
        }
        else
        {
           // Debug.LogError($"Unrecognized team: '{team}'. Expected Team1, Team2, Blue, or Red.");
        }

        // Update UI
        UpdateUI();
    }

    /// <summary>
    /// Checks if a team name refers to Team 1 (Blue)
    /// </summary>
    private bool IsTeam1(string team)
    {
        if (string.IsNullOrEmpty(team)) return false;
        string normalized = team.ToLower().Trim();
        return normalized == "team1" || normalized == "blue";
    }

    /// <summary>
    /// Checks if a team name refers to Team 2 (Red)
    /// </summary>
    private bool IsTeam2(string team)
    {
        if (string.IsNullOrEmpty(team)) return false;
        string normalized = team.ToLower().Trim();
        return normalized == "team2" || normalized == "red";
    }

    /// <summary>
    /// Checks if team has reached any milestones and unlocks buffs
    /// </summary>
    private void CheckMilestones(string team)
    {
        bool isTeam1 = team == "Team1";
        int teamScore = isTeam1 ? team1Score : team2Score;

        // Check damage buff milestone (50 points)
        if (teamScore >= damageBuffThreshold)
        {
            if (isTeam1 && !team1DamageBuff)
            {
                team1DamageBuff = true;
                //Debug.Log("<color=blue>TEAM 1 UNLOCKED DAMAGE BUFF!</color> Territory damage now 1.0x");
                onDamageBuffUnlocked?.Invoke("Team1");
            }
            else if (!isTeam1 && !team2DamageBuff)
            {
                team2DamageBuff = true;
                //Debug.Log("<color=red>TEAM 2 UNLOCKED DAMAGE BUFF!</color> Territory damage now 1.0x");
                onDamageBuffUnlocked?.Invoke("Team2");
            }
        }

        // Check defense buff milestone (100 points)
        if (teamScore >= defenseBuffThreshold)
        {
            if (isTeam1 && !team1DefenseBuff)
            {
                team1DefenseBuff = true;
                //Debug.Log("<color=blue>TEAM 1 UNLOCKED DEFENSE BUFF!</color> Territory damage taken now 1.0x");
                onDefenseBuffUnlocked?.Invoke("Team1");
            }
            else if (!isTeam1 && !team2DefenseBuff)
            {
                team2DefenseBuff = true;
                //Debug.Log("<color=red>TEAM 2 UNLOCKED DEFENSE BUFF!</color> Territory damage taken now 1.0x");
                onDefenseBuffUnlocked?.Invoke("Team2");
            }
        }
    }

    /// <summary>
    /// Gets the damage multiplier for a team in their territory
    /// </summary>
    public float GetTerritoryDamageMultiplier(string team)
    {
        bool isTeam1 = IsTeam1(team);

        if (isTeam1)
        {
            return team1DamageBuff ? 1.0f : 0.5f;
        }
        else
        {
            return team2DamageBuff ? 1.0f : 0.5f;
        }
    }

    /// <summary>
    /// Gets the damage taken multiplier for a team in their territory
    /// </summary>
    public float GetTerritoryDefenseMultiplier(string team)
    {
        bool isTeam1 = IsTeam1(team);

        if (isTeam1)
        {
            return team1DefenseBuff ? 1.0f : 0.5f;
        }
        else
        {
            return team2DefenseBuff ? 1.0f : 0.5f;
        }
    }

    /// <summary>
    /// Gets a team's current score
    /// </summary>
    public int GetTeamScore(string team)
    {
        bool isTeam1 = IsTeam1(team);
        return isTeam1 ? team1Score : team2Score;
    }

    /// <summary>
    /// Public properties for easy access
    /// </summary>
    public int Team1Score => team1Score;
    public int Team2Score => team2Score;
    public bool Team1DamageBuff => team1DamageBuff;
    public bool Team2DamageBuff => team2DamageBuff;
    public bool Team1DefenseBuff => team1DefenseBuff;
    public bool Team2DefenseBuff => team2DefenseBuff;

    // Legacy property names for backward compatibility
    public int RedTeamScore => team2Score;
    public int BlueTeamScore => team1Score;
    public bool RedTeamDamageBuff => team2DamageBuff;
    public bool BlueTeamDamageBuff => team1DamageBuff;
    public bool RedTeamDefenseBuff => team2DefenseBuff;
    public bool BlueTeamDefenseBuff => team1DefenseBuff;

    /// <summary>
    /// Updates the UI display
    /// </summary>
    private void UpdateUI()
    {
        UIManager uiManager = FindObjectOfType<UIManager>();
        if (uiManager != null)
        {
            uiManager.UpdateTeamScores();
        }
    }
}