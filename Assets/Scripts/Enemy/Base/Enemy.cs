using UnityEngine;

public class Enemy : MonoBehaviour
{
    [SerializeField] private EnemyStats stats;
    private int currentHealth;
    private EnemyTeamComponent teamComponent;
    private float lastAttackTime = -999f; // Allow first attack immediately

    // Knockback tracking
    private bool isKnockedBack = false;
    private float knockbackEndTime = 0f;

    void Awake()
    {
        currentHealth = stats.maxHealth;
        teamComponent = GetComponent<EnemyTeamComponent>();

        if (teamComponent == null)
        {
            Debug.LogError($"Enemy '{stats.enemyName}' is missing EnemyTeamComponent!");
        }
    }

    public void TakeDamage(int amount, Vector2 knockbackForce, Vector2 hitPoint)
    {
        // Apply defensive modifier (take less damage at own base, more at enemy base)
        float defenseModifier = teamComponent != null ? teamComponent.GetDamageReceivedModifier() : 1f;
        int finalDamage = Mathf.RoundToInt(amount * defenseModifier);

        currentHealth -= finalDamage;

        // Apply knockback
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null && knockbackForce.magnitude > 0.1f)
        {
            // Stop current movement
            rb.linearVelocity = Vector2.zero;

            // Apply knockback force
            rb.AddForce(knockbackForce, ForceMode2D.Impulse);

            // Flag as knocked back for a short duration
            isKnockedBack = true;
            knockbackEndTime = Time.time + 0.3f; // 0.3 second knockback stun

            Debug.Log($"{stats.enemyName} knocked back by {knockbackForce}");
        }

        Debug.Log($"{stats.enemyName} took {finalDamage} damage (base: {amount}, modifier: {defenseModifier:F2}x) at {hitPoint}. Health = {currentHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// Check if enemy is currently in knockback state (for AI to pause movement)
    /// </summary>
    public bool IsKnockedBack()
    {
        if (isKnockedBack && Time.time >= knockbackEndTime)
        {
            isKnockedBack = false;
        }
        return isKnockedBack;
    }

    // ========== FIX: ADD ATTACK COOLDOWN ==========
    public void AttackPlayer(PlayerStatsHandler player)
    {
        if (player == null)
        {
            Debug.LogWarning($"{stats.enemyName} tried to attack but player reference was null!");
            return;
        }

        // Check attack cooldown
        if (Time.time - lastAttackTime < stats.attackCooldown)
        {
            return; // Still on cooldown
        }

        if (teamComponent == null)
        {
            Debug.LogWarning($"{stats.enemyName} has no team component! Using base damage.");
            player.TakeDamage(stats.attackDamage);
            lastAttackTime = Time.time;
            return;
        }

        // Apply offensive modifier (deal more damage at own base, less at enemy base)
        float damageModifier = teamComponent.GetDamageDealtModifier();
        int finalDamage = Mathf.RoundToInt(stats.attackDamage * damageModifier);

        player.TakeDamage(finalDamage);
        lastAttackTime = Time.time; // Record attack time

        Debug.Log($"{stats.enemyName} (Team: {teamComponent.teamID}, Territory: {teamComponent.territorialAdvantage:F2}) attacked player for {finalDamage} damage (base: {stats.attackDamage}, modifier: {damageModifier:F2}x)!");
    }

    private void Die()
    {
        DropCoins();
        Debug.Log($"{stats.enemyName} died!");
        Destroy(gameObject);
    }

    [SerializeField] private GameObject coinPrefab;
    [SerializeField] private int coinDropCount = 3;

    private void DropCoins()
    {
        if (coinPrefab == null) return;

        for (int i = 0; i < coinDropCount; i++)
        {
            Vector3 spawnPos = transform.position + new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f), 0);
            Instantiate(coinPrefab, spawnPos, Quaternion.identity);
        }
    }
}