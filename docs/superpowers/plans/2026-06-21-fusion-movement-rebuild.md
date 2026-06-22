# Fusion Movement & Input Rebuild Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hand-rolled `NetworkPlayerWrapper` position sync with Fusion's authoritative input/simulation model so the game works correctly host+client, with the local player predicted and remotes interpolated.

**Architecture:** All input is collected once per tick into a single `NetInput : INetworkInput` struct (in `NetworkInputProvider.OnInput`), read in `PlayerController.FixedUpdateNetwork` via `GetInput`, and dispatched to `PlayerMovement.Simulate` / `PlayerCombat.Simulate`. The player's `Rigidbody2D` is synced by `NetworkRigidbody2D`, with `Physics2D` stepped inside Fusion's tick by `RunnerSimulatePhysics2D`. Coroutine/`Time.time`-based dash, stun, and cooldowns become `[Networked] TickTimer` state so they survive prediction and resimulation.

**Tech Stack:** Unity 6000.3.0f1, Photon Fusion 2.0.9, Fusion Physics Addon (`Fusion.Addons.Physics`), Unity Input System, C#.

## Global Constraints

- Unity **6000.3.0f1** (6.3); Photon **Fusion 2.0.9**; Host/Client mode (`PeerMode 0`); tick rate **64**.
- Input System: **new Input System only** — no `UnityEngine.Input` / `KeyCode` reads in gameplay code. The single allowed input-reading site is `NetworkInputProvider.OnInput`.
- Networked mutable state read across ticks in `FixedUpdateNetwork` MUST be `[Networked]` (so prediction/resimulation reconciles it). No `Time.time`, `Invoke`, `WaitForSeconds`, or coroutines for gameplay timing in the simulation path — use `TickTimer`.
- `Runner.Spawn` / `Runner.Despawn` and authoritative damage run under `HasStateAuthority` only.
- Verification is **manual/observational** (real-time networked physics; the project has no test assembly and game code lives in the default `Assembly-CSharp`, which an asmdef test assembly cannot reference). Each task's gate is: (a) Unity Console compiles clean, and (b) the stated in-editor check passes.

## Reference docs to skim before starting

- Fusion 2 "Network Input": https://doc.photonengine.com/fusion/current/manual/network-input
- Fusion 2 "Physics Addon" (`NetworkRigidbody2D`, `RunnerSimulatePhysics2D`): https://doc.photonengine.com/fusion/current/addons/physics/overview
- Fusion 2 `NetworkButtons`, `TickTimer` API in `Assets/Photon/Fusion/Assemblies/Fusion.Runtime.xml`.
- Unity 6 Multiplayer Play Mode: Window ▸ Multiplayer Play Mode.

## File structure

| File | Responsibility | Change |
| --- | --- | --- |
| `Assets/Scripts/Player/NetInput.cs` | Per-tick input struct + button enum | Create |
| `Assets/Scripts/Player/NetworkInputProvider.cs` | Reads local devices, fills `NetInput` in `OnInput` | Create |
| `Assets/Scripts/Player/PlayerController.cs` | Simulation driver: `GetInput` → dispatch; camera/collision binding | Rewrite |
| `Assets/Scripts/Player/PlayerMovement.cs` | Tick-based walk/jump/dash/stun on the rigidbody | Rewrite (MonoBehaviour → NetworkBehaviour) |
| `Assets/Scripts/Player/PlayerCombat.cs` | Tick-based melee + server-authoritative shoot | Modify |
| `Assets/Scripts/Player/Projectile.cs` | Server-spawned, networked projectile | Rewrite (MonoBehaviour → NetworkBehaviour) |
| `Assets/Scripts/GameNetworkManager.cs` | Add input provider + `RunnerSimulatePhysics2D` to runner | Modify |
| `Assets/Scripts/NetworkSpawnManager.cs` | Drop `NetworkPlayerWrapper` call | Modify |
| `Assets/Scripts/Player/CameraFollow.cs` | Retarget local-player lookup to `PlayerController` | Modify |
| `Assets/Scripts/Player/Playercamera.cs` | Retarget local-player lookup to `PlayerController` | Modify |
| `Assets/Scripts/Player/NetworkPlayerWrapper.cs` | Hand-rolled sync, replaced by `NetworkRigidbody2D` | Delete |
| `Assets/Scripts/Player/PlayerPrefab.prefab` | Add `NetworkRigidbody2D`, remove `NetworkPlayerWrapper` | Editor |
| `Assets/Scripts/Player/Projectile/Projectile Prefab.prefab` | Add `NetworkRigidbody2D` | Editor |

---

## Task 1: Define the `NetInput` input struct

**Files:**
- Create: `Assets/Scripts/Player/NetInput.cs`

**Interfaces:**
- Produces: `enum PlayerButton { Jump=0, Dash=1, Melee=2, Shoot=3 }`; `struct NetInput : INetworkInput` with fields `sbyte Horizontal`, `sbyte VerticalAim`, `NetworkButtons Buttons`, `Vector2 AimWorldPoint`.

- [ ] **Step 1: Create the struct**

