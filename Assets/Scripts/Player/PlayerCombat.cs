using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Game.Combat.Core;

/// <summary>
/// Tick-based, networked combat. Driven by PlayerController.FixedUpdateNetwork.
/// Melee detection/damage and projectile spawning run under StateAuthority only.
/// Cooldowns are TickTimers so they predict and reconcile correctly.
/// </summary>
public class PlayerCombat : NetworkBehaviour
{
    [Header("Stats")]
    [SerializeField] private PlayerStats stats;

    [Header("Attack Settings")]
    [SerializeField] private LayerMask attackableLayer;
    // Melee damage, knockback strength, and cooldown are driven by PlayerStats
    // (attackDamage / attackForce / attackCooldown) so designers tune them in one place.
    // Upward knockback has no PlayerStats equivalent and stays a per-prefab tuning value.
    [SerializeField] private float knockbackUpward = 5f;

    [Header("Attack Points")]
    [SerializeField] private Transform sideAttackPoint;
    [SerializeField] private Transform upAttackPoint;
    [SerializeField] private Transform downAttackPoint;

    [Header("Attack Areas")]
    [SerializeField] private Vector2 sideAttackArea = new Vector2(1f, 1f);
    [SerializeField] private Vector2 upAttackArea = new Vector2(1f, 1f);
    [SerializeField] private Vector2 downAttackArea = new Vector2(1f, 1f);

    [Header("Ground Pound")]
    [SerializeField] private bool useGroundPound = true;
    [SerializeField] private float groundPoundForce = 20f;
    [SerializeField] private GameObject groundPoundImpactEffect;

    [Header("Projectile Settings")]
    [SerializeField] private NetworkObject projectilePrefab;
    [SerializeField] private Transform projectileSpawnPoint;
    [SerializeField] private int projectileDamage = 15;
    [SerializeField] private float projectileSpeed = 10f;
    [SerializeField] private float projectileScale = 1f;
    [SerializeField] private float projectileCooldown = 0.5f;

    [Header("Shoot Prediction (cosmetic, firing client only)")]
    [Tooltip("Optional muzzle-flash prefab spawned instantly on the firing client. Null = tracer only.")]
    [SerializeField] private GameObject muzzleFlashPrefab;
    [SerializeField] private float muzzleFlashLifetime = 0.2f;
    [Tooltip("Code-generated tracer shown instantly on fire (no art needed).")]
    [SerializeField] private Color tracerColor = new Color(1f, 0.9f, 0.3f, 1f);
    [SerializeField] private float tracerLength = 1.5f;
    [SerializeField] private float tracerWidth = 0.08f;
    [SerializeField] private float tracerDuration = 0.1f;

