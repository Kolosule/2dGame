# 2dGame Improvement Roadmap

Tracking doc for the prioritized improvements from the design review (2026-06-21). Each open
item has a self-contained prompt for a fresh Claude Code (Opus 4.8) session. Prepend the
**Shared context** block to whichever item prompt you run.

## Status

| # | Item | Status |
| --- | --- | --- |
| 1 | Rebuild player movement on Fusion's input/simulation model | ✅ Done (movement + melee verified; see note) |
| 2 | Eliminate duplicate/ghost input | ✅ Done (folded into #1; ghost input gone in MPPM) |
| 3 | One networked source of truth for team | ⬜ Open |
| 4 | Unify damage pipeline + decide CTF-vs-coins | ⬜ Open |
| 5 | Make projectiles and enemies network-correct | 🟡 In progress (projectile mid-debug; enemies untouched) |
| 6 | Delete dead / conflicting systems | ⬜ Open |
| 7 | TickTimer for networked timing; cut polling/log spam | ⬜ Open |
| 8 | Harden the spawn / team-selection handshake | ⬜ Open |

**Suggested order (by dependency): #3 → #8 → #4 → #5 → #6 → #7.**
Team identity (#3) underpins the spawn handshake (#8) and the damage pipeline (#4); cleanup (#6)
and timing polish (#7) come last.

### Notes on done/in-progress work
- **#1 / #2:** Implemented this session. Input now flows `NetworkInputProvider.OnInput` →
  `NetInput` struct → `PlayerController.FixedUpdateNetwork` → `PlayerMovement.Simulate` /
  `PlayerCombat.Simulate`. `NetworkPlayerWrapper` deleted; `NetworkRigidbody2D` +
  `RunnerSimulatePhysics2D` added. Single-player feel and two-player MPPM (no ghost input,
  prediction + interpolation) verified. Specs/plan in `docs/superpowers/specs|plans/`.
- **#5:** `Projectile` partially converted to a server-authoritative `NetworkBehaviour`, but
  shooting is currently broken (projectile not appearing). Diagnostic logs `[SHOOT-DIAG]`
  (PlayerCombat.cs) and `[PROJ-DIAG]` (Projectile.cs) are in place. Leading suspects: the player
  prefab's `PlayerCombat` Projectile Prefab / Projectile Spawn Point fields unassigned, and/or the
  Projectile prefab missing `NetworkRigidbody2D`. Enemy networking not started.

---

## Shared context (prepend to every item prompt)

```
Unity 6.3 (6000.3.0f1) 2D PvPvE game (two human teams + AI), Photon Fusion 2.0.9 in
Host/Client mode, Fusion Physics Addon installed. Git branch: Starting-agina-with-claude-code.

Recent history: a networking rebuild (review item #1) replaced the old hand-rolled
NetworkPlayerWrapper position sync with Fusion's authoritative model — input is collected in
NetworkInputProvider.OnInput into a NetInput struct, read in PlayerController.FixedUpdateNetwork,
and dispatched to PlayerMovement.Simulate / PlayerCombat.Simulate (both NetworkBehaviours,
tick-based with TickTimer + [Networked] state). NetworkRigidbody2D + RunnerSimulatePhysics2D
now sync the player and step Physics2D inside the network tick. Design/plan docs:
  docs/superpowers/specs/2026-06-21-fusion-movement-rebuild-design.md
  docs/superpowers/plans/2026-06-21-fusion-movement-rebuild.md

Conventions to follow: new Input System only (single device-read site is NetworkInputProvider);
gameplay timing in the simulation path uses TickTimer, not Invoke/Time.time/coroutines;
Runner.Spawn/Despawn and authoritative state changes happen under HasStateAuthority only.
Verification is manual/observational (no test assembly; game code is in Assembly-CSharp):
compile clean, then check in single-player (GameNetworkManager.singlePlayerMode=true, Host) and
in Multiplayer Play Mode (singlePlayerMode=false, 1 host + 1 virtual client). Commit with a clear
message referencing the review item. Before deleting anything, confirm it isn't referenced by a
scene/prefab.
```

---

## Item #2 — Eliminate duplicate/ghost input (verification sweep)

> Mostly done via #1. Run this only to confirm and to sweep leftover input reads.

```
# Task: Verify and finish eliminating duplicate/ghost player input (review item #2)

## Goal
Confirm player input is processed exactly once, only for the input-authority player, only through
the Fusion input pipeline — and remove any remaining code that reads input outside that pipeline
or processes it per-frame on all client copies (the original "ghost input / double-jump" cause).

## Do this
1. Read PlayerController.cs, PlayerMovement.cs, PlayerCombat.cs, NetworkInputProvider.cs,
   NetInput.cs and confirm no Update()/FixedUpdate() in the player scripts reads input; simulation
   runs only in FixedUpdateNetwork via GetInput; remote proxies are not driven by local input.
2. Grep Assets/Scripts for stray input reads that bypass the pipeline: UnityEngine.Input,
   Input.Get, Keyboard.current, Mouse.current, Gamepad.current, and any Update() that controls the
   player. Expected legitimate hit: NetworkInputProvider.OnInput. Investigate every other hit.
   Known leftovers: CameraFollow.cs uses Input.GetAxisRaw("Horizontal") for camera look-ahead;
   Coin Scripts/HomeBase.cs uses Input.GetKeyDown for manual deposit. Decide per case — camera
   look-ahead should derive from the followed player's velocity or local NetInput, not a raw global
   axis; the deposit key, if kept, must be read in a clearly local/non-simulation context.
3. Constraint: new Input System only in gameplay/simulation code; sole device-read site is
   NetworkInputProvider.OnInput.

## Verify
Compile clean; single-player feel unchanged; MPPM shows each window controls only its own
character (no double-jump or input bleed). Commit referencing item #2.
```

---

## Item #3 — One networked source of truth for team

```
# Task: Unify player team identity into a single networked source of truth (review item #3)

## Problem
Team identity is represented three ways and kept in sync by polling:
- PlayerTeamData (Assets/Scripts/Player/PlayerTeamData.cs): [Networked] int Team; its
  FixedUpdateNetwork polls every tick to copy the value into PlayerTeamComponent.
- PlayerTeamComponent (Assets/Scripts/Enemy/Base/PlayerTeamComponent.cs): string teamID +
  gameplay logic (territorial advantage, colors).
- TeamSelectionData (Assets/Scripts/Teams/TeamSelectionData.cs): static menu choice.
Plus EnemyTeamComponent (string). Every consumer re-derives "is this Team1/Blue/team1/Red/team2"
with its own string matching: TeamScoreManager.IsTeam1/IsTeam2, HomeBase.IsPlayerOnCorrectTeam,
Flag.GetTeamDisplayName, TerritoryZone. This is the source of most team bugs (wrong base,
friendly fire, colors).

## Goal
A single networked team value on the player, an OnChanged callback (not tick polling) to refresh
visuals/gameplay, and one shared team utility that replaces all the duplicated string matching.

## Steps
1. Introduce a Team enum (None=0, Team1=1, Team2=2, Team3AI=3) and a TeamUtil helper with
   Normalize(string)->enum and display-name/equality helpers. Put magic-string aliases
   (Blue/Red/team1/team2) in one place.
2. Make PlayerTeamData.Team the single networked source; replace its FixedUpdateNetwork polling
   with [Networked, OnChanged] (or OnChangedRender) to push updates into gameplay/visuals. Keep
   PlayerTeamComponent's gameplay logic but have it read team from the networked value rather than
   holding an independent string.
3. Replace IsTeam1/IsTeam2/IsPlayerOnCorrectTeam/GetTeamDisplayName string logic in
   TeamScoreManager, HomeBase, Flag (and EnemyTeamComponent where applicable) with TeamUtil.
4. Keep TeamManager (Assets/Scripts/Teams/TeamManager.cs) as the config/data lookup, keyed off
   the enum.

## Verify
Single-player + MPPM: player colors correct, friendly-fire correct, flag/home-base team checks
correct, no per-tick team-poll logs. Commit referencing item #3.

## Note: prerequisite for items #4 and #8.
```

---

## Item #4 — Unify the damage pipeline & resolve CTF-vs-coins

```
# Task: One damage pipeline + decide how CTF and the coin/territory economy relate (review item #4)

## Problem
Four parallel damage-modifier systems exist and NONE governs player melee damage:
- TeamManager: distance-based territorial modifier.
- TeamScoreManager (Assets/Scripts/Coin Scripts/TeamScoreManager.cs): buff-based 0.5x/1.0x from
  coin milestones — has getters nothing calls.
- TerritoryZone (Assets/Scripts/Coin Scripts/TerritoryZone.cs): a third, labeled "EXAMPLE".
- CombatConfig (Assets/Scripts/ScriptableObjects/CombatConfig.cs): CalculateFinalDamage + fields.
PlayerCombat.Attack deals a hardcoded damageAmount=25 and consults none of them. Enemy.AttackPlayer
DOES use the distance modifier. So territory rules apply to AI but not players.

Separately, two scoring/win systems run in parallel: CTF (capture both flags -> win) and the coin
loop (kill enemies -> coins -> deposit -> team score -> unlock buffs). The coin buffs currently
have ZERO gameplay effect because they're never applied to combat.

## Goal
1. Route ALL damage (player melee, enemy attacks, projectile once item #5 lands) through a single
   method that takes base damage + attacker/defender team + position and applies the agreed
   modifiers. CombatConfig.CalculateFinalDamage is the natural home.
2. Make an explicit product decision and implement it: is the coin/territory economy a meta-layer
   that buffs CTF combat, or a separate mode? Pick one. If meta-layer: wire TeamScoreManager buffs
   into the unified damage method. If separate: gate one mode off so they don't run simultaneously.
   Delete the modifier systems that aren't chosen.

## Steps
- Decide the model first (ask the human if unclear) and note it at the top of the changed file(s).
- Implement the single damage entry point; have PlayerCombat and Enemy both call it.
- Remove the now-dead modifier path(s).

## Verify
Single-player + MPPM: melee damage reflects the chosen modifiers; coin milestones visibly change
combat (if meta-layer) or are cleanly disabled (if separate). Commit referencing item #4.

## Depends on: item #3.
```

---

## Item #5 — Make projectiles and enemies network-correct

```
# Task: Finish networked projectile + make enemies network-correct (review item #5)

## Current state
A prior session began converting Projectile (Assets/Scripts/Player/Projectile.cs) to a
NetworkBehaviour (server-spawned via PlayerCombat.ShootProjectile, single-source damage,
Runner.Despawn, networked stun) but shooting is currently BROKEN/mid-debug: the projectile does
not appear. Temporary diagnostic logging is tagged [SHOOT-DIAG] (PlayerCombat.cs) and [PROJ-DIAG]
(Projectile.cs). Leading suspects: the player prefab's PlayerCombat "Projectile Prefab" /
"Projectile Spawn Point" fields became unassigned during earlier prefab edits, and/or the
Projectile prefab is missing a NetworkRigidbody2D. Start by reproducing with those logs and
checking those two things.

## Problems
- Projectile: must spawn on server only, sync via NetworkRigidbody2D, detect hits/apply damage on
  StateAuthority once, despawn via Runner.Despawn (never Destroy on a NetworkObject).
- Enemy: Enemy (Assets/Scripts/Enemy/Base/Enemy.cs) is networked (health) but EnemyAI
  (Assets/Scripts/Enemy/Base/EnemyAI.cs) is a plain MonoBehaviour running independently on every
  client and driving the Rigidbody2D locally — positions diverge, worse now that Physics2D is
  Fusion-stepped. Enemies need a NetworkRigidbody2D and their AI must run only on StateAuthority
  (proxies interpolate).

## Steps
1. Fix shooting: verify/repair the PlayerCombat projectile fields on the player prefab; add
   NetworkRigidbody2D to the Projectile prefab; confirm it spawns, flies, damages once, despawns.
2. Convert enemy movement to authoritative: add NetworkRigidbody2D to the enemy prefab; gate
   EnemyAI's movement/state machine to run only under HasStateAuthority (drive it from the Enemy
   NetworkBehaviour's FixedUpdateNetwork, or guard Update with an authority check); let proxies
   interpolate. Preserve existing patrol/chase/telegraph behavior.
3. Remove the [SHOOT-DIAG]/[PROJ-DIAG] logging once verified.

## Verify
MPPM: both players' projectiles appear in both windows, deal damage once, despawn cleanly (no
"Destroy on NetworkObject" warnings). Enemies are in the same position in both windows and chase
correctly. Commit referencing item #5.
```

---

## Item #6 — Delete dead / conflicting systems

```
# Task: Remove dead and conflicting systems (review item #6)

## Targets (verify each isn't referenced by a live scene/prefab before removing)
1. MultiplayerRespawnManager (Assets/Scripts/Player/RespawnManager.cs): a non-networked manager
   that Instantiates players directly in Start() — parallel to and conflicting with the Fusion
   NetworkedSpawnManager. Remove it (and scene references).
2. TerritoryZone (Assets/Scripts/Coin Scripts/TerritoryZone.cs): unintegrated "EXAMPLE". Remove
   (coordinate with item #4 if that work is happening).
3. EnemySpawner (Assets/Scripts/Enemy/Base/EnemySpawner.cs ~145-153): SetPatrolPoints is commented
   out, so spawned enemies never receive patrol points. Either re-enable it (assign the
   created/auto patrol points) or remove the dead patrol-point plumbing — make it work or be gone.
4. Flag.cs (Assets/Scripts/CTF Flag/Flag.cs ~163-166): a `movement.enabled=false; movement.enabled
   =true;` no-op in PickupFlag. Remove.
5. PlayerInventory.OnPlayerDeath (Assets/Scripts/Coin Scripts/PlayerInventory.cs): a TODO that
   clears coins instead of dropping them — implement coin-drop-on-death or remove the dead stub.
6. Debug-only scripts (DebugTeamDisplay, RespawnDebug, SpawnManagerDebugger, EnemySpawnerDebugger):
   confirm not needed and remove, or clearly mark editor-only.

## Goal
Each listed thing is either made to actually work or fully removed (script + .meta + scene refs).

## Verify
Compile clean; single-player + MPPM still spawn exactly one player per client, enemies patrol,
flags pick up/drop. Commit referencing item #6.
```

---

## Item #7 — TickTimer for networked timing; cut polling & log spam

```
# Task: Replace real-time timers with TickTimer and reduce polling/log spam (review item #7)

## Problems
- PlayerStatsHandler (Assets/Scripts/Player/PlayerStatsHandler.cs): respawn uses
  Invoke(nameof(Respawn),3f); spawn immunity and rapid-hit guard use Time.time. Convert to
  TickTimer.
- Flag (Assets/Scripts/CTF Flag/Flag.cs): auto-return uses StartCoroutine + WaitForSeconds.
  Convert to a server-evaluated TickTimer.
- CTFGameManager (Assets/Scripts/CTF Flag/CTFGameManager.cs): Update() rebuilds all flag UI every
  frame; win condition polls 4 flag-to-base distances every tick (with a fudged capture distance).
  Drive UI and win checks off the flag's networked state changes (OnChangedRender) instead.
- Log spam: Debug.Log inside FixedUpdateNetwork and RPCs (e.g. TeamScoreManager.RPC_AddPoints).
  Strip or gate behind a verbose flag.
- Runtime lookups in hot/repeated paths: FindObjectsByType/FindFirstObjectByType in
  PlayerStatsHandler.DropFlagOnDeath, etc. Cache or use existing singletons.

## Goal
All gameplay timing in the simulation path is tick-based and deterministic; no per-frame distance
polling for win conditions; no Debug.Log in per-tick/RPC hot paths; no per-call scene-wide Find in
hot paths.

## Verify
Single-player + MPPM: respawn timing, spawn immunity, flag auto-return, and CTF win detection
behave correctly and identically across host/client. Commit referencing item #7.
```

---

## Item #8 — Harden the spawn / team-selection handshake

```
# Task: Make player spawning and team selection race-free (review item #8)

## Problem
NetworkedSpawnManager (Assets/Scripts/NetworkSpawnManager.cs) drives spawning from a 0.5s
coroutine (DelayedPlayerCheck -> CheckForExistingPlayers) plus OnSceneLoadDone. AssignTeam reads
pendingTeamChoices (correct, set via RPC_SetPlayerTeamChoice) but falls back to the static
TeamSelectionData — which only exists on the host's machine, so it's meaningless for remote
clients. If a client's team-choice RPC hasn't arrived within the 0.5s window, the player is
auto-balanced onto the wrong team.

## Goal
Spawn each player only after their team choice is known, via an explicit signal — not a timed poll.
Remove the host-only static fallback from server-side assignment.

## Steps
1. Replace the time-delay spawn trigger with an explicit ready signal: spawn in response to the
   team-choice RPC (server has the choice), or store the choice in a per-player networked property
   the server reads before spawning.
2. Remove the TeamSelectionData fallback from AssignTeam; keep auto-balance only as a deliberate
   "no choice made" path, not a race artifact.
3. Coordinate with item #3 (use the unified team enum/source).

## Verify
MPPM with both players choosing teams (including the client choosing the non-default team): each
spawns on the team they picked, at the right spawn point, every time across repeated runs. Commit
referencing item #8.

## Depends on: item #3.
```