```csharp
// Assets/Scripts/Player/NetInput.cs
using Fusion;
using UnityEngine;

/// <summary>Button indices used with NetInput.Buttons (NetworkButtons).</summary>
public enum PlayerButton
{
    Jump = 0,
    Dash = 1,
    Melee = 2,
    Shoot = 3,
}

/// <summary>All per-tick player input, collected in NetworkInputProvider.OnInput
/// and consumed in PlayerController.FixedUpdateNetwork.</summary>
public struct NetInput : INetworkInput
{
    public sbyte Horizontal;     // -1 / 0 / 1
    public sbyte VerticalAim;    // -1 / 0 / 1 (for up/down attacks)
    public NetworkButtons Buttons;
    public Vector2 AimWorldPoint; // mouse world position for projectile aim
}
```

- [ ] **Step 2: Compile**

In Unity, let the Console recompile. Expected: no errors referencing `NetInput`.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Player/NetInput.cs" "Assets/Scripts/Player/NetInput.cs.meta"
git commit -m "feat(net): add NetInput per-tick input struct"
```

---

## Task 2: Add input provider + Fusion-stepped physics to the runner

**Files:**
- Create: `Assets/Scripts/Player/NetworkInputProvider.cs`
- Modify: `Assets/Scripts/GameNetworkManager.cs`

**Interfaces:**
- Consumes: `NetInput`, `PlayerButton` (Task 1).
- Produces: `NetworkInputProvider` (a `MonoBehaviour, INetworkRunnerCallbacks`) registered on the runner; the runner now carries `RunnerSimulatePhysics2D`.

- [ ] **Step 1: Create the input provider**

```csharp
// Assets/Scripts/Player/NetworkInputProvider.cs
using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The ONLY place that reads local input devices. Fusion calls OnInput each tick
/// on the local client to poll input for the input-authority player.
/// </summary>
public class NetworkInputProvider : MonoBehaviour, INetworkRunnerCallbacks
{
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new NetInput();

        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        var gamepad = Gamepad.current;

        // Horizontal (-1/0/1)
        float h = 0f;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) h -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) h += 1f;
        }
        if (gamepad != null)
        {
            float gx = gamepad.leftStick.ReadValue().x;
            if (Mathf.Abs(gx) > 0.2f) h = Mathf.Sign(gx);
        }
        data.Horizontal = (sbyte)Mathf.Clamp(Mathf.RoundToInt(h), -1, 1);

        // Vertical aim (-1/0/1)
        float v = 0f;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) v += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) v -= 1f;
        }
        if (gamepad != null)
        {
            float gy = gamepad.leftStick.ReadValue().y;
            if (Mathf.Abs(gy) > 0.2f) v = Mathf.Sign(gy);
        }
        data.VerticalAim = (sbyte)Mathf.Clamp(Mathf.RoundToInt(v), -1, 1);

        // Buttons
        bool jump  = (keyboard != null && keyboard.spaceKey.isPressed)    || (gamepad != null && gamepad.buttonNorth.isPressed);
        bool dash  = (keyboard != null && keyboard.leftShiftKey.isPressed) || (gamepad != null && gamepad.rightShoulder.isPressed);
        bool melee = (mouse != null && mouse.leftButton.isPressed)         || (keyboard != null && keyboard.leftCtrlKey.isPressed) || (gamepad != null && gamepad.buttonSouth.isPressed);
        bool shoot = (mouse != null && mouse.rightButton.isPressed)        || (keyboard != null && keyboard.leftAltKey.isPressed)  || (gamepad != null && gamepad.buttonWest.isPressed);

        data.Buttons.Set((int)PlayerButton.Jump,  jump);
        data.Buttons.Set((int)PlayerButton.Dash,  dash);
        data.Buttons.Set((int)PlayerButton.Melee, melee);
        data.Buttons.Set((int)PlayerButton.Shoot, shoot);

        // Aim world point (for projectiles); PlayerCombat turns this into a direction
        // relative to its spawn point at sim time.
        Vector2 aimWorld = Vector2.zero;
        if (mouse != null && Camera.main != null)
        {
            Vector3 mw = Camera.main.ScreenToWorldPoint(mouse.position.ReadValue());
            aimWorld = new Vector2(mw.x, mw.y);
        }
        data.AimWorldPoint = aimWorld;

        input.Set(data);
    }

    // --- Unused INetworkRunnerCallbacks members ---
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
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

- [ ] **Step 2: Wire provider + physics into the runner in `GameNetworkManager`**

In `Assets/Scripts/GameNetworkManager.cs`, add the Physics addon namespace at the top with the other usings:

```csharp
using Fusion.Addons.Physics;
```

Replace the `Start()` body's runner-creation line so the runner also gets physics stepping and the input provider. Change:

```csharp
        DontDestroyOnLoad(gameObject);
        runner = gameObject.AddComponent<NetworkRunner>();
```

to:

```csharp
        DontDestroyOnLoad(gameObject);
        runner = gameObject.AddComponent<NetworkRunner>();

        // Fusion steps Physics2D inside the network tick (required for NetworkRigidbody2D prediction).
        gameObject.AddComponent<RunnerSimulatePhysics2D>();

        // Register the single input source.
        var inputProvider = gameObject.AddComponent<NetworkInputProvider>();
        runner.AddCallbacks(inputProvider);
```

- [ ] **Step 3: Compile**

