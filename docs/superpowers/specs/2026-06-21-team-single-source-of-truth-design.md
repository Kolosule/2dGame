# Design: One networked source of truth for team (review item #3)

Date: 2026-06-21
Branch: Starting-agina-with-claude-code
Status: Approved (pending spec review)

## Problem

Team identity is represented three ways and kept in sync by polling:

- `PlayerTeamData` (`Assets/Scripts/Player/PlayerTeamData.cs`): `[Networked] int Team`; its
  `FixedUpdateNetwork` polls every tick to copy the value into `PlayerTeamComponent`.
- `PlayerTeamComponent` (`Assets/Scripts/Enemy/Base/PlayerTeamComponent.cs`): independent
  `string teamID` + gameplay logic (territorial advantage, colors, damage modifiers).
- `TeamSelectionData` (`Assets/Scripts/Teams/TeamSelectionData.cs`): static menu choice (`int`).
- `EnemyTeamComponent` (`Assets/Scripts/Enemy/Base/EnemyTeamComponent.cs`): `string teamID`.

Every consumer re-derives "is this Team1/Blue/team1/Red/team2" with its own ad-hoc string
matching: `TeamScoreManager.IsTeam1/IsTeam2`, `HomeBase.IsPlayerOnCorrectTeam`,
`Flag.GetTeamDisplayName`, `TerritoryZone`, `CoinData.GetValueForTeam`,
`PlayerStatsHandler.ConvertTeamIdToNumber`, etc. This duplicated, inconsistent matching is the
source of most team bugs (wrong base, friendly fire, colors).

## Goal

A single networked team value on the player, an `OnChanged` callback (not tick polling) to
refresh visuals/gameplay, and one shared team utility that replaces all the duplicated string
matching. Prerequisite for review items #4 and #8.

## Investigation findings

- `PlayerPrefab.prefab` carries `PlayerTeamData` **and** `PlayerTeamComponent` (and
  `PlayerCameraRespawnHandler`) together, so `PlayerTeamComponent` can safely derive its team
  from the co-located networked `PlayerTeamData`.
- Enemies use only `EnemyTeamComponent` (string `teamID` set at spawn by
  `NetworkedEnemySpawner`); they have no `PlayerTeamData`. This is a separate path.
- The legacy `MultiplayerRespawnManager` (`Assets/Scripts/Player/RespawnManager.cs`) is **not**
  referenced by any scene or prefab. It is `Instantiate`-based (incompatible with the Fusion
  spawn flow) and sets the `teamID` field this change removes.
- Existing asset/scene/prefab data uses the canonical string IDs `Team1`/`Team2`/`Team3`
  (e.g. `Team1Data.asset` `teamID: Team1`, coin assets `coinTeam: Team3`, `Flag.owningTeam`,
  `HomeBase.baseTeam` serialized as strings).

## Architecture

### 1. `Team` enum + `TeamUtil` (new files, `Assets/Scripts/Teams/`)

```csharp
public enum Team { None = 0, Team1 = 1, Team2 = 2, Team3AI = 3 }
```

`TeamUtil` — a pure static helper, no scene/`MonoBehaviour` dependency. It is the **single**
place magic-string aliases (Blue/Red/team1/team2/ai) live.

- `Team Normalize(string)` — case- and whitespace-insensitive:
  `team1`/`blue`/`1` → `Team1`; `team2`/`red`/`2` → `Team2`; `team3`/`ai` → `Team3AI`;
  null/empty/unknown → `None`.
- `int ToNumber(Team)` — `Team1`→1, `Team2`→2, `Team3AI`→3, `None`→0.
- `Team FromNumber(int)` — inverse of `ToNumber`.
- `string ToId(Team)` — canonical IDs `"Team1"/"Team2"/"Team3"`, `None`→`""`. Matches existing
  asset `teamID`/`coinTeam` values so `TeamManager` lookups by id keep working.
