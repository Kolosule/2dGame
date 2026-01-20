using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System;
using System.Linq;

/// <summary>
/// COMPREHENSIVE FIX - Handles both Host and Client spawning correctly
/// 
/// KEY INSIGHT: In Host/Client mode:
/// - Host (Player 0) spawns themselves locally
/// - When Client (Player 1) joins, the HOST spawns them
/// - This is handled automatically by OnPlayerJoined being called on the HOST
/// </summary>
public class NetworkedSpawnManager : NetworkBehaviour, INetworkRunnerCallbacks
{
    #region Singleton
    public static NetworkedSpawnManager Instance { get; private set; }
    #endregion

    #region Inspector Fields
    [Header("Player Setup")]
    [SerializeField] private NetworkObject playerPrefab;

    [Header("Spawn Points")]
    [SerializeField] private Transform[] team1SpawnPoints;
    [SerializeField] private Transform[] team2SpawnPoints;

    [Header("Debug Settings")]
    [SerializeField] private bool verboseLogging = true;
    #endregion

    #region Private Fields
    private Dictionary<PlayerRef, int> playerTeams = new Dictionary<PlayerRef, int>();
    private int team1Count = 0;
    private int team2Count = 0;
    private NetworkRunner runner;
    private bool isInitialized = false;
    private HashSet<PlayerRef> spawnedPlayers = new HashSet<PlayerRef>();
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("⚠️ Multiple NetworkedSpawnManagers found! Destroying duplicate.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Debug.Log("✅ NetworkedSpawnManager singleton initialized");
    }

    private void Start()
    {
        runner = FindFirstObjectByType<NetworkRunner>();

        if (runner != null)
        {
            Debug.Log("✅ ========================================");
            Debug.Log("✅ NetworkedSpawnManager STARTED");
            Debug.Log($"✅ Found NetworkRunner: {runner.name}");
            Debug.Log($"✅ GameMode: {runner.GameMode}");
            Debug.Log($"✅ IsServer: {runner.IsServer}");
            Debug.Log($"✅ IsClient: {runner.IsClient}");
            Debug.Log("✅ Registering callbacks...");
            Debug.Log("✅ ========================================");

            runner.AddCallbacks(this);
            isInitialized = true;

            Debug.Log("✅ Callbacks registered successfully");

            // Wait a moment for the scene to fully load, then check for existing players
            StartCoroutine(DelayedPlayerCheck());
        }
        else
        {
            Debug.LogError("❌ ========================================");
            Debug.LogError("❌ NetworkRunner NOT FOUND!");
            Debug.LogError("❌ Make sure GameNetworkManager exists and has connected");
            Debug.LogError("❌ ========================================");
        }

        ValidateSpawnPoints();
    }

    private System.Collections.IEnumerator DelayedPlayerCheck()
    {
        // Wait a bit for the scene to fully initialize
        yield return new WaitForSeconds(0.2f);
        CheckForExistingPlayers();
    }

    /// <summary>
    /// CRITICAL: Spawn players who joined in MainMenu but weren't spawned yet
    /// Only runs on the HOST (not clients)
    /// </summary>
    private void CheckForExistingPlayers()
    {
        if (runner == null || !runner.IsRunning)
        {
            Debug.Log("⏭️ Runner not running yet, skipping existing player check");
            return;
        }

        // CRITICAL: Only the HOST spawns players in Host/Client mode
        if (!runner.IsServer)
        {
            Debug.Log("⏭️ We're a client - host will handle spawning");
            return;
        }

        Debug.Log("🔍 ========================================");
        Debug.Log("🔍 Checking for existing players...");
        Debug.Log($"🔍 Active players count: {runner.ActivePlayers.Count()}");
        Debug.Log($"🔍 We are the HOST - we will spawn all players");
        Debug.Log("🔍 ========================================");

        // Get all active players in the session
        int playerCount = 0;
        foreach (var player in runner.ActivePlayers)
        {
            playerCount++;
            Debug.Log($"🔍 Found existing player: {player.PlayerId}");

            // Spawn them if they haven't been spawned yet
            if (!spawnedPlayers.Contains(player))
            {
                Debug.Log($"🎮 Player {player.PlayerId} needs spawning");
                HandlePlayerJoined(player);
            }
            else
            {
                Debug.Log($"✅ Player {player.PlayerId} already spawned");
            }
        }

        Debug.Log($"🔍 Check complete. Processed {playerCount} players");
    }

