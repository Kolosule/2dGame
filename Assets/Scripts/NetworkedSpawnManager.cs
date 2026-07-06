using System.Collections.Generic;
using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System;
using System.Linq;

/// <summary>
/// Spawns each player on the team they picked, with no timed poll and no auto-balance race. Team
/// choices are collected by the host during the lobby (see GameNetworkManager / LobbyTeamChoices)
/// BEFORE the Gameplay scene is loaded, so by the time this manager spawns every player's choice is
/// already known authoritatively on the host. A player is spawned as soon as they are an active
/// member of the session AND a lobby choice exists for them; auto-balance remains only as a
/// defensive fallback for a player with no recorded choice (e.g. an unexpected late joiner).
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
    // Auto-balance sentinel: no recorded choice for this player.
    private const int NoTeamChoice = 0;

    private Dictionary<PlayerRef, int> playerTeams = new Dictionary<PlayerRef, int>();
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
        Runner.AddCallbacks(this);

        if (!Object.HasStateAuthority)
            return;

        ValidateSpawnPoints();

        // Area of Interest: hand the registrar the active runner so it can mark the flags/managers
        // (and later the flag carrier) always-interested for every player. No-op if no registrar
        // is present in the scene (AoI simply not configured yet).
        if (AreaOfInterestRegistrar.Instance != null)
            AreaOfInterestRegistrar.Instance.ServerInitialize(Runner);

        // Every player who reached gameplay already submitted a choice in the lobby, so spawn the
        // current roster now. (OnPlayerJoined for these players fired back in the MainMenu before
        // this manager existed, so we reconcile against ActivePlayers here.)
        foreach (var player in Runner.ActivePlayers)
            TrySpawnPlayer(player);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        // Symmetric with the AddCallbacks in Spawned. Without this, a scene unload while the
        // runner survives would leave the runner calling into a destroyed component.
        runner.RemoveCallbacks(this);
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
        TrySpawnPlayer(player);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        // Runner callbacks fire on every peer; only the server owns despawning.
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
            return;

        // Fusion does NOT clean up a leaver's objects in Host/Server mode - that is our job.
        // Drop any flag they were carrying FIRST (while the avatar still exists, so the flag
        // lands at their last position and the carrier-marker cleanup can still run), then
        // despawn the avatar so it doesn't linger as a frozen zombie.
        foreach (var flag in FindObjectsByType<Flag>(FindObjectsSortMode.None))
        {
            if (flag.IsCarriedBy(player))
                flag.DropFlag();
        }

        if (runner.TryGetPlayerObject(player, out NetworkObject playerObject))
            runner.Despawn(playerObject);

        if (playerTeams.TryGetValue(player, out int team))
        {
            if (team == 1)
                team1Count--;
            else if (team == 2)
                team2Count--;

            playerTeams.Remove(player);
        }

        spawnedPlayers.Remove(player);
    }

    /// <summary>
    /// Server-only. Spawns the player once they are active in the session AND a lobby team choice
    /// exists for them. Idempotent for already-spawned players, so it is safe to call from both the
    /// initial roster scan and OnPlayerJoined.
    /// </summary>
    private void TrySpawnPlayer(PlayerRef player)
    {
        if (!Object.HasStateAuthority)
            return;

        if (spawnedPlayers.Contains(player))
            return;

        if (!Runner.ActivePlayers.Contains(player))
        {
            return;
        }

        if (!LobbyTeamChoices.TryGet(player, out int choice))
        {
            // The lobby gate normally guarantees a choice before gameplay loads; reaching here means
            // an unexpected late joiner with no recorded choice. AssignTeam auto-balances them.
            Debug.LogWarning($"⚠️ No lobby team choice for Player {player.PlayerId} - auto-balancing");
            choice = NoTeamChoice;
        }

        spawnedPlayers.Add(player);
        int team = AssignTeam(player, choice);

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


        NetworkObject spawnedObject = Runner.Spawn(
            playerPrefab,
            spawnPosition,
            Quaternion.identity,
            player,
            (runner, obj) => OnPlayerSpawned(runner, obj, team)
        );

        if (spawnedObject == null)
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
        // Register this object as the player's canonical player-object. Fusion replicates the
        // association to every peer, so Runner.TryGetPlayerObject(playerRef) resolves on clients
        // (not just the host). CTF flag carrier resolution (Flag.cs) depends on this; without it
        // clients can't find the carrier GameObject, so head markers and the carried-flag arrow
        // never track the carrier.
        runner.SetPlayerObject(obj.InputAuthority, obj);

        PlayerTeamData teamData = obj.GetComponent<PlayerTeamData>();

        if (teamData != null)
        {
            teamData.SetTeam(TeamUtil.FromNumber(team));
        }
        // Position is set by Runner.Spawn and synced by NetworkRigidbody2D.

        // Initialise the player's buff loadout from their lobby choice (host-authoritative).
        PlayerBuffs buffs = obj.GetComponent<PlayerBuffs>();
        if (buffs != null && LobbyLoadoutChoices.TryGet(obj.InputAuthority, out byte[] order))
            buffs.ServerInitLoadout(order);
        // If no lobby choice, PlayerBuffs.Spawned applies the config default order.
    }
    #endregion

    #region Team Assignment
    private int AssignTeam(PlayerRef player, int choice)
    {

        if (playerTeams.TryGetValue(player, out int existingTeam))
        {
            return existingTeam;
        }

        int team = (choice == 1 || choice == 2) ? choice : NoTeamChoice;

        if (team == NoTeamChoice)
        {
            // Deliberate "no choice made" path - auto-balance onto the smaller team.
            if (allowUnbalancedTeams)
                Debug.LogWarning($"⚠️ No team choice for Player {player.PlayerId} - using auto-balance");

            team = (team1Count <= team2Count) ? 1 : 2;
        }

        playerTeams[player] = team;

        if (team == 1)
            team1Count++;
        else if (team == 2)
            team2Count++;


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
        // Intentionally empty: connection policy lives in ONE place (GameNetworkManager).
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
        // Safety net: reconcile the roster in case any join signal was missed.
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
