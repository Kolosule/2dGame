using UnityEngine;
using Fusion;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Enhanced player combat with:
/// - Two down attack versions (ground pound OR air attack)
/// - Projectile shooting system with mouse/gamepad aiming
/// - Friendly fire prevention
/// - Works with BOTH old Input Manager AND new Input System
/// - Works with Photon Fusion networking
/// </summary>
public class PlayerCombat : MonoBehaviour
{
    [Header("Combat Settings")]
    [SerializeField] private PlayerStats stats;
    [SerializeField] private LayerMask attackableLayer;
    [SerializeField] private int damageAmount = 10;
    [SerializeField] private float knockbackStrength = 10f;
    [SerializeField] private float knockbackUpward = 3f;

    [Header("Attack Speed")]
    [Tooltip("Time in seconds between attacks (lower = faster attacks)")]
    [SerializeField] private float attackCooldown = 0.5f;

    [Header("Attack Points")]
    [SerializeField] private Transform sideAttackPoint;
    [SerializeField] private Transform upAttackPoint;
    [SerializeField] private Transform downAttackPoint;

    [Header("Attack Areas")]
    [SerializeField] private Vector2 sideAttackArea = new Vector2(1f, 1f);
    [SerializeField] private Vector2 upAttackArea = new Vector2(1f, 1f);
    [SerializeField] private Vector2 downAttackArea = new Vector2(1f, 1f);

    [Header("Hit Marker Effects")]
    [Tooltip("Particle effect to spawn when attacks hit enemies")]
    [SerializeField] private GameObject hitMarkerPrefab;
    [Tooltip("Color of hit marker for successful hits")]
    [SerializeField] private Color hitMarkerColor = Color.white;
    [Tooltip("Duration of hit marker effect in seconds")]
    [SerializeField] private float hitMarkerDuration = 0.3f;

    [Header("Down Attack Configuration")]
    [Tooltip("Version A = Ground Pound (propels downward), Version B = Regular Air Attack")]
    [SerializeField] private bool useGroundPound = true;

    [Header("Ground Pound Settings (Version A)")]
    [Tooltip("Downward force applied when ground pounding")]
    [SerializeField] private float groundPoundForce = 20f;
    [Tooltip("Optional effect when ground pound hits ground")]
    [SerializeField] private GameObject groundPoundImpactEffect;

    [Header("Projectile Settings")]
    [Tooltip("Projectile prefab to spawn (must have Projectile.cs attached)")]
    [SerializeField] private GameObject projectilePrefab;
    [Tooltip("Where projectiles spawn from")]
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
    private Animator anim;
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
        anim = GetComponent<Animator>();
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

    public void HandleInput()
    {
#if ENABLE_INPUT_SYSTEM
        // NEW INPUT SYSTEM
        HandleInputNewSystem();
#else
        // OLD INPUT MANAGER
        HandleInputOldSystem();
#endif
    }

#if !ENABLE_INPUT_SYSTEM
    /// <summary>
    /// Handle input using OLD Input Manager
    /// </summary>
    private void HandleInputOldSystem()
    {
        yAxis = Input.GetAxisRaw("Vertical");

        // MELEE ATTACK - Fire1
        if (Input.GetButtonDown("Fire1") && timeSinceAttack >= attackCooldown)
        {
            Attack();
        }

        // PROJECTILE ATTACK - Fire2
        if (Input.GetButtonDown("Fire2") && timeSinceProjectile >= projectileCooldown)
        {
            ShootProjectile();
        }
    }
#endif

#if ENABLE_INPUT_SYSTEM
    /// <summary>
    /// Handle input using NEW Input System
    /// </summary>
    private void HandleInputNewSystem()
    {
        // Get vertical input (old style compatible)
        var keyboard = Keyboard.current;
        var gamepad = Gamepad.current;

        yAxis = 0;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) yAxis = 1;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) yAxis = -1;
        }
        if (gamepad != null)
        {
            float stickY = gamepad.leftStick.y.ReadValue();
            if (Mathf.Abs(stickY) > 0.2f) yAxis = stickY;
        }

        // MELEE ATTACK - Fire1 (left mouse, left ctrl, or gamepad button)
        bool fire1Pressed = false;
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
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
        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
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
            anim.SetTrigger("Attack");
            attackTransform = upAttackPoint;
            attackArea = upAttackArea;
            Hit(attackTransform, attackArea);
        }
        else if (yAxis < 0)
        {
            // DOWN ATTACK - Only works in air
            if (IsInAir())
            {
                anim.SetTrigger("Attack");
                attackTransform = downAttackPoint;
                attackArea = downAttackArea;

                if (useGroundPound)
                {
                    PerformGroundPound();
                }
                else
                {
                    Hit(attackTransform, attackArea);
                }
            }
            else
            {
                // On ground, down does side attack
                attackTransform = sideAttackPoint;
                attackArea = sideAttackArea;
                Hit(attackTransform, attackArea);
            }
        }
        else
        {
            // SIDE ATTACK
            anim.SetTrigger("Attack");
            attackTransform = sideAttackPoint;
            attackArea = sideAttackArea;
            Hit(attackTransform, attackArea);
        }
    }

    private void PerformGroundPound()
    {
        if (rb == null) return;

        rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);
        rb.AddForce(Vector2.down * groundPoundForce, ForceMode2D.Impulse);

        isGroundPounding = true;
        Hit(downAttackPoint, downAttackArea);

        Debug.Log("Ground Pound activated!");
    }

    private bool IsInAir()
    {
        if (playerMovement != null)
        {
            return !Physics2D.OverlapCircle(transform.position + Vector3.down * 0.5f, 0.2f, LayerMask.GetMask("Ground"));
        }
        return rb != null && Mathf.Abs(rb.linearVelocity.y) > 0.1f;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (isGroundPounding && collision.gameObject.layer == LayerMask.NameToLayer("Ground"))
        {
            isGroundPounding = false;
            if (groundPoundImpactEffect != null)
            {
                Instantiate(groundPoundImpactEffect, transform.position, Quaternion.identity);
            }
        }
    }

    /// <summary>
    /// Shoots projectile towards mouse or gamepad aim
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

        Vector2 fireDirection = GetAimDirection();

        GameObject projectileObj = Instantiate(projectilePrefab, projectileSpawnPoint.position, Quaternion.identity);
        projectileObj.transform.localScale = Vector3.one * projectileScale;

        Projectile projectile = projectileObj.GetComponent<Projectile>();
        if (projectile != null)
        {
            string shooterTeam = teamComponent != null ? teamComponent.teamID : "";
            projectile.Initialize(fireDirection, projectileSpeed, projectileDamage, shooterTeam);
        }
        else
        {
            Debug.LogError("PlayerCombat: Projectile prefab missing Projectile.cs!");
            Destroy(projectileObj);
        }
    }

    /// <summary>
    /// Gets aim direction from mouse or gamepad
    /// </summary>
    private Vector2 GetAimDirection()
    {
#if ENABLE_INPUT_SYSTEM
        // NEW INPUT SYSTEM
        var gamepad = Gamepad.current;
        var mouse = Mouse.current;

        // Try gamepad right stick first
        if (gamepad != null)
        {
            Vector2 rightStick = gamepad.rightStick.ReadValue();
            if (rightStick.magnitude > 0.2f)
            {
                Debug.Log($"Aiming with gamepad: {rightStick.normalized}");
                return rightStick.normalized;
            }
        }

        // Fall back to mouse
        if (mouse != null && mainCamera != null)
        {
            Vector2 mousePos = mouse.position.ReadValue();
            Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, 0));
            mouseWorldPos.z = 0;
            Vector2 direction = (mouseWorldPos - transform.position).normalized;
            Debug.Log($"Aiming with mouse: {direction}");
            return direction;
        }

        // Fallback to facing direction
        return new Vector2(transform.localScale.x, 0);