    [Header("Ground Check (for down attack)")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundLayer;

    private PlayerAnimator playerAnimator;
    private PlayerTeamData teamComponent;
    private PlayerStatsHandler statsHandler;
    private Rigidbody2D rb;
    private PlayerMovement playerMovement;
    private PlayerStatModifiers mods;
    private int verticalAim;
    private Vector2 lastAimWorldPoint;

    // Dash-strike dedup: server-only, non-networked. Cleared on each new dash rising edge.
    private readonly HashSet<Collider2D> dashStruck = new HashSet<Collider2D>();
    private bool wasDashing;

    [Networked] private TickTimer AttackCooldownTimer { get; set; }
    [Networked] private TickTimer ShootCooldownTimer { get; set; }

    // Swing state (spec 2.2): the swing is its start tick + latched aim/facing; the phase is
    // derived per tick via SwingPhase.Resolve, so it predicts and resimulates correctly.
    [Networked] private int AttackStartTick { get; set; }
    [Networked] private int AttackAim { get; set; }
    [Networked] private NetworkBool AttackFacingRight { get; set; }
    [Networked] private NetworkBool AttackIsPound { get; set; }

    // Per-swing hit dedup: server-only, non-networked (same pattern as dashStruck).
    private readonly HashSet<Collider2D> swingStruck = new HashSet<Collider2D>();

    void Awake()
    {
        playerAnimator = GetComponent<PlayerAnimator>();
        teamComponent = GetComponent<PlayerTeamData>();
        statsHandler = GetComponent<PlayerStatsHandler>();
        rb = GetComponent<Rigidbody2D>();
        playerMovement = GetComponent<PlayerMovement>();
        mods = GetComponent<PlayerStatModifiers>();
    }

    /// <summary>Called every tick by PlayerController when input is available.</summary>
    public void Simulate(NetInput input, NetworkButtons pressed)
    {
        verticalAim = input.VerticalAim;
        lastAimWorldPoint = input.AimWorldPoint;

        SwingPhaseKind phase = CurrentSwingPhase();

        if (pressed.IsSet((int)PlayerButton.Melee) && phase == SwingPhaseKind.None &&
            AttackCooldownTimer.ExpiredOrNotRunning(Runner))
        {
            AttackCooldownTimer = TickTimer.CreateFromSeconds(Runner, stats.attackCooldown);
            BeginSwing();
            phase = CurrentSwingPhase(); // now Startup (or Active if startupTicks is 0)
        }

        if (phase == SwingPhaseKind.Active)
        {
            SimulateSwingTick(phase);
        }

        if (pressed.IsSet((int)PlayerButton.Shoot))
        {
            if (ShootCooldownTimer.ExpiredOrNotRunning(Runner))
            {
                ShootCooldownTimer = TickTimer.CreateFromSeconds(Runner, projectileCooldown);
                ShootProjectile(input.AimWorldPoint);
            }
        }

        // Quicker Dash tier 3: deal melee damage in the front swing box while dashing.
        // Each target is hit AT MOST ONCE per dash (dashStruck dedup set cleared on rising edge).
        bool dashing = playerMovement != null && playerMovement.IsDashing();
        if (HasStateAuthority && dashing && mods != null && mods.DashDealsDamage && sideAttackPoint != null)
        {
            if (!wasDashing) dashStruck.Clear();
            ApplyMeleeHits(sideAttackPoint.position, sideAttackArea, dashStruck);
        }
        wasDashing = dashing;
    }

    private SwingPhaseKind CurrentSwingPhase()
    {
        return SwingPhase.Resolve(Runner.Tick, AttackStartTick,
            stats.attackStartupTicks, stats.attackActiveTicks, stats.attackRecoveryTicks);
    }

    /// <summary>True while a swing owns the player's offense (Startup/Active/Recovery).
    /// PlayerMovement reads this to block dash starts (spec 2.3).</summary>
    public bool IsSwingCommitted => CurrentSwingPhase() != SwingPhaseKind.None;

    /// <summary>Latch a new swing: start tick + aim/facing/pound-ness frozen at commit
    /// (spec 2.2 — the swing never flips mid-animation). Runs on state authority and the
    /// predicting input authority, like the old Attack().</summary>
    private void BeginSwing()
    {
        AttackStartTick = Runner.Tick;
        AttackAim = verticalAim;
        AttackFacingRight = playerMovement != null ? playerMovement.IsFacingRight()
                                                   : transform.localScale.x >= 0f;

        bool isGrounded = groundCheck != null &&
                          Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        AttackIsPound = verticalAim < 0 && !isGrounded && downAttackPoint != null;

        swingStruck.Clear();

        if (playerAnimator != null)
        {
            if (AttackIsPound) playerAnimator.TriggerGroundPound();
            else playerAnimator.TriggerAttack();
        }
    }

    /// <summary>Per-tick swing behaviour. Pound impulse fires exactly once on the first Active
    /// tick (predicted + authoritative, like the old press-time write). Hit detection runs on
    /// every Active tick, server-only, at most one hit per target per swing (swingStruck).</summary>
    private void SimulateSwingTick(SwingPhaseKind phase)
    {
        if (useGroundPound && AttackIsPound &&
            SwingPhase.IsFirstActiveTick(Runner.Tick, AttackStartTick, stats.attackStartupTicks))
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, -groundPoundForce);
        }

        if (phase != SwingPhaseKind.Active) return;
        if (!HasStateAuthority) return;
        if (sideAttackPoint == null) return; // parity with old Attack()'s null guard

