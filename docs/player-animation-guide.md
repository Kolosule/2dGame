# Player Animation Guide

How the networked player animation system works, and exactly how to update or add
animations. Read this before touching `Player.controller`, `Weapon.controller`, or
`PlayerAnimator.cs`.

---

## 1. The model in one paragraph

Animation is **split by whether a pose can be inferred from motion**. **Authoritative**
poses that a remote client *cannot* guess from movement — Dead, the latched one-shots
(Attack/AttackUp/AttackDown/Shoot), Stunned, Dash — are computed on the state authority (and
predicted on the local input authority) and replicated as a single `[Networked]`
`PlayerAnimator.OverrideState` enum (`AnimState.None` = "no override"). **Locomotion**
(Idle/Walk/Jump/Fall) is **not networked**: every client derives it in `Render()` from
*this object's own rendered (interpolated) motion*, via the pure `LocomotionResolver`, so a
proxy's legs stay in lockstep with the smooth position the viewer actually sees — at render
frame-rate, not the low network send rate (that mismatch was the old choppiness). The one
locomotion input that can't be read from smoothed motion (jump apex / landing are ambiguous)
is a replicated `Grounded` bool. In `Render()` (all clients) the resolved pose is pushed into
the Animator(s) as one integer parameter named **`State`**. There are still **no triggers and
no per-state bools** — a remote player sees your attack/shot because `OverrideState`
replicated, and your walk/jump because they derived it from your motion.

There are **two animation tracks**, both driven off the same `State` int:

| Track | GameObject | Controller | Purpose |
|---|---|---|---|
| **Body** | `Sprite` child | `Assets/Animation/Player.controller` | Locomotion + action poses (the visible character) |
| **Weapon / portal** | `WeaponFacing/WeaponVisual` child | `Assets/Animation/Weapon.controller` | Directional weapon swings; hidden otherwise |

Both are wired in the prefab on the `PlayerAnimator` component (`anim` = body,
`weaponAnim` = weapon). `weaponFacingRoot` references the unanimated `WeaponFacing`
parent: it compensates for body turns so the weapon keeps the facing committed at
the start of the swing. Leaving `weaponAnim` empty disables the weapon track.

---

## 2. The `State` enum ↔ clip map

`AnimState` lives in `Assets/Scripts/Player/Animation/Core/AnimState.cs`. The integer values are a
contract: the controllers select a state via a transition condition `State Equals <int>`.
**If you change the enum, you must update both controllers' transitions to match.**

| int | AnimState | Body clip (`Player.controller`) | Weapon clip (`Weapon.controller`) |
|---|---|---|---|
| 0 | Idle | `Player_Idle.anim` | Hidden (no clip) |
| 1 | Walk | `Player Walk.anim` | Hidden |
| 2 | Jump | `Player Jump.anim` | Hidden |
| 3 | Fall | `Fall.anim` | Hidden |
| 4 | Dash | `Dash.anim` | Hidden |
| 5 | Attack | `AttackGround.anim` | `WeaponAttackSwing.anim` |
| 6 | AttackDown | `AttackGround.anim` *(same body pose)* | `WeaponAttackDown.anim` |
| 7 | Shoot | `AttackGround.anim` *(placeholder)* | `WeaponShootDraw.anim` *(empty stub)* |
| 8 | Stunned | `Hit.anim` | Hidden |
| 9 | Dead | `Dead.anim` | Hidden |
| 10 | AttackUp | `AttackGround.anim` *(same body pose)* | `WeaponAttackUp.anim` |

The three melee clips use the same sprites and sprite-key timing. Up/down change
only weapon placement and in-plane orientation; the body stays upright. State 6
retains the old ground-pound state ID, and the down clip retains that asset's GUID.
Only Shoot still uses placeholder body art and an empty weapon clip.

---

## 3. How the pose is decided

**Authoritative override** — `PlayerAnimator.ComputeOverride()` runs each tick on the state
authority (predicted on the local input authority), first match wins:

1. `stats.IsDead` → **Dead**
2. A latched one-shot action still running → **ActionState** (Attack / AttackUp / AttackDown / Shoot)
3. `movement.IsStunned()` → **Stunned**
4. `movement.IsDashing()` → **Dash**
5. otherwise → **None** (locomotion is derived locally)

This result is replicated as `OverrideState`, and the grounded flag as `Grounded`.

**Locomotion** — when `OverrideState == None`, `Render()` derives the pose on *every* client
from this object's rendered motion (`(position - lastRenderPos)/Δt`) and the replicated
`Grounded`, via `LocomotionResolver` (Fusion-free, unit-tested):

- airborne & rising (`vy > riseSpeed`) → **Jump**
- airborne & falling (`vy < fallSpeed`) → **Fall** (apex holds the current airborne pose)
- grounded & `|vx| > walkEnterSpeed` → **Walk**; drops back to **Idle** below `walkStopSpeed`
  (asymmetric = hysteresis), and a grounded Walk↔Idle flip must persist `minGroundedDwell`
  seconds before it shows. These thresholds are `[SerializeField]` fields on `PlayerAnimator`.

