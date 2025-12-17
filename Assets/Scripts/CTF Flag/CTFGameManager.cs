using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Manages Capture the Flag game mode
/// Tracks both flags and checks win conditions
/// Attach this to a GameObject in your Gameplay scene
/// </summary>
public class CTFGameManager : NetworkBehaviour
{
    public static CTFGameManager Instance { get; private set; }

    [Header("Flag References")]
    [Tooltip("Reference to Team1/Blue flag")]
    [SerializeField] private Flag team1Flag;
    
    [Tooltip("Reference to Team2/Red flag")]
    [SerializeField] private Flag team2Flag;

    [Header("UI References")]
    [Tooltip("Text element for notifications")]
    [SerializeField] private TextMeshProUGUI notificationText;
    
    [Tooltip("Panel for game over screen")]
    [SerializeField] private GameObject gameOverPanel;
    
    [Tooltip("Text for winner announcement")]
    [SerializeField] private TextMeshProUGUI winnerText;
    
    [Tooltip("Text for Team1/Blue flag status")]
    [SerializeField] private TextMeshProUGUI team1FlagStatusText;
    
    [Tooltip("Text for Team2/Red flag status")]
    [SerializeField] private TextMeshProUGUI team2FlagStatusText;

    [Header("Flag Indicators")]
    [Tooltip("Transform showing Team1 flag location")]
    [SerializeField] private GameObject team1FlagIndicator;
    
    [Tooltip("Transform showing Team2 flag location")]
    [SerializeField] private GameObject team2FlagIndicator;

    [Header("Settings")]
    [Tooltip("Time in seconds to show notifications")]
    [SerializeField] private float notificationDuration = 3f;

    // Network variable to track if game is over
    private NetworkVariable<bool> gameIsOver = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Track flags at each base
    private NetworkVariable<bool> team1HasBothFlags = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> team2HasBothFlags = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Hide game over panel initially
        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        // Subscribe to game state changes
        gameIsOver.OnValueChanged += OnGameOverChanged;

