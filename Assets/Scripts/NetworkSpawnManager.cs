using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System;
using System.Linq;

/// <summary>
/// Spawns each player only once their team choice is known, via an explicit handshake instead of a
/// timed poll. A client sends its team choice to the state authority (host) over an RPC when its
/// NetworkedSpawnManager spawns into the Gameplay scene; the host spawns the player as soon as BOTH
/// signals are present — the player has joined AND their choice has arrived — regardless of order.
/// This removes the old 0.5s coroutine race and the host-only static TeamSelectionData fallback.
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
    // Auto-balance sentinel: a player explicitly expressing "no preference".
    private const int NoTeamChoice = 0;

    private Dictionary<PlayerRef, int> playerTeams = new Dictionary<PlayerRef, int>();
    private Dictionary<PlayerRef, int> pendingTeamChoices = new Dictionary<PlayerRef, int>();
    private int team1Count = 0;
    private int team2Count = 0;
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

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
    #endregion

    #region Fusion Lifecycle
    public override void Spawned()
    {
        Debug.Log("✅ NetworkedSpawnManager spawned into the Gameplay scene");
        Runner.AddCallbacks(this);

        if (Object.HasStateAuthority)
            ValidateSpawnPoints();

        // Step 1 of the handshake: tell the host which team this peer's local player picked.
        SendLocalTeamChoice();

        // The OnPlayerJoined callbacks for players who joined back in the MainMenu scene fired
        // before this manager existed, so reconcile against the current roster once. TrySpawnPlayer
        // is choice-gated, so this never spawns a player whose choice hasn't arrived yet.
        if (Object.HasStateAuthority)
        {
            foreach (var player in Runner.ActivePlayers)
                TrySpawnPlayer(player);
        }
    }

    private void ValidateSpawnPoints()
    {
        if (team1SpawnPoints == null || team1SpawnPoints.Length == 0)
            Debug.LogError("❌ Team 1 spawn points not assigned!");

        if (team2SpawnPoints == null || team2SpawnPoints.Length == 0)
            Debug.LogError("❌ Team 2 spawn points not assigned!");
    }
    #endregion

    #region Player Spawning
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"🎮 [SPAWN MANAGER] OnPlayerJoined: Player {player.PlayerId}");
        TrySpawnPlayer(player);
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

    /// <summary>
    /// Server-only. Spawns the player the moment BOTH handshake signals are present: the player is
    /// an active member of the session AND we have received their team choice. Safe to call from
    /// either trigger (join callback or choice RPC) and idempotent for already-spawned players.
    /// </summary>
    private void TrySpawnPlayer(PlayerRef player)
    {
        if (!Object.HasStateAuthority)
            return;

        if (spawnedPlayers.Contains(player))
            return;

        if (!Runner.ActivePlayers.Contains(player))
        {
            Debug.Log($"⏳ Player {player.PlayerId} not active yet - waiting to spawn");
            return;
        }

        if (!pendingTeamChoices.TryGetValue(player, out int choice))
        {
            Debug.Log($"⏳ No team choice yet for Player {player.PlayerId} - waiting to spawn");
            return;
        }

        spawnedPlayers.Add(player);
        int team = AssignTeam(player, choice);
        pendingTeamChoices.Remove(player);

        Vector3 spawnPosition = GetSpawnPosition(team);
        SpawnPlayer(Runner, player, spawnPosition, team);
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
            // Roll the bookkeeping back so a later trigger can retry the spawn cleanly.
            spawnedPlayers.Remove(player);
            if (playerTeams.Remove(player))
            {
                if (team == 1) team1Count--;
                else if (team == 2) team2Count--;
            }
        }
    }

    private void OnPlayerSpawned(NetworkRunner runner, NetworkObject obj, int team)
    {
        PlayerTeamData teamData = obj.GetComponent<PlayerTeamData>();

        if (teamData != null)
        {
            teamData.SetTeam(TeamUtil.FromNumber(team));
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

        // NoTeamChoice is a deliberate "no preference -> auto-balance" request; 1/2 are real picks.
        if (teamChoice != NoTeamChoice && teamChoice != 1 && teamChoice != 2)
        {
            Debug.LogError($"❌ Invalid team choice: {teamChoice}");
            return;
        }

        pendingTeamChoices[player] = teamChoice;
        Debug.Log($"✅ Team choice stored for Player {player.PlayerId}");

        // Second handshake signal arrived - spawn now if the player has also joined.
        TrySpawnPlayer(player);
    }

    /// <summary>
    /// Sends this peer's local player's menu choice to the host. Called from Spawned() once the
    /// manager (and therefore the networked RPC channel) exists in the Gameplay scene.
    /// </summary>
    private void SendLocalTeamChoice()
    {
        if (Runner.LocalPlayer == PlayerRef.None)
        {
            // Dedicated server with no local player - nothing to send.
            return;
        }

        int choice = TeamSelectionData.HasChosenTeam()
            ? TeamSelectionData.GetLocalPlayerTeam()
            : NoTeamChoice;

        Debug.Log($"📤 Sending local team choice to host: Team {choice}");
        RPC_SetPlayerTeamChoice(Runner.LocalPlayer, choice);
    }

    private int AssignTeam(PlayerRef player, int choice)
    {
        Debug.Log($"🎲 AssignTeam for Player {player.PlayerId} (choice: {choice})");

        if (playerTeams.TryGetValue(player, out int existingTeam))
        {
            Debug.Log($"♻️ Player {player.PlayerId} rejoining with Team {existingTeam}");
            return existingTeam;
        }

        int team = (choice == 1 || choice == 2) ? choice : NoTeamChoice;

        if (team == NoTeamChoice)
        {
            // Deliberate "no choice made" path - auto-balance onto the smaller team.
            if (!allowUnbalancedTeams)
                Debug.Log($"⚖️ Balancing Player {player.PlayerId} (no choice made)");
            else
                Debug.LogWarning($"⚠️ No team choice for Player {player.PlayerId} - using auto-balance");

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
        // Safety net: reconcile the roster in case any join/choice signal was missed.
        if (Object.HasStateAuthority)
        {
            foreach (var player in Runner.ActivePlayers)
                TrySpawnPlayer(player);
        }
    }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    #endregion
}
