# Friendly Fire, Friendly Collision, and Self Marker — Design

**Date:** 2026-08-05
**Status:** Approved (design), no implementation plan authored
**Game:** Unity 6.3 Photon Fusion 2 2D PvPvE arena, Host/Client + dedicated server, ~20 players

## Problem

Team mechanics are mostly in place — a single networked `Team` on
[`PlayerTeamData`](../../../Assets/Scripts/Player/PlayerTeamData.cs:14), team-derived body color, and
`TeamUtil.AreEnemies` as the hostility predicate — but three things are wrong or missing for a
20-player team game:

1. **Friendly fire is inconsistent across damage sources.** Melee
   ([`PlayerCombat.ApplyMeleeHits`](../../../Assets/Scripts/Player/PlayerCombat.cs:258)) gates
   correctly on `TeamUtil.AreEnemies` and has a self-hit guard
   (`targetPlayer != statsHandler`). Projectiles
   ([`Projectile.OnTriggerEnter2D`](../../../Assets/Scripts/Player/Projectile.cs:83)) use a
   separate, hand-rolled check — `targetTeam != Team.None && targetTeam == ShooterTeam` — with
   **no self-hit guard**. The two disagree on `Team.None`: `AreEnemies` treats it as
   non-hostile (fail-safe), the projectile check treats it as "not friendly," so a projectile can
   damage and stun a player whose team hasn't replicated yet (spawn / late-join / reconnect
   window), and a `Team.None` shooter can hit themselves point-blank.
2. **Friendly collision does not exist.** All players — both teams — share physics layer 8
   (`Player`) with one non-trigger `BoxCollider2D` and a `Rigidbody2D` /
   `NetworkRigidbody2D`. The `Player`↔`Player` pair is enabled in
   `ProjectSettings/Physics2DSettings.asset`, so teammates physically block and shove each other,
   producing pileups at the home base and around the flag.
3. **No visual distinguishes "me" from "my teammates."** Every player on a team renders in the
   same `TeamData.teamColor`. The only existing overhead-marker precedent is
   [`FlagCarrierMarker`](../../../Assets/Scripts/CTF%20Flag/FlagCarrierMarker.cs) at height 2,
   visible to everyone. Nothing marks the locally-controlled player for the client controlling
   them.

This spec fixes all three: one shared damage-gating predicate, runtime-scoped collision
suppression between teammates, and a local-only overhead marker.

## Decisions (from brainstorming)

| # | Decision |
|---|---|
| 1 | **Friendly-fire gate is a single new pure predicate**, `FriendlyFire.CanDamagePlayer`, called from both melee and projectile paths. Replaces the projectile's hand-rolled check. |
| 2 | **`Team.None` is non-hostile on both sides** (fail-safe), matching `TeamUtil.AreEnemies`'s existing treatment. This is a deliberate behavior change for projectiles, closing the spawn/reconnect damage window. |
| 3 | **Self-hit is impossible from any damage source**, folded into the same predicate rather than left as an ad-hoc identity check per call site. |
| 4 | **Friendly collision uses runtime `Physics2D.IgnoreCollision`**, not per-team physics layers and not a global `Player`↔`Player` matrix toggle. Chosen over layers because splitting the `Player` layer would require re-authoring 7 enemy prefabs' `playerLayerMask`, the player prefab's `attackableLayer`, and `CoinPickup`'s hardcoded `LayerMask.GetMask("Player")` — each a silent single-sided failure point if missed. Chosen over a global matrix toggle because that would also stop enemy-vs-enemy body blocking (e.g. blocking a flag carrier), which is out of scope here. |
| 5 | **Teammates pass through each other completely** (no blocking, no soft separation). Confirmed: no map or mechanic currently depends on teammate collision. |
| 6 | **Collision ignoring is derived independently on every peer** from the replicated `Team`, not authority-gated or networked as new state. Every client runs its own `Physics2D` world and must apply the same ignores locally. |
| 7 | **Unassigned team (`Team.None`) keeps collision ON** until the team replicates — fail-safe in the same direction as decision 2. Expected to resolve within a tick or two of spawn. |
| 8 | **Self marker is a local-only overhead chevron**, enabled via `HasInputAuthority` with no networked state and no per-frame logic. Only your own player ever shows it, on your own client only. |
| 9 | **Marker scope is "just you,"** not teammate nameplates or pips. Teammates remain distinguished by team color alone. |
| 10 | **Marker height (2.6) sits above the flag-carrier icon (2.0)** so the two never overlap or fight for the same slot. |
| 11 | **Marker color is fixed white**, not team-derived — chosen because white reads against both team body tints, where a team-colored chevron would blend into exactly the bodies it needs to stand out from. |
| 12 | **Marker has a code-generated triangle fallback** if no sprite is assigned, following the `CosmeticTracer` "no art needed" precedent — insurance against the project's recurring unassigned-serialized-reference failure mode. |
| 13 | **Marker stays visible while dead.** The camera stays on the corpse during the respawn timer; hiding the marker on death buys no readability and adds a special case. |

