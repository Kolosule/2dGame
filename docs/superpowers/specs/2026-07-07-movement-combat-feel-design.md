# Movement & Combat Feel Pass — Design

**Date:** 2026-07-07
**Status:** Approved (brainstormed with user)
**Scope:** Ground/air acceleration model, dash momentum carry-over, asymmetric jump gravity + apex hang + fast-fall, melee attack phases (startup/active/recovery), coin-carrier aura.

## Context

Movement currently sets `rb.linearVelocity` directly from input every tick (`PlayerMovement.Simulate`), melee hitboxes exist for exactly one tick (`PlayerCombat.Attack`), and a loaded coin carrier is visually indistinguishable from an empty one. Reference feel: Hollow Knight (tight ground control, committed offense), Melee (momentum tech, dash-jump depth), Alterac Valley (hunt-the-carrier PvPvE tension).

**Chosen tuning philosophy (user decision): hybrid** — snappy, near-instant ground control; momentum-rich air and dash physics. Depth lives in dash-jump tech, not ground friction.

All simulation changes follow the project's existing netcode rules: gameplay state is `[Networked]` or derived per-tick from networked state + input (resimulation-safe); damage and hit detection run on StateAuthority only.

## Part 1 — Movement physics

### 1.1 Acceleration model (replaces direct velocity set)

Each tick, compute target speed `input.Horizontal * walkSpeed` and move current `vx` toward it at a rate expressed in **ticks to reach full walk speed**. New `PlayerStats` fields (defaults):

| Field | Default | Meaning |
|---|---|---|
| `groundAccelTicks` | 4 | ticks from 0 to walkSpeed on ground |
| `groundDecelTicks` | 3 | ticks from walkSpeed to 0 on ground, no input |
| `airAccelTicks` | 10 | same, airborne |
| `airDecelTicks` | 18 | same, airborne, no input |

Grounded state comes from the existing `groundCheck` overlap. Stun behaviour is unchanged (horizontal velocity zeroed while stunned).

### 1.2 Over-speed momentum rule

When `|vx| > walkSpeed` (reachable only via dash carry-over today), the movement code never clamps it instantly:

- Input in the direction of travel, or no input: the excess above `walkSpeed` decays at `momentumDecayAir` (default **8 u/s²** — "very minor" decay per user decision, airborne) or `momentumDecayGround` (default **40 u/s²** — a full-dash ground exit slides ≈0.25 s instead of 1.25 s). The within-walkSpeed portion follows the normal accel/decel model.
- Input against travel: brake at the normal accel/decel rates on the full velocity (counter-input is always effective).

This single rule produces dash-slides, dash-jump momentum, and (future) knockback carry-over with no special cases.

### 1.3 Dash exit and dash-jump

- `EndDash()` (timer expiry or button release) no longer implies a velocity rewrite: the player keeps full dash velocity and the momentum rule (1.2) decays it.
- **Dash-jump** (jump pressed during dash — jump already cancels dash): `vy = jumpForce`, `vx = dashDir * dashSpeed * dashJumpCarryFactor`. Default `dashJumpCarryFactor` = **0.65** (`PlayerStats` field). Result ≈ 2× walk speed at launch, decaying in air.
- Flag carriers cannot dash (existing rule), so objective traversal speed is unaffected.

### 1.4 Jump arc: asymmetric gravity + apex hang

`rb.gravityScale` becomes a pure per-tick function of state (same pattern as the current dash-gravity line):

| State | gravityScale |
|---|---|
| Dashing | 0 (unchanged) |
| Rising (`vy > apexThreshold`) | `baseGravity` |
| Apex (`|vy| ≤ apexThreshold`, airborne, not fast-falling) | `baseGravity * apexGravityMult` (default **0.5**) |
| Falling | `baseGravity * fallGravityMult` (default **1.7**) |

`apexThreshold` default **1.5 u/s**. New: **terminal fall-speed clamp** `maxFallSpeed` (default **20 u/s**) — currently none exists and 1.7× gravity demands one.

### 1.5 Fast-fall

- Trigger: airborne, past the apex (`vy ≤ apexThreshold`), **down pressed (rising edge)**.
- Effect: `vy = -fastFallSpeed` (default **18 u/s**, still clamped by `maxFallSpeed`); a `[Networked]` bool `FastFalling` latches until landing (skips apex hang, drives animation if desired).
- Input precedence: **down + attack = ground pound** (existing, unchanged, resolves first); down alone = fast-fall.

### Netcode cost (Part 1)

One new `[Networked]` bool (`FastFalling`). Everything else derives from `rb.linearVelocity` (synced by NetworkRigidbody2D) + input each tick.

## Part 2 — Melee attack phases

### 2.1 Phase model

A swing is three consecutive windows, authored as tick counts on `PlayerStats`:

