# Coins → Buffs Economy — Setup & Testing Guide

Covers all four scopes of
[the design spec](../specs/2026-07-29-coins-buffs-economy-design.md), built from
[the scope prompts](../2026-07-29-coins-buffs-economy-scope-prompts.md):

| Scope | Branch | PR |
|---|---|---|
| 1 — Win-condition boundary + Sudden Death | `feat/win-condition-sudden-death` | #67 |
| 2 — Territory rework + Vanguard team buff | `feat/territory-vanguard-rework` | #68 |
| 3 — Individual layer (Flag Runner, MaxTier, 12-step curve) | `feat/individual-buff-layer` | #69 |
| 4 — Feedback surfaces + deterministic coin supply | `feat/economy-feedback-surfaces` | #70 |

---

## 0. What is already verified

Run against the merged four-scope tree, Unity 6000.3.0f1, batchmode:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.0f1/Editor/Unity.exe" -batchmode -nographics -projectPath "C:/Users/1/Documents/GitHub/2dGame" -runTests -testPlatform EditMode -testResults results.xml -logFile unity-tests.log
```

- **297 / 297 EditMode tests pass**, 0 failed, 0 skipped.
- **0 compile errors, 0 compile warnings** across Assembly-CSharp, all `Game.*.Core`
  assemblies, and the Editor assembly (so `EconomyHudBuilder` and `LoadoutPickerBuilder`
  both compile).
- Dead code removal is complete: no remaining references to `MatchManager.IsLive`,
  `GetDamageReceivedModifier`, `coinsToDropMin/Max`, or `scoreLimit`.
- Assets already committed and correct: `BuffLoadoutConfig.asset` (4 buffs, 12 ascending
  thresholds, `maxTier: 3`, default order `ExtraJump, Stealth, QuickerDash, FlagRunner`),
  `FlagRunnerBuff.asset` (`1.1 / 1.2 / 1.2`, dash-lift at tier 3), all seven enemy prefabs
  on the new single `coinsToDrop`, and the MainMenu 4th loadout picker row (labels, up and
  down buttons all bound).

**Nothing below is a compile or logic problem. Everything below is scene wiring** — the
part no test can reach.

---

## 1. Blocking wiring — do these first

Without §1.1 the entire Scope 2 behaviour change is inert and the Scope 4 zone indicator
displays a penalty the game is not applying. Without §1.3 there are no Scope 4 surfaces at
all.

### 1.1 Create a `CombatConfig` asset and assign it — **BLOCKS ALL OF SCOPE 2**

`Assets/Scenes/Gameplay.unity` currently has `GameSettingsManager.combatConfig: {fileID: 0}`,
and **no `CombatConfig` asset exists anywhere in the project**. Consequences today:

- `CombatConfig.ResolveDamage` is never called. `PlayerCombat.ResolveMeleeDamage` and
  `ResolveProjectileDamage` fall through to raw base damage; `Enemy.DealDamage` skips the
  pipeline entirely.
- Therefore: no territorial debuff, no Vanguard lift, no crit, no `globalDamageMultiplier`.
  Coin deposits buy **nothing** at the team level.
- The `territorialAdvantageEnabled is FALSE` warning does **not** fire, because it lives
  inside the method that is never reached. Silence here is not evidence.
- Meanwhile `TerritoryReadout` derives its display straight from `TerritorialCombat`, which
  does not know the config is missing — so the HUD will read `ENEMY TERRITORY −DAMAGE`
  while damage is actually ×1.00. **The strip lies until this asset exists.**

Steps:

1. Project window → right-click → `Create ▸ Game ▸ Combat Configuration`.
   Save as `Assets/Settings/Combat/CombatConfig.asset`.
2. Leave `territorialAdvantageEnabled` **ticked** (it is the default). Sensible starting
   values: `globalDamageMultiplier 1.0`, `criticalChance 0.1`, `criticalMultiplier 2.0`.
3. Select the `GameSettingsManager` object in `Gameplay.unity` and drag the asset into
   **Combat Config**.
4. Confirm `TeamManager`'s `team1Data` / `team2Data` are assigned and each `TeamData` has a
   sane `basePosition` — `GetTerritorialAdvantage` returns a flat `0f` (no debuff anywhere)
   when either is missing.

> Heads-up on crit: projectiles now route through `ResolveDamage`, so they crit for the
> first time. The roll happens **once per shot at fire time**, so a piercing projectile
> applies the same crit to every target it hits. Intended, but it is a visible change.

### 1.2 Create a `DifficultyRingConfig` asset + an `ArenaCenter` — unblocks the ring coin bonus

Also currently null in the scene, and **neither the asset nor an `ArenaCenter` exists**.
`Enemy.ResolveEffectiveStats` therefore falls back to `RingTier.Identity` and logs
`no DifficultyRingConfig/ArenaCenter; using base stats` once per enemy spawn. Scope 4's
`coinDropBonus` is always `0`, and health/damage/speed ring scaling is inert too.

1. Add an empty GameObject named `ArenaCenter` at the arena's centre in `Gameplay.unity`
   and put the `ArenaCenter` component on it.
2. `Create ▸ …` the `DifficultyRingConfig` asset, save under `Assets/Settings/Enemies/`.
3. Author three rings **inner → outer** (ascending `maxDistanceFromCenter`), starting
   values from the spec:

   | Band | maxDistanceFromCenter | health/damage/speed | **coinDropBonus** |
   |---|---|---|---|
   | Inner | tune to your arena | > 1.0 | **2** |
   | Middle | tune to your arena | ~1.0 | **1** |
   | Outer | large / max | ≤ 1.0 | **0** |

4. Assign it to `GameSettingsManager.difficultyRingConfig`.

Until this is done, total supply is exactly `kills × coinsToDrop`, using the authored
per-archetype values: **Red 2, Violet 2, Blue 3, Indigo 3, Orange 3, Yellow 3, Green 4**.

### 1.3 Build the Scope 4 HUD surfaces

Open `Assets/Scenes/Gameplay.unity`, then **`Tools ▸ Economy ▸ Build Economy HUD`**, then
**Ctrl+S**. It is re-runnable and undo-friendly; it rebuilds only the containers it owns,
by name.

It creates and wires: the shared `UnlockToast` feed; tier pips + next-unlock fill +
toast-feed reference + human-readable display name on every existing `BuffIconDisplay`;
the Team Power strip's Vanguard pips / progress bar / milestone text / zone icon + text;
and the Sudden Death banner on `MatchPhaseHud`.

Read the console line it prints — it names anything it skipped:

```
[Economy] HUD built: toast feed ✔, N buff icon(s) extended (…), Team Power strip ✔, Sudden Death banner ✔.
```

If it says `SKIPPED — no TeamScoreDisplay in scene` or `SKIPPED — no MatchPhaseHud in
scene`, stop and fix that first; the builder wires existing components, it does not create
those two.

### 1.4 Add the fourth buff icon (Flag Runner) — the builder will **not** do this for you

`Gameplay.unity` contains exactly **3** `BuffIconDisplay` components. Flag Runner has no
icon, so its tier, pips, progress and unlock toast are invisible.

1. Duplicate one of the three existing buff-icon GameObjects in the HUD hierarchy.
2. On its `BuffIconDisplay`, set **Buff Id = FlagRunner**. Leave `cooldownRadial` empty —
   Flag Runner is passive.
3. Select the `PlayerHud` object and add the new display to its **Displays** array.
   `PlayerHud` binds only what is listed there; an unlisted display never receives
   `Bind()` and stays permanently blank.
4. Re-run `Tools ▸ Economy ▸ Build Economy HUD` so the new icon gets its pips, fill,
   toast-feed reference and the `"Flag Runner"` display name. Save.

### 1.5 Match settings for a short test loop

On `GameSettingsManager` in `Gameplay.unity`:

- `matchTimeLimit` is currently **1** (minute). Keep it at 1 while testing — Live expires
  fast, which is how you reach Sudden Death.
- `suddenDeathHardCap` — **leave at 0** for normal testing. Set it to `0.5` only for the
  one draw test in §2.1.
- The scene YAML still carries a stale `scoreLimit: 0` key from the renamed field. Unity
  drops it the next time the scene is saved. Harmless.

---

## 2. Manual test plan

Two clients minimum (Multiplayer Play Mode, or an editor host + one build). One player on
each team unless stated otherwise. **With a 1-player-per-team roster, the Vanguard
per-player-average thresholds equal the raw team score: T1 at 12 deposited, T2 at 45.**

### 2.1 Scope 1 — capture-only win condition + Sudden Death

| # | Do | Expect | Fails if |
|---|---|---|---|
| 1 | Start a match, let the 1-minute Live timer run out without a capture | Phase → **Sudden Death**. Banner: `SUDDEN DEATH · all buffs unlocked · next capture wins`. Match timer disappears (no clock armed). | A winner is crowned from coin score, or the results panel appears |
| 2 | Deliberately be **behind** on coins when the timer expires | Still Sudden Death, no winner | The higher coin score wins — the tiebreak was not removed |
| 3 | In Sudden Death, capture the enemy flag | Match ends immediately, correct team in the results banner, PostMatch counts down 20s to lobby | Capture is ignored (the `ReportCapture` phase guard did not widen to Sudden Death) |
| 4 | In Sudden Death, on the client that has banked **zero** coins: check the buff row and Team Power strip | Every pip filled on all four icons; Vanguard shows `T2 … MAX`; zone reads `ENEMY TERRITORY CLEAR` in the enemy third | Pips stay empty on the client — the phase-driven repaint is not reaching non-authority peers |
| 5 | Capture during normal **Live** play | Ends the match exactly as before | — |
| 6 | Set `suddenDeathHardCap = 0.5`, restart, let Live **and** the 30s cap expire | Results panel reads `It's a Draw!` | The server wedges in Sudden Death forever |
| 7 | Reset `suddenDeathHardCap = 0`, play a full match, return to lobby, start a **second** match | Tiers are back to normal (derived from the fresh `TotalDepositedValue`), no leftover max tiers | Sudden Death's override leaked across matches |

