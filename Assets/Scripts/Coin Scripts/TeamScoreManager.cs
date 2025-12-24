using UnityEngine;
using UnityEngine.Events;
using Fusion;

/// <summary>
/// DIAGNOSTIC VERSION - Added extensive logging to troubleshoot score not updating
/// Singleton manager that tracks team scores and unlocks territory buffs.
/// Place this on an empty GameObject in your scene (only one needed).
/// PHOTON FUSION VERSION - Compatible with network team names (Team1/Team2)
/// </summary>
public class TeamScoreManager : NetworkBehaviour
{
    [Header("Score Tracking")]
    [Networked] public int Team1Score { get; set; }
    [Networked] public int Team2Score { get; set; }

    [Header("Milestone Thresholds")]
    [Tooltip("Score needed to unlock damage buff (removes 0.5x territory debuff)")]
    [SerializeField] private int damageBuffThreshold = 50;

    [Tooltip("Score needed to unlock defense buff (removes 0.5x territory debuff)")]
    [SerializeField] private int defenseBuffThreshold = 100;

    [Header("Buff Status")]
    [Networked] public bool Team1DamageBuff { get; set; }
    [Networked] public bool Team2DamageBuff { get; set; }
    [Networked] public bool Team1DefenseBuff { get; set; }
    [Networked] public bool Team2DefenseBuff { get; set; }

    // Events that fire when milestones are reached (optional, for effects/UI)
    public UnityEvent<string> onDamageBuffUnlocked;
    public UnityEvent<string> onDefenseBuffUnlocked;

    // Singleton instance
    private static TeamScoreManager instance;

    public static TeamScoreManager Instance => instance;

    private void Awake()
    {
        // Ensure only one instance exists
        if (instance == null)
        {
            instance = this;
            Debug.Log("✓ TeamScoreManager instance created");
        }
        else
        {
            Debug.LogWarning("Multiple TeamScoreManagers detected! Destroying duplicate.");
            Destroy(gameObject);
        }
    }

    public override void Spawned()
    {
        Debug.Log($"[TeamScoreManager] Spawned! HasStateAuthority: {HasStateAuthority}");
        Debug.Log($"[TeamScoreManager] Object.IsValid: {Object != null && Object.IsValid}");
        Debug.Log($"[TeamScoreManager] Initial scores - Team1: {Team1Score}, Team2: {Team2Score}");
    }

    /// <summary>
    /// Adds points to a team's score and checks for milestone unlocks
    /// Handles multiple team naming conventions: Team1/Blue and Team2/Red
    /// RPC so any client can request adding points, but only server executes
    /// </summary>
    /// <param name="team">The team receiving points</param>
    /// <param name="points">Number of points to add</param>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_AddPoints(string team, int points)
    {
        Debug.Log($"[TeamScoreManager] ===== RPC_AddPoints CALLED =====");
        Debug.Log($"[TeamScoreManager] Team: '{team}', Points: {points}");
        Debug.Log($"[TeamScoreManager] HasStateAuthority: {HasStateAuthority}");
        Debug.Log($"[TeamScoreManager] Object null? {Object == null}");

        if (Object != null)
        {
            Debug.Log($"[TeamScoreManager] Object.IsValid: {Object.IsValid}");
        }

        // Only execute on server/state authority
        if (!HasStateAuthority)
        {
            Debug.LogWarning("[TeamScoreManager] RPC_AddPoints called on CLIENT - should only run on SERVER. Returning.");
            return;
        }

        Debug.Log("[TeamScoreManager] Running on SERVER - processing points...");

        // Normalize team name
        bool isTeam1 = IsTeam1(team);
        bool isTeam2 = IsTeam2(team);

        Debug.Log($"[TeamScoreManager] IsTeam1: {isTeam1}, IsTeam2: {isTeam2}");

        if (isTeam1)
        {
            int oldScore = Team1Score;
            Team1Score += points;
            Debug.Log($"[SERVER] ✓ Team1 score updated: {oldScore} → {Team1Score} (+{points})");
            CheckMilestones("Team1");
        }
        else if (isTeam2)
        {
            int oldScore = Team2Score;
            Team2Score += points;
            Debug.Log($"[SERVER] ✓ Team2 score updated: {oldScore} → {Team2Score} (+{points})");
            CheckMilestones("Team2");
        }
        else
        {
            Debug.LogError($"[SERVER] ❌ Unrecognized team: '{team}'. Expected Team1, Team2, Blue, or Red.");
        }

        // Update UI on all clients
        UpdateUI();
    }