Airborne poses and any air↔ground change commit immediately; only the grounded Walk↔Idle
flip is dwell-gated. This is what makes remote locomotion smooth instead of choppy.

"Latched action" = when you melee/shoot, `PlayerCombat` calls
`PlayerAnimator.TriggerAttack(verticalAim)` or `TriggerShoot()`, which sets a networked
`TickTimer` for a few tenths of a second. While that timer runs, `State` is forced to the
action value so the swing/portal plays to completion even though you're still moving.
All three melee directions share `attackDuration`; shooting uses `shootDuration`
(default 0.3s each). Sprite-key timing is copied from the existing side swing rather
than stretched to fill that hold.

### Directional melee behavior

Use the normal melee button with W/up-arrow or the gamepad stick up for an up
attack, S/down-arrow or stick down for a down attack, and no vertical aim for a
side attack. Side and up are available on the ground and in the air.

Down attacks use `PlayerMovement.IsGrounded()` and are airborne-only. A grounded
request does not start a swing, play its animation/sound, spend its cooldown, or
fall back to a side attack. Landing during startup or active frames cancels the
remaining down hits and the down pose, but retains cooldown and recovery. Leaving
the ground again does not reactivate that swing.

An accepted down hit spikes an enemy NPC or opposing player downward. The
attacker does not dive or bounce, and simultaneous down-plus-melee does not start
fast-fall. The target receives a one-shot downward velocity, with no new sideways
launch, ground-impact damage, or forced fall until landing. A target's active dash
and stale buffered jump cannot erase the spike; subsequent inputs work normally.
Damage, target filtering, and one-hit-per-swing rules match the side attack.
Damaging dash remains an ordinary side hit.

Tune `downAttackSpikeSpeed` on `PlayerCombat` (default 20). Keep it compatible with
the player's `maxFallSpeed`, which still caps subsequent falling motion. The old
`groundPoundForce` value migrates to this field, but it now affects the target.
Ordinary side/up and projectile knockback are unchanged.

Ranged attacks charge while the shoot control is held and call `TriggerShoot()` only
on release. Charging does not latch the Shoot pose or play firing effects. The player
prefab ramps launch speed from 5 to 40 over 2 seconds, with no ranged firing cooldown;
holding longer retains full charge until release. There is no separate charge animation.

---

## 4. Common tasks

### A. Replace a movement clip (e.g. a new Walk animation)
1. Import the new `.anim` (or edit the existing one in the Animation window).
2. Open `Player.controller` in the Animator window.
3. Click the state (e.g. **Walk**) → in the Inspector set **Motion** to the new clip.
4. Done. No code or transition changes — the state↔int wiring is unchanged.

> You can also just overwrite the existing clip asset; the state already references it by
> GUID, so the new motion shows up automatically.

### B. Update the directional weapon art
The melee weapon clips in `Assets/Animation/` are already populated:
`WeaponAttackSwing.anim`, `WeaponAttackUp.anim`, and `WeaponAttackDown.anim`.
`WeaponShootDraw.anim` remains an empty stub.

1. Select `WeaponFacing/WeaponVisual` in the player prefab. Its default sprite is
   intentionally empty so the Hidden state can reset it to invisible.
2. Edit the side swing's sprite sequence and copy the same sprites/key times into
   the up/down clips. Preserve the vertical clips' directional transform keys.
   Rotate in the sprite plane, not edge-on around Y; keep the existing side-art
   orientation in mind when composing rotations.
3. Keep the up/down visual centers aligned with their gameplay attack points.
   Gameplay points are separate children of the player root and must not be
   animated. Their boxes are rotated copies of the side box (3 by 2 versus 2 by 3
   in the current prefab), with equivalent reach.
4. Keep the clips non-looping and within `attackDuration`. A shorter clip holds
   its final frame until the latch expires. Adjust all three clips together if
   changing melee timing; simulation hit windows remain in `PlayerStats`.

Do not animate `WeaponFacing`: its horizontal scale is owned by `PlayerAnimator`
to preserve committed facing. The side attack point also serves as the projectile
muzzle; moving the weapon artwork must not move that gameplay point.

### C. Tune how long an attack pose holds
Edit `attackDuration` / `shootDuration` on the `PlayerAnimator`
component in the prefab. This is the single source of truth for the hold time on **both**
tracks (they read the same replicated `State`).

### D. Add a brand-new state (example: `WallSlide`)
1. **Enum:** add the value at the **end** of `AnimState` (e.g. `WallSlide = 11`) so existing
   ints don't shift. (If you must insert in the middle, you have to renumber every
   transition in both controllers — avoid it.)
