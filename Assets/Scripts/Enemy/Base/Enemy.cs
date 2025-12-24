using UnityEngine;
using Fusion;

/// <summary>
/// FIXED VERSION - Now drops coins on death!
/// Base enemy class handling health, damage, and combat.
/// Works with non-networked enemies (spawned by server via EnemySpawner).
/// </summary>
public class Enemy : MonoBehaviour
{
    [Header("Enemy Configuration")]
    [SerializeField] private EnemyStats stats;

    [Header("Coin Drop Settings")]
    [Tooltip("Coin prefab to spawn when this enemy dies")]
    [SerializeField] private NetworkObject coinPrefab;

    [Tooltip("How many coins to drop on death")]
    [SerializeField] private int coinsToDropMin = 1;
    [SerializeField] private int coinsToDropMax = 3;

    [Tooltip("How far coins should scatter from death position")]
    [SerializeField] private float coinScatterRadius = 1.5f;

    // Health tracking
    private int currentHealth;

    // Knockback tracking
    private bool isKnockedBack = false;
    private float knockbackEndTime = 0f;

    // Combat tracking
    private float lastAttackTime = -999f;

    // Team component reference
    private EnemyTeamComponent teamComponent;

    private void Start()
    {
        // Initialize health from stats
        if (stats != null)
        {
            currentHealth = stats.maxHealth;
            Debug.Log($"{stats.enemyName} initialized with {currentHealth} health");
        }
        else
        {
            Debug.LogError($"Enemy on {gameObject.name} has no EnemyStats assigned!");
        }

        // Get team component for territorial damage modifiers
        teamComponent = GetComponent<EnemyTeamComponent>();

        // Warn if coin prefab is missing
        if (coinPrefab == null)
        {
            Debug.LogWarning($"{gameObject.name} has no coin prefab assigned - won't drop coins on death!");
        }
    }

    /// <summary>
    /// Apply damage to this enemy with knockback
    /// </summary>
    public void TakeDamage(int amount, Vector2 knockbackForce, Vector2 hitPoint)
    {
        // Apply damage
        currentHealth -= amount;
        Debug.Log($"{stats.enemyName} took {amount} damage. Health: {currentHealth}/{stats.maxHealth}");

        // Apply knockback
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero; // Reset current velocity
            rb.AddForce(knockbackForce, ForceMode2D.Impulse);

            // Set knockback state with duration
            isKnockedBack = true;
            knockbackEndTime = Time.time + 0.3f; // 0.3 second knockback duration
        }

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
    /// Enemy death handler - NOW DROPS COINS!
    /// </summary>
    private void Die()
    {
        Debug.Log($"{stats.enemyName} has died!");

        // Spawn coins if we have a coin prefab
        if (coinPrefab != null)
        {
            SpawnCoins();
        }

        // TODO: Add death effects, animations, etc.

        Destroy(gameObject);
    }

    /// <summary>
    /// Spawns coins at the enemy's death position
    /// </summary>
    private void SpawnCoins()
    {
        // Only spawn coins on the server/host
        NetworkRunner runner = FindFirstObjectByType<NetworkRunner>();
        if (runner == null || (!runner.IsServer && !runner.IsSharedModeMasterClient))
        {
            Debug.Log("Not server - skipping coin spawn");
            return;
        }

        // Determine how many coins to drop
        int coinCount = Random.Range(coinsToDropMin, coinsToDropMax + 1);

        Debug.Log($"Spawning {coinCount} coins from {stats.enemyName} death");

        // Spawn each coin with slight scatter
        for (int i = 0; i < coinCount; i++)
        {
            // Calculate random scatter position
            Vector2 randomOffset = Random.insideUnitCircle * coinScatterRadius;
            Vector3 spawnPosition = transform.position + new Vector3(randomOffset.x, randomOffset.y, 0);

            // Spawn the coin on the network
            NetworkObject coin = runner.Spawn(
                coinPrefab,
                spawnPosition,
                Quaternion.identity
            );

            // Optional: Add a small upward force to make coins "pop out"
            if (coin != null)
            {
                Rigidbody2D coinRb = coin.GetComponent<Rigidbody2D>();
                if (coinRb != null)
                {
                    Vector2 popForce = new Vector2(
                        Random.Range(-2f, 2f),  // Random horizontal force
                        Random.Range(3f, 5f)     // Upward force
                    );
                    coinRb.AddForce(popForce, ForceMode2D.Impulse);
                }
            }
        }

        Debug.Log($"✓ Successfully spawned {coinCount} coins!");
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
        Gizmos.DrawWireSphere(transform.position, 1.5f); // Attack range

        // Show coin scatter radius
        Gizmos.color = new Color(1f, 0.84f, 0f, 0.3f); // Transparent gold
        Gizmos.DrawWireSphere(transform.position, coinScatterRadius);
    }
}