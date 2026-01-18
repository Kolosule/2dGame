using Fusion;
using UnityEngine;

/// <summary>
/// FIXED VERSION - Drops flag on death and uses correct float health type
/// Handles player health, damage, and death/respawn with Photon Fusion networking
/// INCLUDES SPAWN IMMUNITY to prevent damage on spawn
/// 
/// WHAT CHANGED:
/// - Fixed Respawn() method to convert string team ID to int
/// - Works with both PlayerTeamData (int) and PlayerTeamComponent (string)
/// - Compatible with the fixed NetworkedSpawnManager
/// - FIXED LINE 244: String-to-int conversion for GetSpawnPosition()
/// </summary>
public class PlayerStatsHandler : NetworkBehaviour
{
    [Header("Stats")]
    [SerializeField] private PlayerStats stats;

    [Header("Health Bar UI")]
    [SerializeField] private UnityEngine.UI.Image healthBar;

    [Header("Spawn Protection")]
    [Tooltip("Duration of spawn immunity in seconds")]
    [SerializeField] private float spawnImmunityDuration = 1.5f;

    // Networked properties - FIXED: Use float for health
    [Networked, OnChangedRender(nameof(OnHealthChanged))]
    public float CurrentHealth { get; set; }

    [Networked]
    public bool IsDead { get; set; }