- `string DisplayName(Team)` — `"Blue"/"Red"/"AI"`, `None`→`"Unknown"`.
- `bool AreEnemies(Team a, Team b)` — two distinct, non-`None` teams are hostile (PvPvE: all
  teams hostile to each other).
- `bool IsPlayerTeam(Team)` — `Team1` or `Team2`.

### 2. `PlayerTeamData` — the single networked source

- Replace `[Networked] int Team` with `[Networked, OnChangedRender(nameof(OnTeamChanged))]
  Team Team`. (C# "Color Color" rule allows a property named `Team` of type `Team`; enum
  literals are written `Team.Team1`.)
- Delete the per-tick `FixedUpdateNetwork` polling. `OnTeamChanged` (render callback) pushes
  the new value into `PlayerTeamComponent` (refresh visuals) and re-runs teammate-collision
  setup. Call `OnTeamChanged()` once in `Spawned()` so clients that receive the value as
  initial state (no change event) still initialize correctly.
- `public void SetTeam(Team team)` guarded by `Object.HasStateAuthority`. Keep validation
  (reject `None`/AI for player assignment). Remove the manual `previousTeam` bookkeeping and the
  string-building bridge — the OnChanged callback replaces it.
- Utility methods (`IsSameTeam`, `IsOnTeam`, `GetTeamID`) re-expressed in terms of `Team`/
  `TeamUtil` (or removed if unused after migration).

### 3. `PlayerTeamComponent` — gameplay logic, no independent identity

- Remove the serialized `string teamID`. Cache `PlayerTeamData` in `Awake`.
- Expose `public Team Team => teamData != null ? teamData.Team : Team.None;`.
- `OnTeamChanged()` (called by `PlayerTeamData`) refreshes the sprite color from
  `TeamManager.GetTeamData(Team)`. Re-arm visual init so a team that arrives after `Awake`
  still colors correctly (drop the one-shot `visualsInitialized` guard, or reset it on change).
- Territorial-advantage / damage-modifier methods read `Team` and call the enum-keyed
  `TeamManager` API.

### 4. `TeamManager` — config/data lookup keyed off the enum (step 4)

- Add enum-keyed API: `TeamData GetTeamData(Team)`, `float GetDamageDealtModifier(Team, float)`,
  `float GetDamageReceivedModifier(Team, float)`, `bool AreEnemies(Team, Team)`,
  `bool IsAITeam(Team)`, `Team[] GetPlayerTeams()` (or keep returning ids as needed).
- Internally maps `Team` → the assigned `TeamData` asset (`team1Data`/`team2Data`/`team3Data`),
  using `TeamUtil.Normalize(asset.teamID)` to bridge the asset's string id to the enum.
- Existing string-keyed methods are removed once all callers use the enum API (the asset
  `teamID` strings remain only as serialized data, normalized on read).

### 5. Consumer migration (full migration)

Route every runtime team comparison through `Team`/`TeamUtil`:

