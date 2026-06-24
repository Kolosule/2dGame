# Deposit-Earned Buffs — Unity Setup & Verification Guide

This guide covers everything you must do **in the Unity Editor** to make the merged
buff system actually run. All the C# is on `main`; the steps below create the data
assets, wire components, build the lobby UI, and play-test.

Work top-to-bottom — later steps depend on earlier ones.

---

## 0. Sync and let Unity compile

1. `git checkout main` and `git pull` so you have the merged code.
2. Open the project in Unity and let it import/compile. The new code adds two
   assembly definitions:
   - `Assets/Scripts/Buffs/Core/Game.Buffs.Core.asmdef` (pure unlock math)
   - `Assets/Tests/EditMode/Game.Buffs.EditModeTests.asmdef` (unit tests)
3. Watch the **Console**. Expected: **zero compile errors**. If you see errors about
   `nunit` or the test assembly, make sure the **Test Framework** package is installed
   (Window → Package Manager → Unity Registry → "Test Framework").

### Run the unit tests
- Window → General → **Test Runner** → **EditMode** tab → **Run All**.
- Expected: the `BuffUnlockTests` cases all pass (the round-robin tier math).

---

## 1. Create the buff data assets

Create a folder `Assets/Settings/Buffs/` (right-click in Project → Create → Folder).
Then create four ScriptableObject assets via the right-click **Create → Buffs** menu.

### 1a. Extra Jump  (Create → Buffs → Extra Jump)
| Field | Value |
|---|---|
| Id | `ExtraJump` |
| Display Name | `Extra Jump` |
| Icon | (optional sprite) |
| Kind | `Passive` |
| Bonus Air Jumps | `[1, 2, 0]` (default) |
| Unlimited At Tier | `3` (default) |

### 1b. Stealth  (Create → Buffs → Stealth)
| Field | Value |
|---|---|
| Id | `Stealth` |
| Display Name | `Stealth` |
| Kind | `Active` |
| Durations | `[1, 3, 10]` seconds (default) |
| Cooldown | `20` (default) |
| Flag Usable From Tier | `3` (default) |

### 1c. Quicker Dash  (Create → Buffs → Quicker Dash)
| Field | Value |
|---|---|
| Id | `QuickerDash` |
| Display Name | `Quicker Dash` |
| Kind | `Passive` |
| Cooldown Multipliers | `[0.5, 0, 0]` (default) |
| Dash Damage From Tier | `3` (default) |

> **The `Id` field is critical** — it's the stable token the network and the loadout
> picker use. ExtraJump / Stealth / QuickerDash must each be set correctly; the array
> *order* in the config does not matter, but the Ids do.

### 1d. Loadout Config  (Create → Buffs → Loadout Config)
| Field | Value |
|---|---|
| All Buffs | size 3 → assign the three assets above (any order) |
| Thresholds | `[5,10,15, 30,45,60, 120,180,240]` (default) |
| Default Order | `[ExtraJump, Stealth, QuickerDash]` (default) |

This single asset is the registry + tuning knobs for the whole system.

---

## 2. Wire the player prefab

Open **`Assets/Scripts/Player/PlayerPrefab.prefab`** (the networked player prefab —
the one with `PlayerMovement`, `PlayerCombat`, `NetworkObject`). Add these three
components to the **root** GameObject:

### 2a. PlayerBuffs
- **Config** → assign the `BuffLoadoutConfig` asset from step 1d.

### 2b. PlayerStatModifiers
- **Stats** → assign the **same `PlayerStats` asset** already used by `PlayerMovement`
  and `PlayerCombat` (so effective values build on the real base stats).
- **Unlimited Air Jump Sentinel** → leave `99`.

### 2c. PlayerStealthVisual
- **Body Renderers** → assign the **visible body `SpriteRenderer`** — the one driven by
  `Player.controller`, i.e. the same Sprite child the body `Animator` (`PlayerAnimator.anim`)
  points at. **Do NOT assign the weapon renderer** (the one on `SideAttackTransform`).
  > Reminder: this prefab has two Animators / multiple SpriteRenderers.
  > `GetComponentInChildren` returns the *weapon* one, which is why this field is
  > explicit. Pick the body sprite.
- **Owner Alpha** `0.5`, **Teammate Alpha** `0.5`, **Enemy Alpha** `0.05` (defaults).

### 2d. Confirm Fusion sees PlayerBuffs
`PlayerBuffs` is a `NetworkBehaviour`. After saving the prefab, select the
`NetworkObject` component and confirm `PlayerBuffs` appears in its baked
networked-behaviours list (Fusion 2 collects this automatically on save; if there's a
"Rebake"/refresh button, click it).

**Save the prefab.**

---

## 3. Build the lobby loadout picker UI

