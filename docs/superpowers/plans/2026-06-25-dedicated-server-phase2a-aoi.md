# Dedicated Server — Phase 2a: Area of Interest Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cut per-client bandwidth at 20 players by enabling Fusion Area of Interest so each client only receives network objects near its player — without breaking the CTF HUD, score, or the flag-direction arrow for distant/flag-carrying players.

**Architecture:** On the dedicated server, each player contributes a spatial interest region around itself every tick (`AddPlayerAreaOfInterest`). Objects that must replicate regardless of distance (both flags, the CTF/score managers, home bases) are marked **always-interested** for every player by a single server-side registrar that also covers late joiners. Because the flag-direction HUD and carried-flag position depend on the carrier's player object being present, the registrar also takes **dynamic** always-interest targets: the `Flag` adds its carrier on pickup and removes it on drop/return. The replication-features config flip that activates AoI lands LAST, after these safety nets exist, so the game never runs in a state where everything is culled.

**Tech Stack:** Unity, Photon Fusion 2 (Server mode, server-authoritative), C#.

## Global Constraints

- Photon **Fusion 2**, dedicated `GameMode.Server` (server is state authority for all objects); tick rate **64**; `PlayerCount 20`.
- `AddPlayerAreaOfInterest(player, pos, radius)` must be called **every FixedUpdateNetwork, on the server only** (per Fusion docs); it is re-evaluated per tick.
- `SetPlayerAlwaysInterested(player, bool)` may only be called by the object's **State Authority** (the server). It is per-player — there is no "all players" overload, so future joiners must be handled explicitly.
- Enabling AoI culls any object that is neither in a player's interest region nor explicitly always-interested for that player. The `ReplicationFeatures` flip (Task 4) MUST be the last change, after Tasks 1–3 land.
- Positions sync via `NetworkRigidbody2D` (no `NetworkTransform`) — a culled player object has NO position on the culling client, which is why the flag carrier needs dynamic always-interest.
- Server authority unchanged; do not move authority to clients. Friends invite-only, no anti-cheat.
- Unity cannot compile/build/run or run the Test Runner in this authoring environment — all compile checks, scene/prefab wiring, and multi-peer bandwidth verification are performed by the user in the Editor. There is little pure logic here, so this phase has no EditMode unit tests; it is verified by a documented multi-peer bandwidth + correctness run.

---

### Task 1: Always-interested registrar + marker

**Files:**
- Create: `Assets/Scripts/AreaOfInterest/AlwaysInterestedMarker.cs`
- Create: `Assets/Scripts/AreaOfInterest/AreaOfInterestRegistrar.cs`

> **Assembly note:** these go in `Assets/Scripts/AreaOfInterest/` (part of `Assembly-CSharp`, where `GameNetworkManager` lives), NOT under `Assets/Scripts/Net/` — that folder is the `Game.Net` asmdef, which is intentionally engine-free and Fusion-free (`noEngineReferences: true`, empty references). A `MonoBehaviour` implementing `INetworkRunnerCallbacks` cannot compile in `Game.Net`.

**Interfaces:**
- Produces:
  - `class AlwaysInterestedMarker : MonoBehaviour` — empty tag component placed on GameObjects whose `NetworkObject` must replicate to every player regardless of distance.
  - `class AreaOfInterestRegistrar : MonoBehaviour, INetworkRunnerCallbacks` with `static AreaOfInterestRegistrar Instance`, and public methods `void AddAlwaysInterested(NetworkObject obj)` and `void RemoveAlwaysInterested(NetworkObject obj)` used by dynamic callers (Task 3).

- [ ] **Step 1: Create the marker component**

Create `Assets/Scripts/AreaOfInterest/AlwaysInterestedMarker.cs`:

```csharp
using UnityEngine;

/// <summary>
/// Tag for a GameObject (carrying a NetworkObject) that must be replicated to EVERY player
/// regardless of Area-of-Interest distance — e.g. the flags, the CTF/score managers, home bases.
/// AreaOfInterestRegistrar finds every marker at startup and registers its NetworkObject as
/// always-interested for all players (including late joiners). Marking is the single explicit
/// place AoI culling is overridden, so HUD/objective state never disappears for distant players.
/// </summary>
public class AlwaysInterestedMarker : MonoBehaviour
{
}
```

- [ ] **Step 2: Create the registrar**

Create `Assets/Scripts/AreaOfInterest/AreaOfInterestRegistrar.cs`:

```csharp
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
```

- [ ] **Step 3: Initialize the registrar on the server when the gameplay scene is ready**

