# Unified Damage Pipeline & CTF/Economy Relationship (review item #4)

**Date:** 2026-06-22
**Status:** Approved (pending spec review)
**Depends on:** item #3 (team single-source-of-truth — landed)

## Product decision

**The coin/territory economy is a meta-layer on top of CTF, not a separate mode.**

- **CTF is the win condition.** Capture both flags → win (unchanged, `CTFGameManager`).
- **The coin loop is a secondary objective** whose milestones buff a team's combat by
  *lifting the territorial nerf*. Kill enemies → coins drop → deposit at `HomeBase` →
  `TeamScoreManager` team score → unlock DamageBuff (50 pts) / DefenseBuff (100 pts).
- **Design intent (from product owner):** the territorial modifier disincentivizes early
  aggression (pushing into enemy territory hits weakly and hurts more). Coin buffs *remove*
  those disincentives as they unlock, so the economy rewards farming early and enables
  pressing the attack later.

This is recorded at the top of the changed files (`CombatConfig`, `TeamScoreManager`).

## Problem being fixed

Four parallel damage-modifier systems existed and **none** governed player melee:

- `TeamManager` — distance-based territorial modifier (0.5x–1.5x). Used by `Enemy.AttackPlayer`
  but **not** by players, so territory applied to AI only.
- `TeamScoreManager` — coin-milestone buff getters (`GetTerritoryDamageMultiplier` etc.) that
  **nothing called**, encoding a competing 0.5/1.0 model.
- `TerritoryZone` — a third, unintegrated "EXAMPLE" (no scene/prefab references).
- `CombatConfig.CalculateFinalDamage` — present, assigned to `GameSettingsManager`, **never called**.

`PlayerCombat.Attack` dealt a hardcoded `damageAmount = 25` and consulted none of them. Coin
buffs had zero gameplay effect.

## The territorial-nerf-lifting model

The distance-based modifier maps a combatant's position to a territorial advantage
`a ∈ [-1, +1]` (`+1` own base, `0` midfield, `-1` enemy base), then to a multiplier
`lerp(0.5, 1.5, (a+1)/2)`. The portion **below 1.0x is the nerf**. Buffs lift it:

| Buff | Side | No buff | Buff unlocked |
|------|------|---------|---------------|
| DamageBuff (50 pts) | outgoing (`dealtModifier`) | `0.5x … 1.5x` | floor lifted to 1.0 → `1.0x … 1.5x` |
| DefenseBuff (100 pts) | incoming (`receivedModifier`) | `0.5x … 1.5x` damage taken | ceiling capped to 1.0 → `0.5x … 1.0x` |

- `dealtModifier` buff-lift: `if attacker team has DamageBuff: dealt = Max(dealt, 1.0)`
  (removes the aggression nerf, keeps the home-base bonus).
- `receivedModifier` buff-cap: `if defender team has DefenseBuff: received = Min(received, 1.0)`
  (removes enemy-territory vulnerability, keeps home-base protection).

`receivedModifier` is computed as `GetDamageReceivedModifier(defenderTeam, defenderAdvantage)`
= `GetDamageDealtModifier(defenderTeam, -defenderAdvantage)`: `<1` when defending at home
(protected), `>1` when caught in enemy territory (vulnerable).

The AI team (`Team3AI`) keeps its existing behavior: `TeamManager.aiUsesTerritory == false`
forces its dealt/received modifier to `1.0`, so enemies are unaffected by territory and buffs.

## Single damage entry point

```
finalDamage = base
            × globalDamageMultiplier              (CombatConfig)
            × dealtModifier(attacker)              (TeamManager, floor-lifted by attacker DamageBuff)
            × receivedModifier(defender)           (TeamManager, capped by defender DefenseBuff)
            × (isCritical ? criticalMultiplier : 1)
```

Both sides apply (confirmed): a home-base defender vs. an unbuffed attacker can compound to
~2.25x — an intentional strong home-field advantage.

### `CombatConfig.ResolveDamage(...)` — the one public entry point