In the **MainMenu** scene, on the team-selection panel (the one with the
`TeamSelectionUI` component), add a small "Loadout" sub-panel:

1. Create **3 rows**, each containing:
   - a `Text` label (shows e.g. "1. Extra Jump"),
   - a ▲ Up `Button`,
   - a ▼ Down `Button`.
2. Select the `TeamSelectionUI` component and wire its new fields:
   - **Buff Config** → the `BuffLoadoutConfig` asset (**don't skip this** — see footgun
     below).
   - **Slot Labels** → the 3 row labels, **top-to-bottom = priority 1 → 3**.
   - **Slot Up Buttons** → the 3 ▲ buttons, same row order as the labels.
   - **Slot Down Buttons** → the 3 ▼ buttons, same row order.

The picker initializes to the config's Default Order; ▲/▼ reorder the rows; picking a
team submits the chosen order alongside the team choice and locks the rows.

> **Footgun:** if you leave **Buff Config** unassigned on `TeamSelectionUI`, the picker
> submits an empty loadout. Players still get the default loadout (a guard protects
> this), but the picker UI will be blank. Always assign it.

---

## 4. Play-test checklist (Host)

`GameNetworkManager.singlePlayerMode` is `true` by default, so the Host button runs a
local session you can test solo.

**Tip:** to unlock fast while testing, temporarily set the `BuffLoadoutConfig`
**Thresholds** to `[1,2,3, 4,5,6, 7,8,9]`. **Restore the real values afterward.**

Verify, in order:

1. **Loadout picker** — in the menu, the three buffs show in default order; ▲/▼ reorder
   and renumber; picking a team locks them.
2. **Jump tiers** — deposit coins to cross thresholds: +1 air jump (T1) → +2 (T2) →
   unlimited (T3).
3. **Dash tiers** — dash cooldown bar refills ~2× faster (T1) → effectively no cooldown
   (T2).
4. **Dash-strike (T3)** — at Quicker Dash T3, dashing through an enemy deals damage but
   **does NOT instakill** it (each enemy takes at most one hit per dash). This is the
   specific bug the final review caught — confirm it.
5. **Stealth** — press **Q** once Stealth is unlocked: your body fades (owner sees ~0.5
   alpha) for the tier's duration (1s at T1), then a 20s cooldown before you can re-use.
6. **Stealth vs AI** — while stealthed, a chasing enemy loses you and returns to patrol;
   it won't start a new chase until you're visible again.
7. **Priority order** — reorder the loadout (e.g. Stealth first) and confirm unlocks
   follow the new order round-robin.

Two-client checks (optional, needs a second instance with `singlePlayerMode = false`):
- An opposing player sees a stealthed enemy at ~0.05 alpha (near-invisible); teammates
  see ~0.5.

---

## 5. Tuning reference (all data-driven)

| Knob | Where | Default |
|---|---|---|
| Unlock thresholds (cumulative value) | `BuffLoadoutConfig.Thresholds` | 5/10/15, 30/45/60, 120/180/240 |
| Default loadout order | `BuffLoadoutConfig.Default Order` | ExtraJump, Stealth, QuickerDash |
| Air jumps per tier | `ExtraJumpBuff.Bonus Air Jumps` / `Unlimited At Tier` | 1, 2, unlimited@3 |
| Stealth durations / cooldown | `StealthBuff.Durations` / `Cooldown` | 1,3,10 / 20s |
| Stealth flag-use tier | `StealthBuff.Flag Usable From Tier` | 3 |
| Dash cooldown multipliers / damage tier | `QuickerDashBuff.Cooldown Multipliers` / `Dash Damage From Tier` | 0.5,0,0 / 3 |
| Stealth alphas | `PlayerStealthVisual` | owner/teammate 0.5, enemy 0.05 |
| Stealth key | code: `NetworkInputProvider` | `Q` (gamepad: `buttonEast`) |

---

## 6. Known issues / follow-ups (not blockers)

- **Gamepad stealth = B/Circle** (`buttonEast`) — unusual for an ability button. To
  change it, edit the `stealth` line in
  `Assets/Scripts/Player/NetworkInputProvider.cs`.
- **Adding a 4th buff later:** create a new `BuffDefinition` subclass (with its tier
  table + `ContributeStats`/`GetActiveParams`), make the asset, add it to
  `BuffLoadoutConfig.AllBuffs`. It auto-appears in the picker. No core-loop edits.
- **Pre-existing (unrelated) bug:** `PlayerStatsHandler.RPC_DisablePlayerControls`
  (the dead-player dim) uses `GetComponentInChildren<SpriteRenderer>()`, which returns
  the **weapon** renderer, not the body — so the death dim may target the wrong sprite.
  Worth fixing separately; if you do, point it at the same body renderer(s) you assigned
  to `PlayerStealthVisual`.
