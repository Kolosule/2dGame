using UnityEngine;
using UnityEngine.UI;
using TMPro; // If using TextMeshPro (recommended)
using Fusion;

/// <summary>
/// Manages UI display for team scores and player coin count.
/// UPDATED: Now works with NetworkedPlayerInventory for Photon Fusion.
/// Attach this to a UI Canvas GameObject.
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("Team Score Display")]
    [Tooltip("Text element for Team1/Blue team score")]
    [SerializeField] private TextMeshProUGUI team1ScoreText; // Or Text if not using TextMeshPro

    [Tooltip("Text element for Team2/Red team score")]
    [SerializeField] private TextMeshProUGUI team2ScoreText;

    [Header("Player Coin Display")]
    [Tooltip("Text element for player's coin count")]
    [SerializeField] private TextMeshProUGUI playerCoinText;

    [Tooltip("Text element for player's coin value (optional)")]
    [SerializeField] private TextMeshProUGUI playerCoinValueText;

    [Header("Buff Indicators (Optional)")]
    [Tooltip("Image/Icon that shows when Team1 has damage buff")]
    [SerializeField] private GameObject team1DamageBuffIcon;

    [Tooltip("Image/Icon that shows when Team1 has defense buff")]
    [SerializeField] private GameObject team1DefenseBuffIcon;

    [Tooltip("Image/Icon that shows when Team2 has damage buff")]
    [SerializeField] private GameObject team2DamageBuffIcon;

    [Tooltip("Image/Icon that shows when Team2 has defense buff")]
    [SerializeField] private GameObject team2DefenseBuffIcon;

    [Header("Update Settings")]
    [Tooltip("How often to update the UI (in seconds)")]
    [SerializeField] private float updateInterval = 0.1f;

    // Reference to the local player (for coin count display)
    private NetworkedPlayerInventory localPlayer;
    private float nextUpdateTime;

    private void Start()
    {
        // Hide buff icons initially
        if (team1DamageBuffIcon != null) team1DamageBuffIcon.SetActive(false);
        if (team1DefenseBuffIcon != null) team1DefenseBuffIcon.SetActive(false);
        if (team2DamageBuffIcon != null) team2DamageBuffIcon.SetActive(false);
        if (team2DefenseBuffIcon != null) team2DefenseBuffIcon.SetActive(false);

        // Try to find local player
        FindLocalPlayer();

        // Initial update
        UpdateTeamScores();
        UpdatePlayerCoinDisplay();
    }

    private void Update()
    {
        // Update UI at regular intervals
        if (Time.time >= nextUpdateTime)
        {
            UpdateTeamScores();
            UpdatePlayerCoinDisplay();
            nextUpdateTime = Time.time + updateInterval;
        }
    }

    /// <summary>
    /// Find the local player's inventory component
    /// </summary>
    private void FindLocalPlayer()
    {
        if (localPlayer != null) return;

        // Find all player inventories
        NetworkedPlayerInventory[] allPlayers = FindObjectsByType<NetworkedPlayerInventory>(FindObjectsSortMode.None);

        foreach (NetworkedPlayerInventory player in allPlayers)
        {
            // Check if this is the local player (has input authority)
            if (player.HasInputAuthority)
            {
                localPlayer = player;
                Debug.Log("UIManager: Found local player inventory");
                break;
            }
        }
    }

    /// <summary>
    /// Updates the team score displays
    /// </summary>
    public void UpdateTeamScores()
    {
        TeamScoreManager scoreManager = TeamScoreManager.Instance;
        if (scoreManager == null) return;

        // Update Team1 score
        if (team1ScoreText != null)
        {
            team1ScoreText.text = $"Team1: {scoreManager.Team1Score}";
        }

        // Update Team2 score
        if (team2ScoreText != null)
        {
            team2ScoreText.text = $"Team2: {scoreManager.Team2Score}";
        }

        // Update buff indicators
        UpdateBuffIndicators(scoreManager);
    }

    /// <summary>
    /// Updates buff indicator icons based on team progress
    /// </summary>
    private void UpdateBuffIndicators(TeamScoreManager scoreManager)
    {
        if (team1DamageBuffIcon != null)
        {
            team1DamageBuffIcon.SetActive(scoreManager.Team1DamageBuff);
        }

        if (team1DefenseBuffIcon != null)
        {
            team1DefenseBuffIcon.SetActive(scoreManager.Team1DefenseBuff);
        }

        if (team2DamageBuffIcon != null)
        {
            team2DamageBuffIcon.SetActive(scoreManager.Team2DamageBuff);
        }

        if (team2DefenseBuffIcon != null)
        {
            team2DefenseBuffIcon.SetActive(scoreManager.Team2DefenseBuff);
        }
    }

    /// <summary>
    /// Updates the player's coin count display
    /// </summary>
    public void UpdatePlayerCoinDisplay()
    {
        // Try to find local player if we don't have one
        if (localPlayer == null)
        {
            FindLocalPlayer();
        }

        // Update coin count text
        if (playerCoinText != null)
        {
            if (localPlayer != null)
            {
                playerCoinText.text = $"Coins: {localPlayer.CoinCount}";
            }
            else
            {
                playerCoinText.text = "Coins: 0";
            }
        }

        // Update coin value text (optional)
        if (playerCoinValueText != null)
        {
            if (localPlayer != null)
            {
                playerCoinValueText.text = $"Value: {localPlayer.TotalCoinValue}";
            }
            else
            {
                playerCoinValueText.text = "Value: 0";
            }
        }
    }

    /// <summary>
    /// Manually set which player the UI should track
    /// Useful if you need to override the automatic detection
    /// </summary>
    public void SetTrackedPlayer(NetworkedPlayerInventory player)
    {
        localPlayer = player;
        UpdatePlayerCoinDisplay();
    }

    // ===== LEGACY COMPATIBILITY =====
    // These methods maintain compatibility with old code that might call them

    /// <summary>
    /// Legacy method - kept for compatibility
    /// </summary>
    public void UpdatePlayerCoinDisplay(NetworkedPlayerInventory player)
    {
        if (player != null && player.HasInputAuthority)
        {
            localPlayer = player;
        }
        UpdatePlayerCoinDisplay();
    }
}

// ===== IF NOT USING TEXTMESHPRO =====
// If you're not using TextMeshPro:
// 1. Remove "using TMPro;" at the top
// 2. Replace all "TextMeshProUGUI" with "Text"
// 3. Make sure you have "using UnityEngine.UI;" at the top