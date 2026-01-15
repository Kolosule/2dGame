using UnityEngine;
using Fusion;

/// <summary>
/// FIXED VERSION - Now properly networked for multiplayer!
/// 
/// WHAT CHANGED:
/// - Inherits from NetworkBehaviour instead of MonoBehaviour
/// - Health is now [Networked] so all clients see the same value
/// - Only server handles damage and death
/// - Clients automatically sync health changes
/// 
/// This ensures that when the host kills an enemy, all clients see it die!
/// </summary>
public class Enemy : NetworkBehaviour
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

    // ⭐ CRITICAL FIX: Health is now networked!
    // This means all clients will see the same health value
    [Networked]
    private int CurrentHealth { get; set; }

    // Knockback tracking
    private bool isKnockedBack = false;
    private float knockbackEndTime = 0f;

    // Combat tracking
    private float lastAttackTime = -999f;

    // Team component reference
    private EnemyTeamComponent teamComponent;

    // Rigidbody reference
    private Rigidbody2D rb;

    /// <summary>
    /// Called when this enemy spawns on the network
    /// </summary>
    public override void Spawned()
    {
        // Initialize health from stats
        if (stats != null)
        {
            // ⭐ IMPORTANT: Only the server sets initial health
            // Clients will automatically receive this value
            if (HasStateAuthority)
            {
                CurrentHealth = stats.maxHealth;
                Debug.Log($"[SERVER] {stats.enemyName} spawned with {CurrentHealth} health");
            }
        }
        else
        {
            Debug.LogError($"Enemy on {gameObject.name} has no EnemyStats assigned!");
        }

        // Get components
        teamComponent = GetComponent<EnemyTeamComponent>();
        rb = GetComponent<Rigidbody2D>();

        // Warn if coin prefab is missing
        if (coinPrefab == null)
        {
            Debug.LogWarning($"{gameObject.name} has no coin prefab assigned - won't drop coins on death!");
        }
    }

    /// <summary>
    /// Apply damage to this enemy with knockback
    /// 
    /// HOW THIS WORKS:
    /// - Any client can call this (e.g., when player hits enemy)
    /// - But only the SERVER actually applies the damage
    /// - The health change is then synced to all clients automatically
    /// </summary>
    public void TakeDamage(int amount, Vector2 knockbackForce, Vector2 hitPoint)
    {
        // ⭐ CRITICAL: Only server can modify health
        // If a client tries to damage an enemy, we need to tell the server
        if (!HasStateAuthority)
        {
            // Client detected a hit - tell the server about it
            RPC_TakeDamage(amount, knockbackForce, hitPoint);
            return;
        }

        // SERVER CODE BELOW:
        // Apply damage
        CurrentHealth -= amount;
        Debug.Log($"[SERVER] {stats.enemyName} took {amount} damage. Health: {CurrentHealth}/{stats.maxHealth}");

        // Apply knockback
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero; // Reset current velocity
            rb.AddForce(knockbackForce, ForceMode2D.Impulse);

            // Set knockback state with duration
            isKnockedBack = true;
            knockbackEndTime = Time.time + 0.3f; // 0.3 second knockback duration
        }

        // Check if dead
        if (CurrentHealth <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// RPC that lets clients tell the server about damage
    /// 
    /// WHAT IS AN RPC?
    /// - RPC = Remote Procedure Call
    /// - It's like a phone call from client to server
    /// - Client says "hey, I hit this enemy for X damage"
    /// - Server then processes the damage
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_TakeDamage(int amount, Vector2 knockbackForce, Vector2 hitPoint)
    {
        // This runs on the SERVER when a client reports a hit
        TakeDamage(amount, knockbackForce, hitPoint);
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
    public void AttackPlayer(PlayerStatsHandler player)
    {
        // ⭐ IMPORTANT: Only server should attack
        // Clients will see the attack results through health sync
        if (!HasStateAuthority)
        {
            return;
        }

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
        }

        // Deal damage to player
        player.TakeDamage(finalDamage);
        lastAttackTime = Time.time;

        Debug.Log($"[SERVER] {stats.enemyName} attacked {player.name} for {finalDamage} damage!");
    }

    /// <summary>
    /// Enemy death handler - NOW DROPS COINS!
    /// Only runs on server
    /// </summary>
    private void Die()
    {
        // ⭐ Double-check we're on the server
        if (!HasStateAuthority)
        {
            return;
        }

        Debug.Log($"[SERVER] {stats.enemyName} has died!");

        // Spawn coins if we have a coin prefab
        if (coinPrefab != null)
        {
            SpawnCoins();
        }

        // ⭐ IMPORTANT: Use Runner.Despawn instead of Destroy
        // This removes the enemy from the network properly
        Runner.Despawn(Object);
    }

    /// <summary>
    /// Spawns coins at the enemy's death position
    /// Only called on server
    /// </summary>
    private void SpawnCoins()
    {
        // Determine how many coins to drop
        int coinCount = Random.Range(coinsToDropMin, coinsToDropMax + 1);

        Debug.Log($"[SERVER] Spawning {coinCount} coins from {stats.enemyName} death");

        // Spawn each coin with slight scatter
        for (int i = 0; i < coinCount; i++)
        {
            // Calculate random scatter position
            Vector2 randomOffset = Random.insideUnitCircle * coinScatterRadius;
            Vector3 spawnPosition = transform.position + new Vector3(randomOffset.x, randomOffset.y, 0);

            // ⭐ Spawn the coin on the network
            // Runner.Spawn makes sure ALL clients see the coin!
            NetworkObject coin = Runner.Spawn(
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

        Debug.Log($"[SERVER] Successfully spawned {coinCount} coins!");
    }

    /// <summary>
    /// Get current health (useful for health bars)
    /// </summary>
    public int GetCurrentHealth()
    {
        return CurrentHealth;
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