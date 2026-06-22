using UnityEngine;
using Fusion;

/// <summary>
/// The single networked source of truth for a player's team. The [Networked] Team replicates to
/// all clients; an OnChanged render callback (no per-tick polling) pushes updates into the
/// gameplay/visual component. Authoritative changes happen only under state authority.
/// </summary>
public class PlayerTeamData : NetworkBehaviour
{
    /// <summary>The authoritative team for this player. Replicated to all clients.</summary>
    [Networked, OnChangedRender(nameof(OnTeamChanged))]
    public Team Team { get; set; }

    private PlayerTeamComponent playerTeamComponent;

    private void Awake()
    {
        playerTeamComponent = GetComponent<PlayerTeamComponent>();
        if (playerTeamComponent == null)
        {
            Debug.LogError("PlayerTeamData requires PlayerTeamComponent on the same GameObject!");
        }
    }

    public override void Spawned()
    {
        // OnChangedRender does not fire for the value a late joiner receives as initial state,
        // so initialize gameplay/visuals once here.
        OnTeamChanged();
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

        // Apply immediately on the authority; remote clients get it via OnChangedRender.
        OnTeamChanged();
    }

    /// <summary>Render-time callback: refresh gameplay/visuals from the networked value.</summary>
    private void OnTeamChanged()
    {
        if (playerTeamComponent != null)
        {
            playerTeamComponent.OnTeamChanged();
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