```csharp
// The single home for all damage modification. Gathers territorial + economy modifiers
// and composes them with the pure-math CalculateFinalDamage.
public int ResolveDamage(float baseDamage,
                         Team attackerTeam, Vector2 attackerPos,
                         Team defenderTeam, Vector2 defenderPos)
```

- Reads `TeamManager.Instance` for distance modifiers, `TeamScoreManager.Instance` for buff
  state. All singletons null-guarded → neutral `1.0` fallback (no regression if absent).
- Rolls crit via existing `RollCritical()`.
- Delegates the final arithmetic to the existing **pure-math** method, generalized to take both
  modifiers:
  `CalculateFinalDamage(float baseDamage, float dealtModifier, float receivedModifier, bool isCritical)`.
- Returns `Mathf.RoundToInt(...)`, clamped to `>= 0`.

### Call sites

- **`PlayerCombat.Attack`**: replace `enemy.TakeDamage(damageAmount, …)` with
  `int dmg = config.ResolveDamage(damageAmount, myTeam, transform.position, enemyTeam, enemyPos)`.
  `damageAmount` becomes the *base* (still serialized). Knockback unchanged.
- **`Enemy.AttackPlayer`**: replace `stats.attackDamage * teamComponent.GetDamageDealtModifier()`
  with `config.ResolveDamage(stats.attackDamage, myTeam, transform.position, playerTeam, playerPos)`.
- Both fetch the config via `GameSettingsManager.Instance?.GetCombatConfig()`; if null, fall back
  to `Mathf.RoundToInt(baseDamage)` so combat still functions.
- Computation stays on **StateAuthority** only (both call sites already gate on it).
- **Projectile** is intentionally left for item #5; it will call the same `ResolveDamage`.

## Cleanup (dead modifier paths)

- **Delete** `Assets/Scripts/Coin Scripts/TerritoryZone.cs` + `.meta`. Verified: no scene, prefab,
  or asset references.
- **`TeamScoreManager`**: replace the uncalled `GetTerritoryDamageMultiplier(string)` /
  `GetTerritoryDefenseMultiplier(string)` with `bool HasDamageBuff(Team)` / `bool HasDefenseBuff(Team)`.
  Networked buff bools (`Team1DamageBuff` …) and the `UIManager` indicators are untouched.
- **`TeamManager`**: add `float GetTerritorialAdvantage(Team team, Vector2 position)` (hoists the
  distance formula currently duplicated in `PlayerTeamComponent.CalculateTerritorialAdvantage`).
  `PlayerTeamComponent` delegates to it (single formula). Existing `GetDamageDealtModifier` /
  `GetDamageReceivedModifier` kept.
- **`CombatConfig`**: remove the now-dead duplicate `minTerritorialDamage` / `maxTerritorialDamage`
  fields (TeamManager owns the 0.5–1.5 range). `bonusDamageColor` and other visual fields stay.

## What is NOT changing

- CTF flow, flag logic, win condition.
- The coin pickup/deposit/scoring loop and its networking.
- `PlayerTeamComponent` / `EnemyTeamComponent` modifier methods (kept; `DebugTeamDisplay` reads them).
- `globalDamageMultiplier`, crit, knockback tuning.

## Verification (manual / observational)

No test assembly; game code is in `Assembly-CSharp`. Verify by:

1. **Compile clean.**
2. **Single-player** (`GameNetworkManager.singlePlayerMode = true`, Host):
   - Melee an enemy near your own base → higher damage; near enemy base → ~0.5x (nerfed).
   - Bank ≥50 coins → DamageBuff icon lights → melee near enemy base now ~1.0x (nerf gone).
   - Enemy attacks still apply territory to the player; ≥100 coins → DefenseBuff caps incoming.
3. **MPPM** (`singlePlayerMode = false`, 1 host + 1 virtual client): damage numbers/health match
   across both windows; buffs unlocked on the host are reflected in client combat.
4. Commit referencing item #4.
