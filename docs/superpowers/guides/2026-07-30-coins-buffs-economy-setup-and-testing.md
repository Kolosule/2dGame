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
displays a penalty the game is not applying. Without §1.2 there are no Scope 4 surfaces at
all.

### 1.1 Create a `CombatConfig` asset and assign it — **BLOCKS ALL OF SCOPE 2**

`Assets/Scenes/Gameplay.unity` currently has `GameSettingsManager.combatConfig: {fileID: 0}`,
and **no `CombatConfig` asset exists anywhere in the project**. Consequences today:

- `CombatConfig.ResolveDamage` is never called. `PlayerCombat`'s melee/projectile damage
  resolution and `Enemy.AttackPlayer` fall through to raw base damage — no unified pipeline
  at all.
- Therefore: no own-base-distance vulnerability, no Vanguard lift, no `globalDamageMultiplier`.
  Coin deposits buy **nothing** at the team level. (There is no crit in this model — it was
  removed along with the old attacker-side debuff.)
- The `territorialAdvantageEnabled is FALSE` warning does **not** fire, because it lives
  inside the method that is never reached. Silence here is not evidence.
- Meanwhile `TeamScoreDisplay`'s zone indicator derives its display straight from
  `TerritorialCombat` / `TeamManager.GetOwnBaseDistance01`, which does not know the config is
  missing — so the HUD can read `EXPOSED  +DAMAGE TAKEN` while the actual applied damage is
  ×1.00. **The strip lies until this asset exists.**

Steps:

1. Project window → right-click → `Create ▸ Game ▸ Combat Configuration`.
   Save as `Assets/Settings/Combat/CombatConfig.asset`.
2. Leave `territorialAdvantageEnabled` **ticked** (it is the default). Sensible starting
   value: `globalDamageMultiplier 1.0`. There is no crit setting to configure — the model
   is one modifier (own-base-distance vulnerability), one side (the defender), full stop.
3. Select the `GameSettingsManager` object in `Gameplay.unity` and drag the asset into
   **Combat Config**.
4. Confirm `TeamManager`'s `team1Data` / `team2Data` are assigned and each `TeamData` has a
   sane `basePosition` — `TeamManager.GetOwnBaseDistance01` returns a flat `0f` (no malus,
   multiplier stays ×1.0) when either is missing.

### 1.2 Build the Scope 4 HUD surfaces

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

### 1.3 Add the fourth buff icon (Flag Runner) — the builder will **not** do this for you

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

### 1.4 Match settings for a short test loop

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
| 4 | In Sudden Death, on the client that has banked **zero** coins: check the buff row and Team Power strip | Every pip filled on all four icons; Vanguard shows `T2 … MAX`; zone reads `EXPOSED  VANGUARD SHIELDED` (not a penalty) when standing far from your own base | Pips stay empty on the client — the phase-driven repaint is not reaching non-authority peers |
| 5 | Capture during normal **Live** play | Ends the match exactly as before | — |
| 6 | Set `suddenDeathHardCap = 0.5`, restart, let Live **and** the 30s cap expire | Results panel reads `It's a Draw!` | The server wedges in Sudden Death forever |
| 7 | Reset `suddenDeathHardCap = 0`, play a full match, return to lobby, start a **second** match | Tiers are back to normal (derived from the fresh `TotalDepositedValue`), no leftover max tiers | Sudden Death's override leaked across matches |

Also confirm in Sudden Death: **enemies still move and input is still live**. Every
gameplay gate moved from `Phase == Live` to `IsPlayActive`; if one was missed, the arena
freezes at the exact moment the match is supposed to be decided.

### 2.2 Scope 2 — own-base-distance vulnerability + Vanguard

This is a **defender-side, continuous** model, not the old attacker-side two-state debuff.
A defender's damage-taken multiplier scales smoothly with their own distance from their own
base — ×1.0 at/near their own base, rising to a capped maximum at (or beyond) the enemy
base — and Vanguard's tier reduces that maximum. Only Team1/Team2 are vulnerable defenders;
enemy AI (Team3AI) and Team.None are exempt and always take ×1.0. See
`TerritorialCombat.ReceivedMultiplier` (`Assets/Scripts/Combat/Core/TerritorialCombat.cs`):
`1 + 1.5 * clamp01(ownBaseDistance01) * (1 - 0.5 * clampTier(vanguardTier))`.

