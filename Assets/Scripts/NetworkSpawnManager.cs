using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System;
using System.Linq;

/// <summary>
/// FINAL VERSION - Fixed GetSpawnPosition() accessibility
/// Made GetSpawnPosition() public so other scripts can access it
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

    [Header("Team Settings")]
    [Tooltip("Allow unbalanced teams (players can choose any team)")]
    [SerializeField] private bool allowUnbalancedTeams = true;

    [Header("Debug Settings")]
    [SerializeField] private bool verboseLogging = true;
    #endregion

    #region Private Fields
    private Dictionary<PlayerRef, int> playerTeams = new Dictionary<PlayerRef, int>();
    private Dictionary<PlayerRef, int> pendingTeamChoices = new Dictionary<PlayerRef, int>();
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
            Debug.Log("✅ NetworkedSpawnManager STARTED");
            runner.AddCallbacks(this);
            isInitialized = true;
            StartCoroutine(DelayedPlayerCheck());
        }
        else
        {
            Debug.LogError("❌ NetworkRunner NOT FOUND!");
        }
    }

    private System.Collections.IEnumerator DelayedPlayerCheck()
    {
        yield return new WaitForSeconds(0.5f);
        CheckForExistingPlayers();
    }

    private void CheckForExistingPlayers()
    {
        if (runner == null || !isInitialized) return;

        Debug.Log($"🔍 Checking for existing players...");
        int playerCount = 0;

        foreach (var playerRef in runner.ActivePlayers)
        {
            if (!spawnedPlayers.Contains(playerRef))
            {
                Debug.Log($"📋 Found unspawned player: {playerRef.PlayerId}");
                HandlePlayerJoined(playerRef);
                playerCount++;
            }
        }

        ValidateSpawnPoints();
        Debug.Log($"✅ Processed {playerCount} players");
    }

    private void ValidateSpawnPoints()
    {
        if (team1SpawnPoints == null || team1SpawnPoints.Length == 0)
        {
            Debug.LogError("❌ Team 1 spawn points not assigned!");
        }

        if (team2SpawnPoints == null || team2SpawnPoints.Length == 0)
        {
            Debug.LogError("❌ Team 2 spawn points not assigned!");
        }
    }
    #endregion

    #region Player Spawning
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"🎮 [SPAWN MANAGER] OnPlayerJoined: Player {player.PlayerId}");
        HandlePlayerJoined(player);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"👋 [SPAWN MANAGER] OnPlayerLeft: Player {player.PlayerId}");

        if (playerTeams.TryGetValue(player, out int team))
        {
            if (team == 1)
                team1Count--;
            else if (team == 2)
                team2Count--;

            playerTeams.Remove(player);
            Debug.Log($"✅ Removed Player {player.PlayerId} from Team {team}");
            Debug.Log($"📊 Updated counts - Team 1: {team1Count}, Team 2: {team2Count}");
        }

        spawnedPlayers.Remove(player);
        pendingTeamChoices.Remove(player);
    }

    private void HandlePlayerJoined(PlayerRef player)
    {
        if (!runner.IsServer)
        {
            Debug.Log($"⏭️ We're a client - not spawning");
            return;
        }

        if (!isInitialized)
        {
            Debug.LogError("❌ NetworkedSpawnManager not initialized!");
            return;
        }

        if (spawnedPlayers.Contains(player))
        {
            Debug.LogWarning($"⚠️ Player {player.PlayerId} already spawned!");
            return;
        }

        spawnedPlayers.Add(player);
        int team = AssignTeam(player);
        Vector3 spawnPosition = GetSpawnPosition(team);
        SpawnPlayer(runner, player, spawnPosition, team);
    }

    private void SpawnPlayer(NetworkRunner runner, PlayerRef player, Vector3 spawnPosition, int team)
    {
        if (playerPrefab == null)
        {
            Debug.LogError("❌ Player prefab not assigned!");
            return;
        }

        Debug.Log($"🎯 SPAWNING Player {player.PlayerId} on Team {team} at {spawnPosition}");

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
            spawnedPlayers.Remove(player);
        }
    }

    private void OnPlayerSpawned(NetworkRunner runner, NetworkObject obj, int team)
    {
        PlayerTeamData teamData = obj.GetComponent<PlayerTeamData>();

        if (teamData != null)
        {
            teamData.SetTeam(team);
            Debug.Log($"✅ Team {team} assigned");
        }
        // Position is set by Runner.Spawn and synced by NetworkRigidbody2D.
    }
    #endregion

    #region Team Assignment
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_SetPlayerTeamChoice(PlayerRef player, int teamChoice)
    {
        Debug.Log($"🎯 [SERVER] Received team choice from Player {player.PlayerId}: Team {teamChoice}");

        if (teamChoice != 1 && teamChoice != 2)
        {
            Debug.LogError($"❌ Invalid team choice: {teamChoice}");
            return;
        }

        pendingTeamChoices[player] = teamChoice;
        Debug.Log($"✅ Team choice stored for Player {player.PlayerId}");
    }

    public void SetLocalPlayerTeamChoice(int teamChoice)
    {
        if (runner == null || runner.LocalPlayer == null)
        {
            Debug.LogError("❌ Cannot set team choice - no runner or local player!");
            return;
        }

        Debug.Log($"📤 [CLIENT] Sending team choice to server: Team {teamChoice}");
        RPC_SetPlayerTeamChoice(runner.LocalPlayer, teamChoice);
    }

    private int AssignTeam(PlayerRef player)
    {
        Debug.Log($"🎲 AssignTeam for Player {player.PlayerId}");

        if (playerTeams.TryGetValue(player, out int existingTeam))
        {
            Debug.Log($"♻️ Player {player.PlayerId} rejoining with Team {existingTeam}");
            return existingTeam;
        }

        int team = 0;

        if (pendingTeamChoices.TryGetValue(player, out int chosenTeam))
        {
            team = chosenTeam;
            pendingTeamChoices.Remove(player);
            Debug.Log($"🎯 Using player's network choice: Team {team}");
        }
        else if (TeamSelectionData.HasChosenTeam())
        {
            team = TeamSelectionData.GetLocalPlayerTeam();
            TeamSelectionData.ClearTeamSelection();
            Debug.Log($"🎯 Using local team choice: Team {team}");

            if (team != 1 && team != 2)
            {
                Debug.LogError($"❌ Invalid team: {team}");
                team = 0;
            }
        }

        if (team == 0)
        {
            if (allowUnbalancedTeams)
            {
                Debug.LogWarning($"⚠️ No team choice for Player {player.PlayerId} - using auto-balance");
            }

            team = (team1Count <= team2Count) ? 1 : 2;
            Debug.Log($"⚖️ Auto-balanced to Team {team} (T1: {team1Count}, T2: {team2Count})");
        }

        playerTeams[player] = team;

        if (team == 1)
            team1Count++;
        else if (team == 2)
            team2Count++;

        Debug.Log($"✅ Player {player.PlayerId} assigned to Team {team}");
        Debug.Log($"📊 Team counts - Team 1: {team1Count}, Team 2: {team2Count}");

        return team;
    }

    /// <summary>
    /// ⭐ MADE PUBLIC - Other scripts need to access this for respawning
    /// </summary>
    public Vector3 GetSpawnPosition(int team)
    {
        Transform[] spawnPoints = (team == 1) ? team1SpawnPoints : team2SpawnPoints;

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError($"❌ No spawn points for Team {team}!");
            return Vector3.zero;
        }

        int randomIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
        return spawnPoints[randomIndex].position;
    }
    #endregion

    #region INetworkRunnerCallbacks
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
        Debug.Log("🎬 Scene load complete");
        CheckForExistingPlayers();
    }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    #endregion
}