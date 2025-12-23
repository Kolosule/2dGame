using UnityEngine;

/// <summary>
/// Standard enemy component for non-networked or local enemies.
/// Handles health, damage, knockback, and attacking players.
/// </summary>
public class Enemy : MonoBehaviour
{
    [SerializeField] private EnemyStats stats;

    // Health tracking
    private int currentHealth;

    // Component references
    private EnemyTeamComponent teamComponent;
    private Rigidbody2D rb;

    // Combat timing
    private float lastAttackTime = -999f;

    // Knockback state
    private bool isKnockedBack = false;
    private float knockbackEndTime = 0f;

    void Awake()
    {
        // Initialize health
        currentHealth = stats.maxHealth;

        // Get component references
        teamComponent = GetComponent<EnemyTeamComponent>();
        rb = GetComponent<Rigidbody2D>();

        // Warn if missing required components
        if (teamComponent == null)
        {
            Debug.LogError($"Enemy '{stats.enemyName}' is missing EnemyTeamComponent!");
        }

        if (rb == null)
        {
            Debug.LogWarning($"Enemy '{stats.enemyName}' is missing Rigidbody2D - knockback won't work!");
        }
    }

    /// <summary>
    /// Apply damage to this enemy with knockback
    /// </summary>
    /// <param name="amount">Damage amount</param>
    /// <param name="knockbackForce">Knockback force vector</param>
    /// <param name="hitPoint">Position where hit occurred</param>
    public void TakeDamage(int amount, Vector2 knockbackForce, Vector2 hitPoint)
    {
        // Apply defensive modifier based on territory
        float defenseModifier = teamComponent != null ? teamComponent.GetDamageReceivedModifier() : 1f;
        int finalDamage = Mathf.RoundToInt(amount * defenseModifier);

        currentHealth -= finalDamage;

        // Apply knockback
        if (rb != null && knockbackForce.magnitude > 0.1f)
        {
            // Stop current velocity completely
            rb.linearVelocity = Vector2.zero;

            // Apply the knockback force as an impulse
            rb.AddForce(knockbackForce, ForceMode2D.Impulse);

            // Enter knockback state
            isKnockedBack = true;
            knockbackEndTime = Time.time + 0.3f;

            Debug.Log($"{stats.enemyName} knocked back with force {knockbackForce}");
        }

        Debug.Log($"{stats.enemyName} took {finalDamage} damage (after {defenseModifier:F2}x modifier). Health: {currentHealth}/{stats.maxHealth}");

        // Check if dead
        if (currentHealth <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// Check if enemy is currently knocked back (AI should pause movement)
    /// </summary>
    public bool IsKnockedBack()
    {
        // Clear knockback state if time has expired
        if (isKnockedBack && Time.time >= knockbackEndTime)
        {
            isKnockedBack = false;
        }
        return isKnockedBack;
    }

    /// <summary>
    /// Attack a player
    /// </summary>
    /// <param name="player">The player to attack</param>
    public void AttackPlayer(PlayerStatsHandler player)
    {
        if (player == null)
        {
            Debug.LogWarning($"{stats.enemyName} tried to attack null player!");
            return;
        }

        // Check attack cooldown
        if (Time.time - lastAttackTime < stats.attackCooldown)
        {
            Debug.Log($"{stats.enemyName} attack on cooldown. Time remaining: {stats.attackCooldown - (Time.time - lastAttackTime):F2}");
            return;
        }

        // Calculate damage with territorial modifier
        int finalDamage = stats.attackDamage;
        if (teamComponent != null)
        {
            float attackModifier = teamComponent.GetDamageDealtModifier();
            finalDamage = Mathf.RoundToInt(stats.attackDamage * attackModifier);
            Debug.Log($"{stats.enemyName} attacking {player.name} with {finalDamage} damage (base: {stats.attackDamage}, modifier: {attackModifier:F2}x)");
        }

        // Deal damage to player
        player.TakeDamage(finalDamage);
        lastAttackTime = Time.time;

        Debug.Log($"{stats.enemyName} successfully attacked {player.name}!");
    }

    /// <summary>
    /// Enemy death handler
    /// </summary>
    private void Die()
    {
        Debug.Log($"{stats.enemyName} has died!");

        // TODO: Add death effects, spawn coins, etc.

        Destroy(gameObject);
    }

    /// <summary>
    /// Get current health (useful for health bars)
    /// </summary>
    public int GetCurrentHealth()
    {
        return currentHealth;
    }

    /// <summary>
    /// Get max health
    /// </summary>
    public int GetMaxHealth()
    {
        return stats.maxHealth;
    }

    // Visual feedback for detection range in editor
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 5f); // Detection range

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 1.5f); // Attack range (adjust to match your EnemyAI)
    }
}