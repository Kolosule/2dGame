using UnityEngine;
using UnityEngine.UI;
using TMPro; // If using TextMeshPro (recommended)

/// <summary>
/// Manages UI display for team scores and player coin count.
/// Attach this to a UI Canvas GameObject.
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("Team Score Display")]
    [Tooltip("Text element for Red team score")]
    [SerializeField] private TextMeshProUGUI redScoreText; // Or Text if not using TextMeshPro
    
    [Tooltip("Text element for Blue team score")]
    [SerializeField] private TextMeshProUGUI blueScoreText;
    
    [Header("Player Coin Display")]
    [Tooltip("Text element for player's coin count")]
    [SerializeField] private TextMeshProUGUI playerCoinText;
    
    [Tooltip("Text element for player's coin value (optional)")]
    [SerializeField] private TextMeshProUGUI playerCoinValueText;
    
    [Header("Buff Indicators (Optional)")]
    [Tooltip("Image/Icon that shows when Red team has damage buff")]
    [SerializeField] private GameObject redDamageBuffIcon;
    
    [Tooltip("Image/Icon that shows when Red team has defense buff")]
    [SerializeField] private GameObject redDefenseBuffIcon;
    
    [Tooltip("Image/Icon that shows when Blue team has damage buff")]
    [SerializeField] private GameObject blueDamageBuffIcon;
    
    [Tooltip("Image/Icon that shows when Blue team has defense buff")]
    [SerializeField] private GameObject blueDefenseBuffIcon;
    
    // Reference to the local player (for coin count display)
    private PlayerInventory localPlayer;
    
    private void Start()
    {
        // Hide buff icons initially
        if (redDamageBuffIcon != null) redDamageBuffIcon.SetActive(false);
        if (redDefenseBuffIcon != null) redDefenseBuffIcon.SetActive(false);
        if (blueDamageBuffIcon != null) blueDamageBuffIcon.SetActive(false);
        if (blueDefenseBuffIcon != null) blueDefenseBuffIcon.SetActive(false);
        
        // Update displays
        UpdateTeamScores();
        UpdatePlayerCoinDisplay(null);
    }
    
    /// <summary>
    /// Updates the team score displays
    /// </summary>
    public void UpdateTeamScores()
    {
        TeamScoreManager scoreManager = FindObjectOfType<TeamScoreManager>();
        if (scoreManager == null) return;
        
        // Update Red team score
        if (redScoreText != null)
        {
            redScoreText.text = $"Red: {scoreManager.RedTeamScore}";
        }
        
        // Update Blue team score
        if (blueScoreText != null)
        {
            blueScoreText.text = $"Blue: {scoreManager.BlueTeamScore}";
        }
        
        // Update buff indicators
        UpdateBuffIndicators(scoreManager);
    }
    
    /// <summary>
    /// Updates buff indicator icons based on team progress
    /// </summary>
    private void UpdateBuffIndicators(TeamScoreManager scoreManager)
    {
        if (redDamageBuffIcon != null)
        {
            redDamageBuffIcon.SetActive(scoreManager.RedTeamDamageBuff);
        }
        
        if (redDefenseBuffIcon != null)
        {
            redDefenseBuffIcon.SetActive(scoreManager.RedTeamDefenseBuff);
        }
        
        if (blueDamageBuffIcon != null)
        {
            blueDamageBuffIcon.SetActive(scoreManager.BlueTeamDamageBuff);
        }
        
        if (blueDefenseBuffIcon != null)
        {
            blueDefenseBuffIcon.SetActive(scoreManager.BlueTeamDefenseBuff);
        }
    }
    
    /// <summary>
    /// Updates the player's coin count display
    /// </summary>
    /// <param name="player">The player whose coins to display</param>
    public void UpdatePlayerCoinDisplay(PlayerInventory player)
    {
        // Cache the player reference
        if (player != null)
        {
            localPlayer = player;
        }
        
        // If we don't have a player reference, try to find one
        if (localPlayer == null)
        {
            localPlayer = FindObjectOfType<PlayerInventory>();
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
                int value = localPlayer.GetCurrentCoinValue();
                playerCoinValueText.text = $"Value: {value}";
            }
            else
            {
                playerCoinValueText.text = "Value: 0";
            }
        }
    }
    
    /// <summary>
    /// Optional: Set which player the UI should track (for multiplayer)
    /// </summary>
    public void SetTrackedPlayer(PlayerInventory player)
    {
        localPlayer = player;
        UpdatePlayerCoinDisplay(player);
    }
}

// ===== IF NOT USING TEXTMESHPRO =====
// Replace all "TextMeshProUGUI" with "Text" in the script above
// and add "using UnityEngine.UI;" at the top
