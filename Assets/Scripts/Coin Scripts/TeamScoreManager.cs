using UnityEngine;
using UnityEngine.Events;
using Fusion;

/// <summary>
/// Singleton manager that tracks team scores and unlocks coin-milestone buffs.
/// META-LAYER MODEL (review item #4): CTF is the win condition; coin milestones lift the
/// territorial combat nerf. HasDamageBuff/HasDefenseBuff feed CombatConfig.ResolveDamage.
/// See docs/superpowers/specs/2026-06-22-unified-damage-pipeline-design.md.
/// Place one on an empty GameObject in the Gameplay scene. PHOTON FUSION networked.
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
        }
        else
        {
            Debug.LogWarning("Multiple TeamScoreManagers detected! Destroying duplicate.");
            Destroy(gameObject);
        }
    }

    public override void Spawned()
    {
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


        // Only execute on server/state authority
        if (!HasStateAuthority)
        {
            Debug.LogWarning("[TeamScoreManager] RPC_AddPoints called on CLIENT - should only run on SERVER. Returning.");
            return;
        }


        // Normalize team name
        Team scoring = TeamUtil.Normalize(team);


        if (scoring == Team.Team1)
        {
            int oldScore = Team1Score;
            Team1Score += points;
            CheckMilestones("Team1");
        }
        else if (scoring == Team.Team2)
        {
            int oldScore = Team2Score;
            Team2Score += points;
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
        RPC_AddPoints(team, points);
    }

    /// <summary>
    /// Checks if team has reached any milestones and unlocks buffs
    /// Only runs on server/state authority
    /// </summary>
    private void CheckMilestones(string team)
    {
        if (!HasStateAuthority) return;

        bool isTeam1 = TeamUtil.Normalize(team) == Team.Team1;
        int teamScore = isTeam1 ? Team1Score : Team2Score;


        // Check damage buff milestone (50 points)
        if (teamScore >= damageBuffThreshold)
        {
            if (isTeam1 && !Team1DamageBuff)
            {
                Team1DamageBuff = true;
                onDamageBuffUnlocked?.Invoke("Team1");
            }
            else if (!isTeam1 && !Team2DamageBuff)
            {
                Team2DamageBuff = true;
                onDamageBuffUnlocked?.Invoke("Team2");
            }
        }

        // Check defense buff milestone (100 points)
        if (teamScore >= defenseBuffThreshold)
        {
            if (isTeam1 && !Team1DefenseBuff)
            {
                Team1DefenseBuff = true;
                onDefenseBuffUnlocked?.Invoke("Team1");
            }
            else if (!isTeam1 && !Team2DefenseBuff)
            {
                Team2DefenseBuff = true;
                onDefenseBuffUnlocked?.Invoke("Team2");
            }
        }
    }

    /// <summary>
    /// Updates the UI - called on all clients
    /// </summary>
    private void UpdateUI()
    {

        // The UIManager should automatically pick up the changed values
        // since Team1Score and Team2Score are [Networked] properties
    }

    /// <summary>True once the team has unlocked its coin-milestone damage buff.</summary>
    public bool HasDamageBuff(Team team)
    {
        if (team == Team.Team1) return Team1DamageBuff;
        if (team == Team.Team2) return Team2DamageBuff;
        return false;
    }

    /// <summary>True once the team has unlocked its coin-milestone defense buff.</summary>
    public bool HasDefenseBuff(Team team)
    {
        if (team == Team.Team1) return Team1DefenseBuff;
        if (team == Team.Team2) return Team2DefenseBuff;
        return false;
    }
}