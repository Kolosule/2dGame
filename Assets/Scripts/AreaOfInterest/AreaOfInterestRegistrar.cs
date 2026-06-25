using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using System;
using UnityEngine;

/// <summary>
/// Server-only owner of all "always-interested" (AoI-exempt) network objects. Holds a set of
/// objects that must replicate to every player regardless of distance and applies
/// NetworkObject.SetPlayerAlwaysInterested for every active player, including players who join
/// later. Static markers (AlwaysInterestedMarker) are discovered at spawn; dynamic targets (the
/// current flag carrier) are added/removed at runtime via AddAlwaysInterested/RemoveAlwaysInterested.
///
/// Place ONE of these on a GameObject in the Gameplay scene. It is a plain MonoBehaviour (not a
/// NetworkBehaviour) and registers itself for runner callbacks so it learns about joiners.
/// </summary>
public class AreaOfInterestRegistrar : MonoBehaviour, INetworkRunnerCallbacks
{
    public static AreaOfInterestRegistrar Instance { get; private set; }

    private NetworkRunner runner;
    private readonly HashSet<NetworkObject> alwaysInterested = new HashSet<NetworkObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (runner != null) runner.RemoveCallbacks(this);
    }

    /// <summary>Server-only: discover static markers and register them for all current players.</summary>
    public void ServerInitialize(NetworkRunner activeRunner)
    {
        runner = activeRunner;
        if (runner == null || !runner.IsServer) return;
        runner.AddCallbacks(this);

        foreach (var marker in FindObjectsByType<AlwaysInterestedMarker>(FindObjectsSortMode.None))
        {
            var obj = marker.GetComponent<NetworkObject>();
            if (obj != null) AddAlwaysInterested(obj);
        }
    }

    /// <summary>Server-only: make <paramref name="obj"/> always-interested for every active player.</summary>
    public void AddAlwaysInterested(NetworkObject obj)
    {
        if (runner == null || !runner.IsServer || obj == null) return;
        if (!alwaysInterested.Add(obj)) return; // already registered
        foreach (var player in runner.ActivePlayers)
            obj.SetPlayerAlwaysInterested(player, true);
    }

    /// <summary>Server-only: stop forcing interest in <paramref name="obj"/> for every active player.</summary>
    public void RemoveAlwaysInterested(NetworkObject obj)
    {
        if (runner == null || !runner.IsServer || obj == null) return;
        if (!alwaysInterested.Remove(obj)) return; // wasn't registered
        foreach (var player in runner.ActivePlayers)
            obj.SetPlayerAlwaysInterested(player, false);
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer) return;
        // A late joiner must immediately be interested in every always-interested object.
        foreach (var obj in alwaysInterested)
            if (obj != null) obj.SetPlayerAlwaysInterested(player, true);
    }

    // --- Unused INetworkRunnerCallbacks members ---
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}
