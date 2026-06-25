# Dedicated Server — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run the match on a headless, non-player **dedicated server** (`GameMode.Server`), with a **designated host-client** (lowest-id active player) who starts the match — while leaving all `HasStateAuthority`-gated gameplay code unchanged.

**Architecture:** Players connect as `GameMode.Client`; one process runs `GameMode.Server` headless. The MainMenu scene has no NetworkObject, so lobby coordination uses Fusion **reliable-data** (as team/loadout choices already do): the server broadcasts a per-client "lobby status" (are-you-host, can-start) message, and the designated host-client sends back a "start match" message. The genuinely new decision logic (boot-mode resolution, host designation, can-start) is extracted into a testable `Game.Net` assembly; the networked wiring is verified via multi-peer runs.

**Tech Stack:** Unity, Photon Fusion 2, C#, Unity Test Framework (NUnit, EditMode).

## Global Constraints

- Photon **Fusion 2**; tick rate **64**; `PlayerCount 20` — do not change in Phase 1.
- Positions sync via **`NetworkRigidbody2D`** (no `NetworkTransform`). Do not add NetworkTransform.
- The MainMenu scene has **no NetworkObject** — menu-phase coordination MUST use reliable-data, never RPCs.
- Server authority is unchanged: gameplay spawns/flag/combat stay gated on `HasStateAuthority` (= the dedicated server). Do NOT move authority to clients.
- Friends invite-only, **no anti-cheat** required — do not add input validation/authority hardening.
- Reliable-data payloads must be non-empty (a zero-length payload trips a Fusion assert on the real socket path — see existing loadout note in `GameNetworkManager`).
- New testable logic goes in its own asmdef assembly (`Game.Net`); test asmdefs reference that assembly (matching `Assets/Tests/EditMode/EnemyAI`). `Assembly-CSharp` cannot be referenced by an asmdef test assembly.

---

### Task 1: `NetworkBootMode` — resolve how this process should start

**Files:**
- Create: `Assets/Scripts/Net/Game.Net.asmdef`
- Create: `Assets/Scripts/Net/NetworkBootMode.cs`
- Create: `Assets/Tests/EditMode/Net/Game.Net.Tests.asmdef`
- Test: `Assets/Tests/EditMode/Net/NetworkBootModeTests.cs`

**Interfaces:**
- Produces: `enum NetworkBootKind { DedicatedServer, Client, SinglePlayerHost }` and
  `static NetworkBootKind NetworkBootMode.Resolve(bool isBatchMode, IReadOnlyList<string> args, bool singlePlayerMode)`.

- [ ] **Step 1: Create the production assembly definition**

Create `Assets/Scripts/Net/Game.Net.asmdef`:

```json
{
    "name": "Game.Net",
    "rootNamespace": "",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
```

