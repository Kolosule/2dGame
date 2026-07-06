# Responsiveness fixes 2–5 — design

**Date:** 2026-07-06
**Context:** Follow-up to the 20-player networking review. Finding 1 (send rates → 64Hz)
was implemented separately by the user (`ClientSendIndex`/`ServerSendIndex` now `0`).
This spec covers review findings 2–5. Findings 6 (enemy AI staggering) and the send-rate
change are explicitly **out of scope**.

Guiding priority: input responsiveness and control feel at up to 20 players. Where a choice
trades authority/bandwidth/cleanliness against felt responsiveness, responsiveness wins.

---

## Fix 2 — Coin cleanup (lifetime + global cap + staggered pickup polling)

**Problem.** `NetworkedCoinPickup` has no lifetime, cap, or cleanup. Every live coin runs
`FixedUpdateNetwork` on the server with an `OverlapCircle` pickup poll every tick (64Hz),
forever. Enemies respawn every 5s per spawner and drop 1–3 coins each; deaths re-scatter
carried coins. Uncollected coins accumulate for the whole match. Hundreds of coins × 64Hz
physics queries plus that many extra network objects in join snapshots and AoI sorts push
server tick time toward the 15.6ms budget; when it overruns, **every** player's input
latency and correction rate degrade at once (reads as "the game got laggy", not "too many
coins"). Coins are **not** pooled (no `Poolable` component), so there is no pool-reuse
interaction to worry about.

**Design.**

1. **Lifetime.** Add `[Networked] TickTimer LifetimeTimer` to `NetworkedCoinPickup`, armed on
   the state authority in `Spawned` from a new serialized `float lifetimeSeconds` (default
   `45`). In `FixedUpdateNetwork` (authority, before/independent of the pickup poll): if
   `LifetimeTimer.Expired(Runner)` → `Runner.Despawn(Object)` and return. Lifetime starts at
   spawn; 45s ≫ fall time so a coin never despawns mid-flight in practice.

2. **Global cap.** A server-only `CoinRegistry` static class (mirrors the existing
   `HitCooldownLedger` / static-buffer style already used in the codebase):
   - Insertion-ordered store of live coins (e.g. `LinkedList<NetworkedCoinPickup>` or a
     `Queue`), server-only, never networked.
   - `Register(coin)` called from `NetworkedCoinPickup.Spawned` on the state authority;
     `Unregister(coin)` from `Despawned`.
   - On `Register`, first prune destroyed/invalid entries (Unity `== null` guard), then if the
     live count exceeds the cap **`100`** (a documented `const`), despawn the **oldest** live
     coin. The cap is the hard bound; lifetime handles the slow-decay case.
   - Cleared on runner shutdown from `GameNetworkManager.OnShutdown`, alongside the existing
     `objectProvider.ClearPools()` call, so state cannot leak across sessions. `Despawned`
     unregistering each coin already covers the normal path; the explicit clear + null-prune
     is belt-and-suspenders.

3. **Staggered pickup poll.** Fall integration stays every tick (smooth motion). Gate **only**
   `TryServerPickup()` to run every 4th tick, phase-offset by the coin's `Object.Id` so coins
   don't all poll on the same tick:
   `if (isReadyForPickup && ((Runner.Tick + (int)(Object.Id.Raw & 3)) & 3) == 0) TryServerPickup();`
   ~16Hz effective poll → ≤~47ms worst-case pickup latency, imperceptible for coin collection.

**Tunables:** `lifetimeSeconds` (per-coin serialized, default 45); `CoinRegistry` cap (`const`
= 100); pickup poll interval (const/literal = 4 ticks).

**Files:** `Assets/Scripts/Coin Scripts/CoinPickup.cs` (`NetworkedCoinPickup`), new
`CoinRegistry` (server-only static), `Assets/Scripts/GameNetworkManager.cs` (`OnShutdown`
clear).

---

## Fix 3 — Respawn via `NetworkRigidbody2D.Teleport`

**Problem.** `PlayerStatsHandler.Respawn()` sets `transform.position = RespawnPosition` on the
server. `NetworkRigidbody2D.Teleport()` exists specifically to bump a `TeleportKey` so remote
interpolation **snaps** instead of lerping. Without it, every other client sees the respawner
streak across the map over one send interval, and the respawning client takes the move as a
large prediction correction (currently masked by the camera's correction-absorb). More
frequent at 20 players simply because there are more deaths per minute on screen.

**Design.** In `PlayerStatsHandler`:
- Cache a `NetworkRigidbody2D netRb` reference in `Spawned` (`GetComponent<NetworkRigidbody2D>()`).
- In `Respawn()`, replace `transform.position = RespawnPosition;` with
  `netRb.Teleport(RespawnPosition);` (null-guarded; fall back to the transform write if the
  component is somehow absent). Keep the existing `rb.linearVelocity`/`angularVelocity`
  zeroing — `Teleport` handles position + teleport key, not velocity.
- Add `using Fusion.Addons.Physics;`.

`Teleport` requires `Object.IsInSimulation`, which is always true on the state authority
where `Respawn()` runs.

**Files:** `Assets/Scripts/Player/PlayerStatsHandler.cs`.

---

## Fix 4 — Accept the victim-hit snap (option A) and remove melee hit-stop entirely

**Decision.** Do **not** predict victim knockback/stun. The reconcile snap on getting hit is
accepted as-is (option A from the review). Additionally, **remove** the predicted melee
hit-stop entirely: it reads too similarly to the damage-taken camera shake
(`PlayerCameraShakeHandler`), so the two effects muddy each other.

**Design — remove hit-stop across four files.** Dash kick and the muzzle-flash/tracer shoot
prediction are unrelated and stay.

- **`PlayerCombat`:** remove the hit-stop block in `Attack()` (the `HasInputAuthority &&
  Runner.IsForward && feelHandler != null && PredictWouldHitEnemy(...)` → `TriggerHitStop()`
  branch), remove the now-unused `PredictWouldHitEnemy()` helper, and remove the `feelHandler`
  field plus its `Awake` lookup (hit-stop was its only user).
- **`PlayerCameraFeelHandler`:** remove `TriggerHitStop()` and the `hitStopDuration` /
  `hitStopPunch` serialized fields + their header. **Keep** the dash kick. Update the class
  summary to drop the hit-stop mention.
- **`PlayerCamera`:** remove `Hold()`, the `holdTimer` field, its per-frame decrement, and the
  `holdTimer > 0f` freeze branch in `ComputeFollowPosition` (the "remove dead plumbing"
  choice). Reference sites to clear: field declaration, the per-frame `holdTimer -= Time.deltaTime`,
  the freeze branch read, and the `Hold()` writer.
- **`PlayerPrefab.prefab`:** the serialized `hitStopDuration` / `hitStopPunch` entries become
  harmless stale YAML once the fields are gone; Unity strips them on next save. No hand-editing.

**Files:** `Assets/Scripts/Player/PlayerCombat.cs`,
`Assets/Scripts/Player/PlayerCameraFeelHandler.cs`, `Assets/Scripts/Player/Playercamera.cs`.

---

## Fix 5 — Dead-player AoI covers the respawn point

**Problem.** `PlayerController.FixedUpdateNetwork` adds the player's interest region around
`transform.position` every tick — including the ~3s dead window, during which the camera has
already transitioned to `RespawnPosition`. If the spawn is >`areaOfInterestRadius` (25u) from
the corpse, objects around the spawn aren't replicated until after the teleport → respawn into
pop-in.

**Design.** In `PlayerController.FixedUpdateNetwork`, keep the existing self-region every tick
(covers the corpse), and while `stats.IsDead` add a **second** region at `stats.RespawnPosition`:

```csharp
if (Runner.IsServer)
{
    Runner.AddPlayerAreaOfInterest(Object.InputAuthority, transform.position, areaOfInterestRadius);
    if (stats != null && stats.IsDead)
        Runner.AddPlayerAreaOfInterest(Object.InputAuthority, stats.RespawnPosition, areaOfInterestRadius);
}
```

`RespawnPosition` is networked and set in `Die()` before the dead window, so it is valid
throughout death. While alive the second region is never added, so its default value is
irrelevant. Fusion accepts multiple regions per player per tick; the two regions together
cover the full corpse→spawn camera transition.

**Files:** `Assets/Scripts/Player/PlayerController.cs`.

---

## Testing

Unity holds the project lock during editor sessions; compile/EditMode runs may need the
bundled-Roslyn workaround (see project memory). Verification is primarily in-editor / multi-peer:

- **Fix 2:** Spawn coins past the cap (kill many enemies); confirm oldest despawn and total
  live coins stay ≤ cap. Confirm coins self-despawn ~45s after landing. Confirm pickup still
  feels instant. Watch the Fusion stats overlay: server tick time should stay flat as coins
  churn rather than climbing.
- **Fix 3:** 2+ peers. On a non-host client, watch another player die and respawn — the avatar
  should snap to the spawn point, not streak across the map. The respawning player's own camera
  should not lurch.
- **Fix 4:** Confirm melee no longer freezes/punches the camera; dash kick and shoot tracer
  still work; damage shake still works. No compile references to `Hold`/`holdTimer`/
  `TriggerHitStop`/`PredictWouldHitEnemy` remain.
- **Fix 5:** With a spawn point >25u from a death location, die on a client and confirm the
  spawn area's players/objects are already visible when the camera arrives (no pop-in on
  respawn).

## Out of scope

- Finding 1 (send rates) — already implemented by the user.
- Finding 6 (enemy AI `AcquireTarget` staggering, death-dim RPC → `OnChangedRender`,
  `RPC_AddPoints` hardening) — deferred.
- Any victim-side knockback/stun prediction — explicitly rejected (option A).
