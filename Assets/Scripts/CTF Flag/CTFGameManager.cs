using Fusion;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;

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

    // Networked properties with OnChanged callbacks
    [Networked]
    public bool GameIsOver { get; set; }

    [Networked]
    public bool Team1HasBothFlags { get; set; }

    [Networked]
    public bool Team2HasBothFlags { get; set; }

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

    public override void Spawned()
    {
        base.Spawned();

        // Find flags if not assigned
        if (team1Flag == null || team2Flag == null)
        {
            FindFlags();
        }

        // Initialize UI on all clients
        UpdateFlagStatusUI();
    }

    private void OnDestroy()
    {
        // Clean up singleton
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        // Update flag indicator positions (all clients)
        UpdateFlagIndicators();

        // Update flag status UI
        UpdateFlagStatusUI();
    }

    public override void FixedUpdateNetwork()
    {
        // SERVER: Continuously check win condition
        if (HasStateAuthority && !GameIsOver)
        {
            CheckWinCondition();
        }

        // Check for game over state change on all clients
        if (GameIsOver && gameOverPanel != null && !gameOverPanel.activeSelf)
        {
            gameOverPanel.SetActive(true);
        }
    }

    /// <summary>
    /// Find flag objects in the scene
    /// </summary>
    private void FindFlags()
    {
        Flag[] flags = FindObjectsByType<Flag>(FindObjectsSortMode.None);
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

    private void CheckWinCondition()
    {
        if (!HasStateAuthority || GameIsOver) return;

        if (team1Flag == null || team2Flag == null)
        {
            Debug.LogWarning("Cannot check win condition - flags are null!");
            return;
        }

        // Get base positions from TeamManager
        if (TeamManager.Instance == null)
        {
            Debug.LogError("TeamManager.Instance is null!");
            return;
        }

        TeamData team1Data = TeamManager.Instance.GetTeamData("Team1");
        TeamData team2Data = TeamManager.Instance.GetTeamData("Team2");

        if (team1Data == null || team2Data == null)
        {
            Debug.LogError("Team data is null!");
            return;
        }

        Vector3 team1BasePos = team1Data.basePosition;
        Vector3 team2BasePos = team2Data.basePosition;

        // Check if team1 has both flags at their base
        bool team1FlagAtTeam1Base = Vector3.Distance(team1Flag.transform.position, team1BasePos) < 2f;
        bool team2FlagAtTeam1Base = Vector3.Distance(team2Flag.transform.position, team1BasePos) < 2f;
        Team1HasBothFlags = team1FlagAtTeam1Base && team2FlagAtTeam1Base;

        // Check if team2 has both flags at their base
        bool team1FlagAtTeam2Base = Vector3.Distance(team1Flag.transform.position, team2BasePos) < 2f;
        bool team2FlagAtTeam2Base = Vector3.Distance(team2Flag.transform.position, team2BasePos) < 2f;
        Team2HasBothFlags = team1FlagAtTeam2Base && team2FlagAtTeam2Base;

        // Check for win
        if (Team1HasBothFlags)
        {
            EndGame(1);
        }
        else if (Team2HasBothFlags)
        {
            EndGame(2);
        }
    }

    private void EndGame(int winningTeam)
    {
        if (!HasStateAuthority || GameIsOver) return;

        GameIsOver = true;
        AnnounceWinnerRpc(winningTeam);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_ShowNotification(string message)
    {
        if (notificationText != null)
        {
            notificationText.text = message;
            notificationText.gameObject.SetActive(true);
            CancelInvoke(nameof(HideNotification));
            Invoke(nameof(HideNotification), notificationDuration);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void AnnounceWinnerRpc(int winningTeam)
    {
        if (winnerText != null)
        {
            winnerText.text = $"Team {winningTeam} Wins!";
        }

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }

        // Show final notification
        if (notificationText != null)
        {
            notificationText.text = $"Game Over! Team {winningTeam} Wins!";
            notificationText.gameObject.SetActive(true);
        }
    }

    private void HideNotification()
    {
        if (notificationText != null)
        {
            notificationText.gameObject.SetActive(false);
        }
    }

    private void UpdateFlagStatusUI()
    {
        // Update Team1 flag status
        if (team1FlagStatusText != null && team1Flag != null)
        {
            if (team1Flag.State == Flag.FlagState.AtHome)
            {
                team1FlagStatusText.text = "🏴 At Base";
                team1FlagStatusText.color = Color.green;
            }
            else if (team1Flag.State == Flag.FlagState.Carried)
            {
                team1FlagStatusText.text = "⚠️ Taken!";
                team1FlagStatusText.color = Color.red;
            }
            else
            {
                team1FlagStatusText.text = "📍 Dropped";
                team1FlagStatusText.color = Color.yellow;
            }
        }

        // Update Team2 flag status
        if (team2FlagStatusText != null && team2Flag != null)
        {
            if (team2Flag.State == Flag.FlagState.AtHome)
            {
                team2FlagStatusText.text = "🏴 At Base";
                team2FlagStatusText.color = Color.green;
            }
            else if (team2Flag.State == Flag.FlagState.Carried)
            {
                team2FlagStatusText.text = "⚠️ Taken!";
                team2FlagStatusText.color = Color.red;
            }
            else
            {
                team2FlagStatusText.text = "📍 Dropped";
                team2FlagStatusText.color = Color.yellow;
            }
        }
    }

    private void UpdateFlagIndicators()
    {
        // Update Team1 flag indicator
        if (team1FlagIndicator != null && team1Flag != null)
        {
            team1FlagIndicator.transform.position = team1Flag.transform.position;
            team1FlagIndicator.SetActive(team1Flag.State != Flag.FlagState.AtHome);
        }

        // Update Team2 flag indicator
        if (team2FlagIndicator != null && team2Flag != null)
        {
            team2FlagIndicator.transform.position = team2Flag.transform.position;
            team2FlagIndicator.SetActive(team2Flag.State != Flag.FlagState.AtHome);
        }
    }

    #region Public Getters

    public bool IsGameOver() => GameIsOver;

    public int GetPlayerCount()
    {
        if (Runner == null) return 0;
        return Runner.ActivePlayers.Count();
    }

    #endregion

    #region Public Methods (called by Flag script or other systems)

    /// <summary>
    /// SERVER: Called when a flag is captured (brought to enemy base)
    /// </summary>
    public void OnFlagCaptured(string flagOwner, string capturingTeam)
    {
        if (!HasStateAuthority) return;

        Debug.Log($"Flag captured! {flagOwner} flag captured by {capturingTeam}");

        // Check if capturing team now has both flags in their base
        CheckWinCondition();
    }

    #endregion
}