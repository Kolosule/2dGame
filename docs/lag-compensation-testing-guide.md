# Player Combat Lag Compensation

This project uses Photon Fusion lag compensation for player-versus-player melee. The code is ready
for controlled testing, but the latency and dedicated-server performance matrix below still needs
to be completed before calling the feature fully validated.

## What problem this solves

Lag is the travel time between a client and the server. A player can see an opponent inside a
melee box, press attack, and then have the server receive that input after the opponent has moved.
A normal server query checks the opponent's latest server position and can reject a hit that looked
correct to the attacker.

For player melee, the server now asks Fusion where player hitboxes appeared to the attacking client
when that input was generated. Fusion reads an earlier snapshot from its history; it does not move
the live player backward. The server still checks teams, health, spawn immunity, cooldowns, damage,
and knockback, and only the server can change health.

## Technical decision

The installed package is Photon Fusion **2.0.12 Stable, build 1861**. Its installed XML and DLL
documentation are authoritative:

- `HitboxManager.OverlapBox(...)` accepts a `PlayerRef`, a reusable
  `List<LagCompensatedHit>`, a layer mask, and `HitOptions`.
- `HitOptions.SubtickAccuracy` interpolates the historical snapshots as the attacker saw them.
- `HitOptions.IgnoreInputAuthority` excludes hitbox roots owned by the attacker.
- `HitboxRoot` registers child `Hitbox` components in Fusion's history.
- Historical shapes are 3D boxes, spheres, and capsules. `IncludeBox2D` only adds current Box2D
  colliders; it does not rewind a moving `Collider2D`.

The installed manager exposes `Raycast`, `RaycastAll`, `OverlapSphere`, `OverlapBox`, and
`PositionRotation`. It has no historical 2D overlap, capsule-overlap query, sphere cast, or
projectile/ballistic query. This implementation uses only `OverlapBox`, with neither
`IncludePhysX` nor `IncludeBox2D`.

The selected approach is therefore **a thin 3D Fusion box representing each player on the 2D
plane**. Existing Box2D colliders remain responsible for movement, walls, triggers, projectiles,
friendly collision, flags, coins, and enemy attacks.

Dedicated Server Mode has no real `Runner.LocalPlayer`. Each query instead passes the attacking
player object's `InputAuthority`, which lets Fusion select that remote player's historical tick.
An invalid or missing authority produces a warned current-tick fallback rather than querying from
the server's perspective.

## Which attacks are compensated

| Attack or target | Detection |
| --- | --- |
| Side, upward, and downward player melee | Historical Fusion player hitbox |
| Ground-pound player contact | Historical Fusion player hitbox |
| Quicker Dash damaging dash | Historical Fusion player hitbox |
| Any of those attacks against enemies | Current-tick `Physics2D` box |
| Physical projectiles against players or enemies | Current-tick `OnTriggerEnter2D` |

Fusion 2.0.12 has no swept-sphere or ballistic projectile lag-compensation API. Combining a
historical player overlap with the projectile's live Box2D wall trigger could accept a player
behind a wall or apply both trigger and historical hits. Physical projectiles are intentionally
not rewound until a validated projectile design can preserve wall ordering and one-impact
semantics.

## Authoritative hit flow

1. The server reaches an active melee or damaging-dash tick.
2. Enemies are collected from the current Box2D world on the `Enemy` layer.
3. If the game-level switch, Fusion history, and attacker authority are valid, the server performs
   `Runner.LagCompensation.OverlapBox` on the `Player` layer.
4. Otherwise, only player detection falls back to a current-tick Box2D query.
5. A reusable registry keyed by `NetworkId` permits one target per swing or damaging dash, even if
   a target later gains more colliders or hitboxes.
6. Friendly, self, dead, spawn-immune, and rapid-repeat hits are rejected before feedback or
   knockback.
7. Territorial damage uses the defender's current authoritative team and position.
8. An accepted historical hit applies knockback to the defender's current authoritative
   `Rigidbody2D`. No transform or rigidbody is rewound or teleported.

## Player hitbox

`Assets/Scripts/Player/PlayerPrefab.prefab` has one `HitboxRoot` and one box `Hitbox` on the
networked player root:

- XY offset: `(0.012964457, -0.19861764)`, matching the retained `BoxCollider2D`
- XYZ half-extents: `(0.49968734, 0.8525756, 0.125)`
- full Z thickness: `0.25`, centered on the game's Z=0 plane
- broadphase radius: `1.0460912`
- layer: `Player`

One body region is enough for early alpha because combat has no headshot or limb-specific rules.
`PlayerStatsHandler` disables the `HitboxRoot` at death and re-enables it after the authoritative
respawn teleport. A current dead-state check prevents an old pre-death snapshot from taking damage;
spawn immunity protects a newly respawned player while older snapshots age out.

## History and capacity

`Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion` contains:

