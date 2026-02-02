using UnityEngine;
using UnityEngine.InputSystem;
using Fusion;

/// <summary>
/// FIXED VERSION - Removed IsGrounded() call that doesn't exist
/// Uses direct ground check instead
/// </summary>
public class PlayerCombat : NetworkBehaviour
{
    [Header("Stats")]
    [SerializeField] private PlayerStats stats;

    [Header("Attack Settings")]
    [SerializeField] private LayerMask attackableLayer;
    [SerializeField] private int damageAmount = 25;
    [SerializeField] private float knockbackStrength = 10f;
    [SerializeField] private float knockbackUpward = 5f;
    [SerializeField] private float attackCooldown = 0.3f;

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
    private PlayerTeamComponent teamComponent;
    private PlayerStatsHandler statsHandler;
    private Rigidbody2D rb;
    private PlayerMovement playerMovement;
    private float timeSinceAttack;
    private float timeSinceProjectile;
    private float yAxis;
    private bool isGroundPounding = false;
    private Camera mainCamera;

    void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        if (anim == null)
        {
            Debug.LogWarning("PlayerCombat: Animator not found in children!");
        }

        teamComponent = GetComponent<PlayerTeamComponent>();
        statsHandler = GetComponent<PlayerStatsHandler>();
        rb = GetComponent<Rigidbody2D>();
        playerMovement = GetComponent<PlayerMovement>();
        mainCamera = Camera.main;
    }

    void Update()
    {
        if (!HasInputAuthority) return;

        timeSinceAttack += Time.deltaTime;
        timeSinceProjectile += Time.deltaTime;
    }

    public void HandleInput()
    {
        if (!HasInputAuthority) return;

        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        var gamepad = Gamepad.current;

        // VERTICAL AXIS
        yAxis = 0;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) yAxis = 1;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) yAxis = -1;
        }
        if (gamepad != null)
        {
            float stickY = gamepad.leftStick.ReadValue().y;
            if (Mathf.Abs(stickY) > 0.2f) yAxis = stickY;
        }

        // MELEE ATTACK
        bool fire1Pressed = false;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            fire1Pressed = true;
        if (keyboard != null && keyboard.leftCtrlKey.wasPressedThisFrame)
            fire1Pressed = true;
        if (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame)
            fire1Pressed = true;

        if (fire1Pressed && timeSinceAttack >= attackCooldown)
        {
            Attack();
        }

        // PROJECTILE ATTACK
        bool fire2Pressed = false;
        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            fire2Pressed = true;
        if (keyboard != null && keyboard.leftAltKey.wasPressedThisFrame)
            fire2Pressed = true;
        if (gamepad != null && gamepad.buttonWest.wasPressedThisFrame)
            fire2Pressed = true;

        if (fire2Pressed && timeSinceProjectile >= projectileCooldown)
        {
            ShootProjectile();
        }
    }

    private void Attack()
    {
        timeSinceAttack = 0;

        Transform attackTransform = null;
        Vector2 attackArea = Vector2.zero;
        string attackDirection = "side";

        if (yAxis > 0.5f && upAttackPoint != null)
        {
            attackTransform = upAttackPoint;
            attackArea = upAttackArea;
            attackDirection = "up";
        }
        else if (yAxis < -0.5f && downAttackPoint != null)
        {
            // ⭐ FIXED: Check if player is grounded directly instead of calling IsGrounded()
            bool isGrounded = groundCheck != null &&
                             Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

            if (!isGrounded) // Only allow down attack in air
            {
                attackTransform = downAttackPoint;
                attackArea = downAttackArea;
                attackDirection = "down";

                if (useGroundPound)
                {
                    isGroundPounding = true;
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, -groundPoundForce);
                }
            }
            else
            {
                // If grounded, do side attack instead
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

        Collider2D[] objectsHit = Physics2D.OverlapBoxAll(
            attackTransform.position,
            attackArea,
            0f,
            attackableLayer
        );

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
                Vector2 knockbackForce = new Vector2(knockbackDirection.x * knockbackStrength, knockbackUpward);
                enemy.TakeDamage(damageAmount, knockbackForce, hit.transform.position);
            }
        }
    }

    private void ShootProjectile()
    {
        if (projectilePrefab == null)
        {
            Debug.LogWarning("PlayerCombat: No projectile prefab assigned!");
            return;
        }

        if (projectileSpawnPoint == null)
        {
            Debug.LogWarning("PlayerCombat: No projectile spawn point assigned!");
            return;
        }

        timeSinceProjectile = 0;

        Vector2 aimDirection;

        if (Mouse.current != null && mainCamera != null)
        {
            Vector2 mousePos = mainCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            aimDirection = (mousePos - (Vector2)projectileSpawnPoint.position).normalized;
        }
        else
        {
            float facingDirection = Mathf.Sign(transform.localScale.x);
            aimDirection = new Vector2(facingDirection, 0);
        }

        NetworkObject projectile = Runner.Spawn(
            projectilePrefab,
            projectileSpawnPoint.position,
            Quaternion.identity,
            Object.InputAuthority,
            (runner, obj) => {
                obj.transform.localScale = Vector3.one * projectileScale;

                Projectile projectileScript = obj.GetComponent<Projectile>();
                if (projectileScript != null)
                {
                    string shooterTeam = teamComponent != null ? teamComponent.teamID : "";
                    projectileScript.Initialize(aimDirection, projectileSpeed, projectileDamage, shooterTeam);
                }
            }
        );

        if (anim != null)
        {
            anim.SetTrigger("Shoot");
        }

        Debug.Log($"[{(Runner.IsServer ? "SERVER" : "CLIENT")}] Projectile spawned");
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

        // Draw ground check
        if (groundCheck != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}