# Design: Individual deposit-earned buffs

**Date:** 2026-06-24
**Status:** Approved design, pending implementation plan
**Game:** Photon Fusion 2D, tick-based, server-authoritative

## Goal

Reward individual players for depositing coins at their home base with a
**scalable, data-driven, tiered buff system**. Each player picks a *priority
ordering* of buffs before the match; depositing coins (by value) progressively
unlocks buff tiers in a round-robin across that ordering. Passive buffs apply
automatically on unlock; the one active buff (Stealth) is triggered by an input
button with a duration and cooldown.

This is **additive** — team scoring (`TeamScoreManager`) and the existing deposit
flow are left intact.

## Networking constraints (non-negotiable, from the codebase)

- Server-authoritative + resimulation-safe. All gameplay-affecting state lives in
  `[Networked]` properties; durations/cooldowns use `TickTimer`. No local bools,
  no `Time.time` in simulation. Mirror `PlayerMovement.cs` (`[Networked]` flags,
  `TickTimer`, `Simulate(...)` driven by `PlayerController.FixedUpdateNetwork`).
- **No NetworkTransform.** Stealth is a visual/targeting change only.
- Input flows through `NetInput` / `PlayerButton` → `NetworkInputProvider.OnInput`
  → consumed in `PlayerController.FixedUpdateNetwork` → `*.Simulate(...)`. No direct
  `Keyboard.current` reads in simulation.
- Visuals in `Render()`, gameplay in `Simulate()`. Stealth's fade runs in `Render`,
  derived from networked state.
- The player prefab has **two Animators**; the visible body uses `Player.controller`.
  `GetComponentInChildren<Animator>()` / `<SpriteRenderer>()` may return the weapon
  one. Stealth visuals target **explicitly-serialized body renderer(s)**.

## Gameplay model

### Loadout = priority ordering of all three buffs

The loadout is a permutation of the three buffs (all are always present, only the
*order* is chosen). Default: `[Extra Jump, Stealth, Quicker Dash]`. The pre-match
menu is a **reorder** UI, not a subset picker.

### Round-robin tiered unlock

Nine unlock steps, driven by a flat, data-driven, **cumulative deposited-value**
threshold list:

```
thresholds = [5, 10, 15,   30, 45, 60,   120, 180, 240]
              ^tier1        ^tier2         ^tier3
UnlockedSteps = count(thresholds[i] <= TotalDepositedValue)   // 0..9
```

Unlock step `i` (0-based) unlocks `priority[i % 3]` to tier `(i / 3) + 1`.
So order `Jump > Stealth > Dash` unlocks in sequence:
Jump T1, Stealth T1, Dash T1, Jump T2, Stealth T2, Dash T2, Jump T3, Stealth T3, Dash T3.

Each buff's current tier is **derived** (never stored as independent networked state):

```
p = indexOf(buff) in LoadoutOrder            // 0,1,2
TierLevel(buff) = clamp(ceil((UnlockedSteps - p) / 3), 0, 3)
```

(Verified: UnlockedSteps=4, order Jump/Stealth/Dash → Jump=2, Stealth=1, Dash=1.)

`TotalDepositedValue` accumulates the **point value** of each deposit (what
`ServerDepositCoins()` returns / what feeds team score), not coin count.

### Tier tables

| Buff | Type | Tier 1 | Tier 2 | Tier 3 |
|---|---|---|---|---|
| **Extra Jump** | passive | +1 air jump | +2 air jumps | unlimited air jumps |
| **Stealth** | active | 1s duration | 3s duration | 10s duration, usable while carrying flag |
| **Quicker Dash** | passive (+on-dash) | +50% dash range (longer dash duration) | + dash cooldown ×0.5 | + deals melee damage in front while dashing |

> **Revised 2026-06-26.** The Quicker Dash tiers were re-tuned from the original
> `cooldown ×0.5 / cooldown ×0 / +damage`. Tiers are now **cumulative**: T1 extends
> dash range by 50% via `DashTimeMultiplier ×1.5` (same dash speed, 50% longer
> duration → 50% farther), T2 *adds* `DashCooldownMultiplier ×0.5`, T3 *adds* the
> front dash-strike. See [DashBuffDefinition.cs](../../../Assets/Scripts/Buffs/DashBuffDefinition.cs).

Fixed tuning (all data-driven, listed here as defaults):

- **Stealth cooldown:** flat **20s**, all tiers. Cooldown begins when the stealth
  effect *ends* (cycle = duration + 20s).
