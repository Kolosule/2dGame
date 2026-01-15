using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System;

/// <summary>
/// UPDATED VERSION - Now respects player's team choice from team selection UI
/// 
/// CHANGES FROM ORIGINAL:
/// ✅ AssignTeam() now checks TeamSelectionData for player's choice
/// ✅ Falls back to auto-balancing only if no team was chosen
/// ✅ Clears team selection data after spawning to prevent reuse
/// 
/// HOW IT WORKS:
/// 1. Player picks team in MainMenu → Stored in TeamSelectionData
/// 2. Gameplay scene loads → NetworkedSpawnManager spawns player
/// 3. AssignTeam() reads the player's choice from TeamSelectionData
/// 4. Player spawns on their chosen team
/// 5. Team choice is cleared (can't switch teams mid-game)
/// 
/// SETUP INSTRUCTIONS:
/// Same as before - just replace the old NetworkedSpawnManager.cs with this file
/// </summary>
public class NetworkedSpawnManager : NetworkBehaviour, INetworkRunnerCallbacks
{
    #region Singleton

    public static NetworkedSpawnManager Instance { get; private set; }

    #endregion

    #region Inspector Fields

    [Header("Player Setup")]
    [Tooltip("The player prefab to spawn (must have NetworkObject component)")]
    [SerializeField] private NetworkObject playerPrefab;

    [Header("Spawn Points")]
    [Tooltip("Spawn points for Team 1 - MUST BE ASSIGNED IN INSPECTOR!")]
    [SerializeField] private Transform[] team1SpawnPoints;

    [Tooltip("Spawn points for Team 2 - MUST BE ASSIGNED IN INSPECTOR!")]
    [SerializeField] private Transform[] team2SpawnPoints;

    [Header("Debug Settings")]
    [Tooltip("Enable extra detailed logging for debugging")]
    [SerializeField] private bool verboseLogging = true;

    #endregion

    #region Private Fields

    // Dictionary to track which team each player is on
    private Dictionary<PlayerRef, int> playerTeams = new Dictionary<PlayerRef, int>();

    // Track how many players are on each team (for auto-balancing)
    private int team1Count = 0;
    private int team2Count = 0;

    // Reference to the Fusion network runner
    private NetworkRunner runner;

    // Track if we've successfully initialized and registered callbacks
    private bool isInitialized = false;

    // HashSet to prevent duplicate spawns
    private HashSet<PlayerRef> spawnedPlayers = new HashSet<PlayerRef>();

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // Singleton pattern
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
        // Find the NetworkRunner in the scene
        runner = FindFirstObjectByType<NetworkRunner>();

        if (runner != null)
        {
            Debug.Log("✅ ========================================");
            Debug.Log("✅ NetworkedSpawnManager STARTED");
            Debug.Log($"✅ Found NetworkRunner: {runner.name}");
            Debug.Log("✅ Registering callbacks...");
            Debug.Log("✅ ========================================");

            // Register this script to receive network callbacks
            runner.AddCallbacks(this);
            isInitialized = true;

            Debug.Log("✅ Callbacks registered successfully");
        }
        else
        {
            Debug.LogError("❌ ========================================");
            Debug.LogError("❌ NetworkRunner NOT FOUND!");
            Debug.LogError("❌ Make sure GameNetworkManager exists and has connected");
            Debug.LogError("❌ ========================================");
        }

        // Validate spawn points
        ValidateSpawnPoints();
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

