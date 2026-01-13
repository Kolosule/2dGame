using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System;

/// <summary>
/// FIXED VERSION - Manages player spawning for Photon Fusion multiplayer.
/// Handles team assignment, spawn positioning, and prevents duplicate spawns.
/// 
/// SETUP INSTRUCTIONS:
/// 1. Attach this component to a GameObject in your GAMEPLAY scene (NOT MainMenu)
/// 2. In Unity Inspector, assign:
///    - Player Prefab (must have NetworkObject component)
///    - Team1 Spawn Points (array of Transform objects)
///    - Team2 Spawn Points (array of Transform objects)
/// 3. Make sure GameNetworkManager does NOT spawn players
/// 
/// HOW IT WORKS:
/// - Only runs on the server/host
/// - Automatically called by Fusion when players join
/// - Auto-balances teams
/// - Spawns players at correct team spawn points
/// - Prevents duplicate spawns
/// </summary>
public class NetworkedSpawnManager : NetworkBehaviour, INetworkRunnerCallbacks
{
    #region Singleton

    // Singleton pattern ensures only one spawn manager exists
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

    // CRITICAL: HashSet to prevent duplicate spawns
    // A HashSet is like a guest list - it remembers who's already been spawned
    private HashSet<PlayerRef> spawnedPlayers = new HashSet<PlayerRef>();

    #endregion

    #region Unity Lifecycle

    /// <summary>
    /// Called when the GameObject is created
    /// Sets up the singleton instance
    /// </summary>
    private void Awake()
    {
        // Singleton pattern: only allow one NetworkedSpawnManager to exist
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("⚠️ Multiple NetworkedSpawnManagers found! Destroying duplicate.");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (verboseLogging)
        {
            Debug.Log("✅ NetworkedSpawnManager Instance created in Awake");
        }
    }

    /// <summary>
    /// Called when the GameObject is destroyed
    /// Cleanup to prevent memory leaks
    /// </summary>
    private void OnDestroy()
    {
        // Clear singleton reference if this was the active instance
        if (Instance == this)
        {
            Instance = null;
        }

        // Unregister from network callbacks to prevent errors
        if (runner != null)
        {
            runner.RemoveCallbacks(this);
        }

        if (verboseLogging)
        {
            Debug.Log("🗑️ NetworkedSpawnManager destroyed and cleaned up");
        }
    }

    #endregion

    #region Fusion Lifecycle

    /// <summary>
    /// Called by Fusion when this NetworkBehaviour is spawned on the network
    /// This is where we register our callbacks
    /// </summary>
    public override void Spawned()
    {
        runner = Runner;

        // Only the server/host handles player spawning
        // Clients don't need to register spawn callbacks
        if (Runner.IsServer || Runner.IsSharedModeMasterClient)
        {
            Debug.Log("🖥️ ========================================");
            Debug.Log("🖥️ [SERVER] NetworkedSpawnManager initializing...");
            Debug.Log("🖥️ ========================================");

            // Register this object to receive network callbacks
            runner.AddCallbacks(this);
            isInitialized = true;

            // Validate that everything is set up correctly
            ValidateSetup();

            Debug.Log("✅ ========================================");
            Debug.Log("✅ [SERVER] NetworkedSpawnManager ready!");
            Debug.Log("✅ Waiting for players to join...");
            Debug.Log("✅ ========================================");
        }
        else
        {
            Debug.Log("💻 [CLIENT] NetworkedSpawnManager: Running as client (spawning disabled)");
            Debug.Log("💻 Only the server spawns players - this client will receive them automatically");
        }
    }

    /// <summary>
    /// Called by Fusion when this NetworkBehaviour is despawned
    /// Cleanup to prevent errors
    /// </summary>
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (runner != null)
        {
            runner.RemoveCallbacks(this);
        }

        isInitialized = false;

