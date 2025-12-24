using UnityEngine;
using Fusion;

/// <summary>
/// FULLY FIXED VERSION - Respawns at team spawn point AND fixes camera issues!
/// Handles player health, damage, death, and respawning for Photon Fusion.
/// Works with standard Enemy class (non-networked enemies).
/// </summary>
public class PlayerStatsHandler : NetworkBehaviour
{
    [SerializeField] private PlayerStats stats;

    // Networked health - automatically syncs across all clients
    [Networked] public float CurrentHealth { get; set; }

    [Networked] public bool IsDead { get; set; }

    private float lastAttackTime;
    private int playerID = -1; // Track player ID for MultiplayerRespawnManager

    public override void Spawned()
    {
        // Initialize health when spawned on network
        if (HasStateAuthority)
        {
            CurrentHealth = stats.maxHealth;
            IsDead = false;
        }

        Debug.Log($"Player initialized with {CurrentHealth} health");
    }

    /// <summary>
    /// Set the player ID (called by MultiplayerRespawnManager when spawning)
    /// </summary>
    public void SetPlayerID(int id)
    {
        playerID = id;
        Debug.Log($"Player ID set to: {playerID}");
    }

    /// <summary>
    /// Apply damage to this player. Only runs on server/host.
    /// </summary>
    public void TakeDamage(float amount)
    {
        // Only server can modify health
        if (!HasStateAuthority)
        {
            Debug.LogWarning("TakeDamage called on client - only server can damage players!");
            return;
        }

        // Don't take damage if already dead
        if (IsDead) return;

        // Apply defensive territorial modifier if available
        PlayerTeamComponent teamComponent = GetComponent<PlayerTeamComponent>();
        if (teamComponent != null)
        {
            float defenseModifier = teamComponent.GetDamageReceivedModifier();
            amount = amount * defenseModifier;
            Debug.Log($"Player damage modified by territory: {defenseModifier:F2}x");
        }

        CurrentHealth -= amount;
        Debug.Log($"Player took {amount} damage. Health = {CurrentHealth}/{stats.maxHealth}");

        // Check if health has dropped to 0 or below
        if (CurrentHealth <= 0)
        {
            CurrentHealth = 0;
            Die();
        }
    }

    /// <summary>
    /// Attack a standard enemy (non-networked).
    /// This can be called from client with input authority, server will validate.
    /// </summary>
    public void AttackEnemy(Enemy enemy)
    {
        if (enemy == null) return;

        // Check attack cooldown
        if (Time.time - lastAttackTime < stats.attackCooldown) return;

        // Apply offensive territorial modifier
        float attackDamage = stats.attackDamage;
        PlayerTeamComponent teamComponent = GetComponent<PlayerTeamComponent>();
        if (teamComponent != null)
        {
            float damageModifier = teamComponent.GetDamageDealtModifier();
            attackDamage = attackDamage * damageModifier;
            Debug.Log($"Player attack modified by territory: {damageModifier:F2}x");
        }

        // Calculate knockback
        Vector2 knockbackDirection = (enemy.transform.position - transform.position).normalized;
        Vector2 knockbackForce = knockbackDirection * stats.attackForce;

        // Add upward component to knockback
        knockbackForce.y += 2f;

        // Apply damage with knockback to enemy
        enemy.TakeDamage((int)attackDamage, knockbackForce, enemy.transform.position);

        Debug.Log($"Player attacked {enemy.name} for {attackDamage} damage.");

        lastAttackTime = Time.time;
    }

    /// <summary>
    /// Handles player death. Only runs on server.
    /// </summary>
    private void Die()
    {
        if (!HasStateAuthority) return;

        IsDead = true;
        Debug.Log("Player died!");

        // CAMERA FIX: Disable PlayerCameraRespawnHandler to prevent camera issues
        PlayerCameraRespawnHandler cameraHandler = GetComponent<PlayerCameraRespawnHandler>();
        if (cameraHandler != null)
        {
            cameraHandler.enabled = false;
            Debug.Log("Disabled PlayerCameraRespawnHandler to prevent camera jump issues");
        }

        // Disable player controls on all clients
        RPC_DisablePlayerControls();

        // Start respawn timer
        Invoke(nameof(Respawn), 3f); // Respawn after 3 seconds
    }

    /// <summary>
    /// RPC to disable player controls on all clients when dead
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_DisablePlayerControls()
    {
        // Disable combat/movement
        PlayerCombat combat = GetComponent<PlayerCombat>();
        if (combat != null) combat.enabled = false;

        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.enabled = false;

        // Optional: Make sprite semi-transparent to show player is dead
        SpriteRenderer sprite = GetComponent<SpriteRenderer>();
        if (sprite != null)
        {
            Color color = sprite.color;
            color.a = 0.5f;
            sprite.color = color;
        }
    }

    /// <summary>
    /// FIXED - Respawn the player at their team's spawn point. Only runs on server.
    /// </summary>
    private void Respawn()
    {
        if (!HasStateAuthority)
        {
            Debug.LogWarning("Respawn called on client - only server can respawn players!");
            return;
        }

        // Reset health
        CurrentHealth = stats.maxHealth;
        IsDead = false;

        // Get spawn position from NetworkedSpawnManager based on team
        PlayerTeamComponent teamComponent = GetComponent<PlayerTeamComponent>();
        if (teamComponent != null && NetworkedSpawnManager.Instance != null)
        {
            // Get the spawn position for this player's team
            Vector3 spawnPosition = NetworkedSpawnManager.Instance.GetSpawnPosition(teamComponent.teamID);

            // Move player to spawn position
            transform.position = spawnPosition;

            // Reset velocity
            Rigidbody2D rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }

            Debug.Log($"✓ Player respawned at team spawn point: {spawnPosition}");
        }
        else
        {
            Debug.LogWarning("⚠️ Could not get spawn position - respawning at current location");
        }

        // Re-enable player on all clients
        RPC_EnablePlayerControls();

        // CAMERA FIX: Re-enable PlayerCameraRespawnHandler after respawn
        PlayerCameraRespawnHandler cameraHandler = GetComponent<PlayerCameraRespawnHandler>();
        if (cameraHandler != null)
        {
            cameraHandler.enabled = true;
            Debug.Log("Re-enabled PlayerCameraRespawnHandler after respawn");
        }

        Debug.Log("Player respawned!");
    }

    /// <summary>
    /// RPC to re-enable player controls on all clients
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_EnablePlayerControls()
    {
        // Re-enable combat/movement
        PlayerCombat combat = GetComponent<PlayerCombat>();
        if (combat != null) combat.enabled = true;

        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.enabled = true;

        // Restore full opacity
        SpriteRenderer sprite = GetComponent<SpriteRenderer>();
        if (sprite != null)
        {
            Color color = sprite.color;
            color.a = 1f;
            sprite.color = color;
        }
    }

    // ===== PUBLIC GETTERS FOR UI AND OTHER SYSTEMS =====

    /// <summary>
    /// Get the current health value
    /// </summary>
    public float GetCurrentHealth()
    {
        return CurrentHealth;
    }

    /// <summary>
    /// Get the maximum health value
    /// </summary>
    public float GetMaxHealth()
    {
        return stats.maxHealth;
    }

    /// <summary>
    /// Check if player is currently dead
    /// </summary>
    public bool IsPlayerDead()
    {
        return IsDead;
    }
}