Expected: Console clean. (`RunnerSimulatePhysics2D` resolves from `Fusion.Addons.Physics`.)

- [ ] **Step 4: Verify input is collected (host-only smoke)**

Temporarily add to `OnInput` (first line of body) `if (data.Horizontal != 0) Debug.Log($"input H={data.Horizontal}");` — actually verify without edits: Press Play with `singlePlayerMode` on, click Host. Confirm no errors and the game still runs (movement still uses the OLD path until Task 3; that is expected). The check here is only: **the project runs with the provider + physics component present and no console errors**.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Player/NetworkInputProvider.cs" "Assets/Scripts/Player/NetworkInputProvider.cs.meta" "Assets/Scripts/GameNetworkManager.cs"
git commit -m "feat(net): collect input via NetworkInputProvider and enable Fusion-stepped physics"
```

---

## Task 3: Player cutover — `NetworkRigidbody2D` + tick-based movement

This is the core task. After it, the player moves under Fusion simulation and the hand-rolled wrapper is gone. It groups the movement rewrite, controller rewrite, wrapper deletion, dependent-reference fixes, and prefab/runner changes because none of them is independently runnable.

**Files:**
- Rewrite: `Assets/Scripts/Player/PlayerMovement.cs`
- Rewrite: `Assets/Scripts/Player/PlayerController.cs`
- Modify: `Assets/Scripts/NetworkSpawnManager.cs:198-213`
- Modify: `Assets/Scripts/Player/CameraFollow.cs`
- Modify: `Assets/Scripts/Player/Playercamera.cs`
- Delete: `Assets/Scripts/Player/NetworkPlayerWrapper.cs` (+ `.meta`)
- Editor: `Assets/Scripts/Player/PlayerPrefab.prefab`

**Interfaces:**
- Consumes: `NetInput`, `PlayerButton` (Task 1).
- Produces:
  - `PlayerMovement : NetworkBehaviour` with `void Simulate(NetInput input, NetworkButtons pressed, NetworkButtons released)`, `void ApplyStun(float duration)`, `bool IsDashing()`, `bool IsStunned()`, `float GetDashCooldownPercent()`.
  - `PlayerController : NetworkBehaviour` (drives simulation; binds camera/collision in `Spawned`).

- [ ] **Step 1: Rewrite `PlayerMovement.cs`**

Replace the entire file with:

```csharp
using UnityEngine;
using Fusion;

/// <summary>
/// Tick-based, networked player movement. Driven by PlayerController.FixedUpdateNetwork.
/// All gameplay timing uses TickTimer / networked counters so prediction + resimulation
/// reconcile correctly. NetworkRigidbody2D (on the prefab) syncs the body.
/// </summary>
public class PlayerMovement : NetworkBehaviour
{
    [Header("Stats")]
    [SerializeField] private PlayerStats stats;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Jump Settings")]
    [SerializeField] private int coyoteTimeTicks = 6;
    [SerializeField] private int jumpBufferTicks = 6;
    [SerializeField] private float jumpCutMultiplier = 0.1f;

    [Header("UI References")]
    [SerializeField] private UnityEngine.UI.Image dashCooldownBar;

    // Component refs
    private Rigidbody2D rb;
    private Animator anim;
    private FlagCarrierMarker flagCarrierMarker;
    private float baseGravity = 5f;

    // Networked simulation state
    [Networked] private int RemainingAirJumps { get; set; }
    [Networked] private int CoyoteCounter { get; set; }
    [Networked] private int JumpBufferCounter { get; set; }
    [Networked] private NetworkBool Jumping { get; set; }
    [Networked] private NetworkBool JumpCut { get; set; }
    [Networked] private NetworkBool Dashing { get; set; }
    [Networked] private float DashDir { get; set; }
    [Networked] private NetworkBool FacingRight { get; set; }
    [Networked] private TickTimer DashDurationTimer { get; set; }
    [Networked] private TickTimer DashCooldownTimer { get; set; }
    [Networked] private TickTimer StunTimer { get; set; }

    public override void Spawned()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponentInChildren<Animator>();
        flagCarrierMarker = GetComponent<FlagCarrierMarker>();
        if (rb != null) baseGravity = rb.gravityScale;