        if (verboseLogging)
        {
            Debug.Log("🛑 NetworkedSpawnManager: Despawned and cleaned up");
        }
    }

    #endregion

    #region Validation

    /// <summary>
    /// Validates that spawn points and player prefab are properly assigned
    /// Logs clear error messages if anything is missing
    /// </summary>
    private void ValidateSetup()
    {
        bool hasErrors = false;

        // Check Team 1 spawn points
        if (team1SpawnPoints == null || team1SpawnPoints.Length == 0)
        {
            Debug.LogError("❌ ========================================");
            Debug.LogError("❌ CRITICAL ERROR: Team 1 Spawn Points NOT assigned!");
            Debug.LogError("❌ Players will spawn at (0, 0, 0) which is probably wrong!");
            Debug.LogError("❌ ========================================");
            Debug.LogError("❌ HOW TO FIX:");
            Debug.LogError("❌ 1. Select the GameObject with NetworkedSpawnManager");
            Debug.LogError("❌ 2. In Inspector, find 'Team1 Spawn Points'");
            Debug.LogError("❌ 3. Set size to at least 1");
            Debug.LogError("❌ 4. Drag spawn point GameObjects into the array");
            Debug.LogError("❌ ========================================");
            hasErrors = true;
        }
        else
        {
            Debug.Log($"✅ Team 1 has {team1SpawnPoints.Length} spawn point(s):");
            for (int i = 0; i < team1SpawnPoints.Length; i++)
            {
                if (team1SpawnPoints[i] != null)
                {
                    Debug.Log($"   [{i}] {team1SpawnPoints[i].name} at position {team1SpawnPoints[i].position}");
                }
                else
                {
                    Debug.LogError($"❌ Team 1 Spawn Point [{i}] is NULL!");
                    hasErrors = true;
                }
            }
        }

        // Check Team 2 spawn points
        if (team2SpawnPoints == null || team2SpawnPoints.Length == 0)
        {
            Debug.LogError("❌ ========================================");
            Debug.LogError("❌ CRITICAL ERROR: Team 2 Spawn Points NOT assigned!");
            Debug.LogError("❌ Players will spawn at (0, 0, 0) which is probably wrong!");
            Debug.LogError("❌ ========================================");
            Debug.LogError("❌ HOW TO FIX:");
            Debug.LogError("❌ 1. Select the GameObject with NetworkedSpawnManager");
            Debug.LogError("❌ 2. In Inspector, find 'Team2 Spawn Points'");
            Debug.LogError("❌ 3. Set size to at least 1");
            Debug.LogError("❌ 4. Drag spawn point GameObjects into the array");
            Debug.LogError("❌ ========================================");
            hasErrors = true;
        }
        else
        {
            Debug.Log($"✅ Team 2 has {team2SpawnPoints.Length} spawn point(s):");
            for (int i = 0; i < team2SpawnPoints.Length; i++)
            {
                if (team2SpawnPoints[i] != null)
                {
                    Debug.Log($"   [{i}] {team2SpawnPoints[i].name} at position {team2SpawnPoints[i].position}");
                }
                else
                {
                    Debug.LogError($"❌ Team 2 Spawn Point [{i}] is NULL!");
                    hasErrors = true;
                }
            }
        }

        // Check player prefab
        if (playerPrefab == null)
        {
            Debug.LogError("❌ ========================================");
            Debug.LogError("❌ CRITICAL ERROR: Player Prefab NOT assigned!");
            Debug.LogError("❌ Cannot spawn players without a prefab!");
            Debug.LogError("❌ ========================================");
            Debug.LogError("❌ HOW TO FIX:");
            Debug.LogError("❌ 1. Select the GameObject with NetworkedSpawnManager");
            Debug.LogError("❌ 2. In Inspector, find 'Player Prefab'");
            Debug.LogError("❌ 3. Drag your networked player prefab into this field");
            Debug.LogError("❌ 4. Make sure the prefab has a NetworkObject component");
            Debug.LogError("❌ ========================================");
            hasErrors = true;
        }
        else
        {
            // Verify the prefab has required components
            NetworkObject netObj = playerPrefab.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError($"❌ Player Prefab '{playerPrefab.name}' is missing NetworkObject component!");
                hasErrors = true;
            }
            else
            {
                Debug.Log($"✅ Player Prefab assigned: {playerPrefab.name}");
            }

            PlayerTeamData teamData = playerPrefab.GetComponent<PlayerTeamData>();
            if (teamData == null)
            {
                Debug.LogWarning($"⚠️ Player Prefab '{playerPrefab.name}' is missing PlayerTeamData component!");
                Debug.LogWarning("⚠️ Team assignment may not work correctly!");
            }
            else
            {
                Debug.Log($"✅ Player Prefab has PlayerTeamData component");
            }
        }

        // Final warning if there are errors
        if (hasErrors)
        {
            Debug.LogError("⚠️⚠️⚠️ ========================================");
            Debug.LogError("⚠️⚠️⚠️ SPAWNING WILL FAIL!");
            Debug.LogError("⚠️⚠️⚠️ Fix the errors above in Unity Inspector!");
            Debug.LogError("⚠️⚠️⚠️ ========================================");
        }
        else
        {
            Debug.Log("✅ All spawn setup validations passed!");
        }
    }

    #endregion

    #region INetworkRunnerCallbacks - PLAYER CALLBACKS

    /// <summary>
    /// Called automatically by Fusion when a new player joins the game.
    /// This is the MAIN method for player spawning.
    /// 
    /// FLOW:
    /// 1. Verify we're the server (only server spawns)
    /// 2. Check if player already spawned (prevent duplicates)
    /// 3. Assign team (auto-balance)
    /// 4. Get spawn position from team spawn points
    /// 5. Spawn the player using Runner.Spawn()
    /// </summary>
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (verboseLogging)
        {
            Debug.Log($"📥 ========================================");
            Debug.Log($"📥 [SPAWN MANAGER] OnPlayerJoined TRIGGERED");
            Debug.Log($"📥    Player ID: {player.PlayerId}");
            Debug.Log($"📥    Is Server: {Runner.IsServer}");
            Debug.Log($"📥    Is Initialized: {isInitialized}");
            Debug.Log($"📥    Total players spawned so far: {spawnedPlayers.Count}");
            Debug.Log($"📥 ========================================");
        }

        // STEP 1: Only the server/host should spawn players
        // Clients will receive the spawned players automatically
        if (!Runner.IsServer && !Runner.IsSharedModeMasterClient)
        {
            if (verboseLogging)
            {
                Debug.Log("💻 [CLIENT] Not server - skipping spawn (server will handle it)");
            }
            return;
        }

        // STEP 2: Verify we're initialized
        if (!isInitialized)
        {
            Debug.LogError("❌ NetworkedSpawnManager not initialized! Cannot spawn player.");
            Debug.LogError("❌ This shouldn't happen - check Spawned() method");
            return;
        }

        // STEP 3: CRITICAL - Check if we already spawned this player
        // This prevents duplicate spawns if OnPlayerJoined is called multiple times
        if (spawnedPlayers.Contains(player))
        {
            Debug.LogWarning($"⚠️⚠️⚠️ ========================================");
            Debug.LogWarning($"⚠️⚠️⚠️ DUPLICATE SPAWN ATTEMPT DETECTED!");
            Debug.LogWarning($"⚠️⚠️⚠️ Player {player.PlayerId} was already spawned!");
            Debug.LogWarning($"⚠️⚠️⚠️ ========================================");
            Debug.LogWarning($"⚠️ This means OnPlayerJoined was called TWICE for the same player!");
            Debug.LogWarning($"⚠️ Common causes:");
            Debug.LogWarning($"⚠️ 1. Multiple scripts implementing OnPlayerJoined (check GameNetworkManager)");
            Debug.LogWarning($"⚠️ 2. Multiple NetworkedSpawnManager instances in scene");
            Debug.LogWarning($"⚠️ 3. Callbacks registered multiple times");
            Debug.LogWarning($"⚠️⚠️⚠️ ========================================");
            return;
        }

        // STEP 4: Check if player already has a team (shouldn't happen, but double-check)
        if (playerTeams.ContainsKey(player))
        {
            Debug.LogWarning($"⚠️ Player {player.PlayerId} already has a team assigned. This is unusual.");
            Debug.LogWarning($"⚠️ Skipping spawn to prevent issues.");
            return;
        }

        // STEP 5: Mark this player as being spawned
        // This prevents duplicate spawns even if OnPlayerJoined is called again
        spawnedPlayers.Add(player);

        if (verboseLogging)
        {
            Debug.Log($"✅ Player {player.PlayerId} marked as spawning");
            Debug.Log($"✅ Total spawned players: {spawnedPlayers.Count}");
        }

        // STEP 6: Assign team (auto-balance between Team 1 and Team 2)
        int team = AssignTeam(player);

        // STEP 7: Get spawn position from the team's spawn points
        Vector3 spawnPosition = GetSpawnPosition(team);

        // STEP 8: Actually spawn the player on the network
        SpawnPlayer(runner, player, spawnPosition, team);
    }

    /// <summary>
    /// Called when a player leaves the game.
    /// Updates team counts and removes player from tracking.
    /// </summary>
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"👋 ========================================");
        Debug.Log($"👋 [SPAWN MANAGER] Player {player.PlayerId} left the game");
        Debug.Log($"👋 ========================================");

        // Only server manages teams
        if (!Runner.IsServer && !Runner.IsSharedModeMasterClient)
            return;

        // Remove from spawned players list
        if (spawnedPlayers.Contains(player))
        {
            spawnedPlayers.Remove(player);
            Debug.Log($"✅ Removed Player {player.PlayerId} from spawned list");
            Debug.Log($"✅ Remaining spawned players: {spawnedPlayers.Count}");
        }

        // Update team counts for auto-balancing
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

    #region Team Assignment

    /// <summary>
    /// Assigns a team to the player using auto-balancing.
    /// Players are assigned to the team with fewer players.
    /// 
    /// RETURNS: 1 for Team1, 2 for Team2
    /// </summary>
    private int AssignTeam(PlayerRef player)
    {
        int team;

        // Auto-balance: assign to the team with fewer players
        if (team1Count <= team2Count)
        {
            team = 1;
            team1Count++;
        }
        else
        {
            team = 2;
            team2Count++;
        }

        // Store the team assignment for this player
        playerTeams[player] = team;

        // Log team assignment details
        Debug.Log($"👥 ========================================");
        Debug.Log($"👥 TEAM ASSIGNMENT");
        Debug.Log($"👥    Player {player.PlayerId} → Team {team}");
        Debug.Log($"👥    Team 1 count: {team1Count}");
        Debug.Log($"👥    Team 2 count: {team2Count}");
        Debug.Log($"👥 ========================================");

        return team;
    }

    #endregion

    #region Spawn Position Logic

    /// <summary>
    /// Gets a random spawn position for the specified team.
    /// Randomly selects from the team's assigned spawn points.
    /// 
    /// PARAMS:
    ///   team - 1 for Team1, 2 for Team2
    ///   
    /// RETURNS: Position to spawn at (or Vector3.zero if no spawn points)
    /// </summary>
    public Vector3 GetSpawnPosition(int team)
    {
        // Select the appropriate spawn points array based on team
        Transform[] spawnPoints = team == 1 ? team1SpawnPoints : team2SpawnPoints;

        // Validate that spawn points are assigned
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError($"❌ ========================================");
            Debug.LogError($"❌ NO SPAWN POINTS for Team {team}!");
            Debug.LogError($"❌ Player will spawn at Vector3.zero (0, 0, 0)!");
            Debug.LogError($"❌ ========================================");
            Debug.LogError($"❌ FIX THIS: Assign spawn points in Unity Inspector!");
            Debug.LogError($"❌ ========================================");
            return Vector3.zero;
        }

        // Pick a random spawn point from the array
        int randomIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
        Transform selectedSpawn = spawnPoints[randomIndex];

        // Validate the selected spawn point isn't null
        if (selectedSpawn == null)
        {
            Debug.LogError($"❌ Spawn point at index {randomIndex} for Team {team} is NULL!");
            return Vector3.zero;
        }

        Vector3 position = selectedSpawn.position;

        if (verboseLogging)
        {
            Debug.Log($"📍 ========================================");
            Debug.Log($"📍 SPAWN POSITION SELECTED");
            Debug.Log($"📍    Team: {team}");
            Debug.Log($"📍    Spawn point: {selectedSpawn.name}");
            Debug.Log($"📍    Index: {randomIndex} of {spawnPoints.Length}");
            Debug.Log($"📍    Position: {position}");
            Debug.Log($"📍 ========================================");
        }

        return position;
    }

    #endregion

    #region Player Spawning

    /// <summary>
    /// Spawns the player character on the network using Fusion's Runner.Spawn().
    /// 
    /// This creates the player GameObject on the server and automatically
    /// replicates it to all connected clients.
    /// 
    /// PARAMS:
    ///   runner - The NetworkRunner instance
    ///   player - Which player this object belongs to
    ///   position - Where to spawn the player
    ///   team - Which team the player is on (1 or 2)
    /// </summary>
    private void SpawnPlayer(NetworkRunner runner, PlayerRef player, Vector3 position, int team)
    {
        Debug.Log($"🎯 ========================================");
        Debug.Log($"🎯 SPAWNING PLAYER");
        Debug.Log($"🎯    Player ID: {player.PlayerId}");
        Debug.Log($"🎯    Position: {position}");
        Debug.Log($"🎯    Team: {team}");
        Debug.Log($"🎯    Prefab: {(playerPrefab != null ? playerPrefab.name : "NULL")}");
        Debug.Log($"🎯 ========================================");

        // Validate that we have a player prefab assigned
        if (playerPrefab == null)
        {
            Debug.LogError("❌ ========================================");
            Debug.LogError("❌ SPAWN FAILED!");
            Debug.LogError("❌ Player prefab is NULL!");
            Debug.LogError("❌ Assign the player prefab in Unity Inspector!");
            Debug.LogError("❌ ========================================");
            return;
        }

        // Spawn the player using Fusion's network spawn method
        // This creates the object on the network and replicates it to all clients
        NetworkObject playerObject = runner.Spawn(
            playerPrefab,           // The prefab to instantiate
            position,               // World position to spawn at
            Quaternion.identity,    // Rotation (no rotation = identity)
            player,                 // Input authority (who controls this player)
            (runner, obj) => OnPlayerSpawned(runner, obj, team) // Callback after spawn
        );

        // Verify the spawn was successful
        if (playerObject != null)
        {
            Debug.Log($"✅ ========================================");
            Debug.Log($"✅ SPAWN SUCCESS!");
            Debug.Log($"✅    Player {player.PlayerId} spawned successfully!");
            Debug.Log($"✅    GameObject: {playerObject.gameObject.name}");
            Debug.Log($"✅    Position: {playerObject.transform.position}");
            Debug.Log($"✅    Team: {team}");
            Debug.Log($"✅    NetworkObject ID: {playerObject.Id}");
            Debug.Log($"✅ ========================================");
        }
        else
        {
            Debug.LogError($"❌ ========================================");
            Debug.LogError($"❌ SPAWN FAILED!");
            Debug.LogError($"❌ Player {player.PlayerId} spawn returned NULL!");
            Debug.LogError($"❌ Check:");
            Debug.LogError($"❌ 1. Player prefab has NetworkObject component");
            Debug.LogError($"❌ 2. Player prefab is in Resources folder or Network Prefabs list");
            Debug.LogError($"❌ 3. No errors in Console about the prefab");
            Debug.LogError($"❌ ========================================");
        }
    }

    /// <summary>
    /// Called automatically after a player object has been spawned on the network.
    /// This is where we set the player's team in their PlayerTeamData component.
    /// 
    /// PARAMS:
    ///   runner - The NetworkRunner instance
    ///   obj - The spawned NetworkObject
    ///   team - Which team to assign (1 or 2)
    /// </summary>
    private void OnPlayerSpawned(NetworkRunner runner, NetworkObject obj, int team)
    {
        if (verboseLogging)
        {
            Debug.Log($"🎉 ========================================");
            Debug.Log($"🎉 OnPlayerSpawned callback running");
            Debug.Log($"🎉    GameObject: {obj.gameObject.name}");
            Debug.Log($"🎉    Setting team to: {team}");
            Debug.Log($"🎉 ========================================");
        }

        // Find the PlayerTeamData component on the spawned player
        PlayerTeamData teamData = obj.GetComponent<PlayerTeamData>();

        if (teamData != null)
        {
            // Set the team (this will be networked to all clients)
            teamData.SetTeam(team);

            Debug.Log($"✅ Team {team} successfully assigned via PlayerTeamData");
        }
        else
        {
            Debug.LogError("❌ ========================================");
            Debug.LogError("❌ PlayerTeamData component NOT FOUND!");
            Debug.LogError($"❌ Cannot assign team to player!");
            Debug.LogError("❌ ========================================");
            Debug.LogError("❌ FIX THIS:");
            Debug.LogError("❌ 1. Select your player prefab");
            Debug.LogError("❌ 2. Add Component → PlayerTeamData");
            Debug.LogError("❌ 3. Save the prefab");
            Debug.LogError("❌ ========================================");
        }
    }

    #endregion

    #region INetworkRunnerCallbacks - Other Required Callbacks

    // These callbacks are required by the INetworkRunnerCallbacks interface
    // but aren't used by NetworkedSpawnManager, so we leave them empty

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