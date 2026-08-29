using System.Collections.Generic;
using Fusion;
using UnityEngine;
using Game.Combat.Core;

/// <summary>
/// Suppresses physics collision between same-team players. Local physics decision, computed
/// independently on every peer from the replicated Team -- every client runs its own Physics2D
/// world, so every client must derive the same ignores. PlayerTeamData raises TeamChanged from
/// Spawned/FixedUpdateNetwork (never from Render), covering the initial assignment and later
/// reassignments even when dedicated-server render callbacks are disabled. Only the player's
/// primary non-trigger body collider is affected; trigger colliders are untouched.
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

        if (teamData == null || bodyCollider == null)
            Debug.LogError($"FriendlyCollision on {name} needs PlayerTeamData and Collider2D on the same GameObject.", this);
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
            bool sameTeam = FriendlyCollisionRules.ShouldIgnore(
                TeamUtil.ToNumber(myTeam),
                TeamUtil.ToNumber(otherTeam));
            Physics2D.IgnoreCollision(bodyCollider, other.bodyCollider, sameTeam);
        }
    }
}
