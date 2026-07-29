# Match Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the CTF arena a real match loop — start → play → resolve → back to lobby → repeat — driven by a networked `MatchPhase` state machine that replaces `CTFGameManager`'s dead-end `GameIsOver` bool.

**Architecture:** A new server-authoritative `MatchManager` (`NetworkBehaviour`, one per Gameplay scene) owns a `[Networked] MatchPhase` enum (`Warmup → Countdown → Live → PostMatch → Intermission`), a single reused `[Networked] TickTimer`, and a `[Networked] byte Winner`. It is the single "who won" resolver: `CTFGameManager` reports captures into it, and it resolves timer expiry via a pure `MatchResolver`. PostMatch auto-returns to the existing lobby by reloading the MainMenu scene through the persistent `GameNetworkManager`. Match state resets for free because each new match is a fresh Gameplay-scene load.

**Tech Stack:** Unity 6.3 (6000.3.0f1), Photon Fusion 2.0.9 (Host/Client), C#, new Input System, NUnit EditMode tests, TextMeshPro.

## Global Constraints

- Simulation-path timing uses `TickTimer` only — never `Invoke`/`Time.time`/coroutines. Create with `TickTimer.CreateFromSeconds(Runner, seconds)`, poll with `PhaseTimer.Expired(Runner)`, read remaining with `PhaseTimer.RemainingTime(Runner)` (returns `float?`).
- All `Runner.Spawn`/`Despawn` and authoritative state changes happen only under `HasStateAuthority`. No client-local match state.
- Positions sync via `NetworkRigidbody2D` or `[Networked]` anchors, never `NetworkTransform` (not relevant to new code here, but do not add any).
- New Input System only.
- Pure-logic classes that are unit-tested live in their own auto-referenced asmdef (mirror `Game.Buffs.Core`); Fusion `NetworkBehaviour`s live in `Assembly-CSharp` (no asmdef) so they can see the existing managers.
- Verification is manual per house style: compile clean, then check single-player (`GameNetworkManager.singlePlayerMode`… i.e. Host, `singlePlayerMode=true`) and Multiplayer Play Mode (1 host + 1 client). Only the pure `MatchResolver` has automated EditMode tests. EditMode tests run via Unity's Test Runner (Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All), or outside a locked editor with the bundled-Roslyn workaround (see `docs/…unity-locked-verification`).
- `Team` enum: `None=0, Team1=1, Team2=2, Team3AI=3`. `TeamUtil.ToNumber(Team)` / `FromNumber(int)` convert.

---

## File Structure

**Created:**
- `Assets/Scripts/Match/Core/MatchResolver.cs` — pure winner logic (namespace `Game.Match.Core`).
- `Assets/Scripts/Match/Core/Game.Match.Core.asmdef` — auto-referenced, engine-free.
- `Assets/Tests/EditMode/Match/MatchResolverTests.cs` — NUnit tests.
- `Assets/Tests/EditMode/Match/Game.Match.Core.Tests.asmdef` — references `Game.Match.Core`.
- `Assets/Scripts/Match/MatchManager.cs` — the phase state machine + `MatchPhase` enum (`Assembly-CSharp`).
- `Assets/Scripts/Hud/MatchPhaseHud.cs` — local HUD for countdown / live timer / results panel.

**Modified:**
- `Assets/Scripts/CTF Flag/CTFGameManager.cs` — remove `GameIsOver`/`gameOverPanel`/`winnerText`/`EndGame`/`AnnounceWinnerRpc`/`OnGameOverChanged`; route captures to `MatchManager.ReportCapture`; derive `IsGameOver()` from phase.
- `Assets/Scripts/GameNetworkManager.cs` — become a `DontDestroyOnLoad` singleton with a dup-guard; add `menuSceneIndex`, `BeginReturnToLobby()`, and menu re-wire in `OnSceneLoadDone`.
- `Assets/Scripts/Player/NetworkInputProvider.cs` — send neutral input when the match is not Live.
- `Assets/Scripts/Enemy/Base/EnemyAI.cs` — freeze AI in non-Live phases.

**Scene work (Gameplay.unity / hud):** add a `MatchManager` scene NetworkObject; repurpose the existing game-over panel into the results panel wired to `MatchPhaseHud`.

---

## Task 1: MatchResolver pure winner logic

Pure, engine-free, TDD. This is the only automated-test task.

**Files:**
- Create: `Assets/Scripts/Match/Core/MatchResolver.cs`
- Create: `Assets/Scripts/Match/Core/Game.Match.Core.asmdef`
- Test: `Assets/Tests/EditMode/Match/MatchResolverTests.cs`
- Test: `Assets/Tests/EditMode/Match/Game.Match.Core.Tests.asmdef`

**Interfaces:**
- Produces:
  - `Game.Match.Core.MatchResolver.ResolveTimerWinner(int team1Score, int team2Score) -> int` (returns `1`, `2`, or `0` for draw).
  - `Game.Match.Core.MatchResolver.WinnerLabel(int winner) -> string` (`1`→"Team 1 Wins!", `2`→"Team 2 Wins!", else "It's a Draw!").

