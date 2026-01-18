using UnityEngine;
using UnityEngine.UI;
using Fusion;

/// <summary>
/// Manages the team selection UI in the MainMenu scene.
/// Shows team buttons after the player connects to the network.
/// 
/// FIXED VERSION - Works with regular Unity Text (no TextMeshPro required)
/// 
/// SETUP INSTRUCTIONS:
/// 1. Attach this script to a new GameObject called "TeamSelectionManager" in MainMenu scene
/// 2. Create the UI elements (see Setup Instructions section in SETUP_GUIDE.md)
/// 3. Assign all UI references in the Inspector
/// 4. Link this to GameNetworkManager
/// 
/// HOW IT WORKS:
/// 1. Initially hidden
/// 2. GameNetworkManager shows this UI after connecting
/// 3. Player clicks a team button
/// 4. Team choice is stored in TeamSelectionData
/// 5. Gameplay scene loads and uses that team choice
/// </summary>
public class TeamSelectionUI : MonoBehaviour
{
    #region Inspector References

    [Header("📱 UI Panel")]
    [Tooltip("The main panel containing all team selection UI elements")]
    [SerializeField] private GameObject teamSelectionPanel;

    [Header("🔵 Team 1 Button")]
    [Tooltip("Button to join Team 1 (Blue Team)")]
    [SerializeField] private Button team1Button;

    [Tooltip("Text showing Team 1 player count")]
    [SerializeField] private Text team1CountText; // Using regular Unity Text

    [Header("🔴 Team 2 Button")]
    [Tooltip("Button to join Team 2 (Red Team)")]
    [SerializeField] private Button team2Button;

    [Tooltip("Text showing Team 2 player count")]
    [SerializeField] private Text team2CountText; // Using regular Unity Text

    [Header("🎮 Network Settings")]
    [Tooltip("Reference to GameNetworkManager to trigger scene loading")]
    [SerializeField] private GameNetworkManager networkManager;

    [Tooltip("Build index of Gameplay scene (must match GameNetworkManager)")]
    [SerializeField] private int gameplaySceneIndex = 1;

    [Header("🎨 Visual Settings")]
    [Tooltip("Color for Team 1 button")]
    [SerializeField] private Color team1Color = new Color(0.2f, 0.4f, 1f); // Blue

    [Tooltip("Color for Team 2 button")]
    [SerializeField] private Color team2Color = new Color(1f, 0.2f, 0.2f); // Red

    #endregion

    #region Private Fields

    // Track current team counts for display purposes
    private int team1PlayerCount = 0;
    private int team2PlayerCount = 0;

    // Reference to the network runner
    private NetworkRunner runner;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        // Hide the team selection panel at start
        // It will be shown by GameNetworkManager after connecting
        if (teamSelectionPanel != null)
        {
            teamSelectionPanel.SetActive(false);
        }
        else
        {
            Debug.LogError("❌ Team Selection Panel is not assigned in Inspector!");
        }

        // Set up button click listeners
        if (team1Button != null)
        {
            team1Button.onClick.AddListener(() => OnTeamButtonClicked(1));

            // Apply team color to button
            ColorBlock colors = team1Button.colors;
            colors.normalColor = team1Color;
            colors.highlightedColor = team1Color * 1.2f;
            colors.pressedColor = team1Color * 0.8f;
            team1Button.colors = colors;
        }
        else
        {
            Debug.LogError("❌ Team 1 button is not assigned in Inspector!");
        }

        if (team2Button != null)
        {
            team2Button.onClick.AddListener(() => OnTeamButtonClicked(2));

            // Apply team color to button
            ColorBlock colors = team2Button.colors;
            colors.normalColor = team2Color;
            colors.highlightedColor = team2Color * 1.2f;
            colors.pressedColor = team2Color * 0.8f;
            team2Button.colors = colors;
        }
        else
        {
            Debug.LogError("❌ Team 2 button is not assigned in Inspector!");
        }

        // Validate network manager reference
        if (networkManager == null)
        {
            Debug.LogError("❌ GameNetworkManager reference is not assigned in Inspector!");
        }

        Debug.Log("✅ TeamSelectionUI initialized");
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Shows the team selection UI.
    /// Called by GameNetworkManager after successfully connecting.
    /// 
    /// PARAMS:
    ///   networkRunner - The NetworkRunner instance from the connection
    /// </summary>
    public void ShowTeamSelection(NetworkRunner networkRunner)
    {
        if (teamSelectionPanel == null)
        {
            Debug.LogError("❌ Team selection panel is not assigned!");
            return;
        }

        // Store the runner reference
        runner = networkRunner;

        // Show the team selection UI
        teamSelectionPanel.SetActive(true);

        // Update team counts
        UpdateTeamCounts();

        Debug.Log("📱 ========================================");
        Debug.Log("📱 TEAM SELECTION UI SHOWN");
        Debug.Log("📱 Player can now choose their team");
        Debug.Log("📱 ========================================");
    }

