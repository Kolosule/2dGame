using UnityEngine;

/// <summary>
/// FIXED VERSION - Now includes stunning effect!
/// Projectile script for player ranged attacks
/// Features:
/// - Gravity-affected arc trajectory
/// - Collision with surfaces, players, and enemies
/// - Team-based damage (respects friendly fire settings)
/// - Stunning effect that prevents dash/jump until grounded
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

    [Header("NEW: Stun Settings")]
    [Tooltip("Duration of stun effect in seconds")]
    [SerializeField] private float stunDuration = 1.5f;

    [Tooltip("Should the projectile stun players?")]
    [SerializeField] private bool stunPlayers = true;

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
    private string shooterTeam;
    private bool hasHit = false;

    /// <summary>
    /// Initialize the projectile with direction, speed, damage, and shooter team
    /// Called by PlayerCombat when spawning
    /// </summary>
    public void Initialize(Vector2 direction, float projectileSpeed, int projectileDamage, string team)
    {
        speed = projectileSpeed;
        damage = projectileDamage;
        shooterTeam = team;

        if (rb != null)
        {
            rb.linearVelocity = direction.normalized * speed;
        }

        Debug.Log($"Projectile initialized: Speed={speed}, Damage={damage}, Team={shooterTeam}, Stun={stunDuration}s");
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<CircleCollider2D>();

        if (rb != null)
        {
            rb.gravityScale = 1f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    void Update()
    {
        if (rb != null && rb.linearVelocity.magnitude > 0.1f)
        {
            float angle = Mathf.Atan2(rb.linearVelocity.y, rb.linearVelocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }
    }

    /// <summary>
    /// FIXED: Handle collisions with triggers - now includes stunning!
    /// </summary>
    void OnTriggerEnter2D(Collider2D other)
    {
        if (hasHit) return;

        Debug.Log($"Projectile hit trigger: {other.gameObject.name}");

        bool shouldDestroy = false;

        // Check for player hit
        PlayerStatsHandler playerStats = other.GetComponent<PlayerStatsHandler>();
        if (playerStats != null)
        {
            PlayerTeamComponent playerTeam = other.GetComponent<PlayerTeamComponent>();

            // Check if friendly fire or enemy hit
            if (playerTeam == null || playerTeam.teamID != shooterTeam)
            {
                // Damage the player
                playerStats.RPC_TakeDamage(damage);
                Debug.Log($"Projectile hit player for {damage} damage!");

                // NEW: Apply stun effect to the player
                if (stunPlayers)
                {
                    PlayerMovement playerMovement = other.GetComponent<PlayerMovement>();
                    if (playerMovement != null)
                    {
                        playerMovement.ApplyStun(stunDuration);
                        Debug.Log($"Player stunned for {stunDuration} seconds!");
                    }
                }

                shouldDestroy = true;
            }
            else
            {
                Debug.Log("Projectile hit friendly player - no damage");
            }
        }

        // Check for enemy hit
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy != null)
        {
            Vector2 knockbackDirection = (other.transform.position - transform.position).normalized;
            Vector2 knockbackForce = knockbackDirection * 5f;

            enemy.TakeDamage(damage, knockbackForce, other.transform.position);
            Debug.Log($"Projectile hit enemy for {damage} damage!");

            shouldDestroy = true;
        }

        // Check for ground/wall collision
        if (other.gameObject.layer == LayerMask.NameToLayer("Ground") ||
            other.gameObject.CompareTag("Wall"))
        {
            Debug.Log("Projectile hit surface");
            shouldDestroy = true;
        }

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
        DestroyProjectile(collision.contacts[0].point);
    }

    /// <summary>
    /// Destroy projectile with effects
    /// </summary>
    void DestroyProjectile(Vector3 hitPosition)
    {
        if (hasHit) return;
        hasHit = true;

        if (impactEffect != null)
        {
            GameObject effect = Instantiate(impactEffect, hitPosition, Quaternion.identity);
            Destroy(effect, 2f);
        }

        if (impactSound != null)
        {
            AudioSource.PlayClipAtPoint(impactSound, transform.position);
        }

        if (trail != null)
        {
            trail.transform.SetParent(null);
            Destroy(trail.gameObject, trail.time);
        }

        Destroy(gameObject);
    }
}