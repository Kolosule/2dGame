using UnityEngine;
using System.Collections;

/// <summary>
/// Base enemy class handling health, damage, knockback, and combat.
/// Improved version with telegraphed attacks and hit markers.
/// </summary>
public class Enemy : MonoBehaviour
{
    [Header("Stats")]
    [SerializeField] private EnemyStats stats;

    [Header("Attack Telegraph")]
    [Tooltip("Show warning indicator before attacking")]
    [SerializeField] private bool useTelegraph = true;

    [Tooltip("Time to show telegraph before attacking (in seconds)")]
    [SerializeField] private float telegraphDuration = 0.5f;

    [Tooltip("Visual indicator for telegraph (sprite or particle effect)")]
    [SerializeField] private GameObject telegraphPrefab;

    [Tooltip("Color to flash when telegraphing attack")]
    [SerializeField] private Color telegraphColor = new Color(1f, 0.3f, 0.3f, 1f);

    [Header("Attack Cooldown")]
    [Tooltip("Time between attacks in seconds")]
    [SerializeField] private float attackCooldown = 1f;

    [Header("Hit Feedback")]
    [Tooltip("Particle effect when enemy takes damage")]
    [SerializeField] private GameObject hitEffectPrefab;

    [Tooltip("Duration of hit flash effect")]
    [SerializeField] private float hitFlashDuration = 0.1f;

    // Component references
    private EnemyTeamComponent teamComponent;
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private EnemyAI enemyAI;

    // Combat state
    private int currentHealth;
    private float lastAttackTime = -999f;
    private bool isTelegraphing = false;
    private GameObject currentTelegraph;

    // Knockback state
    private bool isKnockedBack = false;
    private float knockbackEndTime = 0f;

    // Visual feedback
    private Color originalColor;

    void Awake()
    {
        currentHealth = stats.maxHealth;
        teamComponent = GetComponent<EnemyTeamComponent>();
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        enemyAI = GetComponent<EnemyAI>();

        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }

        if (teamComponent == null)
        {
            Debug.LogError($"Enemy '{stats.enemyName}' is missing EnemyTeamComponent!");
        }

        // Use attack cooldown from stats if available, otherwise use serialized field
        if (stats.attackCooldown > 0)
        {
            attackCooldown = stats.attackCooldown;
        }
    }

    /// <summary>
    /// Take damage with knockback and visual feedback.
    /// </summary>
    public void TakeDamage(int amount, Vector2 knockbackForce, Vector2 hitPoint)
    {
        // Apply defensive modifier from territory
        float defenseModifier = teamComponent != null ? teamComponent.GetDamageReceivedModifier() : 1f;
        int finalDamage = Mathf.RoundToInt(amount * defenseModifier);

        currentHealth -= finalDamage;

        // Apply knockback
        if (rb != null && knockbackForce.magnitude > 0.1f)
        {
            rb.linearVelocity = Vector2.zero;
            rb.AddForce(knockbackForce, ForceMode2D.Impulse);

            isKnockedBack = true;
            knockbackEndTime = Time.time + 0.3f;
        }

        // Visual feedback
        SpawnHitEffect(hitPoint);
        StartCoroutine(FlashOnHit());

        Debug.Log($"{stats.enemyName} took {finalDamage} damage. Health: {currentHealth}/{stats.maxHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// Check if enemy is currently knocked back (AI should pause movement).
    /// </summary>
    public bool IsKnockedBack()
    {
        if (isKnockedBack && Time.time >= knockbackEndTime)
        {
            isKnockedBack = false;
        }
        return isKnockedBack;
    }

    /// <summary>
    /// Check if enemy is currently telegraphing an attack.
    /// </summary>
    public bool IsTelegraphing()
    {
        return isTelegraphing;
    }

    /// <summary>
    /// Attack a player with optional telegraph warning.
    /// </summary>
    public void AttackPlayer(PlayerStatsHandler player)
    {
        if (player == null)
        {
            Debug.LogWarning($"{stats.enemyName} tried to attack null player!");
            return;
        }

        // Check attack cooldown
        if (Time.time - lastAttackTime < attackCooldown)
        {
            return;
        }

        // Start telegraphed attack
        if (useTelegraph && !isTelegraphing)
        {
            StartCoroutine(TelegraphedAttack(player));
        }
        else if (!useTelegraph)
        {
            PerformAttack(player);
        }
    }

    /// <summary>
    /// Coroutine for telegraphed attack with warning indicator.
    /// </summary>
    private IEnumerator TelegraphedAttack(PlayerStatsHandler player)
    {
        isTelegraphing = true;

        // Show telegraph visual
        if (telegraphPrefab != null)
        {
            currentTelegraph = Instantiate(telegraphPrefab, transform.position, Quaternion.identity, transform);
        }

        // Flash color to indicate attack
        if (spriteRenderer != null)
        {
            spriteRenderer.color = telegraphColor;
        }

        // Wait for telegraph duration
        yield return new WaitForSeconds(telegraphDuration);

        // Cleanup telegraph visual
        if (currentTelegraph != null)
        {
            Destroy(currentTelegraph);
        }

        // Restore original color
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }

        isTelegraphing = false;

        // Perform the actual attack
        if (player != null) // Check player still exists
        {
            PerformAttack(player);
        }
    }

    /// <summary>
    /// Perform the actual attack damage.
    /// </summary>
    private void PerformAttack(PlayerStatsHandler player)
    {
        // Calculate damage with territorial modifier
        int finalDamage = stats.attackDamage;
        if (teamComponent != null)
        {
            float attackModifier = teamComponent.GetDamageDealtModifier();
            finalDamage = Mathf.RoundToInt(stats.attackDamage * attackModifier);
        }

        // Deal damage to player
        player.TakeDamage(finalDamage);
        lastAttackTime = Time.time;

        Debug.Log($"{stats.enemyName} dealt {finalDamage} damage to player");
    }

    /// <summary>
    /// Spawn hit effect at damage point.
    /// </summary>
    private void SpawnHitEffect(Vector2 hitPoint)
    {
        if (hitEffectPrefab != null)
        {
            GameObject effect = Instantiate(hitEffectPrefab, hitPoint, Quaternion.identity);
            Destroy(effect, 1f);
        }
    }

    /// <summary>
    /// Flash sprite when taking damage.
    /// </summary>
    private IEnumerator FlashOnHit()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = Color.red;
            yield return new WaitForSeconds(hitFlashDuration);
            spriteRenderer.color = originalColor;
        }
    }

    /// <summary>
    /// Handle enemy death.
    /// </summary>
    private void Die()
    {
        Debug.Log($"{stats.enemyName} has died!");

        // Notify AI to drop coin if applicable
        if (enemyAI != null)
        {
            enemyAI.OnDeath();
        }

        Destroy(gameObject);
    }

    // Visual feedback for detection range
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 5f); // Detection range

        if (useTelegraph)
        {
            Gizmos.color = telegraphColor;
            Gizmos.DrawWireSphere(transform.position, 1f); // Attack range indicator
        }
    }
}