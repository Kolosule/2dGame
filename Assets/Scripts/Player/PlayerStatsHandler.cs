using System;
using Fusion;
using Fusion.Addons.Physics;
using UnityEngine;
using Game.Audio.Core;
using Game.Combat.Core;

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

    /// <summary>Fires whenever CurrentHealth changes (Fusion render callback). HUD subscribes.</summary>
    public event Action HealthChanged;

    // Networked properties - FIXED: Use float for health
    [Networked, OnChangedRender(nameof(OnHealthChanged))]
    public float CurrentHealth { get; set; }

    [Networked]
    public bool IsDead { get; set; }

    // The spawn point chosen for the NEXT respawn, decided once at death so the camera transition
    // and the actual respawn teleport agree on a single location (they used to pick independently).
    [Networked]
    public Vector3 RespawnPosition { get; private set; }

    // Simulation-path timers (TickTimer = deterministic, authority-driven).
    [Networked] private TickTimer SpawnImmunityTimer { get; set; }
    [Networked] private TickTimer RespawnTimer { get; set; }

    // Per-attacker rapid-hit guard (server-only; keyed by the attacking NetworkObject's id).
    // Replaces the old single global HitCooldownTimer, which ate a second attacker's hit
    // landing inside the window. Never networked; cleared on respawn.
    private readonly HitCooldownLedger hitLedger = new HitCooldownLedger();

    // Most recent attacker on this life, for kill attribution in Die(). Non-networked and safe:
    // it is written and consumed within the same synchronous server call (ServerApplyDamage ->
    // Die()), never read across a tick boundary, so it needs no resimulation safety.
    private NetworkId lastAttackerId;

    // Cached for the respawn teleport: NetworkRigidbody2D.Teleport bumps the teleport key so
    // proxies snap to the respawn point instead of interpolating across the map.
    private NetworkRigidbody2D netRb;

    // Render-side only: previous health, so OnHealthChanged can tell a hit from a heal or a
    // respawn reset. Never read by simulation, never networked.
    private float lastRenderedHealth = float.MaxValue;

    public override void Spawned()
    {
        netRb = GetComponent<NetworkRigidbody2D>();

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
        if (DedicatedServerPresentation.IsHeadless) return;

        // Victim-only, flat: "I am being hit" must be instantly distinguishable from
        // "I am landing hits" (HitConfirm), which is why they are separate cues on separate paths.
        // Guarded on a health DECREASE so healing and the respawn reset never trigger it.
        if (HasInputAuthority && CurrentHealth < lastRenderedHealth)
            Audio.Play2D(AudioCueId.TookDamage);
        lastRenderedHealth = CurrentHealth;

        UpdateHealthBar();
        HealthChanged?.Invoke();
    }

    private void UpdateHealthBar()
    {
        if (DedicatedServerPresentation.IsHeadless) return;

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
    /// SERVER: apply damage attributed to a specific attacker (the attacking NetworkObject's
    /// id: the melee player, the shooter, or the enemy). Spawn immunity is global; the
    /// rapid-hit guard is PER ATTACKER so two players hitting in the same 0.1s both land.
    /// </summary>
    public void ServerApplyDamage(float damage, NetworkId attackerId)
    {
        if (!HasStateAuthority) return;
        if (IsDead) return;

        // Spawn immunity: ignore damage while the immunity timer is still running.
        if (!SpawnImmunityTimer.ExpiredOrNotRunning(Runner))
        {
            return;
        }

        // Rapid-hit guard, per attacker.
        int cooldownTicks = Mathf.Max(1, Mathf.RoundToInt(hitCooldown * Runner.TickRate));
        if (!hitLedger.TryRegisterHit((ulong)attackerId.Raw, Runner.Tick, cooldownTicks))
        {
            return;
        }

        lastAttackerId = attackerId;

        CurrentHealth -= damage;
        CurrentHealth = Mathf.Max(0, CurrentHealth);

        if (CurrentHealth <= 0)
        {
            CurrentHealth = 0;
            Die();
        }
    }

    /// <summary>
    /// FIXED: Handles player death and drops flag. Only runs on server.
    /// </summary>
    private void Die()
    {
        if (!HasStateAuthority) return;

        IsDead = true;

        ReportDeathToStats();

        // Decide where we will respawn NOW, so the camera transition and the respawn teleport
        // target the exact same point (see RespawnPosition).
        RespawnPosition = ResolveSpawnPosition();

        // Drop flag if carrying one
        DropFlagOnDeath();

        // Drop any carried coins back into the world
        NetworkedPlayerInventory inventory = GetComponent<NetworkedPlayerInventory>();
        if (inventory != null)
        {
            inventory.OnPlayerDeath(transform.position);
        }

        // Disable player controls on all clients (control is also gated on IsDead in
        // PlayerController; this RPC additionally handles the dimmed-sprite visual).
        RPC_DisablePlayerControls();

        // Start respawn timer (simulation-path TickTimer, evaluated in FixedUpdateNetwork)
        RespawnTimer = TickTimer.CreateFromSeconds(Runner, respawnDelay);
    }

    /// <summary>
    /// SERVER: mirrors this death into the scoreboard (self death, and the killer's kill if the
    /// last hit resolves to a real player who is not this same player). An attacker NetworkId
    /// that fails to resolve (environmental damage, or any other unattributed source) or that
    /// resolves to a non-player NetworkObject (an AI enemy) credits no kill to anyone --
    /// RecordKill's own IsRealPlayer guard on the manager side handles the AI case.
    /// </summary>
    private void ReportDeathToStats()
    {
        if (MatchStatsManager.Instance == null) return;

        PlayerRef self = Object.InputAuthority;
        MatchStatsManager.Instance.SetDead(self.PlayerId, true);
        MatchStatsManager.Instance.RecordDeath(self);

        if (Runner.TryFindObject(lastAttackerId, out NetworkObject attackerObj) && attackerObj != null)
        {
            PlayerRef attacker = attackerObj.InputAuthority;
            if (attacker != self)
                MatchStatsManager.Instance.RecordKill(attacker);
        }
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
            // Direct call, not an RPC: this path is already server-only (DropFlagOnDeath guards
            // on HasStateAuthority), so an RPC here only round-trips the server to itself.
            flag.DropFlag();
            return true;
        }
        return false;
    }

    // Control freeze itself is gated on the networked IsDead in PlayerController; these RPCs only
    // drive the dimmed-while-dead sprite visual on every client.
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_DisablePlayerControls()
    {
        if (DedicatedServerPresentation.IsHeadless) return;

        if (HasInputAuthority) Audio.Play2D(AudioCueId.PlayerDeath);
        else Audio.PlayAt(AudioCueId.PlayerDeath, transform.position);

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
        hitLedger.Clear(); // fresh life, no stale attacker cooldowns

        if (MatchStatsManager.Instance != null)
            MatchStatsManager.Instance.SetDead(Object.InputAuthority.PlayerId, false);

        // Teleport to the position chosen at death (RespawnPosition), so it matches where the
        // camera already transitioned to. Go through NetworkRigidbody2D.Teleport so proxies snap
        // instead of streaking across the map (a bare transform write interpolates on remotes).
        if (netRb != null)
            netRb.Teleport(RespawnPosition);
        else
            transform.position = RespawnPosition;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        // Re-enable player controls on all clients
        RPC_EnablePlayerControls();
    }

    /// <summary>SERVER: resolve this player's team spawn point, or current position as a fallback.</summary>
    private Vector3 ResolveSpawnPosition()
    {
        PlayerTeamData teamData = GetComponent<PlayerTeamData>();
        if (teamData != null && teamData.Team != Team.None && NetworkedSpawnManager.Instance != null)
        {
            int teamNumber = TeamUtil.ToNumber(teamData.Team);
            return NetworkedSpawnManager.Instance.GetSpawnPosition(teamNumber);
        }

        Debug.LogWarning("⚠️ Could not resolve team spawn position - respawning at current location");
        return transform.position;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_EnablePlayerControls()
    {
        if (DedicatedServerPresentation.IsHeadless) return;

        if (HasInputAuthority) Audio.Play2D(AudioCueId.PlayerRespawn);
        else Audio.PlayAt(AudioCueId.PlayerRespawn, transform.position);

        SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
        if (sprite != null)
        {
            Color color = sprite.color;
            color.a = 1f;
            sprite.color = color;
        }
    }

}