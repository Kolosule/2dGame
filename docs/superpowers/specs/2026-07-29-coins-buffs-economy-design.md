# Coins → Buffs Economy — Design

**Date:** 2026-07-29
**Status:** Approved (design), no implementation plan authored
**Game:** Unity 6.3 Photon Fusion 2 2D PvPvE arena, Host/Client + dedicated server, ~20 players

## Product boundary (fixed, not up for relitigation)

**CTF flag capture is the only win condition.** Coins are not a win path and not a
tiebreak. Coins are a combat/utility economy: depositing them unlocks **individual**
buffs (per player) and **team-wide** buffs (whole team). This spec designs that
economy.

## Problem

The deposit → buff loop is fully wired and works. The *design* behind it is thin
in five specific places:

1. **Team-wide buffs are hollow.** Two on/off booleans that only floor/cap a
   territorial modifier at neutral — they can never make a team *stronger* than
   baseline, and they are literal no-ops when territorial combat is disabled.
2. **Two inconsistent progression models.** Individuals get thresholds → steps →
   tiers → loadout priority. Teams get two magic-number booleans.
3. **No economic tension by construction.** Every deposited point pumps both
   layers identically, so there is nothing to weigh.
4. **Individual catalog scope is undeclared.** Three buffs exist; nobody decided
   whether that is the catalog or a starting subset.
5. **Magic numbers everywhere.** Team 50/100 and individual `{5…240}` have no
   stated derivation, no target outcome, and no documented match length.

And one thing worse than the brief assumed: **coins currently win matches.**

## Audit — what is actually wired today

### Deposit feeds both layers, same value, no split

[`HomeBase.cs:196-216`](../../../Assets/Scripts/Coin%20Scripts/HomeBase.cs) —
`ServerDeposit` takes `points` from
[`PlayerInventory.cs:122`](../../../Assets/Scripts/Coin%20Scripts/PlayerInventory.cs)
`ServerDepositCoins()` and passes the **identical** value to both layers:

```
scoreManager.RPC_AddPoints(baseTeam, points);   // team layer
buffs.ServerAddDepositedValue(points);          // individual layer
```

Coin point value is per-team via `CoinData.GetValueForTeam`, default `1` for both
human teams ([`CoinData.cs:18-21`](../../../Assets/Scripts/Coin%20Scripts/CoinData.cs)).

### Individual buffs — derived tiers, loadout priority

[`PlayerBuffs.cs`](../../../Assets/Scripts/Buffs/PlayerBuffs.cs). Networked state is
only `TotalDepositedValue`, `LoadoutOrder`, and the Stealth timers; every tier is
**derived on query** (`TierOf`, line 90) through
[`BuffUnlock`](../../../Assets/Scripts/Buffs/Core/BuffUnlock.cs) — nothing to replay
on resimulation. `BuildEffectiveStats` (line 100) sums each equipped buff's
`ContributeStats` into
[`EffectiveStats`](../../../Assets/Scripts/Buffs/Core/EffectiveStats.cs), surfaced to
`PlayerMovement` / `PlayerCombat` through
[`PlayerStatModifiers.cs`](../../../Assets/Scripts/Buffs/PlayerStatModifiers.cs),
which never mutates the shared `PlayerStats` SO.

Catalog is exactly three ([`BuffId.cs`](../../../Assets/Scripts/Buffs/Core/BuffId.cs)):
`ExtraJump`, `Stealth`, `QuickerDash`. Thresholds
`{5,10,15,30,45,60,120,180,240}` at
[`BuffLoadoutConfig.cs:15`](../../../Assets/Scripts/Buffs/BuffLoadoutConfig.cs).

**This layer's architecture is good and is kept.** The problems below are all about
what sits on top of it.

### Team buffs — two monotonic booleans, parasitic on territory

[`TeamScoreManager.cs:120-157`](../../../Assets/Scripts/Coin%20Scripts/TeamScoreManager.cs)
`CheckMilestones` sets `Team{1,2}{Damage,Defense}Buff` at 50 and 100 team points.
Sole consumer is
[`CombatConfig.cs:116-123`](../../../Assets/Scripts/ScriptableObjects/CombatConfig.cs):
`Mathf.Max(dealt, 1f)` and `Mathf.Min(received, 1f)`.

### Six findings that shape this design