2. **Logic:** add a branch in `PlayerAnimator.ComputeOverride()` at the right priority.
3. **Body controller:** add a state, bind its clip, add an Any-State transition with
   condition `State Equals 11`, `Has Exit Time = false`, `Transition Duration = 0`,
   `Can Transition To Self = false`.
4. **Weapon controller:** add a matching Any-State transition `State Equals 11` → `Hidden`
   (or a weapon clip if the weapon should show).
5. If it's a one-shot (like an attack), add a `Trigger…()` latch method and call it from
   the gameplay code instead of computing it from velocity.

### E. Jump / land SFX
`PlayerAnimator` plays optional one-shots on the `State` edge into Jump and on landing
(air→ground). They're null-safe and do nothing until you assign them on the component:
- `audioSource` (auto-resolved from the player root if present; add an `AudioSource` if not)
- `jumpClip`, `landClip`

Footstep/loop SFX during Walk can be added the same way (play on entering Walk, stop on
leaving) — see `HandleSfx()`.

---

## 5. Rules & gotchas (don't skip)

- **Never call `SetTrigger`/`SetBool`/`SetFloat` for player animation.** The only animation
  write in the codebase is `anim.SetInteger("State", …)` in `PlayerAnimator.Render()`.
  Triggers fire from input-authority simulation and never reach remote clients — that was
  the original bug this system replaced.
- **Two Animators, easy to grab the wrong one.** `GetComponentInChildren<Animator>()`
  returns the **weapon** Animator first (sibling order), not the visible body. That's why
  `anim` and `weaponAnim` are explicit serialized fields on the prefab. Any new code that
  drives the body must reference the body Animator explicitly, not via
  `GetComponentInChildren`.
- **Keep `Write Defaults` ON for every state.** The weapon's **Hidden** state has no motion;
  it relies on Write Defaults to reset the weapon SpriteRenderer back to its default
  (invisible) when leaving an attack state. Mixing Write Defaults on/off across states
  causes properties to "stick" — keep it uniformly on.
- **Enum order is a serialized contract.** The ints are baked into transition conditions in
  both `.controller` files. Add new values at the end; renumber both controllers if you
  ever reorder.
- **Directional melee clips share sprites, key times, and latch duration** (§4-B/C).
- **Keep the authoritative/locomotion split.** Anything a remote client can't infer from
  motion (actions, stun, dash, death) is written to `OverrideState` only in `Simulate()`
  (state authority + predicted input authority). Anything derivable from motion
  (Idle/Walk/Jump/Fall) is derived in `Render()` on every client — do **not** network it.
  If you add a new derivable pose, extend `LocomotionResolver` (and its EditMode tests); if
  you add an authoritative pose, extend `ComputeOverride()` and give it a latch if it's a
  one-shot.
- **Don't network locomotion "to be safe."** It re-introduces the choppiness: a low-send-rate
  enum snapping on top of the smoothly interpolated proxy position.

---

## 6. Files

| File | Role |
|---|---|
| `Assets/Scripts/Player/PlayerAnimator.cs` | Owns `OverrideState`/`Grounded`, latch timers, Render resolution + application, SFX |
| `Assets/Scripts/Player/Animation/Core/AnimState.cs` | The `AnimState` enum (Fusion-free assembly so the resolver is testable) |
| `Assets/Scripts/Player/Animation/Core/PlayerLocomotionResolver.cs` | Pure Idle/Walk/Jump/Fall derivation with hysteresis + dwell |
| `Assets/Tests/EditMode/PlayerAnimation/PlayerLocomotionResolverTests.cs` | EditMode tests for the resolver |
| `Assets/Scripts/Player/PlayerController.cs` | Calls `animator.Simulate()` each tick (incl. while dead) |
| `Assets/Scripts/Player/PlayerCombat.cs` | Calls `Trigger…()` latches on melee/shoot |
| `Assets/Scripts/Player/PlayerMovement.cs` | Exposes `IsGrounded()`/`IsDashing()`/`IsStunned()` |
| `Assets/Animation/Player.controller` | Body track — 11 states, `State` Int param |
| `Assets/Animation/Weapon.controller` | Weapon/portal track — Hidden + 4 action states |
| `Assets/Animation/WeaponAttackSwing.anim`, `WeaponAttackUp.anim`, `WeaponAttackDown.anim` | Matched directional melee sprite sequences |
| `Assets/Scripts/Combat/Core/MeleeAttackRules.cs` | Airborne eligibility, persistent landing cancellation, and spike velocity rules |
| `Assets/Tests/EditMode/PlayerAnimation/DirectionalAttackAnimationTests.cs` | Controller mappings, sprite/timing parity, orientation, and pose reset |
| `Assets/Tests/EditMode/Combat/Simulation/DirectionalMeleeSimulationTests.cs` | Offline Fusion gameplay coverage for hit queries, damage gates, landing cancellation, target physics, and committed direction |
| `Assets/Scripts/Player/PlayerPrefab.prefab` | Wires body/weapon Animators, the facing pivot, directional hitboxes, and spike tuning |
