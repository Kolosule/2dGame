using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Game.Audio.Core;

/// <summary>
/// MainMenu entry screen: nickname + Join/Host + a status line for connect progress and errors.
/// Purely presentational — GameNetworkManager owns the runner and calls back into Show/ShowStatus
/// on failure or shutdown. The nickname persists in PlayerPrefs across sessions.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button hostButton;
    [SerializeField] private TMP_Text statusText;

    [SerializeField] private Button optionsButton;

    [Header("Settings")]
    [Tooltip("Shared with LobbyScreenUI — one SettingsPanel instance serves both screens.")]
    [SerializeField] private SettingsPanel settingsPanel;

    [Header("Reconnect (optional — the status line alone works without these)")]
    [SerializeField] private GameObject reconnectPanel;
    [SerializeField] private Button cancelReconnectButton;

    [Header("Wiring")]
    [SerializeField] private GameNetworkManager networkManager;

    private const string NicknamePref = "lobby.nickname";

    /// <summary>Sanitized nickname from the input field ("" when empty — server keeps the placeholder).</summary>
    public string Nickname =>
        LobbyProtocol.SanitizeNickname(nicknameInput != null ? nicknameInput.text : "");

    private void Start()
    {
        if (nicknameInput != null)
        {
            nicknameInput.characterLimit = LobbyProtocol.MaxNicknameChars;
            nicknameInput.text = PlayerPrefs.GetString(NicknamePref, "");
        }

        if (joinButton != null) joinButton.onClick.AddListener(() => Connect(asHost: false));
        else Debug.LogError("❌ MainMenuUI: Join button not assigned!");

        if (hostButton != null) hostButton.onClick.AddListener(() => Connect(asHost: true));
        else Debug.LogError("❌ MainMenuUI: Host button not assigned!");

        if (optionsButton != null) optionsButton.onClick.AddListener(OpenSettings);

        if (cancelReconnectButton != null)
        {
            cancelReconnectButton.onClick.AddListener(() =>
            {
                Audio.PlayUi(AudioCueId.UiBack);
                if (networkManager != null) networkManager.CancelReconnect();
            });
        }

        if (reconnectPanel != null) reconnectPanel.SetActive(false);

        ShowStatus("");
    }

    private void Connect(bool asHost)
    {
        Audio.PlayUi(AudioCueId.UiClick);
        if (networkManager == null)
        {
            Debug.LogError("❌ MainMenuUI: networkManager not assigned!");
            return;
        }

        PlayerPrefs.SetString(NicknamePref, Nickname);
        PlayerPrefs.Save();
        SetBusy(true);
        ShowStatus(asHost ? "Starting host..." : "Connecting...");
        if (asHost) networkManager.StartHost();
        else networkManager.StartClient();
    }

    /// <summary>
    /// Re-point at the persistent GameNetworkManager after a return-to-lobby scene reload (the
    /// reloaded scene's serialized ref points at the duplicate GNM the dup-guard destroys).
    /// </summary>
    public void SetNetworkManager(GameNetworkManager gnm)
    {
        networkManager = gnm;
    }

    /// <summary>
    /// Client-local settings only — nothing here touches the runner or any networked state, so it
    /// is safe to open at any point on this screen.
    /// </summary>
    private void OpenSettings()
    {
        if (settingsPanel == null)
        {
            Debug.LogError("❌ MainMenuUI: settingsPanel not assigned!");
            return;
        }

        if (menuPanel != null) menuPanel.SetActive(false);
        settingsPanel.Open(() =>
        {
            if (menuPanel != null) menuPanel.SetActive(true);
        });
    }

    public void Show()
    {
        // A connect failure can call Show() while the options window is open; close it so the two
        // panels never stack.
        if (settingsPanel != null) settingsPanel.Close();
        if (menuPanel != null) menuPanel.SetActive(true);
        SetBusy(false);
    }

    public void Hide()
    {
        // Same defensive close as Show() — EnterLobbyUI calls Hide() while the options window may
        // still be open (e.g. opened during "Connecting..."), and leaving it open here strands it
        // over the lobby screen with a stale onClosed callback.
        if (settingsPanel != null) settingsPanel.Close();
        if (menuPanel != null) menuPanel.SetActive(false);
    }

    public void ShowStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    public void SetBusy(bool busy)
    {
        if (joinButton != null) joinButton.interactable = !busy;
        if (hostButton != null) hostButton.interactable = !busy;
    }

    /// <summary>
    /// Reconnecting state: the retry loop's message on the status line, Join/Host disabled, and the
    /// optional reconnect panel (with its Cancel button) shown. Call Show() first — Show resets busy.
    /// </summary>
    public void ShowReconnecting(string message)
    {
        if (reconnectPanel != null) reconnectPanel.SetActive(true);
        SetBusy(true);
        ShowStatus(message);
    }

    public void HideReconnecting()
    {
        if (reconnectPanel != null) reconnectPanel.SetActive(false);
        SetBusy(false);
    }
}
