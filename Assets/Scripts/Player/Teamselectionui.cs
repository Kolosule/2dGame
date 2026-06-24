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

    [Header("▶️ Start Button (host only)")]
    [Tooltip("Shown to the host only. Enabled once every connected player has chosen a team.")]
    [SerializeField] private Button startButton;

    [Header("🎮 Network Settings")]
    [SerializeField] private GameNetworkManager networkManager;

    [Header("🧪 Loadout Picker")]
    [Tooltip("The buff loadout config (same asset used by the player prefab).")]
    [SerializeField] private BuffLoadoutConfig buffConfig;
    [Tooltip("One row per loadout slot, top = highest priority. Each needs a label + Up/Down buttons.")]
    [SerializeField] private Text[] slotLabels;
    [SerializeField] private Button[] slotUpButtons;
    [SerializeField] private Button[] slotDownButtons;

    private System.Collections.Generic.List<Game.Buffs.Core.BuffId> loadoutOrder;

    [Header("⏳ Status Message")]
    [Tooltip("Optional. Shows prompts like \"Waiting for other players...\". " +
             "If left empty, a basic label is created at runtime under the panel.")]
    [SerializeField] private Text statusText;

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

        if (startButton != null)
        {
            startButton.onClick.AddListener(OnStartButtonClicked);
            startButton.gameObject.SetActive(false);
        }

        InitLoadoutOrder();
        WireLoadoutButtons();
        RefreshLoadoutLabels();
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

        // Reset to the initial "pick a team" state in case the panel is shown again.
        SetButtonsInteractable(true);
        SetLoadoutInteractable(true);
        RefreshLoadoutLabels();
        SetStatus("Choose your team!");

        // The Start button is the host's alone; clients never see it. It starts disabled and is
        // enabled by the host's GameNetworkManager once every connected player has chosen.
        if (startButton != null)
        {
            bool isHost = runner != null && runner.IsServer;
            startButton.gameObject.SetActive(isHost);
            startButton.interactable = false;
        }
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
        networkManager.SubmitLocalLoadoutChoice(LoadoutAsBytes());
        networkManager.SubmitLocalTeamChoice(teamNumber);
        SetButtonsInteractable(false);
        SetLoadoutInteractable(false);
        SetStatus($"Joined Team {teamNumber}.\nWaiting for other players...");

    }

    /// <summary>Host-only: clicking Start asks the host's GameNetworkManager to load the match.</summary>
    private void OnStartButtonClicked()
    {
        if (networkManager == null)
        {
            Debug.LogError("❌ NetworkManager not assigned - cannot start match!");
            return;
        }

        networkManager.RequestStartMatch();
    }

    /// <summary>
    /// Host-only: enable or disable the Start button. Called by GameNetworkManager as lobby state
    /// changes (a no-op for clients, which never have a Start button shown).
    /// </summary>
    public void SetStartAvailable(bool available)
    {
        if (startButton != null)
            startButton.interactable = available;
    }

    /// <summary>Sets the status message, creating a fallback label if none was assigned.</summary>
    private void SetStatus(string message)
    {
        EnsureStatusText();
        if (statusText != null)
            statusText.text = message;
    }

    /// <summary>
    /// Creates a simple status label under the panel if the inspector field is empty, so the
    /// "waiting" message works without any extra scene setup. Assign your own Text to style/place it.
    /// </summary>
    private void EnsureStatusText()
    {
        if (statusText != null || teamSelectionPanel == null)
            return;

        GameObject go = new GameObject("StatusText (auto)", typeof(RectTransform));
        go.transform.SetParent(teamSelectionPanel.transform, false);

        statusText = go.AddComponent<Text>();
        statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        statusText.fontSize = 28;
        statusText.alignment = TextAnchor.MiddleCenter;
        statusText.color = Color.white;
        statusText.raycastTarget = false;
        statusText.horizontalOverflow = HorizontalWrapMode.Overflow;
        statusText.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform rt = statusText.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 30f);
        rt.sizeDelta = new Vector2(700f, 80f);
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

    private void InitLoadoutOrder()
    {
        loadoutOrder = new System.Collections.Generic.List<Game.Buffs.Core.BuffId>();
        if (buffConfig != null && buffConfig.DefaultOrder != null)
        {
            foreach (var id in buffConfig.DefaultOrder) loadoutOrder.Add(id);
        }
    }

    private void WireLoadoutButtons()
    {
        if (slotUpButtons != null)
            for (int i = 0; i < slotUpButtons.Length; i++)
            {
                int idx = i;
                if (slotUpButtons[i] != null) slotUpButtons[i].onClick.AddListener(() => MoveSlot(idx, -1));
            }
        if (slotDownButtons != null)
            for (int i = 0; i < slotDownButtons.Length; i++)
            {
                int idx = i;
                if (slotDownButtons[i] != null) slotDownButtons[i].onClick.AddListener(() => MoveSlot(idx, +1));
            }
    }

    private void MoveSlot(int index, int delta)
    {
        if (loadoutOrder == null) return;
        int target = index + delta;
        if (index < 0 || index >= loadoutOrder.Count || target < 0 || target >= loadoutOrder.Count) return;
        (loadoutOrder[index], loadoutOrder[target]) = (loadoutOrder[target], loadoutOrder[index]);
        RefreshLoadoutLabels();
    }

    private void RefreshLoadoutLabels()
    {
        if (slotLabels == null || loadoutOrder == null || buffConfig == null) return;
        for (int i = 0; i < slotLabels.Length; i++)
        {
            if (slotLabels[i] == null) continue;
            if (i < loadoutOrder.Count)
            {
                var def = buffConfig.GetById(loadoutOrder[i]);
                slotLabels[i].text = $"{i + 1}. {(def != null ? def.DisplayName : loadoutOrder[i].ToString())}";
            }
            else slotLabels[i].text = "";
        }
    }

    private byte[] LoadoutAsBytes()
    {
        if (loadoutOrder == null) return null;
        var bytes = new byte[loadoutOrder.Count];
        for (int i = 0; i < loadoutOrder.Count; i++) bytes[i] = (byte)loadoutOrder[i];
        return bytes;
    }

    private void SetLoadoutInteractable(bool interactable)
    {
        if (slotUpButtons != null)
            foreach (var b in slotUpButtons) if (b != null) b.interactable = interactable;
        if (slotDownButtons != null)
            foreach (var b in slotDownButtons) if (b != null) b.interactable = interactable;
    }
}