#else
        // OLD INPUT SYSTEM
        // Try joystick
        float joyX = Input.GetAxis("RightStickX");
        float joyY = Input.GetAxis("RightStickY");
        
        if (Mathf.Abs(joyX) > 0.2f || Mathf.Abs(joyY) > 0.2f)
        {
            return new Vector2(joyX, joyY).normalized;
        }
        
        // Mouse
        if (mainCamera != null)
        {
            Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
            mouseWorldPos.z = 0;
            return (mouseWorldPos - transform.position).normalized;
        }

        return new Vector2(transform.localScale.x, 0);
#endif
    }

    private void Hit(Transform attackTransform, Vector2 attackArea)
    {
        Collider2D[] objectsHit = Physics2D.OverlapBoxAll(
            attackTransform.position,
            attackArea,
            0f,
            attackableLayer
        );

        bool hitSomething = false;

        foreach (Collider2D hit in objectsHit)
        {
            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null)
            {
                EnemyTeamComponent enemyTeam = enemy.GetComponent<EnemyTeamComponent>();
                if (teamComponent != null && enemyTeam != null)
                {
                    if (teamComponent.teamID == enemyTeam.teamID)
                    {
                        continue;
                    }
                }

                if (statsHandler != null)
                {
                    statsHandler.AttackEnemy(enemy);
                    hitSomething = true;

                    if (hitMarkerPrefab != null)
                    {
                        GameObject marker = Instantiate(hitMarkerPrefab, hit.transform.position, Quaternion.identity);
                        Destroy(marker, hitMarkerDuration);
                    }
                }
            }

            PlayerStatsHandler otherPlayer = hit.GetComponent<PlayerStatsHandler>();
            if (otherPlayer != null && otherPlayer != statsHandler)
            {
                PlayerTeamComponent otherTeam = otherPlayer.GetComponent<PlayerTeamComponent>();
                if (teamComponent != null && otherTeam != null)
                {
                    bool friendlyFireEnabled = GameSettingsManager.Instance != null &&
                                               GameSettingsManager.Instance.friendlyFireEnabled;

                    if (teamComponent.teamID == otherTeam.teamID && !friendlyFireEnabled)
                    {
                        continue;
                    }

                    float attackDamage = damageAmount;
                    if (teamComponent != null)
                    {
                        attackDamage *= teamComponent.GetDamageDealtModifier();
                    }

                    otherPlayer.TakeDamage(attackDamage);
                    hitSomething = true;

                    if (hitMarkerPrefab != null)
                    {
                        GameObject marker = Instantiate(hitMarkerPrefab, hit.transform.position, Quaternion.identity);
                        Destroy(marker, hitMarkerDuration);
                    }
                }
            }
        }
    }

    private void OnDrawGizmosSelected()
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

        if (projectileSpawnPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(projectileSpawnPoint.position, 0.2f);
        }
    }
}