| Field | Default | Window |
|---|---|---|
| `attackStartupTicks` | 3 | wind-up, no hitbox (~50 ms) |
| `attackActiveTicks` | 5 | hitbox live |
| `attackRecoveryTicks` | 10 | no melee, no dash |

Total ≈ 18 ticks ≈ current 0.3 s `attackCooldown`, so attack *rate* is roughly unchanged; what changes is hit reliability (multi-tick hitbox) and a punishable whiff tail.

### 2.2 State & derivation

New `[Networked]` fields on `PlayerCombat`:

- `AttackStartTick` (int, 0 = no swing in progress)
- `AttackAim` (int, latched `verticalAim` at press) and `AttackFacingRight` (bool, latched facing at press) — the hitbox choice (up/down/side) and direction are locked at commit; the swing never flips mid-animation.

Current phase is **derived** each tick from `Runner.Tick - AttackStartTick` (pure function, resim-proof). Pure helper `SwingPhase` maps elapsed ticks → `None / Startup / Active / Recovery`.

### 2.3 Behaviour

- **Press accepted** when no swing is in progress and `AttackCooldownTimer` expired. On accept: set `AttackStartTick`, latch aim/facing, start cooldown, call `TriggerAttack()` / `TriggerGroundPound()` immediately (animation begins on the press tick — prediction/responsiveness unchanged; startup lives inside the anim wind-up).
- **Active phase (server only):** each active tick runs `ApplyMeleeHits` with a **per-swing dedup set** (same pattern as the existing `dashStruck`), cleared at swing start → max one hit per target per swing.
- **Recovery:** blocks starting a melee and starting a dash. Movement, jump, and shooting stay free ("offense commits, movement doesn't"). `PlayerCombat` exposes `IsSwingCommitted`; the dash-start condition in `PlayerMovement.Simulate` gains that one check (mirror of the existing `playerMovement.IsDashing()` read).
- **Ground pound:** same phase system; its downward velocity fires at the start of the *active* window (≈3-tick telegraph).
- **Animation alignment:** `PlayerAnimator.attackDuration` should be tuned to match startup + active so visuals and hitbox cannot drift.

**Untouched:** dash-strike damage (Quicker Dash T3, separate path + per-dash dedup), projectiles, `RPC_HitFeedback`, the per-attacker `HitCooldownLedger`.

## Part 3 — Coin-carrier aura

### 3.1 Component

`CoinCarrierAura` MonoBehaviour on the player prefab. Purely visual, non-networked (same role split as `FlagCarrierMarker` / `PlayerStealthVisual`). Subscribes to `NetworkedPlayerInventory.CoinsChanged` (already fires on every client) and reads **`TotalCoinValue`** — value, not coin count.

### 3.2 Visual

Child `SpriteRenderer`, soft radial glow sprite, sorted **behind** the body sprite, warm gold, slow pulse. Sprite-based (no 2D-lights dependency; one quad per player). Intensity tiered by a serialized threshold array; pure helper `AuraTiers` maps value → tier.

| Tier | TotalCoinValue ≥ | Look |
|---|---|---|
| 0 | — | off |
| 1 | 5 | faint glow |
| 2 | 15 | clear glow |
| 3 | 30 | bright glow, faster pulse |

Thresholds/colors are designer-serialized defaults, expected to be retuned in playtests.

### 3.3 Interaction rules

- **Stealth hides the aura** (follows the stealth visual's state on every viewer) — a stealthed carrier goes fully dark; Stealth doubles as a smuggler's buff.
- **Death:** coins drop → `TotalCoinValue` = 0 → same change event turns the aura off (no death-specific code).
- **AoI:** players outside interest have no replicated object; nothing to handle.
- Separate renderer behind the body → cannot conflict with hit-flash, spawn-immunity dim, or stealth transparency (all operate on the body sprite).

## Testing

Branchy math lands in plain static classes (project's established pure-core pattern — `Game.Hud.Core`, `Game.Combat.Core`) with EditMode tests; NetworkBehaviours stay thin:

- `MovementMath` — accel-toward-target, over-speed momentum decay, gravity-state selection, fast-fall gating, terminal clamp
- `SwingPhase` — elapsed ticks → phase derivation, boundary ticks exact
- `AuraTiers` — value → tier mapping, threshold edges

In-editor multi-peer verification (host + client) required before merge: dash-jump momentum replicates smoothly on proxies, swing hitbox timing matches animation on both peers, aura visible/hidden correctly across stealth and death.

## Out of scope

- Knockback scaling / DI, attacker-local hitlag (separate future pass)
- Projectile stun rework (separate future pass)
- Melee damage rebalance vs projectiles (tuning, not code — flagged for playtest)
- Bounty *rewards* for killing loaded carriers (visibility only, this pass)
