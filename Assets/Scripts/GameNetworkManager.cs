using UnityEngine;
using UnityEngine.UI;
using Fusion;
using Fusion.Sockets;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Manages network connection for a PvPvE 2D platformer game using Photon Fusion
/// Handles host and client connections with automatic scene management
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
        Debug.Log("Starting as Host...");

        // Disable buttons while connecting
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

        // Start the game session
        var result = await runner.StartGame(args);

        // Check if connection was successful
        if (result.Ok)
        {
            Debug.Log("Host started successfully!");
            HideMenu();
        }
        else
        {
            Debug.LogError($"Failed to start host: {result.ShutdownReason}");
            SetButtonsInteractable(true);
        }
    }

    /// <summary>
    /// Joins an existing game as a client
    /// Clients play the game but the host manages the game state
    /// </summary>
    async void StartClient()
    {
        Debug.Log("Starting as Client...");

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

        // Join the game session
        var result = await runner.StartGame(args);

        // Check if connection was successful
        if (result.Ok)
        {
            Debug.Log("Client connected successfully!");
            HideMenu();
        }
        else
        {
            Debug.LogError($"Failed to connect as client: {result.ShutdownReason}");
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
    }

    // ============================================================
    // FUSION CALLBACKS
    // These methods are called automatically by Fusion when
    // network events happen. You can add your own logic here.
    // ============================================================

    /// <summary>
    /// Called when a player joins the game session
    /// </summary>
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"Player {player.PlayerId} joined the game!");

        // TODO: Add your player spawn logic here
        // For example:
        // - Spawn the player character
        // - Assign them to a team
        // - Initialize their score/coins
    }

    /// <summary>
    /// Called when a player leaves the game session
    /// </summary>
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"Player {player.PlayerId} left the game!");

        // TODO: Add your player cleanup logic here
        // For example:
        // - Remove their character from the game
        // - Redistribute their coins/items
        // - Update team counts
    }

    /// <summary>
    /// Called when the network session shuts down
    /// Use this to clean up and return to the menu
    /// </summary>
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"Network shutdown: {shutdownReason}");

        // Show the menu again so players can reconnect
        if (menuPanel != null)
        {
            menuPanel.SetActive(true);
        }

        // Re-enable the buttons
        SetButtonsInteractable(true);

        // TODO: Add any additional cleanup here
        // For example:
        // - Clear player lists
        // - Reset game state
        // - Save player stats
    }

    // ============================================================
    // OTHER FUSION CALLBACKS
    // These are required by INetworkRunnerCallbacks but you can
    // leave them empty if you don't need them right now
    // ============================================================

    public void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log("Connected to server!");
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.Log($"Disconnected from server: {reason}");
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        // Accept all connection requests
        request.Accept();
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError($"Connection failed: {reason}");
        SetButtonsInteractable(true);
    }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        // Input handling - you'll implement this when adding player controls
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
    {
        // Called when input is missing - usually can ignore
    }

    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
    {
        // For custom messages between players
    }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        // Called when the list of available sessions updates
    }

    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
    {
        // For custom authentication systems
    }

    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
        // Called when the host changes (advanced feature)
    }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
        // For reliable data transmission
    }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
    {
        // For tracking reliable data upload progress
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log("Scene loading complete!");
        // Fusion automatically handles scene loading
    }

    public void OnSceneLoadStart(NetworkRunner runner)
    {
        Debug.Log("Scene loading started...");
        // Fusion automatically handles scene loading
    }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
        // Called when a network object enters a player's Area of Interest
        // Usually can ignore unless doing advanced optimizations
    }

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
        // Called when a network object leaves a player's Area of Interest
        // Usually can ignore unless doing advanced optimizations
    }
}