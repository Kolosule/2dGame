using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;

/// <summary>
/// Manages player spawning for a PvPvE 2D platformer using Photon Fusion.
/// Handles team assignment, spawn positions, and auto-balancing teams.
/// COMPATIBLE WITH FUSION 2.0+ (all required callbacks implemented)
/// </summary>
public class NetworkedSpawnManager : NetworkBehaviour, INetworkRunnerCallbacks
{
    #region Singleton Pattern
    public static NetworkedSpawnManager Instance { get; private set; }
    #endregion

    #region Serialized Fields
    [Header("Player Prefab")]
    [Tooltip("Drag your player prefab here - must have NetworkObject, PlayerTeamData, AND PlayerTeamComponent")]
    [SerializeField] private NetworkObject playerPrefab;

    [Header("Team 1 Spawn Points")]
    [Tooltip("Drag all Team 1 spawn point transforms here")]
    [SerializeField] private Transform[] team1SpawnPoints;

    [Header("Team 2 Spawn Points")]
    [Tooltip("Drag all Team 2 spawn point transforms here")]
    [SerializeField] private Transform[] team2SpawnPoints;
    #endregion

    #region Private Fields
    private Dictionary<PlayerRef, int> playerTeams = new Dictionary<PlayerRef, int>();
    private int team1Count = 0;
    private int team2Count = 0;
    private NetworkRunner runner;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

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

        if (Runner.IsServer)
        {
            runner.AddCallbacks(this);
            Debug.Log("NetworkedSpawnManager initialized on server");
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (runner != null)
        {
            runner.RemoveCallbacks(this);
        }
    }
    #endregion

    #region INetworkRunnerCallbacks - PLAYER CALLBACKS

    /// <summary>
    /// Called when a new player joins the game.
    /// This is where we assign teams and spawn the player character.
    /// </summary>
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer)
            return;

        Debug.Log($"Player {player.PlayerId} joined the game");

        // Step 1: Assign to team
        int assignedTeam = AssignTeam(player);

        // Step 2: Get spawn position
        Vector3 spawnPosition = GetSpawnPosition(assignedTeam);

        // Step 3: Spawn the player
        SpawnPlayer(runner, player, spawnPosition, assignedTeam);
    }

    /// <summary>
    /// Called when a player leaves the game.
    /// We need to clean up their team assignment.
    /// </summary>
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer)
            return;

        if (playerTeams.TryGetValue(player, out int team))
        {
            playerTeams.Remove(player);

            if (team == 1)
                team1Count--;
            else if (team == 2)
                team2Count--;

            Debug.Log($"Player {player.PlayerId} left. Team {team} now has {(team == 1 ? team1Count : team2Count)} players");
        }
    }

    #endregion

    #region INetworkRunnerCallbacks - INPUT CALLBACKS

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        // Input handling - implement if you need custom input
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
    {
        // Called when input is missing - usually can ignore
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
        // Connection approval - can implement custom logic here
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError($"Connect failed: {reason}");
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
        Debug.Log("Scene load done");
    }

    public void OnSceneLoadStart(NetworkRunner runner)
    {
        Debug.Log("Scene load start");
    }

    #endregion

    #region INetworkRunnerCallbacks - OBJECT CALLBACKS

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
        // Called when an object exits a player's Area of Interest
        // Usually can ignore unless you have custom culling logic
    }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
        // Called when an object enters a player's Area of Interest
        // Usually can ignore unless you have custom culling logic
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

        Debug.Log($"Assigned Player {player.PlayerId} to Team {assignedTeam}. Team 1: {team1Count}, Team 2: {team2Count}");

        return assignedTeam;
    }

    #endregion

    #region Spawn Position Logic

    /// <summary>
    /// Gets a spawn position for the specified team.
    /// Randomly selects from available spawn points.
    /// </summary>
    private Vector3 GetSpawnPosition(int team)
    {
        Transform[] spawnPoints = team == 1 ? team1SpawnPoints : team2SpawnPoints;

        // Validate spawn points exist
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError($"No spawn points configured for Team {team}! Using default position.");
            return Vector3.zero;
        }

        // Pick a random spawn point
        int randomIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
        Vector3 position = spawnPoints[randomIndex].position;

        Debug.Log($"Selected spawn position for Team {team}: {position}");
        return position;
    }

    #endregion

    #region Player Spawning

    /// <summary>
    /// Spawns the player character on the network.
    /// </summary>
    private void SpawnPlayer(NetworkRunner runner, PlayerRef player, Vector3 position, int team)
    {
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

        Debug.Log($"Spawned player {player.PlayerId} for Team {team} at position {position}");
    }

    /// <summary>
    /// Called after a player object has been spawned.
    /// Sets the team in PlayerTeamData, which updates PlayerTeamComponent.
    /// </summary>
    private void OnPlayerSpawned(NetworkRunner runner, NetworkObject obj, int team)
    {
        // Get the PlayerRef for this spawned object
        PlayerRef player = obj.InputAuthority;

        // Get the PlayerTeamData component
        PlayerTeamData teamData = obj.GetComponent<PlayerTeamData>();

        if (teamData != null)
        {
            // Set the team - this will sync across network and update PlayerTeamComponent
            teamData.SetTeam(team);
            Debug.Log($"✓ Player {player.PlayerId} team set to {team} via PlayerTeamData");
        }
        else
        {
            Debug.LogError($"PlayerTeamData component not found on player prefab! Add it alongside PlayerTeamComponent.");
        }

        // Verify PlayerTeamComponent exists (for debugging)
        PlayerTeamComponent legacyTeamComponent = obj.GetComponent<PlayerTeamComponent>();
        if (legacyTeamComponent == null)
        {
            Debug.LogWarning("PlayerTeamComponent not found on player prefab. Territorial/damage systems won't work!");
        }
        else
        {
            Debug.Log($"✓ Player has both PlayerTeamData (networking) and PlayerTeamComponent (gameplay)");
        }
    }

    #endregion

    #region Public Helper Methods
    /// <summary>
    /// Gets spawn position by team name (backwards compatible)
    /// </summary>
    public Vector3 GetSpawnPosition(string teamName)
    {
        int team = teamName == "Team1" ? 1 : 2;
        return GetSpawnPosition(team);
    }
    /// <summary>
    /// Gets the team number for a specific player.
    /// Returns -1 if player is not found.
    /// </summary>
    public int GetPlayerTeam(PlayerRef player)
    {
        if (playerTeams.TryGetValue(player, out int team))
        {
            return team;
        }
        return -1;
    }

    /// <summary>
    /// Gets the current count for a specific team.
    /// </summary>
    public int GetTeamCount(int team)
    {
        return team == 1 ? team1Count : team2Count;
    }

    #endregion
}