- [ ] **Step 1: Create the runtime asmdef**

Create `Assets/Scripts/Match/Core/Game.Match.Core.asmdef` (mirrors `Game.Buffs.Core`):

```json
{
    "name": "Game.Match.Core",
    "rootNamespace": "Game.Match.Core",
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

- [ ] **Step 2: Create the test asmdef**

Create `Assets/Tests/EditMode/Match/Game.Match.Core.Tests.asmdef`:

```json
{
    "name": "Game.Match.Core.Tests",
    "rootNamespace": "",
    "references": [
        "Game.Match.Core",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [ "Editor" ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [ "nunit.framework.dll" ],
    "autoReferenced": false,
    "defineConstraints": [ "UNITY_INCLUDE_TESTS" ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 3: Write the failing tests**

Create `Assets/Tests/EditMode/Match/MatchResolverTests.cs`:

```csharp
using NUnit.Framework;
using Game.Match.Core;

public class MatchResolverTests
{
    [TestCase(3, 1, 1)] // team1 higher
    [TestCase(1, 3, 2)] // team2 higher
    [TestCase(2, 2, 0)] // equal -> draw
    [TestCase(0, 0, 0)] // both zero -> draw
    public void ResolveTimerWinner_HigherScoreWins_EqualIsDraw(int t1, int t2, int expected)
    {
        Assert.AreEqual(expected, MatchResolver.ResolveTimerWinner(t1, t2));
    }

    [TestCase(1, "Team 1 Wins!")]
    [TestCase(2, "Team 2 Wins!")]
    [TestCase(0, "It's a Draw!")]
    [TestCase(99, "It's a Draw!")] // any non-1/2 -> draw label
    public void WinnerLabel_MapsWinnerToText(int winner, string expected)
    {
        Assert.AreEqual(expected, MatchResolver.WinnerLabel(winner));
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run EditMode tests (Test Runner ▸ EditMode ▸ Run All, or the bundled-Roslyn CLI).
Expected: FAIL / compile error — `MatchResolver` does not exist.

- [ ] **Step 5: Write the implementation**

Create `Assets/Scripts/Match/Core/MatchResolver.cs`:

```csharp
namespace Game.Match.Core
{
    /// <summary>
    /// Pure match-outcome logic, engine-free so it is unit-testable. The single place that
    /// decides a timer-expiry winner and formats the results banner. Winner codes: 0 = draw,
    /// 1 = Team1, 2 = Team2 (matches TeamUtil.ToNumber).
    /// </summary>
    public static class MatchResolver
    {
        /// <summary>Timer expired with no capture: higher coin score wins, exactly equal is a draw.</summary>
        public static int ResolveTimerWinner(int team1Score, int team2Score)
        {
            if (team1Score > team2Score) return 1;
            if (team2Score > team1Score) return 2;
            return 0;
        }

        /// <summary>Results-banner text for a winner code. Anything other than 1/2 reads as a draw.</summary>
        public static string WinnerLabel(int winner)
        {
            switch (winner)
            {
                case 1: return "Team 1 Wins!";
                case 2: return "Team 2 Wins!";
                default: return "It's a Draw!";
            }
        }
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run EditMode tests again. Expected: all 8 cases PASS.

- [ ] **Step 7: Commit**

```bash
git add "Assets/Scripts/Match/Core/MatchResolver.cs" "Assets/Scripts/Match/Core/Game.Match.Core.asmdef" "Assets/Tests/EditMode/Match/MatchResolverTests.cs" "Assets/Tests/EditMode/Match/Game.Match.Core.Tests.asmdef"
git commit -m "feat(match): add pure MatchResolver winner logic + tests"
```

---

## Task 2: MatchManager phase machine (Warmup → Countdown → Live)

Introduce the networked state machine and its plumbing. Live does not end yet (that is Task 3); this task is complete when a started match advances Warmup → Countdown → Live on the state authority.

**Files:**
- Create: `Assets/Scripts/Match/MatchManager.cs`

**Interfaces:**
- Consumes: `Game.Match.Core.MatchResolver` (auto-referenced), `GameSettingsManager.Instance.matchTimeLimit` (float, minutes), `TeamUtil`, `LobbyHostPolicy.DesignateHostId`.
- Produces (relied on by Tasks 3–6):
  - `enum MatchPhase : byte { Warmup, Countdown, Live, PostMatch, Intermission }`
  - `MatchManager.Instance` (static)
  - `MatchManager.Phase` (`[Networked] MatchPhase`)
  - `MatchManager.Winner` (`[Networked] byte`)
  - `MatchManager.IsLive` (`bool`)
  - `MatchManager.InputEnabled` (`bool`, true only in Live)
  - `MatchManager.PhaseTimeRemaining` (`float?`)
  - `MatchManager.PhaseChanged` (`event Action`)
  - `MatchManager.ReportCapture(Team)` — stub in this task, filled in Task 3
  - `MatchManager.EnterPhase(MatchPhase)` — private
  - `MatchManager.LocalPlayerIsHost()` — added in Task 4

- [ ] **Step 1: Create MatchManager with the enum, networked state, and Warmup→Countdown→Live machine**

Create `Assets/Scripts/Match/MatchManager.cs`:

```csharp
using System;
using Fusion;
using UnityEngine;
using Game.Match.Core;

/// <summary>Explicit match life-cycle phases. Replaces CTFGameManager's lone GameIsOver bool.</summary>
public enum MatchPhase : byte { Warmup, Countdown, Live, PostMatch, Intermission }

/// <summary>
/// Server-authoritative match life cycle. Owns the phase enum, one reused TickTimer, and the
/// single "who won" resolver (CTF capture + timer expiry both feed it). One per Gameplay scene.
/// Must be always-interested under AoI so every player sees the phase/timer/results.
/// </summary>
public class MatchManager : NetworkBehaviour
{
    public static MatchManager Instance { get; private set; }

    [Header("Phase Durations (seconds)")]
    [SerializeField] private float warmupSeconds = 3f;
    [SerializeField] private float countdownSeconds = 3f;
    [Tooltip("How long the results panel holds before auto-returning to the lobby.")]
    [SerializeField] private float postMatchSeconds = 20f;

    [Networked, OnChangedRender(nameof(OnPhaseChanged))]
    public MatchPhase Phase { get; set; }

    [Networked] public byte Winner { get; set; } // 0 = draw, 1 = Team1, 2 = Team2

    // One timer reused per timed phase; its networked so clients/late-joiners see remaining time.
    [Networked] private TickTimer PhaseTimer { get; set; }

    /// <summary>Fires on every phase change (all peers, via OnChangedRender). HUD subscribes.</summary>
    public event Action PhaseChanged;

    public bool IsLive => Phase == MatchPhase.Live;
    public bool InputEnabled => Phase == MatchPhase.Live;

    /// <summary>Seconds left in the current timed phase, or null when the phase has no running timer.</summary>
    public float? PhaseTimeRemaining => PhaseTimer.RemainingTime(Runner);

    private void Awake()
    {
        // Never Destroy() a spawned NetworkObject locally (desyncs Fusion's object table); disable
        // the duplicate and leave it inert, matching TeamScoreManager's guard.
        if (Instance != null && Instance != this) { enabled = false; return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
            EnterPhase(MatchPhase.Warmup);

        // Late-joiner reconcile: render the current phase immediately (mirrors the old
        // OnGameOverChanged-from-Spawned pattern).
        OnPhaseChanged();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        switch (Phase)
        {
            case MatchPhase.Warmup:
                if (PhaseTimer.Expired(Runner)) EnterPhase(MatchPhase.Countdown);
                break;
            case MatchPhase.Countdown:
                if (PhaseTimer.Expired(Runner)) EnterPhase(MatchPhase.Live);
                break;
            case MatchPhase.Live:
                // Timer-expiry resolution is added in Task 3.
                break;
            case MatchPhase.PostMatch:
                // Auto-advance is added in Task 4.
                break;
            case MatchPhase.Intermission:
                break;
        }
    }

    /// <summary>Server-only. Sets Phase and arms the timer for the new phase.</summary>
    private void EnterPhase(MatchPhase next)
    {
        Phase = next;

        switch (next)
        {
            case MatchPhase.Warmup:
                PhaseTimer = TickTimer.CreateFromSeconds(Runner, warmupSeconds);
                break;
            case MatchPhase.Countdown:
                PhaseTimer = TickTimer.CreateFromSeconds(Runner, countdownSeconds);
                break;
            case MatchPhase.Live:
                float limit = (GameSettingsManager.Instance != null)
                    ? GameSettingsManager.Instance.matchTimeLimit * 60f
                    : 0f;
                // matchTimeLimit == 0 means "no timer": capture is then the only end condition.
                PhaseTimer = limit > 0f ? TickTimer.CreateFromSeconds(Runner, limit) : TickTimer.None;
                break;
            case MatchPhase.PostMatch:
                PhaseTimer = TickTimer.CreateFromSeconds(Runner, postMatchSeconds);
                break;
            case MatchPhase.Intermission:
                PhaseTimer = TickTimer.None;
                break;
        }
    }

    /// <summary>Server-only. A team captured the enemy flag. Filled in Task 3.</summary>
    public void ReportCapture(Team winningTeam)
    {
        if (!HasStateAuthority || Phase != MatchPhase.Live) return;
        // Body added in Task 3.
    }

    private void OnPhaseChanged() => PhaseChanged?.Invoke();
}
```

- [ ] **Step 2: Verify it compiles**

Let Unity recompile (or run the bundled-Roslyn compile). Expected: no errors; `MatchManager` and `MatchPhase` resolve, `Game.Match.Core` is visible.

- [ ] **Step 3: Manual single-player smoke check (deferred to scene wiring)**

`MatchManager` needs a scene NetworkObject to run; that wiring is Task 7. For now, confirm compilation only. (A temporary `Debug.Log($"[Match] {Phase}")` inside `EnterPhase` is optional; remove before commit.)

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/Match/MatchManager.cs"
git commit -m "feat(match): add MatchManager phase machine (Warmup->Countdown->Live)"
```

---

## Task 3: Win resolution — capture + timer expiry, retire GameIsOver

Wire both end conditions into `MatchManager` and migrate `CTFGameManager` off its dead-end bool. Nothing outside `CTFGameManager` reads `GameIsOver`/`IsGameOver`/`gameOverPanel`/`winnerText` (verified), so this migration is self-contained.

**Files:**
- Modify: `Assets/Scripts/Match/MatchManager.cs`
- Modify: `Assets/Scripts/CTF Flag/CTFGameManager.cs`

**Interfaces:**
- Consumes: `MatchManager.ReportCapture(Team)`, `MatchManager.IsLive`, `TeamScoreManager.Instance.Team1Score/Team2Score`, `MatchResolver.ResolveTimerWinner`.
- Produces: `CTFGameManager.IsGameOver()` now derives from `MatchManager.Phase`.

- [ ] **Step 1: Fill in MatchManager capture + timer resolution**

In `Assets/Scripts/Match/MatchManager.cs`, replace the `case MatchPhase.Live:` line in `FixedUpdateNetwork` (currently just a comment) with:

```csharp
            case MatchPhase.Live:
                if (PhaseTimer.Expired(Runner)) ResolveByTimer();
                break;
```

Replace the `ReportCapture` stub body with:

```csharp
    /// <summary>Server-only. A team carried the enemy flag home during Live — instant win.</summary>
    public void ReportCapture(Team winningTeam)
    {
        if (!HasStateAuthority || Phase != MatchPhase.Live) return;
        Winner = (byte)TeamUtil.ToNumber(winningTeam);
        EnterPhase(MatchPhase.PostMatch);
    }

    /// <summary>Server-only. Live timer ran out with no capture: higher coin score wins, tie = draw.</summary>
    private void ResolveByTimer()
    {
        int t1 = TeamScoreManager.Instance != null ? TeamScoreManager.Instance.Team1Score : 0;
        int t2 = TeamScoreManager.Instance != null ? TeamScoreManager.Instance.Team2Score : 0;
        Winner = (byte)MatchResolver.ResolveTimerWinner(t1, t2);
        EnterPhase(MatchPhase.PostMatch);
    }
```

- [ ] **Step 2: Strip the dead-end win handling out of CTFGameManager**

In `Assets/Scripts/CTF Flag/CTFGameManager.cs`:

Delete the two UI fields (lines ~27–31):

```csharp
    [Tooltip("Panel for game over screen")]
    [SerializeField] private GameObject gameOverPanel;

    [Tooltip("Text for winner announcement")]
    [SerializeField] private TextMeshProUGUI winnerText;
```

Delete the networked bool (lines ~37–39):

```csharp
    // Networked properties with OnChanged callbacks
    [Networked, OnChangedRender(nameof(OnGameOverChanged))]
    public bool GameIsOver { get; set; }
```

In `Awake()`, delete the panel-hide block:

```csharp
        // Hide game over panel initially
        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);
```

In `Spawned()`, delete the trailing `OnGameOverChanged();` call.

Delete `EnterGame`'s replacement target `EndGame`, the `AnnounceWinnerRpc`, and `OnGameOverChanged` entirely (methods at lines ~148–154, ~168–187, ~197–205).

- [ ] **Step 3: Route captures and phase guards through MatchManager**

In `CTFGameManager.OnCarrierEnteredBase`, replace the whole body with:

```csharp
    public void OnCarrierEnteredBase(PlayerRef carrier, Team baseTeam)
    {
        if (!HasStateAuthority) return;
        if (MatchManager.Instance == null || !MatchManager.Instance.IsLive) return;
        if (team1Flag == null || team2Flag == null) return;

        if (baseTeam == Team.Team1 &&
            team2Flag.IsCarriedBy(carrier) && team1Flag.State == Flag.FlagState.AtHome)
        {
            MatchManager.Instance.ReportCapture(Team.Team1);
        }
        else if (baseTeam == Team.Team2 &&
            team1Flag.IsCarriedBy(carrier) && team2Flag.State == Flag.FlagState.AtHome)
        {
            MatchManager.Instance.ReportCapture(Team.Team2);
        }
    }
```

In `CTFGameManager.OnFlagReturnedHome`, change the guard line:

```csharp
        if (!HasStateAuthority) return;
        if (MatchManager.Instance != null && !MatchManager.Instance.IsLive) return;
```

Replace `IsGameOver()` (in the Public Getters region):

```csharp
    public bool IsGameOver() =>
        MatchManager.Instance != null &&
        (MatchManager.Instance.Phase == MatchPhase.PostMatch ||
         MatchManager.Instance.Phase == MatchPhase.Intermission);
```

- [ ] **Step 4: Verify it compiles**

Recompile. Expected: no errors. If the `TMPro` using is now unused it may warn — leave it (notificationText still uses `TextMeshProUGUI`).

- [ ] **Step 5: Manual verification (after Task 7 scene wiring; note here, execute in Task 7)**

Deferred to Task 7's play test: capturing a flag during Live moves `Phase` to `PostMatch` with `Winner` = the capturing team; with `matchTimeLimit > 0` and no capture, timer expiry sets `Winner` from coin scores (or draw). For now, compile-clean is the gate.

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/Match/MatchManager.cs" "Assets/Scripts/CTF Flag/CTFGameManager.cs"
git commit -m "feat(match): resolve wins via MatchManager; retire GameIsOver"
```

---

## Task 4: PostMatch auto-advance + host skip + return-to-lobby plumbing

Close the loop: PostMatch holds for 20 s (or a host skip), then the server reloads MainMenu and re-shows the persisted lobby. Requires making `GameNetworkManager` a dup-guarded singleton so the reloaded menu scene's copy self-destructs.

**Files:**
- Modify: `Assets/Scripts/Match/MatchManager.cs`
- Modify: `Assets/Scripts/GameNetworkManager.cs`

**Interfaces:**
- Consumes: `GameNetworkManager.Instance.BeginReturnToLobby()`, `LobbyHostPolicy.DesignateHostId`.
- Produces:
  - `MatchManager.RPC_RequestReturnToLobby(RpcInfo)` (host-validated skip)
  - `MatchManager.LocalPlayerIsHost() -> bool` (HUD uses it to show the skip button)
  - `GameNetworkManager.Instance` (static)
  - `GameNetworkManager.BeginReturnToLobby()`

- [ ] **Step 1: MatchManager — auto-advance, Intermission entry, host-skip RPC**

In `Assets/Scripts/Match/MatchManager.cs`, replace the `case MatchPhase.PostMatch:` line in `FixedUpdateNetwork` with:

```csharp
            case MatchPhase.PostMatch:
                if (PhaseTimer.Expired(Runner)) EnterPhase(MatchPhase.Intermission);
                break;
```

Add the return-to-lobby trigger to the `Intermission` branch of `EnterPhase`:

```csharp
            case MatchPhase.Intermission:
                PhaseTimer = TickTimer.None;
                if (GameNetworkManager.Instance != null)
                    GameNetworkManager.Instance.BeginReturnToLobby();
                break;
```

Add these members (near `ReportCapture`):

```csharp
    /// <summary>Host-only early advance from the results screen. Auto-advance still fires otherwise.</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestReturnToLobby(RpcInfo info = default)
    {
        if (!HasStateAuthority || Phase != MatchPhase.PostMatch) return;
        if (!IsHost(info.Source)) return;
        EnterPhase(MatchPhase.Intermission);
    }

    /// <summary>True on the peer whose local player is the designated host (lowest active PlayerId).</summary>
    public bool LocalPlayerIsHost() => Runner != null && IsHost(Runner.LocalPlayer);

    private bool IsHost(PlayerRef p)
    {
        if (Runner == null || p == PlayerRef.None) return false;
        var ids = new System.Collections.Generic.List<int>();
        foreach (var active in Runner.ActivePlayers) ids.Add(active.PlayerId);
        return LobbyHostPolicy.DesignateHostId(ids) == p.PlayerId;
    }
```

- [ ] **Step 2: GameNetworkManager — singleton + dup-guard**

In `Assets/Scripts/GameNetworkManager.cs`, add a static instance field near the top of the class (after the `runner` fields):

```csharp
    public static GameNetworkManager Instance { get; private set; }
    public int menuSceneIndex = 0;
```

Add an `Awake` **before** `Start` that guards duplicates (the reloaded MainMenu scene contains its own `GameNetworkManager`; the persistent one must win):

```csharp
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
```

At the very top of `Start()`, bail if this is the doomed duplicate (its `Awake` already scheduled destruction):

```csharp
    void Start()
    {
        if (Instance != this) return;
        DontDestroyOnLoad(gameObject);
        // ... existing body unchanged ...
```

Add an `OnDestroy` guard so the persistent instance clears the static on real teardown (there is already an `OnDestroy` that shuts the runner down — merge this in):

```csharp
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (runner != null) runner.Shutdown();
    }
```

- [ ] **Step 3: GameNetworkManager — BeginReturnToLobby + menu re-wire**

Add the return method (server-only; resets the one-way `gameStarting` latch, then loads the menu):

```csharp
    /// <summary>
    /// Server-only. Ends the match by reloading the MainMenu scene and re-showing the persisted
    /// lobby. Resets the gameStarting latch so the host can Start the next match. Called by
    /// MatchManager when entering Intermission.
    /// </summary>
    public void BeginReturnToLobby()
    {
        if (runner == null || !runner.IsServer) return;
        gameStarting = false;
        _ = runner.LoadScene(SceneRef.FromIndex(menuSceneIndex));
    }
```

Find `GameNetworkManager`'s `OnSceneLoadDone` callback. (It exists as part of `INetworkRunnerCallbacks`; if the body is empty, replace it — otherwise append.) Set it to re-acquire the freshly loaded menu UI and show the lobby on every peer:

```csharp
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        // Only care about arriving back in the menu scene (the return-to-lobby path). The gameplay
        // load has a different build index and is handled by the gameplay-side managers.
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex != menuSceneIndex)
            return;

        // The persistent GameNetworkManager's serialized menu/lobby refs died with the previous
        // menu scene instance; re-acquire the new ones.
        menuUI = FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
        lobbyUI = FindFirstObjectByType<LobbyScreenUI>(FindObjectsInactive.Include);

        if (menuUI != null) menuUI.Hide();   // skip the Join/Host screen — we are still connected
        if (lobbyUI != null) lobbyUI.Show();

        // Server re-broadcasts the persisted roster so every client's lobby repopulates.
        if (runner.IsServer) BroadcastLobby();
    }
```

If `GameNetworkManager` already declares `OnSceneLoadDone` (empty), replace that declaration; do not add a second one.

- [ ] **Step 4: Verify it compiles**

Recompile. Expected: no errors. Confirm there is exactly one `OnSceneLoadDone` in `GameNetworkManager` and one `OnDestroy`.

- [ ] **Step 5: Manual verification (deferred to Task 7)**

Play-test in Task 7 confirms: results panel auto-returns to the lobby after 20 s; the host skip button returns immediately; the lobby repopulates; pressing Start begins a fresh match.

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/Match/MatchManager.cs" "Assets/Scripts/GameNetworkManager.cs"
git commit -m "feat(match): auto-advance PostMatch and return to the persisted lobby"
```

---

## Task 5: Input lock + enemy freeze in non-Live phases

Players and enemies must be inert during Warmup/Countdown/PostMatch. Gate players at the single input chokepoint (neutral input everywhere it is consumed) and enemies at the single authority-only AI step.

**Files:**
- Modify: `Assets/Scripts/Player/NetworkInputProvider.cs`
- Modify: `Assets/Scripts/Enemy/Base/EnemyAI.cs`

**Interfaces:**
- Consumes: `MatchManager.Instance.InputEnabled`, `MatchManager.Instance.IsLive`.

- [ ] **Step 1: Suppress local input outside Live**

In `Assets/Scripts/Player/NetworkInputProvider.cs`, at the very top of `OnInput` (before `var data = new NetInput();`), add:

```csharp
        // Freeze the local player whenever the match is not Live (Warmup/Countdown/PostMatch/
        // Intermission). One chokepoint disables movement AND every button, so no per-system
        // edits are needed. In the menu (no MatchManager) input flows normally.
        if (MatchManager.Instance != null && !MatchManager.Instance.InputEnabled)
        {
            input.Set(new NetInput());
            return;
        }
```

- [ ] **Step 2: Freeze enemy AI outside Live**

In `Assets/Scripts/Enemy/Base/EnemyAI.cs`, at the very top of `public void Tick()` (the authority-only AI step), add:

```csharp
        // Hold enemies still during non-Live phases (countdown spawn-in, post-match freeze).
        if (MatchManager.Instance != null && !MatchManager.Instance.IsLive) return;
```

- [ ] **Step 3: Verify it compiles**

Recompile. Expected: no errors.

- [ ] **Step 4: Manual verification (deferred to Task 7)**

Task 7 play test confirms: during Countdown the local player cannot move/dash/attack and enemies stand still; both resume the instant Live begins; both freeze again in PostMatch.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Player/NetworkInputProvider.cs" "Assets/Scripts/Enemy/Base/EnemyAI.cs"
git commit -m "feat(match): lock player input and freeze enemies outside Live"
```

---

## Task 6: MatchPhaseHud — countdown, live timer, results panel

Local, event-driven HUD for the phases. Mirrors the `FlagDirectionHud` pattern (a `MonoBehaviour` on the HUD canvas that reads networked state each `LateUpdate`); phase transitions come from `MatchManager.PhaseChanged`, only the ticking number is read per-frame.

**Files:**
- Create: `Assets/Scripts/Hud/MatchPhaseHud.cs`

**Interfaces:**
- Consumes: `MatchManager.Instance` (`Phase`, `Winner`, `PhaseTimeRemaining`, `PhaseChanged`, `LocalPlayerIsHost()`, `RPC_RequestReturnToLobby()`), `TeamScoreManager.Instance.Team1Score/Team2Score`, `MatchResolver.WinnerLabel`.

- [ ] **Step 1: Create the HUD component**

Create `Assets/Scripts/Hud/MatchPhaseHud.cs`:

```csharp
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Match.Core;

/// <summary>
/// Local presentation of the match life cycle. Binds once to MatchManager, toggles panels on
/// PhaseChanged, and reads the countdown/timer number each LateUpdate (local render-path read of
/// networked state — not networked polling). No authoritative state here.
/// </summary>
public class MatchPhaseHud : MonoBehaviour
{
    [Header("Countdown / warmup (center)")]
    [SerializeField] private GameObject countdownRoot;
    [SerializeField] private TMP_Text countdownText;

    [Header("Live match timer (top)")]
    [SerializeField] private GameObject matchTimerRoot;
    [SerializeField] private TMP_Text matchTimerText;

    [Header("Results panel")]
    [SerializeField] private GameObject resultsPanel;
    [SerializeField] private TMP_Text winnerText;
    [SerializeField] private TMP_Text finalScoreText;
    [SerializeField] private TMP_Text returnCountdownText;
    [SerializeField] private Button returnToLobbyButton;

    private MatchManager bound;

    private void Awake()
    {
        HideAll();
        if (returnToLobbyButton != null)
            returnToLobbyButton.onClick.AddListener(OnReturnToLobbyClicked);
    }

    private void OnDestroy()
    {
        if (bound != null) bound.PhaseChanged -= Render;
        if (returnToLobbyButton != null)
            returnToLobbyButton.onClick.RemoveListener(OnReturnToLobbyClicked);
    }

    private void LateUpdate()
    {
        // Bind lazily: MatchManager spawns after the scene loads.
        if (bound == null)
        {
            if (MatchManager.Instance == null) return;
            bound = MatchManager.Instance;
            bound.PhaseChanged += Render;
            Render();
        }

        // Per-frame numeric read for the ticking display only.
        float? remaining = bound.PhaseTimeRemaining;
        switch (bound.Phase)
        {
            case MatchPhase.Countdown:
                if (countdownText != null)
                    countdownText.text = Mathf.CeilToInt(Mathf.Max(0f, remaining ?? 0f)).ToString();
                break;
            case MatchPhase.Live:
                if (matchTimerRoot != null) matchTimerRoot.SetActive(remaining.HasValue);
                if (remaining.HasValue && matchTimerText != null)
                    matchTimerText.text = FormatClock(remaining.Value);
                break;
            case MatchPhase.PostMatch:
                if (returnCountdownText != null)
                    returnCountdownText.text =
                        $"Returning to lobby in {Mathf.CeilToInt(Mathf.Max(0f, remaining ?? 0f))}…";
                break;
        }
    }

    /// <summary>Toggle which panel is visible for the current phase. Called on every PhaseChanged.</summary>
    private void Render()
    {
        if (bound == null) return;
        MatchPhase phase = bound.Phase;

        if (countdownRoot != null)
            countdownRoot.SetActive(phase == MatchPhase.Warmup || phase == MatchPhase.Countdown);
        if (countdownText != null && phase == MatchPhase.Warmup)
            countdownText.text = "Get ready…";

        if (matchTimerRoot != null)
            matchTimerRoot.SetActive(phase == MatchPhase.Live && bound.PhaseTimeRemaining.HasValue);

        bool results = phase == MatchPhase.PostMatch || phase == MatchPhase.Intermission;
        if (resultsPanel != null) resultsPanel.SetActive(results);
        if (results)
        {
            if (winnerText != null) winnerText.text = MatchResolver.WinnerLabel(bound.Winner);
            if (finalScoreText != null)
            {
                int t1 = TeamScoreManager.Instance != null ? TeamScoreManager.Instance.Team1Score : 0;
                int t2 = TeamScoreManager.Instance != null ? TeamScoreManager.Instance.Team2Score : 0;
                finalScoreText.text = $"Team 1  {t1}   —   {t2}  Team 2";
            }
            if (returnToLobbyButton != null)
                returnToLobbyButton.gameObject.SetActive(bound.LocalPlayerIsHost());
        }
    }

    private void OnReturnToLobbyClicked()
    {
        if (bound != null) bound.RPC_RequestReturnToLobby();
    }

    private void HideAll()
    {
        if (countdownRoot != null) countdownRoot.SetActive(false);
        if (matchTimerRoot != null) matchTimerRoot.SetActive(false);
        if (resultsPanel != null) resultsPanel.SetActive(false);
    }

    private static string FormatClock(float seconds)
    {
        int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return $"{s / 60:0}:{s % 60:00}";
    }
}
```

- [ ] **Step 2: Verify it compiles**

Recompile. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Hud/MatchPhaseHud.cs"
git commit -m "feat(match): add MatchPhaseHud (countdown, live timer, results panel)"
```

---

## Task 7: Scene wiring + full multi-peer verification

Wire the new objects into `Gameplay.unity` and the HUD, then run the manual verification that gates every prior task. This is editor + play-test work.

**Files:**
- Modify: `Assets/Scenes/Gameplay.unity` (scene objects — done in the editor)

**Interfaces:**
- Consumes: everything produced in Tasks 2–6.

- [ ] **Step 1: Add the MatchManager scene object**

In `Gameplay.unity`, create an empty GameObject `MatchManager`, add a `NetworkObject` component and the `MatchManager` component (same setup as the existing `TeamScoreManager`/`CTFGameManager` scene objects). Leave `warmupSeconds`/`countdownSeconds` at 3 and `postMatchSeconds` at 20.

- [ ] **Step 2: Mark MatchManager always-interested (AoI)**

If an `AreaOfInterestRegistrar` is present in the scene (see `NetworkedSpawnManager.Spawned`), add the `MatchManager`'s `NetworkObject` to its always-interested set alongside the flags and `TeamScoreManager`. Without this, distant players in a 20-player match lose the phase/timer/results HUD. If no registrar is configured yet, note it as a follow-up but leave a comment in the scene.

- [ ] **Step 3: Repurpose the game-over panel into the results panel**

The old `gameOverPanel`/`winnerText` were serialized on `CTFGameManager` and are now removed. On the HUD canvas: keep the existing panel GameObject, rename to `ResultsPanel`, and add child `TMP_Text`s for winner, final score, and return countdown, plus a `Button` "Return to Lobby". Add the `MatchPhaseHud` component to the HUD canvas root (or an empty HUD child) and wire its serialized fields:
- `countdownRoot`/`countdownText` — a center "Get ready… / 3 / 2 / 1" text.
- `matchTimerRoot`/`matchTimerText` — a top clock (hidden when no timer).
- `resultsPanel`/`winnerText`/`finalScoreText`/`returnCountdownText`/`returnToLobbyButton` — the repurposed panel.

- [ ] **Step 4: Set a match time limit for testing**

On the `GameSettingsManager` prefab/object, set `matchTimeLimit` to `1` (one minute) so the timer path is exercisable. (Production default stays `0` = capture-only if desired.)

- [ ] **Step 5: Single-player smoke test**

Set `GameNetworkManager.singlePlayerMode = true`, enter Play as Host from the MainMenu, Start. Verify:
- Warmup → Countdown "3…2…1" (input locked, enemies still) → Live (both resume, timer counts down from 1:00).
- Capture a flag → results panel shows the correct winner + final score; countdown from 20; host "Return to Lobby" button visible.
- Let the 20 s elapse → returns to the lobby; the roster is intact; Start begins a fresh match (flags home, scores 0, full health, enemies respawned).
- Let a match instead run to 0:00 with no capture → results show the higher-coin-score team, or "It's a Draw!" when equal.

- [ ] **Step 6: Multiplayer Play Mode test (1 host + 1 client)**

Per the dedicated-server / multi-peer testing guide:
- Both peers see the same countdown, live timer, and results panel.
- Only the host sees the "Return to Lobby" button; clicking it returns both peers immediately.
- Late-join into each phase (Warmup, Live, PostMatch) → the joiner sees the correct phase, remaining time, and scores.
- Host leaves during PostMatch → auto-advance still returns the remaining peer to the lobby (no dead end).
- After returning to the lobby, Start again → a fresh match for both peers with no leaked state.

- [ ] **Step 7: Commit the scene**

```bash
git add "Assets/Scenes/Gameplay.unity"
git commit -m "feat(match): wire MatchManager + results HUD into Gameplay scene"
```

---

## Self-Review

**Spec coverage:**
- End/win conditions (capture instant + `matchTimeLimit` timer + coin-score tiebreak, `scoreLimit` deferred) → Tasks 1, 2, 3. ✓
- Post-match flow (results screen → auto-advance → return to lobby, host-authorized skip, auto-advance if host leaves) → Tasks 4, 6, 7. ✓
- Match-start flow (Warmup grace + Countdown, server-authoritative via `TickTimer`) → Task 2. ✓
- Explicit networked phase enum replacing `GameIsOver`, with `OnChangedRender` → Task 2 (`Phase` + `OnPhaseChanged`); `GameIsOver` removed in Task 3. ✓
- State reset on rematch (scene-reload contract; `gameStarting` latch reset; no in-place reset) → Task 4 (`BeginReturnToLobby`) + Task 7 (fresh-match verification). ✓ The reset "audit" is the fresh-match check in Steps 5–6.
- Networking correctness (`HasStateAuthority` transitions, `TickTimer`, replicated phase, late-join per phase, always-interested AoI, 20-player) → Tasks 2, 4 (host calc from `ActivePlayers`), 7 Step 2 (AoI) + Step 6 (late-join). ✓
- HUD per phase, event-driven → Task 6. ✓
- Input/enemy freeze (spec's "input locked" rows) → Task 5. ✓

**Placeholder scan:** No "TBD"/"handle edge cases"/"similar to Task N". Deferred *manual* verifications are explicitly forwarded to Task 7 (the scene must exist first) and named there — not vague. ✓

**Type consistency:** `MatchPhase`, `Phase`, `Winner`, `PhaseTimeRemaining` (`float?`), `IsLive`, `InputEnabled`, `ReportCapture(Team)`, `RPC_RequestReturnToLobby(RpcInfo)`, `LocalPlayerIsHost()`, `BeginReturnToLobby()`, `MatchResolver.ResolveTimerWinner(int,int)`/`WinnerLabel(int)` are used identically across Tasks 2–7. Winner codes are `int`/`byte` 0/1/2 throughout (`TeamUtil.ToNumber` produces them; `MatchResolver` consumes/returns them). ✓

## Known limitations (acceptable for v1)

- After a return-to-lobby, the reloaded `MainMenuUI`'s own serialized `networkManager` reference points at the self-destructed duplicate. Harmless because the menu panel stays hidden and the persistent `GameNetworkManager` drives the lobby; the Join/Host screen is not used while already connected.
- `scoreLimit` remains unwired (deferred with the CTF-vs-coins question).

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-07-29-match-lifecycle.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach?**
