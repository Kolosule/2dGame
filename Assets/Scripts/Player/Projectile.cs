using UnityEngine;

/// <summary>
/// Projectile script for player ranged attacks
/// Features:
/// - Gravity-affected arc trajectory
/// - Collision with surfaces, players, and enemies
/// - Team-based damage (respects friendly fire settings)
/// - Auto-destroys on impact
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class Projectile : MonoBehaviour
{
    [Header("Projectile Stats")]
    [Tooltip("Damage dealt on hit")]
    private int damage = 15;

    [Tooltip("Initial speed multiplier")]
    private float speed = 10f;

    [Header("Visual Effects")]
    [Tooltip("Particle effect on impact (optional)")]
    [SerializeField] private GameObject impactEffect;

    [Tooltip("Trail renderer for projectile path (optional)")]
    [SerializeField] private TrailRenderer trail;

    [Header("Audio")]
    [Tooltip("Sound played on impact (optional)")]
    [SerializeField] private AudioClip impactSound;

    // Runtime variables
    private Rigidbody2D rb;
    private CircleCollider2D col;
    private string shooterTeam; // Team that fired this projectile
    private bool hasHit = false; // Prevent multiple hits

    /// <summary>
    /// Initialize the projectile with direction, speed, damage, and shooter team
    /// Called by PlayerCombat when spawning
    /// </summary>
    public void Initialize(Vector2 direction, float projectileSpeed, int projectileDamage, string team)
    {
        speed = projectileSpeed;
        damage = projectileDamage;
        shooterTeam = team;

        // Apply initial velocity
        if (rb != null)
        {
            rb.linearVelocity = direction.normalized * speed;
        }

        Debug.Log($"Projectile initialized: Speed={speed}, Damage={damage}, Team={shooterTeam}");
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<CircleCollider2D>();

        // Configure rigidbody for arc trajectory
        if (rb != null)
        {
            rb.gravityScale = 1f; // Enable gravity for arc
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; // Better collision
        }

        // Ensure collider is trigger
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    void Update()
    {
        // Rotate projectile to face movement direction
        if (rb != null && rb.linearVelocity.magnitude > 0.1f)
        {
            float angle = Mathf.Atan2(rb.linearVelocity.y, rb.linearVelocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }
    }

    /// <summary>
    /// Handle collisions with triggers (players, enemies, etc.)
    /// </summary>
    void OnTriggerEnter2D(Collider2D other)
    {
        if (hasHit) return; // Already hit something

        // Ignore collision with shooter (optional safety check)
        // You could add shooter GameObject tracking if needed

        // Check what we hit
        bool shouldDestroy = false;

        // Hit a player?
        PlayerStatsHandler player = other.GetComponent<PlayerStatsHandler>();
        if (player != null)
        {
            PlayerTeamComponent playerTeam = player.GetComponent<PlayerTeamComponent>();

            // Check friendly fire
            if (playerTeam != null)
            {
                bool friendlyFireEnabled = GameSettingsManager.Instance != null &&
                                           GameSettingsManager.Instance.friendlyFireEnabled;

                // Same team - check friendly fire setting
                if (playerTeam.teamID == shooterTeam)
                {
                    if (!friendlyFireEnabled)
                    {
                        Debug.Log("Projectile hit teammate - friendly fire disabled, ignoring");
                        return; // Don't damage teammates
                    }
                }

                // Different team OR friendly fire enabled - deal damage
                player.TakeDamage(damage);
                Debug.Log($"Projectile hit player: {player.name} for {damage} damage");
                shouldDestroy = true;
            }
        }

        // Hit an enemy?
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy != null)
        {
            EnemyTeamComponent enemyTeam = enemy.GetComponent<EnemyTeamComponent>();

            // Check if same team
            if (enemyTeam != null && enemyTeam.teamID == shooterTeam)
            {
                Debug.Log("Projectile hit teammate enemy - ignoring");
                return; // Don't damage teammates
            }

            // Calculate knockback direction (away from projectile)
            Vector2 knockbackDirection = (enemy.transform.position - transform.position).normalized;
            Vector2 knockbackForce = knockbackDirection * 5f; // Light knockback from projectile
            knockbackForce.y += 1f; // Small upward component

            // Damage enemy with knockback
            enemy.TakeDamage(damage, knockbackForce, other.ClosestPoint(transform.position));
            Debug.Log($"Projectile hit enemy: {enemy.name} for {damage} damage");
            shouldDestroy = true;
        }

        // Hit ground or wall?
        if (other.gameObject.layer == LayerMask.NameToLayer("Ground") ||
            other.gameObject.CompareTag("Wall"))
        {
            Debug.Log("Projectile hit surface");
            shouldDestroy = true;
        }

        // Destroy projectile
        if (shouldDestroy)
        {
            DestroyProjectile(other.ClosestPoint(transform.position));
        }
    }

    /// <summary>
    /// Handle collisions with non-trigger colliders
    /// </summary>
    void OnCollisionEnter2D(Collision2D collision)
    {
        if (hasHit) return;

        Debug.Log($"Projectile collided with: {collision.gameObject.name}");

        // Destroy on any collision (ground, walls, etc.)
        DestroyProjectile(collision.contacts[0].point);
    }

    /// <summary>
    /// Destroy projectile with effects
    /// </summary>
    void DestroyProjectile(Vector3 hitPosition)
    {
        if (hasHit) return;
        hasHit = true;

        // Spawn impact effect
        if (impactEffect != null)
        {
            GameObject effect = Instantiate(impactEffect, hitPosition, Quaternion.identity);
            Destroy(effect, 2f); // Clean up after 2 seconds
        }

        // Play impact sound
        if (impactSound != null)
        {
            AudioSource.PlayClipAtPoint(impactSound, transform.position);
        }

        // Detach trail if it exists (so it doesn't get destroyed with projectile)
        if (trail != null)
        {
            trail.transform.SetParent(null);
            Destroy(trail.gameObject, trail.time); // Destroy after trail fades
        }

        // Destroy the projectile
        Destroy(gameObject);
    }
}