using UnityEngine;
using Fusion;
using Fusion.Addons.Physics;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Match.Core;

/// <summary>
/// Boot + transport + server-side lobby glue. Players land in a single 20-player lobby
/// (session "PvPvERoom"); the server auto-assigns each joiner to the smaller team, tracks
/// nicknames and team switches in LobbyServerState, and broadcasts a full LobbyState snapshot
/// (LobbyProtocol) to every player after each lobby change. The designated host-client
/// (lowest PlayerId — the first joiner) gets the Start button and may start whenever at least
/// one player is connected. In host mode the host's own UI is fed the same snapshot through a
/// local loopback, so both modes share one rendering path (LobbyScreenUI.ApplyLobbyState).
/// Team/loadout results land in LobbyTeamChoices/LobbyLoadoutChoices for NetworkedSpawnManager.
/// </summary>
public class GameNetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("UI References")]
    public MainMenuUI menuUI;
    public LobbyScreenUI lobbyUI;

    [Header("Network Settings")]
    public string sessionName = "PvPvERoom";
    public int gameplaySceneIndex = 1;

    [Tooltip("Session player cap. Fusion refuses connections beyond this count.")]
    public int maxPlayers = 20;

    // Reliable-data channel tags. TEAM is a team-SWITCH request (teams are auto-assigned on join).
    private static readonly ReliableKey TeamChoiceKey = ReliableKey.FromInts(0x54454100, 0x4D, 0, 0); // "TEAM"
    private static readonly ReliableKey LoadoutKey = ReliableKey.FromInts(0x4C4F4144, 0x55, 0, 0);    // "LOAD"
    private static readonly ReliableKey NameKey = ReliableKey.FromInts(0x4E414D45, 0, 0, 0);          // "NAME"
    private static readonly ReliableKey RosterKey = ReliableKey.FromInts(0x524F5354, 0, 0, 0);        // "ROST"
    private static readonly ReliableKey StartMatchKey = ReliableKey.FromInts(0x53545254, 0, 0, 0);    // "STRT"

    private NetworkRunner runner;
    private NetworkSceneManagerDefault sceneManager;
    private PooledNetworkObjectProvider objectProvider;
    private LobbyServerState serverLobby = new LobbyServerState();
    private bool gameStarting = false;

    // Server-only. Token -> state preserved for players who dropped mid-match, held for the REST OF
    // THE MATCH (no timer) and released in BeginReturnToLobby / OnShutdown.
    private ReconnectRegistry reconnectRegistry = new ReconnectRegistry();

    // Server-only. A claimed hold, parked between OnPlayerJoined (which reclaims it) and the spawn
    // that consumes it — the two can be a whole scene load apart for a mid-match rejoin.
    private readonly Dictionary<PlayerRef, ReconnectHeldSlot> pendingRestores =
        new Dictionary<PlayerRef, ReconnectHeldSlot>();

    // The session name we are actually connected to. Reconnect retries against THIS rather than
    // re-reading the sessionName field, so a future server browser cannot silently send a
    // reconnecting player to the wrong server. See the spec's session-identity section.
    private string connectedSessionName;

    // Last roster snapshot decoded on a client. Cached so the return-to-lobby scene load can
    // re-apply it if the broadcast arrived before lobbyUI was re-acquired (avoids empty roster).
    private LobbyStateSnapshot pendingLobbySnapshot;

    public static GameNetworkManager Instance { get; private set; }
    public int menuSceneIndex = 0;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // A second GameNetworkManager rode in with a reloaded menu scene. Kill it; the
            // DontDestroyOnLoad original owns the runner and the lobby state.
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        if (Instance != this) return;
        DontDestroyOnLoad(gameObject);
        runner = gameObject.AddComponent<NetworkRunner>();

        // Fusion steps Physics2D inside the network tick (required for NetworkRigidbody2D prediction).
        // ClientPhysicsSimulation defaults to Disabled, which means CLIENTS never call
        // Physics.Simulate() and so never integrate their own rigidbody forward — SimulateForward
        // enables client-side prediction of the local player's position.
        var simulatePhysics = gameObject.AddComponent<RunnerSimulatePhysics2D>();
        simulatePhysics.ClientPhysicsSimulation = ClientPhysicsSimulation.SimulateForward;

        // Pool high-churn networked prefabs (projectiles) instead of Instantiate/Destroy each shot.
        objectProvider = gameObject.AddComponent<PooledNetworkObjectProvider>();

        // One scene manager for the lifetime of the runner. Created here (not per StartGame call)
        // so a failed connect + retry doesn't stack duplicate components on this GameObject.
        sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();

        // Register the single input source.
        var inputProvider = gameObject.AddComponent<NetworkInputProvider>();
        runner.AddCallbacks(inputProvider);

        // Receive lobby callbacks (player join/leave, reliable lobby data).
        runner.AddCallbacks(this);

        LobbyTeamChoices.Clear();
        LobbyNicknameChoices.Clear();
        LobbyLoadoutChoices.Clear();
        serverLobby = new LobbyServerState();
        gameStarting = false;

        var boot = NetworkBootMode.Resolve(
            Application.isBatchMode,
            System.Environment.GetCommandLineArgs());

        if (boot == NetworkBootKind.DedicatedServer)
        {
            StartServer();
            return; // headless server: no menu UI
        }

        if (menuUI == null) Debug.LogError("❌ MainMenuUI not assigned!");
        if (lobbyUI == null) Debug.LogError("❌ LobbyScreenUI not assigned!");
    }

    // ============================
    // Connection entry points (menuUI buttons call these)
    // ============================

    public async void StartHost()
    {
        var args = new StartGameArgs()
        {
            GameMode = GameMode.Host, // AutoHostOrClient creates separate sessions — never use it here
            SessionName = sessionName,
            PlayerCount = maxPlayers,
            SceneManager = sceneManager,
            ObjectProvider = objectProvider,
            // Stable per-install identity, so a rejoining player is recognisable across a new
            // PlayerRef. Sent on every connect — there is no separate "reconnect mode" on the wire.
            ConnectionToken = PlayerIdentity.TokenBytes
        };

        var result = await runner.StartGame(args);

        if (result.Ok)
        {
            connectedSessionName = args.SessionName;
            EnterLobbyUI();
        }
        else
        {
            Debug.LogError($"❌ Failed to start host: {result.ShutdownReason}");
            if (menuUI != null)
            {
                menuUI.ShowStatus($"Failed to start host: {result.ShutdownReason}");
                menuUI.SetBusy(false);
            }
        }
    }

    public async void StartClient()
    {
        var args = new StartGameArgs()
        {
            GameMode = GameMode.Client,
            SessionName = sessionName,
            PlayerCount = maxPlayers,
            SceneManager = sceneManager,
            ObjectProvider = objectProvider,
            // Stable per-install identity, so a rejoining player is recognisable across a new
            // PlayerRef. Sent on every connect — there is no separate "reconnect mode" on the wire.
            ConnectionToken = PlayerIdentity.TokenBytes
        };

        var result = await runner.StartGame(args);

        if (result.Ok)
        {
            connectedSessionName = args.SessionName;
            EnterLobbyUI();
        }
        else
        {
            Debug.LogError($"❌ Failed to connect: {result.ShutdownReason}");
            if (menuUI != null)
            {
                menuUI.ShowStatus($"Failed to connect: {result.ShutdownReason}");
                menuUI.SetBusy(false);
            }
        }
    }

    async void StartServer()
    {
        var args = new StartGameArgs()
        {
            GameMode = GameMode.Server,
            SessionName = sessionName,
            PlayerCount = maxPlayers,
            SceneManager = sceneManager,
            ObjectProvider = objectProvider
        };

        var result = await runner.StartGame(args);

        if (result.Ok)
            Debug.Log("✅ Dedicated server started — waiting for players.");
        else
            Debug.LogError($"❌ Server failed to start: {result.ShutdownReason}");
    }

    private void EnterLobbyUI()
    {
        if (menuUI != null) menuUI.Hide();
        if (lobbyUI != null) lobbyUI.Show();
        SendLocalNickname();
    }

    /// <summary>
    /// Pushes the local player's nickname to the lobby. Host mode records directly and
    /// re-broadcasts; clients send over reliable data. An empty nickname keeps the server's
    /// "Player N" placeholder (nothing is sent — the decoder rejects empty payloads anyway).
    /// </summary>
    private void SendLocalNickname()
    {
        if (runner == null || !runner.IsRunning) return;
        string nick = menuUI != null ? menuUI.Nickname : "";

        if (runner.IsServer)
        {
            if (runner.LocalPlayer != PlayerRef.None)
            {
                serverLobby.SetNickname(runner.LocalPlayer.PlayerId, nick);
                BroadcastLobby(); // refresh even if unchanged so the just-shown UI gets a snapshot
            }
        }
        else if (nick.Length > 0)
        {
            runner.SendReliableDataToServer(NameKey, LobbyProtocol.EncodeNickname(nick));
        }
    }

    // ============================
    // Lobby actions (lobbyUI buttons call these)
    // ============================

    /// <summary>Ask to move to the given team (1|2). Server-side it must actually change something.</summary>
    public void RequestTeamSwitch(int teamNumber)
    {
        if (teamNumber != 1 && teamNumber != 2) return;
        if (runner == null || !runner.IsRunning || gameStarting) return;

        if (runner.IsServer)
        {
            if (runner.LocalPlayer == PlayerRef.None) return; // dedicated server is not a player
            if (serverLobby.SwitchTeam(runner.LocalPlayer.PlayerId, teamNumber))
            {
                LobbyTeamChoices.Set(runner.LocalPlayer, teamNumber);
                BroadcastLobby();
            }
        }
        else
        {
            runner.SendReliableDataToServer(TeamChoiceKey, new byte[] { (byte)teamNumber });
        }
    }

    /// <summary>
    /// Called by LobbyScreenUI when the local player reorders their buffs. Records on the host
    /// (directly if we are the host, else over reliable-data).
    /// </summary>
    public void SubmitLocalLoadoutChoice(byte[] order)
    {
        if (runner == null || !runner.IsRunning) return;

        // A zero-length reliable payload trips a Fusion assert on the real socket path
        // (only reproduces on remote clients). Treat null/empty as "no custom loadout".
        if (order == null || order.Length == 0) return;

        if (runner.IsServer)
            LobbyLoadoutChoices.Set(runner.LocalPlayer, order);
        else
            runner.SendReliableDataToServer(LoadoutKey, order);
    }

    /// <summary>
    /// Called when the local player clicks Start. Host mode starts directly; on a dedicated
    /// server the designated host-client asks the server, which re-validates.
    /// </summary>
    public void RequestStartMatch()
    {
        if (runner == null || !runner.IsRunning || gameStarting) return;

        if (runner.IsServer)
        {
            if (!LobbyHostPolicy.CanStart(serverLobby.PlayerCount)) return;
            gameStarting = true;
            LoadGameplayScene();
        }
        else
        {
            runner.SendReliableDataToServer(StartMatchKey, new byte[] { 1 });
        }
    }

    // ============================
    // Server-side lobby state + broadcast
    // ============================

    /// <summary>
    /// Server-only: seat the player in the lobby roster and mirror the result into the handoff
    /// dictionaries NetworkedSpawnManager reads. Runs for mid-match late joiners too.
    ///
    /// A reconnecting player (a token matching a held slot) reclaims their held team, name, and
    /// loadout instead of being auto-assigned, and their progression is parked in pendingRestores
    /// for the spawn to consume. Restoration deliberately flows through the SAME three dictionaries
    /// as a normal join, so the spawn path needs no reconnect-specific branch and the lobby
    /// team-pick is skipped implicitly rather than by a special case.
    /// </summary>
    private void ServerHandleJoin(NetworkRunner runner, PlayerRef player)
    {
        string token = IdentityTokenCodec.ToHex(runner.GetPlayerConnectionToken(player));

        if (reconnectRegistry.TryClaim(token, out ReconnectHeldSlot held))
        {
            int heldTeam = serverLobby.PlayerJoinedOnTeam(player.PlayerId, held.Team);
            serverLobby.SetNickname(player.PlayerId, held.DisplayName);

            LobbyTeamChoices.Set(player, heldTeam);
            LobbyNicknameChoices.Set(player, held.DisplayName);
            if (held.LoadoutOrder != null && held.LoadoutOrder.Length > 0)
                LobbyLoadoutChoices.Set(player, held.LoadoutOrder);

            pendingRestores[player] = held;

            Debug.Log($"🔄 Player {player.PlayerId} reconnected — restored to team {heldTeam} " +
                      $"with {held.TotalDepositedValue} deposited.");

            if (!gameStarting) BroadcastLobby();
            return;
        }

        int team = serverLobby.PlayerJoined(player.PlayerId);
        LobbyTeamChoices.Set(player, team);
        LobbyNicknameChoices.Set(player, LobbyProtocol.PlaceholderName(player.PlayerId));
        if (!gameStarting) BroadcastLobby();
    }

    /// <summary>
    /// Server-only. Hands the spawn path a reconnecting player's parked state exactly once. Returns
    /// false (and null) for a normal joiner.
    /// </summary>
    public bool TryConsumeRestore(PlayerRef player, out ReconnectHeldSlot slot)
    {
        if (!pendingRestores.TryGetValue(player, out slot)) return false;
        pendingRestores.Remove(player);
        return true;
    }

    /// <summary>
    /// Server-only. Puts a consumed restore back when the spawn it was consumed for failed, so the
    /// spawn manager's existing retry path still restores the player.
    /// </summary>
    public void ReturnRestore(PlayerRef player, ReconnectHeldSlot slot)
    {
        if (slot != null) pendingRestores[player] = slot;
    }

    /// <summary>
    /// Server-only: encode one snapshot and send it to every remote player; a host-as-player
    /// applies it to its own LobbyScreenUI directly (same snapshot, no wire trip).
    /// </summary>
    private void BroadcastLobby()
    {
        if (runner == null || !runner.IsServer) return;

        var snap = serverLobby.BuildSnapshot(maxPlayers);
        byte[] payload = LobbyProtocol.EncodeLobbyState(snap);

        foreach (var p in runner.ActivePlayers)
        {
            if (p == runner.LocalPlayer) continue; // local loopback below instead
            runner.SendReliableDataToPlayer(p, RosterKey, payload);
        }

        if (runner.LocalPlayer != PlayerRef.None && lobbyUI != null)
            lobbyUI.ApplyLobbyState(snap, runner.LocalPlayer.PlayerId);
    }

    private async void LoadGameplayScene()
    {
        if (lobbyUI != null) lobbyUI.Hide();
        await runner.LoadScene(SceneRef.FromIndex(gameplaySceneIndex));
    }

    /// <summary>
    /// Server-only. Ends the match by reloading the MainMenu scene and re-showing the persisted
    /// lobby. Resets the gameStarting latch so the host can Start the next match. Called by
    /// MatchManager when entering Intermission.
    /// </summary>
    public void BeginReturnToLobby()
    {
        if (runner == null || !runner.IsServer) return;

        // The match is over: every hold expires here. This is the whole reason the reconnect design
        // needs no grace TickTimer — the match boundary is the timer.
        reconnectRegistry.Clear();
        pendingRestores.Clear();

        gameStarting = false;
        _ = runner.LoadScene(SceneRef.FromIndex(menuSceneIndex));
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (runner != null) runner.Shutdown();
    }

    void OnApplicationQuit()
    {
        if (runner != null) runner.Shutdown();
    }

    // ============================
    // Fusion callbacks
    // ============================

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        // DO NOT SPAWN PLAYER HERE — NetworkedSpawnManager in the Gameplay scene handles it.
        if (runner.IsServer)
            ServerHandleJoin(runner, player);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (runner.IsServer)
        {
            // FIRST: preserve their state while the lobby records and the avatar still exist.
            ServerCaptureForReconnect(runner, player);

            serverLobby.PlayerLeft(player.PlayerId);
            LobbyTeamChoices.Remove(player);
            LobbyNicknameChoices.Remove(player);
            LobbyLoadoutChoices.Remove(player);
            pendingRestores.Remove(player);
            if (!gameStarting) BroadcastLobby();
        }
    }

    /// <summary>
    /// Server-only. Preserves a mid-match leaver's earned state, keyed by their connection token, so
    /// a rejoin can restore it (docs/superpowers/specs/2026-07-29-reconnection-design.md).
    ///
    /// ORDERING INVARIANT: this must run while the leaver's avatar still exists, because the
    /// deposited value is read off it. GameNetworkManager registers its callbacks in Start() on the
    /// persistent object, long before NetworkedSpawnManager.Spawned() registers the callback that
    /// despawns the avatar, so this always runs first on the same runner. The existing JOIN path
    /// already depends on the same invariant (ServerHandleJoin must fill LobbyTeamChoices before
    /// TrySpawnPlayer reads it), so it is load-bearing in both directions — the LogError below is
    /// the tripwire if it ever changes.
    /// </summary>
    private void ServerCaptureForReconnect(NetworkRunner runner, PlayerRef player)
    {
        // Only while a match is actually being played. A lobby drop (no MatchManager at all) and a
        // PostMatch/Intermission drop release fully, exactly as before this feature: there is
        // nothing worth preserving, and reserving seats in a lobby would lock out real joiners.
        if (MatchManager.Instance == null) return;
        if (!MatchRules.PreservesDisconnectState(MatchManager.Instance.Phase)) return;

        // GetPlayerConnectionToken is server-only and returns null when the client sent no token;
        // ToHex turns anything malformed into "", which the registry refuses to key on.
        string token = IdentityTokenCodec.ToHex(runner.GetPlayerConnectionToken(player));
        if (string.IsNullOrEmpty(token)) return;

        // Dropped again before their avatar ever spawned (e.g. during the gameplay scene load):
        // re-hold the still-unconsumed restore instead of reading an avatar that never existed.
        if (pendingRestores.TryGetValue(player, out ReconnectHeldSlot pending))
        {
            reconnectRegistry.Capture(token, pending);
            return;
        }

        var slot = new ReconnectHeldSlot
        {
            Team = LobbyTeamChoices.TryGet(player, out int team) ? team : serverLobby.TeamOf(player.PlayerId),
            DisplayName = LobbyNicknameChoices.TryGet(player, out string name) && !string.IsNullOrEmpty(name)
                ? name
                : LobbyProtocol.PlaceholderName(player.PlayerId),
            // Without this the rejoiner silently reverts to BuffLoadoutConfig's default priority
            // order, because LobbyLoadoutChoices.Remove runs moments from now.
            LoadoutOrder = LobbyLoadoutChoices.TryGet(player, out byte[] order) ? order : null
        };

        if (runner.TryGetPlayerObject(player, out NetworkObject avatar) && avatar != null)
        {
            PlayerBuffs buffs = avatar.GetComponent<PlayerBuffs>();
            if (buffs != null) slot.TotalDepositedValue = buffs.TotalDeposited;
        }
        else
        {
            Debug.LogError($"❌ ServerCaptureForReconnect: no avatar for Player {player.PlayerId} — " +
                           "callback order changed; their deposited value will NOT be restored.");
        }

        if (MatchStatsManager.Instance != null &&
            MatchStatsManager.Instance.TryGetEntry(player.PlayerId, out PlayerStatEntry entry))
        {
            slot.Kills = entry.Kills;
            slot.Deaths = entry.Deaths;
            slot.Captures = entry.Captures;
            slot.CoinsDeposited = entry.CoinsDeposited;
            slot.FlagCarrySeconds = entry.FlagCarrySeconds;
            slot.FlagReturns = entry.FlagReturns;
        }

        reconnectRegistry.Capture(token, slot);
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        // Pooled instances belong to the session that just died — drop them so a
        // restarted session doesn't reuse destroyed objects.
        if (objectProvider != null)
            objectProvider.ClearPools();

        // The live-coin registry is server-only static state; clear it so a restarted session
        // doesn't inherit stale (destroyed) coin references or a bogus live count.
        CoinRegistry.Clear();
        reconnectRegistry.Clear();
        pendingRestores.Clear();

        if (lobbyUI != null) lobbyUI.Hide();
        if (menuUI != null)
        {
            menuUI.Show();
            menuUI.ShowStatus($"Disconnected: {shutdownReason}");
        }

        LobbyTeamChoices.Clear();
        LobbyNicknameChoices.Clear();
        LobbyLoadoutChoices.Clear();
        serverLobby = new LobbyServerState();
        gameStarting = false;
    }

    public void OnConnectedToServer(NetworkRunner runner) { }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        // The ONLY place that accepts/refuses connections (future ban list / lockout goes here).
        //
        // A held (disconnected) slot RESERVES its seat, which Fusion's PlayerCount cannot express —
        // Fusion frees its own slot the instant a player drops. So the real cap is enforced here,
        // while StartGameArgs.PlayerCount stays at maxPlayers as a backstop. Since every held slot
        // was previously an active one, active + held <= maxPlayers holds by construction.
        bool known = reconnectRegistry.Has(IdentityTokenCodec.ToHex(token));

        if (ReconnectPolicy.CanAdmit(known, runner.ActivePlayers.Count(), reconnectRegistry.HeldCount, maxPlayers))
        {
            request.Accept();
        }
        else
        {
            Debug.Log($"🚪 Refusing connection: session full " +
                      $"({runner.ActivePlayers.Count()} active + {reconnectRegistry.HeldCount} held / {maxPlayers}).");
            request.Refuse();
        }
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError($"❌ Connection failed: {reason}");
        if (menuUI != null)
        {
            menuUI.ShowStatus($"Connection failed: {reason}");
            menuUI.SetBusy(false);
        }
    }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
        if (runner.IsServer)
        {
            if (key == TeamChoiceKey)
            {
                if (gameStarting || data.Count < 1 || data.Array == null) return;
                int team = data.Array[data.Offset];
                if (serverLobby.SwitchTeam(player.PlayerId, team))
                {
                    LobbyTeamChoices.Set(player, team);
                    BroadcastLobby();
                }
                return;
            }

            if (key == NameKey)
            {
                if (data.Array == null) return;
                if (!LobbyProtocol.TryDecodeNickname(data.Array, data.Offset, data.Count, out string name)) return;
                if (serverLobby.SetNickname(player.PlayerId, name))
                {
                    LobbyNicknameChoices.Set(player, name);
                    if (!gameStarting) BroadcastLobby();
                }
                return;
            }

            if (key == LoadoutKey)
            {
                if (data.Count < 1 || data.Array == null) return;
                var order = new byte[data.Count];
                Array.Copy(data.Array, data.Offset, order, 0, data.Count);
                LobbyLoadoutChoices.Set(player, order);
                return;
            }

            if (key == StartMatchKey)
            {
                // Only the designated host-client may start, and only when the gate allows it.
                if (!gameStarting
                    && player.PlayerId == serverLobby.CurrentHostId()
                    && LobbyHostPolicy.CanStart(serverLobby.PlayerCount))
                {
                    gameStarting = true;
                    LoadGameplayScene();
                }
                return;
            }

            return;
        }

        // ---- Client ----
        if (key == RosterKey && data.Array != null)
        {
            if (LobbyProtocol.TryDecodeLobbyState(data.Array, data.Offset, data.Count, out var snap))
            {
                pendingLobbySnapshot = snap;
                if (lobbyUI != null)
                    lobbyUI.ApplyLobbyState(snap, runner.LocalPlayer.PlayerId);
            }
        }
    }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        // A real dedicated server (no local player) should not render or play audio. -nographics
        // already suppresses rendering; disabling cameras/listeners avoids per-frame work and
        // AudioListener warnings on the headless build.
        if (runner.IsServer && runner.LocalPlayer == PlayerRef.None)
        {
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                cam.enabled = false;
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                listener.enabled = false;
        }

        // Only care about arriving back in the menu scene (the return-to-lobby path). The gameplay
        // load has a different build index and is handled by the gameplay-side managers.
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex != menuSceneIndex)
            return;

        // The persistent GameNetworkManager's serialized menu/lobby refs died with the previous
        // menu scene instance; re-acquire the new ones.
        menuUI = FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
        lobbyUI = FindFirstObjectByType<LobbyScreenUI>(FindObjectsInactive.Include);

        // The reloaded scene's UI holds serialized refs to the duplicate GameNetworkManager that
        // the dup-guard just destroyed; re-point them at this persistent instance or their buttons
        // call into a destroyed object and do nothing.
        if (menuUI != null) menuUI.SetNetworkManager(this);
        if (lobbyUI != null) lobbyUI.SetNetworkManager(this);

        if (menuUI != null) menuUI.Hide();   // skip the Join/Host screen — we are still connected
        if (lobbyUI != null) lobbyUI.Show();

        // Re-apply the last roster snapshot in case it arrived before we re-acquired lobbyUI on
        // this return-to-lobby scene load (otherwise the returning client shows an empty roster).
        if (lobbyUI != null && pendingLobbySnapshot != null)
            lobbyUI.ApplyLobbyState(pendingLobbySnapshot, runner.LocalPlayer.PlayerId);

        // Server re-broadcasts the persisted roster so every client's lobby repopulates.
        if (runner.IsServer) BroadcastLobby();
    }

    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
}

