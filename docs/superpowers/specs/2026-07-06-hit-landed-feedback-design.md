# Hit-Landed Feedback Effect — Design

## Problem

When a player deals damage (melee, dash-strike, or projectile) to an enemy or
another player, the only feedback today is a small hit-marker sprite
(`PlayerCombat.cs:198-203`). This adds impact particles, a brief flash on the
target, and a floating damage-number popup — all local/cosmetic on the
attacking client only, matching the existing `hitMarkerPrefab` pattern (no
RPC, no Fusion networking).

## Scope

- Applies to both PvE (player → `Enemy`) and PvP (player → `PlayerStatsHandler`)
  hits.
- Applies to normal melee attacks, the dash-strike (Quicker Dash tier 3), and
  projectile hits — every place damage currently lands.
- Attacker-only cosmetic: only the attacking player sees the flash, particles,
  and number for a given hit. No networked *state* is added; delivery uses a
  single targeted RPC (see Topology note below).
- Out of scope: hit-stop/freeze-frame and camera punch (explicitly not
  requested); broadcasting the effect to all clients.

## Topology note (why an RPC is required)

This project runs Fusion in **Host/Server/Client** mode
(`GameNetworkManager.cs:124,153,174`), so **only the server/host has state
authority**. Both hit-detection paths gate on `HasStateAuthority`
(`PlayerCombat.cs:173`, `Projectile.cs:73`), meaning a hit is detected on the
server, never locally on a plain client. Spawning feedback directly inside
those methods would put it on the server's screen (headless for a dedicated
server, or the host's screen for a host), not the attacker's.

Therefore the server, on detecting a landed hit, sends **one RPC targeted at
the attacker's `InputAuthority`** (`RpcTargets.InputAuthority`). Only the
attacker's client runs the feedback. This mirrors the existing
`RPC_Impact` / `RPC_TakeDamage` / `RPC_DisablePlayerControls` patterns and
keeps bandwidth minimal (one small RPC per hit, to a single peer). It adds no
`[Networked]` state.

## Architecture

A scene singleton, `HitFeedback.Instance.Play(GameObject target, Vector2 hitPoint, int damage)`,
holds the prefab references and performs the three cosmetic effects. It is
invoked on the attacker's client from RPC handlers on the damage-dealing
`NetworkBehaviour`s:

- `PlayerCombat.RPC_HitFeedback(NetworkId, Vector2, int)` — targeted at
  `InputAuthority`. Called from `ApplyMeleeHits()` after the authoritative
  damage call, in both the enemy branch (~line 212) and player branch
  (~line 229). Dash-strike routes through the same method, so it's covered.
- `Projectile.RPC_HitFeedback(NetworkId, Vector2, int)` — targeted at
  `InputAuthority`. Called from `OnTriggerEnter2D()` in the enemy branch
  (~line 107) and player branch (~line 91), before `Hit()` despawns the
  projectile (same ordering `RPC_Impact` already relies on).

A singleton (rather than a `static` class) is used because the effects need
serialized prefab references, which a static class cannot hold. Centralizing
them in one scene object avoids wiring the particle/number prefabs onto every
player and projectile prefab.

The RPC carries the target's `NetworkId` (both `Enemy` and `PlayerStatsHandler`
are `NetworkBehaviour`s) so the receiving client can resolve its local copy via
`Runner.FindObject(id)` to play the flash on the correct sprite.

## Components

1. **`HitFeedback.cs`** (new `MonoBehaviour` singleton, `Assets/Scripts/Player/`)
   — sets `Instance` in `Awake`; serialized fields `particleBurstPrefab` and
   `damageNumberPrefab`. Public `Play(GameObject target, Vector2 hitPoint, int damage)`
   does three things:
   - Instantiate `particleBurstPrefab` at `hitPoint`; `Destroy` after its
     `ParticleSystem.main.duration` (same lifecycle pattern as `hitMarkerPrefab`).
   - Instantiate `damageNumberPrefab` at `hitPoint`, call `Init(damage)` on its
     `DamageNumber` component.
   - `target.GetComponentInChildren<HitFlash>()` and call `PlayFlash()` if found.
   Each of the three is independently null-guarded.

2. **`HitFlash.cs`** (new tiny `MonoBehaviour`, added to Enemy and Player
   prefabs alongside their `SpriteRenderer`) — exposes `PlayFlash()`: stops any
   running flash coroutine, snaps `SpriteRenderer.color` to white, lerps back
   to the original color over ~0.1s. Owning the coroutine on the target means
   rapid repeated hits just restart it cleanly.

3. **`DamageNumber.cs`** (new tiny `MonoBehaviour` on a new prefab) —
   `Init(int amount)` sets a TMP text field; in `Update`, drifts upward
   (~1 unit/sec) and fades alpha over ~0.7s, then `Destroy(gameObject)`.

4. **Particle prefab** — plain Unity `ParticleSystem` (small spark/burst), no
   new script required.

## Data flow

Server (state authority) computes `finalDamage` → calls the existing
authoritative damage call (`enemy.TakeDamage(...)` /
`targetPlayer.ServerApplyDamage(...)`, unchanged) → sends
`RPC_HitFeedback(targetId, hitPoint, finalDamage)` to the attacker's
`InputAuthority`. On the attacker's client, the RPC handler resolves
`Runner.FindObject(targetId)` and calls
`HitFeedback.Instance.Play(targetObj.gameObject, hitPoint, finalDamage)`.
No other client runs the effect; no `[Networked]` state is added.

## Error handling

- `HitFeedback.Instance` null (singleton not in scene) → RPC handler no-ops.
- `Runner.FindObject(targetId)` returns null (target despawned/culled on the
  attacker's client) → skip; still spawn particles + number at `hitPoint`,
  only the flash is skipped.
- Missing `HitFlash` on target → skip flash silently (mirrors the existing
  `if (sr != null)` null-check at `PlayerCombat.cs:201`).
- Missing prefab references → each effect is null-checked and skipped
  independently, same as `hitMarkerPrefab != null` today.

## Testing

- EditMode unit tests where they add value without Unity runtime: `DamageNumber`
  fade/drift math and `HitFlash` color-lerp math can be exercised by driving
  their update step directly. RPC wiring and prefab instantiation are verified
  manually (they require a live `NetworkRunner` and the editor).
- Manual multi-peer: with a host + at least one remote client, melee an enemy,
  land a dash-strike, and hit an enemy player; confirm particle burst + flash +
  damage number appear **only on the attacking peer's screen**, for both the
  host's and the remote client's attacks. Repeat for a projectile hit.
