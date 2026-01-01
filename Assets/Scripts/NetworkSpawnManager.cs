using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System;

/// <summary>
/// FIXED VERSION - Manages player spawning for Photon Fusion multiplayer.
/// Handles team assignment and spawn positioning.
/// Attach this to a GameObject in your Gameplay scene (NOT MainMenu).
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
    [Tooltip("Spawn points for Team 1")]
    [SerializeField] private Transform[] team1SpawnPoints;

    [Tooltip("Spawn points for Team 2")]
    [SerializeField] private Transform[] team2SpawnPoints;

    #endregion

    #region Private Fields

    // Track which team each player is on
    private Dictionary<PlayerRef, int> playerTeams = new Dictionary<PlayerRef, int>();

    // Track how many players are on each team (for auto-balancing)
    private int team1Count = 0;
    private int team2Count = 0;

    // Reference to the Fusion runner
    private NetworkRunner runner;

    // Track if we've registered callbacks
    private bool isInitialized = false;

    // CRITICAL FIX: Track which players we've already spawned
    private HashSet<PlayerRef> spawnedPlayers = new HashSet<PlayerRef>();

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // Singleton pattern - only one spawn manager should exist
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple NetworkedSpawnManagers found! Destroying duplicate.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Debug.Log("NetworkedSpawnManager Awake - Instance set");
    }

    private void OnDestroy()
    {
        // Clear singleton if this is the active instance
        if (Instance == this)
        {
            Instance = null;
        }

        // Remove callbacks if registered
        if (runner != null)
        {
            runner.RemoveCallbacks(this);
        }
    }

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        runner = Runner;

        // Only server handles spawning
        if (Runner.IsServer || Runner.IsSharedModeMasterClient)
        {
            Debug.Log("NetworkedSpawnManager: Registering callbacks on server");
            runner.AddCallbacks(this);
            isInitialized = true;

            // ========================================
            // CRITICAL FIX: REMOVED THE FOREACH LOOP!
            // ========================================
            // We do NOT spawn players here anymore.
            // Photon Fusion will call OnPlayerJoined automatically
            // for each player (including the host).

            Debug.Log("NetworkedSpawnManager ready - waiting for OnPlayerJoined callbacks");
        }
        else
        {
            Debug.Log("NetworkedSpawnManager: Running as client");
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (runner != null)
        {
            runner.RemoveCallbacks(this);
        }
        isInitialized = false;
    }

    #endregion

    #region INetworkRunnerCallbacks - PLAYER CALLBACKS

    /// <summary>
    /// Called when a new player joins the game.
    /// This is where we assign teams and spawn the player character.
    /// </summary>
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"📥 OnPlayerJoined called for Player {player.PlayerId}");

        // Only the server spawns players
        if (!Runner.IsServer && !Runner.IsSharedModeMasterClient)
        {
            Debug.Log("Not server - skipping spawn");
            return;
        }

        // Verify we're initialized
        if (!isInitialized)
        {
            Debug.LogError("NetworkedSpawnManager not initialized! Cannot spawn player.");
            return;
        }

        // ========================================
        // CRITICAL FIX: Check if we already spawned this player
        // ========================================
        if (spawnedPlayers.Contains(player))
        {
            Debug.LogWarning($"⚠️ Player {player.PlayerId} already spawned! Skipping duplicate spawn.");
            return;
        }

        // Check if player already has a team assigned (shouldn't happen, but double-check)
        if (playerTeams.ContainsKey(player))
        {
            Debug.LogWarning($"Player {player.PlayerId} already has a team assigned. Skipping.");
            return;
        }

        // Mark this player as being spawned
        spawnedPlayers.Add(player);

        // Assign team
        int team = AssignTeam(player);

        // Get spawn position
        Vector3 spawnPosition = GetSpawnPosition(team);

        // Spawn the player
        SpawnPlayer(runner, player, spawnPosition, team);
    }

    /// <summary>
    /// Called when a player leaves the game.
    /// Updates team counts for auto-balancing.
    /// </summary>
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"📤 Player {player.PlayerId} left the game");

        // Remove from spawned players tracking
        spawnedPlayers.Remove(player);

        // Update team counts
        if (playerTeams.TryGetValue(player, out int team))
        {
            if (team == 1)
            {
                team1Count--;
            }
            else if (team == 2)
            {
                team2Count--;
            }

            playerTeams.Remove(player);

            Debug.Log($"Removed Player {player.PlayerId} from Team {team}. Team 1: {team1Count}, Team 2: {team2Count}");
        }
    }

    #endregion

    #region Team Assignment Logic

    /// <summary>
    /// Assigns a player to a team, automatically balancing teams.
    /// Always puts new players on the team with fewer players.
    /// </summary>
    private int AssignTeam(PlayerRef player)
    {
        int assignedTeam;

        // Auto-balance: assign to team with fewer players
        if (team1Count <= team2Count)
        {
            assignedTeam = 1;
            team1Count++;
        }
        else
        {
            assignedTeam = 2;
            team2Count++;
        }

        // Store the team assignment
        playerTeams[player] = assignedTeam;

        Debug.Log($"✅ Assigned Player {player.PlayerId} to Team {assignedTeam}. Current counts - Team 1: {team1Count}, Team 2: {team2Count}");

        return assignedTeam;
    }

    /// <summary>
    /// Public method to get a player's team (used by other scripts)
    /// </summary>
    public int GetPlayerTeam(PlayerRef player)
    {
        if (playerTeams.TryGetValue(player, out int team))
        {
            return team;
        }

        Debug.LogWarning($"Player {player.PlayerId} has no team assignment!");
        return 0; // 0 = no team
    }

    #endregion

    #region Spawn Position Logic

    /// <summary>
    /// Gets a random spawn position for the specified team
    /// </summary>
    public Vector3 GetSpawnPosition(int team)
    {
        Transform[] spawnPoints = team == 1 ? team1SpawnPoints : team2SpawnPoints;

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError($"No spawn points assigned for Team {team}!");
            return Vector3.zero;
        }

        // Pick a random spawn point
        int randomIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
        Vector3 position = spawnPoints[randomIndex].position;

        Debug.Log($"📍 Selected spawn position for Team {team}: {position}");

        return position;
    }

    /// <summary>
    /// Gets a spawn position by team name (Team1 or Team2)
    /// </summary>
    public Vector3 GetSpawnPosition(string teamName)
    {
        int teamNumber;

        if (teamName == "Team1")
        {
            teamNumber = 1;
        }
        else if (teamName == "Team2")
        {
            teamNumber = 2;
        }
        else
        {
            Debug.LogError($"Invalid team name: {teamName}. Expected 'Team1' or 'Team2'");
            return Vector3.zero;
        }

        // Use the int version of the method
        return GetSpawnPosition(teamNumber);
    }

    #endregion

    #region Player Spawning

    /// <summary>
    /// Spawns the player character on the network.
    /// </summary>
    private void SpawnPlayer(NetworkRunner runner, PlayerRef player, Vector3 position, int team)
    {
        Debug.Log($"🎯 SPAWNING Player {player.PlayerId} at position {position} for Team {team}");

        // Validate prefab
        if (playerPrefab == null)
        {
            Debug.LogError("Player prefab is not assigned in NetworkedSpawnManager!");
            return;
        }

        // Spawn the player using Fusion's Runner.Spawn method
        NetworkObject playerObject = runner.Spawn(
            playerPrefab,           // The prefab to spawn
            position,               // Where to spawn it
            Quaternion.identity,    // Default rotation (no rotation)
            player,                 // Which player owns this object
            (runner, obj) => OnPlayerSpawned(runner, obj, team) // Callback after spawn
        );

        if (playerObject != null)
        {
            Debug.Log($"✅ Successfully spawned player {player.PlayerId} for Team {team}");
        }
        else
        {
            Debug.LogError($"❌ Failed to spawn player {player.PlayerId}");
        }
    }

    /// <summary>
    /// Called after a player object has been spawned.
    /// Sets the team in PlayerTeamData, which updates PlayerTeamComponent.
    /// </summary>
    private void OnPlayerSpawned(NetworkRunner runner, NetworkObject obj, int team)
    {
        Debug.Log($"🎉 OnPlayerSpawned callback - Setting team to {team}");

        // Get the PlayerTeamData component
        PlayerTeamData teamData = obj.GetComponent<PlayerTeamData>();

        if (teamData != null)
        {
            // Set the team - this will be networked to all clients
            teamData.SetTeam(team);
            Debug.Log($"✅ Team {team} assigned to player via PlayerTeamData");
        }
        else
        {
            Debug.LogError("❌ PlayerTeamData component not found on spawned player!");
        }
    }

    #endregion

    #region INetworkRunnerCallbacks - CONNECTION CALLBACKS

    public void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log("Connected to server");
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
    }

    #endregion

    #region INetworkRunnerCallbacks - INPUT CALLBACKS

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        // Input handling is done per-player, not in spawn manager
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
    {
        // Input missing - ignore or handle as needed
    }

    #endregion

    #region INetworkRunnerCallbacks - SESSION CALLBACKS

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        // Session list updates - for lobby systems
    }

    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
    {
        // Custom authentication responses
    }

    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
        Debug.Log("Host migration occurred");
    }

    #endregion

    #region INetworkRunnerCallbacks - RELIABLE DATA CALLBACKS

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
        // Reliable data received - for custom reliable messages
    }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
    {
        // Progress updates for reliable data transfer
    }

    #endregion

    #region INetworkRunnerCallbacks - SCENE CALLBACKS

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log("Scene load done - NetworkedSpawnManager ready");
    }

    public void OnSceneLoadStart(NetworkRunner runner)
    {
        Debug.Log("Scene load starting");
    }

    #endregion

    #region INetworkRunnerCallbacks - OBJECT CALLBACKS

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
        // Called when an object exits a player's Area of Interest
    }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
        // Called when an object enters a player's Area of Interest
    }

    #endregion

    #region INetworkRunnerCallbacks - OTHER CALLBACKS

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"Shutdown: {shutdownReason}");
    }

    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
    {
        // User simulation messages - for custom networking
    }

    #endregion
}