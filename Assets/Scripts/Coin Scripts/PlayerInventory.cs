using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// Attached to player GameObjects. Tracks coins the player is carrying.
/// PHOTON FUSION VERSION - Compatible with network team assignment
/// </summary>
public class PlayerInventory : NetworkBehaviour
{
    [Header("Team Assignment")]
    [Tooltip("Leave empty if using NetworkPlayerWrapper - team will be auto-assigned")]
    [SerializeField] private string playerTeam = "";

    [Header("Inventory Settings")]
    [Tooltip("Maximum number of coins player can carry at once (0 = unlimited)")]
    [SerializeField] private int maxCoins = 0; // 0 means unlimited

    // List to store the coins the player is currently carrying
    private List<CoinData> heldCoins = new List<CoinData>();

    // Cache for team component
    private PlayerTeamComponent teamComponent;
    private bool teamComponentChecked = false;

    /// <summary>
    /// Public property to get the player's team
    /// Checks multiple sources: manual assignment, PlayerTeamComponent, or NetworkPlayerWrapper
    /// </summary>
    public string PlayerTeam
    {
        get
        {
            // If manually assigned, use that
            if (!string.IsNullOrEmpty(playerTeam))
            {
                return playerTeam;
            }

            // Try to get from PlayerTeamComponent (works with both networked and local)
            if (!teamComponentChecked)
            {
                teamComponent = GetComponent<PlayerTeamComponent>();
                teamComponentChecked = true;
            }

            if (teamComponent != null)
            {
                // PlayerTeamComponent uses team names like "Team1" or "Team2"
                return teamComponent.teamID;
            }

            // Fallback: return empty string
            return "";
        }
    }

    /// <summary>
    /// Public property to get current coin count
    /// </summary>
    public int CoinCount => heldCoins.Count;

    /// <summary>
    /// Attempts to add a coin to the player's inventory
    /// </summary>
    /// <param name="coin">The coin being picked up</param>
    /// <returns>True if coin was added, false if inventory is full</returns>
    public bool AddCoin(CoinPickup coin)
    {
        // Check if player has reached max capacity
        if (maxCoins > 0 && heldCoins.Count >= maxCoins)
        {
            Debug.Log($"{gameObject.name} inventory is full! ({maxCoins} coins max)");
            return false;
        }

        // Add the coin's data to inventory
        heldCoins.Add(coin.CoinDataProperty);

        Debug.Log($"{gameObject.name} picked up a {coin.CoinDataProperty.coinTeam} coin. Total coins: {heldCoins.Count}");

        // Update UI if it exists
        UpdateUI();

        return true;
    }

    /// <summary>
    /// Deposits all held coins and returns the total point value for this player's team
    /// </summary>
    /// <returns>Total points to add to team score</returns>
    public int DepositCoins()
    {
        int totalPoints = 0;
        string team = PlayerTeam;

        // Calculate points for each coin based on player's team
        foreach (CoinData coin in heldCoins)
        {
            totalPoints += coin.GetValueForTeam(team);
        }

        Debug.Log($"{gameObject.name} deposited {heldCoins.Count} coins for {totalPoints} points!");

        // Clear the inventory
        heldCoins.Clear();

        // Update UI
        UpdateUI();

        return totalPoints;
    }

    /// <summary>
    /// Returns the current total value of held coins (for display purposes)
    /// </summary>
    public int GetCurrentCoinValue()
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
    /// Updates the UI display (if UIManager exists)
    /// </summary>
    private void UpdateUI()
    {
        UIManager uiManager = FindObjectOfType<UIManager>();
        if (uiManager != null)
        {
            uiManager.UpdatePlayerCoinDisplay(this);
        }
    }

    private void Start()
    {
        // Wait a frame to let network team assignment happen
        StartCoroutine(ValidateTeamAssignment());
    }

    /// <summary>
    /// Validates team assignment after a short delay (allows network sync)
    /// </summary>
    private System.Collections.IEnumerator ValidateTeamAssignment()
    {
        // Wait for network/team components to initialize
        yield return new WaitForSeconds(0.2f);

        string team = PlayerTeam;

        // Validate team assignment
        if (string.IsNullOrEmpty(team))
        {
            Debug.LogWarning($"PlayerInventory on {gameObject.name} has no team assigned! " +
                           "Make sure PlayerTeamComponent is attached and initialized, " +
                           "or manually set the team in Inspector.");
        }
        else
        {
            Debug.Log($"✓ {gameObject.name} team validated: {team}");
        }
    }

    /// <summary>
    /// Manual team override (useful for testing or non-networked games)
    /// </summary>
    public void SetTeam(string team)
    {
        playerTeam = team;
        Debug.Log($"✓ {gameObject.name} team manually set to: {team}");
    }
}