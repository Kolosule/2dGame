# Enemy Shape Archetypes — Design

**Date:** 2026-07-23
**Status:** Approved (design), pending implementation plan

## Goal

Replace the current all-boxes enemy roster with **four distinct archetypes** that
differ in shape, speed, and feel — while keeping **one shared `EnemyAI`** tuned
per archetype through `EnemyStats` ScriptableObjects. The only new AI capability
is a **flight mode**; everything else (wander, chase, telegraph, attack, return,
leash, networking) stays shared and unchanged.

Ordered easy → hard:

1. **Box** — slow. Short detection, lazy wander. The baseline grunt.
2. **Octagon** — a bit quicker, slightly more detection/leash. (Shape rendered as a
   hexagon — the closest Unity built-in; archetype/stats keep the name "Octagon".)
3. **Circle** — fast, relentless pursuer. Large detection + leash, minimal wander so
   it reads as always coming for you.
4. **Flyer** — the challenge. True flight: gravity off, pursues the player freely in
   2D (up/down/diagonal), fast, large detection. Melee attack like the others.

## Existing architecture (studied before designing)

- `Assets/Scripts/Enemy/Base/EnemyStats.cs` — ScriptableObject: combat stats, moveSpeed,
  AI ranges (detectionRange, attackRange, leashRadius, wanderRadius).
- `Assets/Scripts/Enemy/Base/EnemyAI.cs` — shared, authority-only `MonoBehaviour`. State
  machine (Guard/Chasing/Telegraphing/Attacking/Returning) drives the `Rigidbody2D` only
  on the state authority via `Tick()`. `MoveToward` currently writes **X velocity only**
  and preserves gravity-owned Y; `HandleMovementJump`/`TryJump`/`IsGrounded` handle
  hopping. Wander and ReturnHome use **X-only arrival** checks (Y is gravity-owned).
- `Assets/Scripts/Enemy/Base/Enemy.cs` — `NetworkBehaviour`. Networked health, team,
  `IsTelegraphing`, `FacingLeft`. `Spawned()` already passes the whole `stats` object into
  `ai.Initialize(Home, effectiveMoveSpeed, stats)`, so a new `canFly` flag flows through
  with no signature change. Position replicates via `NetworkRigidbody2D` (no NetworkTransform).
- `Assets/Scripts/Enemy/Base/EnemySpawner.cs` (`NetworkedEnemySpawner`) — spawns an assigned
  prefab. **Unchanged** by this work.
- `Assets/Scripts/Enemy/AI/EnemyAILeash.cs` + `Assets/Tests/EditMode/EnemyAI/EnemyAILeashTests.cs`
  — pure leash math extracted for EditMode unit testing. This is the pattern to mirror.
- `Assets/Scripts/Enemy/Base/EnemyTeamComponent.cs` — recolors the sprite **only if** its
  `spriteRenderer` field is assigned. On all 7 prefabs it is unassigned (`fileID: 0`), so the
  authored per-prefab `m_Color` is what renders. Color identity is preserved for free.

The 7 color prefabs (`RedEnemy`, `VioletEnemy`, `OrangeEnemy`, `Indigo Enemy`, `BlueEnemy`,
`YellowEnemy`, `GreenEnemy`) are structurally uniform: `BoxCollider2D`, gravityScale 1, each
referencing its own `EnemyStats_N` asset.

## Archetype → prefab mapping

Four archetypes; each defined by one shared `EnemyStats` asset. The existing color prefabs
are re-shaped/re-tuned onto them (kept by name, so scene spawner references stay intact).
Two prefabs sharing an archetype reference the same stats asset.

| Archetype | Sprite (built-in v2) | Collider | Rigidbody2D gravity | Color prefabs |
|---|---|---|---|---|
| **Box** | Square | `BoxCollider2D` | 1 | Red, Violet |
| **Octagon** | HexagonFlatTop | `PolygonCollider2D` (6-pt hex) | 1 | Orange, Indigo |
| **Circle** | Circle | `CircleCollider2D` | 1 | Blue, Yellow |
| **Flyer** | IsometricDiamond | `CircleCollider2D` | **0** | Green |

### Built-in sprite references (com.unity.2d.sprite v2 shapes)

| Shape | png guid | sprite fileID | ref type |
|---|---|---|---|
| Square | `311925a002f4447b3a28927169b83ea6` | `7482667652216324306` | 3 |
| Circle | `a86470a33a6bf42c4b3595704624658b` | `-2413806693520163455` | 3 |
| HexagonFlatTop | `b670ab75dde984907b8570040daa08c5` | `-712631045867808456` | 3 |
| IsometricDiamond | `19fb86013d8c24d6cb8410c0aadf30fa` | `3625043607559282579` | 3 |

(Square is already the sprite the box enemies use, confirming the v2 references resolve.)

## Code changes — flight mode is the only new capability

### `EnemyStats.cs`
Add under the Movement header:
```csharp
[Tooltip("If true, the enemy flies: MoveToward drives both X and Y velocity and skips " +
         "ground/jump logic. Requires Rigidbody2D gravityScale = 0 on the prefab.")]
public bool canFly = false;
```

