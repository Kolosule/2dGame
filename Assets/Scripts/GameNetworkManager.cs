using UnityEngine;
using UnityEngine.UI;
using Fusion;
using Fusion.Sockets;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;

/// <summary>
/// FIXED VERSION - Only handles network connection, NOT player spawning
/// Player spawning is handled by NetworkedSpawnManager in Gameplay scene
/// 
/// WHAT THIS SCRIPT DOES:
/// ✅ Shows Host/Client buttons in menu
/// ✅ Connects to Photon Fusion network
/// ✅ Loads the Gameplay scene
/// 
/// WHAT THIS SCRIPT DOES NOT DO:
/// ❌ Spawn players (that's NetworkedSpawnManager's job)
/// ❌ Assign teams (that's NetworkedSpawnManager's job)
/// ❌ Manage player objects (that's NetworkedSpawnManager's job)
/// </summary>
public class GameNetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("UI References")]
    [Tooltip("Button to start as host (server + client)")]
    public Button hostButton;

    [Tooltip("Button to join as client")]
    public Button clientButton;

    [Tooltip("Menu panel to hide after connecting")]
    public GameObject menuPanel;

    [Header("Network Settings")]
    [Tooltip("Name of the game session players will join")]
    public string sessionName = "PvPvERoom";

    [Tooltip("Build index of the Gameplay scene (check File > Build Settings)")]
    public int gameplaySceneIndex = 1;

    // The NetworkRunner handles all Fusion networking
    private NetworkRunner runner;

    void Start()
    {
        // Create a new NetworkRunner instance
        // This is the core component that manages all networking in Fusion
        runner = gameObject.AddComponent<NetworkRunner>();

        // Set up button click events
        if (hostButton != null)
        {
            hostButton.onClick.AddListener(StartHost);
        }

        if (clientButton != null)
        {
            clientButton.onClick.AddListener(StartClient);
        }
    }

    /// <summary>
    /// Starts the game as a host (acts as both server and client)
    /// The host can play the game AND manages the game state for all players
    /// </summary>
    async void StartHost()
    {
        Debug.Log("🏠 ========================================");
        Debug.Log("🏠 Starting as Host...");
        Debug.Log("🏠 ========================================");

        // Disable buttons while connecting to prevent double-clicks
        SetButtonsInteractable(false);

        // Configure the game session settings
        var args = new StartGameArgs()
        {
            // GameMode.Host means this player is both the server and a player
            GameMode = GameMode.Host,

            // Session name - all players joining this name will be in the same game
            SessionName = sessionName,

            // The scene to load - using build index is most reliable
            Scene = SceneRef.FromIndex(gameplaySceneIndex),

            // SceneManager tells Fusion to handle scene loading automatically
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        };

        // Start the game session (this is async, so we await it)
        var result = await runner.StartGame(args);

        // Check if connection was successful
        if (result.Ok)
        {
            Debug.Log("✅ ========================================");
            Debug.Log("✅ Host started successfully!");
            Debug.Log("✅ Loading Gameplay scene...");
            Debug.Log("✅ ========================================");
            HideMenu();
        }
        else
        {
            Debug.LogError("❌ ========================================");
            Debug.LogError($"❌ Failed to start host: {result.ShutdownReason}");
            Debug.LogError("❌ ========================================");
            SetButtonsInteractable(true);
        }
    }

    /// <summary>
    /// Joins an existing game as a client
    /// Clients play the game but the host manages the game state
    /// </summary>
    async void StartClient()
    {
        Debug.Log("🔌 ========================================");
        Debug.Log("🔌 Starting as Client...");
        Debug.Log($"🔌 Looking for session: {sessionName}");
        Debug.Log("🔌 ========================================");

        // Disable buttons while connecting
        SetButtonsInteractable(false);

        // Configure the game session settings
        var args = new StartGameArgs()
        {
            // GameMode.Client means this player joins an existing game
            GameMode = GameMode.Client,

            // Join the session with this name
            SessionName = sessionName,

            // The scene to load - using build index is most reliable
            Scene = SceneRef.FromIndex(gameplaySceneIndex),

            // SceneManager tells Fusion to handle scene loading automatically
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        };

        // Join the game session (this is async, so we await it)
        var result = await runner.StartGame(args);

        // Check if connection was successful
        if (result.Ok)
        {
            Debug.Log("✅ ========================================");
            Debug.Log("✅ Client connected successfully!");
            Debug.Log("✅ Loading Gameplay scene...");
            Debug.Log("✅ ========================================");
            HideMenu();
        }
        else
        {
            Debug.LogError("❌ ========================================");
            Debug.LogError($"❌ Failed to connect as client: {result.ShutdownReason}");
            Debug.LogError("❌ Make sure a host is running!");
            Debug.LogError("❌ ========================================");
            SetButtonsInteractable(true);
        }
    }

    /// <summary>
    /// Hides the menu panel after successfully connecting
    /// </summary>
    void HideMenu()
    {
        if (menuPanel != null)
        {
            menuPanel.SetActive(false);
            Debug.Log("📱 Menu panel hidden");
        }
    }

    /// <summary>
    /// Enable or disable the host and client buttons
    /// </summary>
    void SetButtonsInteractable(bool interactable)
    {
        if (hostButton != null)
            hostButton.interactable = interactable;

        if (clientButton != null)
            clientButton.interactable = interactable;

        Debug.Log($"🎮 Buttons {(interactable ? "enabled" : "disabled")}");
    }

    // ============================================================
    // FUSION CALLBACKS
    // These methods are called automatically by Fusion when
    // network events happen.
    // 
    // IMPORTANT: This script ONLY logs events - it does NOT
    // spawn players or manage game objects!
    // ============================================================

    /// <summary>
    /// Called when a player joins the game session
    /// NOTE: This is just for logging! Player spawning happens in NetworkedSpawnManager!
    /// </summary>
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"🌐 [GameNetworkManager] Player {player.PlayerId} connected to session");

        // ========================================
        // CRITICAL: DO NOT SPAWN PLAYERS HERE!
        // ========================================
        // Player spawning is handled by NetworkedSpawnManager in the Gameplay scene.
        // This script (GameNetworkManager) only handles connection and scene loading.
        // 
        // If you try to spawn players here, you'll get duplicate spawns because
        // BOTH GameNetworkManager and NetworkedSpawnManager will try to spawn!
        // 
        // Let NetworkedSpawnManager handle all spawning logic.
    }

    /// <summary>
    /// Called when a player leaves the game session
    /// </summary>
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"👋 [GameNetworkManager] Player {player.PlayerId} disconnected from session");
    }

    /// <summary>
    /// Called when the network session shuts down
    /// Use this to clean up and return to the menu
    /// </summary>
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log("🛑 ========================================");
        Debug.Log($"🛑 Network shutdown: {shutdownReason}");
        Debug.Log("🛑 Returning to menu...");
        Debug.Log("🛑 ========================================");

        // Show the menu again so players can reconnect
        if (menuPanel != null)
        {
            menuPanel.SetActive(true);
        }

        // Re-enable the buttons
        SetButtonsInteractable(true);
    }

    // ============================================================
    // OTHER FUSION CALLBACKS
    // These are required by INetworkRunnerCallbacks interface
    // We implement them but leave most empty since GameNetworkManager
    // only handles connection, not gameplay
    // ============================================================

    public void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log("📡 [GameNetworkManager] Connected to server!");
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.Log($"📡 [GameNetworkManager] Disconnected from server: {reason}");
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        // Accept all connection requests
        request.Accept();
        Debug.Log("📡 [GameNetworkManager] Connection request accepted");
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError($"❌ [GameNetworkManager] Connection failed: {reason}");
    }

    // Empty callbacks - not used by GameNetworkManager
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
        Debug.Log("🔄 [GameNetworkManager] Host migration occurred");
    }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log("🎬 [GameNetworkManager] Scene load completed");
    }
    public void OnSceneLoadStart(NetworkRunner runner)
    {
        Debug.Log("🎬 [GameNetworkManager] Scene load starting");
    }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
}