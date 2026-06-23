using Fusion;
using UnityEngine;

/// <summary>
/// FIXED VERSION - Drops flag on death and uses correct float health type
/// Handles player health, damage, and death/respawn with Photon Fusion networking
/// INCLUDES SPAWN IMMUNITY to prevent damage on spawn
/// 
/// WHAT CHANGED:
/// - Respawn() resolves the team from the networked PlayerTeamData enum
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

    [Header("Respawn")]
    [Tooltip("Delay in seconds before a dead player respawns")]
    [SerializeField] private float respawnDelay = 3f;

    [Tooltip("Minimum seconds between consecutive hits (rapid-hit guard)")]
    [SerializeField] private float hitCooldown = 0.1f;

    // Networked properties - FIXED: Use float for health
    [Networked, OnChangedRender(nameof(OnHealthChanged))]
    public float CurrentHealth { get; set; }

    [Networked]
    public bool IsDead { get; set; }

    // Simulation-path timers (TickTimer = deterministic, authority-driven).
    [Networked] private TickTimer SpawnImmunityTimer { get; set; }
    [Networked] private TickTimer HitCooldownTimer { get; set; }
    [Networked] private TickTimer RespawnTimer { get; set; }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            CurrentHealth = stats.maxHealth;
            IsDead = false;
            SpawnImmunityTimer = TickTimer.CreateFromSeconds(Runner, spawnImmunityDuration);
        }

        UpdateHealthBar();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        // Respawn once the death timer elapses.
        if (IsDead && RespawnTimer.Expired(Runner))
        {
            RespawnTimer = default;
            Respawn();
        }
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

        // Spawn immunity: ignore damage while the immunity timer is still running.
        if (!SpawnImmunityTimer.ExpiredOrNotRunning(Runner))
        {
            return;
        }

        // Rapid-hit guard: ignore damage while the hit-cooldown timer is still running.
        if (!HitCooldownTimer.ExpiredOrNotRunning(Runner))
        {
            return;
        }

        CurrentHealth -= damage;
        CurrentHealth = Mathf.Max(0, CurrentHealth);


        if (CurrentHealth <= 0)
        {
            CurrentHealth = 0;
            Die();
        }

        HitCooldownTimer = TickTimer.CreateFromSeconds(Runner, hitCooldown);
    }

    /// <summary>
    /// FIXED: Handles player death and drops flag. Only runs on server.
    /// </summary>
    private void Die()
    {
        if (!HasStateAuthority) return;

        IsDead = true;

        // Drop flag if carrying one
        DropFlagOnDeath();

        // Drop any carried coins back into the world
        NetworkedPlayerInventory inventory = GetComponent<NetworkedPlayerInventory>();
        if (inventory != null)
        {
            inventory.OnPlayerDeath(transform.position);
        }

        // Disable camera handler
        PlayerCameraRespawnHandler cameraHandler = GetComponent<PlayerCameraRespawnHandler>();
        if (cameraHandler != null)
        {
            cameraHandler.enabled = false;
        }

        // Disable player controls on all clients
        RPC_DisablePlayerControls();

        // Start respawn timer (simulation-path TickTimer, evaluated in FixedUpdateNetwork)
        RespawnTimer = TickTimer.CreateFromSeconds(Runner, respawnDelay);
    }

    /// <summary>
    /// Drops the flag if the player is carrying one. Uses the CTFGameManager's cached
    /// flag references instead of a scene-wide Find in this death hot path.
    /// </summary>
    private void DropFlagOnDeath()
    {
        if (!HasStateAuthority) return;
        if (CTFGameManager.Instance == null) return;

        // There are only ever two flags; a player can carry at most one.
        if (TryDropFlag(CTFGameManager.Instance.Team1Flag)) return;
        TryDropFlag(CTFGameManager.Instance.Team2Flag);
    }

    private bool TryDropFlag(Flag flag)
    {
        if (flag != null && flag.IsCarriedBy(Object.InputAuthority))
        {
            flag.DropFlagRpc();
            return true;
        }
        return false;
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
        SpawnImmunityTimer = TickTimer.CreateFromSeconds(Runner, spawnImmunityDuration); // Reset spawn immunity

        // Resolve the respawn position from the single networked team source.
        PlayerTeamData teamData = GetComponent<PlayerTeamData>();
        if (teamData != null && teamData.Team != Team.None && NetworkedSpawnManager.Instance != null)
        {
            int teamNumber = TeamUtil.ToNumber(teamData.Team);
            Vector3 spawnPosition = NetworkedSpawnManager.Instance.GetSpawnPosition(teamNumber);
            transform.position = spawnPosition;

            Rigidbody2D rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }

        }
        else
        {
            Debug.LogWarning("⚠️ Could not resolve team spawn position - respawning at current location");
        }

        // Re-enable player controls on all clients
        RPC_EnablePlayerControls();

        // Re-enable camera handler
        PlayerCameraRespawnHandler cameraHandler = GetComponent<PlayerCameraRespawnHandler>();
        if (cameraHandler != null)
        {
            cameraHandler.enabled = true;
        }

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

}