using UnityEngine;

/// <summary>
/// Handles player combat including directional attacks, damage dealing, and visual feedback.
/// UPDATED: Now works with NetworkedEnemy for Photon Fusion.
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

    // Component references
    private Animator anim;
    private PlayerTeamComponent teamComponent;
    private PlayerStatsHandler statsHandler;

    // Combat state
    private float timeSinceAttack;
    private float yAxis;

    void Awake()
    {
        anim = GetComponent<Animator>();
        teamComponent = GetComponent<PlayerTeamComponent>();
        statsHandler = GetComponent<PlayerStatsHandler>();

        if (teamComponent == null)
        {
            Debug.LogWarning("PlayerCombat: No PlayerTeamComponent found! Territory modifiers won't work.");
        }

        if (statsHandler == null)
        {
            Debug.LogWarning("PlayerCombat: No PlayerStatsHandler found!");
        }
    }

    void Update()
    {
        timeSinceAttack += Time.deltaTime;
    }

    public void HandleInput()
    {
        yAxis = Input.GetAxisRaw("Vertical");

        // Attack input
        if (Input.GetButtonDown("Fire1") && timeSinceAttack >= attackCooldown)
        {
            Attack();
        }
    }

    private void Attack()
    {
        timeSinceAttack = 0;

        Transform attackTransform;
        Vector2 attackArea;

        // Determine attack direction based on input
        if (yAxis > 0)
        {
            // Up attack
            anim.SetTrigger("Attack");
            attackTransform = upAttackPoint;
            attackArea = upAttackArea;
        }
        else if (yAxis < 0)
        {
            // Down attack
            anim.SetTrigger("Attack");
            attackTransform = downAttackPoint;
            attackArea = downAttackArea;
        }
        else
        {
            // Side attack
            anim.SetTrigger("Attack");
            attackTransform = sideAttackPoint;
            attackArea = sideAttackArea;
        }

        // Perform attack detection and damage
        Hit(attackTransform, attackArea);
    }

    private void Hit(Transform attackTransform, Vector2 attackArea)
    {
        // Detect all colliders in the attack area
        Collider2D[] objectsHit = Physics2D.OverlapBoxAll(
            attackTransform.position,
            attackArea,
            0f,
            attackableLayer
        );

        bool hitSomething = false;

        // Process each hit object
        foreach (Collider2D hit in objectsHit)
        {
            // Try to get NetworkedEnemy component (new networked version)
            NetworkedEnemy enemy = hit.GetComponent<NetworkedEnemy>();
            if (enemy != null)
            {
                // Check if this enemy is on a different team
                EnemyTeamComponent enemyTeam = enemy.GetComponent<EnemyTeamComponent>();
                if (teamComponent != null && enemyTeam != null)
                {
                    if (teamComponent.teamID == enemyTeam.teamID)
                    {
                        continue; // Don't attack teammates
                    }
                }

                // Calculate knockback direction
                Vector2 knockbackDir = (enemy.transform.position - transform.position).normalized;
                float knockbackForce = knockbackStrength;

                // Deal damage using PlayerStatsHandler (server authority)
                if (statsHandler != null)
                {
                    statsHandler.Attack(enemy);
                }
                else
                {
                    // Fallback: direct damage call (less ideal)
                    enemy.TakeDamage(damageAmount, knockbackDir, knockbackForce);
                }

                // Spawn hit marker effect
                SpawnHitMarker(hit.transform.position);

                hitSomething = true;
            }
        }

        // Optional: Camera shake on hit
        if (hitSomething)
        {
            CameraFollow cam = Camera.main?.GetComponent<CameraFollow>();
            if (cam != null)
            {
                cam.ShakeCamera();
            }
        }
    }

    /// <summary>
    /// Spawns a visual hit marker effect at the hit location.
    /// </summary>
    private void SpawnHitMarker(Vector2 hitPosition)
    {
        if (hitMarkerPrefab != null)
        {
            GameObject hitMarker = Instantiate(hitMarkerPrefab, hitPosition, Quaternion.identity);

            // Try to set the color if it's a particle system
            ParticleSystem ps = hitMarker.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                main.startColor = hitMarkerColor;
            }

            // Try to set the color if it's a sprite renderer
            SpriteRenderer sr = hitMarker.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.color = hitMarkerColor;
            }

            // Destroy after duration
            Destroy(hitMarker, hitMarkerDuration);
        }
    }

    // Visual debugging for attack ranges
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
    }
}