1. **Coins are a win path today.**
   [`MatchManager.cs:161-168`](../../../Assets/Scripts/Match/MatchManager.cs)
   `ResolveByTimer()` calls
   [`MatchResolver.ResolveTimerWinner`](../../../Assets/Scripts/Match/Core/MatchResolver.cs)
   → higher coin score wins, equal is a draw. With `matchTimeLimit > 0`, coin
   farming decides matches. Contradicts the product boundary.

2. **Team buffs are zero, not merely capped, when territory is off.** The lift is
   applied in `ResolveDamage`, but `dealt * received` is only multiplied in at all
   inside `if (territorialAdvantageEnabled)`
   ([`CombatConfig.cs:80-83`](../../../Assets/Scripts/ScriptableObjects/CombatConfig.cs)).
   With the flag false both team buffs do nothing while still firing unlock events
   and lighting the HUD badge.

3. **The territorial swing is 9×, invisible, and untuned.** Modifiers compound:
   `GetDamageReceivedModifier(d, adv)` is `GetDamageDealtModifier(d, -adv)`
   ([`TeamManager.cs:65-68`](../../../Assets/Scripts/Teams/TeamManager.cs)), so at
   Team1's base, Team1 attacking Team2 resolves to `1.5 × 1.5 = 2.25×` while Team2
   attacking Team1 resolves to `0.5 × 0.5 = 0.25×`. Nothing on screen says so.

4. **Team thresholds are effectively free.** 50/100 are compared against *team*
   score — the sum of ~10 players' deposits. At typical per-player banking a
   10-player team crosses both milestones within the first minute or two. They are
   not tuned low; they are not tuned at all.

5. **`MaxTier` is a fragile division.**
   [`BuffLoadoutConfig.cs:24`](../../../Assets/Scripts/Buffs/BuffLoadoutConfig.cs)
   `thresholds.Length / BuffCount`. Adding a 4th buff without 3 more thresholds
   silently drops every buff from tier 3 to tier 2.

6. **The unlock events fire on the wrong peer.** `onDamageBuffUnlocked` /
   `onDefenseBuffUnlocked` are invoked inside `CheckMilestones`, which returns
   early unless `HasStateAuthority`. On a dedicated server they fire headless where
   no client can observe them, and they have zero subscribers. The only
   client-visible path is `OnChangedRender` → `TeamBuffsChanged` →
   [`TeamScoreDisplay.cs:62`](../../../Assets/Scripts/Hud/TeamScoreDisplay.cs), one
   undifferentiated badge that cannot distinguish damage from defense.

7. **The respawn-time config is a dead parallel path.**
   `GameSettingsManager.GetRespawnTime(TeamData)` and `respawnTimeMultiplier`
   ([`Game Settings Manager.cs:96-99`](../../../Assets/Scripts/ScriptableObjects/Game%20Settings%20Manager.cs))
   have **zero callers**, and `TeamData.respawnDelay` is read only by that dead
   method. The live respawn path is
   [`PlayerStatsHandler.cs:204`](../../../Assets/Scripts/Player/PlayerStatsHandler.cs),
   which arms `RespawnTimer` from its own local `[SerializeField] respawnDelay = 3f`.
   This matters because it is where the Reinforcement team buff must hook.

Minor, noted not designed: `RPC_AddPoints` is `RpcSources.All`, so any client can
inflate team score directly.

## Decisions (from brainstorming)

| # | Decision |
|---|---|
| 1 | **Dual-benefit deposits.** Every point pumps both layers. No allocation UI, no split, no second currency. Tension comes from the carry loop instead. |
| 2 | **Territorial combat is kept and tuned**, simplified to **one debuff**, swing **3×**, removed by team buff in **50% increments**. |
| 3 | **Territory and team buffs share one HUD surface**; individual buffs keep their own. |
| 4 | **8–10 minute matches.** Tier 2 reliably reached on top priorities; tier 3 rare. |
| 5 | **Individual buffs are movement/utility only.** No attack, health, or base move-speed buffs. |
| 6 | **Stealth remains the only active ability.** Everything else is passive. |
| 7 | **Individual catalog is four buffs** — the existing three plus Flag Runner. |
| 8 | **Team catalog is two buffs at two tiers each.** |
| 9 | **Enemy coin drops become deterministic** so pacing is computable. |
| 10 | **Timer expiry enters Sudden Death** with all buffs unlocked for everyone. No draw in normal play. |
| 11 | **`GameSettingsManager.scoreLimit` is deleted**, not repurposed. |

