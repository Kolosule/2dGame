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

    [Header("Ground Check (for down attack)")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundLayer;

    private Animator anim;
    private PlayerTeamData teamComponent;
    private PlayerStatsHandler statsHandler;
    private Rigidbody2D rb;
    private PlayerMovement playerMovement;
    private int verticalAim;

    [Networked] private TickTimer AttackCooldownTimer { get; set; }
    [Networked] private TickTimer ShootCooldownTimer { get; set; }

    void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        if (anim == null)
        {
            Debug.LogWarning("PlayerCombat: Animator not found in children!");
        }

        teamComponent = GetComponent<PlayerTeamData>();
        statsHandler = GetComponent<PlayerStatsHandler>();
        rb = GetComponent<Rigidbody2D>();
        playerMovement = GetComponent<PlayerMovement>();
    }

    /// <summary>Called every tick by PlayerController when input is available.</summary>
    public void Simulate(NetInput input, NetworkButtons pressed)
    {
        verticalAim = input.VerticalAim;

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
    }

    private void Attack()
    {
        Transform attackTransform = null;
        Vector2 attackArea = Vector2.zero;
        string attackDirection = "side";

        if (verticalAim > 0 && upAttackPoint != null)
        {
            attackTransform = upAttackPoint;
            attackArea = upAttackArea;
            attackDirection = "up";
        }
        else if (verticalAim < 0 && downAttackPoint != null)
        {
            bool isGrounded = groundCheck != null &&
                              Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
            if (!isGrounded)
            {
                attackTransform = downAttackPoint;
                attackArea = downAttackArea;
                attackDirection = "down";
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

        if (anim != null)
        {
            anim.SetTrigger("Attack");
            anim.SetBool("AttackingUp", attackDirection == "up");
            anim.SetBool("AttackingDown", attackDirection == "down");
        }

        // Damage + hit detection only on the server (avoids double-apply across clients).
        if (!HasStateAuthority) return;

        Collider2D[] objectsHit = Physics2D.OverlapBoxAll(
            attackTransform.position, attackArea, 0f, attackableLayer);

        foreach (Collider2D hit in objectsHit)
        {
            if (hitMarkerPrefab != null)
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
        if (anim != null) anim.SetTrigger("Shoot");
        if (projectilePrefab == null || projectileSpawnPoint == null) return;
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
