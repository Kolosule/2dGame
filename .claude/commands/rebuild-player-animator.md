---
description: Rebuild the networked player animation system from scratch (PlayerAnimator + single-layer Player.controller)
---

# Task: Rebuild the player animation system from scratch

You are working in a Unity 2D project (`2dGame`) that uses **Photon Fusion** for networking. The current player animator is poorly designed and must be rebuilt from the ground up. This prompt contains the full design — follow it precisely. Before writing code, read the files referenced below to confirm current state; do not assume.

## Critical context: how this project handles networked state

This project deliberately does **NOT** use Unity's `NetworkTransform` or rely on input-driven animation. Movement is synced via `NetworkRigidbody2D` + `[Networked]` properties applied locally in `Render()`. **Animation must follow the same pattern: derive it from networked state and apply it on every client in `Render()`** — never fire animation triggers from input-authority-only simulation code.

## The defect you are fixing (root cause)

Today, animation is driven as a side-effect of local input:
- `PlayerMovement.Render()` sets `Walking`/`Dashing` bools (these replicate, OK).
- `PlayerMovement.DoJump()`, `PlayerCombat.Attack()`, `PlayerCombat.ShootProjectile()` call `SetTrigger("Jump"/"Attack"/"Shoot"/"Jump Attack")` **inside `Simulate()` (FixedUpdateNetwork)**. That code path only runs for input/state authority, so **remote players never see jumps, attacks, or shots.** This is the primary bug to eliminate.

Other confirmed defects to clean up while rebuilding:
1. `Dashing` is declared as a **Trigger** in the controller but driven with `SetBool` (type mismatch).
2. `Shoot` trigger is **never declared** in the controller.
3. `Grounded` bool is **never set by code**, yet transitions depend on it (dead edges).
4. `WallSliding`, `Die`, `JumpUp` params are declared/referenced but never properly driven; there is **no wall-slide gameplay logic** in `PlayerMovement` — do not add wall-slide.
5. The current "Player Jump" state is wrongly bound to **Fall.anim**, idle is bound to `Iddle.anim` (not `Player_Idle.anim`), attack is bound to `AttackEnemy.anim` (not `AttackGround.anim`).
6. The controller has a redundant **Weapon Layer** duplicating Base Layer states. Collapse to a single layer.

## Files to read first
- `Assets/Scripts/Player/PlayerMovement.cs` — networked movement; sets Walking/Dashing/Jump.
- `Assets/Scripts/Player/PlayerCombat.cs` — networked combat; sets Attack/Jump Attack/Shoot.
- `Assets/Scripts/Player/PlayerController.cs` — orchestrator; calls `movement.Simulate()` / `combat.Simulate()` in `FixedUpdateNetwork`, and gates on `stats.IsDead`.
- `Assets/Scripts/Player/PlayerStatsHandler.cs` — exposes `[Networked] IsDead`.
- `Assets/Animation/Player.controller` — the controller to replace.
- `Assets/MetroidvaniaController/Animation/Scripts/JumpStateBehaviour.cs` and `PlaySoundBehaviour.cs` — StateMachineBehaviours that play jump/land/footstep SFX off animator params; these params are being removed, so re-home or delete them (see SFX note).

## Target design

### 1. New component: `PlayerAnimator : NetworkBehaviour`
Create `Assets/Scripts/Player/PlayerAnimator.cs`.

- Define `public enum AnimState : byte { Idle, Walk, Jump, Fall, Dash, Attack, GroundPound, Shoot, Stunned, Dead }`.
- `[Networked] public AnimState State { get; private set; }`
- `[Networked] private TickTimer ActionTimer { get; set; }` — holds one-shot action states (Attack/Shoot/GroundPound) for the clip duration so they replicate without triggers.
- `[Networked] private AnimState ActionState { get; set; }` — which action is currently latched.
- Cache `Animator anim` (via `GetComponentInChildren<Animator>()`) and refs to `PlayerMovement`, `PlayerCombat`, `PlayerStatsHandler`, `Rigidbody2D` in `Spawned()`.

**State computation (state authority only), called once per tick.** Add a public method `public void Simulate()` invoked from `PlayerController.FixedUpdateNetwork` AFTER movement/combat simulate, OR compute directly in `FixedUpdateNetwork` guarded by `HasStateAuthority`. Priority order (first match wins):
1. `stats.IsDead` → `Dead`
2. action latched and `!ActionTimer.ExpiredOrNotRunning(Runner)` → `ActionState` (Attack/Shoot/GroundPound)
3. `movement.IsStunned()` → `Stunned`
4. `movement.IsDashing()` → `Dash`
5. not grounded and `rb.linearVelocity.y > 0.1f` → `Jump`
6. not grounded and `rb.linearVelocity.y <= 0.1f` → `Fall`
7. `Mathf.Abs(rb.linearVelocity.x) > 0.1f` → `Walk`
8. else → `Idle`

**Latching actions:** expose `public void TriggerAttack()`, `TriggerGroundPound()`, `TriggerShoot()` on `PlayerAnimator`. Each sets `ActionState` and `ActionTimer = TickTimer.CreateFromSeconds(Runner, <clipLen>)` (use a serialized per-action duration, default ~0.3s). Call these from `PlayerCombat.Attack()` / `ShootProjectile()` **instead of** the current `anim.SetTrigger(...)` calls. They must run on state authority (the combat code already gates damage on `HasStateAuthority`; the latch must be set on the authority that owns the networked state).

