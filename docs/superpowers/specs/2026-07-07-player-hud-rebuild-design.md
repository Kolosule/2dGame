# Player HUD Rebuild — Design

## Context

The in-match HUD is currently driven by `Assets/Scripts/Coin Scripts/UIManager.cs`, a single
polling script (`Update()` every 0.1s) that renders team score, coin count/value, a health
slider, a dash-cooldown radial, and 4 static team-wide buff icons sourced from
`TeamScoreManager` (`Team1DamageBuff` etc.).

Since that script was written, the game has grown a per-player deposit-earned buff system
(`Assets/Scripts/Buffs/PlayerBuffs.cs`) with three equippable buffs (dash, jump, stealth) each
having tiers 0–3 driven by `TotalDepositedValue`. The old team-wide damage/defense buff system
still exists and is still earned via team score — it is not being replaced, just re-skinned.

The CTF flag HUD (directional arrows, carrier icon, center notifications) was already redesigned
under `docs/superpowers/specs/2026-06-24-flag-hud-rebuild-design.md` and is out of scope for
visual changes here — this rebuild only needs to confirm it doesn't visually clash with the new
HUD.

This is a full rebuild of the persistent in-match HUD: health, coins, the three per-player buffs,
team score, and the re-skinned team buff indicator. Combat feedback (hit markers, damage numbers)
is explicitly deferred and not touched by this design.

## Goals

- Replace the `UIManager.cs` god-script with small, focused, event-driven display components.
- Establish a minimal, clarity-first visual language: dark translucent panels, functional color
  coding (not team-colored), segmented health bar, tier-as-glow buff icons.
- Keep the flag HUD and combat feedback systems untouched.
- Re-skin (not redesign) the existing team-wide buff mechanic.

## Non-Goals

- Redesigning flag HUD visuals or logic.
- Redesigning or restyling hit markers / damage numbers.
- Any new team-milestone mechanic — the existing `TeamScoreManager` damage/defense buff system is
  reused as-is.

## Layout

Bottom-anchored primary cluster, matching priority order (buffs/coins/health → flag → team score):

```
┌──────────────────────────────────────────────┐
│              ▲ (flag arrow, top-center)  [42-17]│
│                                          [◆]    │  ← team buff symbol, shown only when active
│                                                  │
│                    (gameplay)                    │
│                                                  │
│ ❤ ▮▮▮▮▮▯▯▯▯▯   🪙 142   [Dash][Jump][Stealth]   │
└──────────────────────────────────────────────┘
```

- **Bottom-left/center cluster**: health, coins, buff row — the player's most immediately
  relevant state, grouped as one glanceable unit.
- **Top-center**: flag directional arrow / carrier icon / notifications — existing implementation,
  unchanged position and behavior.
- **Top-right, small**: team score, with a small buff-symbol badge appearing next to it only when
  the team-wide damage/defense buff is currently active.

## Components

Each display is a standalone `MonoBehaviour` under a `PlayerHUD` root. Each finds its data source
once (via the local player's `PlayerRef`/`NetworkObject`) and re-renders only when the underlying
networked value changes (Fusion `OnChanged` callbacks), replacing the current `Update()`-polling
pattern.

### HealthDisplay
- Source: `PlayerStatsHandler.GetCurrentHealth()` / `GetMaxHealth()`.
- Visual: segmented/chunked bar — discrete blocks rather than a continuous fill — for a clearer
  at-a-glance damage read than the current smooth slider.
- Color: red/white functional coloring, not team-colored.

### CoinDisplay
- Source: `NetworkedPlayerInventory.CoinCount` / `TotalCoinValue`.
- Visual: icon + number, unchanged in concept from current implementation, restyled to match the
  new panel language.

### BuffTierDisplay (×3: dash, jump, stealth)
- Source: `PlayerBuffs.TierOf(BuffId)` for tier (0–3), plus each buff's own
  activation/cooldown state (e.g. dash's existing `PlayerMovement.GetDashCooldownPercent()`,
  stealth's cooldown/active timers on `PlayerBuffs`).
- Tier visualization: **icon color/glow intensity** — tier 0 dim/gray, tier 3 bright/saturated
  accent color. Each buff type gets its own distinct accent color (not team-colored) so meaning
  reads independent of team.
- Cooldown/activation visualization: **built into the same icon** as a radial sweep overlay (the
  icon darkens/sweeps during cooldown), rather than a separate ability bar — one compact element
  per ability, consistent with the existing dash radial pattern.
- Three icons arranged in a row, immediately right of the coin display, inside the same bottom
  cluster panel.

### TeamScoreDisplay + TeamBuffIndicator
- Source: `TeamScoreManager` (score values + `Team1DamageBuff`/`Team2DamageBuff`/defense
  equivalents).
- Team score: small text/number, top-right corner — lowest visual weight per priority order.
- Team buff indicator: a small symbol/badge next to the score that only renders while the
  corresponding team buff is active. This replaces the current 4 static always-visible icons with
  a single state-driven badge. Re-skin only — no change to the underlying unlock mechanic.

### Flag HUD (unchanged)
- No component changes. Verify visually post-implementation that panel colors/fonts don't clash
  with the new bottom-cluster and team-score styling.

## Visual Language

- **Panels**: dark, semi-transparent backgrounds behind each cluster (not opaque bars), for a
  minimal, clarity-first feel.
- **Color coding is functional, not team-based**: red/white = health, gold = coins, distinct
  per-buff accent colors for dash/jump/stealth. Team color is not used anywhere in this HUD except
  implicitly via the flag HUD (unchanged) and the team score number itself.
- **Typography**: match existing project font/TMP conventions; no new font introduced.

## Testing

- Unit-testable logic (tier-to-color mapping, cooldown percent calculation, team-buff-active
  boolean) should be extracted into plain C# so it can be tested without Unity/Fusion runtime,
  consistent with existing project patterns (see `HitCooldownLedger.cs` style).
- Manual in-editor verification: single-player smoke test for health/coin/buff tier changes,
  multi-peer test for team score and team buff indicator sync, confirm flag HUD renders correctly
  alongside the new panels at various screen resolutions.