### `EnemyAI.cs`
- New field `private bool canFly;`, set in `Initialize` from `stats.canFly`.
- **`MoveToward(Vector2 target)`**: when `canFly`, set `rb.linearVelocity` to the full 2D
  steering vector (`direction * moveSpeed`), set facing from `direction.x`, and `return`
  before any jump/ground logic. The grounded branch (X velocity + preserved Y + `HandleMovementJump`)
  is unchanged.
- **Velocity halts**: introduce a `StopMovement()` helper and replace every
  `rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y)` site (wander pause, wander
  arrival, telegraph freeze, attack, return arrival) with it:
  - grounded: `new Vector2(0f, rb.linearVelocity.y)` (today's Y-preserving behavior),
  - flying: `Vector2.zero` (both axes — gravity 0 will not bleed off residual Y velocity,
    so without this a flyer drifts vertically forever after "arriving").

No other AI method changes. `TryJump` is a natural no-op for flyers (`IsGrounded()` is false
midair), so the flyer pursues freely up/down/diagonal with no special-casing in chase.

### Testability (TDD)
Mirror `EnemyAILeash`: extract the pure decisions into `Assets/Scripts/Enemy/AI/EnemyAIMovement.cs`:
```csharp
public static Vector2 SteeringVelocity(Vector2 from, Vector2 target, float moveSpeed,
                                       float currentVelocityY, bool canFly);
public static Vector2 StopVelocity(float currentVelocityY, bool canFly);
```
`MoveToward`/`StopMovement` call these. Unit-test in `Assets/Tests/EditMode/EnemyAI/EnemyAIMovementTests.cs`:
- flying steering drives both axes toward target at moveSpeed;
- grounded steering drives X only and preserves currentVelocityY;
- flying stop → `Vector2.zero`; grounded stop preserves Y;
- zero-length (from == target) direction is handled without NaN.

Existing `EnemyAILeashTests` stay untouched and passing.

## EnemyStats asset values (starting point; tunable in editor)

attackRange = 1.5 for all. Runtime difficulty-ring scaling (`ResolveEffectiveStats`) still
applies on top of these.

| Archetype | moveSpeed | detectionRange | leashRadius | wanderRadius | canFly | maxHealth | attackDamage | attackCooldown |
|---|---|---|---|---|---|---|---|---|
| Box | 2.5 | 6 | 10 | 4 | false | 20 | 3 | 1.2 |
| Octagon | 3.5 | 9 | 16 | 6 | false | 25 | 4 | 1.1 |
| Circle | 6 | 16 | 30 | 1 | false | 30 | 5 | 1.0 |
| Flyer | 6.5 | 18 | 32 | 3 | true | 28 | 6 | 1.0 |

New assets under `Assets/Scripts/Enemy/Types/`: `EnemyStats_Box.asset`, `EnemyStats_Octagon.asset`,
`EnemyStats_Circle.asset`, `EnemyStats_Flyer.asset` (each with a `.meta`/GUID). `enemyName` set to
the archetype name.

## Prefab edits (turnkey, hand-authored YAML)

For each of the 7 prefabs, per its archetype:
1. `SpriteRenderer.m_Sprite` → the archetype's built-in shape sprite (guid + fileID above).
   `m_Color` is left as authored (color identity preserved).
2. Collider: replace `BoxCollider2D` with the archetype's collider type, preserving the
   existing `PhysicsMaterial2D` reference and updating the GameObject `m_Component` fileID list:
   - Box → keep `BoxCollider2D` (size 1×1).
   - Circle / Flyer → `CircleCollider2D` (radius ≈ 0.5).
   - Octagon → `PolygonCollider2D` with 6 authored flat-top hexagon points inscribed in the
     unit sprite.
3. `Rigidbody2D.m_GravityScale`: 0 for the Flyer (Green), 1 for the rest.
4. `Enemy.stats` reference → the archetype's new `EnemyStats` asset (by GUID).

`EnemyStats_1..7` are left orphaned (harmless). No renames, so scene spawner references and
`NetworkRigidbody2D` wiring are untouched. No `NetworkTransform` is added.

## Networking (unchanged, must stay intact)

- Health/team/coin drops remain networked on `Enemy.cs`.
- AI runs authority-only; `IsTelegraphing`/`FacingLeft` stay `[Networked]` so proxies still
  see the telegraph flash and correct facing during attacks.
- Position replicates via `NetworkRigidbody2D`. The flyer is a normal dynamic body with
  gravityScale 0 whose velocity the authority drives each tick — no new networking needed.

## Testing & verification

- **EditMode:** new `EnemyAIMovementTests` (red→green→refactor); full `EnemyAI` suite green.
- **Compile:** verify outside the editor with the bundled Roslyn if Unity holds the lock.
- **In-editor (user):** open each prefab, confirm shape/collider/gravity/stats; play-test that
  Box is a lazy grunt, Circle relentlessly chases, Flyer pursues in true 2D and melee-attacks;
  confirm proxies (second peer) see telegraph + facing.

## Out of scope / non-goals

- No changes to `NetworkedEnemySpawner` (spawning stays as-is: assign a prefab per spawner).
- No new AI states or behaviors beyond the flight branch.
- No parallax, no art beyond built-in shape sprites, no true 8-sided octagon.
- No deletion of the old `EnemyStats_1..7` assets.