    /// <summary>
    /// Local version for backward compatibility - calls RPC
    /// </summary>
    public void AddPoints(string team, int points)
    {
        Debug.Log($"[TeamScoreManager] AddPoints (local wrapper) called for team '{team}' with {points} points");
        RPC_AddPoints(team, points);
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
    /// Only runs on server/state authority
    /// </summary>
    private void CheckMilestones(string team)
    {
        if (!HasStateAuthority) return;

        bool isTeam1 = team == "Team1";
        int teamScore = isTeam1 ? Team1Score : Team2Score;

        Debug.Log($"[SERVER] Checking milestones for {team}: Score={teamScore}");

        // Check damage buff milestone (50 points)
        if (teamScore >= damageBuffThreshold)
        {
            if (isTeam1 && !Team1DamageBuff)
            {
                Team1DamageBuff = true;
                Debug.Log($"[SERVER] <color=blue>TEAM 1 UNLOCKED DAMAGE BUFF!</color> Territory damage now 1.0x");
                onDamageBuffUnlocked?.Invoke("Team1");
            }
            else if (!isTeam1 && !Team2DamageBuff)
            {
                Team2DamageBuff = true;
                Debug.Log($"[SERVER] <color=red>TEAM 2 UNLOCKED DAMAGE BUFF!</color> Territory damage now 1.0x");
                onDamageBuffUnlocked?.Invoke("Team2");
            }
        }

        // Check defense buff milestone (100 points)
        if (teamScore >= defenseBuffThreshold)
        {
            if (isTeam1 && !Team1DefenseBuff)
            {
                Team1DefenseBuff = true;
                Debug.Log($"[SERVER] <color=blue>TEAM 1 UNLOCKED DEFENSE BUFF!</color> Territory damage taken now 1.0x");
                onDefenseBuffUnlocked?.Invoke("Team1");
            }
            else if (!isTeam1 && !Team2DefenseBuff)
            {
                Team2DefenseBuff = true;
                Debug.Log($"[SERVER] <color=red>TEAM 2 UNLOCKED DEFENSE BUFF!</color> Territory damage taken now 1.0x");
                onDefenseBuffUnlocked?.Invoke("Team2");
            }
        }
    }

    /// <summary>
    /// Updates the UI - called on all clients
    /// </summary>
    private void UpdateUI()
    {
        Debug.Log($"[TeamScoreManager] UpdateUI called - Team1: {Team1Score}, Team2: {Team2Score}");

        // The UIManager should automatically pick up the changed values
        // since Team1Score and Team2Score are [Networked] properties
    }

    /// <summary>
    /// Gets the damage multiplier for a team in their territory
    /// </summary>
    public float GetTerritoryDamageMultiplier(string team)
    {
        bool isTeam1 = IsTeam1(team);

        if (isTeam1)
        {
            return Team1DamageBuff ? 1.0f : 0.5f;
        }
        else
        {
            return Team2DamageBuff ? 1.0f : 0.5f;
        }
    }

    /// <summary>
    /// Gets the damage resistance multiplier for a team in their territory
    /// </summary>
    public float GetTerritoryDefenseMultiplier(string team)
    {
        bool isTeam1 = IsTeam1(team);

        if (isTeam1)
        {
            return Team1DefenseBuff ? 1.0f : 0.5f;
        }
        else
        {
            return Team2DefenseBuff ? 1.0f : 0.5f;
        }
    }
}