    // Local variables
    private float lastAttackTime = 0f;
    private float spawnTime = 0f; // Track when player spawned for immunity

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            CurrentHealth = stats.maxHealth;
            IsDead = false;
            spawnTime = Time.time; // Record spawn time for immunity
        }

        UpdateHealthBar();
    }

    private void OnHealthChanged()
    {
        UpdateHealthBar();
    }

    private void UpdateHealthBar()
    {
        if (healthBar != null)
        {
            healthBar.fillAmount = CurrentHealth / stats.maxHealth;
        }
    }

    public float GetCurrentHealth()
    {
        return CurrentHealth;
    }

    public float GetMaxHealth()
    {
        return stats.maxHealth;
    }

    /// <summary>
    /// Check if player is currently dead (for other scripts)
    /// </summary>
    public bool IsPlayerDead()
    {
        return IsDead;
    }

    /// <summary>
    /// Set the player ID (called by RespawnManager when spawning)
    /// </summary>
    public void SetPlayerID(int id)
    {
        // This is used by the old RespawnManager system
        // We don't actually need to store this since Fusion handles it
        Debug.Log($"SetPlayerID called with ID: {id} (using Fusion networking instead)");
    }

    /// <summary>
    /// Legacy TakeDamage method (for compatibility with Enemy scripts)
    /// Converts float to match our networked float health system
    /// </summary>
    public void TakeDamage(float damage)
    {
        // Call the RPC version
        RPC_TakeDamage(damage);
    }

    /// <summary>
    /// SERVER: Damages the player. Only runs on server.
    /// INCLUDES SPAWN IMMUNITY CHECK
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TakeDamage(float damage)
    {
        if (!HasStateAuthority) return;
        if (IsDead) return;

        // Check for spawn immunity
        float timeSinceSpawn = Time.time - spawnTime;
        if (timeSinceSpawn < spawnImmunityDuration)
        {
            Debug.Log($"🛡️ Player has spawn immunity! ({(spawnImmunityDuration - timeSinceSpawn):F2}s remaining)");
            return;
        }

        // Prevent rapid consecutive damage
        if (Time.time - lastAttackTime < 0.1f)
        {
            return;
        }

        CurrentHealth -= damage;
        CurrentHealth = Mathf.Max(0, CurrentHealth);

        Debug.Log($"Player took {damage} damage. Current health: {CurrentHealth}/{stats.maxHealth}");

        if (CurrentHealth <= 0)
        {
            CurrentHealth = 0;
            Die();
        }

        lastAttackTime = Time.time;
    }

    /// <summary>
    /// FIXED: Handles player death and drops flag. Only runs on server.
    /// </summary>
    private void Die()
    {
        if (!HasStateAuthority) return;

        IsDead = true;
        Debug.Log("Player died!");

        // Drop flag if carrying one
        DropFlagOnDeath();

        // Disable camera handler
        PlayerCameraRespawnHandler cameraHandler = GetComponent<PlayerCameraRespawnHandler>();
        if (cameraHandler != null)
        {
            cameraHandler.enabled = false;
            Debug.Log("Disabled PlayerCameraRespawnHandler to prevent camera jump issues");
        }

        // Disable player controls on all clients
        RPC_DisablePlayerControls();

        // Start respawn timer
        Invoke(nameof(Respawn), 3f);
    }

    /// <summary>
    /// Drops the flag if the player is carrying one
    /// </summary>
    private void DropFlagOnDeath()
    {
        if (!HasStateAuthority) return;

        // Find all flags in the scene
        Flag[] allFlags = FindObjectsByType<Flag>(FindObjectsSortMode.None);

        foreach (Flag flag in allFlags)
        {
            // Check if this player is carrying this flag
            if (flag.IsCarriedBy(Object.InputAuthority))
            {
                Debug.Log($"Player was carrying {flag.OwningTeam}'s flag - dropping it!");
                flag.DropFlagRpc();
                return; // Player can only carry one flag
            }
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_DisablePlayerControls()
    {
        PlayerCombat combat = GetComponent<PlayerCombat>();
        if (combat != null) combat.enabled = false;

        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.enabled = false;

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
    /// FIXED: Respawn the player at their team's spawn point. Only runs on server.
    /// INCLUDES SPAWN IMMUNITY RESET and proper string→int conversion
    /// </summary>
    private void Respawn()
    {
        if (!HasStateAuthority)
        {
            Debug.LogWarning("Respawn called on client - only server can respawn players!");
            return;
        }

        CurrentHealth = stats.maxHealth;
        IsDead = false;
        spawnTime = Time.time; // Reset spawn immunity timer

        // PRIORITY 1: Try to get spawn position using PlayerTeamData (int-based)
        PlayerTeamData teamData = GetComponent<PlayerTeamData>();
        if (teamData != null && NetworkedSpawnManager.Instance != null)
        {
            int team = teamData.Team;

            if (team != 0) // 0 means no team assigned
            {
                Vector3 spawnPosition = NetworkedSpawnManager.Instance.GetSpawnPosition(team);
                transform.position = spawnPosition;

                Debug.Log($"✓ Player respawned at team {team} spawn point: {spawnPosition}");

                // Reset physics
                Rigidbody2D rb = GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector2.zero;
                    rb.angularVelocity = 0f;
                }
            }
            else
            {
                Debug.LogWarning("⚠️ PlayerTeamData exists but team is 0 (not assigned yet)");
            }
        }
        else
        {
            // PRIORITY 2: Fallback to old method using PlayerTeamComponent (string-based)
            PlayerTeamComponent teamComponent = GetComponent<PlayerTeamComponent>();
            if (teamComponent != null && NetworkedSpawnManager.Instance != null)
            {
                string teamId = teamComponent.teamID;

                // CRITICAL FIX (LINE 244): Convert string to int
                // "Team1" → 1, "Team2" → 2
                int teamNumber = ConvertTeamIdToNumber(teamId);

                if (teamNumber != 0)
                {
                    // Now pass the int to GetSpawnPosition
                    Vector3 spawnPosition = NetworkedSpawnManager.Instance.GetSpawnPosition(teamNumber);
                    transform.position = spawnPosition;

                    // Reset physics
                    Rigidbody2D rb = GetComponent<Rigidbody2D>();
                    if (rb != null)
                    {
                        rb.linearVelocity = Vector2.zero;
                        rb.angularVelocity = 0f;
                    }

                    Debug.Log($"✓ Player respawned at team {teamNumber} spawn point: {spawnPosition}");
                }
            }
            else
            {
                Debug.LogWarning("⚠️ Could not get spawn position - respawning at current location");
            }
        }

        // Re-enable player controls on all clients
        RPC_EnablePlayerControls();

        // Re-enable camera handler
        PlayerCameraRespawnHandler cameraHandler = GetComponent<PlayerCameraRespawnHandler>();
        if (cameraHandler != null)
        {
            cameraHandler.enabled = true;
            Debug.Log("Re-enabled PlayerCameraRespawnHandler after respawn");
        }

        Debug.Log("Player respawned!");
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_EnablePlayerControls()
    {
        PlayerCombat combat = GetComponent<PlayerCombat>();
        if (combat != null) combat.enabled = true;

        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.enabled = true;

        SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
        if (sprite != null)
        {
            Color color = sprite.color;
            color.a = 1f;
            sprite.color = color;
        }
    }

    /// <summary>
    /// HELPER METHOD: Converts string team ID to int team number
    /// "Team1" → 1, "Team2" → 2
    /// </summary>
    private int ConvertTeamIdToNumber(string teamId)
    {
        if (teamId == "Team1") return 1;
        if (teamId == "Team2") return 2;

        Debug.LogWarning($"⚠️ Unknown team ID: {teamId}. Defaulting to Team 1.");
        return 1; // Default fallback
    }
}