- **Stealth alpha:** owner sees `0.5`, opposing team sees `0.05`, teammate sees `0.5`.
  Derived locally from networked `IsStealthed` + local `PlayerTeamData` comparison.
- **Dash-damage amount (T3):** reuses the player's melee `attackDamage` through the
  existing `PlayerCombat.ResolveMeleeDamage` pipeline (team/`CombatConfig`-aware).
- **Stealth activate key:** `Q` (+ a gamepad button).

### Tricky-tier handling

- **Unlimited air jumps:** facade exposes `UnlimitedAirJumps` bool. `PlayerMovement`
  gates the air jump on `unlimited || RemainingAirJumps > 0` and only decrements
  `RemainingAirJumps` when not unlimited.
- **Dash range +50% (T1, revised 2026-06-26):** `DashTimeMultiplier ×1.5` →
  `EffectiveDashTime = dashTime × 1.5`. Dash speed is unchanged, so the dash lasts 50%
  longer and covers 50% more ground (range = dashSpeed × dashTime). Flows through the
  existing `StartDash` / `EffectiveDashTime` path unchanged.
- **Dash cooldown ×0.5 (T2, revised 2026-06-26):** `EffectiveDashCooldown = dashCooldown × 0.5`;
  cooldown halved. (Originally T1 ×0.5 / T2 ×0; re-tuned so T2 keeps a real cooldown.)
- **Dash deals damage (T3):** `PlayerCombat.Simulate` gains a server-only branch —
  while `movement.IsDashing()` and the facade reports `DashDealsDamage`, it runs the
  same `Physics2D.OverlapBoxAll(sideAttackPoint, sideAttackArea, attackableLayer)` +
  `ResolveMeleeDamage` path as a normal side swing. The target's existing 0.1s
  hit-cooldown (`PlayerStatsHandler.HitCooldownTimer`) throttles per-tick multi-hits.
  No new hitbox.
- **Stealth while carrying flag:** below T3, activation is blocked when
  `FlagCarrierMarker.IsCarryingFlag()` (mirrors the existing dash flag-block). At T3
  the block lifts.

## Architecture

### Pure-derivation over imperative Apply/Remove

Every passive effect is a pure function of networked `TotalDepositedValue` +
`LoadoutOrder`. Effective stats are therefore **derived on query** rather than
stored as separately-networked modifier fields that get mutated via Apply/Remove.
This is strictly more resimulation-safe (nothing to replay/undo on rollback) and a
deliberate adaptation of the brief's `IBuffEffect.Apply/Remove/Tick`. The isolated
per-buff abstraction and registry are retained; the per-buff hooks contribute to
derived state instead of mutating it imperatively.

### Components

**Data (ScriptableObjects):**

- `BuffDefinition` (abstract SO): `id`, `displayName`, `Sprite icon`, `BuffKind`
  (`Passive`/`Active`); owns a 3-entry tier table in its subclass; two pure hooks:
  - `void ContributeStats(ref EffectiveStats stats, int tierLevel)` — passive.
  - `ActiveBuffParams GetActiveParams(int tierLevel)` — active (duration, cooldown,
    `usableWhileCarryingFlag`).
- Subclasses: `JumpBuffDefinition`, `StealthBuffDefinition`, `DashBuffDefinition`,
  each `[CreateAssetMenu]` with its own tier fields.
- `BuffLoadoutConfig` (single SO): `BuffDefinition[] allBuffs` (registry; array index
  = the byte serialized over the network), `int[] thresholds` (9 entries),
  `int maxSlots = 3`, default loadout order.

**Effective-stats facade:**

- `PlayerStatModifiers` (component): reads `PlayerBuffs` tiers + the shared
  `PlayerStats` SO; exposes `EffectiveMaxAirJumps`, `UnlimitedAirJumps`,
  `EffectiveDashCooldown`, `EffectiveDashTime`, `DashDealsDamage`. **Never mutates
  the SO.** `PlayerMovement` and `PlayerCombat` read effective values through it,
  falling back to raw `stats` if the component is absent.

**Core networked component:**

