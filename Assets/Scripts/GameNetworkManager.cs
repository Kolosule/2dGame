using UnityEngine;
using UnityEngine.UI;
using Fusion;
using Fusion.Addons.Physics;
using Fusion.Sockets;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;

/// <summary>
/// Menu + lobby controller. Players pick a team in the MainMenu; each choice is sent to the host
/// (the host records its own directly, clients use Fusion reliable-data since no NetworkObject
/// exists in the menu scene). Only the host has a Start button, and it is enabled only once every
/// connected player has submitted a choice; the host clicks it to load the Gameplay scene, so the
/// host's authoritative scene load never drags a client into gameplay before they have chosen. The
/// collected choices live in LobbyTeamChoices, which the Gameplay-scene NetworkedSpawnManager reads
/// on the host to spawn each player on the right team.
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

    // Reliable-data channel tag for a client sending its team choice to the host.
    private static readonly Fusion.Sockets.ReliableKey TeamChoiceKey =
        Fusion.Sockets.ReliableKey.FromInts(0x54454100, 0x4D, 0, 0); // "TEAM"

    // Reliable-data channel tag for a client sending its buff loadout to the host.
    private static readonly Fusion.Sockets.ReliableKey LoadoutKey =
        Fusion.Sockets.ReliableKey.FromInts(0x4C4F4144, 0x55, 0, 0); // "LOAD"

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

        LobbyTeamChoices.Clear();
        LobbyLoadoutChoices.Clear();
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
    /// (directly if we are the host, otherwise over reliable-data) and refreshes the host's Start
    /// gate. Does NOT load the scene - the host loads it explicitly via RequestStartMatch once they
    /// click Start (which is only enabled after every connected player has chosen).
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

        if (runner.IsServer)
        {
            RecordChoice(runner.LocalPlayer, teamNumber);
        }
        else
        {
            runner.SendReliableDataToServer(TeamChoiceKey, new byte[] { (byte)teamNumber });
        }
    }

    /// <summary>
    /// Called by TeamSelectionUI when the local player confirms their buff order. Records on the host
    /// (directly if we are the host, else over reliable-data), parallel to SubmitLocalTeamChoice.
    /// </summary>
    public void SubmitLocalLoadoutChoice(byte[] order)
    {
        if (order == null || runner == null || !runner.IsRunning) return;

        if (runner.IsServer)
            LobbyLoadoutChoices.Set(runner.LocalPlayer, order);
        else
            runner.SendReliableDataToServer(LoadoutKey, order);
    }

    /// <summary>Host-only: store a player's choice and refresh the host's Start button.</summary>
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

        RefreshStartGate();
    }

    /// <summary>
    /// Host-only: true once every connected player (the host included) has submitted a team choice.
    /// There is no player-count minimum - a solo host who has chosen may start.
    /// </summary>
    private bool CanStartMatch()
    {
        if (runner == null || !runner.IsServer || gameStarting)
            return false;

        foreach (var player in runner.ActivePlayers)
        {
            if (!LobbyTeamChoices.Has(player))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Host-only: recompute whether the match may start and push that state to the host's Start
    /// button. Called whenever lobby state changes (player joined/left, choice recorded). Never
    /// loads the scene - the host triggers that explicitly via RequestStartMatch.
    /// </summary>
    private void RefreshStartGate()
    {
        if (runner == null || !runner.IsServer)
            return;

        if (teamSelectionUI != null)
            teamSelectionUI.SetStartAvailable(CanStartMatch());
    }

    /// <summary>
    /// Host-only: called when the host clicks the Start button. Loads the Gameplay scene if every
    /// connected player has chosen a team. Idempotent - only triggers the load once.
    /// </summary>
    public void RequestStartMatch()
    {
        if (!CanStartMatch())
            return;

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
        // Let NetworkedSpawnManager in the Gameplay scene handle it.

        // A late joiner hasn't chosen yet, so re-disable the host's Start button until they pick.
        if (runner.IsServer && !gameStarting)
            RefreshStartGate();
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {

        // Drop their lobby choice and re-evaluate the start gate (e.g. a leaver who hadn't chosen
        // should no longer block the others).
        if (runner.IsServer && !gameStarting)
        {
            LobbyTeamChoices.Remove(player);
            LobbyLoadoutChoices.Remove(player);
            RefreshStartGate();
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
        LobbyTeamChoices.Clear();
        LobbyLoadoutChoices.Clear();
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
        if (!runner.IsServer) return;

        if (key == TeamChoiceKey)
        {
            if (data.Count < 1 || data.Array == null)
            {
                Debug.LogError($"❌ [HOST] Empty team-choice payload from Player {player.PlayerId}");
                return;
            }
            int teamNumber = data.Array[data.Offset];
            RecordChoice(player, teamNumber);
            return;
        }

        if (key == LoadoutKey)
        {
            if (data.Count < 1 || data.Array == null) return;
            var order = new byte[data.Count];
            System.Array.Copy(data.Array, data.Offset, order, 0, data.Count);
            LobbyLoadoutChoices.Set(player, order);
        }
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

/// <summary>
/// Per-player buff loadout (priority order as BuffId bytes) collected by the host during the lobby,
/// parallel to LobbyTeamChoices. Read by NetworkedSpawnManager on the host to initialise each
/// player's PlayerBuffs. A missing entry falls back to the BuffLoadoutConfig default order.
/// </summary>
public static class LobbyLoadoutChoices
{
    private static readonly Dictionary<PlayerRef, byte[]> choices = new Dictionary<PlayerRef, byte[]>();

    public static void Set(PlayerRef player, byte[] order) => choices[player] = order;
    public static bool TryGet(PlayerRef player, out byte[] order) => choices.TryGetValue(player, out order);
    public static void Remove(PlayerRef player) => choices.Remove(player);
    public static void Clear() => choices.Clear();
}