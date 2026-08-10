using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Game.Audio.Core;

/// <summary>
/// Lobby screen: "Players: X/20" header, two team columns of name rows, team-switch buttons, a
/// collapsible loadout picker, and a Start button shown only to the designated host. Renders
/// purely from the server's LobbyStateSnapshot (ApplyLobbyState) — it holds no authoritative
/// state, so host-mode and dedicated-server mode share one rendering path.
/// </summary>
public class LobbyScreenUI : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private TMP_Text playersHeader;
    [SerializeField] private TMP_Text statusText;

    [SerializeField] private Button optionsButton;

    [Tooltip("Shared with MainMenuUI — one SettingsPanel instance serves both screens.")]
    [SerializeField] private SettingsPanel settingsPanel;

    [Header("Team Columns")]
    [SerializeField] private Transform team1ListParent;
    [SerializeField] private Transform team2ListParent;
    [Tooltip("Prefab with a TMP_Text on it (or a child). One instance per roster row, pooled.")]
    [SerializeField] private GameObject nameRowPrefab;
    [SerializeField] private Button switchToTeam1Button;
    [SerializeField] private Button switchToTeam2Button;

    [Header("Start (designated host only)")]
    [SerializeField] private Button startButton;

    [Header("Loadout (collapsible)")]
    [SerializeField] private Button loadoutToggleButton;
    [SerializeField] private GameObject loadoutPanel;
    [Tooltip("The buff loadout config (same asset used by the player prefab).")]
    [SerializeField] private BuffLoadoutConfig buffConfig;
    [Tooltip("One row per loadout slot, top = highest priority.")]
    [SerializeField] private TMP_Text[] slotLabels;
    [SerializeField] private Button[] slotUpButtons;
    [SerializeField] private Button[] slotDownButtons;

    [Header("Wiring")]
    [SerializeField] private GameNetworkManager networkManager;

    private readonly List<GameObject> rowPool = new List<GameObject>();
    private List<Game.Buffs.Core.BuffId> loadoutOrder;

    private void Start()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (loadoutPanel != null) loadoutPanel.SetActive(false);

        if (switchToTeam1Button != null)
            switchToTeam1Button.onClick.AddListener(() => networkManager.RequestTeamSwitch(1));
        if (switchToTeam2Button != null)
            switchToTeam2Button.onClick.AddListener(() => networkManager.RequestTeamSwitch(2));

        if (startButton != null)
        {
            startButton.onClick.AddListener(() =>
            {
                Audio.PlayUi(AudioCueId.UiClick);
                networkManager.RequestStartMatch();
            });
            startButton.gameObject.SetActive(false);
        }

        if (loadoutToggleButton != null)
            loadoutToggleButton.onClick.AddListener(ToggleLoadoutPanel);

        if (optionsButton != null) optionsButton.onClick.AddListener(OpenSettings);

        InitLoadoutOrder();
        WireLoadoutButtons();
        RefreshLoadoutLabels();
    }

    /// <summary>
    /// Re-point this screen at the persistent GameNetworkManager after a return-to-lobby scene
    /// reload. The reloaded scene's serialized reference points at the duplicate GNM that the
    /// singleton dup-guard destroys, so without this the buttons call into a destroyed object and
    /// do nothing. The onClick lambdas read this field at click time, so updating it is enough.
    /// </summary>
    public void SetNetworkManager(GameNetworkManager gnm)
    {
        networkManager = gnm;
    }

    public void Show()
    {
        // Symmetric with Hide() — closes the shared settings window defensively so this screen can
        // never come up with the options window stacked over it, regardless of which path led here.
        if (settingsPanel != null) settingsPanel.Close();
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
        SetStatus("Waiting for lobby state...");
    }

    public void Hide()
    {
        if (settingsPanel != null) settingsPanel.Close();
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
    }

    /// <summary>
    /// Client-local settings only. Opening this does not touch the runner, the roster, or any
    /// networked state — the lobby connection keeps running behind it.
    /// </summary>
    private void OpenSettings()
    {
        if (settingsPanel == null)
        {
            Debug.LogError("❌ LobbyScreenUI: settingsPanel not assigned!");
            return;
        }

        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        settingsPanel.Open(() =>
        {
            if (lobbyPanel != null) lobbyPanel.SetActive(true);
        });
    }

    /// <summary>
    /// Renders the server's snapshot: header count, roster rows per team column, switch-button
    /// enabling, and the Start button (visible only when the local player is the designated host).
    /// </summary>
    public void ApplyLobbyState(LobbyStateSnapshot s, int localPlayerId)
    {
        if (s == null) return;

        if (playersHeader != null)
            playersHeader.text = $"Players: {s.Players.Count}/{s.MaxPlayers}";

        int rowIndex = 0;
        int localTeam = 0;
        foreach (var p in s.Players)
        {
            if (p.Id == localPlayerId) localTeam = p.Team;

            var row = GetRow(rowIndex++);
            row.transform.SetParent(p.Team == 1 ? team1ListParent : team2ListParent, false);
            var label = row.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                string host = p.Id == s.HostId ? "★ " : "";
                string you = p.Id == localPlayerId ? " (you)" : "";
                label.text = $"{host}{p.Name}{you}";
            }
            row.SetActive(true);
        }
        for (int i = rowIndex; i < rowPool.Count; i++)
            rowPool[i].SetActive(false);

        if (switchToTeam1Button != null) switchToTeam1Button.interactable = localTeam == 2;
        if (switchToTeam2Button != null) switchToTeam2Button.interactable = localTeam == 1;

        bool isHost = s.HostId != LobbyHostPolicy.NoHost && localPlayerId == s.HostId;
        if (startButton != null)
        {
            startButton.gameObject.SetActive(isHost);
            startButton.interactable = isHost && s.CanStart;
        }

        SetStatus(isHost
            ? "You are the lobby host — press Start when ready."
            : "Waiting for the host to start the match...");
    }

    private GameObject GetRow(int index)
    {
        while (rowPool.Count <= index)
        {
            var row = Instantiate(nameRowPrefab);
            row.SetActive(false);
            rowPool.Add(row);
        }
        return rowPool[index];
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    // ============================
    // Loadout picker (moved from the old TeamSelectionUI, labels now TMP)
    // ============================

    private void ToggleLoadoutPanel()
    {
        Audio.PlayUi(AudioCueId.UiToggle);
        if (loadoutPanel != null) loadoutPanel.SetActive(!loadoutPanel.activeSelf);
    }

    private void InitLoadoutOrder()
    {
        loadoutOrder = new List<Game.Buffs.Core.BuffId>();
        if (buffConfig != null && buffConfig.DefaultOrder != null)
            foreach (var id in buffConfig.DefaultOrder) loadoutOrder.Add(id);
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

        // Every reorder is submitted immediately (tiny payload) — no separate confirm step.
        if (networkManager != null)
            networkManager.SubmitLocalLoadoutChoice(LoadoutAsBytes());
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

    private byte[] LoadoutAsBytes() => Game.Buffs.Core.LoadoutCodec.ToBytes(loadoutOrder);
}