- `PlayerBuffs : NetworkBehaviour`. Networked state:
  - `[Networked, Capacity(3)] NetworkArray<byte> LoadoutOrder` — buff indices into
    `allBuffs`, in priority order.
  - `[Networked] int TotalDepositedValue`.
  - `[Networked] TickTimer StealthDurationTimer`, `[Networked] TickTimer
    StealthCooldownTimer`, `[Networked] NetworkBool IsStealthed`.
  - Tiers and effective stats are **computed**, not stored.
  - `ServerInitLoadout(byte[] order)` — host sets the loadout at spawn.
  - `ServerAddDepositedValue(int points)` — server-only, called from the deposit RPC;
    increments `TotalDepositedValue`.
  - `Simulate(NetInput input, NetworkButtons pressed)` — called from
    `PlayerController.FixedUpdateNetwork` after movement/combat; handles stealth
    activation (button + tier/cooldown/flag gating → `StealthDurationTimer`,
    `IsStealthed = true`) and expiry (timer expired → `IsStealthed = false`, start
    `StealthCooldownTimer`). Mirrors the dash lifetime pattern.
  - Public read accessors: `IsStealthed`, `TierLevelOf(buff)`, etc.

**Pure logic (Fusion-free, unit-testable):**

- `BuffUnlock` static helper: `UnlockedSteps(thresholds, total)` and
  `TierLevel(unlockedSteps, priorityPosition)`. Isolated so it is testable without
  the network layer.

**Stealth visual + AI:**

- `PlayerStealthVisual.Render()` — sets alpha on **explicitly-serialized body
  `SpriteRenderer[]`**, choosing owner/teammate/enemy alpha from networked
  `IsStealthed` + local team comparison.
- `EnemyAI.CheckForPlayers` / `CheckIfPlayerEscaped` — skip/drop players whose
  `PlayerBuffs.IsStealthed` is true.

**Input:**

- Add `Stealth = 4` to the `PlayerButton` enum; bind `Q` (+ gamepad) in
  `NetworkInputProvider.OnInput`; consume via `buffs.Simulate(input, pressed)` in
  `PlayerController.FixedUpdateNetwork`.

**Lobby loadout picker:**

- Extend `TeamSelectionUI` with a reorder UI (3 buffs, populated from `allBuffs`).
- New `LoadoutKey` reliable-data channel mirroring the team-choice one; client sends
  the ordered buff-index bytes, host records its own directly. Stored in a static
  `LobbyLoadoutChoices` map (parallel to `LobbyTeamChoices`).
- `NetworkedSpawnManager` (host) already reads `LobbyTeamChoices` at spawn; it
  additionally reads `LobbyLoadoutChoices` and calls `PlayerBuffs.ServerInitLoadout`.
- Loadout is **optional** for the start gate (`CanStartMatch` stays keyed on team
  only); a missing loadout defaults to `[Jump, Stealth, Dash]`.

## Data flow (deposit → unlock → effect)

```
Player enters own base → NetworkedHomeBase.RPC_RequestDeposit (server)
  → inventory.ServerDepositCoins() returns points (unchanged)
  → TeamScoreManager.RPC_AddPoints(...) (unchanged)
  → buffs.ServerAddDepositedValue(points)            // NEW, additive
       TotalDepositedValue += points
  → networked state replicates → every client/proxy now derives:
       UnlockedSteps, per-buff TierLevel, EffectiveStats
  → PlayerMovement reads effective air-jumps / dash cooldown
  → PlayerCombat applies dash damage when DashDealsDamage
  → Stealth becomes activatable when its tier >= 1
```

## Adding a 4th buff

1. New `BuffDefinition` subclass with its 3-entry tier table + the two pure hooks.
2. Create the asset; add it to `BuffLoadoutConfig.allBuffs`.
3. It appears in the reorder menu, serializes by array index, and flows through the
   same round-robin unlock math.

Passive buffs just contribute to `EffectiveStats` (a genuinely new stat dimension is
a one-field add to `PlayerStatModifiers`). Active buffs reuse the activation path
(currently one button; multiple actives would map to additional buttons or a
slot-indexed "use active"). **No edits to the deposit hook, unlock math, or the
`PlayerBuffs` loop.**

## Testing / verification

- `BuffUnlock` math and loadout byte (de)serialization are plain C# → unit-testable
  without Fusion (edit-mode tests).
- Networked behavior verified by playing in-editor as host via the project's
  run/rebuild flow. Success claims stay tied to observed behavior.

## Assumptions / decisions on record

- Threshold unit: **deposited value (points)**, not coin count.
- Loadout selection: **full reorder picker** in the lobby; default `[Jump, Stealth,
  Dash]`; loadout optional at the start gate.
- Thresholds live **per unlock-step (slot/tier position)**, not per buff.
- Stealth cooldown: **flat 20s**, begins when the effect ends.
- Dash-T3 damage: reuses melee `attackDamage` via `ResolveMeleeDamage`.
- Stealth reduces visibility to **AI enemies and PvP opponents** (owner still sees
  self at 0.5 alpha).
```