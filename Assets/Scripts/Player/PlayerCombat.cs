using System.Collections.Generic;
using UnityEngine;
using Fusion;

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

    [Header("Hit Marker")]
    [SerializeField] private GameObject hitMarkerPrefab;
    [SerializeField] private Color hitMarkerColor = Color.white;
    [SerializeField] private float hitMarkerDuration = 0.3f;

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
    private PlayerCameraFeelHandler feelHandler;
    private int verticalAim;
    private Vector2 lastAimWorldPoint;

    // Dash-strike dedup: server-only, non-networked. Cleared on each new dash rising edge.
    private readonly HashSet<Collider2D> dashStruck = new HashSet<Collider2D>();
    private bool wasDashing;

    [Networked] private TickTimer AttackCooldownTimer { get; set; }
    [Networked] private TickTimer ShootCooldownTimer { get; set; }

    void Awake()
    {
        playerAnimator = GetComponent<PlayerAnimator>();
        teamComponent = GetComponent<PlayerTeamData>();
        statsHandler = GetComponent<PlayerStatsHandler>();
        rb = GetComponent<Rigidbody2D>();
        playerMovement = GetComponent<PlayerMovement>();
        mods = GetComponent<PlayerStatModifiers>();
        feelHandler = GetComponent<PlayerCameraFeelHandler>();
    }

    /// <summary>Called every tick by PlayerController when input is available.</summary>
    public void Simulate(NetInput input, NetworkButtons pressed)
    {
        verticalAim = input.VerticalAim;
        lastAimWorldPoint = input.AimWorldPoint;

        if (pressed.IsSet((int)PlayerButton.Melee) && AttackCooldownTimer.ExpiredOrNotRunning(Runner))
        {
            AttackCooldownTimer = TickTimer.CreateFromSeconds(Runner, stats.attackCooldown);
            Attack();
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
            ApplyMeleeHits(sideAttackPoint.position, sideAttackArea, spawnHitMarkers: false, dashStruck);
        }
        wasDashing = dashing;
    }

    private void Attack()
    {
        Transform attackTransform = null;
        Vector2 attackArea = Vector2.zero;
        bool isGroundPound = false;

        if (verticalAim > 0 && upAttackPoint != null)
        {
            attackTransform = upAttackPoint;
            attackArea = upAttackArea;
        }
        else if (verticalAim < 0 && downAttackPoint != null)
        {
            bool isGrounded = groundCheck != null &&
                              Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
            if (!isGrounded)
            {
                attackTransform = downAttackPoint;
                attackArea = downAttackArea;
                isGroundPound = true;
                if (useGroundPound)
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, -groundPoundForce);
            }
            else
            {
                attackTransform = sideAttackPoint;
                attackArea = sideAttackArea;
            }
        }
        else
        {
            attackTransform = sideAttackPoint;
            attackArea = sideAttackArea;
        }

        if (attackTransform == null) return;

        // Latch the animation as networked state (replicates to every client). A mid-air
        // down attack is a ground pound and gets its own latched state. The latch runs on the
        // state authority (authoritative) and the local input authority (predicted), so the
        // local player's swing animates on this tick instead of after a server round-trip.
        if (playerAnimator != null)
        {
            if (isGroundPound)
                playerAnimator.TriggerGroundPound();
            else
                playerAnimator.TriggerAttack();
        }

        // Cosmetic local hit-stop: on the firing client, on the forward tick only, predict whether
        // the swing connects and briefly hold the camera. Render-only; the server still owns damage.
        if (HasInputAuthority && Runner.IsForward && feelHandler != null &&
            PredictWouldHitEnemy(attackTransform.position, attackArea))
        {
            feelHandler.TriggerHitStop();
        }

        // Damage + hit detection only on the server (avoids double-apply across clients).
        if (!HasStateAuthority) return;

        ApplyMeleeHits(attackTransform.position, attackArea, spawnHitMarkers: true);
    }

    /// <summary>
    /// CLIENT-LOCAL prediction: would this swing box overlap an enemy (enemy AI or an enemy-team
    /// player)? Read-only — applies no damage. Used only to fire the cosmetic hit-stop on the
    /// firing client; a rare false positive is an acceptable, render-only artifact.
    /// </summary>
    private bool PredictWouldHitEnemy(Vector2 center, Vector2 area)
    {
        Collider2D[] hits = Physics2D.OverlapBoxAll(center, area, 0f, attackableLayer);
        Team myTeam = teamComponent != null ? teamComponent.Team : Team.None;
        foreach (Collider2D hit in hits)
        {
            if (hit.GetComponent<Enemy>() != null) return true;

            PlayerStatsHandler other = hit.GetComponent<PlayerStatsHandler>();
            if (other != null && other != statsHandler)
            {
                PlayerTeamData otherTeam = hit.GetComponent<PlayerTeamData>();
                Team ot = otherTeam != null ? otherTeam.Team : Team.None;
                if (TeamUtil.AreEnemies(myTeam, ot)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// SERVER: overlap the given box and apply melee damage/knockback to enemies and enemy
    /// players. Shared by the normal swing and the dash-strike (Quicker Dash tier 3).
    /// When <paramref name="alreadyHit"/> is non-null, each collider is processed at most once
    /// (used by the dash-strike to limit damage to one hit per target per dash).
    /// Normal Attack() calls pass null → behaviour is byte-identical to before.
    /// </summary>
    private void ApplyMeleeHits(Vector2 center, Vector2 area, bool spawnHitMarkers,
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

            if (spawnHitMarkers && hitMarkerPrefab != null)
            {
                GameObject marker = Instantiate(hitMarkerPrefab, hit.transform.position, Quaternion.identity);
                SpriteRenderer sr = marker.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = hitMarkerColor;
                Destroy(marker, hitMarkerDuration);
            }

            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null)
            {
                Vector2 knockbackDirection = (hit.transform.position - transform.position).normalized;
                Vector2 knockbackForce = new Vector2(knockbackDirection.x * stats.attackForce, knockbackUpward);
                int finalDamage = ResolveMeleeDamage(hit.gameObject, hit.transform.position);
                enemy.TakeDamage(finalDamage, knockbackForce, hit.transform.position);
                continue;
            }

            // Player hit. Skip ourselves and friendly players (no melee friendly-fire). Damage
            // goes through RPC_TakeDamage so spawn-immunity / hit-cooldown are respected — which
            // also throttles the dash-strike's per-tick calls to one hit per 0.1s per target.
            PlayerStatsHandler targetPlayer = hit.GetComponent<PlayerStatsHandler>();
            if (targetPlayer != null && targetPlayer != statsHandler)
            {
                PlayerTeamData targetTeam = hit.GetComponent<PlayerTeamData>();
                Team myTeam = teamComponent != null ? teamComponent.Team : Team.None;
                Team otherTeam = targetTeam != null ? targetTeam.Team : Team.None;
                if (!TeamUtil.AreEnemies(myTeam, otherTeam)) continue;

                int finalDamage = ResolveMeleeDamage(hit.gameObject, hit.transform.position);
                targetPlayer.RPC_TakeDamage(finalDamage);

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
    /// Resolves melee damage to a hit target through the unified pipeline (review item #4).
    /// Falls back to raw base damage if no CombatConfig is available.
    /// </summary>
    private int ResolveMeleeDamage(GameObject target, Vector2 targetPos)
    {
        CombatConfig config = GameSettingsManager.Instance != null
            ? GameSettingsManager.Instance.GetCombatConfig()
            : null;
        if (config == null) return Mathf.RoundToInt(stats.attackDamage);

        Team myTeam = teamComponent != null ? teamComponent.Team : Team.None;

        Team targetTeam = Team.None;
        EnemyTeamComponent etc = target.GetComponent<EnemyTeamComponent>();
        if (etc != null)
        {
            targetTeam = etc.Team;
        }
        else
        {
            PlayerTeamData ptc = target.GetComponent<PlayerTeamData>();
            if (ptc != null) targetTeam = ptc.Team;
        }

        return config.ResolveDamage(stats.attackDamage, myTeam, transform.position, targetTeam, targetPos);
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
        Team shooterTeam = teamComponent != null ? teamComponent.Team : Team.None;

        NetworkObject spawned = Runner.Spawn(
            projectilePrefab,
            projectileSpawnPoint.position,
            Quaternion.identity,
            Object.InputAuthority,
            (runner, obj) =>
            {
                obj.transform.localScale = Vector3.one * projectileScale;
                Projectile p = obj.GetComponent<Projectile>();
                if (p != null) p.ServerInitialize(aimDirection, projectileSpeed, projectileDamage, shooterTeam);
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
