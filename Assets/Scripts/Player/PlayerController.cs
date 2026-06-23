using UnityEngine;
using Fusion;

[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerCombat))]
public class PlayerController : NetworkBehaviour
{
    private PlayerMovement movement;
    private PlayerCombat combat;
    private PlayerStatsHandler stats;
    private Rigidbody2D rb;
    private NetworkButtons previousButtons;

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        combat = GetComponent<PlayerCombat>();
        stats = GetComponent<PlayerStatsHandler>();
        rb = GetComponent<Rigidbody2D>();
    }

    public override void Spawned()
    {
        // The gameplay camera (PlayerCamera) self-finds the local player via
        // HasInputAuthority, so no explicit camera binding is needed here.
        StartCoroutine(SetupTeammateCollisionsWhenReady());
    }

    public override void FixedUpdateNetwork()
    {
        if (GetInput(out NetInput input))
        {
            NetworkButtons current = input.Buttons;
            NetworkButtons pressed = current.GetPressed(previousButtons);
            NetworkButtons released = current.GetReleased(previousButtons);
            previousButtons = current;

            // Death freeze, gated on the networked IsDead so it is authoritative and
            // resimulation-safe (no reliance on RPC-toggled component enabled flags). Zero the
            // velocity and gravity so the corpse stays put; PlayerMovement.Simulate restores
            // gravityScale on the first live tick after respawn.
            if (stats != null && stats.IsDead)
            {
                if (rb != null)
                {
                    rb.linearVelocity = Vector2.zero;
                    rb.gravityScale = 0f;
                }
                return;
            }

            // Honor either component being disabled (we drive them directly, not via Fusion).
            if (movement.enabled) movement.Simulate(input, pressed, released);
            if (combat.enabled) combat.Simulate(input, pressed);
        }
    }

    // Ignore collisions between same-team players (replaces NetworkPlayerWrapper's coroutine).
    // Local physics decision; identical on every client because team data is networked.
    private System.Collections.IEnumerator SetupTeammateCollisionsWhenReady()
    {
        PlayerTeamData myTeam = GetComponent<PlayerTeamData>();
        Collider2D myCol = GetComponent<Collider2D>();
        if (myTeam == null || myCol == null) yield break;

        float timeout = 5f;
        while (myTeam.Team == Team.None && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
        if (myTeam.Team == Team.None) yield break;

        var players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        foreach (var other in players)
        {
            if (other == this) continue;
            PlayerTeamData otherTeam = other.GetComponent<PlayerTeamData>();
            Collider2D otherCol = other.GetComponent<Collider2D>();
            if (otherTeam != null && otherCol != null && otherTeam.Team == myTeam.Team)
                Physics2D.IgnoreCollision(myCol, otherCol, true);
        }
    }
}
