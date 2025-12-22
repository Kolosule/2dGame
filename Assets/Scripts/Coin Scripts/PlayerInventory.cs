using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// Networked player inventory for Photon Fusion.
/// Tracks coins the player is carrying with network synchronization.
/// </summary>
public class NetworkedPlayerInventory : NetworkBehaviour
{
    [Header("Inventory Settings")]
    [Tooltip("Maximum number of coins player can carry at once (0 = unlimited)")]
    [SerializeField] private int maxCoins = 0; // 0 means unlimited

    [Header("Audio (Optional)")]
    [Tooltip("Sound to play when picking up a coin")]
    [SerializeField] private AudioClip coinPickupSound;

    [Tooltip("Sound to play when depositing coins")]
    [SerializeField] private AudioClip depositSound;

    // Network property to sync coin count across all clients
    [Networked]
    public int CoinCount { get; private set; }

    // Network property to sync total coin value
    [Networked]
    public int TotalCoinValue { get; private set; }

    // Local list to store coin data (not networked, only on server)
    // Client uses CoinCount for display only
    private List<CoinData> heldCoins = new List<CoinData>();

    // Cache for team component
    private PlayerTeamComponent teamComponent;
    private bool teamComponentChecked = false;

    /// <summary>
    /// Public property to get the player's team
    /// </summary>
    public string PlayerTeam
    {
        get
        {
            // Try to get from PlayerTeamComponent
            if (!teamComponentChecked)
            {
                teamComponent = GetComponent<PlayerTeamComponent>();
                teamComponentChecked = true;
            }

            if (teamComponent != null)
            {
                return teamComponent.teamID;
            }

            // Fallback: try PlayerTeamData (networked version)
            PlayerTeamData teamData = GetComponent<PlayerTeamData>();
            if (teamData != null)
            {
                // Convert team number to team name
                return teamData.Team == 1 ? "Team1" : "Team2";
            }

            return "";
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
            Debug.Log($"{gameObject.name} inventory is full! ({maxCoins} coins max)");
            return false;
        }

        // Add the coin's data to inventory
        heldCoins.Add(coinData);

        // Update networked properties
        CoinCount = heldCoins.Count;
        TotalCoinValue = CalculateTotalValue();

        Debug.Log($"[SERVER] {gameObject.name} picked up a {coinData.coinTeam} coin. Total: {CoinCount}");

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
        if (coinPickupSound != null && HasInputAuthority)
        {
            AudioSource.PlayClipAtPoint(coinPickupSound, transform.position);
        }
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

        Debug.Log($"[SERVER] {gameObject.name} deposited {heldCoins.Count} coins for {totalPoints} points!");

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
        if (depositSound != null && HasInputAuthority)
        {
            AudioSource.PlayClipAtPoint(depositSound, transform.position);
        }
    }

    /// <summary>
    /// Calculates the total point value of all held coins for this player's team
    /// </summary>
    private int CalculateTotalValue()
    {
        int totalValue = 0;
        string team = PlayerTeam;

        foreach (CoinData coin in heldCoins)
        {
            totalValue += coin.GetValueForTeam(team);
        }

        return totalValue;
    }

    /// <summary>
    /// Called when player dies - drops all coins
    /// </summary>
    public void OnPlayerDeath(Vector3 deathPosition)
    {
        // Only server handles dropping coins
        if (!HasStateAuthority) return;

        if (heldCoins.Count == 0) return;

        Debug.Log($"[SERVER] {gameObject.name} died and dropped {heldCoins.Count} coins!");

        // TODO: If you want coins to drop on death, spawn them here
        // For now, we'll just clear the inventory

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

        string team = PlayerTeam;

        if (string.IsNullOrEmpty(team))
        {
            Debug.LogWarning($"NetworkedPlayerInventory on {gameObject.name} has no team assigned!");
        }
        else
        {
            Debug.Log($"[SERVER] ✓ {gameObject.name} inventory initialized for team: {team}");
        }
    }
}