**Applying animation (ALL clients, in `Render()`):**
```csharp
public override void Render()
{
    if (anim == null) return;
    anim.SetInteger("State", (int)State);
}
```
Drive the controller off this single `State` int. Do not use any triggers or other bools for state selection.

### 2. Strip animator calls from movement/combat
- `PlayerMovement`: remove `anim.SetTrigger("Jump")`, `anim.SetBool("Walking", ...)`, `anim.SetBool("Dashing", ...)`. Movement already exposes `IsDashing()` / `IsStunned()`; keep those. Walk/Jump/Fall are now derived by `PlayerAnimator` from velocity + grounded, so `PlayerMovement` no longer needs the `anim` reference (remove it unless used elsewhere). Keep a `public bool IsGrounded()` accessor (compute from the existing groundCheck OverlapCircle) so `PlayerAnimator` can read grounded state instead of recomputing — or expose grounded via a small networked/local helper. Pick one source of truth and document it.
- `PlayerCombat`: replace `anim.SetTrigger("Attack")` → `playerAnimator.TriggerAttack()`, `anim.SetTrigger("Jump Attack")` → `playerAnimator.TriggerGroundPound()`, `anim.SetTrigger("Shoot")` → `playerAnimator.TriggerShoot()`. Remove the `anim` field if no longer used.
- `PlayerController`: add a `PlayerAnimator` ref; call its simulate step each tick after movement/combat (and ensure it still ticks while dead so the `Dead` state latches — currently `FixedUpdateNetwork` early-returns on `IsDead` before simulating; the animator state for Dead can be computed before that return, or move the dead-state handling so `PlayerAnimator` still updates).

### 3. Rebuild `Assets/Animation/Player.controller`
Author a **fresh single-layer** controller (delete the Weapon Layer and all duplicate states).

- **Parameters:** exactly one — `State` (Int). Remove `Walking`, `Jump`, `Grounded`, `Dashing`, `Jump Attack`, `Attack`, `WallSliding`, `Die`.
- **States (one per enum value), bound to these verified clip GUIDs:**

  | State (int) | AnimState | Clip | GUID |
  |---|---|---|---|
  | 0 | Idle | Player_Idle.anim | `a789abc0cdaf8624586007c687312f72` |
  | 1 | Walk | Player Walk.anim | `08b391e99adbbbd44864e73c6041825f` |
  | 2 | Jump | Player Jump.anim | `61ce7f28aade01d4294f4c3bb9a5d475` |
  | 3 | Fall | Fall.anim | `a7214ada599484442bd5137366fc8201` |
  | 4 | Dash | Dash.anim | `672d90e35c9f5cf429ca6168588129e0` |
  | 5 | Attack | AttackGround.anim | `0489ae753b0116f47bbfba7941ae1094` |
  | 6 | GroundPound | **PLACEHOLDER → reuse AttackGround.anim** `0489ae753b0116f47bbfba7941ae1094` | (TODO: no dedicated clip) |
  | 7 | Shoot | **PLACEHOLDER → reuse AttackGround.anim** `0489ae753b0116f47bbfba7941ae1094` | (TODO: no dedicated clip) |
  | 8 | Stunned | Hit.anim | `26a1b23e6af98ce43a5b8844e367f70c` |
  | 9 | Dead | Dead.anim | `0bd87dfb5890a204f95daf6be766b962` |

  Clearly mark GroundPound (6) and Shoot (7) as **placeholder art / TODO** in a comment near the enum and in your summary — do not invent clips.
- **Transitions:** drive everything from `State` via **Any State → each state**, condition `State Equals <n>`, `Has Exit Time = false`, `Transition Duration = 0` (or a small fixed 0.05s), `Can Transition To Self = false`. Default state = Idle.
- The `.controller` is a YAML asset. Write it directly. Keep the existing controller's `m_Name: Player` and its `.meta` GUID intact so the prefab reference doesn't break. Verify the prefab still points at it.

### 4. SFX (jump/land/footsteps)
`JumpStateBehaviour` and `PlaySoundBehaviour` read params being removed (`JumpUp`, etc.). Either:
- Re-home jump/land SFX into `PlayerAnimator` (e.g., play a jump sound when `State` transitions into `Jump`, land sound on `Jump/Fall → grounded`), **or**
- Attach the StateMachineBehaviours to the new Jump/Fall/Land states without the removed-param gating.

Pick the simpler one, keep the existing `AudioClip` assignments working, and note what you did. Do not silently drop the audio.

## Constraints
- Match existing code style (the player scripts use `[Networked]` properties, `TickTimer`, XML doc comments — mirror that).
- All gameplay/state decisions run on **state authority**; all visual application runs in **`Render()` on every client**. No `SetTrigger` anywhere.
- Don't add features not listed (no wall-slide, no directional-melee split, no land-recovery state) unless you flag them as optional follow-ups.

## Verification before claiming done
1. `grep -rn "SetTrigger\|SetBool\|SetFloat" Assets/Scripts/Player` returns **no animation calls** outside `PlayerAnimator` (PlayerAnimator should use only `SetInteger`).
2. The project compiles (no missing-param Animator warnings at runtime — the only param is `State`).
3. Open `Player.controller` in the Animator window: one layer, 10 states, one Int param, Any-State transitions wired, every state bound to a real clip (no "None" motions).
4. Confirm the player prefab's `Animator.runtimeAnimatorController` still resolves to `Player.controller` and that `PlayerAnimator` is on the prefab.
5. Report exactly what you changed, which clips are placeholders (Shoot, GroundPound), and any TODOs (missing Shoot/GroundPound art).

Do not mark the task complete until items 1–4 are verified with actual command output / file inspection, not assumption.
