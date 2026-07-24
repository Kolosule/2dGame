# Timing-Based Enemy Combat — Design

**Date:** 2026-07-23
**Status:** Approved (design)

## Goal

Make enemy combat reward timing your attacks well. Two enemy-side changes:
1. Enemies **attack faster when in range** (shorter windup + a bit less cooldown).
2. Enemies **take more knockback**, so a well-timed player hit pushes them out of
   their swing.

Player attack is unchanged. The timing skill is: react to the faster enemy windup and
land a hit that knocks the enemy past `attackRange`, which aborts its wound-up attack.

## Design decisions (from brainstorming)

- **Enemy-side only.** No new player mechanic (no parry / perfect-hit).
- **"Faster" = mainly windup, plus a modest cooldown reduction.**
- **Interrupt stays emergent** via knockback — no explicit telegraph-cancel code. More
  knockback pushes the enemy past `attackRange`; `EnemyAI.CompleteTelegraph`'s existing
  range re-check already turns the attack into a whiff when out of range.
- **Per-archetype, data-driven** through `EnemyStats` (consistent with the shape-archetype
  feature; different shapes feel different, tuned in one place).

## Existing flow (studied)

- `PlayerCombat.ApplyMeleeHits` → `enemy.TakeDamage(dmg, knockbackForce, hitPoint)`, where
  `knockbackForce = (dir.x * stats.attackForce, knockbackUpward)`. `Enemy.TakeDamage`
  applies it as an impulse (Rigidbody2D mass 1) and sets a 0.3s `knockbackTimer` that
  pauses the AI (`IsKnockedBack`).
- `EnemyAI`: Chase → within `attackRange` → `StartTelegraph` (windup = serialized
  `attackTelegraphDuration`, 0.5 on every prefab) → `CompleteTelegraph` re-checks range →
  `Attack` → Chasing, then waits `stats.attackCooldown`.
- Telegraph flash is reproduced on proxies from `[Networked] IsTelegraphing`, not the
  timer, so moving the duration into stats does not affect remote viewers.

## Changes

### `EnemyStats.cs` — two new fields
```csharp
[Tooltip("Windup shown before an attack lands (seconds). Shorter = faster, tighter " +
         "reaction window once in range.")]
public float attackTelegraphDuration = 0.5f;

[Tooltip("Scales incoming knockback this enemy takes from player hits. >1 = flies back " +
         "further, easier to knock out of an attack.")]
public float knockbackMultiplier = 1f;
```

### `EnemyAI.Initialize`
Read the windup from stats, overriding the component default (mirrors the existing
`detectionRange`/`leashRadius` reads):
```csharp
attackTelegraphDuration = stats.attackTelegraphDuration;
```
`attackTelegraphDuration` is already a serialized field on `EnemyAI`; it stays as the
editor/fallback default and is overridden here on the authority. No other AI change.

### `Enemy.TakeDamage`
Scale the impulse by the per-archetype multiplier before applying:
```csharp
Vector2 scaledForce = knockbackForce * (stats != null ? stats.knockbackMultiplier : 1f);
rb.AddForce(scaledForce, ForceMode2D.Impulse);
```
The 0.3s `knockbackTimer` and everything else stay the same. Authority-only path unchanged.

## Per-archetype values (starting point; tunable in-editor)

Add the two new fields to all four `EnemyStats_*` assets (preserving existing
health/speed/damage values) and nudge cooldowns down a bit:

| Archetype | attackTelegraphDuration | knockbackMultiplier | attackCooldown |
|---|---|---|---|
| Box | 0.45 | 1.6 | 1.05 |
| Octagon | 0.38 | 1.9 | 0.95 |
| Circle | 0.28 | 2.3 | 0.8 |
| Flyer | 0.24 | 2.6 | 0.8 |

Faster/aggressive archetypes get the shortest windup and the most knockback — the
relentless enemies are exactly the ones a well-timed hit can interrupt.

## Networking / testability

- Untouched: authority-only AI + damage, `[Networked]` telegraph/facing (proxies still
  flash), position via `NetworkRigidbody2D` (no NetworkTransform).
- Config plumbing + a scalar multiply — no new algorithm, so no new pure-logic tests are
  warranted. Verify via the Assembly-CSharp compile gate + in-editor feel/timing playtest.

## Out of scope

- No player-side timing mechanic. No new AI states. No change to the knockback pause
  duration or to `PlayerCombat`.