Also confirm in Sudden Death: **enemies still move and input is still live**. Every
gameplay gate moved from `Phase == Live` to `IsPlayActive`; if one was missed, the arena
freezes at the exact moment the match is supposed to be decided.

### 2.2 Scope 2 — one-sided territorial debuff + Vanguard

The debuff boundary is `advantage < -0.33`, i.e. roughly **the last third of the way to the
enemy base** along the base-to-base line. Do not eyeball the midline.

| # | Do | Expect | Fails if |
|---|---|---|---|
| 1 | With Vanguard locked (team score < 12 on a 1-player team), hit an enemy from your **own half** | Full damage, ×1.00 | — |
| 2 | Same attacker, same target, now standing deep in the **enemy third** | **~1/3 damage** (×0.33). Zone indicator reads `ENEMY TERRITORY −DAMAGE` | Damage is unchanged → §1.1 was skipped, or `territorialAdvantageEnabled` is off |
| 3 | Reverse it: have the enemy hit **you** while you stand at your own base | Their damage depends only on **where they are standing**, not on where you are | Your position changes incoming damage → the received-side modifier is still threaded somewhere |
| 4 | Bank to 12 deposited (1-player team), then hit from the enemy third | Damage rises to **×0.665**. Vanguard milestone text shows `VANGUARD T1`, one pip filled, a `VANGUARD T1` toast fires **once** | Vanguard unlocks within seconds of the match starting → a raw threshold is being compared to the absolute team score |
| 5 | Bank to 45 deposited, hit from the enemy third | **×1.00** — debuff fully lifted. Both pips filled, milestone reads `MAX`, zone indicator flips to `ENEMY TERRITORY CLEAR` **without you moving** | The zone indicator does not change meaning → the Vanguard fold is not wired |
| 6 | **Run this one with 2 players on one team.** Bank 12 total | Vanguard stays **locked** — 12/2 = 6, below the threshold. It needs **24** total | It unlocks at 12 → the roster divisor is being ignored |
| 7 | Have a third player join **mid-match** on that team | The divisor does **not** change; already-earned tiers are stable | A tier is revoked by a late joiner → the roster freeze is not latching |