## Win-condition boundary

Capture is the only win. Two removals and one addition.

**Removed:** the coin tiebreak. `MatchManager.ResolveByTimer()` and
`MatchResolver.ResolveTimerWinner` both go, along with
`Assets/Tests/EditMode/Match/MatchResolverTests.cs`'s
`ResolveTimerWinner_HigherScoreWins_EqualIsDraw` case. `MatchResolver.WinnerLabel`
stays — it still formats the results banner.

**Removed:** `GameSettingsManager.scoreLimit`
([`Game Settings Manager.cs:49`](../../../Assets/Scripts/ScriptableObjects/Game%20Settings%20Manager.cs)),
currently referenced in zero files. A coin score limit *is* a coin win path by
definition, and there is nothing to repurpose it into — match length is
`matchTimeLimit`, and buff pacing lives in the threshold configs. An unwired public
field named "score limit to win", in a game where score cannot win, is a footgun.

**Added: Sudden Death.** A sixth `MatchPhase` between `Live` and `PostMatch`.

- `Live` timer expiry → `SuddenDeath` (instead of resolving a winner).
- In `SuddenDeath`, **every player has every individual buff at max tier, and both
  teams have both team buffs at max tier.**
- The match ends on the next capture, which sets `Winner` and enters `PostMatch`
  exactly as a normal capture does.
- `SuddenDeath` has **no timer**. The escalation is the forcing function: maxed
  Flag Runner, Stealth T3 and unlimited air jumps make a flag run dramatically
  easier than in normal play, and the territorial debuff is fully lifted for both
  sides, so nobody is defending from behind a damage advantage.

Sudden Death costs no new per-player state. Because tiers are *derived*, the
unlock is a read-time override: tier resolution returns `MaxTier` when
`MatchManager.Phase == SuddenDeath`. Nothing to replay, nothing to reset.

Coins still deposit during Sudden Death but no longer affect anything, since every
tier is already maxed. That is the intended shape: Sudden Death is purely about the
flag.

**Server safety valve.** A `suddenDeathHardCap` setting, **default 0 = off**. When
an operator sets it and it elapses, the match resolves as a draw so a headless
dedicated server cannot wedge on an unwinnable match forever. This is an operations
lever, not a game rule — draws are unreachable in default play.

## Unified progression model

Both layers speak one vocabulary:

> **cumulative deposited value → ordered unlock steps → per-buff tiers**

`BuffUnlock.UnlockedSteps(thresholds, total)` counts thresholds at or below the
total; step `i` raises the buff at priority position `i % buffCount` to tier
`i / buffCount + 1`. The team layer stops being booleans and derives tiers through
the same pure helper.

### The one justified asymmetry

Individuals **choose** their priority order in the lobby. Teams use a **fixed
authored** order. There is no team-level UI, and picking one collectively mid-match
is a coordination problem with no good answer. So team order is authored, not
chosen. Everything else about the two layers is identical.

### Team thresholds are per-player-average