        if (HasStateAuthority)
        {
            FacingRight = transform.localScale.x >= 0f;
            RemainingAirJumps = stats.maxAirJumps;
        }
    }

    /// <summary>Called every tick by PlayerController when input is available.</summary>
    public void Simulate(NetInput input, NetworkButtons pressed, NetworkButtons released)
    {
        if (rb == null) return;

        bool grounded = groundCheck != null &&
                        Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        bool stunned = IsStunned();

        // Resolve dash lifetime first (pure function of networked timers).
        if (Dashing && DashDurationTimer.ExpiredOrNotRunning(Runner))
            EndDash();

        // Gravity is a pure function of dash state (resimulation-safe).
        rb.gravityScale = Dashing ? 0f : baseGravity;

        // ---- Horizontal velocity ----
        if (Dashing)
        {
            rb.linearVelocity = new Vector2(DashDir * stats.dashSpeed, 0f);
        }
        else if (stunned)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
        else
        {
            rb.linearVelocity = new Vector2(input.Horizontal * stats.walkSpeed, rb.linearVelocity.y);
        }

        // ---- Facing ----
        if (input.Horizontal < 0) FacingRight = false;
        else if (input.Horizontal > 0) FacingRight = true;
        ApplyFacing();

        // ---- Coyote / air jumps ----
        if (grounded)
        {
            CoyoteCounter = coyoteTimeTicks;
            RemainingAirJumps = stats.maxAirJumps;
            if (Jumping && rb.linearVelocity.y <= 0.01f) Jumping = false;
        }
        else if (CoyoteCounter > 0)
        {
            CoyoteCounter--;
        }

        // ---- Dash start / cancel ----
        if (!stunned && pressed.IsSet((int)PlayerButton.Dash) && !Dashing &&
            DashCooldownTimer.ExpiredOrNotRunning(Runner))
        {
            bool carrying = flagCarrierMarker != null && flagCarrierMarker.IsCarryingFlag();
            if (!carrying) StartDash();
        }
        if (released.IsSet((int)PlayerButton.Dash) && Dashing)
            EndDash();

        // ---- Jump buffer ----
        if (!stunned && pressed.IsSet((int)PlayerButton.Jump))
        {
            JumpBufferCounter = jumpBufferTicks;
            if (Dashing) EndDash(); // jump cancels dash
        }
        else if (JumpBufferCounter > 0)
        {
            JumpBufferCounter--;
        }

        if (!stunned && JumpBufferCounter > 0 && (CoyoteCounter > 0 || RemainingAirJumps > 0))
        {
            DoJump(grounded);
            JumpBufferCounter = 0;
        }

        // ---- Variable jump height (release cuts upward velocity) ----
        if (released.IsSet((int)PlayerButton.Jump) && rb.linearVelocity.y > 0f && Jumping && !JumpCut)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);
            JumpCut = true;
        }
    }

    private void DoJump(bool grounded)
    {
        if (grounded || CoyoteCounter > 0)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, stats.jumpForce);
            CoyoteCounter = 0;
        }
        else if (RemainingAirJumps > 0)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, stats.jumpForce);
            RemainingAirJumps--;
        }
        Jumping = true;
        JumpCut = false;
        if (anim != null) anim.SetTrigger("Jump");
    }

    private void StartDash()
    {
        Dashing = true;
        DashDir = FacingRight ? 1f : -1f;
        DashDurationTimer = TickTimer.CreateFromSeconds(Runner, stats.dashTime);
        rb.linearVelocity = new Vector2(DashDir * stats.dashSpeed, 0f);
    }

    private void EndDash()
    {
        if (!Dashing) return;
        Dashing = false;
        DashCooldownTimer = TickTimer.CreateFromSeconds(Runner, stats.dashCooldown);
    }

    /// <summary>SERVER: stun the player for a duration (set by projectile hits).</summary>
    public void ApplyStun(float duration)
    {
        if (!HasStateAuthority) return;
        StunTimer = TickTimer.CreateFromSeconds(Runner, duration);
        if (Dashing) EndDash();
    }

    private void ApplyFacing()
    {
        Vector3 s = transform.localScale;
        float mag = Mathf.Abs(s.x);
        s.x = FacingRight ? mag : -mag;
        transform.localScale = s;
    }

    public override void Render()
    {
        if (rb == null) return;
        ApplyFacing();

        if (anim != null)
        {
            anim.SetBool("Walking", Mathf.Abs(rb.linearVelocity.x) > 0.1f && !Dashing);
            anim.SetBool("Dashing", Dashing);
        }

        if (dashCooldownBar != null && HasInputAuthority)
            dashCooldownBar.fillAmount = GetDashCooldownPercent();
    }

    // ---- Public accessors (used by other scripts) ----
    public bool IsDashing() => Dashing;
    public bool IsStunned() => !StunTimer.ExpiredOrNotRunning(Runner);

    public float GetDashCooldownPercent()
    {
        if (stats.dashCooldown <= 0f) return 1f;
        float remaining = DashCooldownTimer.RemainingTime(Runner) ?? 0f;
        return 1f - Mathf.Clamp01(remaining / stats.dashCooldown);
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}
```

- [ ] **Step 2: Rewrite `PlayerController.cs`**

Replace the entire file with:

```csharp
using UnityEngine;
using Fusion;

