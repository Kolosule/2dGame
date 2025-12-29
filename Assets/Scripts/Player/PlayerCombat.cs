using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCombat : MonoBehaviour
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
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform projectileSpawnPoint;
    [Tooltip("Projectile damage")]
    [SerializeField] private int projectileDamage = 15;
    [Tooltip("Projectile initial speed")]
    [SerializeField] private float projectileSpeed = 10f;
    [Tooltip("Projectile scale/size")]
    [SerializeField] private float projectileScale = 1f;
    [Tooltip("Cooldown between projectile shots")]
    [SerializeField] private float projectileCooldown = 0.5f;

    // Component references
    private Animator anim; // NOW REFERENCES CHILD OBJECT
    private PlayerTeamComponent teamComponent;
    private PlayerStatsHandler statsHandler;
    private Rigidbody2D rb;
    private PlayerMovement playerMovement;

    // Combat state
    private float timeSinceAttack;
    private float timeSinceProjectile;
    private float yAxis;
    private bool isGroundPounding = false;

    // Camera reference for mouse world position
    private Camera mainCamera;

    void Awake()
    {
        // UPDATED: Get Animator from child object instead of parent
        anim = GetComponentInChildren<Animator>();

        if (anim == null)
        {
            Debug.LogWarning("PlayerCombat: Animator not found in children! Make sure the Sprite child has an Animator component.");
        }

        teamComponent = GetComponent<PlayerTeamComponent>();
        statsHandler = GetComponent<PlayerStatsHandler>();
        rb = GetComponent<Rigidbody2D>();
        playerMovement = GetComponent<PlayerMovement>();
        mainCamera = Camera.main;

        if (teamComponent == null)
        {
            Debug.LogWarning("PlayerCombat: No PlayerTeamComponent found!");
        }

        if (statsHandler == null)
        {
            Debug.LogWarning("PlayerCombat: No PlayerStatsHandler found!");
        }

        if (projectileSpawnPoint == null && sideAttackPoint != null)
        {
            projectileSpawnPoint = sideAttackPoint;
            Debug.Log("PlayerCombat: Using sideAttackPoint as projectileSpawnPoint");
        }

        if (mainCamera == null)
        {
            Debug.LogWarning("PlayerCombat: No MainCamera found! Mouse aiming won't work.");
        }
    }

    void Update()
    {
        timeSinceAttack += Time.deltaTime;
        timeSinceProjectile += Time.deltaTime;
    }

#if ENABLE_INPUT_SYSTEM
    /// <summary>
    /// Called by PlayerController to handle combat input (New Input System)
    /// </summary>
    public void HandleInput()
    {
        // Get input devices
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        var gamepad = Gamepad.current;

        // VERTICAL AXIS - For directional attacks
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

        // MELEE ATTACK - Fire1 (left mouse, left ctrl, or gamepad button)
        bool fire1Pressed = false;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            fire1Pressed = true;
        if (keyboard != null && keyboard.leftCtrlKey.wasPressedThisFrame)
            fire1Pressed = true;
        if (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame) // A/X button
            fire1Pressed = true;

        if (fire1Pressed && timeSinceAttack >= attackCooldown)
        {
            Attack();
        }

        // PROJECTILE ATTACK - Fire2 (right mouse, left alt, or gamepad button)
        bool fire2Pressed = false;
        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            fire2Pressed = true;
        if (keyboard != null && keyboard.leftAltKey.wasPressedThisFrame)
            fire2Pressed = true;
        if (gamepad != null && gamepad.rightTrigger.wasPressedThisFrame) // RT/R2
            fire2Pressed = true;

        if (fire2Pressed && timeSinceProjectile >= projectileCooldown)
        {
            ShootProjectile();
        }
    }
