# Deposit-Earned Buffs — Unity Setup & Verification Guide

This guide covers everything you must do **in the Unity Editor** to make the merged
buff system actually run. All the C# is on `main`; the steps below create the data
assets, wire components, build the lobby UI, and play-test.

Every player uses one fixed upgrade path: **Jump → Dash → Invisibility → Flag Carrying**
(`ExtraJump → QuickerDash → Stealth → FlagRunner`). This order repeats for each tier.
The lobby shows it read-only; clients cannot submit a different order, and reconnects
restore deposited value without restoring any personal path.

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
- Expected: `BuffUnlockTests`, `BuffProgressTests`, and `BuffUpgradePathTests` all pass
  (round-robin tiers, progress, the fixed path, and its read-only lobby display).

---

## 1. Create the buff data assets

Create a folder `Assets/Settings/Buffs/` (right-click in Project → Create → Folder).
Then create the four buff assets and their config via the right-click **Create → Buffs** menu.

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

### 1d. Flag Runner  (Create → Buffs → Flag Runner)
| Field | Value |
|---|---|
| Id | `FlagRunner` |
| Display Name | `Flag Runner` |
| Kind | `Passive` |
| Carry Speed Multipliers | `[1.1, 1.2, 1.2]` |
| Dash While Carrying From Tier | `3` |

> **The `Id` field is critical** — it identifies the buff in the fixed path.
> ExtraJump / QuickerDash / Stealth / FlagRunner must each be set correctly; the array
> *order* in the registry does not change unlock priority.

### 1e. Loadout Config  (Create → Buffs → Loadout Config)
| Field | Value |
|---|---|
| All Buffs | size 4 → assign the four buff assets above (any order) |
| Thresholds | `[5,10,16,24, 34,46,62,80, 110,150,200,260]` (default) |
| Max Tier | `3` |

This single asset is the registry + tuning knobs for the whole system. Upgrade order
is not an Inspector setting: `Game.Buffs.Core.BuffUpgradePath` owns the immutable path.

---

## 2. Wire the player prefab

Open **`Assets/Scripts/Player/PlayerPrefab.prefab`** (the networked player prefab —
the one with `PlayerMovement`, `PlayerCombat`, `NetworkObject`). Add these three
components to the **root** GameObject:

### 2a. PlayerBuffs
- **Config** → assign the `BuffLoadoutConfig` asset from step 1e.

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

## 3. Wire the read-only lobby upgrade path

In the **MainMenu** scene, the `LobbyScreenUI` component owns the collapsible
**Upgrade Path** panel.

1. Create **4 TMP text labels**, one per upgrade; do not add reorder buttons.
2. Assign **Buff Config** to the shared `BuffLoadoutConfig` asset for display names.
3. Assign **Slot Labels** top-to-bottom. They always show Extra Jump, Quicker Dash,
   Stealth, and Flag Runner, using the assets' display names.
4. Wire **Loadout Toggle Button** and **Loadout Panel** to the button and panel.
   These serialized field names are retained for existing scene references.

For an existing scene, run **Tools → Lobby → Refresh Upgrade Path** and save.
The tool adds missing labels and hides legacy up/down controls. Runtime also hides
and disables those controls; no lobby action submits an upgrade order.

---

## 4. Play-test checklist (Host)

`GameNetworkManager.singlePlayerMode` is `true` by default, so the Host button runs a
local session you can test solo.

**Tip:** to unlock fast while testing, temporarily set the `BuffLoadoutConfig`
**Thresholds** to `[1,2,3,4, 5,6,7,8, 9,10,11,12]`. **Restore the real values afterward.**

Verify, in order:

1. **Upgrade path** — in the lobby, the four buffs show in the approved order with
   no up/down controls. Switching teams or reopening the panel cannot change it.
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
7. **Fixed order** — with the normal thresholds, bank to 5 / 10 / 16 / 24: Jump,
   Dash, Invisibility, then Flag Carrying reach T1. The same order repeats at T2 and T3.
8. **Reconnect** — earned tiers remain intact and subsequent unlocks follow the same
   approved path. A new match starts progression from zero in that same order.

Two-client checks (optional, needs a second instance with `singlePlayerMode = false`):
- An opposing player sees a stealthed enemy at ~0.05 alpha (near-invisible); teammates
  see ~0.5.

---

## 5. Tuning reference (all data-driven)

| Knob | Where | Default |
|---|---|---|
| Unlock thresholds (cumulative value) | `BuffLoadoutConfig.Thresholds` | 5/10/16/24, 34/46/62/80, 110/150/200/260 |
| Fixed upgrade order (not player-configurable) | `BuffUpgradePath.Order` | ExtraJump, QuickerDash, Stealth, FlagRunner |
| Air jumps per tier | `ExtraJumpBuff.Bonus Air Jumps` / `Unlimited At Tier` | 1, 2, unlimited@3 |
| Stealth durations / cooldown | `StealthBuff.Durations` / `Cooldown` | 1,3,10 / 20s |
| Stealth flag-use tier | `StealthBuff.Flag Usable From Tier` | 3 |
| Dash cooldown multipliers / damage tier | `QuickerDashBuff.Cooldown Multipliers` / `Dash Damage From Tier` | 0.5,0,0 / 3 |
| Flag carry speed / dash tier | `FlagRunnerBuff.Carry Speed Multipliers` / `Dash While Carrying From Tier` | 1.1,1.2,1.2 / 3 |
| Stealth alphas | `PlayerStealthVisual` | owner/teammate 0.5, enemy 0.05 |
| Stealth key | code: `NetworkInputProvider` | `Q` (gamepad: `buttonEast`) |

---

## 6. Known issues / follow-ups (not blockers)

- **Gamepad stealth = B/Circle** (`buttonEast`) — unusual for an ability button. To
  change it, edit the `stealth` line in
  `Assets/Scripts/Player/NetworkInputProvider.cs`.
- **Changing the approved catalog** requires an explicit code change to
  `BuffUpgradePath`, matching definitions in `BuffLoadoutConfig.AllBuffs`, and a
  complete threshold curve. Adding an asset alone does not change players' paths.
- **Pre-existing (unrelated) bug:** `PlayerStatsHandler.RPC_DisablePlayerControls`
  (the dead-player dim) uses `GetComponentInChildren<SpriteRenderer>()`, which returns
  the **weapon** renderer, not the body — so the death dim may target the wrong sprite.
  Worth fixing separately; if you do, point it at the same body renderer(s) you assigned
  to `PlayerStealthVisual`.
