using UnityEngine;
using Fusion;

/// <summary>
/// Handles player health, damage, death, and respawning for Photon Fusion.
/// This script manages player stats and coordinates with NetworkedSpawnManager for respawning.
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
    /// Attack an enemy. Should be called on server.
    /// </summary>
    public void Attack(Enemy enemy)
    {
        // Only server processes attacks
        if (!HasStateAuthority) return;

        if (Time.time - lastAttackTime < stats.attackCooldown) return;

        if (enemy != null)
        {
            // Apply offensive territorial modifier
            float attackDamage = stats.attackDamage;
            PlayerTeamComponent teamComponent = GetComponent<PlayerTeamComponent>();
            if (teamComponent != null)
            {
                float damageModifier = teamComponent.GetDamageDealtModifier();
                attackDamage = attackDamage * damageModifier;
                Debug.Log($"Player attack modified by territory: {damageModifier:F2}x");
            }

            // Apply damage with knockback
            Vector2 knockbackForce = new Vector2(transform.localScale.x * stats.attackForce, 2f);
            Vector2 hitPoint = enemy.transform.position;

            enemy.TakeDamage((int)attackDamage, knockbackForce, hitPoint);
            Debug.Log($"Player attacked {enemy.name} for {attackDamage} damage.");
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

        // Drop flag if carrying one (if you have CTF mode)
        FlagCarrierMarker carrierMarker = GetComponent<FlagCarrierMarker>();
        if (carrierMarker != null && carrierMarker.IsCarryingFlag())
        {
            // Find which flag this player is carrying
            Flag[] flags = FindObjectsByType<Flag>(FindObjectsSortMode.None);
            foreach (Flag flag in flags)
            {
                // You'll need to adapt this based on your Flag implementation
                // The flag system will need to be converted to Fusion too
                Debug.Log("Flag drop logic needs Fusion conversion");
            }
        }

        // Disable combat/movement on all clients via RPC
        RPC_DisablePlayerControls();

        // Trigger respawn after 2 seconds (only on server)
        if (HasStateAuthority)
        {
            StartCoroutine(RespawnAfterDelay(2f));
        }
    }

    /// <summary>
    /// RPC to disable player controls on all clients
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_DisablePlayerControls()
    {
        // Disable combat/movement
        PlayerCombat combat = GetComponent<PlayerCombat>();
        if (combat != null) combat.enabled = false;

        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.enabled = false;

        Animator anim = GetComponent<Animator>();
        if (anim != null)
        {
            anim.SetTrigger("Die");
        }

        // Make player semi-transparent
        SpriteRenderer sprite = GetComponent<SpriteRenderer>();
        if (sprite != null)
        {
            Color color = sprite.color;
            color.a = 0.5f;
            sprite.color = color;
        }
    }

    /// <summary>
    /// Coroutine to delay respawn
    /// </summary>
    private System.Collections.IEnumerator RespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Respawn();
    }

    /// <summary>
    /// Respawn the player at their team's spawn point
    /// </summary>
    private void Respawn()
    {
        if (!HasStateAuthority)
        {
            Debug.LogWarning("Respawn called on client - only server can respawn!");
            return;
        }

        Debug.Log("=== RESPAWN DEBUG START ===");
        Debug.Log("Respawning player...");

        // Reset health and death state
        IsDead = false;
        CurrentHealth = stats.maxHealth;

        // Re-enable scripts on all clients via RPC
        RPC_EnablePlayerControls();

        // OPTION 1: Use MultiplayerRespawnManager if available
        MultiplayerRespawnManager respawnManager = FindFirstObjectByType<MultiplayerRespawnManager>();
        Debug.Log($"MultiplayerRespawnManager found: {respawnManager != null}, PlayerID: {playerID}");

        if (respawnManager != null && playerID >= 0)
        {
            respawnManager.RespawnPlayer(playerID);
            Debug.Log($"Using MultiplayerRespawnManager to respawn player {playerID}");
            Debug.Log("=== RESPAWN DEBUG END ===");
            return;
        }

        // OPTION 2: NETWORK MODE - Use NetworkedSpawnManager if available
        Debug.Log($"NetworkedSpawnManager.Instance: {NetworkedSpawnManager.Instance != null}");

        if (NetworkedSpawnManager.Instance != null)
        {
            // Get team from PlayerTeamComponent (works with both networking systems)
            PlayerTeamComponent teamComponent = GetComponent<PlayerTeamComponent>();
            if (teamComponent != null && !string.IsNullOrEmpty(teamComponent.teamID))
            {
                // PlayerTeamComponent uses team names like "Team1" or "Team2"
                // This is compatible with the old NetworkedSpawnManager.GetSpawnPosition(string)
                Vector3 spawnPos = NetworkedSpawnManager.Instance.GetSpawnPosition(teamComponent.teamID);

                Debug.Log($"Got spawn position from NetworkedSpawnManager for {teamComponent.teamID}: {spawnPos}");
                transform.position = spawnPos;
                Debug.Log("=== RESPAWN DEBUG END ===");
                return;
            }

            // Fallback: Try PlayerTeamData if it exists
            PlayerTeamData teamData = GetComponent<PlayerTeamData>();
            if (teamData != null)
            {
                // Wait for Fusion to compile TeamID property
                // For now, get team from PlayerTeamComponent which TeamData should have updated
                PlayerTeamComponent teamCompFallback = GetComponent<PlayerTeamComponent>();
                if (teamCompFallback != null && !string.IsNullOrEmpty(teamCompFallback.teamID))
                {
                    Vector3 spawnPos = NetworkedSpawnManager.Instance.GetSpawnPosition(teamCompFallback.teamID);
                    Debug.Log($"Got spawn position via PlayerTeamData->TeamComponent: {spawnPos}");
                    transform.position = spawnPos;
                    Debug.Log("=== RESPAWN DEBUG END ===");
                    return;
                }
            }
        }

        // OPTION 3: STANDALONE MODE - Try to respawn at team base if available
        Debug.Log($"TeamManager.Instance: {TeamManager.Instance != null}");

        PlayerTeamComponent teamComp = GetComponent<PlayerTeamComponent>();
        Debug.Log($"PlayerTeamComponent (standalone check): {teamComp != null}");

        if (teamComp != null && TeamManager.Instance != null)
        {
            Debug.Log($"Getting team data for: {teamComp.teamID}");
            TeamData teamDataStandalone = TeamManager.Instance.GetTeamData(teamComp.teamID);
            Debug.Log($"TeamData found: {teamDataStandalone != null}");

            if (teamDataStandalone != null)
            {
                Debug.Log($"Team base position: {teamDataStandalone.basePosition}");
                transform.position = teamDataStandalone.basePosition;
                Debug.Log($"Respawned at team base: {teamDataStandalone.basePosition}");
                Debug.Log("=== RESPAWN DEBUG END ===");
                return;
            }
            else
            {
                Debug.LogError($"Could not find TeamData for teamID: '{teamComp.teamID}'");
            }
        }

        // Fallback: respawn at current position
        Debug.LogWarning("All respawn methods failed! Using fallback (current position)");
        Debug.Log($"Current position: {transform.position}");
        Debug.Log("=== RESPAWN DEBUG END ===");
    }

    /// <summary>
    /// RPC to re-enable player controls on all clients
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_EnablePlayerControls()
    {
        // Re-enable scripts
        PlayerCombat combat = GetComponent<PlayerCombat>();
        if (combat != null) combat.enabled = true;

        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.enabled = true;

        // Reset transparency
        SpriteRenderer sprite = GetComponent<SpriteRenderer>();
        if (sprite != null)
        {
            Color color = sprite.color;
            color.a = 1f;
            sprite.color = color;
        }

        // Reset animator if needed
        Animator anim = GetComponent<Animator>();
        if (anim != null)
        {
            anim.ResetTrigger("Die");
        }
    }

    // Public getters for UI
    public float GetCurrentHealth()
    {
        return CurrentHealth;
    }

    public float GetMaxHealth()
    {
        return stats.maxHealth;
    }

    public bool IsPlayerDead()
    {
        return IsDead;
    }
}