## Architecture

### Friendly fire — `Game.Combat.Core.FriendlyFire`

New static class in `Assets/Scripts/Combat/Core/`, alongside `TerritorialCombat` and `SwingPhase`
(existing pure-logic types in the same namespace with existing EditMode test coverage).

```csharp
namespace Game.Combat.Core
{
    public static class FriendlyFire
    {
        /// <summary>The single gate every player-damaging source must pass. Team.None on
        /// either side is non-hostile: an unassigned or not-yet-replicated player can
        /// neither deal nor take player damage. isSelf always blocks, regardless of team.</summary>
        public static bool CanDamagePlayer(Team attackerTeam, Team defenderTeam, bool isSelf)
            => !isSelf && TeamUtil.AreEnemies(attackerTeam, defenderTeam);
    }
}
```

Call sites, both server-only (state authority), both replacing existing gating logic in place —
no change to what happens after the gate (damage, knockback, stun application is untouched):

- [`PlayerCombat.ApplyMeleeHits`](../../../Assets/Scripts/Player/PlayerCombat.cs:253-258): replace
  the `targetPlayer != statsHandler` check plus the inline `!TeamUtil.AreEnemies(...)` continue
  with one `FriendlyFire.CanDamagePlayer(myTeam, otherTeam, targetPlayer == statsHandler)` call.
- [`Projectile.OnTriggerEnter2D`](../../../Assets/Scripts/Player/Projectile.cs:79-84): replace the
  `friendly` bool (`targetTeam != Team.None && targetTeam == ShooterTeam`) with
  `FriendlyFire.CanDamagePlayer(ShooterTeam, targetTeam, isSelf)`, where `isSelf` is
  `Object.InputAuthority == playerStats.Object.InputAuthority`. A friendly or self projectile
  still passes through without calling `Hit()` — it can go on to hit an enemy behind the
  teammate, matching current pass-through behavior for the friendly case.

### Friendly collision — `FriendlyCollision` (new `NetworkBehaviour`)

Added to `PlayerPrefab`. Maintains a static in-memory registry of all live instances (mirrors the
existing `FindObjectsByType` / static-registry patterns used elsewhere in the codebase, e.g.
`PlayerStealthVisual.LocalViewerTeam`).

```
Spawned()               → register self; refresh pairs against all other registered instances
PlayerTeamData.TeamChanged (new event) → refresh pairs against all other registered instances
Despawned()              → unregister self
```

Refreshing a pair is one call:

```csharp
Physics2D.IgnoreCollision(myCollider, otherCollider, sameTeam);
```

Passing `sameTeam` directly (rather than only calling this when `true`) means the identical code
path restores collision when a pairing's team relationship changes — no separate "un-ignore"
branch. Idempotent: calling with the same value repeatedly is a no-op in Physics2D.

Scope: only the player's primary non-trigger `BoxCollider2D` is registered and paired. Trigger
colliders (coin pickup, flag capture, home base zones) are untouched — `IgnoreCollision` on a
trigger pair has no effect on trigger callbacks in Unity, and this system never touches trigger
colliders regardless.

`Team.None` vs `Team.None` is **not** treated as "same team" (`sameTeam = ownTeam == otherTeam &&
ownTeam != Team.None`, i.e. explicitly false when either side is `None`) — collision
stays on until both sides have a real team, consistent with decision 7.

`PlayerTeamData` gains one addition: `public event Action TeamChanged;`, raised at the end of the
existing `OnTeamChanged()` render callback. This mirrors the existing
`NetworkedPlayerInventory.CoinsChanged` event pattern that `CoinCarrierAura` already subscribes
to, keeping `FriendlyCollision` event-driven rather than polling.

**Not authority-gated.** Every peer simulates its own `Physics2D` world (this is a 2D Fusion
project without server-authoritative physics broadcast for these bodies), so every peer must
independently ignore the same pairs. Each peer computes this from its own view of the replicated
`Team` values, so results converge without introducing new networked state.

**Cost.** Registration/refresh is O(n) per join/leave/team-change event, not per-frame. At 20
players that is at most 190 pairs, evaluated only on those events.

### Self marker — `LocalPlayerMarker` (new `NetworkBehaviour`)

Added to `PlayerPrefab`. Entire gameplay contract:

```csharp
public override void Spawned()
{
    if (markerRoot == null) markerRoot = BuildFallbackMarker();
    markerRoot.SetActive(HasInputAuthority);
}
```

- `markerRoot` is a serialized child `GameObject` (its own `SpriteRenderer`, not a member of
  `PlayerStealthVisual.bodyRenderers` and not `CoinCarrierAura.auraRenderer` — the same
  isolation the aura already relies on, so the marker is immune to death-dim, stealth-fade, and
  hit-flash color writes, all of which target renderer color directly on the renderers they own).
