# Zone-Bound, Center-Scaled Enemy AI — Design

**Date:** 2026-06-24
**Status:** Approved for planning

## Goal

Replace the current enemy AI with a single, data-driven system shared by all enemy
types. Enemies must:

1. **Stay in their zone** — roam around a home anchor and never chase players across
   the map.
2. **Target players that come close** — engage the nearest valid player who enters
   detection range, then disengage and return home when the player leaves.
3. **Scale in difficulty toward the center** — enemies whose zone sits nearer the map
   center are tougher (more health, damage, and move speed), computed once at spawn.

"Scalable & simple" means: a new enemy type is a new `EnemyStats` asset + prefab, with
**no new code**. One AI class, one shared difficulty config for the whole game.

## Non-Goals (v1)

- No per-enemy difficulty visual tint / `RingIndex` networked state (deferred).
- No dynamic re-scaling based on the player's position (difficulty is static per enemy,
  derived from the enemy's own zone location).
- No detection/leash/attack range scaling (only health, damage, move speed scale).
- No advanced pathfinding/navmesh — direct steering with leash clamping, as today.

## Architecture

Keep the existing component split; changes are surgical.

| Component | Role / Change |
|---|---|
| `EnemyStats` (ScriptableObject) | **Base = outer-rim baseline (×1.0).** Add `detectionRange`, `attackRange`, `leashRadius`, and `wanderRadius` here so they are per-archetype data instead of hardcoded on the AI. |
| `DifficultyRingConfig` (**new SO**) | Single shared asset. Ordered list of concentric rings; each ring carries `healthMult`, `damageMult`, `speedMult`. |
| `ArenaCenter` (**new, tiny MonoBehaviour**) | Marks map center; exposes a singleton accessor + world position. |
| `Enemy` (NetworkBehaviour) | On `Spawned` (authority): capture home, resolve ring from distance(home → center), compute effective stats, init health from effective max, feed effective speed/damage to the AI and combat. |
| `EnemyAI` (brain) | New leashed state machine: **Guard(wander) → Chase → Telegraph → Attack → Return.** Patrol A/B removed. |
| `NetworkedEnemySpawner` | Simplifies: no patrol-point creation. Home anchor = spawn position (captured by `Enemy`). Keeps team/territory assignment. |

## Data Model

### EnemyStats (extended)
Base values represent the **outermost ring (×1.0)**.

```
Identity:    enemyName
Combat:      maxHealth, attackDamage, attackCooldown
Movement:    moveSpeed
Ranges:      detectionRange, attackRange, leashRadius, wanderRadius   // NEW
Progression: level, enemyTeam
```

Constraint: `wanderRadius <= leashRadius`. (`wanderRadius` = how far it roams while
idle-guarding; `leashRadius` = hard max distance from home it will ever travel.)

### DifficultyRingConfig (new ScriptableObject)
A single shared asset (`Enemy/Difficulty Ring Config`).

```
rings: ordered list, INNER → OUTER (ascending maxDistanceFromCenter), each:
  - maxDistanceFromCenter : float   // upper bound of this ring's distance band
  - healthMult            : float
  - damageMult            : float
  - speedMult             : float
```

`GetRing(distance)` returns the first ring whose `maxDistanceFromCenter >= distance`
(so the innermost matching band wins). Distances beyond the outermost ring clamp to the
outermost ring (×1.0 baseline). With ascending order, small distances (near center) match
the early, toughest rings and large distances match the later, baseline rings.

### ArenaCenter (new)
- `MonoBehaviour` with a static `Instance` (set in `Awake`, cleared in `OnDestroy`).
- Exposes `Vector2 Position => transform.position`.
- Exactly one expected per scene.

## Behavior (the brain)

State machine, authority-only (driven by `Enemy.FixedUpdateNetwork → EnemyAI.Tick`):

- **Guard (wander)** — default. Roam around home: pick a random point within
  `wanderRadius` of home, move to it, pause briefly, pick another. Each tick, scan for
  targets.
- **Detection** — find the **nearest living, non-stealthed player** within
  `detectionRange` *and* within `leashRadius` of home → enter **Chase**.
- **Chase** — steer toward the target, but **clamp at the leash boundary**: never travel
  farther than `leashRadius` from home. If the target leaves `detectionRange` or the
  leash, **disengage → Return**.
- **Telegraph → Attack** — unchanged from current implementation (telegraph timer +
  flash + dodge window; on completion, attack if still in `attackRange`, else back to
  Chase).
- **Return** — walk back toward home; on arrival resume **Guard**.
- Knockback still pauses the AI (unchanged: `Enemy.IsKnockedBack`).

Target selection: nearest valid player only (players-only targeting; existing dead and
stealth checks retained). No other-AI targeting.

## Difficulty Scaling (rings)

At spawn, on the authority only:

```
home       = transform.position
distance   = Vector2.Distance(home, ArenaCenter.Instance.Position)
ring       = DifficultyRingConfig.GetRing(distance)
effHealth  = round(stats.maxHealth   * ring.healthMult)
effDamage  = round(stats.attackDamage * ring.damageMult)
effSpeed   = stats.moveSpeed         * ring.speedMult
CurrentHealth = effHealth
```

Effective stats are stored in plain authority-side fields and handed to the AI and to
the combat path. Static for the enemy's lifetime → zero per-tick scaling cost and no
extra networked state. Detection, leash, attack range, and cooldown are **not** scaled.

## Edge Cases & Error Handling

- No `ArenaCenter.Instance` or no `DifficultyRingConfig` assigned → fall back to ring 0 /
  base stats (×1.0), log a single warning. AI still functions.
- Target dies, stealths, or exceeds leash/detection → disengage cleanly to Return.
- Leash guarantees no infinite pursuit.
- Missing `SpriteRenderer` → telegraph flash skipped, warned (as today).

## Networking

- Difficulty resolution and the entire state machine run **authority-only**, exactly like
  today. Proxies interpolate position via `NetworkRigidbody2D` and reproduce facing +
  telegraph from existing `[Networked]` bools.
- No new networked state in v1 (`RingIndex` tint deferred).

## Testing

Extract the two pure decision functions so they are unit-testable in EditMode (no Unity
runtime required):

1. `DifficultyRingConfig.GetRing(distance)` → ring/multipliers. Cases: at center, at rim,
   exactly on a band boundary, beyond the outermost ring, empty/unconfigured list.
2. Leash helper: given `home, currentPos, targetPos, leashRadius` → desired move target
   (clamped to boundary) and disengage decision. Cases: target inside leash, target
   beyond leash, enemy already at boundary.

The networked state-machine wiring is validated by in-editor play-testing.

## Migration Notes

- `EnemyAI` loses `pointA`/`pointB`/`SetPatrolPoints`; spawner stops creating patrol
  points. Existing enemy prefabs need their new range fields set on `EnemyStats` assets.
- One `ArenaCenter` must be placed in each gameplay scene.
- One `DifficultyRingConfig` asset authored and referenced (via `GameSettingsManager` or
  a direct serialized field on `Enemy`/spawner — decided in the plan).