    /// <summary>
    /// Called automatically by Fusion when a player joins the game.
    /// This is where we spawn the player on their chosen team.
    /// </summary>
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"🎮 ========================================");
        Debug.Log($"🎮 [SPAWN MANAGER] Player {player.PlayerId} joined");
        Debug.Log($"🎮 ========================================");

        // STEP 1: Only the server/host should spawn players
        if (!Runner.IsServer && !Runner.IsSharedModeMasterClient)
        {
            Debug.Log($"⏭️ Not server/host - skipping spawn logic");
            return;
        }

        // STEP 2: Verify we're initialized
        if (!isInitialized)
        {
            Debug.LogError("❌ NetworkedSpawnManager not initialized! Cannot spawn player.");
            return;
        }

        // STEP 3: Check if already spawned (prevent duplicates)
        if (spawnedPlayers.Contains(player))
        {
            Debug.LogWarning($"⚠️ Player {player.PlayerId} was already spawned! Skipping.");
            return;
        }

        // STEP 4: Check if player already has a team
        if (playerTeams.ContainsKey(player))
        {
            Debug.LogWarning($"⚠️ Player {player.PlayerId} already has a team assigned. Skipping spawn.");
            return;
        }

        // STEP 5: Mark player as being spawned
        spawnedPlayers.Add(player);
        Debug.Log($"✅ Player {player.PlayerId} marked as spawning");

        // STEP 6: Assign team (NEW - uses player's choice)
        int team = AssignTeam(player);

        // STEP 7: Get spawn position
        Vector3 spawnPosition = GetSpawnPosition(team);

        // STEP 8: Spawn the player
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

        // Spawn the player on the network
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

        // Set the player's team in PlayerTeamData
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

    #region Team Assignment (UPDATED)

    /// <summary>
    /// UPDATED VERSION - Assigns team based on player's choice
    /// 
    /// NEW FLOW:
    /// 1. Check if player chose a team in TeamSelectionData
    /// 2. If yes → Use their choice
    /// 3. If no → Fall back to auto-balancing
    /// 4. Store the assignment and update counts
    /// 
    /// PARAMS:
    ///   player - The player to assign a team to
    ///   
    /// RETURNS: 1 for Team 1, 2 for Team 2
    /// </summary>
    private int AssignTeam(PlayerRef player)
    {
        int team = 0;

        // NEW: Check if the player chose a team in the team selection UI
        if (TeamSelectionData.HasChosenTeam())
        {
            // Use the player's chosen team
            team = TeamSelectionData.GetLocalPlayerTeam();

            Debug.Log($"🎯 ========================================");
            Debug.Log($"🎯 USING PLAYER'S TEAM CHOICE");
            Debug.Log($"🎯    Player {player.PlayerId} chose Team {team}");
            Debug.Log($"🎯 ========================================");

            // IMPORTANT: Clear the team selection data immediately
            // This prevents the choice from being reused if the player reconnects
            TeamSelectionData.ClearTeamSelection();

            // Validate the team number
            if (team != 1 && team != 2)
            {
                Debug.LogError($"❌ Invalid team choice: {team}. Falling back to auto-balance.");
                team = 0; // Will trigger auto-balance below
            }
        }

        // If no valid team was chosen, use auto-balancing
        if (team == 0)
        {
            Debug.Log($"⚖️ ========================================");
            Debug.Log($"⚖️ NO TEAM CHOICE - AUTO-BALANCING");
            Debug.Log($"⚖️    Team 1 count: {team1Count}");
            Debug.Log($"⚖️    Team 2 count: {team2Count}");
            Debug.Log($"⚖️ ========================================");

            // Auto-balance: assign to the team with fewer players
            if (team1Count <= team2Count)
            {
                team = 1;
            }
            else
            {
                team = 2;
            }
        }

        // Update team counts
        if (team == 1)
        {
            team1Count++;
        }
        else if (team == 2)
        {
            team2Count++;
        }

        // Store the team assignment
        playerTeams[player] = team;

        // Log final assignment
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

        // Only server manages teams
        if (!Runner.IsServer && !Runner.IsSharedModeMasterClient)
            return;

        // Remove from spawned players list
        if (spawnedPlayers.Contains(player))
        {
            spawnedPlayers.Remove(player);
            Debug.Log($"✅ Removed Player {player.PlayerId} from spawned list");
        }

        // Update team counts
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

    #region Spawn Position Logic

    public Vector3 GetSpawnPosition(int team)
    {
        Transform[] spawnPoints = team == 1 ? team1SpawnPoints : team2SpawnPoints;

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError($"❌ No spawn points for Team {team}!");
            return Vector3.zero;
        }

        // Pick a random spawn point
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
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }

    #endregion
}