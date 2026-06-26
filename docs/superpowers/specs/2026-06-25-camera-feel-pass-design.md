# Camera Feel Pass — Design

**Date:** 2026-06-25
**Status:** Approved (pending written-spec review)
**Scope:** Make the local player's camera feel tight, crosshair-led, and kinetic, and make
that feel survive higher latency / 20 players. Adds tight-X / deadzone-Y follow, a subtle
mouse-aim lean, a dash kick, and a predicted melee hit-stop — built on a single additive
offset channel. Movement look-ahead, recoil kick, landing thump, and a Cinemachine migration
are explicitly out of scope.

## Problem / motivation

The game currently feels good at 2 players, but the camera is quietly working against
responsiveness, and that will surface as latency stacks up at the 20-player cap:

1. **Follow smoothing re-adds latency.** The body is already zero-latency (client-side
   physics prediction is on), but `PlayerCamera` follows it through
   `SmoothDamp(followSmoothTime = 0.15)` ([Playercamera.cs:164](../../../Assets/Scripts/Player/Playercamera.cs)),
   adding ~150 ms of perceived camera lag on top of an instant body. At 2p, low RTT masks it.
2. **No combat framing.** Aiming is mouse/world-point based (`input.AimWorldPoint`,
   [PlayerCombat.cs:106](../../../Assets/Scripts/Player/PlayerCombat.cs)), but the camera stays
   centered on the body and gives no extra vision down the firing lane.
3. **Thin kinetic feedback.** Damage shake exists ([Playercamera.cs:271](../../../Assets/Scripts/Player/Playercamera.cs)),
   but high-agency actions (dash, landing a melee hit) produce no camera response.

## Decisions

| Decision | Choice | Why |
| --- | --- | --- |
| Self-follow | **Tight X, deadzone/eased Y** | Run/dash feel instant; jumps and hops don't jerk the view (action-platformer standard). |
| Latency robustness | **Follow predicted body + absorb correction snaps** | Keep zero-latency response to real input; ease only the residual when the server reconciles a misprediction. The piece that lets a tight follow survive 20p. |
| Aim framing | **Subtle constant lean toward aim direction** | Extra vision down the firing lane without being off-center or disorienting. |
| Aim driver | **Aim *direction*, capped — not raw cursor distance** | Cursor world-point moves with the camera; leaning on distance would create a camera↔cursor feedback loop. |
| Juice | **Dash kick + predicted melee hit-stop** | The two highest-agency moments. Recoil/landing were considered and dropped. |
| Hit-stop authority | **Local, render-only, client-predicted** | Damage is server-authoritative and the firing client can't be frozen (Fusion tick must keep running). A predicted local overlap gives instant feel; rare false positives are acceptable for a *feel* effect (unlike an information-claiming hit-marker). |

### Why hit-stop is predicted, not server-confirmed

Melee damage/overlap runs under `HasStateAuthority` ([PlayerCombat.cs:171](../../../Assets/Scripts/Player/PlayerCombat.cs)),
so the firing client does not locally know whether a swing connected. Waiting for a server
confirmation RPC would delay the hit-stop by ~½ RTT and defeat its purpose. Instead the firing
client runs the *same* `OverlapBox` non-authoritatively, enemy-team filtered, and triggers the
effect locally. This mirrors the project's existing cosmetic-prediction philosophy (the muzzle
flash + tracer in [PlayerCombat.cs:307](../../../Assets/Scripts/Player/PlayerCombat.cs)). A
predicted hit-*marker* was previously rejected because a false "you hit!" claim feels bad; a
~70 ms camera hold on an occasional near-miss does not read as a false claim, so the same
objection does not apply.

## Environment (confirmed)

- `PlayerCamera` is a single scene camera in the Gameplay scene; it finds the local
  input-authority player and follows it ([Playercamera.cs:195](../../../Assets/Scripts/Player/Playercamera.cs)).
- Feel events come from handlers on the **player prefab** that call public `PlayerCamera`
  methods. `PlayerCameraRespawnHandler` correctly gates to `HasInputAuthority`
  ([PlayerCameraRespawnHandler.cs:67](../../../Assets/Scripts/Player/PlayerCameraRespawnHandler.cs));
  `PlayerCameraShakeHandler` does **not** — every player's instance polls health and shakes
  the single camera on any player's damage ([PlayerCameraShakeHandler.cs:64](../../../Assets/Scripts/Player/PlayerCameraShakeHandler.cs)).
  That latent bug is fixed as part of this pass.
- Existing `PlayerCamera` features to preserve: speed-based zoom, damage shake, respawn
  transition + hold, `SnapToPosition`, `RefreshTarget`.
