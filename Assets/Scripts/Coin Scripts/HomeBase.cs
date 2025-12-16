using UnityEngine;

/// <summary>
/// Attached to home base GameObjects where players deposit coins.
/// NOW COMPATIBLE WITH NETWORK TEAM NAMES (Team1/Team2)
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class HomeBase : MonoBehaviour
{
    [Header("Base Settings")]
    [Tooltip("Which team this base belongs to: 'Team1'/'Blue' or 'Team2'/'Red'")]
    [SerializeField] private string baseTeam;

    [Header("Deposit Settings")]
    [Tooltip("Should deposit happen automatically on enter, or require a button press?")]
    [SerializeField] private bool autoDeposit = true;

    [Tooltip("If not auto-deposit, which key to press (default: E)")]
    [SerializeField] private KeyCode depositKey = KeyCode.E;

    [Header("Audio (Optional)")]
    [Tooltip("Sound to play when coins are deposited")]
    [SerializeField] private AudioClip depositSound;

    // Track which player is currently in the base zone
    private PlayerInventory playerInZone = null;

    private void Start()
    {
        // Ensure the collider is set to trigger mode
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }

        if (string.IsNullOrEmpty(baseTeam))
        {
            Debug.LogError($"HomeBase on {gameObject.name} has no team assigned!");
        }
    }

    /// <summary>
    /// Called when a player enters the base trigger zone
    /// </summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        PlayerInventory player = collision.GetComponent<PlayerInventory>();

        if (player != null)
        {
            // Check if player is on the same team as this base
            if (IsPlayerOnCorrectTeam(player))
            {
                playerInZone = player;

                // If auto-deposit is enabled, deposit immediately
                if (autoDeposit)
                {
                    DepositCoins(player);
                }
                else
                {
                    Debug.Log($"Press {depositKey} to deposit coins!");
                }
            }
            else
            {
                Debug.Log($"{player.gameObject.name} is on the wrong team! This is {baseTeam}'s base.");
            }
        }
    }

    /// <summary>
    /// Called when a player leaves the base trigger zone
    /// </summary>
    private void OnTriggerExit2D(Collider2D collision)
    {
        PlayerInventory player = collision.GetComponent<PlayerInventory>();

        if (player != null && player == playerInZone)
        {
            playerInZone = null;
        }
    }

    /// <summary>
    /// Check for deposit key press if manual deposit is enabled
    /// </summary>
    private void Update()
    {
        if (!autoDeposit && playerInZone != null && Input.GetKeyDown(depositKey))
        {
            DepositCoins(playerInZone);
        }
    }

    /// <summary>
    /// Checks if player is on the correct team for this base
    /// Handles multiple team naming conventions
    /// </summary>
    private bool IsPlayerOnCorrectTeam(PlayerInventory player)
    {
        string playerTeamName = player.PlayerTeam.ToLower().Trim();
        string baseTeamName = baseTeam.ToLower().Trim();

        // Direct match
        if (playerTeamName == baseTeamName)
        {
            return true;
        }

        // Check alternate names
        // Team1 = Blue
        if ((playerTeamName == "team1" || playerTeamName == "blue") &&
            (baseTeamName == "team1" || baseTeamName == "blue"))
        {
            return true;
        }

        // Team2 = Red
        if ((playerTeamName == "team2" || playerTeamName == "red") &&
            (baseTeamName == "team2" || baseTeamName == "red"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Handles the coin deposit process
    /// </summary>
    private void DepositCoins(PlayerInventory player)
    {
        // Only deposit if player has coins
        if (player.CoinCount == 0)
        {
            Debug.Log("No coins to deposit!");
            return;
        }

        // Get points from player's inventory
        int points = player.DepositCoins();

        // Add points to team score through the TeamScoreManager
        TeamScoreManager scoreManager = FindObjectOfType<TeamScoreManager>();
        if (scoreManager != null)
        {
            scoreManager.AddPoints(baseTeam, points);
        }
        else
        {
            Debug.LogError("TeamScoreManager not found in scene!");
        }

        // Play deposit sound if assigned
        if (depositSound != null)
        {
            AudioSource.PlayClipAtPoint(depositSound, transform.position);
        }

        Debug.Log($"{player.gameObject.name} deposited coins at {baseTeam} base for {points} points!");
    }
}