The registrar must discover markers only after the gameplay scene (with the flags/managers) is
loaded and the server is running. `NetworkedSpawnManager.Spawned()` already runs server-only at
that point. In `Assets/Scripts/NetworkSpawnManager.cs`, inside `Spawned()`, after the existing
`if (!Object.HasStateAuthority) return;` guard and `ValidateSpawnPoints();` call, add:

```csharp
        // Area of Interest: hand the registrar the active runner so it can mark the flags/managers
        // (and later the flag carrier) always-interested for every player. No-op if no registrar
        // is present in the scene (AoI simply not configured yet).
        if (AreaOfInterestRegistrar.Instance != null)
            AreaOfInterestRegistrar.Instance.ServerInitialize(Runner);
```

- [ ] **Step 4: Self-review and commit**

This task has no automated test (integration code; Unity Test Runner unavailable here). Self-review:
confirm the registrar guards every public method on `runner.IsServer`, that `OnPlayerJoined`
re-applies interest, and that `Add/RemoveAlwaysInterested` are idempotent via the `HashSet` return.

```bash
git add Assets/Scripts/AreaOfInterest/AlwaysInterestedMarker.cs Assets/Scripts/AreaOfInterest/AreaOfInterestRegistrar.cs Assets/Scripts/NetworkSpawnManager.cs
git commit -m "feat(net): always-interested registrar + marker for AoI-exempt objects"
```

---

### Task 2: Per-player interest region from the player

**Files:**
- Modify: `Assets/Scripts/Player/PlayerController.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks (independent; dormant until AoI is enabled in Task 4).
- Produces: a serialized `areaOfInterestRadius` on `PlayerController` and a per-tick server-side `Runner.AddPlayerAreaOfInterest` call.

- [ ] **Step 1: Add the serialized radius field**

In `PlayerController` (`Assets/Scripts/Player/PlayerController.cs`), add a serialized field with the
other state. Place it right after the `private NetworkButtons previousButtons;` line:

```csharp
    [Header("Area of Interest")]
    [Tooltip("Server-only: radius (world units) around this player that is replicated to them. " +
             "Must exceed the camera's max view half-extent to avoid pop-in at the screen edge. " +
             "Camera base size 5 + speed-zoom can show ~14 units half-width on widescreen, so the " +
             "default 25 leaves margin. Tune with the multi-peer pop-in check.")]
    [SerializeField] private float areaOfInterestRadius = 25f;
```

- [ ] **Step 2: Add the per-tick region call in `FixedUpdateNetwork`**

At the very top of `PlayerController.FixedUpdateNetwork()` (before the `if (GetInput(...))` block),
add the server-only interest-region contribution:

```csharp
        // Area of Interest: on the server, register this player's interest region around itself
        // every tick (Fusion clears regions per tick). Drives which objects replicate to this
        // player. No effect until AoI is enabled in the NetworkProjectConfig. Runs regardless of
        // input/alive state so the region never lapses (e.g. while dead/awaiting respawn).
        if (Runner.IsServer)
            Runner.AddPlayerAreaOfInterest(Object.InputAuthority, transform.position, areaOfInterestRadius);
```

- [ ] **Step 3: Self-review and commit**

Self-review: confirm the call is guarded by `Runner.IsServer`, uses `Object.InputAuthority` (the
owning player, not the server), and runs before the `GetInput` early-returns so a dead player's
region is still added.

```bash
git add Assets/Scripts/Player/PlayerController.cs
git commit -m "feat(net): each player contributes a server-side AoI region around itself"
```

---

### Task 3: Dynamic always-interest for the flag carrier

**Files:**
- Modify: `Assets/Scripts/CTF Flag/Flag.cs`

**Interfaces:**
- Consumes: `AreaOfInterestRegistrar.Instance`, `AddAlwaysInterested(NetworkObject)`, `RemoveAlwaysInterested(NetworkObject)` (Task 1).
- Produces: carrier player objects become always-interested for all players while carrying.

- [ ] **Step 1: Mark the carrier always-interested on pickup**

In `Flag.PickupFlag(GameObject player, PlayerRef playerRef)` (server-only, already
`HasStateAuthority`-gated), after the existing `marker.SetCarryingFlag(true);` block, add:

```csharp
        // Area of Interest: the carrier must replicate to EVERY player (even distant ones) or the
        // flag-direction HUD and carried-flag position desync for players outside the carrier's
        // region. NetworkRigidbody2D carries no position to a culling client otherwise.
        NetworkObject carrierObj = player.GetComponent<NetworkObject>();
        if (carrierObj != null && AreaOfInterestRegistrar.Instance != null)
            AreaOfInterestRegistrar.Instance.AddAlwaysInterested(carrierObj);