Team score is the sum of a whole roster's deposits, so a raw threshold is
meaningless across roster sizes (finding #4). Team thresholds are therefore
authored as **per-player-average deposited value** and compared against
`teamScore / rosterSize`.

To keep derivation pure, **`TeamRosterSize` is captured once as networked state on
entering `Live`** and used as the divisor for the rest of the match. Roster churn
then cannot retroactively unlock or revoke a tier, so no monotonic latch and no
stored tier state is needed — preserving the property that made the individual
layer resimulation-safe.

### `MaxTier` becomes explicit

`BuffLoadoutConfig.MaxTier` stops being `thresholds.Length / BuffCount` and becomes
an authored field, validated against `thresholds.Length == maxTier × buffCount`
(finding #5). Catalog growth then fails loudly instead of silently demoting
everyone.

## Individual buff catalog — four buffs, movement/utility, one active

| Buff | Kind | Tier 1 | Tier 2 | Tier 3 |
|---|---|---|---|---|
| **Extra Jump** | passive | +1 air jump | +2 air jumps | unlimited air jumps |
| **Quicker Dash** | passive | +50% dash range | + dash cooldown ×0.5 | + dash deals damage |
| **Stealth** | **active** | 1 s | 3 s | 10 s + usable while carrying flag |
| **Flag Runner** *(new)* | passive | +10% move speed while carrying | +20% while carrying | +20% and **dash permitted while carrying** |

The first three are unchanged from
[the 2026-06-24 spec](2026-06-24-deposit-earned-buffs-design.md) and its 2026-06-26
dash retune.

**Why Flag Runner, and why it is cheap.** There is no flag-carry speed stat today —
the carry penalty is that carrying **blocks dash outright**
([`PlayerMovement.cs:126-135`](../../../Assets/Scripts/Player/PlayerMovement.cs)) and
blocks Stealth below T3. So the buff attaches to hooks that already exist: one new
`EffectiveStats.CarrySpeedMultiplier` field, and lifting that existing gate at T3.
Its T3 deliberately mirrors Stealth's T3, making **"top tier lifts the flag
restriction"** consistent design language rather than a one-off.

**Deliberately excluded** (decision 5): attack damage, max health, and base move
speed. A player who out-farms you should become harder to catch and better at
running the objective — not someone who two-shots you. This caps the snowball at
mobility, which can still be outplayed, and keeps PvP damage tuned in one place.

**Stealth stays the only active** (decision 6). One button, one cooldown, and the
existing single-button path in `PlayerBuffs.Simulate` needs no change. The loadout
picker is about tuning your movement, not managing a hotbar.

### Loadout UX

Unchanged in kind from the 2026-06-24 spec: a **reorder** picker in the lobby (not a
subset picker), all buffs always equipped, only the order chosen. Now four entries.
Default order `[ExtraJump, Stealth, QuickerDash, FlagRunner]`. Loadout stays
optional at the start gate; a missing choice takes the default.

The cost of ordering is real and intended: priority #4 unlocks nothing until step 4,
so putting Flag Runner last means no carry speed until you have banked a fair
amount.

## Territorial combat — one debuff, 3× swing

The lerped, compounding, two-sided model (finding #3) is replaced by a **single
debuff on damage dealt**, applied only in the enemy third of the map:

| Zone | Territorial advantage | Damage dealt |
|---|---|---|
| Own two thirds | ≥ −0.33 | ×1.00 |
| **Enemy third** | < −0.33 | ×0.33 |

Full swing **3×**. `GetTerritorialAdvantage` is reused unchanged as the input — only
its output is quantized, so this is a small change to a formula that already exists.
`GetDamageReceivedModifier` and the received-side modifier are **deleted**: there is
one debuff, on one side, in one direction.

The boundary sits at the enemy *third*, not the midline, so midfield fighting is
clean and neutral and only committing deep carries the tax. Two states, not a
gradient, which is what makes it displayable as an icon.

**This is what ties the economy to the objective.** The enemy flag sits in the enemy
third, so the debuff is precisely a tax on flag-grabbing — and the team buff below
progressively lifts it. The coin economy literally funds your ability to attack the
win condition.

## Team buff catalog — two buffs, two tiers

Fixed authored priority order: `[Vanguard, Reinforcement]`. Four unlock steps.

| Team buff | Tier 1 | Tier 2 |
|---|---|---|
| **Vanguard** | 50% of the territorial debuff removed → ×0.67 | 100% removed → ×1.00 |
| **Reinforcement** | −15% team respawn delay | −30% team respawn delay |

Vanguard resolves as `dealt = 1 − 0.67 × (1 − 0.5 × tier)`, giving even thirds
`0.33 → 0.67 → 1.00` across tiers 0/1/2.

**Reinforcement is the standalone pillar.** It is entirely territory-independent and
matters enormously in CTF, where respawn timing decides whether a defence re-forms
before a carrier gets home. It exists so the team layer is not 100% parasitic on
territory (finding #2) — if territory is ever dialled down or disabled, half the
team catalog still does real work.

**Where it hooks, given finding #7.** Not `GameSettingsManager.GetRespawnTime` —
that method is dead. Reinforcement scales the delay armed at
[`PlayerStatsHandler.cs:204`](../../../Assets/Scripts/Player/PlayerStatsHandler.cs),
resolved from the dying player's team tier at the moment of death.

Since this design has to touch the respawn path anyway, it also **collapses the dead
parallel path**: `PlayerStatsHandler` becomes the single owner of respawn timing, and
`GameSettingsManager.GetRespawnTime` / `respawnTimeMultiplier` /
`TeamData.respawnDelay` are deleted. Leaving a second, unused, differently-configured
respawn formula alive next to a buff that modifies respawn timing is how the next
reader gets it wrong. This is a targeted cleanup of code the work already touches,
not an invitation to refactor respawn behaviour.

Both old booleans and their thresholds are deleted. `Team{1,2}DamageBuff` /
`Team{1,2}DefenseBuff` and `damageBuffThreshold` / `defenseBuffThreshold` are
replaced by derived tiers over the team threshold list.

## The deposit economy — dual benefit, and where the tension lives

**Every deposited point pumps both layers, unchanged** (decision 1). No allocation
UI, no split, no second currency. This is the core economy decision and it is a
choice for simplicity: nothing new to learn, nothing new to network, and no default
to get wrong for players who never engage with an allocation screen.

The consequence is that the tension cannot come from *where* the coins go. It comes
from **the carry loop, which is already built**:

| Knob | Current value | Effect |
|---|---|---|
| Carrier aura tiers | 5 / 15 / 30 carried value ([`CoinCarrierAura.cs:19`](../../../Assets/Scripts/Coin%20Scripts/CoinCarrierAura.cs)) | The more you carry, the brighter and faster-pulsing you glow — a big bank is *advertised* to the enemy |
| Drop on death | All coins re-spawned, fully re-lootable by anyone ([`PlayerInventory.cs:182`](../../../Assets/Scripts/Coin%20Scripts/PlayerInventory.cs)) | Dying mid-run donates your progress to whoever kills you |
| Coin lifetime | 30 s ([`CoinPickup.cs:56`](../../../Assets/Scripts/Coin%20Scripts/CoinPickup.cs)) | Loot does not wait; a cautious player loses it to decay |
| Stealth interaction | Aura hidden while stealthed ([`CoinCarrierAura.cs:66-68`](../../../Assets/Scripts/Coin%20Scripts/CoinCarrierAura.cs)) | Stealth is the counterplay to being advertised — a real reason to prioritise it |

So the decision every run is **bank small and safe, or bank big and lit up**, and
both layers reward that choice identically. These four knobs are the tuning surface
for economic tension; the allocation UI is not built.

## Balance and pacing

### Deterministic coin supply

Pacing cannot be tuned against a random drop. `Enemy.SpawnCoins` currently rolls
`Random.Range(coinsToDropMin, coinsToDropMax + 1)` with 1–3
([`Enemy.cs:332`](../../../Assets/Scripts/Enemy/Base/Enemy.cs)). This is replaced by
(decision 9):

- A **single authored `coinsToDrop` int per enemy archetype prefab** — stronger
  archetypes drop more. No randomness.
- An integer **`coinDropBonus` on `RingTier`**
  ([`DifficultyRingConfig.cs`](../../../Assets/Scripts/Enemy/AI/DifficultyRingConfig.cs)
  currently carries `healthMult` / `damageMult` / `speedMult` only), so enemies nearer
  the hazardous centre pay out more. Integer, so the total stays exactly computable.

Starting values, to be tuned in play: base `2` for the weakest archetype rising to
`4` for the strongest, with ring bonus `+0` outer / `+1` middle / `+2` inner. Coin
point value stays `1`.

Total deposited value for a player is then exactly
`kills × (coinsToDrop + ringBonus)`, minus what is lost to death and 30 s decay.

### Target outcomes at 8–10 minutes

| Player | Banked | Should reach |
|---|---|---|
| Typical | ~40–70 | T1 on all four, T2 on top two |
| Strong | ~100–140 | T2 on all four, T3 on #1 |
| Exceptional | ~200+ | T3 on #1–#3 |
| Runaway | 260+ | All T3 |

### Individual curve — 12 steps (4 buffs × 3 tiers)

```
tier 1 block:    5,  10,  16,  24
tier 2 block:   34,  46,  62,  80
tier 3 block:  110, 150, 200, 260
```

| Banked | Steps | Result under default order |
|---|---|---|
| 55 | 6 | #1,#2 at T2; #3,#4 at T1 |
| 120 | 9 | #1 at T3; #2,#3,#4 at T2 |
| 220 | 11 | #1,#2,#3 at T3; #4 at T2 |
| 260 | 12 | all T3 |

Tier 3 is deliberately rare in normal play (decision 4) because those tiers are
strong — unlimited air jumps, 10 s stealth, dash-damage, dash-while-carrying. Sudden
Death is the one place everyone gets to feel them.

### Team curve — 4 steps, per-player-average thresholds

```
per-player-average deposited value:  12, 24, 45, 70
compared against:  teamScore / TeamRosterSize
```

| Team average | Steps | Result |
|---|---|---|
| 30 | 2 | Vanguard T1, Reinforcement T1 |
| 55 (typical) | 3 | **Vanguard T2** (debuff fully lifted), Reinforcement T1 |
| 120 (dominant) | 4 | Both maxed |

A normal team fully lifts the territorial debuff around the middle of the match,
which is a deliberate arc: pushing into the enemy third to grab the flag gets
progressively cheaper as the team's economy matures.

### Caps and diminishing returns

- Tiers are hard-capped at `MaxTier`; there is no scaling past the top threshold, so
  a runaway player's surplus coins convert to nothing personal. That is the
  intended ceiling on the snowball.
- No diminishing-returns curve is applied *within* a tier — tiers are discrete steps
  by design, which is what makes them legible and testable.
- The catalog's exclusion of combat stats (decision 5) is the primary snowball cap;
  it is a design constraint, not a runtime clamp.

## Feedback surfaces

Two surfaces, event-driven under `Assets/Scripts/Hud/`, no polling (decision 3).

### Merged Team Power strip — territory and team buffs together

Territory and team buffs are **one subject** — how strong is my team's position
right now — so they share one readout. It shows:

- Team scores, with a **next-milestone tick** on the bar.
- A **zone indicator** whose displayed state already folds in unlocked team buffs.

That folding is the whole point: a player pushing into enemy turf watches the zone
indicator stop reading as penalised once the team unlocks Vanguard. The buff is
taught by the thing it changes, which is legible in a way "floors your dealt
modifier at 1.0" never was.

The zone is evaluated for the **local player only** and fires **on band change**,
not per frame — with two discrete states, changes are rare, so this costs nothing at
20 players.

### Individual buff row — its own surface

[`BuffIconDisplay.cs`](../../../Assets/Scripts/Hud/BuffIconDisplay.cs) keeps its tier
colour lerp and its radial cooldown sweep for Stealth and Dash, and gains:

- **Tier pips** (`●●○`) so the current tier is readable exactly, not inferred from
  a colour.
- A **next-unlock progress fill** driven by `TotalDepositedValue` against the next
  threshold, so a player can see what a deposit run is worth before making it.

Both repaint off the existing `PlayerBuffs.BuffsChanged` event.

### Unlock moments

Tier-ups are **discrete events** and need a toast, not just a changed state.
Detection is **client-side**, inside `OnChangedRender`, comparing previous to new
tier — for both the individual row and the team strip.

`onDamageBuffUnlocked` and `onDefenseBuffUnlocked` are **deleted** (finding #6).
They fire behind a `HasStateAuthority` guard, so on a dedicated server they fire
headless where no client can ever observe them, and nothing subscribes to them
today. The `OnChangedRender` path is the only correct one, because it also gives
late joiners the right state without a missed RPC.

### Sudden Death

Its own banner — "SUDDEN DEATH · all buffs unlocked · next capture wins". The two
existing surfaces need no special case: the individual row shows every pip filled
and the team strip shows the debuff fully lifted, both derived from `Phase` like
everything else.

## Reset on rematch

Handled by the match-lifecycle spec's scene-reload contract — see
[2026-07-29-match-lifecycle-design.md](2026-07-29-match-lifecycle-design.md)
§"State-reset contract". Not duplicated here. What this design adds to that
contract:

| State | How it resets |
|---|---|
| `TeamScoreManager` scores | Scene `NetworkBehaviour`, despawned and respawned at zero |
| Team buff tiers | **Derived** from score + roster size — nothing to reset |
| `TeamRosterSize` | Scene network state; re-captured on the next entry to `Live` |
| `PlayerBuffs.TotalDepositedValue` | Player objects re-spawned by `NetworkedSpawnManager` |
| Individual tiers | **Derived** — nothing to reset |
| Sudden Death unlock override | **Derived** from `MatchManager.Phase` — nothing to reset |

Converting the team booleans to derived tiers **removes** an existing hazard rather
than adding one: `Team1DamageBuff` and friends are monotonic flags that no code ever
clears, and they survive today only because the scene reload happens to despawn
their host object. Derived tiers cannot leak by construction.

Remaining work is an audit, not a routine: confirm no `static` or
`DontDestroyOnLoad` object holds economy state. `GameSettingsManager` persists
intentionally and holds config only.

## Scope note — natural seams

This is one coherent economy design, but it is more than one sitting of work. It
decomposes along four seams that can land independently, each leaving the game in a
playable state:

1. **Win-condition boundary** — remove the coin tiebreak and `scoreLimit`, add the
   `SuddenDeath` phase and its tier override. Touches `MatchManager`,
   `MatchResolver`, `GameSettingsManager`. Independent of everything below.
2. **Territory + team layer** — one debuff, two zones, derived team tiers,
   `TeamRosterSize`, Reinforcement and the respawn-path collapse. Touches
   `TeamManager`, `CombatConfig`, `TeamScoreManager`, `PlayerStatsHandler`.
3. **Individual layer** — explicit `MaxTier`, the 12-step curve, Flag Runner,
   `CarrySpeedMultiplier`, the 4-entry loadout picker.
4. **Feedback + supply** — the merged Team Power strip, buff-row pips and progress,
   unlock toasts, deterministic coin drops.

Seam 1 is the highest-value first landing: it is the smallest change and it is the
one that makes the game's stated win condition true. Sequencing beyond that is a
planning decision, not a design one, and is deliberately left to the plan.

## Non-goals

- **Coins as a win path or tiebreak**, in any form. Removed, not deferred.
- **A deposit allocation / split UI**, or a personal-vs-team choice at the base.
- **A second currency** for the team layer.
- **A team-level buff picker.** Team order is authored.
- **Individual combat-stat buffs** — attack, health, base move speed.
- **Additional active abilities** beyond Stealth.
- **A two-sided territorial model.** One debuff, one direction.
- **Territorial gradients.** Two discrete zones, not a lerp.
- **Overtime other than Sudden Death**, and no draw in default play.
- **Arena shrinking or other Sudden Death escalation** beyond the buff unlock.
- **A per-player stats scoreboard** (K/D, captures, coins) — separate item, as the
  match-lifecycle spec also notes.
- **In-place networked reset** — scene reload does it.
- **Fixing `RPC_AddPoints`'s `RpcSources.All` cheat surface.** Noted in the audit;
  it is a security concern, not an economy-design one, and wants its own pass.

## Resolved open questions

| Question | Resolution |
|---|---|
| Dual-benefit, player-chosen split, or separate currencies? | **Dual benefit.** Tension moves to the carry loop's four knobs instead of an allocation decision. |
| Keep territorial combat or cut it? | **Keep and tune** — simplified to one debuff, 3× swing, two zones. Team buffs stay coupled to it deliberately, with Reinforcement as the territory-independent pillar. |
| Target match length and reachable tiers? | **8–10 min.** T2 on top priorities is the reliable outcome; T3 is rare, and universal only in Sudden Death. |
| Combat stats in the individual catalog? | **No.** Movement/utility only, to cap the snowball at mobility. |
| More actives beyond Stealth? | **No.** Stealth is the only active; everything else is passive. |
| Is Jump/Dash/Stealth the whole catalog? | **A subset.** The catalog is four, adding Flag Runner. |
| What replaces the coin tiebreak on timer expiry? | **Sudden Death** with all buffs unlocked for everyone, ending on the next capture. Draw is reachable only via an off-by-default server hard cap. |
| Delete or repurpose `scoreLimit`? | **Delete.** A coin score limit is a coin win path by definition, and match length and buff pacing are already configured elsewhere. |

## Verification notes

Per project convention the authoritative check is manual play, but the pure logic
here is deliberately engine-free and unit-testable outside Unity (see the
bundled-Roslyn workaround):

- `BuffUnlock` step and tier math against the new 12-step and 4-step curves,
  including the 4-buff round-robin.
- The `MaxTier` validation rule — `thresholds.Length == maxTier × buffCount` must
  fail loudly.
- Team threshold normalisation: `teamScore / TeamRosterSize` against the
  per-player-average list, including `rosterSize` of 1 and an empty team.
- Vanguard's debuff formula producing exactly `0.33 / 0.67 / 1.00` at tiers 0/1/2.
- Zone classification at the ±0.33 boundaries.
- Sudden Death's tier override returning `MaxTier` for every buff regardless of
  deposited value.

Manual play must confirm the two feedback surfaces (in particular that the zone
indicator visibly changes meaning when Vanguard unlocks), and that a Sudden Death
match actually terminates in reasonable time with everyone fully buffed.