- Aim is mouse/world based: `NetInput.AimWorldPoint` (shooting) and `VerticalAim` (melee tiers).
- Server/headless build strips the camera (Phase 1 plan); all new triggers are
  input-authority + render-only, so they no-op on the server.

## Architecture — one offset channel, several sources

`PlayerCamera.LateUpdate` is restructured so the final camera position is an explicit sum of
separable contributions, computed in this order:

```
finalPos = FollowPos        (tight-X, deadzone-Y, on the predicted body)
         + CorrectionAbsorb (decaying residual from a network reconciliation snap)
         + AimLean          (capped offset toward aim direction)
         + ImpulseOffset    (additive decaying channel: shake + dash kick)
```

The inline shake math is replaced by a single **impulse channel** that shake, dash-kick, and
future juice push into. Each contribution is independently tunable in the Inspector and
independently testable.

### Components / responsibilities

1. **`PlayerCamera` (the rig).** Owns the contribution sum above and the public feel API.
   - **Follow:** horizontal near-1:1 to the predicted body (zero/tiny smooth time); vertical
     deadzone band with gentle ease outside it.
   - **CorrectionAbsorb:** follows `transform.position` (predicted) directly. Each frame, if the
     body delta exceeds max legitimate motion (threshold above dash speed), the excess is moved
     into a decaying correction offset instead of snapping the camera. Legitimate teleports
     (respawn) bypass via the existing `SnapToPosition` / respawn-transition path.
   - **AimLean:** capped offset (~15–20% of view) toward the followed player's aim direction,
     lightly smoothed; suppressed during respawn transitions.
   - **Impulse channel:** `AddImpulse(Vector2 dir, float magnitude, float duration)` plus a
     `Hold(float duration)` for hit-stop; summed into `ImpulseOffset` and decayed each frame.
     Shake is reimplemented on top of this.
2. **Aim source.** A getter exposing the followed player's last aim direction in world space,
   input-authority only (on `PlayerController`/`PlayerCombat`); `PlayerCamera` reads it from its
   current target.
3. **Dash-kick trigger.** Fires on the dash rising edge for the input-authority player (off
   `PlayerMovement.IsDashing()` transition), calling `AddImpulse` once per dash.
4. **Hit-stop trigger.** On the input-authority client, when a melee swing is initiated, runs the
   same `OverlapBox` as `ApplyMeleeHits` non-authoritatively, enemy-team filtered; on a predicted
   connect, calls `PlayerCamera.Hold(...)` + a small punch. Render-only; never touches
   `Time.timeScale` or the tick.
5. **Shake cleanup.** `PlayerCameraShakeHandler` routes through the impulse channel and is gated
   to `HasInputAuthority` (fixes the any-player-shakes-my-camera bug).

### Data flow

- Triggers are **event-driven from where the events already live** (dash edge in
  `PlayerMovement`, melee initiation in `PlayerCombat`), guarded by `HasInputAuthority` — not
  per-frame health polling.
- `PlayerCamera` pulls the aim direction from its current target each `LateUpdate`.

## Edge cases / error handling

- **No target yet:** existing `FindLocalPlayer` retry loop is unchanged.
- **Respawn transition:** aim lean and impulses are suppressed while the scripted respawn
  transition/hold is active.
- **Legitimate fast motion (dash):** correction threshold is set above dash speed so dashes are
  followed instantly, not absorbed.
- **Server/headless:** no camera exists; triggers are input-authority + render-only and no-op.
- **Hit-stop false positive:** bounded — a brief, render-only camera hold; no gameplay effect.

## Verification (manual / observational, per project convention)

No test assembly; verify by compiling clean then observing in single-player and Multiplayer
Play Mode (1 host/server + 1 client):

- Horizontal follow feels instant on run/dash; vertical does not jerk on hops/jumps; large falls
  still follow.
- Aim lean is visible, capped, and biases toward the cursor without swinging on fast flicks.
- Dash kick fires exactly once per dash.
- Hit-stop fires when a swing lands on an enemy and does **not** fire on whiffs or on friendlies;
  game timescale is unchanged.
- In MPPM, feel effects affect only the local window's camera (the shake-handler gating fix).
- With artificial Fusion latency, correction-absorb visibly hides reconciliation snaps while
  normal motion stays instant.

## Out of scope

- Movement (velocity) look-ahead — not chosen.
- Recoil kick on fire, landing thump — considered, dropped.
- Cinemachine migration — keeps existing respawn/shake/zoom hooks intact.
- Any change to authority, hit detection, or networked state.