[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerCombat))]
public class PlayerController : NetworkBehaviour
{
    private PlayerMovement movement;
    private PlayerCombat combat;
    private NetworkButtons previousButtons;

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        combat = GetComponent<PlayerCombat>();
    }

    public override void Spawned()
    {
        // Bind the camera to the local player only.
        if (HasInputAuthority)
        {
            CameraFollow cam = FindFirstObjectByType<CameraFollow>();
            if (cam != null) cam.SetTarget(transform);
        }

        StartCoroutine(SetupTeammateCollisionsWhenReady());
    }

    public override void FixedUpdateNetwork()
    {
        if (GetInput(out NetInput input))
        {
            NetworkButtons current = input.Buttons;
            NetworkButtons pressed = current.GetPressed(previousButtons);
            NetworkButtons released = current.GetReleased(previousButtons);
            previousButtons = current;

            // Respect the death/respawn freeze: PlayerStatsHandler disables these
            // components to lock controls. Since we call them directly (not via Fusion),
            // we must honor their enabled flag here.
            if (movement.enabled) movement.Simulate(input, pressed, released);
            if (combat.enabled) combat.Simulate(input, pressed);
        }
    }

    // Ignore collisions between same-team players (replaces NetworkPlayerWrapper's coroutine).
    // Local physics decision; identical on every client because team data is networked.
    private System.Collections.IEnumerator SetupTeammateCollisionsWhenReady()
    {
        PlayerTeamData myTeam = GetComponent<PlayerTeamData>();
        Collider2D myCol = GetComponent<Collider2D>();
        if (myTeam == null || myCol == null) yield break;

        float timeout = 5f;
        while (myTeam.Team == 0 && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
        if (myTeam.Team == 0) yield break;

        var players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        foreach (var other in players)
        {
            if (other == this) continue;
            PlayerTeamData otherTeam = other.GetComponent<PlayerTeamData>();
            Collider2D otherCol = other.GetComponent<Collider2D>();
            if (otherTeam != null && otherCol != null && otherTeam.Team == myTeam.Team)
                Physics2D.IgnoreCollision(myCol, otherCol, true);
        }
    }
}
```

- [ ] **Step 3: Drop the wrapper call in `NetworkSpawnManager.cs`**

In `OnPlayerSpawned` (around lines 198-213), remove the `NetworkPlayerWrapper` block. The final method body is:

```csharp
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
```

- [ ] **Step 4: Retarget `CameraFollow.cs` to `PlayerController`**

Make these four edits:

Line ~40, change the locked-player field type:
```csharp
    private PlayerController lockedPlayer; // local player we follow
```

In `SearchForPlayer()` (~line 99), change the lookup:
```csharp
            PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);

            foreach (var player in players)
            {
                if (player.HasInputAuthority)
                {
                    SetTarget(player.transform);
                    lockedPlayer = player;
                    Debug.Log($"✓ CameraFollow: locked to LOCAL player: {player.name}");
                    yield break;
                }
            }
```

In `SetTarget(Transform newTarget)` (~line 195), change the validation:
```csharp
        PlayerController playerController = newTarget.GetComponent<PlayerController>();
        if (playerController != null && !playerController.HasInputAuthority)
        {
            Debug.LogWarning($"⚠️ CameraFollow: target {newTarget.name} is not the local player. Ignoring.");
            return;
        }

        Target = newTarget;
        lockedPlayer = playerController;
```

(The `lockedPlayer.HasInputAuthority` reads in `SearchForPlayer`/`LateUpdate` work unchanged — `PlayerController` inherits `HasInputAuthority` from `NetworkBehaviour`.)

- [ ] **Step 5: Retarget `Playercamera.cs` to `PlayerController`**

In `FindLocalPlayer()` (~lines 205-235), change the lookup type:
```csharp
        PlayerController[] allPlayers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);

        if (allPlayers.Length == 0)
            return;

        foreach (PlayerController player in allPlayers)
        {
            if (player.HasInputAuthority)
            {
                targetPlayer = player.transform;
                targetRigidbody = player.GetComponent<Rigidbody2D>();

                currentFollowPosition = targetPlayer.position;
                currentFollowPosition.z = cameraZPosition;
                transform.position = currentFollowPosition;
                return;
            }
        }
```

- [ ] **Step 6: Delete `NetworkPlayerWrapper.cs`**

```bash
git rm "Assets/Scripts/Player/NetworkPlayerWrapper.cs" "Assets/Scripts/Player/NetworkPlayerWrapper.cs.meta"
```

- [ ] **Step 7: Compile**

Expected: Console clean. If "type or namespace `NetworkPlayerWrapper`" errors remain, re-grep for stragglers: `git grep -n NetworkPlayerWrapper -- "Assets/**/*.cs"` should return nothing.

- [ ] **Step 8: Prefab edits (Unity Editor)**

Open `Assets/Scripts/Player/PlayerPrefab.prefab` in the Prefab editor:
1. **Remove** the `Network Player Wrapper` component (it shows as "missing script" after deletion — remove it).
2. **Add Component ▸ Network Rigidbody 2D** (`Fusion.Addons.Physics.NetworkRigidbody2D`). It auto-references the existing `Rigidbody2D`.
3. Select the `Rigidbody2D`: set **Interpolate = None** and **Collision Detection = Continuous** (NetworkRigidbody2D drives interpolation; leave Body Type = Dynamic).
4. Confirm component order top-to-bottom: `NetworkObject` → `NetworkRigidbody2D` → gameplay scripts (`PlayerController`, `PlayerMovement`, `PlayerCombat`, etc.).
5. Re-assign any now-empty serialized references the rewritten `PlayerMovement` exposes in the Inspector (Stats, Ground Check transform, Ground Layer, Dash Cooldown Bar) — they carry over by field name, but verify none reset to None.
6. Save the prefab.

- [ ] **Step 9: Verify — single-player feel (host-only)**

`singlePlayerMode` on, press Play, Host, pick a team. Confirm against the pre-rebuild baseline:
- walk speed and direction-facing flip,
- single + double jump, coyote grace, jump buffering (press just before landing), variable jump height (tap vs hold),
- dash distance/direction, dash cooldown bar fills, dash cancels on jump and on shift-release, dash blocked while carrying a flag,
- no Console errors; player no longer has `NetworkPlayerWrapper`.

- [ ] **Step 10: Verify — MPPM host + client**

Window ▸ Multiplayer Play Mode: enable 1 virtual player. Turn `singlePlayerMode` **off**. Run; Host in main editor, the virtual player clicks Client and picks a team. Confirm:
- each window drives only its own character (no ghost input),
- local movement is instant; the remote character moves smoothly with no rubber-banding,
- jump/dash/facing replicate both directions,
- camera in each window follows that window's local player.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "feat(net): drive player movement from FixedUpdateNetwork via NetworkRigidbody2D; remove NetworkPlayerWrapper"
```

