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

    // Networked visual state so proxies (other clients) still see the attack
    // telegraph flash and correct facing — the AI itself only runs on the
    // state authority, so these must be synced for remote viewers.
    [Networked] public NetworkBool IsTelegraphing { get; set; }
    [Networked] public NetworkBool FacingLeft { get; set; }

    // Knockback tracking (TickTimer = simulation-path timing, authority only)
    private TickTimer knockbackTimer;

    // Combat tracking (attack cooldown, authority only)
    private TickTimer attackCooldownTimer;

    // Team component reference
    private EnemyTeamComponent teamComponent;

    // Rigidbody reference
    private Rigidbody2D rb;

    // AI driver (authority only)
    private EnemyAI ai;

    // Effective (ring-scaled) stats, resolved once on the authority in Spawned().
    private int effectiveMaxHealth;
    private int effectiveAttackDamage;
    private float effectiveMoveSpeed;

    // Home anchor captured at spawn (authority); the AI leashes to this point.
    public Vector2 Home { get; private set; }

    /// <summary>
    /// Called when this enemy spawns on the network
    /// </summary>
    public override void Spawned()
    {
        // Get components first (needed by both authority and proxies).
        teamComponent = GetComponent<EnemyTeamComponent>();
        rb = GetComponent<Rigidbody2D>();
        ai = GetComponent<EnemyAI>();

        if (stats == null)
        {
            Debug.LogError($"Enemy on {gameObject.name} has no EnemyStats assigned!");
            return;
        }

        if (HasStateAuthority)
        {
            ResolveEffectiveStats();
            CurrentHealth = effectiveMaxHealth;

            if (ai != null)
            {
                ai.Initialize(Home, effectiveMoveSpeed, stats);
            }
        }

        if (coinPrefab == null)
        {
            Debug.LogWarning($"{gameObject.name} has no coin prefab assigned - won't drop coins on death!");
        }
    }

    /// <summary>
    /// Authority-only: capture home and scale base stats by the difficulty ring for
    /// this enemy's distance from the arena center. Falls back to base stats (x1.0)
    /// if the ring config or arena center is missing.
    /// </summary>
    private void ResolveEffectiveStats()
    {
        Home = transform.position;

        RingTier tier = RingTier.Identity;
        DifficultyRingConfig ringConfig = GameSettingsManager.Instance != null
            ? GameSettingsManager.Instance.GetDifficultyRingConfig()
            : null;

        if (ringConfig != null && ArenaCenter.Instance != null)
        {
            float distance = Vector2.Distance(Home, ArenaCenter.Instance.Position);
            tier = ringConfig.GetRing(distance);
        }
        else
        {
            Debug.LogWarning($"{gameObject.name}: no DifficultyRingConfig/ArenaCenter; using base stats.");
        }

        effectiveMaxHealth = Mathf.Max(1, Mathf.RoundToInt(stats.maxHealth * tier.healthMult));
        effectiveAttackDamage = Mathf.Max(0, Mathf.RoundToInt(stats.attackDamage * tier.damageMult));
        effectiveMoveSpeed = stats.moveSpeed * tier.speedMult;
    }

    /// <summary>
    /// Authoritative AI step. The state machine and Rigidbody2D are only driven on
    /// the state authority; proxies interpolate position via NetworkRigidbody2D and
    /// read the networked visual state in Render().
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        if (ai != null) ai.Tick();
    }

    /// <summary>Applies networked visual state (facing + telegraph flash) on every client.</summary>
    public override void Render()
    {
        if (ai != null) ai.RenderVisuals();
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

        // Apply knockback
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero; // Reset current velocity
            rb.AddForce(knockbackForce, ForceMode2D.Impulse);

            // Set knockback state with duration (0.3s) — pauses AI movement
            knockbackTimer = TickTimer.CreateFromSeconds(Runner, 0.3f);
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
        // Knocked back while the timer is running and not yet expired.
        return !knockbackTimer.ExpiredOrNotRunning(Runner);
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
        if (!attackCooldownTimer.ExpiredOrNotRunning(Runner))
        {
            return;
        }

        // Calculate damage through the unified pipeline (review item #4).
        int finalDamage = effectiveAttackDamage;
        CombatConfig config = GameSettingsManager.Instance != null
            ? GameSettingsManager.Instance.GetCombatConfig()
            : null;
        if (config != null)
        {
            Team myTeam = teamComponent != null ? teamComponent.Team : Team.None;
            PlayerTeamData playerTeam = player.GetComponent<PlayerTeamData>();
            Team defenderTeam = playerTeam != null ? playerTeam.Team : Team.None;
            finalDamage = config.ResolveDamage(effectiveAttackDamage, myTeam, transform.position,
                                               defenderTeam, player.transform.position);
        }

        // Deal damage to player, attributed to this enemy (per-attacker hit cooldown).
        player.ServerApplyDamage(finalDamage, Object.Id);
        attackCooldownTimer = TickTimer.CreateFromSeconds(Runner, stats.attackCooldown);

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


        // Spawn each coin with slight scatter
        for (int i = 0; i < coinCount; i++)
        {
            // Calculate random scatter position
            Vector2 randomOffset = Random.insideUnitCircle * coinScatterRadius;
            Vector3 spawnPosition = transform.position + new Vector3(randomOffset.x, randomOffset.y, 0);

            // ⭐ Spawn the coin on the network
            // Runner.Spawn makes sure ALL clients see the coin!
            // The coin gives itself its "pop" and falls under its own server-side simulation
            // (see NetworkedCoinPickup), so no Rigidbody/force handling is needed here.
            Runner.Spawn(
                coinPrefab,
                spawnPosition,
                Quaternion.identity
            );
        }

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
        return effectiveMaxHealth;
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