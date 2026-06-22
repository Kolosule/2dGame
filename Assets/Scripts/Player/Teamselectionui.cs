using UnityEngine;
using UnityEngine.UI;
using Fusion;

/// <summary>
/// Records the local player's team choice before the Gameplay scene loads.
/// NetworkedSpawnManager (a NetworkBehaviour that only exists in the Gameplay scene) reads this
/// local choice in Spawned() and relays it to the host over RPC_SetPlayerTeamChoice, which is the
/// explicit signal that gates spawning. We do not touch NetworkedSpawnManager.Instance here because
/// it has not been spawned yet while this menu UI is on screen.
/// </summary>
public class TeamSelectionUI : MonoBehaviour
{
    [Header("📱 UI Panel")]
    [SerializeField] private GameObject teamSelectionPanel;

    [Header("🔵 Team 1 Button")]
    [SerializeField] private Button team1Button;
    [SerializeField] private Text team1CountText;

    [Header("🔴 Team 2 Button")]
    [SerializeField] private Button team2Button;
    [SerializeField] private Text team2CountText;

    [Header("🎮 Network Settings")]
    [SerializeField] private GameNetworkManager networkManager;
    [SerializeField] private int gameplaySceneIndex = 1;

    [Header("🎨 Visual Settings")]
    [SerializeField] private Color team1Color = new Color(0.2f, 0.4f, 1f);
    [SerializeField] private Color team2Color = new Color(1f, 0.2f, 0.2f);

    private int team1PlayerCount = 0;
    private int team2PlayerCount = 0;
    private NetworkRunner runner;

    private void Start()
    {
        if (teamSelectionPanel != null)
        {
            teamSelectionPanel.SetActive(false);
        }

        if (team1Button != null)
        {
            team1Button.onClick.AddListener(() => OnTeamButtonClicked(1));

            ColorBlock colors = team1Button.colors;
            colors.normalColor = team1Color;
            colors.highlightedColor = team1Color * 1.2f;
            colors.pressedColor = team1Color * 0.8f;
            team1Button.colors = colors;
        }

        if (team2Button != null)
        {
            team2Button.onClick.AddListener(() => OnTeamButtonClicked(2));

            ColorBlock colors = team2Button.colors;
            colors.normalColor = team2Color;
            colors.highlightedColor = team2Color * 1.2f;
            colors.pressedColor = team2Color * 0.8f;
            team2Button.colors = colors;
        }
    }

    public void ShowTeamSelection(NetworkRunner networkRunner)
    {
        if (teamSelectionPanel == null)
        {
            Debug.LogError("❌ Team selection panel not assigned!");
            return;
        }

        runner = networkRunner;
        teamSelectionPanel.SetActive(true);
        UpdateTeamCounts();

        Debug.Log("📱 TEAM SELECTION UI SHOWN");
    }

    public void HideTeamSelection()
    {
        if (teamSelectionPanel != null)
        {
            teamSelectionPanel.SetActive(false);
        }
    }

    private void OnTeamButtonClicked(int teamNumber)
    {
        Debug.Log($"🎯 TEAM {teamNumber} SELECTED");

        if (teamNumber != 1 && teamNumber != 2)
        {
            Debug.LogError($"❌ Invalid team number: {teamNumber}");
            return;
        }

        // Store the local choice. NetworkedSpawnManager reads it in Spawned() once the Gameplay
        // scene loads and relays it to the host, which spawns this player on the chosen team.
        TeamSelectionData.SetLocalPlayerTeam(teamNumber);

        SetButtonsInteractable(false);
        LoadGameplayScene();
    }

    private async void LoadGameplayScene()
    {
        if (runner == null)
        {
            Debug.LogError("❌ NetworkRunner is null!");
            return;
        }

        Debug.Log("🎬 Loading Gameplay Scene...");
        HideTeamSelection();

        await runner.LoadScene(SceneRef.FromIndex(gameplaySceneIndex));
        Debug.Log("✅ Gameplay scene load initiated");
    }

    private void UpdateTeamCounts()
    { 
        if (team1CountText != null)
        {
            team1CountText.text = $"Team 1\n{team1PlayerCount} Players";
        }

        if (team2CountText != null)
        {
            team2CountText.text = $"Team 2\n{team2PlayerCount} Players";
        }
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (team1Button != null)
            team1Button.interactable = interactable;

        if (team2Button != null)
            team2Button.interactable = interactable;
    }
}