    private void ValidateSpawnPoints()
    {
        if (team1SpawnPoints == null || team1SpawnPoints.Length == 0)
        {
            Debug.LogError("❌ Team 1 spawn points not assigned!");
        }
        else
        {
            Debug.Log($"✅ Team 1 has {team1SpawnPoints.Length} spawn points");
        }

        if (team2SpawnPoints == null || team2SpawnPoints.Length == 0)
        {
            Debug.LogError("❌ Team 2 spawn points not assigned!");
        }
        else
        {
            Debug.Log($"✅ Team 2 has {team2SpawnPoints.Length} spawn points");
        }
    }
    #endregion

    #region Player Spawning
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"🎮 ========================================");
        Debug.Log($"🎮 [SPAWN MANAGER] OnPlayerJoined callback");
        Debug.Log($"🎮 Player: {player.PlayerId}");
        Debug.Log($"🎮 IsServer: {runner.IsServer}");
        Debug.Log($"🎮 Scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        Debug.Log($"🎮 ========================================");

        // This callback runs when:
        // 1. Player joins AFTER scene loaded (late joiner)
        // 2. On clients when other players join (but clients can't spawn)

        HandlePlayerJoined(player);
    }

    private void HandlePlayerJoined(PlayerRef player)
    {
        Debug.Log($"🔧 HandlePlayerJoined called for Player {player.PlayerId}");
        Debug.Log($"🔧 IsServer: {runner.IsServer}");
        Debug.Log($"🔧 IsInitialized: {isInitialized}");

        // CRITICAL: Only HOST spawns players in Host/Client mode
        if (!runner.IsServer)
        {
            Debug.Log($"⏭️ We're a client - not spawning (host handles this)");
            return;
        }

        if (!isInitialized)
        {
            Debug.LogError("❌ NetworkedSpawnManager not initialized! Cannot spawn player.");
            return;
        }

        if (spawnedPlayers.Contains(player))
        {
            Debug.LogWarning($"⚠️ Player {player.PlayerId} was already spawned! Skipping.");
            return;
        }

        // Mark as spawning BEFORE we actually spawn (prevents double-spawn)
        spawnedPlayers.Add(player);
        Debug.Log($"✅ Player {player.PlayerId} marked as spawning");

        int team = AssignTeam(player);
        Vector3 spawnPosition = GetSpawnPosition(team);
        SpawnPlayer(runner, player, spawnPosition, team);
    }

    private void SpawnPlayer(NetworkRunner runner, PlayerRef player, Vector3 spawnPosition, int team)
    {
        if (playerPrefab == null)
        {
            Debug.LogError("❌ Player prefab is not assigned!");
            return;
        }

        Debug.Log($"🎯 ========================================");
        Debug.Log($"🎯 SPAWNING PLAYER");
        Debug.Log($"🎯    Player ID: {player.PlayerId}");
        Debug.Log($"🎯    Team: {team}");
        Debug.Log($"🎯    Position: {spawnPosition}");
        Debug.Log($"🎯    GameMode: {runner.GameMode}");
        Debug.Log($"🎯    IsServer: {runner.IsServer}");
        Debug.Log($"🎯 ========================================");

        // In Host/Client mode, Runner.Spawn on the host will automatically
        // replicate the spawned object to all clients
        NetworkObject spawnedObject = Runner.Spawn(
            playerPrefab,
            spawnPosition,
            Quaternion.identity,
            player,
            (runner, obj) => OnPlayerSpawned(runner, obj, team)
        );

        if (spawnedObject != null)
        {
            Debug.Log($"✅ Player {player.PlayerId} spawned successfully!");
            Debug.Log($"✅ NetworkObject ID: {spawnedObject.Id}");
        }
        else
        {
            Debug.LogError($"❌ Failed to spawn player {player.PlayerId}!");
            // Remove from spawned list since spawn failed
            spawnedPlayers.Remove(player);
        }
    }

    private void OnPlayerSpawned(NetworkRunner runner, NetworkObject obj, int team)
    {
        if (verboseLogging)
        {
            Debug.Log($"🎉 ========================================");
            Debug.Log($"🎉 OnPlayerSpawned callback");
            Debug.Log($"🎉 Team: {team}");
            Debug.Log($"🎉 Position: {obj.transform.position}");
            Debug.Log($"🎉 ========================================");
        }

        PlayerTeamData teamData = obj.GetComponent<PlayerTeamData>();

        if (teamData != null)
        {
            teamData.SetTeam(team);
            Debug.Log($"✅ Team {team} assigned via PlayerTeamData");
        }
        else
        {
            Debug.LogError("❌ PlayerTeamData component NOT FOUND!");
        }

        // Force initialize the spawn position
        NetworkPlayerWrapper wrapper = obj.GetComponent<NetworkPlayerWrapper>();
        if (wrapper != null)
        {
            wrapper.ForceInitializePosition(obj.transform.position);
            Debug.Log($"✅ Position locked at: {obj.transform.position}");
        }
        else
        {
            Debug.LogWarning("⚠️ NetworkPlayerWrapper not found - position lock skipped");
        }
    }
    #endregion

    #region Team Assignment
    private int AssignTeam(PlayerRef player)
    {
        Debug.Log($"🎲 AssignTeam called for Player {player.PlayerId}");

        // Check if player already has a team (reconnecting case)
        if (playerTeams.TryGetValue(player, out int existingTeam))
        {
            Debug.Log($"♻️ Player {player.PlayerId} reconnecting with existing team {existingTeam}");
            return existingTeam;
        }

        int team = 0;

        // Check if LOCAL player made a team choice
        // Note: This only works for the local player (player who chose the team)
        if (TeamSelectionData.HasChosenTeam())
        {
            team = TeamSelectionData.GetLocalPlayerTeam();

            Debug.Log($"🎯 ========================================");
            Debug.Log($"🎯 USING PLAYER'S TEAM CHOICE");
            Debug.Log($"🎯    Player {player.PlayerId} chose Team {team}");
            Debug.Log($"🎯 ========================================");

            TeamSelectionData.ClearTeamSelection();

            if (team != 1 && team != 2)
            {
                Debug.LogError($"❌ Invalid team choice: {team}. Falling back to auto-balance.");
                team = 0;
            }
        }
        else
        {
            Debug.Log($"ℹ️ No team choice found for Player {player.PlayerId}");
        }

        // If no team chosen, auto-balance
        if (team == 0)
        {
            Debug.Log($"⚖️ ========================================");
            Debug.Log($"⚖️ NO TEAM CHOICE - AUTO-BALANCING");
            Debug.Log($"⚖️    Team 1 count: {team1Count}");
            Debug.Log($"⚖️    Team 2 count: {team2Count}");
            Debug.Log($"⚖️ ========================================");

            team = (team1Count <= team2Count) ? 1 : 2;
        }

        // Update team counts
        if (team == 1)
            team1Count++;
        else if (team == 2)
            team2Count++;

        playerTeams[player] = team;

        Debug.Log($"👥 ========================================");
        Debug.Log($"👥 FINAL TEAM ASSIGNMENT");
        Debug.Log($"👥    Player {player.PlayerId} → Team {team}");
        Debug.Log($"👥    Team 1 count: {team1Count}");
        Debug.Log($"👥    Team 2 count: {team2Count}");
        Debug.Log($"👥 ========================================");

        return team;
    }
    #endregion

    #region Player Leaving
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"👋 Player {player.PlayerId} left the game");

        if (!runner.IsServer)
            return;

        if (spawnedPlayers.Contains(player))
        {
            spawnedPlayers.Remove(player);
            Debug.Log($"✅ Removed Player {player.PlayerId} from spawned list");
        }

        if (playerTeams.TryGetValue(player, out int team))
        {
            if (team == 1)
            {
                team1Count--;
                Debug.Log($"👥 Team 1 count decreased to: {team1Count}");
            }
            else if (team == 2)
            {
                team2Count--;
                Debug.Log($"👥 Team 2 count decreased to: {team2Count}");
            }

            playerTeams.Remove(player);
            Debug.Log($"✅ Removed Player {player.PlayerId} team assignment");
        }
    }
    #endregion

    #region Spawn Position
    public Vector3 GetSpawnPosition(int team)
    {
        Transform[] spawnPoints = team == 1 ? team1SpawnPoints : team2SpawnPoints;

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError($"❌ No spawn points for Team {team}!");
            return Vector3.zero;
        }

        int randomIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
        Vector3 position = spawnPoints[randomIndex].position;

        Debug.Log($"📍 Spawn position: {position} (Team {team}, point {randomIndex})");

        return position;
    }
    #endregion

    #region INetworkRunnerCallbacks - Required Empty Methods
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        request.Accept();
    }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log("🎬 ========================================");
        Debug.Log("🎬 Scene load complete");
        Debug.Log($"🎬 Scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        Debug.Log($"🎬 IsServer: {runner.IsServer}");
        Debug.Log("🎬 Will check for existing players");
        Debug.Log("🎬 ========================================");

        CheckForExistingPlayers();
    }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    #endregion
}