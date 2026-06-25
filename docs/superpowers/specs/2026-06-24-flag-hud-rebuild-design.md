# Flag HUD Rebuild — Design

**Date:** 2026-06-24
**Status:** Approved (design), pending implementation plan

## Problem

The Capture-the-Flag HUD has two jobs that are currently unreliable and/or
wrong:

1. **"Flag picked up" feedback** — a moment-it-happens alert plus a way to
   identify who is carrying a flag up close.
2. **"Where is the flag" indicator** — when a flag is away from its home base
   the player should get a directional arrow pointing toward it.

Today the "indicator" is a world-space GameObject (`team1FlagIndicator` /
`team2FlagIndicator`) that is moved *onto* the flag's position and toggled on
when the flag leaves home. It sits on the flag — it does **not** point toward an
off-screen flag, which is the desired behavior. There is also a persistent
status label ("At Base / Taken! / Dropped") that we are dropping.

## Goals

- A **screen-edge arrow** for **each** flag (both teams), color-coded by the
  flag's owning team, that pins to the screen edge and rotates to point toward
  the flag **only when that flag is away from home AND off-screen**.
- A **center-screen text notification** on pickup / drop / return.
- A **floating icon above the carrier's head**, visible to every peer, so the
  thief can be identified up close.

## Non-Goals

- No persistent flag-status label (removed).
- No distance readout on the arrow.
- No on-screen marker hovering over a visible flag (arrow hides when the flag is
  on screen).
- No new networked state. Flag position/state/carrier are already fully
  networked; all of this is **local presentation** derived from that state.

## Architecture

Approach A: a dedicated local HUD component, separating presentation from the
networked game logic.

| Component | Type | Responsibility |
|---|---|---|
| `FlagDirectionHud` | **new** `MonoBehaviour`, lives on the Screen-Space HUD canvas | Owns the two UI arrows. Each `LateUpdate`, reads both flags from `CTFGameManager.Instance` and updates each arrow (see Arrow Behavior). Local-only, no networking. |
| `FlagCarrierMarker` | **rebuilt** `MonoBehaviour` on the player prefab | Idempotently shows/hides a head icon when carrying. No dash hack. |
| `CTFGameManager` | **trimmed** `NetworkBehaviour` | Keeps `RPC_ShowNotification` and the `Team1Flag`/`Team2Flag` references. Loses the indicator-follow `Update()`, the two `*FlagIndicator` fields, the two `*FlagStatusText` fields, `RefreshAllFlagUI`/`RefreshFlagUI`, and the status-label calls in `OnFlagStateChanged`. |
| `Flag` | **lightly touched** | Unchanged networking. Exposes a clean owning-team enum getter for the HUD. Per-peer carrier-marker reconciliation in `Update()` stays. |

### Data flow

```
Flag (networked state: State, position, CarrierPlayerRef)
  │
  ├── Flag.Update() per peer ── reconciles FlagCarrierMarker on the carrier
  │
  ├── Flag pickup/drop/return ── CTFGameManager.RPC_ShowNotification (server→all)
  │
  └── FlagDirectionHud.LateUpdate() (local) ── reads Flag.State + transform,
        Camera.main viewport math ── positions/rotates/colors each edge arrow
```

## Arrow behavior (`FlagDirectionHud`)

Two arrow `RectTransform`s (each with an `Image`), children of the HUD canvas,
wired in the inspector — one bound to Team1's flag, one to Team2's. Each
`LateUpdate`, for each flag:

1. If the flag is null/not spawned, or `flag.State == AtHome` → hide the arrow,
   continue.
2. `Vector3 vp = cam.WorldToViewportPoint(flag.transform.position)`.
3. **On-screen** (`vp.z > 0 && vp.x in [0,1] && vp.y in [0,1]`) → hide the arrow
   (the flag is visible; arrow only shows when off-screen).
4. **Off-screen** → show the arrow. Compute the direction from screen center to
   the flag in screen space, clamp the arrow's anchored position to a screen rect
   inset by `edgeMargin`, and rotate the arrow so it points outward toward the
   flag (`atan2(dir.y, dir.x)`). Set the arrow `Image.color` to the owning team's
   `teamColor` (`TeamManager.Instance.GetTeamData(team).teamColor`).

Behind-camera guard: if `vp.z < 0`, treat as off-screen and invert the direction
so the arrow still points the correct way.

Inspector config: the two arrow `RectTransform`s, the owning `Team` for each, an
optional `Camera` (defaults to `Camera.main`), and a float `edgeMargin` (pixels
from the screen edge).

## Carrier icon (`FlagCarrierMarker`)

- Keep the world-space head icon: instantiate `flagIconPrefab` as a child at
  `Vector3.up * iconHeight` when carrying starts; destroy it when carrying ends.
- `SetCarryingFlag(bool)` is idempotent — calling it repeatedly with the same
  value is a no-op, so the per-frame reconciliation in `Flag.Update()` can call
  it freely without spawning duplicates.
- Remove the no-op `playerMovement.enabled = false; = true;` lines. Dash
  suppression is handled elsewhere by checking `IsCarryingFlag()`.
- `OnDestroy` cleans up the icon so it never leaks if the player despawns
  mid-carry.

## Notification (center-screen text)

- Unchanged: `CTFGameManager.RPC_ShowNotification(string)` (StateAuthority → All)
  is already invoked from `Flag.PickupFlag` / `DropFlag` / `ReturnFlag`. It drives
  `notificationText` on the HUD canvas with a timed hide. Verify the wiring
  survives the `CTFGameManager` trim.

## What gets deleted

From `CTFGameManager`:
- The `Update()` body (indicator transform-follow).
- `team1FlagIndicator`, `team2FlagIndicator` fields.
- `team1FlagStatusText`, `team2FlagStatusText` fields.
- `RefreshAllFlagUI()`, `RefreshFlagUI(...)`.
- The status-label refresh calls inside `OnFlagStateChanged` (the method may be
  removed entirely if nothing else needs it; the notification path is separate).

The two old world-space indicator GameObjects are removed from the scene; the two
new arrow UI elements are added under the HUD canvas.

## Testing / verification

Unity + Photon Fusion presentation code — verified in-editor with host + one
client:

1. Steal each flag → center-screen notification flashes on both peers.
2. While carried → head icon shows over the carrier on **both** peers; clears on
   drop/return.
3. Each arrow:
   - Hidden while its flag is at home.
   - Hidden while its flag is out but on screen.
   - Visible, edge-pinned, and pointing correctly while its flag is out and off
     screen; tracks as the player moves.
   - Colored by the flag's owning team.

The code will be written with clear seams, but the authoritative check is manual
play in the editor.
