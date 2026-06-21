using UnityEngine;
using Fusion;

[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerCombat))]
public class PlayerController : NetworkBehaviour
{
    private PlayerMovement movement;
    private PlayerCombat combat;
    private NetworkButtons previousButtons;

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        combat = GetComponent<PlayerCombat>();
    }

    public override void Spawned()
    {
        // Bind the camera to the local player only.
        if (HasInputAuthority)
        {
            CameraFollow cam = FindFirstObjectByType<CameraFollow>();
            if (cam != null) cam.SetTarget(transform);
        }

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

            // Respect the death/respawn freeze: PlayerStatsHandler disables these
            // components to lock controls. Since we call them directly (not via Fusion),
            // we must honor their enabled flag here.
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
        while (myTeam.Team == 0 && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
        if (myTeam.Team == 0) yield break;

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
