# Fusion Movement & Input Rebuild — Design

**Date:** 2026-06-21
**Status:** Approved (pending written-spec review)
**Scope:** Review item #1 — rebuild player movement and input on Photon Fusion's
authoritative input/simulation model.

## Problem

The player is synced by a hand-rolled component (`NetworkPlayerWrapper`) that writes
`[Networked]` position/velocity from `FixedUpdateNetwork` whenever
`HasStateAuthority || HasInputAuthority`. In Host mode only the host holds state
authority over player objects, so a client's writes are discarded and never replicate —
the foundational reason the game has only ever run host-only.

Compounding issues this rebuild fixes at the root:

- `PlayerMovement` is a `MonoBehaviour` whose `Update()`/`FixedUpdate()` read input on
  **every** copy of the player on every client, and also double-process the local
  player's input alongside `PlayerController` (the "ghost input" symptom).
- Two input systems are mixed: `PlayerMovement` uses the legacy Input Manager,
  `PlayerCombat` uses the new Input System.
- `PlayerCombat.ShootProjectile` calls `Runner.Spawn` directly on the client
  (`PlayerCombat.cs:248`), which is invalid for non-host clients.
- `Render()` writes physics state on remote proxies (`NetworkPlayerWrapper.cs:62-88`).
- Dash/stun/respawn use coroutines, `Invoke`, and `Time.time`, none of which survive
  Fusion prediction/resimulation.

## Environment (confirmed)

- Unity **6000.3.0f1** (6.3) → Multiplayer Play Mode (MPPM) available.
- Photon **Fusion 2.0.9**, Host/Client (`PeerMode 0`), tick rate **64**.
- **Fusion Physics Addon installed** (`Fusion.Addons.Physics` in the weave list) →
  `NetworkRigidbody2D` and `RunnerSimulatePhysics2D` are available.
- `InputDataWordCount: 0` and all `OnInput` callbacks are empty stubs → no input struct
  registered yet.

## Decisions

| Decision | Choice |
| --- | --- |
| Input scope | **All input** — move, jump, dash, melee, shoot — through one struct |
| Physics/sync model | **`NetworkRigidbody2D` + Fusion-stepped physics** (`RunnerSimulatePhysics2D`) |
| Topology | Keep **Host/Client** mode; local player predicted, remotes interpolated |
| Input system | Standardize on the **new Input System**; retire legacy `Input.*` reads |
| Testing | Layered: single-player smoke → MPPM host+client → lag sim → build+editor on cloud |

### Approaches considered

- **A. NetworkRigidbody2D + Fusion-stepped physics (CHOSEN).** Physics2D runs inside
  Fusion's tick; the player keeps its `Rigidbody2D` and existing jump/dash/gravity math
  moves into `FixedUpdateNetwork` largely intact, preserving feel. Full client-side
  prediction + reconciliation. Cost: physics steps at network rate scene-wide, pulling
  enemies/projectiles/coins into this model (which they need regardless).
- **B. Kinematic NetworkTransform (manual integration).** Player becomes kinematic;
  gravity/jump/dash reimplemented by hand and synced via `NetworkTransform`. Leaves
  global physics untouched but rewrites the movement math, risking feel drift and
  trickier collisions. Rejected: more custom code, higher chance of wrong platformer feel.
- **C. Authority-correct manual sync (minimal).** Keep hand-rolled sync but make writes
  server-authoritative via input RPCs. No prediction (laggy local feel), keeps fighting
  Fusion. Rejected: band-aid.

## Architecture

### New components

**`NetInput : INetworkInput`** — one struct, all per-tick input:
- `sbyte Horizontal` — movement axis (-1/0/1).
- `sbyte VerticalAim` — up/down for directional attacks (-1/0/1).
- `NetworkButtons Buttons` — `Jump`, `Dash`, `Melee`, `Shoot` (enum-indexed).
- `Vector2 AimDirection` — mouse aim for projectiles, computed locally on the
  input-authority client and shipped in the struct so the host spawns with correct aim.

**`NetworkInputProvider`** — small component registered on the runner via
`runner.AddCallbacks`. Implements `OnInput`, reads local devices once per tick (new Input
System only), fills `NetInput`. The single place that touches `Keyboard`/`Mouse`/`Gamepad`.

### Changed components

- **`PlayerController`** → simulation driver. `FixedUpdateNetwork()` does
  `GetInput<NetInput>(out var input)`, computes pressed-this-tick via
  `input.Buttons.GetPressed(_previousButtons)`, then dispatches to
  `PlayerMovement.Simulate(...)` and `PlayerCombat.Simulate(...)`. Stores
  `_previousButtons`. Its `Update()` input gating is removed. Remote (non-input-authority)
  copies early-out of input-driven branches.
- **`PlayerMovement`** → keeps physics math (jump force, coyote, jump buffer, variable
  jump, dash, stun) but loses `Update()`/`FixedUpdate()` device reads. Exposes
  `Simulate(NetInput input, bool jumpPressed, bool dashPressed, ...)` called from the
  controller. Tick-based conversions:
  - Dash: `[Networked] TickTimer DashTimer`, `[Networked] TickTimer DashCooldown`
    (replaces `IEnumerator Dash()` + `StopAllCoroutines()` + `Time.deltaTime` bar).
  - Coyote time / jump buffer: per-tick counter decrements (already frame-counter shaped).
  - Stun: `[Networked] TickTimer StunTimer`, set by projectile hit on the server.
  - UI bars (dash cooldown) read remaining time in `Render()`.
