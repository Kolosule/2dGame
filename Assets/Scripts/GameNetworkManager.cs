using UnityEngine;
using UnityEngine.UI;
using Fusion;
using Fusion.Addons.Physics;
using Fusion.Sockets;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Menu + lobby controller. Players pick a team in the MainMenu; each choice is sent to the host
/// (the host records its own directly, clients use Fusion reliable-data since no NetworkObject
/// exists in the menu scene). The host loads the Gameplay scene only once every connected player
/// has submitted a choice, so the host's authoritative scene load never drags a client into
/// gameplay before they have chosen. The collected choices live in LobbyTeamChoices, which the
/// Gameplay-scene NetworkedSpawnManager reads on the host to spawn each player on the right team.
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

    [Header("Lobby")]
    [Tooltip("How many players must connect before the host starts the match (multiplayer only). " +
             "Single-player mode always starts with 1.")]
    public int expectedPlayerCount = 2;

    // Reliable-data channel tag for a client sending its team choice to the host.
    private static readonly Fusion.Sockets.ReliableKey TeamChoiceKey =
        Fusion.Sockets.ReliableKey.FromInts(0x54454100, 0x4D, 0, 0); // "TEAM"

    private NetworkRunner runner;
    private bool isConnected = false;
    private bool gameStarting = false;

    void Start()
    {
        DontDestroyOnLoad(gameObject);
        runner = gameObject.AddComponent<NetworkRunner>();

        // Fusion steps Physics2D inside the network tick (required for NetworkRigidbody2D prediction).
        gameObject.AddComponent<RunnerSimulatePhysics2D>();

        // Register the single input source.
        var inputProvider = gameObject.AddComponent<NetworkInputProvider>();
        runner.AddCallbacks(inputProvider);

        // Receive lobby callbacks (player join/leave, reliable team-choice data) on the host.
        runner.AddCallbacks(this);

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
        LobbyTeamChoices.Clear();
        gameStarting = false;
    }

    async void StartHost()
    {
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

        var result = await runner.StartGame(args);

        if (result.Ok)
        {
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
            StartHost();
            return;
        }

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
        }
    }

    void ShowTeamSelection()
    {
        if (teamSelectionUI != null && runner != null)
        {
            teamSelectionUI.ShowTeamSelection(runner);
        }
        else
        {
            Debug.LogError("❌ Cannot show team selection!");
        }
    }

    // ============================
    // LOBBY: team choice + start gate
    // ============================

    /// <summary>
    /// Called by TeamSelectionUI when the local player picks a team. Records the choice on the host
    /// (directly if we are the host, otherwise over reliable-data) and re-evaluates the start gate.
    /// Does NOT load the scene - the host does that once everyone has chosen (see TryStartMatch).
    /// </summary>
    public void SubmitLocalTeamChoice(int teamNumber)
    {
        if (teamNumber != 1 && teamNumber != 2)
        {
            Debug.LogError($"❌ Invalid team number: {teamNumber}");
            return;
        }

        if (runner == null || !runner.IsRunning)
        {
            Debug.LogError("❌ Cannot submit team choice - runner not running!");
            return;
        }

        // Keep the local player's own intent available locally (used for UI / debugging).
        TeamSelectionData.SetLocalPlayerTeam(teamNumber);

        if (runner.IsServer)
        {
            RecordChoice(runner.LocalPlayer, teamNumber);
        }
        else
        {
            runner.SendReliableDataToServer(TeamChoiceKey, new byte[] { (byte)teamNumber });
        }
    }

    /// <summary>Host-only: store a player's choice and re-check whether the match can start.</summary>
    private void RecordChoice(PlayerRef player, int teamNumber)
    {
        if (!runner.IsServer)
            return;

        if (teamNumber != 1 && teamNumber != 2)
        {
            Debug.LogError($"❌ [HOST] Invalid team choice {teamNumber} from Player {player.PlayerId}");
            return;
        }

        LobbyTeamChoices.Set(player, teamNumber);

        TryStartMatch();
    }

    /// <summary>
    /// Host-only: load the Gameplay scene once enough players have connected AND every connected
    /// player has submitted a team choice. Idempotent - only triggers the load once.
    /// </summary>
    private void TryStartMatch()
    {
        if (runner == null || !runner.IsServer || gameStarting)
            return;

        int required = singlePlayerMode ? 1 : Mathf.Max(1, expectedPlayerCount);
        int active = runner.ActivePlayers.Count();

        if (active < required)
        {
            return;
        }

        foreach (var player in runner.ActivePlayers)
        {
            if (!LobbyTeamChoices.Has(player))
            {
                return;
            }
        }

        gameStarting = true;
        LoadGameplayScene();
    }

    private async void LoadGameplayScene()
    {
        if (teamSelectionUI != null)
            teamSelectionUI.HideTeamSelection();

        await runner.LoadScene(SceneRef.FromIndex(gameplaySceneIndex));
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

        // CRITICAL: DO NOT SPAWN PLAYER HERE
        // Let NetworkedSpawnManager in the Gameplay scene handle it
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {

        // Drop their lobby choice and re-evaluate the start gate (e.g. a leaver who hadn't chosen
        // should no longer block the others).
        if (runner.IsServer && !gameStarting)
        {
            LobbyTeamChoices.Remove(player);
            TryStartMatch();
        }
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        isConnected = false;

        if (teamSelectionUI != null)
            teamSelectionUI.HideTeamSelection();

        if (menuPanel != null)
            menuPanel.SetActive(true);

        SetButtonsInteractable(true);
        TeamSelectionData.Reset();
        LobbyTeamChoices.Clear();
        gameStarting = false;
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
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
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
        // Host receives clients' team choices here (clients use SendReliableDataToServer).
        if (!runner.IsServer || key != TeamChoiceKey)
            return;

        if (data.Count < 1 || data.Array == null)
        {
            Debug.LogError($"❌ [HOST] Empty team-choice payload from Player {player.PlayerId}");
            return;
        }

        int teamNumber = data.Array[data.Offset];
        RecordChoice(player, teamNumber);
    }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner)
    {
    }
    public void OnSceneLoadStart(NetworkRunner runner)
    {
    }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
}

/// <summary>
/// Per-player team choices collected by the host during the lobby, keyed by PlayerRef. Lives on the
/// host only (the server is authoritative over team assignment) and survives the menu -> gameplay
/// scene load. NetworkedSpawnManager reads this on the host to spawn each player on their chosen
/// team. This is NOT the old host-only TeamSelectionData fallback (which conflated every player with
/// the host's single local pick) - every entry here is a specific player's own submitted choice.
/// </summary>
public static class LobbyTeamChoices
{
    private static readonly Dictionary<PlayerRef, int> choices = new Dictionary<PlayerRef, int>();

    public static void Set(PlayerRef player, int team) => choices[player] = team;
    public static bool Has(PlayerRef player) => choices.ContainsKey(player);
    public static bool TryGet(PlayerRef player, out int team) => choices.TryGetValue(player, out team);
    public static void Remove(PlayerRef player) => choices.Remove(player);
    public static void Clear() => choices.Clear();
    public static int Count => choices.Count;
}