---

## Task 4: Combat input via simulation + server-authoritative melee

**Files:**
- Modify: `Assets/Scripts/Player/PlayerCombat.cs`

**Interfaces:**
- Consumes: `NetInput`, `PlayerButton` (Task 1); called by `PlayerController.FixedUpdateNetwork` (Task 3).
- Produces: `PlayerCombat.Simulate(NetInput input, NetworkButtons pressed)`; melee detection/damage runs under `HasStateAuthority`.

- [ ] **Step 1: Replace input handling and cooldowns in `PlayerCombat.cs`**

Remove the `using UnityEngine.InputSystem;` line, the `Update()` method, the entire `HandleInput()` method, and the `timeSinceAttack` / `timeSinceProjectile` float fields. Add networked cooldown timers next to the other fields:

```csharp
    [Networked] private TickTimer AttackCooldownTimer { get; set; }
    [Networked] private TickTimer ShootCooldownTimer { get; set; }
    private int verticalAim;
```

Add the simulation entry point (called by the controller):

```csharp
    /// <summary>Called every tick by PlayerController when input is available.</summary>
    public void Simulate(NetInput input, NetworkButtons pressed)
    {
        verticalAim = input.VerticalAim;

        if (pressed.IsSet((int)PlayerButton.Melee) && AttackCooldownTimer.ExpiredOrNotRunning(Runner))
        {
            AttackCooldownTimer = TickTimer.CreateFromSeconds(Runner, attackCooldown);
            Attack();
        }

        if (pressed.IsSet((int)PlayerButton.Shoot) && ShootCooldownTimer.ExpiredOrNotRunning(Runner))
        {
            ShootCooldownTimer = TickTimer.CreateFromSeconds(Runner, projectileCooldown);
            ShootProjectile(input.AimWorldPoint);
        }
    }
```

- [ ] **Step 2: Gate melee detection to the server in `Attack()`**

In `Attack()`, replace the use of the old `yAxis` field with `verticalAim`, play the animation everywhere, but run the `OverlapBoxAll` detection + damage + hit-markers **only on the state authority**. The method becomes:

```csharp
    private void Attack()
    {
        Transform attackTransform = null;
        Vector2 attackArea = Vector2.zero;
        string attackDirection = "side";

        if (verticalAim > 0 && upAttackPoint != null)
        {
            attackTransform = upAttackPoint;
            attackArea = upAttackArea;
            attackDirection = "up";
        }
        else if (verticalAim < 0 && downAttackPoint != null)
        {
            bool isGrounded = groundCheck != null &&
                              Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
            if (!isGrounded)
            {
                attackTransform = downAttackPoint;
                attackArea = downAttackArea;
                attackDirection = "down";
                if (useGroundPound)
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, -groundPoundForce);
            }
            else
            {
                attackTransform = sideAttackPoint;
                attackArea = sideAttackArea;
            }
        }
        else
        {
            attackTransform = sideAttackPoint;
            attackArea = sideAttackArea;
        }

        if (attackTransform == null) return;

        if (anim != null)
        {
            anim.SetTrigger("Attack");
            anim.SetBool("AttackingUp", attackDirection == "up");
            anim.SetBool("AttackingDown", attackDirection == "down");
        }

        // Damage + hit detection only on the server (avoids double-apply across clients).
        if (!HasStateAuthority) return;

        Collider2D[] objectsHit = Physics2D.OverlapBoxAll(
            attackTransform.position, attackArea, 0f, attackableLayer);

        foreach (Collider2D hit in objectsHit)
        {
            if (hitMarkerPrefab != null)
            {
                GameObject marker = Instantiate(hitMarkerPrefab, hit.transform.position, Quaternion.identity);
                SpriteRenderer sr = marker.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = hitMarkerColor;
                Destroy(marker, hitMarkerDuration);
            }

            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null)
            {
                Vector2 knockbackDirection = (hit.transform.position - transform.position).normalized;
                Vector2 knockbackForce = new Vector2(knockbackDirection.x * knockbackStrength, knockbackUpward);
                enemy.TakeDamage(damageAmount, knockbackForce, hit.transform.position);
            }
        }
    }
```

(The `isGroundPounding` field is no longer read — leave it or delete it; the plan deletes it: remove the `private bool isGroundPounding = false;` field.)

- [ ] **Step 3: Compile**