Sanity-check the numbers against `TerritorialCombatTests` — it asserts exactly
`0.33 / 0.665 / 1.00` at tiers 0/1/2 and the `-0.33` boundary from both sides.

### 2.3 Scope 3 — Flag Runner, MaxTier, 12-step curve

Curve: `5, 10, 16, 24, 34, 46, 62, 80, 110, 150, 200, 260`. Round-robin over the priority
order — default `[ExtraJump, Stealth, QuickerDash, FlagRunner]`.

| Banked | Extra Jump | Stealth | Quicker Dash | Flag Runner |
|---|---|---|---|---|
| 5 | T1 | — | — | — |
| 24 | T1 | T1 | T1 | T1 |
| 55 | T2 | T2 | T1 | T1 |
| 120 | T3 | T2 | T2 | T2 |
| 220 | T3 | T3 | T3 | T2 |
| 260 | T3 | T3 | T3 | T3 |

| # | Do | Expect | Fails if |
|---|---|---|---|
| 1 | Bank in steps and read the pip rows at 5 / 24 / 55 / 120 / 260 | Pips match the table above exactly | Any row is one tier low across the board → the `MaxTier` regression is back |
| 2 | In the **lobby**, open the loadout picker | **Four** rows, reorderable with up/down. Default order as above | Only three rows appear |
| 3 | Reorder so Flag Runner is **priority #1**, start, bank 5 | Flag Runner hits T1 first; Extra Jump is still locked | The order is ignored → the byte payload is not reaching `ServerInitLoadout` |
| 4 | Start a match **without** touching the picker | Default order applied, no errors | An empty payload wipes the loadout |
| 5 | With Flag Runner at **T1**, pick up the enemy flag and run | Noticeably faster while carrying (+10%); normal speed the instant you drop it | Speed bonus applies when not carrying |
| 6 | Same at **T2** | +20% while carrying | — |
| 7 | With Flag Runner **below T3**, carry the flag and press Dash | Dash is blocked (unchanged behaviour) | — |
| 8 | At **T3**, carry the flag and press Dash | Dash fires **while carrying** | Still blocked → `CanDashWhileCarryingFlag` is not reaching `PlayerMovement` |
| 9 | Watch #8 from the **other client** | The carrier's dash replicates correctly | Only the local player sees it → the gate is reading render state instead of `CTFGameManager.IsCarrying` |
| 10 | Temporarily delete one entry from `BuffLoadoutConfig.thresholds` and enter play | **Loud** `Debug.LogError` naming the mismatch, on both editor and headless server. Restore it afterwards. | Silent demotion — the exact footgun this scope removed |

