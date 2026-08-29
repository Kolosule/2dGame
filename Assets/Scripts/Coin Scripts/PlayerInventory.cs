using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Game.Audio.Core;

/// <summary>
/// Networked player inventory for Photon Fusion.
/// Tracks coins the player is carrying with network synchronization.
/// </summary>
public class NetworkedPlayerInventory : NetworkBehaviour
{
    [Header("Inventory Settings")]
    [Tooltip("Maximum number of coins player can carry at once (0 = unlimited)")]
    [SerializeField] private int maxCoins = 0; // 0 means unlimited

    [Header("Death Drop Settings")]
    [Tooltip("Radius around the death position to scatter dropped coins")]
    [SerializeField] private float dropScatterRadius = 1f;

    /// <summary>Fires whenever CoinCount / TotalCoinValue change (Fusion render callback). HUD subscribes.</summary>
    public event System.Action CoinsChanged;

    // Network property to sync coin count across all clients
    [Networked, OnChangedRender(nameof(OnCoinsChanged))]
    public int CoinCount { get; private set; }

    // Network property to sync total coin value
    [Networked, OnChangedRender(nameof(OnCoinsChanged))]
    public int TotalCoinValue { get; private set; }

    private void OnCoinsChanged()
    {
        if (DedicatedServerPresentation.IsHeadless) return;
        CoinsChanged?.Invoke();
    }

    // Local list to store coin data (not networked, only on server)
    // Client uses CoinCount for display only
    private List<CoinData> heldCoins = new List<CoinData>();

    // Cache for team component
    private PlayerTeamData teamComponent;
    private bool teamComponentChecked = false;

    /// <summary>
    /// Public property to get the player's team
    /// </summary>
    public Team PlayerTeam
    {
        get
        {
            if (!teamComponentChecked)
            {
                teamComponent = GetComponent<PlayerTeamData>();
                teamComponentChecked = true;
            }

            if (teamComponent != null)
            {
                return teamComponent.Team;
            }

            PlayerTeamData teamData = GetComponent<PlayerTeamData>();
            return teamData != null ? teamData.Team : Team.None;
        }
    }

    /// <summary>
    /// SERVER ONLY: Adds a coin to the player's inventory
    /// Called by NetworkedCoinPickup via RPC
    /// </summary>
    public bool ServerAddCoin(CoinData coinData)
    {
        // This should only be called on the server
        if (!HasStateAuthority)
        {
            Debug.LogError("ServerAddCoin called on client! This should only be called on server.");
            return false;
        }

        // Check if player has reached max capacity
        if (maxCoins > 0 && heldCoins.Count >= maxCoins)
        {
            return false;
        }

        // Add the coin's data to inventory
        heldCoins.Add(coinData);

        // Update networked properties
        CoinCount = heldCoins.Count;
        TotalCoinValue = CalculateTotalValue();


        // Notify clients to play sound/effects
        RPC_OnCoinAdded();

        return true;
    }
    /// <summary>
    /// RPC to notify all clients that a coin was added (for visual/audio feedback)
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnCoinAdded()
    {
        // SELF half of a pickup: flat, on the UI bus, only for the player who collected it.
        // CoinPickup already plays the positional world cue for everyone, including this player.
        if (HasInputAuthority) Audio.PlayUi(AudioCueId.CoinPickupSelf);
    }

    /// <summary>
    /// Deposits all held coins and returns the total point value.
    /// Called by NetworkedHomeBase when player enters their base.
    /// SERVER ONLY.
    /// </summary>
    public int ServerDepositCoins()
    {
        // This should only be called on the server
        if (!HasStateAuthority)
        {
            Debug.LogError("ServerDepositCoins called on client! This should only be called on server.");
            return 0;
        }

        if (heldCoins.Count == 0)
        {
            return 0;
        }

        // Calculate total points
        int totalPoints = CalculateTotalValue();


        // Clear the inventory
        heldCoins.Clear();
        CoinCount = 0;
        TotalCoinValue = 0;

        // Notify clients
        RPC_OnCoinsDeposited();

        return totalPoints;
    }

    /// <summary>
    /// RPC to notify all clients that coins were deposited (for visual/audio feedback)
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnCoinsDeposited()
    {
        // SELF half of a deposit. HomeBase plays the positional world cue for everyone.
        if (HasInputAuthority) Audio.PlayUi(AudioCueId.DepositSelf);
    }

    /// <summary>
    /// Calculates the total point value of all held coins for this player's team
    /// </summary>
    private int CalculateTotalValue()
    {
        int totalValue = 0;
        Team team = PlayerTeam;

        foreach (CoinData coin in heldCoins)
        {
            totalValue += coin.GetValueForTeam(team);
        }

        return totalValue;
    }

    /// <summary>
    /// Called when player dies - drops all carried coins back into the world.
    /// SERVER ONLY: spawns the matching networked coin prefab for each held coin,
    /// scattered around the death position, then clears the inventory.
    /// </summary>
    public void OnPlayerDeath(Vector3 deathPosition)
    {
        // Only server handles dropping coins
        if (!HasStateAuthority) return;

        if (heldCoins.Count == 0) return;

        // Re-spawn each carried coin so other players can pick it up again.
        // Each coin's prefab already carries its own CoinData, so point values are preserved.
        foreach (CoinData coin in heldCoins)
        {
            if (coin == null || coin.coinPrefab == null)
            {
                Debug.LogWarning($"NetworkedPlayerInventory: held coin '{(coin != null ? coin.name : "null")}' has no coinPrefab assigned - cannot drop it.");
                continue;
            }

            Vector2 offset = Random.insideUnitCircle * dropScatterRadius;
            Vector3 spawnPosition = deathPosition + new Vector3(offset.x, offset.y, 0f);
            Runner.Spawn(coin.coinPrefab, spawnPosition, Quaternion.identity);
        }

        heldCoins.Clear();
        CoinCount = 0;
        TotalCoinValue = 0;
    }

    private void Start()
    {
        // Wait a frame to let network team assignment happen
        if (HasStateAuthority)
        {
            StartCoroutine(ValidateTeamAssignment());
        }
    }

    /// <summary>
    /// Validates team assignment after a short delay
    /// </summary>
    private System.Collections.IEnumerator ValidateTeamAssignment()
    {
        yield return new WaitForSeconds(0.2f);

        Team team = PlayerTeam;

        if (team == Team.None)
        {
            Debug.LogWarning($"NetworkedPlayerInventory on {gameObject.name} has no team assigned!");
        }
    }
}