- **`PlayerCombat`** → loses its own input polling. `TryAttack`/`TryShoot` called from the
  controller. Melee `OverlapBox` hit-detection runs under **StateAuthority only** (no
  double-apply). `Runner.Spawn` for projectiles runs under **StateAuthority only**, using
  `input.AimDirection`.

### Removed

- **`NetworkPlayerWrapper`** deleted. Replaced by:
  - `NetworkRigidbody2D` on the prefab (position/velocity sync + remote interpolation).
  - A small `[Networked] bool FacingRight`, applied to `transform.localScale.x` in
    `Render()`.
  - Camera binding and teammate-collision-ignore relocated to
    `PlayerController.Spawned()` (authority-gated), replacing the wrapper's coroutines.

## Data & control flow

```
Local devices -> NetworkInputProvider.OnInput -> NetInput
                                                    | (Fusion -> StateAuthority;
                                                    |  retained locally for prediction)
                                                    v
PlayerController.FixedUpdateNetwork: GetInput<NetInput>(out input)
        |-> PlayerMovement.Simulate(input)   // walk / jump / dash / stun
        |-> PlayerCombat.Simulate(input)     // melee / shoot
                                                    v
                       NetworkRigidbody2D syncs body state
                       Render(): apply facing + remote interpolation
```

- Edge detection: `input.Buttons.GetPressed(_previousButtons)` for "this tick";
  `input.Buttons.IsSet(...)` for held (variable jump height, dash-hold-to-cancel).
- `FixedUpdateNetwork` runs on StateAuthority and the InputAuthority client. The local
  client predicts; Fusion resimulates/reconciles against the host automatically. Remotes
  are interpolation-only.

## Project & scene changes

**Player prefab:**
- Add `NetworkRigidbody2D` (Physics Addon); remove `NetworkPlayerWrapper`.
- Keep existing `Rigidbody2D`, colliders, ground-check transforms, attack points.
- Component order: `NetworkObject` -> `NetworkRigidbody2D` -> gameplay scripts.

**Runner / bootstrap (`GameNetworkManager`):**
- Add `RunnerSimulatePhysics2D` (Physics Addon) to the runner so Fusion steps `Physics2D`
  (flips simulation mode to script-driven).
- Register `NetworkInputProvider` via `runner.AddCallbacks` after `StartGame`.
- Make `StartClient()` genuinely join as `GameMode.Client` when `singlePlayerMode` is off
  (keep the flag for solo testing). `InputDataWordCount` is auto-computed by the weaver.

## Interim effects (explicitly out of scope here)

- **Enemies:** `EnemyAI` remains a local `MonoBehaviour`; positions still not reconciled
  across clients (pre-existing). This pass leaves enemy behavior no worse than today.
  Full fix = review item #5.
- **Projectiles:** become server-spawned + `NetworkRigidbody2D` so they are visible and
  consistent when fired. Remaining hardening (single-source damage, `Runner.Despawn`,
  friendly fire) = item #5.
- **Coins:** already networked; keep their spawn "pop" working under Fusion-stepped
  physics. Add `NetworkRigidbody2D` only if they visibly desync.

## Testing & verification

No automated tests — this is real-time networked physics; verification is manual and
observational, captured as an explicit checklist:

1. **Single-player smoke** (`singlePlayerMode` on, host-only): rebuilt movement feels
   identical to baseline — walk speed, jump arc, coyote/buffer responsiveness, double
   jump, dash distance/cooldown, variable jump height, stun.
2. **MPPM host + client** (1 host + 1 virtual client):
   - each player drives only their own character (ghost input gone);
   - local movement instant (prediction), remote smooth (interpolation), no rubber-banding;
   - dash, jump, facing, dash-cooldown bar replicate;
   - shooting spawns a projectile visible to both with correct aim; melee damages enemies once;
   - team assignment, spawn position, camera-follow bind to the right player.
3. **Lag simulation** (Fusion `NetworkConditions`: ~150ms delay, jitter, ~5% loss): repeat
   step 2; watch for visible snap/correction on jump/dash; tune reconciliation.
4. **Build + Editor on Photon cloud:** standalone Host + Editor Client over the real relay;
   confirms cloud session path and true-network behavior MPPM can't show.

**Done =** steps 1–3 pass cleanly, movement feel matches the pre-rebuild baseline, and no
client divergence under simulated lag.

## Risks

- **Feel drift** from the coroutine/`Time`-based → tick-based rewrite of dash/stun.
  Mitigated by step-1 smoke test against baseline before networking.
- **Global physics mode change** (`RunnerSimulatePhysics2D`) could affect other 2D bodies;
  mitigated by the interim-effects checks on enemies/projectiles/coins.
- **Reconciliation artifacts** under lag; surfaced and tuned in step 3.