### 2.4 Scope 4 — feedback surfaces + deterministic coin supply

| # | Do | Expect | Fails if |
|---|---|---|---|
| 1 | Kill the same enemy archetype 5× and count coins each time | Identical count every time (Red 2, Violet 2, Blue/Indigo/Orange/Yellow 3, Green 4 — plus ring bonus once §1.2 is done) | Counts vary → randomness survived |
| 2 | After §1.2, kill the same archetype in the inner vs the outer ring | Inner drops `base + 2`, outer drops `base + 0` | Identical → the ring config or ArenaCenter is not wired |
| 3 | Bank coins one at a time and watch a buff icon's progress fill | Fill advances smoothly and reaches **full** exactly on the deposit that tiers it up, then resets | Fill jumps or never fills |
| 4 | Cross a tier boundary | **One** toast, naming the buff (`Flag Runner  T2`) — not `Buff T2`, and not one per deposit | Repeated toasts → the client-side edge detector is mis-primed |
| 5 | **Join a match already in progress** as a fresh client, with the other player at high tiers | **No** toast burst on join; pips and bars show the correct current state immediately | A volley of toasts fires → the edge detector is not priming silently on first observation |
| 6 | Walk from your own half into the enemy third and back, repeatedly | The zone indicator flips on **band change only** | Visible per-frame churn |
| 7 | Enter Sudden Death | The banner shows; **no** burst of tier-up toasts (suppressed by design — the banner is the message) | Four-plus toasts fire at once |
| 8 | Check the Team Power strip on a **late-joining** client | It shows the team's real Vanguard tier on first paint — never a penalty the team already bought away | It paints "penalised" then silently corrects a frame later |