| Vanguard tier | Multiplier at own base | Multiplier at max distance (enemy base) |
|---|---|---|
| 0 (locked) | ×1.00 | ×2.50 |
| 1 | ×1.00 | ×1.75 |
| 2 (max) | ×1.00 | ×1.00 (fully lifted) |

| # | Do | Expect | Fails if |
|---|---|---|---|
| 1 | With Vanguard locked (team score < 12 on a 1-player team), hit an enemy from **your own base** | Full damage, ×1.00 | — |
| 2 | Same attacker, same target, now standing at the **enemy base** | **×2.5 damage**. Zone indicator reads `EXPOSED  +DAMAGE TAKEN` (double space) | Damage is unchanged → §1.1 was skipped, or `territorialAdvantageEnabled` is off |
| 3 | Reverse it: have the enemy hit **you** while you stand at your own base | Your damage taken is ×1.0 regardless of where the attacker is standing — only the **defender's** own-base distance matters | Damage varies with the attacker's position → the modifier is keyed off the wrong side |
| 4 | Bank to 12 deposited (1-player team), then get hit while standing at the enemy base | Damage taken drops to **×1.75**. Vanguard milestone text shows `VANGUARD T1`, one pip filled, a `VANGUARD T1` toast fires **once** | Vanguard unlocks within seconds of the match starting → a raw threshold is being compared to the absolute team score |
| 5 | Bank to 45 deposited, get hit at the enemy base | **×1.00** — vulnerability fully lifted. Both pips filled, milestone reads `MAX`, zone indicator flips to `EXPOSED  VANGUARD SHIELDED` **without you moving** | The zone indicator does not change meaning → the Vanguard fold is not wired |
| 6 | **Run this one with 2 players on one team.** Bank 12 total | Vanguard stays **locked** — 12/2 = 6, below the threshold. It needs **24** total | It unlocks at 12 → the roster divisor is being ignored |
| 7 | Have a third player join **mid-match** on that team | The divisor does **not** change; already-earned tiers are stable | A tier is revoked by a late joiner → the roster freeze is not latching |

Sanity-check the numbers against `TerritorialCombatTests` — it asserts exactly
`×1.0 / ×1.75 / ×2.5` at the own base / half distance / max distance for tier 0, and the
tier-0/1/2 max-distance values of `×2.5 / ×1.75 / ×1.0`.

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
| 1 | Kill the same enemy archetype 5× and count coins each time | Identical count every time — a fixed `coinsToDrop` per prefab (Red 2, Violet 2, Blue/Indigo/Orange/Yellow 3, Green 4), **no position-based bonus** (see `Enemy.ResolveEffectiveStats` in `Assets/Scripts/Enemy/Base/Enemy.cs`) | Counts vary → randomness survived |
| 2 | Kill the same archetype near the arena centre vs. far from it | Identical coin count in both spots | Counts differ → a position-based drop bonus has been reintroduced |
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

1. **`TeamScoreDisplay`'s zone indicator can disagree with the real damage path.** It derives
   from `TerritorialCombat.ReceivedMultiplier` and `TeamManager.GetOwnBaseDistance01`
   directly and deliberately does not consult `CombatConfig.territorialAdvantageEnabled`
   or a null `CombatConfig`. So with the flag off — or with §1.1 not done — the strip shows
   a penalty that combat is not applying. §1.1 closes the live instance of it.
2. **The exemption from the vulnerability is keyed on "not Team1/Team2", not "is
   Team3AI".** `TeamManager.GetDamageReceivedModifier` exempts any defender that isn't
   Team1 or Team2 — Team3AI and Team.None both get ×1.0 today, but a new spawner that
   defaults its team to something other than Team3 would silently become a full
   distance-vulnerable defender. `EnemySpawner.teamID` defaults to `"Team3"` specifically
   to keep new spawner instances on the exempt side.
3. **A genuinely empty team retries its roster capture every tick** for as long as it stays
   empty. Two independent per-team latches are what stop a 1v0 start from permanently
   locking the other team's Vanguard at tier 0. Bounded by ≤20 players with an O(1) body.
4. **`RPC_AddPoints` is `RpcSources.All`** — any client can inflate team score. Pre-existing;
   explicitly left alone by Scope 2. It wants its own security pass.
5. **There is no crit in this model.** The old attacker-side debuff (and the crit roll that
   shipped alongside it when projectiles were routed through `ResolveDamage`) was removed
   by the 2026-08-05 simplification. `CombatConfig` has no crit fields; `ResolveDamage` is
   one modifier (own-base-distance vulnerability), applied to the defender, full stop.
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
