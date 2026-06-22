using UnityEngine;
using UnityEngine.UI;
using Fusion;

/// <summary>
/// MainMenu team-selection panel. On a button click it submits the local player's choice to the
/// host via GameNetworkManager.SubmitLocalTeamChoice and then locks the buttons ("waiting for
/// other players"). It does NOT load the Gameplay scene - the host loads it once every connected
/// player has chosen, so no player is ever dragged into gameplay before picking a team.
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

        if (networkManager == null)
        {
            Debug.LogError("❌ NetworkManager not assigned - cannot submit team choice!");
            return;
        }

        // Submit the choice to the host. The host loads the Gameplay scene only once every
        // connected player has chosen, so we do NOT load the scene here. Lock the buttons so the
        // player sees they are now waiting for the others.
        networkManager.SubmitLocalTeamChoice(teamNumber);
        SetButtonsInteractable(false);

        Debug.Log("⏳ Waiting for all players to choose...");
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