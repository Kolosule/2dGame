using System;
using UnityEngine;
using Fusion;
using Game.Combat.Core;

/// <summary>
/// The single networked source of truth for a player's team, and the team-driven sprite
/// colorizing that derives from it. The [Networked] Team replicates to all clients; an OnChanged
/// render callback (no per-tick polling) refreshes the visual. Authoritative changes happen only
/// under state authority.
/// </summary>
public class PlayerTeamData : NetworkBehaviour
{
    /// <summary>The authoritative team for this player. Replicated to all clients.</summary>
    [Networked, OnChangedRender(nameof(OnTeamChangedRender))]
    public Team Team { get; private set; }

    /// <summary>Fires whenever the networked Team changes to a real value (Team1/Team2/Team3AI),
    /// including the initial value a late joiner receives. This event is raised from the simulation
    /// path, never from OnChangedRender, because FriendlyCollision uses it for Physics2D rules.</summary>
    public event Action TeamChanged;

    [Header("Visual Feedback (Optional)")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private bool colorizePlayer = true;

    private TeamChangeTracker teamChangeTracker;

    public override void Spawned()
    {
        ObserveTeamForGameplay();
        OnTeamChangedRender();
    }

    public override void FixedUpdateNetwork()
    {
        // Proxies learn later team changes through replication. Observe them here so local Physics2D
        // collision rules never depend on Render/OnChangedRender being called.
        ObserveTeamForGameplay();
    }

    /// <summary>Server-only: assign this player's team. Rejects None and the AI team.</summary>
    public void SetTeam(Team team)
    {
        if (!TeamUtil.IsPlayerTeam(team))
        {
            Debug.LogError($"Invalid player team assignment: {team}. Must be Team1 or Team2.");
            return;
        }

        if (!Object.HasStateAuthority)
        {
            Debug.LogWarning("Only the state authority can set team assignment!");
            return;
        }

        Team = team;

        // Apply collision rules immediately on state authority. Remote peers observe the replicated
        // value from FixedUpdateNetwork; visual color remains an OnChangedRender responsibility.
        ObserveTeamForGameplay();

        // Mirror into the central stats table so the scoreboard can group by team regardless of
        // AoI distance. Harmless no-op on the very first call at spawn (before RegisterPlayer has
        // created the entry) -- RegisterPlayer sets the initial Team directly from the already-
        // resolved team int, so this mirror only matters for a LATER reassignment.
        if (MatchStatsManager.Instance != null)
            MatchStatsManager.Instance.SetTeam(Object.InputAuthority.PlayerId, TeamUtil.ToNumber(team));
    }

    /// <summary>Render-time callback: refresh only the team color.</summary>
    private void OnTeamChangedRender()
    {
        if (DedicatedServerPresentation.IsHeadless) return;
        ApplyTeamColor();
    }

    private void ObserveTeamForGameplay()
    {
        if (teamChangeTracker.Observe(TeamUtil.ToNumber(Team)))
            TeamChanged?.Invoke();
    }

    private void ApplyTeamColor()
    {
        if (Team == Team.None || !colorizePlayer || spriteRenderer == null || TeamManager.Instance == null) return;

        TeamData data = TeamManager.Instance.GetTeamData(Team);
        if (data != null)
        {
            spriteRenderer.color = data.teamColor;
        }
    }

    public bool IsSameTeam(PlayerTeamData other)
    {
        return other != null && Team != Team.None && Team == other.Team;
    }

    public bool IsOnTeam(Team team)
    {
        return Team == team;
    }
}
