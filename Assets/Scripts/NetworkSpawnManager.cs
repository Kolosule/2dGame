using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System;
using System.Linq;  // ADDED THIS - needed for Count()

/// <summary>
/// FIXED VERSION - Now handles players who joined before scene loaded!
/// 
/// KEY FIX: In AutoHostOrClient mode, players join in MainMenu, but this script
/// is only in Gameplay scene. We need to spawn existing players when scene loads.
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
            Debug.Log("✅ Registering callbacks...");
            Debug.Log("✅ ========================================");

            runner.AddCallbacks(this);
            isInitialized = true;

            Debug.Log("✅ Callbacks registered successfully");

            // CRITICAL FIX: Check if players are already in the session
            CheckForExistingPlayers();
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

    /// <summary>
    /// CRITICAL FIX: Spawn players who joined before this scene loaded
    /// This happens in AutoHostOrClient mode where player joins in MainMenu
    /// </summary>
    private void CheckForExistingPlayers()
    {
        if (runner == null || !runner.IsRunning)
        {
            Debug.Log("⏭️ Runner not running yet, skipping existing player check");
            return;
        }

        Debug.Log("🔍 ========================================");
        Debug.Log("🔍 Checking for existing players...");
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
                Debug.Log($"🎮 Spawning existing player {player.PlayerId}");
                HandlePlayerJoined(player);
            }
            else
            {
                Debug.Log($"⏭️ Player {player.PlayerId} already spawned");
            }
        }

        Debug.Log($"🔍 Existing player check complete. Total active players: {playerCount}");
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
        Debug.Log($"🎮 [SPAWN MANAGER] Player {player.PlayerId} joined");
        Debug.Log($"🎮 ========================================");

        HandlePlayerJoined(player);
    }

    private void HandlePlayerJoined(PlayerRef player)
    {
        // Only server/host spawns players
        if (!Runner.IsServer && !Runner.IsSharedModeMasterClient)
        {
            Debug.Log($"⏭️ Not server/host - skipping spawn logic");
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

        if (playerTeams.ContainsKey(player))
        {
            Debug.LogWarning($"⚠️ Player {player.PlayerId} already has a team assigned. Skipping spawn.");
            return;
        }

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
        Debug.Log($"🎯 ========================================");

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
        }
        else
        {
            Debug.LogError($"❌ Failed to spawn player {player.PlayerId}!");
        }
    }

    private void OnPlayerSpawned(NetworkRunner runner, NetworkObject obj, int team)
    {
        if (verboseLogging)
        {
            Debug.Log($"🎉 OnPlayerSpawned callback running for team {team}");
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
    }
    #endregion

    #region Team Assignment
    private int AssignTeam(PlayerRef player)
    {
        int team = 0;

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

        if (team == 0)
        {
            Debug.Log($"⚖️ ========================================");
            Debug.Log($"⚖️ NO TEAM CHOICE - AUTO-BALANCING");
            Debug.Log($"⚖️    Team 1 count: {team1Count}");
            Debug.Log($"⚖️    Team 2 count: {team2Count}");
            Debug.Log($"⚖️ ========================================");

            team = (team1Count <= team2Count) ? 1 : 2;
        }

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

        if (!Runner.IsServer && !Runner.IsSharedModeMasterClient)
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
        Debug.Log("🎬 Scene load complete - checking for existing players");
        CheckForExistingPlayers();
    }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    #endregion
}