        // Find flags if not assigned
        if (team1Flag == null || team2Flag == null)
        {
            FindFlags();
        }
    }

    public override void OnNetworkDespawn()
    {
        gameIsOver.OnValueChanged -= OnGameOverChanged;
    }

    private void Update()
    {
        // Update flag status UI
        UpdateFlagStatusUI();

        // Update flag indicators to point to flag locations
        UpdateFlagIndicators();
    }

    /// <summary>
    /// Find flag objects in the scene
    /// </summary>
    private void FindFlags()
    {
        Flag[] flags = FindObjectsOfType<Flag>();
        foreach (Flag flag in flags)
        {
            if (flag.OwningTeam == "Team1" || flag.OwningTeam == "Blue")
                team1Flag = flag;
            else if (flag.OwningTeam == "Team2" || flag.OwningTeam == "Red")
                team2Flag = flag;
        }

        if (team1Flag != null)
            Debug.Log($"✓ Found Team1 flag: {team1Flag.name}");
        else
            Debug.LogError("⚠️ Team1 flag not found!");

        if (team2Flag != null)
            Debug.Log($"✓ Found Team2 flag: {team2Flag.name}");
        else
            Debug.LogError("⚠️ Team2 flag not found!");
    }

    /// <summary>
    /// SERVER: Called when a flag is captured (brought to enemy base)
    /// </summary>
    public void OnFlagCaptured(string flagOwner, string capturingTeam)
    {
        if (!IsServer) return;

        Debug.Log($"Flag captured! {flagOwner} flag captured by {capturingTeam}");

        // Check if capturing team now has both flags in their base
        CheckWinCondition();
    }

    /// <summary>
    /// SERVER: Check if either team has both flags and should win
    /// </summary>
    private void CheckWinCondition()
    {
        if (!IsServer) return;
        if (gameIsOver.Value) return;

        // Check if both flags are at Team1's base
        bool team1HasOwnFlag = team1Flag != null && team1Flag.State == Flag.FlagState.AtHome;
        bool team1HasEnemyFlag = team2Flag != null && 
                                  team2Flag.State == Flag.FlagState.AtHome && 
                                  IsTeam1FlagAtTeam1Base();

        // Check if both flags are at Team2's base
        bool team2HasOwnFlag = team2Flag != null && team2Flag.State == Flag.FlagState.AtHome;
        bool team2HasEnemyFlag = team1Flag != null && 
                                  team1Flag.State == Flag.FlagState.AtHome && 
                                  IsTeam2FlagAtTeam2Base();

        // Win condition: team has both their own flag AND enemy flag in their base
        if (team1HasOwnFlag && team2Flag != null && IsAtTeamBase(team2Flag.transform.position, "Team1"))
        {
            EndGame("Team1");
        }
        else if (team2HasOwnFlag && team1Flag != null && IsAtTeamBase(team1Flag.transform.position, "Team2"))
        {
            EndGame("Team2");
        }
    }

    /// <summary>
    /// Check if a position is at a specific team's base
    /// </summary>
    private bool IsAtTeamBase(Vector3 position, string teamId)
    {
        if (TeamManager.Instance == null) return false;

        TeamData teamData = TeamManager.Instance.GetTeamData(teamId);
        if (teamData == null) return false;

        float distanceToBase = Vector2.Distance(position, teamData.basePosition);
        return distanceToBase < 3f;
    }

    private bool IsTeam1FlagAtTeam1Base()
    {
        return IsAtTeamBase(team1Flag.transform.position, "Team1");
    }

    private bool IsTeam2FlagAtTeam2Base()
    {
        return IsAtTeamBase(team2Flag.transform.position, "Team2");
    }

    /// <summary>
    /// SERVER: End the game with a winner
    /// </summary>
    private void EndGame(string winningTeam)
    {
        if (!IsServer) return;

        gameIsOver.Value = true;

        string teamDisplayName = winningTeam == "Team1" ? "Blue" : "Red";
        ShowGameOverClientRpc(teamDisplayName);

        Debug.Log($"✓ GAME OVER! {teamDisplayName} team wins!");
    }

    /// <summary>
    /// CLIENT RPC: Show game over screen on all clients
    /// </summary>
    [ClientRpc]
    private void ShowGameOverClientRpc(string winningTeamName)
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }

        if (winnerText != null)
        {
            winnerText.text = $"{winningTeamName} Team WINS!";
        }

        // Disable player controls
        PlayerController[] players = FindObjectsOfType<PlayerController>();
        foreach (PlayerController player in players)
        {
            player.enabled = false;
        }

        Debug.Log($"Game Over displayed: {winningTeamName} team wins!");
    }

    /// <summary>
    /// CLIENT RPC: Show notification to all players
    /// </summary>
    [ClientRpc]
    public void ShowNotificationClientRpc(string message)
    {
        if (notificationText != null)
        {
            notificationText.text = message;
            notificationText.gameObject.SetActive(true);

            // Cancel previous hide coroutine if running
            StopAllCoroutines();
            StartCoroutine(HideNotificationAfterDelay());
        }

        Debug.Log($"Notification: {message}");
    }

    /// <summary>
    /// Hide notification after delay
    /// </summary>
    private System.Collections.IEnumerator HideNotificationAfterDelay()
    {
        yield return new WaitForSeconds(notificationDuration);

        if (notificationText != null)
        {
            notificationText.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Update flag status text on UI
    /// </summary>
    private void UpdateFlagStatusUI()
    {
        if (team1FlagStatusText != null && team1Flag != null)
        {
            string status = GetFlagStatusString(team1Flag);
            team1FlagStatusText.text = $"Blue Flag: {status}";
        }

        if (team2FlagStatusText != null && team2Flag != null)
        {
            string status = GetFlagStatusString(team2Flag);
            team2FlagStatusText.text = $"Red Flag: {status}";
        }
    }

    /// <summary>
    /// Get human-readable flag status
    /// </summary>
    private string GetFlagStatusString(Flag flag)
    {
        switch (flag.State)
        {
            case Flag.FlagState.AtHome:
                return "AT BASE";
            case Flag.FlagState.Carried:
                return "CARRIED";
            case Flag.FlagState.Dropped:
                return "DROPPED";
            default:
                return "UNKNOWN";
        }
    }

    /// <summary>
    /// Update flag indicator positions to point at flags
    /// </summary>
    private void UpdateFlagIndicators()
    {
        // Find local player
        PlayerController localPlayer = FindLocalPlayer();
        if (localPlayer == null) return;

        // Update Team1 flag indicator
        if (team1FlagIndicator != null && team1Flag != null)
        {
            UpdateIndicator(team1FlagIndicator, team1Flag.transform.position, localPlayer.transform.position);
        }

        // Update Team2 flag indicator
        if (team2FlagIndicator != null && team2Flag != null)
        {
            UpdateIndicator(team2FlagIndicator, team2Flag.transform.position, localPlayer.transform.position);
        }
    }

    /// <summary>
    /// Update a single indicator to point at target
    /// </summary>
    private void UpdateIndicator(GameObject indicator, Vector3 targetPos, Vector3 playerPos)
    {
        // Calculate direction to flag
        Vector2 direction = (targetPos - playerPos).normalized;
        
        // Rotate indicator to point at flag
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        indicator.transform.rotation = Quaternion.Euler(0, 0, angle);

        // Optional: Hide indicator if flag is very close
        float distance = Vector2.Distance(playerPos, targetPos);
        indicator.SetActive(distance > 2f);
    }

    /// <summary>
    /// Find the local player controlled by this client
    /// </summary>
    private PlayerController FindLocalPlayer()
    {
        PlayerController[] players = FindObjectsOfType<PlayerController>();
        foreach (PlayerController player in players)
        {
            NetworkObject netObj = player.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsOwner)
            {
                return player;
            }
        }
        return null;
    }

    /// <summary>
    /// Called when game over state changes
    /// </summary>
    private void OnGameOverChanged(bool wasOver, bool isOver)
    {
        if (isOver)
        {
            Debug.Log("Game is now over!");
        }
    }

    /// <summary>
    /// SERVER: Restart the game (call this from a restart button)
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RestartGameServerRpc()
    {
        if (!IsServer) return;

        // Reset game state
        gameIsOver.Value = false;

        // Return both flags to home
        if (team1Flag != null)
            team1Flag.ReturnFlag();
        if (team2Flag != null)
            team2Flag.ReturnFlag();

        // Hide game over panel on all clients
        HideGameOverClientRpc();

        Debug.Log("✓ Game restarted");
    }

    /// <summary>
    /// CLIENT RPC: Hide game over screen
    /// </summary>
    [ClientRpc]
    private void HideGameOverClientRpc()
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }

        // Re-enable player controls
        PlayerController[] players = FindObjectsOfType<PlayerController>();
        foreach (PlayerController player in players)
        {
            NetworkObject netObj = player.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsOwner)
            {
                player.enabled = true;
            }
        }
    }
}
