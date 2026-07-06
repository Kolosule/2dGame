# Player Animation Guide

How the networked player animation system works, and exactly how to update or add
animations. Read this before touching `Player.controller`, `Weapon.controller`, or
`PlayerAnimator.cs`.

---

## 1. The model in one paragraph

Animation is **split by whether a pose can be inferred from motion**. **Authoritative**
poses that a remote client *cannot* guess from movement — Dead, the latched one-shots
(Attack/GroundPound/Shoot), Stunned, Dash — are computed on the state authority (and
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
| **Weapon / portal** | `SideAttackTransform` child | `Assets/Animation/Weapon.controller` | The weapon that portals in during attacks; hidden otherwise |

Both are wired in the prefab on the `PlayerAnimator` component (`anim` = body,
`weaponAnim` = weapon). Leaving `weaponAnim` empty just disables the weapon track.

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
| 5 | Attack | `AttackGround.anim` | `WeaponAttackSwing.anim` *(empty stub)* |
| 6 | GroundPound | `AttackGround.anim` *(placeholder)* | `WeaponGroundPound.anim` *(empty stub)* |
| 7 | Shoot | `AttackGround.anim` *(placeholder)* | `WeaponShootDraw.anim` *(empty stub)* |
| 8 | Stunned | `Hit.anim` | Hidden |
| 9 | Dead | `Dead.anim` | Hidden |

**Placeholders / TODO art:** GroundPound and Shoot reuse another clip on the body, and
all three weapon-track actions reuse `AttackEnemy.anim`. Replace these with real clips
when the art exists (see §4).

---

## 3. How the pose is decided

**Authoritative override** — `PlayerAnimator.ComputeOverride()` runs each tick on the state
authority (predicted on the local input authority), first match wins:

1. `stats.IsDead` → **Dead**
2. A latched one-shot action still running → **ActionState** (Attack / GroundPound / Shoot)
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
`PlayerAnimator.TriggerAttack/TriggerGroundPound/TriggerShoot()`, which sets a networked
`TickTimer` for a few tenths of a second. While that timer runs, `State` is forced to the
action value so the swing/portal plays to completion even though you're still moving.
The hold time is the per-action `attackDuration` / `groundPoundDuration` / `shootDuration`
field on the `PlayerAnimator` component (default 0.3s each).

---

## 4. Common tasks

### A. Replace a movement clip (e.g. a new Walk animation)
1. Import the new `.anim` (or edit the existing one in the Animation window).
2. Open `Player.controller` in the Animator window.
3. Click the state (e.g. **Walk**) → in the Inspector set **Motion** to the new clip.
4. Done. No code or transition changes — the state↔int wiring is unchanged.

> You can also just overwrite the existing clip asset; the state already references it by
> GUID, so the new motion shows up automatically.

### B. Give the portal-weapon real attack art
The weapon track is structurally complete and already wired to three **empty stub clips**
(`WeaponAttackSwing.anim`, `WeaponGroundPound.anim`, `WeaponShootDraw.anim` in
`Assets/Animation/`). They produce no visible motion yet — fill them in:
1. On the `SideAttackTransform` child (or add a dedicated `WeaponPivot` child), add/assign
   the **portal** and **weapon** `SpriteRenderer`s. (Today its SpriteRenderer has no
   sprite, which is why nothing shows.)
2. Open a stub clip (e.g. `WeaponAttackSwing.anim`) in the Animation window with the player
   prefab selected, and key the effect: portal scales open → weapon slides out and swings →
   both retract / fade. Animate the SpriteRenderer's `enabled`/color/position/scale.
   The controller already references these clips by GUID, so editing them in place is all
   you need — no re-wiring.
3. **Match the clip length to the latch duration.** The weapon state is only held while
   `State` stays on the action value, i.e. for `attackDuration` seconds (the stubs are
   authored at 0.3s, non-looping, to match the defaults). If the clip is longer it gets cut
   off; if shorter it finishes early and the weapon sits on its last frame. Keep the clip
   length ≈ the matching `*Duration` field, or tune both together.

### C. Tune how long an attack pose holds
Edit `attackDuration` / `groundPoundDuration` / `shootDuration` on the `PlayerAnimator`
component in the prefab. This is the single source of truth for the hold time on **both**
tracks (they read the same replicated `State`).

### D. Add a brand-new state (example: `WallSlide`)
1. **Enum:** add the value at the **end** of `AnimState` (e.g. `WallSlide = 10`) so existing
   ints don't shift. (If you must insert in the middle, you have to renumber every
   transition in both controllers — avoid it.)
2. **Logic:** add a branch in `PlayerAnimator.ComputeState()` at the right priority.
3. **Body controller:** add a state, bind its clip, add an Any-State transition with
   condition `State Equals 10`, `Has Exit Time = false`, `Transition Duration = 0`,
   `Can Transition To Self = false`.
4. **Weapon controller:** add a matching Any-State transition `State Equals 10` → `Hidden`
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
- **Action clip length should track the latch duration**, not the other way around (§4-B/C).
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
| `Assets/Animation/Player.controller` | Body track — 10 states, `State` Int param |
| `Assets/Animation/Weapon.controller` | Weapon/portal track — Hidden + 3 action states |
| `Assets/Scripts/Player/PlayerPrefab.prefab` | Wires `anim`/`weaponAnim` + registers `PlayerAnimator` on the NetworkObject |
