using UnityEngine;

public class Enemy : MonoBehaviour
{
    [SerializeField] private EnemyStats stats;
    private int currentHealth;
    private EnemyTeamComponent teamComponent;
    private float lastAttackTime = -999f;

    // Knockback state
    private bool isKnockedBack = false;
    private float knockbackEndTime = 0f;
    private Rigidbody2D rb;

    void Awake()
    {
        currentHealth = stats.maxHealth;
        teamComponent = GetComponent<EnemyTeamComponent>();
        rb = GetComponent<Rigidbody2D>();

        if (teamComponent == null)
        {
            Debug.LogError($"Enemy '{stats.enemyName}' is missing EnemyTeamComponent!");
        }
    }

    public void TakeDamage(int amount, Vector2 knockbackForce, Vector2 hitPoint)
    {
        // Apply defensive modifier
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

        Debug.Log($"{stats.enemyName} took {finalDamage} damage. Health: {currentHealth}/{stats.maxHealth}");

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
            return;
        }

        // Calculate damage with territorial modifier
        int finalDamage = stats.attackDamage;
        if (teamComponent != null)
        {
            float attackModifier = teamComponent.GetDamageDealtModifier();
            finalDamage = Mathf.RoundToInt(stats.attackDamage * attackModifier);
            Debug.Log($"{stats.enemyName} attacking with {finalDamage} damage (base: {stats.attackDamage}, modifier: {attackModifier:F2}x)");
        }

        // Deal damage to player
        player.TakeDamage(finalDamage);
        lastAttackTime = Time.time;
    }

    private void Die()
    {
        Debug.Log($"{stats.enemyName} has died!");
        Destroy(gameObject);
    }

    // Visual feedback for detection range
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 5f); // Detection range
    }
}