/// <summary>
/// Per-player team assignments collected during the lobby (auto-assigned on join, updated on
/// switch), keyed by PlayerRef. Lives on the host/server only and survives the menu -> gameplay
/// scene load. NetworkedSpawnManager reads this to spawn each player on the right team.
/// </summary>
public static class LobbyTeamChoices
{
    private static readonly Dictionary<PlayerRef, int> choices = new Dictionary<PlayerRef, int>();

    public static void Set(PlayerRef player, int team) => choices[player] = team;
    public static bool Has(PlayerRef player) => choices.ContainsKey(player);
    public static bool TryGet(PlayerRef player, out int team) => choices.TryGetValue(player, out team);
    public static void Remove(PlayerRef player) => choices.Remove(player);
    public static void Clear() => choices.Clear();
    public static int Count => choices.Count;
}

/// <summary>
/// Per-player display name collected during the lobby (placeholder on join, updated on nickname
/// change), keyed by PlayerRef, parallel to LobbyTeamChoices. Survives the menu -> gameplay scene
/// load. NetworkedSpawnManager reads this to register each player's MatchStatsManager entry.
/// </summary>
public static class LobbyNicknameChoices
{
    private static readonly Dictionary<PlayerRef, string> choices = new Dictionary<PlayerRef, string>();

    public static void Set(PlayerRef player, string name) => choices[player] = name;
    public static bool TryGet(PlayerRef player, out string name) => choices.TryGetValue(player, out name);
    public static void Remove(PlayerRef player) => choices.Remove(player);
    public static void Clear() => choices.Clear();
}

/// <summary>
/// Per-player buff loadout (priority order as BuffId bytes) collected during the lobby, parallel
/// to LobbyTeamChoices. Read by NetworkedSpawnManager on the host to initialise each player's
/// PlayerBuffs. A missing entry falls back to the BuffLoadoutConfig default order.
/// </summary>
public static class LobbyLoadoutChoices
{
    private static readonly Dictionary<PlayerRef, byte[]> choices = new Dictionary<PlayerRef, byte[]>();

    public static void Set(PlayerRef player, byte[] order) => choices[player] = order;
    public static bool TryGet(PlayerRef player, out byte[] order) => choices.TryGetValue(player, out order);
    public static void Remove(PlayerRef player) => choices.Remove(player);
    public static void Clear() => choices.Clear();
}