- If unassigned, `BuildFallbackMarker()` generates a small triangle mesh/sprite at runtime
  (`CosmeticTracer`-style, no art dependency).
- Serialized `markerHeight` (default `2.6`) and `markerColor` (default white, fixed —
  not team-derived).
- No `Update`, no networked state, no RPC. Exists once per player, enabled on exactly one client
  (the owner) per instance, for the lifetime of the instance including while dead (decision 13).
- Re-evaluated implicitly on every respawn because `Spawned()` reruns only on
  spawn/despawn — since the player object is never despawned across a respawn (only
  teleported, per `PlayerStatsHandler.Respawn`), no extra hookup is needed: the marker object
  simply stays active the whole time, matching decision 13 directly.

## Data Flow

```
Server: PlayerTeamData.SetTeam(team)
  -> Team (networked property write)
  -> OnChangedRender on every peer
       -> ApplyTeamColor()      [existing]
       -> TeamChanged event     [new]
            -> FriendlyCollision.RefreshPairs() on every peer independently
```

```
Server (state authority only): PlayerCombat.ApplyMeleeHits / Projectile.OnTriggerEnter2D
  -> FriendlyFire.CanDamagePlayer(attackerTeam, defenderTeam, isSelf)
       -> false: no damage, no knockback, no stun, no RPC_HitFeedback
       -> true: existing damage/knockback/stun/feedback path, unchanged
```

```
Client (owning client only): PlayerPrefab.Spawned()
  -> LocalPlayerMarker.Spawned()
       -> markerRoot.SetActive(HasInputAuthority)   [true on exactly one client]
```

## Failure Modes

| Situation | Behavior |
|---|---|
| Team not yet replicated (`Team.None`) | Collision stays **on**; damage gate stays **closed** (both fail toward "no accidental harm"). |
| Teammate despawns mid-match | `FriendlyCollision` registry drops the reference on `Despawned()`; any pending refresh null-checks destroyed colliders before calling `IgnoreCollision`. |
| Player reassigned to a different team | `TeamChanged` fires, triggering a full pair refresh; `IgnoreCollision(..., false)` restores collision against former teammates without a separate code path. |
| `markerRoot` unassigned in the prefab | Runtime-generated triangle fallback; feature degrades to unstyled but present, not silently absent. |
| Reconnect (per [reconnection-design](2026-07-29-reconnection-design.md)) | Rejoining player is a fresh spawn; registers with `FriendlyCollision` and gets a full sweep against the current roster like any new spawn. |

## Testing

**EditMode** — new `Assets/Tests/EditMode/Combat/FriendlyFireTests.cs`, following the existing
`TerritorialCombatTests.cs` / `SwingPhaseTests.cs` pattern for pure `Game.Combat.Core` logic. Full
truth table over `CanDamagePlayer`:

- Same team (`Team1`/`Team1`, `Team2`/`Team2`) → `false`
- Opposing human teams (`Team1`/`Team2` both directions) → `true`
- `Team3AI` vs either human team (both directions) → `true`
- `Team.None` on attacker, defender, or both → `false`
- `isSelf = true` → `false` regardless of team combination, including enemy teams

`FriendlyCollision` and `LocalPlayerMarker` are not unit-tested: both are thin wrappers over
Unity API calls (`Physics2D.IgnoreCollision`, `GameObject.SetActive`) with no independent branching
logic once `FriendlyFire`'s truth table and the `sameTeam` boolean are already covered. Pushing
the one genuinely testable rule into `FriendlyFire` is what makes this acceptable, matching how
`FlagCarrierMarker` and `CoinCarrierAura` (comparably thin visual components) have no EditMode
tests today either.

**Multi-peer verify** (3 peers: two on Team1, one on Team2 — matches the pattern used in prior
specs' verify sections):

1. Teammates walk through each other with no blocking or shove; opponents still collide normally.
2. Melee and projectiles do zero damage/knockback/stun to teammates (including self); both still
   land normally on the opposing player.
3. A friendly (or self) projectile passes through without despawning, then hits an enemy behind
   the teammate.
4. The chevron is visible over your own body only, on your own screen; the other two peers see no
   chevron on you, and you see no chevron on them.
5. Death and respawn: chevron remains visible throughout; teammate pass-through and hostile
   damage gating are both correct immediately on respawn (no stale ignores from the prior life,
   since the collider/object was never despawned).
6. Team reassignment (if triggerable in a test build): collision against former teammates returns
   within one refresh; collision against new teammates turns off within one refresh.

## Out of Scope

- Team-colored or teammate-visible markers (nameplates, pips) — explicitly deferred per decision 9.
- Any change to enemy-vs-enemy (Team3AI) or Team1-vs-Team2 collision — only same-team player pairs
  are affected.
- Any change to trigger-based systems (coin pickup, flag capture, home base) — out of scope by
  construction, since `FriendlyCollision` only touches non-trigger colliders.
- Damage sources other than melee and projectiles (there are none that target players today).