| Setting | Value | Reason |
| --- | ---: | --- |
| History | 200 ms | Covers the requested test range without a long rewind window |
| Default hitbox capacity | 32 per snapshot | 20 players x 1 body hitbox + 12 spare (60% margin) |
| Static collider cache | 0 | The Fusion query does not include PhysX or Box2D statics |
| Simulation/send rate | 64 Hz | Unchanged |

At 64 Hz, Fusion allocates `ceil(0.200 x 64) = 13` history snapshots. The initial player history is
therefore `13 x 32 = 416` hitbox slots, plus Fusion's snapshot/BVH bookkeeping. Installed
documentation does not state exact bytes per slot. Memory scales linearly with snapshot count and
per-snapshot capacity; Fusion can grow the arrays, but a growth warning means the configured
budget or hitbox count should be reviewed.

AOI remains enabled. The authoritative server records its player roots and performs all damage
queries; clients do not decide hits and do not need history for players outside their AOI. Nearby
combat must still be exercised in the matrix because newly interested objects and reconnects are
integration behavior, not covered by EditMode tests.

## Enable, compare, and disable

The reversible game switch is:

`Assets/Scripts/ScriptableObjects/CombatConfig.asset` -> **Enable Lag Compensation**

- **Off (safe default pending latency sign-off):** current-tick Box2D detection for both players
  and enemies.
- **On for A/B testing:** historical player detection; current-tick enemy detection. Fusion keeps
  recording its small history in either mode, so this switch can change between test builds
  without editing Photon package files.

Diagnostic summaries are off by default. To measure a crowded test, enable **Log Lag Compensation
Diagnostics** on the same asset. The server then prints an occasional aggregate summary containing
historical melee query count, accepted historical player hits, current-tick enemy hits, rejected
duplicates, and average query time. It never logs every tick or every query.

## Automated checks

Run the repository's EditMode suite:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" `
  -batchmode -nographics -projectPath "<worktree>" -runTests `
  -testPlatform EditMode -testResults "<results.xml>" -logFile "<tests.log>"
```

The focused tests cover friendly fire and self-hit rules, target-ID deduplication, independent
swing/dash registries, dead and spawn-immune damage gates, secondary-effect rejection, feature
fallback and invalid authority, the safe disabled default, territorial math, prefab hitbox geometry, retained
`BoxCollider2D`, Fusion network-object baking, and history/capacity configuration.

EditMode tests cannot prove historical tick selection, AOI transitions, network jitter, live
rigidbody impulses, or dedicated-server tick time. Use the following two-client test.

## Latency test matrix

Run one dedicated Linux server and at least two clients. For each row, select
`NetworkProjectConfig.fusion` in Unity, enable its network-condition simulation for the test build,
and set delay/jitter/loss to match the row. Fusion's delay and jitter fields are seconds of packet
delay, not a guaranteed measured round trip; start near half the desired RTT per direction and
adjust until Fusion's runtime statistics show the target RTT/jitter. Restore network-condition
simulation to disabled after testing.

| RTT | Jitter | Packet loss |
| ---: | ---: | ---: |
| 0 ms | 0 ms | 0% |
| 50 ms | 5 ms | 0% |
| 100 ms | 10 ms | 1% |
| 150 ms | 20 ms | 2% |
| 200 ms | 30 ms | 3-5% |

At each condition, run once with **Enable Lag Compensation** off and once with it on. Exercise:

- stationary, approaching, retreating, and cross-screen targets
- moving attacker and both players moving
- jump/aerial, ground-pound, and damaging-dash hits
- physical projectile at a moving target and a target beside a wall
- death or respawn during a swing
- two overlapping targets and several players fighting in one area
- disconnect/reconnect followed by immediate melee and projectile combat

Record visible connects rejected by the server, visible misses accepted by the server, duplicate
damage, friendly fire, wall penetration, allocations, server errors, average lag query time, and
64 Hz tick stability. A hit is suspicious when the target was outside the attacker's visible box
at attack time, is a teammate, is dead/spawn-immune, takes damage twice from one attack, or is
knocked back from an old position.

Do not mark the feature validated unless high-latency melee improves without degrading low-latency
accuracy or introducing duplicate/invalid damage.

## Rollback

1. Turn off **Enable Lag Compensation** in `CombatConfig.asset`.
2. Confirm melee uses the warned/documented current-tick player fallback.
3. If removing the Fusion history overhead entirely, set `LagCompensation.Enabled` to `false` in
   `NetworkProjectConfig.fusion`.
4. Keep every existing `Collider2D` and `Rigidbody2D`.
5. Remove `HitboxRoot` and `Hitbox` from `PlayerPrefab` only if no other feature uses them.
6. Rebuild both the client and Linux dedicated server.
7. Repeat a basic enemy/player melee, damaging-dash, wall projectile, death, and respawn test.

The fallback code and Box2D components remain in source, so rollback does not depend on remembering
old prefab values.

## Validation still required

- Complete the full latency matrix; no multi-peer latency result is recorded yet.
- Capture crowded-combat query time, allocations, and dedicated Linux server tick duration.
- Confirm nearby AOI entry and reconnect history behavior in a live server session.
- Revisit projectiles only with a design that orders historical player hits against walls.