Expected: Console clean. (Task 5 changes `ShootProjectile`'s signature; for now update its signature to accept the aim point so it compiles — do Step 4.)

- [ ] **Step 4: Make `ShootProjectile` accept the aim world point (signature only this task)**

Change the method signature and aim computation; keep the existing spawn logic for now (Task 5 hardens it):

```csharp
    private void ShootProjectile(Vector2 aimWorldPoint)
    {
        if (anim != null) anim.SetTrigger("Shoot");
        if (projectilePrefab == null || projectileSpawnPoint == null) return;
        if (!HasStateAuthority) return; // only the server spawns networked objects

        Vector2 aimDirection = (aimWorldPoint - (Vector2)projectileSpawnPoint.position).normalized;
        string shooterTeam = teamComponent != null ? teamComponent.teamID : "";

        Runner.Spawn(
            projectilePrefab,
            projectileSpawnPoint.position,
            Quaternion.identity,
            Object.InputAuthority,
            (runner, obj) =>
            {
                obj.transform.localScale = Vector3.one * projectileScale;
                Projectile p = obj.GetComponent<Projectile>();
                if (p != null) p.Initialize(aimDirection, projectileSpeed, projectileDamage, shooterTeam);
            });
    }
```

(`Projectile.Initialize` still exists at this point; Task 5 replaces it with `ServerInitialize`. This task keeps it compiling.)

- [ ] **Step 5: Compile, then verify**

Expected: Console clean. Single-player: melee swings and damages/knockbacks enemies; up/down/side attacks trigger by holding W/S; attack respects cooldown. MPPM: a client attacker's melee damages an enemy exactly once (watch the enemy health/Debug log on the host — no double-damage).

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/Player/PlayerCombat.cs"
git commit -m "feat(net): drive combat from tick simulation; server-authoritative melee"
```

---

## Task 5: Server-authoritative networked projectile

**Files:**
- Rewrite: `Assets/Scripts/Player/Projectile.cs`
- Modify: `Assets/Scripts/Player/PlayerCombat.cs` (call `ServerInitialize`)
- Editor: `Assets/Scripts/Player/Projectile/Projectile Prefab.prefab`

**Interfaces:**
- Consumes: spawned by `PlayerCombat.ShootProjectile` on the server (Task 4).
- Produces: `Projectile : NetworkBehaviour` with `void ServerInitialize(Vector2 dir, float speed, int damage, string team)`.

- [ ] **Step 1: Rewrite `Projectile.cs`**

Replace the entire file with:

```csharp
using UnityEngine;
using Fusion;

/// <summary>
/// Server-spawned networked projectile. Velocity is set on the server and synced by
/// NetworkRigidbody2D; hit detection, damage, stun, and despawn run on the state authority.
/// Full friendly-fire/effects polish is a later pass — this is the minimal correct version.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class Projectile : NetworkBehaviour
{
    [Header("Stun")]
    [SerializeField] private float stunDuration = 1.5f;
    [SerializeField] private bool stunPlayers = true;

    [Header("Visual Effects")]
    [SerializeField] private GameObject impactEffect;

    [Networked] private Vector2 Direction { get; set; }
    [Networked] private float Speed { get; set; }
    [Networked] private int Damage { get; set; }
    [Networked] private NetworkString<_16> ShooterTeam { get; set; }

    private Rigidbody2D rb;
    private bool hasHit;

    /// <summary>SERVER: set from PlayerCombat's spawn callback before Spawned runs.</summary>
    public void ServerInitialize(Vector2 dir, float speed, int damage, string team)
    {
        Direction = dir.normalized;
        Speed = speed;
        Damage = damage;
        ShooterTeam = team;
    }

    public override void Spawned()
    {
        rb = GetComponent<Rigidbody2D>();
        var col = GetComponent<CircleCollider2D>();
        if (col != null) col.isTrigger = true;
        if (rb != null) rb.gravityScale = 1f;

        if (HasStateAuthority && rb != null)
            rb.linearVelocity = Direction * Speed;
    }

    public override void Render()
    {
        if (rb != null && rb.linearVelocity.sqrMagnitude > 0.01f)
        {
            float angle = Mathf.Atan2(rb.linearVelocity.y, rb.linearVelocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!HasStateAuthority || hasHit) return;

        // Player hit (skip same team)
        PlayerStatsHandler playerStats = other.GetComponent<PlayerStatsHandler>();
        if (playerStats != null)
        {
            PlayerTeamComponent pt = other.GetComponent<PlayerTeamComponent>();
            bool friendly = pt != null && pt.teamID == ShooterTeam.ToString();
            if (!friendly)
            {
                playerStats.RPC_TakeDamage(Damage);
                if (stunPlayers)
                {
                    PlayerMovement pm = other.GetComponent<PlayerMovement>();
                    if (pm != null) pm.ApplyStun(stunDuration);
                }
                Hit();
            }
            return;
        }

        // Enemy hit
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy != null)
        {
            Vector2 dir = ((Vector2)other.transform.position - (Vector2)transform.position).normalized;
            enemy.TakeDamage(Damage, dir * 5f, other.transform.position);
            Hit();
            return;
        }

        // Ground / wall
        if (other.gameObject.layer == LayerMask.NameToLayer("Ground") || other.CompareTag("Wall"))
            Hit();
    }

    private void Hit()
    {
        if (hasHit) return;
        hasHit = true;
        if (impactEffect != null) RPC_Impact(transform.position);
        Runner.Despawn(Object);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_Impact(Vector3 position)
    {
        if (impactEffect != null)
        {
            GameObject fx = Instantiate(impactEffect, position, Quaternion.identity);
            Destroy(fx, 2f);
        }
    }
}
```

- [ ] **Step 2: Update the spawn callback in `PlayerCombat.ShootProjectile`**

Change the one line inside the `Runner.Spawn` callback from `p.Initialize(...)` to:

```csharp
                if (p != null) p.ServerInitialize(aimDirection, projectileSpeed, projectileDamage, shooterTeam);
```

- [ ] **Step 3: Compile**

Expected: Console clean. `git grep -n "\.Initialize(" -- "Assets/Scripts/Player/PlayerCombat.cs"` returns nothing.

- [ ] **Step 4: Projectile prefab edit (Unity Editor)**

Open `Assets/Scripts/Player/Projectile/Projectile Prefab.prefab`:
1. Confirm it has a `NetworkObject` (it is spawned as one). 
2. **Add Component ▸ Network Rigidbody 2D**.
3. On the `Rigidbody2D`: Interpolate = None, Collision Detection = Continuous.
4. Re-assign the `Projectile` script's serialized fields if the Inspector shows them reset (Stun Duration, Stun Players, Impact Effect).
5. Save.

- [ ] **Step 5: Verify (MPPM)**

With host + virtual client, both players shoot (right mouse / left-alt). Confirm: the projectile appears in **both** windows, flies toward the aimed point, damages + stuns an enemy player on hit, and despawns on impact with no Console errors (no "Destroy on a NetworkObject" warnings).

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/Player/Projectile.cs" "Assets/Scripts/Player/PlayerCombat.cs"
git commit -m "feat(net): server-authoritative networked projectile"
```

---

## Task 6: Full verification pass + cleanup

**Files:**
- Modify (if needed): any file with leftover temporary `Debug.Log` added during bring-up.

- [ ] **Step 1: Remove temporary debug logging**

Search for any debug lines added during bring-up and remove them:
```bash
git grep -n "input H=" -- "Assets/**/*.cs"
```
Expected after removal: nothing.

- [ ] **Step 2: Single-player smoke (regression)**

Re-run the Task 3 Step 9 checklist end-to-end. All movement feel matches baseline.

- [ ] **Step 3: MPPM full checklist**

Host + 1 virtual client. Verify the full spec checklist: independent control (no ghost input), prediction (instant local) + interpolation (smooth remote), dash/jump/facing/cooldown-bar replication, shooting visible to both with correct aim, melee single-damage, team assignment + spawn position + camera-follow all bound to the correct player.

- [ ] **Step 4: Lag simulation**

Edit `Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion`: set `NetworkConditions.Enabled` to `true`, `DelayMin`/`DelayMax` to `0.15`, keep `LossChanceMin/Max` `0.05`. Re-run the MPPM checklist. Watch for visible snap/correction on the local player during jump/dash. If correction is jarring, note it (tuning is acceptable follow-up; functional correctness is the gate). Revert `Enabled` to `false` (or leave for ongoing testing) and commit only if you intentionally keep it on.

- [ ] **Step 5: Build + Editor on Photon cloud**

Make a standalone build (`singlePlayerMode` off). Run the build as Host and the Editor as Client (or vice versa) against the real Photon relay. Confirm two real peers connect, both players move/shoot correctly, and there is no divergence. This validates the cloud session path MPPM can't exercise.

- [ ] **Step 6: Final commit**

```bash
git add -A
git commit -m "chore(net): remove bring-up debug logging after movement rebuild verification"
```

---

## Self-review notes

- **Spec coverage:** NetInput struct (Task 1) ✓; NetworkInputProvider + RunnerSimulatePhysics2D + real client mode (Task 2; `StartClient` already branches to real `GameMode.Client` when `singlePlayerMode` is off, so no code change was needed there) ✓; NetworkRigidbody2D + tick simulation + wrapper removal + facing networked + camera/collision relocation (Task 3) ✓; combat via input + server melee (Task 4) ✓; server-spawned networked projectile + stun networked (Tasks 4-5) ✓; layered testing incl. lag sim + cloud build (Task 6) ✓.
- **Known interim limitations (consistent with spec scope):** remote-player one-shot animation triggers (Attack/Jump/Shoot) are driven locally where simulation runs, so a remote observer may not see every one-shot swing; walk/dash animation IS synced via networked state in `Render`. Enemy AI position reconciliation remains item #5. Melee hit-markers spawn on the server only. These are explicitly out of scope for item #1.
- **Death/respawn freeze:** `PlayerStatsHandler` disables `PlayerMovement`/`PlayerCombat` via `.enabled` on death and re-enables on respawn. Because `PlayerController` now invokes `Simulate(...)` directly, the dispatch checks `movement.enabled`/`combat.enabled` so the existing freeze still works without changing `PlayerStatsHandler`.
- **Type consistency:** `Simulate(NetInput, NetworkButtons, NetworkButtons)` on `PlayerMovement` and `Simulate(NetInput, NetworkButtons)` on `PlayerCombat` match their call sites in `PlayerController.FixedUpdateNetwork`. `ServerInitialize` (Task 5) replaces `Initialize` at its single call site. `PlayerMovement.ApplyStun` / `IsDashing` / `IsStunned` public names preserved for external callers.
