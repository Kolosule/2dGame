using UnityEngine;
using UnityEngine.UI;
using Fusion;
using Fusion.Sockets;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;

/// <summary>
/// FIXED VERSION - Prevents auto-spawning without breaking Fusion's input system
/// The key is to NOT implement OnPlayerJoined spawning here
/// </summary>
public class GameNetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("UI References")]
    public Button hostButton;
    public Button clientButton;
    public GameObject menuPanel;

    [Header("Team Selection")]
    public TeamSelectionUI teamSelectionUI;

    [Header("Network Settings")]
    public string sessionName = "PvPvERoom";
    public int gameplaySceneIndex = 1;

    [Header("Testing Mode")]
    [Tooltip("Enable single player mode (no Photon needed)")]
    public bool singlePlayerMode = true;

    private NetworkRunner runner;
    private bool isConnected = false;

    void Start()
    {
        DontDestroyOnLoad(gameObject);
        runner = gameObject.AddComponent<NetworkRunner>();

        if (hostButton != null)
            hostButton.onClick.AddListener(StartHost);
        else
            Debug.LogError("❌ Host button not assigned!");

        if (clientButton != null)
            clientButton.onClick.AddListener(StartClient);
        else
            Debug.LogError("❌ Client button not assigned!");

        if (teamSelectionUI == null)
            Debug.LogError("❌ TeamSelectionUI not assigned!");

        TeamSelectionData.Reset();
        Debug.Log("✅ GameNetworkManager initialized");
    }

    async void StartHost()
    {
        Debug.Log("🏠 Starting game...");
        SetButtonsInteractable(false);

        // CRITICAL FIX: Always use Host mode for multiplayer
        // AutoHostOrClient creates separate sessions!
        GameMode mode = GameMode.Host;  // ⭐ CHANGED THIS

        var args = new StartGameArgs()
        {
            GameMode = mode,
            SessionName = sessionName,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        };

        Debug.Log($"🏠 Starting in {mode} mode");
        var result = await runner.StartGame(args);

        if (result.Ok)
        {
            Debug.Log("✅ Game started successfully!");
            isConnected = true;
            HideMenu();
            ShowTeamSelection();
        }
        else
        {
            Debug.LogError($"❌ Failed to start: {result.ShutdownReason}");
            SetButtonsInteractable(true);
        }
    }

    async void StartClient()
    {
        // In single player mode, client button does the same as host
        if (singlePlayerMode)
        {
            Debug.Log("🔌 Single player mode - starting game...");
            StartHost();
            return;
        }

        Debug.Log("🔌 Starting as Client...");
        SetButtonsInteractable(false);

        var args = new StartGameArgs()
        {
            GameMode = GameMode.Client,
            SessionName = sessionName,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        };

        var result = await runner.StartGame(args);

        if (result.Ok)
        {
            Debug.Log("✅ Connected!");
            isConnected = true;
            HideMenu();
            ShowTeamSelection();
        }
        else
        {
            Debug.LogError($"❌ Failed to connect: {result.ShutdownReason}");
            SetButtonsInteractable(true);
        }
    }

    void HideMenu()
    {
        if (menuPanel != null)
        {
            menuPanel.SetActive(false);
            Debug.Log("✅ Menu hidden");
        }
    }

    void ShowTeamSelection()
    {
        if (teamSelectionUI != null && runner != null)
        {
            teamSelectionUI.ShowTeamSelection(runner);
            Debug.Log("✅ Team selection shown");
        }
        else
        {
            Debug.LogError("❌ Cannot show team selection!");
        }
    }

    void OnDestroy()
    {
        if (runner != null)
        {
            runner.Shutdown();
        }
    }

    void OnApplicationQuit()
    {
        if (runner != null)
        {
            runner.Shutdown();
        }
    }

    void SetButtonsInteractable(bool interactable)
    {
        if (hostButton != null)
            hostButton.interactable = interactable;
        if (clientButton != null)
            clientButton.interactable = interactable;
    }

    // Fusion callbacks
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"🌐 ========================================");
        Debug.Log($"🌐 Player {player.PlayerId} joined in MainMenu");
        Debug.Log($"🌐 Scene: {SceneManager.GetActiveScene().name}");
        Debug.Log($"🌐 We will NOT spawn them here");
        Debug.Log($"🌐 NetworkedSpawnManager will handle spawning");
        Debug.Log($"🌐 ========================================");

        // CRITICAL: DO NOT SPAWN PLAYER HERE
        // Let NetworkedSpawnManager in the Gameplay scene handle it
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"👋 Player {player.PlayerId} left");
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"🛑 Shutdown: {shutdownReason}");
        isConnected = false;

        if (teamSelectionUI != null)
            teamSelectionUI.HideTeamSelection();

        if (menuPanel != null)
            menuPanel.SetActive(true);

        SetButtonsInteractable(true);
        TeamSelectionData.Reset();
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log("📡 Connected!");
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.Log($"📡 Disconnected: {reason}");
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        request.Accept();
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError($"❌ Connection failed: {reason}");
    }

    // Empty required callbacks
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log($"🎬 Scene loaded: {SceneManager.GetActiveScene().name}");
    }
    public void OnSceneLoadStart(NetworkRunner runner)
    {
        Debug.Log("🎬 Loading scene...");
    }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
}