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
- Purely cosmetic/local: only the attacking client sees the flash, particles,
  and number for a given hit. No networked state, no RPC. This mirrors the
  existing hit-marker behavior and is a deliberate bandwidth/consistency
  tradeoff.
- Out of scope: hit-stop/freeze-frame and camera punch (explicitly not
  requested); networked/synced hit effects.

## Architecture

One new static helper, `HitFeedback.Play(GameObject target, Vector2 hitPoint, int damage)`,
called from every place damage currently lands:

- `PlayerCombat.ApplyMeleeHits()` — enemy branch (~line 212) and player branch
  (~line 229). Since dash-strike routes through this same method, it's covered
  automatically.
- `Projectile.OnTriggerEnter2D()` — enemy branch (~line 107) and player branch
  (~line 91).

A shared helper avoids duplicating instantiate/destroy boilerplate across four
call sites (today's hit-marker code is only duplicated in one place; adding
three effects to four sites without a helper would be worse).

## Components

1. **`HitFeedback.cs`** (new static class, `Assets/Scripts/Player/`) — single
   public method, does three things per call:
   - Instantiate a particle-burst prefab at `hitPoint`; `Destroy` after its
     `ParticleSystem.main.duration` (same lifecycle pattern as
     `hitMarkerPrefab`).
   - Instantiate a floating damage-number prefab at `hitPoint`, call
     `Init(damage)` on its `DamageNumber` component.
   - `target.GetComponentInChildren<HitFlash>()` and call `PlayFlash()` if
     found.

2. **`HitFlash.cs`** (new tiny `MonoBehaviour`, added to Enemy and Player
   prefabs alongside their `SpriteRenderer`) — exposes `PlayFlash()`: stops any
   running flash coroutine, snaps `SpriteRenderer.color` to white, lerps back
   to the original color over ~0.1s. Owning the coroutine on the target means
   rapid repeated hits just restart it cleanly, and it's a no-op safe against
   the target being destroyed.

3. **`DamageNumber.cs`** (new tiny `MonoBehaviour` on a new prefab) —
   `Init(int amount)` sets a TMP text field; in `Update`, drifts upward
   (~1 unit/sec) and fades alpha over ~0.7s, then `Destroy(gameObject)`.

4. **Particle prefab** — plain Unity `ParticleSystem` (small spark/burst), no
   new script required.

## Data flow

Attacker's client (has state authority over its own `PlayerCombat`) computes
`finalDamage` → calls the existing authoritative damage call
(`enemy.TakeDamage(...)` / `targetPlayer.ServerApplyDamage(...)`, unchanged) →
immediately also calls `HitFeedback.Play(hit.gameObject, hit.transform.position, finalDamage)`
locally, no RPC. Other clients do not see this instance's flash/particles/
number.

## Error handling

- Missing `HitFlash` on target → skip flash silently (mirrors the existing
  `if (sr != null)` null-check at `PlayerCombat.cs:201`).
- Missing prefab references (`hitFeedback` fields unassigned in the inspector)
  → null-checked and skipped, same as `hitMarkerPrefab != null` today.

## Testing

- Manual in-editor: melee an enemy, a dash-strike, and an enemy player;
  confirm particle burst + flash + damage number appear only on the
  attacker's screen, and that other clients see nothing extra.
- No automated EditMode/PlayMode tests planned — this is pure cosmetic VFX
  with no networked state, consistent with how hit markers and tracers are
  untested today.
