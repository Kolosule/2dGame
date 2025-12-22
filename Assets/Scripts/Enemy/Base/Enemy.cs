using UnityEngine;
using Fusion;
using System.Collections;

/// <summary>
/// Networked enemy base class for Photon Fusion.
/// Handles health, damage, knockback with network synchronization.
/// NOTE: This is a simplified version - you'll need to adapt based on your existing Enemy.cs
/// </summary>
public class NetworkedEnemy : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyStats stats;
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Header("Combat")]
    [SerializeField] private float attackCooldown = 1f;
    [SerializeField] private float telegraphDuration = 0.5f;

    [Header("Visual Effects")]
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private float hitFlashDuration = 0.1f;

    // Networked health
    [Networked]
    public int CurrentHealth { get; private set; }

    [Networked]
    private NetworkBool IsDead { get; set; }

    [Networked]
    private TickTimer AttackCooldownTimer { get; set; }

    [Networked]
    private TickTimer TelegraphTimer { get; set; }

    [Networked]
    private TickTimer KnockbackTimer { get; set; }

    // Component references
    private NetworkedEnemyAI enemyAI;
    private Rigidbody2D rb;
    private Color originalColor;

    // Public state checkers
    public bool IsTelegraphing() => TelegraphTimer.IsRunning;
    public bool IsKnockedBack() => KnockbackTimer.IsRunning;

    private void Awake()
    {
        enemyAI = GetComponent<NetworkedEnemyAI>();
        rb = GetComponent<Rigidbody2D>();

        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
    }

    public override void Spawned()
    {
        // Initialize health on server
        if (HasStateAuthority)
        {
            CurrentHealth = stats.maxHealth;
            IsDead = false;
        }
    }

    /// <summary>
    /// Take damage from a player or other source (SERVER ONLY)
    /// </summary>
    public void TakeDamage(int damage, Vector2 knockbackDirection, float knockbackForce)
    {
        // Only server processes damage
        if (!HasStateAuthority) return;

        if (IsDead) return;

        // Apply damage
        CurrentHealth -= damage;

        Debug.Log($"[SERVER] {stats.enemyName} took {damage} damage. Health: {CurrentHealth}/{stats.maxHealth}");

        // Apply knockback
        if (rb != null && knockbackForce > 0)
        {
            rb.linearVelocity = knockbackDirection.normalized * knockbackForce;
            KnockbackTimer = TickTimer.CreateFromSeconds(Runner, 0.3f);
        }

        // Notify all clients to show hit effects
        RPC_OnHit(transform.position);

        // Check if dead
        if (CurrentHealth <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// RPC to show hit effects on all clients
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnHit(Vector3 hitPosition)
    {
        // Spawn hit effect
        if (hitEffectPrefab != null)
        {
            GameObject effect = Instantiate(hitEffectPrefab, hitPosition, Quaternion.identity);
            Destroy(effect, 1f);
        }

        // Flash sprite
        if (spriteRenderer != null)
        {
            StartCoroutine(FlashOnHit());
        }
    }

    /// <summary>
    /// Flash sprite when taking damage
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
    /// Attack a player (SERVER ONLY)
    /// </summary>
    public void AttackPlayer(PlayerStatsHandler target)
    {
        // Only server controls attacks
        if (!HasStateAuthority) return;

        if (IsDead) return;

        // Check attack cooldown
        if (AttackCooldownTimer.IsRunning) return;

        // Start telegraph
        if (!TelegraphTimer.IsRunning)
        {
            TelegraphTimer = TickTimer.CreateFromSeconds(Runner, telegraphDuration);
            RPC_ShowTelegraph();
            return;
        }

        // Telegraph finished, execute attack
        if (TelegraphTimer.Expired(Runner))
        {
            // Calculate attack direction
            Vector2 direction = (target.transform.position - transform.position).normalized;

            // Apply damage to player
            // NOTE: You'll need to adapt this based on your PlayerStatsHandler implementation
            // target.TakeDamage(stats.damage, direction, stats.knockbackForce);

            // Start cooldown
            AttackCooldownTimer = TickTimer.CreateFromSeconds(Runner, attackCooldown);

            // Notify clients
            RPC_OnAttack(target.transform.position);
        }
    }

    /// <summary>
    /// RPC to show attack telegraph on all clients
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ShowTelegraph()
    {
        // Add visual telegraph effect here (flash, change color, etc.)
        if (spriteRenderer != null)
        {
            StartCoroutine(TelegraphEffect());
        }
    }

    private IEnumerator TelegraphEffect()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = Color.yellow;
            yield return new WaitForSeconds(telegraphDuration);
            spriteRenderer.color = originalColor;
        }
    }

    /// <summary>
    /// RPC to show attack effect on all clients
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnAttack(Vector3 targetPosition)
    {
        // Play attack animation/sound here
        Debug.Log($"{stats.enemyName} attacked!");
    }

    /// <summary>
    /// Handle enemy death (SERVER ONLY)
    /// </summary>
    private void Die()
    {
        if (!HasStateAuthority) return;

        IsDead = true;

        Debug.Log($"[SERVER] {stats.enemyName} has died!");

        // Notify AI to drop coins
        if (enemyAI != null)
        {
            enemyAI.OnDeath();
        }

        // Notify all clients
        RPC_OnDeath();

        // Despawn after a short delay (using coroutine)
        StartCoroutine(DespawnAfterDelay(0.5f));
    }

    /// <summary>
    /// Despawn after a delay to allow death effects to play
    /// </summary>
    private IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (HasStateAuthority)
        {
            Runner.Despawn(Object);
        }
    }

    /// <summary>
    /// RPC to show death effects on all clients
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnDeath()
    {
        // Play death animation/sound here
        Debug.Log($"{stats.enemyName} died!");

        // Optional: Disable visuals before despawn
        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = false;
        }
    }
}