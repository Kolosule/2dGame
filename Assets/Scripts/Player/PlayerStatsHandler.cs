using UnityEngine;
using Fusion;

public class PlayerStatsHandler : NetworkBehaviour
{
    [Header("Stats")]
    [SerializeField] private PlayerStats stats;

    // Networked health
    [Networked] public float CurrentHealth { get; set; }
    [Networked] public bool IsDead { get; set; }

    // Local tracking for damage feedback
    private float lastAttackTime;
    private int playerID = -1; // Track player ID for MultiplayerRespawnManager

    void Start()
    {
        // Initialize health when spawned
        if (HasStateAuthority)
        {
            CurrentHealth = stats.maxHealth;
            IsDead = false;
        }
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
    /// Called when player takes damage. Only runs on server.
    /// </summary>
    public void TakeDamage(float damage)
    {
        // Only the server should modify health
        if (!HasStateAuthority)
        {
            Debug.LogWarning("TakeDamage called on client - only server can modify health!");
            return;
        }

        // Don't take damage if already dead
        if (IsDead) return;

        CurrentHealth -= damage;
        Debug.Log($"Player took {damage} damage. Current health: {CurrentHealth}/{stats.maxHealth}");

        // Check for death
        if (CurrentHealth <= 0)
        {
            CurrentHealth = 0;
            Die();
        }

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

        // UPDATED: Get SpriteRenderer from child object instead of parent
        // Optional: Make sprite semi-transparent to show player is dead
        SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
        if (sprite != null)
        {
            Color color = sprite.color;
            color.a = 0.5f;
            sprite.color = color;
        }
        else
        {
            Debug.LogWarning("PlayerStatsHandler: SpriteRenderer not found in children!");
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

        // UPDATED: Get SpriteRenderer from child object instead of parent
        // Restore full opacity
        SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
        if (sprite != null)
        {
            Color color = sprite.color;
            color.a = 1f;
            sprite.color = color;
        }
        else
        {
            Debug.LogWarning("PlayerStatsHandler: SpriteRenderer not found in children!");
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