#endif

    /// <summary>
    /// Handles melee attacks with directional detection
    /// </summary>
    private void Attack()
    {
        timeSinceAttack = 0;

        Transform attackTransform;
        Vector2 attackArea;

        if (yAxis > 0)
        {
            // UP ATTACK
            attackTransform = upAttackPoint;
            attackArea = upAttackArea;

            // Check if animator exists before setting parameters
            if (anim != null)
            {
                anim.SetTrigger("AttackUp");
            }
        }
        else if (yAxis < 0 && useGroundPound)
        {
            // DOWN ATTACK (Ground Pound)
            GroundPound();
            return;
        }
        else
        {
            // SIDE ATTACK (default)
            attackTransform = sideAttackPoint;
            attackArea = sideAttackArea;

            // Check if animator exists before setting parameters
            if (anim != null)
            {
                anim.SetTrigger("Attack");
            }
        }

        // Perform the attack
        Hit(attackTransform, attackArea);
    }

    /// <summary>
    /// Ground pound attack - slam downward
    /// </summary>
    private void GroundPound()
    {
        if (isGroundPounding) return;

        isGroundPounding = true;

        // Check if animator exists before setting parameters
        if (anim != null)
        {
            anim.SetTrigger("AttackDown");
        }

        // Apply downward force
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, -groundPoundForce);

        // The actual ground pound hit will be triggered on landing
        // You can call Hit() in an animation event or detect ground collision
    }

    /// <summary>
    /// Detects and damages enemies in the attack area
    /// </summary>
    private void Hit(Transform attackTransform, Vector2 attackArea)
    {
        if (attackTransform == null)
        {
            Debug.LogWarning("PlayerCombat: Attack transform is null!");
            return;
        }

        // Detect all colliders in attack range
        Collider2D[] objectsHit = Physics2D.OverlapBoxAll(
            attackTransform.position,
            attackArea,
            0f,
            attackableLayer
        );

        foreach (Collider2D hit in objectsHit)
        {
            // Spawn hit marker at hit location
            if (hitMarkerPrefab != null)
            {
                GameObject marker = Instantiate(hitMarkerPrefab, hit.transform.position, Quaternion.identity);

                SpriteRenderer sr = marker.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sr.color = hitMarkerColor;
                }

                Destroy(marker, hitMarkerDuration);
            }

            // Try to damage the enemy using the Enemy class
            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null)
            {
                // Calculate knockback
                Vector2 knockbackDirection = (hit.transform.position - transform.position).normalized;
                Vector2 knockbackForce = new Vector2(knockbackDirection.x * knockbackStrength, knockbackUpward);

                enemy.TakeDamage(damageAmount, knockbackForce, hit.transform.position);
                Debug.Log($"Hit {hit.name} for {damageAmount} damage!");
            }
        }
    }

    /// <summary>
    /// Shoots a projectile towards the mouse position or in facing direction
    /// </summary>
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

        // Determine aim direction
        Vector2 aimDirection;

        // Try to aim towards mouse if available
        if (Mouse.current != null && mainCamera != null)
        {
            Vector2 mousePos = mainCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            aimDirection = (mousePos - (Vector2)projectileSpawnPoint.position).normalized;
        }
        else
        {
            // Fallback: shoot in facing direction
            float facingDirection = Mathf.Sign(transform.localScale.x);
            aimDirection = new Vector2(facingDirection, 0);
        }

        // Spawn projectile
        GameObject projectile = Instantiate(
            projectilePrefab,
            projectileSpawnPoint.position,
            Quaternion.identity
        );

        // Scale the projectile
        projectile.transform.localScale = Vector3.one * projectileScale;

        // Initialize projectile with all settings
        Projectile projectileScript = projectile.GetComponent<Projectile>();
        if (projectileScript != null)
        {
            string shooterTeam = teamComponent != null ? teamComponent.teamID : "";
            projectileScript.Initialize(aimDirection, projectileSpeed, projectileDamage, shooterTeam);
        }
        else
        {
            Debug.LogWarning("PlayerCombat: Projectile prefab is missing Projectile component!");

            // Fallback: manually set velocity if no Projectile script
            Rigidbody2D projectileRb = projectile.GetComponent<Rigidbody2D>();
            if (projectileRb != null)
            {
                projectileRb.linearVelocity = aimDirection * projectileSpeed;
            }
        }

        // Trigger shoot animation if animator exists
        if (anim != null)
        {
            anim.SetTrigger("Shoot");
        }

        Debug.Log($"Projectile fired in direction: {aimDirection}");
    }

    // ============================
    // DEBUG VISUALIZATION
    // ============================

    void OnDrawGizmosSelected()
    {
        // Visualize attack ranges
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
    }
}