### 2.5 Cross-scope regression sweep

- **20-player interest management.** `MatchManager`, `TeamScoreManager`, both flags and the
  carrier must stay always-interested, or phase, results, scores and the whole Team Power
  strip vanish for distant players. Verify from a player far from the arena centre.
- **Dedicated server.** Play one match against the Azure server
  ([runbook](../../azure-dedicated-server-runbook.md)). Confirm toasts and unlock feedback
  still appear — they are client-side by construction, which is the whole reason the
  server-side `UnityEvent`s were deleted. Also confirm the Sudden Death hard cap behaves
  headless.
- **Rematch loop.** Play → capture → results → lobby → start again. Scores, tiers, roster
  sizes and the Sudden Death override must all be clean on the second match.
- **`NetworkConditions`.** Before judging any of the "feels laggy" observations, check
  `NetworkProjectConfig ▸ NetworkConditions.Enabled` is **off**.

---

## 3. Known divergences and accepted risks

These are deliberate. Recording them so a future reader does not "fix" them.

1. **`TerritoryReadout` can disagree with the real damage path.** It derives from
   `TerritorialCombat` and deliberately does not consult
   `CombatConfig.territorialAdvantageEnabled`, a null `CombatConfig`, or `TeamManager`'s
   Team3AI exemption. So with the flag off — or with §1.1 not done — the strip shows a
   penalty that combat is not applying. Documented in the class docstring; §1.1 closes the
   live instance of it.
2. **Vanguard T1 is ×0.665, not ×0.67.** `1 − 0.67 × (1 − 0.5 × tier)` with
   `FullDebuff = 0.33` gives `0.665`. The spec's `0.67` was rounded prose.
3. **A genuinely empty team retries its roster capture every tick** for as long as it stays
   empty. Two independent per-team latches are what stop a 1v0 start from permanently
   locking the other team's Vanguard at tier 0. Bounded by ≤20 players with an O(1) body.
4. **`RPC_AddPoints` is `RpcSources.All`** — any client can inflate team score. Pre-existing;
   explicitly left alone by Scope 2. It wants its own security pass.
5. **Projectiles now crit**, rolled once per shot at fire time, so a piercing shot crits
   every target it hits. New behaviour from routing projectiles through the single damage
   entry point.
6. **`GameSettingsManager.GetRespawnTime` / `respawnTimeMultiplier` / `TeamData.respawnDelay`**
   remain a real dead path. Untouched on purpose — nothing in these four scopes touches
   respawns.
7. **`FlagRunnerBuffDefinition.ContributeStats`** would index `-1` if
   `carrySpeedMultipliers` were authored empty. Not reachable with the shipped asset
   (three entries); worth a guard if the array ever becomes editable in the wild.
8. **Displays call `Unbind()` from `OnDisable()` but do not re-bind on enable.** Toggling
   the `PlayerHud` root recovers (it clears `bound` and re-binds next `Update`); toggling a
   single child display does not. Pre-existing pattern, not introduced here.

---

## 4. If something is wrong

Re-run the pure-logic suite first — it is fast and rules out the whole derivation layer:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.0f1/Editor/Unity.exe" -batchmode -nographics -projectPath "C:/Users/1/Documents/GitHub/2dGame" -runTests -testPlatform EditMode -testResults results.xml -logFile unity-tests.log
```

If those 297 still pass, the fault is in scene wiring or replication, not in the math.
Work down §1 in order before debugging anything else.