    /// <summary>
    /// Hides the team selection UI.
    /// </summary>
    public void HideTeamSelection()
    {
        if (teamSelectionPanel != null)
        {
            teamSelectionPanel.SetActive(false);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Called when a player clicks a team button.
    /// Stores the team choice and loads the gameplay scene.
    /// 
    /// PARAMS:
    ///   teamNumber - 1 for Team 1, 2 for Team 2
    /// </summary>
    private void OnTeamButtonClicked(int teamNumber)
    {
        Debug.Log($"🎯 ========================================");
        Debug.Log($"🎯 TEAM {teamNumber} SELECTED");
        Debug.Log($"🎯 ========================================");

        // Validate team number
        if (teamNumber != 1 && teamNumber != 2)
        {
            Debug.LogError($"❌ Invalid team number: {teamNumber}");
            return;
        }

        // Store the team choice
        TeamSelectionData.SetLocalPlayerTeam(teamNumber);

        // Disable buttons to prevent double-clicking
        SetButtonsInteractable(false);

        // Load the gameplay scene
        // The NetworkedSpawnManager in the Gameplay scene will read the team choice
        LoadGameplayScene();
    }

    /// <summary>
    /// Loads the gameplay scene where players will spawn.
    /// </summary>
    private async void LoadGameplayScene()
    {
        if (runner == null)
        {
            Debug.LogError("❌ NetworkRunner is null! Cannot load scene.");
            return;
        }

        Debug.Log("🎬 ========================================");
        Debug.Log("🎬 Loading Gameplay Scene...");
        Debug.Log($"🎬 Scene index: {gameplaySceneIndex}");
        Debug.Log("🎬 ========================================");

        // Hide the team selection UI
        HideTeamSelection();

        // Load the gameplay scene using Fusion's scene management
        await runner.LoadScene(SceneRef.FromIndex(gameplaySceneIndex));

        Debug.Log("✅ Gameplay scene load initiated");
    }

    /// <summary>
    /// Updates the team count displays.
    /// This is a simplified version - in a real game, you'd query actual player counts.
    /// </summary>
    private void UpdateTeamCounts()
    {
        // Note: This is a basic implementation
        // In a real game, you'd need to query the actual number of players on each team
        // from the server/host. For now, we'll just show placeholders.

        if (team1CountText != null)
        {
            team1CountText.text = $"Team 1\n{team1PlayerCount} Players";
        }

        if (team2CountText != null)
        {
            team2CountText.text = $"Team 2\n{team2PlayerCount} Players";
        }
    }

    /// <summary>
    /// Enables or disables team selection buttons.
    /// </summary>
    private void SetButtonsInteractable(bool interactable)
    {
        if (team1Button != null)
        {
            team1Button.interactable = interactable;
        }

        if (team2Button != null)
        {
            team2Button.interactable = interactable;
        }

        Debug.Log($"🎮 Team buttons {(interactable ? "enabled" : "disabled")}");
    }

    #endregion

    #region Edge Case Handling

    /// <summary>
    /// EDGE CASE: What if everyone picks Team 1?
    /// 
    /// SOLUTION OPTIONS:
    /// 1. Allow unbalanced teams (current implementation)
    /// 2. Force team balancing by disabling full team buttons
    /// 3. Show warnings but allow player choice
    /// 
    /// To implement option 2 (force balancing), uncomment this method
    /// and call it from UpdateTeamCounts():
    /// </summary>
    /*
    private void EnforceTeamBalance()
    {
        // Calculate team difference
        int difference = Mathf.Abs(team1PlayerCount - team2PlayerCount);
        
        // If difference is 2 or more, disable the button for the larger team
        if (difference >= 2)
        {
            if (team1PlayerCount > team2PlayerCount)
            {
                team1Button.interactable = false;
                team2Button.interactable = true;
                Debug.Log("⚖️ Team 1 is full - join Team 2!");
            }
            else
            {
                team1Button.interactable = true;
                team2Button.interactable = false;
                Debug.Log("⚖️ Team 2 is full - join Team 1!");
            }
        }
        else
        {
            // Teams are balanced, enable both buttons
            team1Button.interactable = true;
            team2Button.interactable = true;
        }
    }
    */

    #endregion
}