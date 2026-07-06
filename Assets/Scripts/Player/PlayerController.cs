using UnityEngine;
using Fusion;

[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerCombat))]
public class PlayerController : NetworkBehaviour
{
    private PlayerMovement movement;
    private PlayerCombat combat;
    private PlayerStatsHandler stats;
    private PlayerAnimator animator;
    private PlayerBuffs buffs;
    private Rigidbody2D rb;

    // [Networked] so it rewinds with the simulation: during a resimulation Fusion re-runs past
    // ticks, and edge detection (GetPressed/GetReleased) must compare against the button state
    // of the tick being re-run, not the latest one. A plain field here causes double-fired or
    // swallowed presses on the predicting client under real latency.
    [Networked] private NetworkButtons PreviousButtons { get; set; }

    [Header("Area of Interest")]
    [Tooltip("Server-only: radius (world units) around this player that is replicated to them. " +
             "Must exceed the camera's max view half-extent to avoid pop-in at the screen edge. " +
             "Camera base size 5 + speed-zoom can show ~14 units half-width on widescreen, so the " +
             "default 25 leaves margin. Tune with the multi-peer pop-in check.")]
    [SerializeField] private float areaOfInterestRadius = 25f;

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        combat = GetComponent<PlayerCombat>();
        stats = GetComponent<PlayerStatsHandler>();
        animator = GetComponent<PlayerAnimator>();
        buffs = GetComponent<PlayerBuffs>();
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
        // Area of Interest: on the server, register this player's interest region around itself
        // every tick (Fusion clears regions per tick). Drives which objects replicate to this
        // player. No effect until AoI is enabled in the NetworkProjectConfig. Runs regardless of
        // input/alive state so the region never lapses (e.g. while dead/awaiting respawn).
        if (Runner.IsServer)
        {
            Runner.AddPlayerAreaOfInterest(Object.InputAuthority, transform.position, areaOfInterestRadius);

            // While dead, the camera has already moved to the respawn point. Add a second interest
            // region there so the spawn area is replicated before the teleport — otherwise a spawn
            // farther than areaOfInterestRadius from the corpse pops in on respawn. RespawnPosition
            // is networked and set in Die() before the dead window, so it is valid here.
            if (stats != null && stats.IsDead)
                Runner.AddPlayerAreaOfInterest(Object.InputAuthority, stats.RespawnPosition, areaOfInterestRadius);
        }

        if (GetInput(out NetInput input))
        {
            NetworkButtons current = input.Buttons;
            NetworkButtons pressed = current.GetPressed(PreviousButtons);
            NetworkButtons released = current.GetReleased(PreviousButtons);
            PreviousButtons = current;

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
                // Still tick the animator while dead so the Dead state latches (it computes
                // Dead with top priority regardless of movement/combat having run).
                if (animator != null) animator.Simulate();
                return;
            }

            // Honor either component being disabled (we drive them directly, not via Fusion).
            if (movement.enabled) movement.Simulate(input, pressed, released);
            if (combat.enabled) combat.Simulate(input, pressed);
            if (buffs != null) buffs.Simulate(input, pressed);

            // Derive animation state AFTER movement/combat so velocity/dash/stun are current.
            if (animator != null) animator.Simulate();
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