        ResolveSwingBox(out Vector2 center, out Vector2 area);
        ApplyMeleeHits(center, area, swingStruck);
    }

    /// <summary>Hitbox from the LATCHED aim/facing. The attack-point children flip with
    /// localScale (current facing); if facing changed mid-swing, mirror the offset back
    /// to the facing latched at commit.</summary>
    private void ResolveSwingBox(out Vector2 center, out Vector2 area)
    {
        Transform point = sideAttackPoint;
        area = sideAttackArea;

        if (AttackAim > 0 && upAttackPoint != null)
        {
            point = upAttackPoint;
            area = upAttackArea;
        }
        else if (AttackIsPound)
        {
            point = downAttackPoint;
            area = downAttackArea;
        }
        // (AttackAim < 0 while grounded falls through to the side box, matching old behaviour.)

        Vector2 offset = (Vector2)point.position - (Vector2)transform.position;
        bool facingRightNow = playerMovement != null ? playerMovement.IsFacingRight() : transform.localScale.x >= 0f;
        if (facingRightNow != (bool)AttackFacingRight) offset.x = -offset.x;
        center = (Vector2)transform.position + offset;
    }

    /// <summary>
    /// SERVER: overlap the given box and apply melee damage/knockback to enemies and enemy
    /// players. Shared by the normal swing and the dash-strike (Quicker Dash tier 3).
    /// When <paramref name="alreadyHit"/> is non-null, each collider is processed at most once
    /// (used by the dash-strike to limit damage to one hit per target per dash).
    /// The normal swing passes the per-swing <c>swingStruck</c> dedup set so each target is hit
    /// at most once per swing; the dash-strike passes <c>dashStruck</c>.
    /// </summary>
    private void ApplyMeleeHits(Vector2 center, Vector2 area,
                                HashSet<Collider2D> alreadyHit = null)
    {
        Collider2D[] objectsHit = Physics2D.OverlapBoxAll(center, area, 0f, attackableLayer);

        foreach (Collider2D hit in objectsHit)
        {
            if (alreadyHit != null)
            {
                if (alreadyHit.Contains(hit)) continue;
                alreadyHit.Add(hit);
            }

            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null)
            {
                Vector2 knockbackDirection = (hit.transform.position - transform.position).normalized;
                Vector2 knockbackForce = new Vector2(knockbackDirection.x * stats.attackForce, knockbackUpward);
                int finalDamage = ResolveMeleeDamage(enemy.Team, hit.transform.position);
                enemy.TakeDamage(finalDamage, knockbackForce, hit.transform.position);
                RPC_HitFeedback(enemy.Object.Id, hit.transform.position, finalDamage);
                continue;
            }

            // Player hit. Skip ourselves and friendly players (no melee friendly-fire). Damage
            // goes through ServerApplyDamage keyed by this attacker's NetworkObject id, so
            // spawn-immunity is respected and the rapid-hit guard is per attacker — which also
            // throttles the dash-strike's per-tick calls to one hit per 0.1s per target.
            PlayerStatsHandler targetPlayer = hit.GetComponent<PlayerStatsHandler>();
            if (targetPlayer != null && targetPlayer != statsHandler)
            {
                PlayerTeamData targetTeam = hit.GetComponent<PlayerTeamData>();
                Team myTeam = teamComponent != null ? teamComponent.Team : Team.None;
                Team otherTeam = targetTeam != null ? targetTeam.Team : Team.None;
                if (!TeamUtil.AreEnemies(myTeam, otherTeam)) continue;

                int finalDamage = ResolveMeleeDamage(otherTeam, hit.transform.position);
                targetPlayer.ServerApplyDamage(finalDamage, Object.Id);
                RPC_HitFeedback(targetPlayer.Object.Id, hit.transform.position, finalDamage);

                Rigidbody2D targetRb = hit.GetComponent<Rigidbody2D>();
                if (targetRb != null)
                {
                    Vector2 knockbackDirection = (hit.transform.position - transform.position).normalized;
                    targetRb.AddForce(new Vector2(knockbackDirection.x * stats.attackForce, knockbackUpward),
                                      ForceMode2D.Impulse);
                }
            }
        }
    }

    /// <summary>
    /// Attacker-only hit feedback. Server calls this on the attacker's client after
    /// a landed melee hit; it resolves the target locally and plays cosmetic FX.
    /// No networked state — mirrors Projectile.RPC_Impact / Enemy.RPC_TakeDamage.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_HitFeedback(NetworkId targetId, Vector2 hitPoint, int damage)
    {
        if (HitFeedback.Instance == null) return;
        GameObject targetGo = null;
        if (Runner.TryFindObject(targetId, out NetworkObject targetObj) && targetObj != null)
            targetGo = targetObj.gameObject;
        HitFeedback.Instance.Play(targetGo, hitPoint, damage);
    }

    /// <summary>
    /// Resolves melee damage through the unified pipeline, keyed by the DEFENDER's team and
    /// position — a defender takes more damage the farther they are from their own base.
    /// Falls back to raw base damage (with a loud one-time warning) if no CombatConfig is assigned.
    /// </summary>
    private int ResolveMeleeDamage(Team defenderTeam, Vector2 defenderPos)
    {
        CombatConfig config = GameSettingsManager.Instance != null
            ? GameSettingsManager.Instance.GetCombatConfig()
            : null;
        if (config == null)
        {
            CombatConfig.WarnMissingOnce();
            return Mathf.RoundToInt(stats.attackDamage);
        }

        return config.ResolveDamage(stats.attackDamage, defenderTeam, defenderPos);
    }

    private void ShootProjectile(Vector2 aimWorldPoint)
    {
        if (playerAnimator != null) playerAnimator.TriggerShoot();
        if (projectilePrefab == null || projectileSpawnPoint == null) return;

        // Cosmetic local prediction: instant muzzle/tracer on the firing client only. IsForward
        // fires it exactly once (not on resimulation); !HasStateAuthority skips a host-as-player
        // whose real projectile is already instant. The server still spawns the authoritative one.
        if (HasInputAuthority && !HasStateAuthority && Runner.IsForward)
        {
            Vector2 dir = (aimWorldPoint - (Vector2)projectileSpawnPoint.position).normalized;
            PlayLocalShootFx(projectileSpawnPoint.position, dir);
        }

        if (!HasStateAuthority)
        {
            return; // only the server spawns networked objects
        }

        Vector2 aimDirection = (aimWorldPoint - (Vector2)projectileSpawnPoint.position).normalized;

        // Degenerate aim (no aim data yet, or aim point exactly on the muzzle) would spawn a
        // stationary projectile. Fall back to the player's facing direction; localScale.x tracks
        // the networked FacingRight, so this is consistent on the server.
        if (aimDirection == Vector2.zero)
            aimDirection = transform.localScale.x >= 0f ? Vector2.right : Vector2.left;

        Team shooterTeam = teamComponent != null ? teamComponent.Team : Team.None;

        // The raw base damage travels with the projectile; final damage is resolved at impact
        // (Projectile.cs) against the DEFENDER's team+position, since the defender isn't known
        // until something is actually hit.
        NetworkObject spawned = Runner.Spawn(
            projectilePrefab,
            projectileSpawnPoint.position,
            Quaternion.identity,
            Object.InputAuthority,
            (runner, obj) =>
            {
                Projectile p = obj.GetComponent<Projectile>();
                if (p != null) p.ServerInitialize(aimDirection, projectileSpeed, projectileDamage, shooterTeam, projectileScale);
            });
    }

    /// <summary>
    /// Client-local, non-networked shot feedback (muzzle flash + tracer). No gameplay effect — the
    /// server's networked projectile is authoritative. Called only on the firing input-authority
    /// client, once per shot.
    /// </summary>
    private void PlayLocalShootFx(Vector3 origin, Vector2 dir)
    {
        if (muzzleFlashPrefab != null)
        {
            GameObject flash = Instantiate(muzzleFlashPrefab, origin, Quaternion.identity);
            Destroy(flash, muzzleFlashLifetime);
        }
        CosmeticTracer.Spawn(origin, dir, tracerLength, tracerWidth, tracerColor, tracerDuration);
    }

    /// <summary>
    /// Local aim direction (unit vector) from this player toward the last mouse aim point. Used by
    /// PlayerCamera for the aim lean. Returns Vector2.zero before any input or if degenerate.
    /// </summary>
    public Vector2 GetAimDirection()
    {
        Vector2 d = lastAimWorldPoint - (Vector2)transform.position;
        return d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.zero;
    }

    void OnDrawGizmosSelected()
    {
        if (sideAttackPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(sideAttackPoint.position, sideAttackArea);
        }

        if (upAttackPoint != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireCube(upAttackPoint.position, upAttackArea);
        }

        if (downAttackPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(downAttackPoint.position, downAttackArea);
        }

        if (groundCheck != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}