```

- [ ] **Step 2: Clear it on drop**

In `Flag.DropFlag()`, inside the existing `if (carrierGameObject != null)` block (where the marker
is cleared), before `carrierGameObject = null;`, add:

```csharp
            NetworkObject carrierObj = carrierGameObject.GetComponent<NetworkObject>();
            if (carrierObj != null && AreaOfInterestRegistrar.Instance != null)
                AreaOfInterestRegistrar.Instance.RemoveAlwaysInterested(carrierObj);
```

- [ ] **Step 3: Clear it on return**

In `Flag.ReturnFlag()`, inside the existing `if (carrierGameObject != null)` block, before
`carrierGameObject = null;`, add the identical clear:

```csharp
            NetworkObject carrierObj = carrierGameObject.GetComponent<NetworkObject>();
            if (carrierObj != null && AreaOfInterestRegistrar.Instance != null)
                AreaOfInterestRegistrar.Instance.RemoveAlwaysInterested(carrierObj);
```

- [ ] **Step 4: Self-review and commit**

Self-review: confirm pickup adds and BOTH drop and return remove (no leak that would keep a
former carrier always-interested forever), and that all three sites are within the existing
server-only (`HasStateAuthority`) methods. Note: a carrier who disconnects mid-carry has their
NetworkObject despawned by Fusion, which drops interest automatically; `RemoveAlwaysInterested`
null-guards the despawned case.

```bash
git add "Assets/Scripts/CTF Flag/Flag.cs"
git commit -m "feat(net): flag carrier stays always-interested to all players while carrying"
```

---

### Task 4: Enable AoI in the config + scene wiring + verification

**Files:**
- Modify: `Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion`

**Interfaces:**
- Consumes: Tasks 1–3 (the safety nets must exist before culling is turned on).

- [ ] **Step 1: Flip ReplicationFeatures to enable interest management**

In `Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion`, in the `"Simulation"` block,
change `ReplicationFeatures` from `1` (Scheduling only) to `2`
(`SchedulingAndInterestManagement`):

```json
        "ReplicationFeatures": 2,
```

(Leave the AoI grid/cell size at Fusion defaults; tune later only if the multi-peer check shows
quantization problems. This is the change that activates culling — it is intentionally last.)

- [ ] **Step 2: Commit the code+config change**

```bash
git add Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion
git commit -m "feat(net): enable Fusion Area of Interest (interest management)"
```

- [ ] **Step 3: USER — scene/prefab wiring in the Unity Editor (deferred, required)**

These steps require the Editor and cannot be done from the authoring environment:

1. Add a GameObject to the **Gameplay scene** (e.g. "AreaOfInterestRegistrar") and attach the
   `AreaOfInterestRegistrar` component. There must be exactly one.
2. Add the `AlwaysInterestedMarker` component to every networked object whose state the HUD/
   objectives need at any distance:
   - Both flags (the GameObjects with `Flag`).
   - The `CTFGameManager` object.
   - The score manager object (`TeamScoreManager`), if it is a NetworkObject.
   - Both home-base objects (`NetworkedHomeBase`), if base occupancy is shown in the HUD.
3. Let Unity generate `.meta` files for the new scripts.
4. (Optional) Tune `PlayerController.areaOfInterestRadius` on the player prefab if the pop-in
   check below shows objects appearing at the screen edge.

- [ ] **Step 4: USER — multi-peer verification (deferred, required)**

Run a headless server + multiple clients (ideally near 20 via duplicated builds / MPPM):

- [ ] **Bandwidth:** With AoI off vs. on, compare per-client inbound traffic (Fusion stats overlay
      or `TryGetFusionStatistics`). Confirm a material drop when players are spread across the map
      (target the documented ~3× reduction on player sync).
- [ ] **No pop-in:** A nearby enemy player/enemy/coin must already be visible before it reaches the
      screen edge. If objects pop in at the edge, raise `areaOfInterestRadius`.
- [ ] **HUD/score at distance:** A player on the far side of the map still sees correct flag state,
      score, and the flag-direction arrow.
- [ ] **Flag carrier at distance:** While an enemy carries your flag far away (outside your region),
      the flag-direction arrow tracks them and the carried flag renders above them. After they drop
      or it returns, the now-distant former carrier is culled again (no permanent always-interest).
- [ ] **Capture flow intact:** Pickup → carry across map → capture in base still scores correctly.

---

## Notes for Phase 2b (separate plan)

- Projectile object pooling via a custom `INetworkObjectProvider` (assigned to `StartGameArgs.ObjectProvider`) is **out of scope here** and gets its own plan — a pooling mistake breaks all spawning, so it is isolated. Research the `NetworkObjectProviderDefault` subclass route (override `AcquirePrefabInstance`/`ReleaseInstance`, keep base `GetPrefabId`) before writing it.
- Send-rate tuning stays "measure first" and is not in 2a.