| File | Change |
|------|--------|
| `TeamScoreManager` | Drop `IsTeam1/IsTeam2`; score/buff logic keyed by `Team`. `RPC_AddPoints` normalizes its incoming string arg via `TeamUtil.Normalize`. |
| `HomeBase` | `IsPlayerOnCorrectTeam` compares `player.PlayerTeam` (enum) to `Normalize(baseTeam)`. |
| `Flag` | Owner checks and `GetTeamDisplayName` use `Team`/`TeamUtil.DisplayName`; `owningTeam` normalized on read. |
| `EnemyTeamComponent` | Keep serialized `string teamID`; add `Team Team => Normalize(teamID)`; logic via enum API. |
| `CoinData.GetValueForTeam` | Add `int GetValueForTeam(Team)` switching on the enum; the serialized `coinTeam` string is normalized only where coins are authored/compared. |
| `TerritoryZone` | Normalize `territoryTeam` + attacker/defender; "in own territory" via `Team` equality. |
| `PlayerInventory.PlayerTeam` | Change to `public Team PlayerTeam => teamComponent.Team;` (drop the string + `Team==1?"Team1":"Team2"` fallback). `CalculateTotalValue` and `HomeBase` consume the enum directly. |
| `Projectile` | Friendly-fire via `TeamUtil.AreEnemies(shooterTeam, targetTeam)`; carry shooter team as enum/int over the network (`[Networked] Team ShooterTeam` or int) instead of `NetworkString`. |
| `PlayerCombat` | Pass shooter `Team` into the projectile. |
| `PlayerStatsHandler` | `Respawn` reads `PlayerTeamData.Team` → `TeamUtil.ToNumber` for `GetSpawnPosition`; remove `ConvertTeamIdToNumber` and the string fallback branch. |
| `PlayerCameraRespawnHandler` | Same: read `PlayerTeamData.Team`; remove `ConvertTeamIdToNumber`/string fallback. |
| `PlayerController` | Teammate-collision compares `Team` (already uses `PlayerTeamData.Team`; update to enum + `Team.None` guard). |
| `NetworkedSpawnManager` | Calls `SetTeam(TeamUtil.FromNumber(team))`; internal 1/2 counts unchanged. |
| `DebugTeamDisplay`, `RespawnDebug`, `SpawnManagerDebugger` | Read `Team`/`DisplayName` for display. |

### Important constraint — serialized authoring fields stay `string`

Fields authored in assets/scenes/prefabs — `TeamData.teamID`, `CoinData.coinTeam`,
`Flag.owningTeam`, `HomeBase.baseTeam`, `EnemyTeamComponent.teamID`,
`TerritoryZone.territoryTeam` — **remain `string` in serialization** and are `Normalize()`d to
`Team` at read time. Converting them to enum fields would silently wipe inspector-set values.
"Full migration" therefore means migrating all *logic* to the enum, not re-authoring assets.

### Legacy code removal

Delete `Assets/Scripts/Player/RespawnManager.cs` and its `.meta` (the
`MultiplayerRespawnManager`): confirmed unreferenced by any scene/prefab and incompatible with
the Fusion spawn flow.

## Data flow

1. Menu/auto-balance picks a team number → `NetworkedSpawnManager.AssignTeam` (int 1/2).
2. On spawn, `OnPlayerSpawned` calls `PlayerTeamData.SetTeam(TeamUtil.FromNumber(team))` under
   state authority. The `[Networked] Team` replicates to all clients.
3. `OnChangedRender` (and the `Spawned()` initial call) fires `OnTeamChanged` → updates
   `PlayerTeamComponent` color and teammate collisions on every client. No per-tick polling.
4. Gameplay reads `PlayerTeamComponent.Team` / `PlayerTeamData.Team` (enum) and uses
   `TeamUtil`/enum-keyed `TeamManager` for all decisions (scoring, friendly fire, base checks,
   territorial advantage).

## Error handling / edge cases

- Team not yet assigned: value is `Team.None`; consumers treat `None` as "no team" (no friendly
  fire immunity, neutral modifiers, collision setup waits/aborts).
- Unknown/legacy strings from assets normalize to `None` with a single warning path in
  `TeamUtil` (not scattered).
- AI team (`Team3AI`) is never assignable to a player via `SetTeam`.

## Testing (manual/observational — no test assembly)

Verification is manual per project convention:

1. Compile clean (Assembly-CSharp).
2. Single-player (`GameNetworkManager.singlePlayerMode = true`, Host):
   - Player colored for chosen team; no per-tick team-poll logs.
   - Friendly fire correct (no self/teammate damage; enemies take damage).
   - Home-base deposit only at own base; flag pickup/capture team checks correct.
3. Multiplayer Play Mode (`singlePlayerMode = false`, 1 host + 1 virtual client):
   - Both players show correct colors on both peers.
   - Cross-team friendly-fire, base, and flag checks correct from both perspectives.

Commit referencing review item #3.

## Out of scope

- Re-authoring asset/scene string fields to enums.
- Items #4 and #8 (this is their prerequisite).
- Broader refactors unrelated to team identity.