(`autoReferenced: true` so `Assembly-CSharp` — where `GameNetworkManager` lives — sees these types. `noEngineReferences: true` keeps the policy pure C#, so it needs no UnityEngine and is trivially testable.)

- [ ] **Step 2: Create the test assembly definition**

Create `Assets/Tests/EditMode/Net/Game.Net.Tests.asmdef`:

```json
{
    "name": "Game.Net.Tests",
    "rootNamespace": "",
    "references": [
        "Game.Net",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 3: Write the failing test**

Create `Assets/Tests/EditMode/Net/NetworkBootModeTests.cs`:

```csharp
using NUnit.Framework;

public class NetworkBootModeTests
{
    [Test]
    public void Resolve_BatchMode_IsDedicatedServer()
    {
        var kind = NetworkBootMode.Resolve(true, new string[0], singlePlayerMode: true);
        Assert.AreEqual(NetworkBootKind.DedicatedServer, kind);
    }

    [Test]
    public void Resolve_DedicatedServerArg_IsDedicatedServer()
    {
        var kind = NetworkBootMode.Resolve(false, new[] { "-dedicatedServer" }, singlePlayerMode: true);
        Assert.AreEqual(NetworkBootKind.DedicatedServer, kind);
    }

    [Test]
    public void Resolve_Interactive_SinglePlayerTrue_IsSinglePlayerHost()
    {
        var kind = NetworkBootMode.Resolve(false, new string[0], singlePlayerMode: true);
        Assert.AreEqual(NetworkBootKind.SinglePlayerHost, kind);
    }

    [Test]
    public void Resolve_Interactive_SinglePlayerFalse_IsClient()
    {
        var kind = NetworkBootMode.Resolve(false, new string[0], singlePlayerMode: false);
        Assert.AreEqual(NetworkBootKind.Client, kind);
    }

    [Test]
    public void Resolve_NullArgs_DoesNotThrow()
    {
        var kind = NetworkBootMode.Resolve(false, null, singlePlayerMode: false);
        Assert.AreEqual(NetworkBootKind.Client, kind);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

In Unity: Window → General → Test Runner → EditMode → Run All.
Expected: the `NetworkBootModeTests` fail to compile / fail with "NetworkBootMode does not exist".

- [ ] **Step 5: Write the minimal implementation**

Create `Assets/Scripts/Net/NetworkBootMode.cs`:

```csharp
using System.Collections.Generic;

/// <summary>How this process should join the session.</summary>
public enum NetworkBootKind
{
    DedicatedServer,   // headless GameMode.Server, not a player
    Client,            // normal player, GameMode.Client
    SinglePlayerHost,  // dev convenience: GameMode.Host (host is also a player)
}

/// <summary>
/// Pure decision for how GameNetworkManager should start the runner. Kept free of UnityEngine
/// so it is unit-testable. Batch mode or an explicit "-dedicatedServer" arg means this process
/// is the dedicated server; otherwise it is an interactive client (or a single-player host for
/// solo dev testing).
/// </summary>
public static class NetworkBootMode
{
    public const string DedicatedServerArg = "-dedicatedServer";

    public static NetworkBootKind Resolve(bool isBatchMode, IReadOnlyList<string> args, bool singlePlayerMode)
    {
        if (isBatchMode) return NetworkBootKind.DedicatedServer;

        if (args != null)
        {
            for (int i = 0; i < args.Count; i++)
                if (args[i] == DedicatedServerArg) return NetworkBootKind.DedicatedServer;
        }

        return singlePlayerMode ? NetworkBootKind.SinglePlayerHost : NetworkBootKind.Client;
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Test Runner → EditMode → Run All. Expected: all 5 `NetworkBootModeTests` PASS.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Net Assets/Tests/EditMode/Net
git commit -m "feat(net): NetworkBootMode resolves dedicated-server vs client vs solo-host"
```

---

### Task 2: `LobbyHostPolicy` — designate host-client and gate match start

**Files:**
- Create: `Assets/Scripts/Net/LobbyHostPolicy.cs`
- Test: `Assets/Tests/EditMode/Net/LobbyHostPolicyTests.cs`

**Interfaces:**
- Consumes: the `Game.Net` / `Game.Net.Tests` asmdefs created in Task 1.
- Produces:
  - `const int LobbyHostPolicy.NoHost = -1`
  - `static int LobbyHostPolicy.DesignateHostId(IReadOnlyList<int> activePlayerIds)` — lowest id, or `NoHost` if empty.
  - `static bool LobbyHostPolicy.CanStart(IReadOnlyList<int> activePlayerIds, System.Func<int,bool> hasChosen)` — true iff at least one player and every active id has chosen.

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/Net/LobbyHostPolicyTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;

public class LobbyHostPolicyTests
{
    [Test]
    public void DesignateHostId_Empty_ReturnsNoHost()
    {
        Assert.AreEqual(LobbyHostPolicy.NoHost, LobbyHostPolicy.DesignateHostId(new int[0]));
    }

    [Test]
    public void DesignateHostId_SinglePlayer_ReturnsThatPlayer()
    {
        Assert.AreEqual(3, LobbyHostPolicy.DesignateHostId(new[] { 3 }));
    }

    [Test]
    public void DesignateHostId_ReturnsLowestId_RegardlessOfOrder()
    {
        Assert.AreEqual(1, LobbyHostPolicy.DesignateHostId(new[] { 4, 1, 7, 2 }));
    }

    [Test]
    public void DesignateHostId_AfterLowestLeaves_ReturnsNextLowest()
    {
        // host (id 1) left; remaining roster re-designates to id 2
        Assert.AreEqual(2, LobbyHostPolicy.DesignateHostId(new[] { 4, 2, 7 }));
    }

    [Test]
    public void CanStart_NoPlayers_False()
    {
        Assert.IsFalse(LobbyHostPolicy.CanStart(new int[0], _ => true));
    }

    [Test]
    public void CanStart_AllChosen_True()
    {
        var chosen = new HashSet<int> { 1, 2, 5 };
        Assert.IsTrue(LobbyHostPolicy.CanStart(new[] { 1, 2, 5 }, chosen.Contains));
    }

    [Test]
    public void CanStart_OneMissing_False()
    {
        var chosen = new HashSet<int> { 1, 5 };
        Assert.IsFalse(LobbyHostPolicy.CanStart(new[] { 1, 2, 5 }, chosen.Contains));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Test Runner → EditMode → Run All. Expected: `LobbyHostPolicyTests` fail with "LobbyHostPolicy does not exist".

- [ ] **Step 3: Write the minimal implementation**

Create `Assets/Scripts/Net/LobbyHostPolicy.cs`:

```csharp
using System;
using System.Collections.Generic;

/// <summary>
/// Pure lobby decisions for a dedicated-server match where the server is NOT a player.
/// The "host-client" (the player who gets the Start button) is simply the lowest-id active
/// player, so designation is deterministic and re-resolves when that player leaves. CanStart
/// mirrors the host-mode gate: every connected player must have submitted a team choice.
/// </summary>
public static class LobbyHostPolicy
{
    public const int NoHost = -1;

    public static int DesignateHostId(IReadOnlyList<int> activePlayerIds)
    {
        int host = NoHost;
        for (int i = 0; i < activePlayerIds.Count; i++)
        {
            int id = activePlayerIds[i];
            if (host == NoHost || id < host) host = id;
        }
        return host;
    }

    public static bool CanStart(IReadOnlyList<int> activePlayerIds, Func<int, bool> hasChosen)
    {
        if (activePlayerIds.Count == 0) return false;
        for (int i = 0; i < activePlayerIds.Count; i++)
            if (!hasChosen(activePlayerIds[i])) return false;
        return true;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Test Runner → EditMode → Run All. Expected: all `LobbyHostPolicyTests` PASS (plus Task 1 tests still green).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Net/LobbyHostPolicy.cs Assets/Tests/EditMode/Net/LobbyHostPolicyTests.cs
git commit -m "feat(net): LobbyHostPolicy designates host-client and gates match start"
```

---

### Task 3: Dedicated-server boot path in `GameNetworkManager`

**Files:**
- Modify: `Assets/Scripts/GameNetworkManager.cs` (`Start`, add `StartServer`, adjust `StartClient`)

**Interfaces:**
- Consumes: `NetworkBootMode.Resolve`, `NetworkBootKind` (Task 1).
- Produces: `async void GameNetworkManager.StartServer()` and a boot-mode branch in `Start()`.

This task is verified by **manual run** (Unity networking has no cheap automated harness). Each step states the exact expected observation.

- [ ] **Step 1: Add the boot-mode branch to `Start()`**

In `GameNetworkManager.Start()`, after the existing component/ callback setup and the
`LobbyTeamChoices.Clear(); LobbyLoadoutChoices.Clear(); gameStarting = false;` lines, and
**before** the `hostButton`/`clientButton` listener wiring, insert:

```csharp
        var boot = NetworkBootMode.Resolve(
            Application.isBatchMode,
            System.Environment.GetCommandLineArgs(),
            singlePlayerMode);

        if (boot == NetworkBootKind.DedicatedServer)
        {
            StartServer();
            return; // headless server: no menu, no team-selection UI
        }
```

(The existing button-wiring and `TeamSelectionUI` null-check stay below this, used by the
interactive Client / SinglePlayerHost paths.)

- [ ] **Step 2: Add `StartServer()`**

Add this method next to `StartHost()`:

```csharp
    async void StartServer()
    {
        var args = new StartGameArgs()
        {
            GameMode = GameMode.Server,
            SessionName = sessionName,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        };

        var result = await runner.StartGame(args);

        if (result.Ok)
            Debug.Log("✅ Dedicated server started — waiting for players.");
        else
            Debug.LogError($"❌ Server failed to start: {result.ShutdownReason}");
    }
```

- [ ] **Step 3: Simplify `StartClient()` (boot mode now governs solo testing)**

In `StartClient()`, delete the single-player redirect block so the client button always
starts a real client:

```csharp
        // In single player mode, client button does the same as host
        if (singlePlayerMode)
        {
            StartHost();
            return;
        }
```

Remove those lines. (Solo dev now uses `singlePlayerMode = true` → `Start()` resolves
`SinglePlayerHost` and the host button hosts; the client button is for real clients.)

- [ ] **Step 4: Build a headless server and run it**

Build a Windows player. Run it headless from a terminal:

```bash
"./Build/2dGame.exe" -batchmode -nographics -logFile ./server.log
```

Expected in `server.log`: `✅ Dedicated server started — waiting for players.` and **no**
`❌` errors, and no NullReference from the (skipped) team-selection UI.

- [ ] **Step 5: Connect one interactive client**

In the Editor, set `GameNetworkManager.singlePlayerMode = false`, enter Play mode, click the
client/connect button.
Expected: the client connects to the running session (Editor console shows no
`OnConnectFailed`), the team-selection panel appears, and the server log shows a player
joined (no spawn yet — gameplay scene not loaded until Task 5).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/GameNetworkManager.cs
git commit -m "feat(net): boot GameNetworkManager as dedicated server, client, or solo host"
```

---

### Task 4: Server broadcasts per-client lobby status (host designation + start gate)

**Files:**
- Modify: `Assets/Scripts/GameNetworkManager.cs` (reliable keys, `RecomputeLobbyAndBroadcast`, `HasChoiceForId`, `currentHostId`, `RefreshStartGate`)

**Interfaces:**
- Consumes: `LobbyHostPolicy.DesignateHostId`, `LobbyHostPolicy.CanStart`, `LobbyHostPolicy.NoHost` (Task 2); `Runner.SendReliableDataToPlayer(PlayerRef, ReliableKey, byte[])`.
- Produces: a server→client reliable message on `LobbyStatusKey` with a 2-byte payload `[isHost, canStart]`; field `private int currentHostId`.

- [ ] **Step 1: Add the two new reliable keys**

Next to the existing `TeamChoiceKey` / `LoadoutKey` declarations, add:

```csharp
    // Reliable-data channel: server -> client per-client lobby status ([isHost, canStart]).
    private static readonly Fusion.Sockets.ReliableKey LobbyStatusKey =
        Fusion.Sockets.ReliableKey.FromInts(0x4C425953, 0, 0, 0); // "LBYS"

    // Reliable-data channel: designated host-client -> server "start the match".
    private static readonly Fusion.Sockets.ReliableKey StartMatchKey =
        Fusion.Sockets.ReliableKey.FromInts(0x53545254, 0, 0, 0); // "STRT"
```

- [ ] **Step 2: Add the host-id field**

With the other private fields (`runner`, `isConnected`, `gameStarting`), add:

```csharp
    // Server-only: PlayerId of the current designated host-client (lowest active id), or
    // LobbyHostPolicy.NoHost when no players are connected.
    private int currentHostId = LobbyHostPolicy.NoHost;
```

- [ ] **Step 3: Add the broadcast + helper methods**

Add these methods (e.g. just below `RefreshStartGate`):

```csharp
    /// <summary>
    /// Dedicated-server only: recompute the host-client designation and start gate, then push a
    /// per-client lobby-status reliable message to every connected player. Re-sent on any lobby
    /// change (join, leave, choice recorded) so the UI stays correct.
    /// </summary>
    private void RecomputeLobbyAndBroadcast()
    {
        if (runner == null || !runner.IsServer || gameStarting)
            return;

        var ids = new List<int>();
        foreach (var p in runner.ActivePlayers)
            ids.Add(p.PlayerId);

        currentHostId = LobbyHostPolicy.DesignateHostId(ids);
        bool canStart = LobbyHostPolicy.CanStart(ids, HasChoiceForId);

        foreach (var p in runner.ActivePlayers)
        {
            byte isHost = (byte)(p.PlayerId == currentHostId ? 1 : 0);
            byte start = (byte)(canStart ? 1 : 0);
            runner.SendReliableDataToPlayer(p, LobbyStatusKey, new byte[] { isHost, start });
        }
    }

    /// <summary>Server-only: has the active player with this PlayerId submitted a team choice?</summary>
    private bool HasChoiceForId(int id)
    {
        foreach (var p in runner.ActivePlayers)
            if (p.PlayerId == id)
                return LobbyTeamChoices.Has(p);
        return false;
    }
```

- [ ] **Step 4: Route the start gate through the broadcast for a real dedicated server**

Replace the body of `RefreshStartGate()` with:

```csharp
    private void RefreshStartGate()
    {
        if (runner == null || !runner.IsServer)
            return;

        // A real dedicated server has no local player; push status to the clients. A
        // host-as-player (solo dev) keeps the old local-UI gate.
        if (runner.LocalPlayer == PlayerRef.None)
            RecomputeLobbyAndBroadcast();
        else if (teamSelectionUI != null)
            teamSelectionUI.SetStartAvailable(CanStartMatch());
    }
```

`RefreshStartGate` is already called from `OnPlayerJoined`, `OnPlayerLeft`, and `RecordChoice`,
so the broadcast fires on every relevant lobby change with no extra call sites.

- [ ] **Step 5: Verify the broadcast fires (manual)**

Add a temporary log at the end of `RecomputeLobbyAndBroadcast`:
`Debug.Log($"Lobby broadcast: host={currentHostId} canStart={canStart} players={ids.Count}");`
Run the headless server, connect two clients, have each pick a team.
Expected server log sequence: a broadcast on each join (`canStart=False`), then `canStart=True`
once both have chosen, with `host=` equal to the lower of the two PlayerIds throughout.
Remove the temporary log before committing.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/GameNetworkManager.cs
git commit -m "feat(net): server broadcasts per-client lobby status and host designation"
```

---

### Task 5: Client consumes lobby status; host-client starts the match

**Files:**
- Modify: `Assets/Scripts/GameNetworkManager.cs` (`OnReliableDataReceived`, `RequestStartMatch`)
- Modify: `Assets/Scripts/Player/Teamselectionui.cs` (`SetHostControls`, `ShowTeamSelection`)

**Interfaces:**
- Consumes: `LobbyStatusKey`, `StartMatchKey`, `currentHostId` (Task 4); `Runner.SendReliableDataToServer`.
- Produces: `void TeamSelectionUI.SetHostControls(bool isHost, bool canStart)`.

- [ ] **Step 1: Add `SetHostControls` to `TeamSelectionUI`**

In `Teamselectionui.cs`, add next to `SetStartAvailable`:

```csharp
    /// <summary>
    /// Driven by the server's lobby-status message (dedicated-server path). Shows the Start button
    /// only on the designated host-client, and enables it only once every player has chosen.
    /// </summary>
    public void SetHostControls(bool isHost, bool canStart)
    {
        if (startButton == null) return;
        startButton.gameObject.SetActive(isHost);
        startButton.interactable = isHost && canStart;
    }
```

- [ ] **Step 2: Stop deriving Start visibility from `runner.IsServer`**

In `TeamSelectionUI.ShowTeamSelection`, replace the existing Start-button block:

```csharp
        if (startButton != null)
        {
            bool isHost = runner != null && runner.IsServer;
            startButton.gameObject.SetActive(isHost);
            startButton.interactable = false;
        }
```

with (the server now tells the client whether it is host):

```csharp
        if (startButton != null)
        {
            // Hidden until the server pushes lobby status (SetHostControls). In the solo-dev
            // host path, RefreshStartGate -> SetStartAvailable still drives it.
            bool soloHost = runner != null && runner.IsServer && runner.LocalPlayer != Fusion.PlayerRef.None;
            startButton.gameObject.SetActive(soloHost);
            startButton.interactable = false;
        }
```

- [ ] **Step 3: Handle the client side of `OnReliableDataReceived`**

In `GameNetworkManager.OnReliableDataReceived`, replace the current early `if (!runner.IsServer) return;`
guard and the body with a server/client split. The server branch keeps the existing
team-choice / loadout handling and adds `StartMatchKey`; the client branch handles `LobbyStatusKey`:

```csharp
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
        if (runner.IsServer)
        {
            if (key == TeamChoiceKey)
            {
                if (data.Count < 1 || data.Array == null)
                {
                    Debug.LogError($"❌ [HOST] Empty team-choice payload from Player {player.PlayerId}");
                    return;
                }
                int teamNumber = data.Array[data.Offset];
                RecordChoice(player, teamNumber);
                return;
            }

            if (key == LoadoutKey)
            {
                if (data.Count < 1 || data.Array == null) return;
                var order = new byte[data.Count];
                System.Array.Copy(data.Array, data.Offset, order, 0, data.Count);
                LobbyLoadoutChoices.Set(player, order);
                return;
            }

            if (key == StartMatchKey)
            {
                // Only the designated host-client may start, and only once everyone has chosen.
                if (player.PlayerId == currentHostId && CanStartMatch())
                {
                    gameStarting = true;
                    LoadGameplayScene();
                }
                return;
            }

            return;
        }

        // ---- Client ----
        if (key == LobbyStatusKey && data.Count >= 2 && data.Array != null)
        {
            bool isHost = data.Array[data.Offset] == 1;
            bool canStart = data.Array[data.Offset + 1] == 1;
            if (teamSelectionUI != null)
                teamSelectionUI.SetHostControls(isHost, canStart);
        }
    }
```

- [ ] **Step 4: Make `RequestStartMatch` send to the server from a client**

Replace `RequestStartMatch()` with:

```csharp
    public void RequestStartMatch()
    {
        if (runner == null || !runner.IsRunning)
            return;

        if (runner.IsServer)
        {
            // Solo-dev host path: start directly.
            if (!CanStartMatch()) return;
            gameStarting = true;
            LoadGameplayScene();
        }
        else
        {
            // Dedicated-server path: ask the server to start (it re-validates the gate).
            runner.SendReliableDataToServer(StartMatchKey, new byte[] { 1 });
        }
    }
```

- [ ] **Step 5: Manual verification — designation, gate, and start**

Run the headless server + two interactive clients (Editor + one build, or two MPPM peers with
`singlePlayerMode = false`).
Expected:
1. Only the lower-PlayerId client shows the Start button; the other never does.
2. The Start button stays non-interactable until **both** clients have picked a team, then
   becomes interactable on the host-client only.
3. Clicking Start on the host-client loads the Gameplay scene on **all** peers and the server;
   players spawn on their chosen teams (existing `NetworkedSpawnManager`, unchanged).
4. PvP collision and flag capture behave exactly as before — no player is acting as server.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/GameNetworkManager.cs Assets/Scripts/Player/Teamselectionui.cs
git commit -m "feat(net): host-client starts dedicated-server match via reliable data"
```

---

### Task 6: Server build hygiene — no rendering/audio on the headless server

**Files:**
- Modify: `Assets/Scripts/GameNetworkManager.cs` (`OnSceneLoadDone`)

**Interfaces:**
- Consumes: `runner.IsServer`, `runner.LocalPlayer` (to detect a real dedicated server).

- [ ] **Step 1: Disable cameras and audio listeners on the dedicated server after scene load**

Replace the empty `OnSceneLoadDone` body with:

```csharp
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
    }
```

(Note: `NetworkedSpawnManager` already implements `OnSceneLoadDone` for its roster reconcile —
this is the separate `GameNetworkManager` callback; both fire.)

- [ ] **Step 2: Manual verification**

Run the headless server and start a match (host-client clicks Start).
Expected: `server.log` shows the gameplay scene loaded and players spawned, with **no**
repeated AudioListener warnings and no camera-related errors. Clients render normally.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/GameNetworkManager.cs
git commit -m "chore(net): disable rendering and audio on the dedicated server build"
```

---

### Task 7: End-to-end multi-peer verification

**Files:** none (verification only — the reviewer gate for the whole phase).

- [ ] **Step 1: Run the full path with three peers**

Start the headless server build. Connect three clients (one Editor with
`singlePlayerMode = false`, two player builds, or MPPM virtual peers).

- [ ] **Step 2: Confirm the acceptance criteria from the spec**

Verify all of the following; if any fails, file the gap before declaring Phase 1 done:
- [ ] No client process acts as the server; the server is the headless build only.
- [ ] Exactly one client (lowest PlayerId) shows the Start button; it enables only when all three have chosen a team.
- [ ] If the host-client disconnects in the lobby, the next-lowest client receives the Start button (host re-designation).
- [ ] Clicking Start loads gameplay on all peers + server; everyone spawns on their chosen team.
- [ ] Player-vs-player physical collision (body-blocking) and flag capture behave as in the current host build.
- [ ] Latency feels comparable across all clients (no single client has a zero-latency advantage).

- [ ] **Step 3: Commit a short verification note (optional)**

If you keep a test log, record the run results under `docs/superpowers/` and commit.

---

## Notes for Phase 2 / 3 (not in scope here)

- Phase 2 (Area of Interest + projectile pooling) and Phase 3 (combat prediction + Fusion lag
  compensation) are separate plans. Do not enable AoI or lag compensation in Phase 1 — the
  `NetworkProjectConfig` stays untouched here.
