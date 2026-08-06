using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Suppresses physics collision between same-team players. Local physics decision, computed
/// independently on every peer from the replicated Team -- every client runs its own Physics2D
/// world, so every client must derive the same ignores. Re-derives on every
/// PlayerTeamData.TeamChanged (the initial spawn assignment, and any later reassignment), not
/// per frame. Only the player's primary non-trigger body collider is affected; trigger
/// colliders (coin pickup, flag capture, home base) are untouched.
///
/// Replaces PlayerController's old SetupTeammateCollisionsWhenReady coroutine, which gave up
/// permanently if team assignment took longer than a 5-second timeout, and never restored
/// collision if a team was ever reassigned (it only ever called IgnoreCollision(..., true)).
/// </summary>
public class FriendlyCollision : NetworkBehaviour
{
    private static readonly List<FriendlyCollision> Active = new List<FriendlyCollision>();

    private PlayerTeamData teamData;
    private Collider2D bodyCollider;

    private void Awake()
    {
        teamData = GetComponent<PlayerTeamData>();
        bodyCollider = GetComponent<Collider2D>();
    }

    public override void Spawned()
    {
        Active.Add(this);
        if (teamData != null) teamData.TeamChanged += RefreshAllPairs;
        RefreshAllPairs();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (teamData != null) teamData.TeamChanged -= RefreshAllPairs;
        Active.Remove(this);
    }

    /// <summary>Re-derive this player's IgnoreCollision pairing against every other currently
    /// active player. Team.None on either side is never "same team", so collision stays on
    /// until both sides have a real team (fail-safe, matching FriendlyFire's None handling).</summary>
    private void RefreshAllPairs()
    {
        if (bodyCollider == null) return;
        Team myTeam = teamData != null ? teamData.Team : Team.None;

        foreach (FriendlyCollision other in Active)
        {
            if (other == this || other == null || other.bodyCollider == null) continue;

            Team otherTeam = other.teamData != null ? other.teamData.Team : Team.None;
            bool sameTeam = myTeam == otherTeam && myTeam != Team.None;
            Physics2D.IgnoreCollision(bodyCollider, other.bodyCollider, sameTeam);
        }
    }
}
