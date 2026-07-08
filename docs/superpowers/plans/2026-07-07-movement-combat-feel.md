# Movement & Combat Feel Pass Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace instant-velocity movement with an accel/momentum model (dash-jump carry-over, asymmetric jump gravity, apex hang, fast-fall), give melee swings startup/active/recovery phases, and add a tiered coin-carrier glow aura.

**Architecture:** Branchy math goes into engine-free pure-core static classes (`MovementMath`, `SwingPhase`, `AuraTiers`) with NUnit EditMode tests, following the repo's existing `Game.*.Core` asmdef pattern. The NetworkBehaviours (`PlayerMovement`, `PlayerCombat`) stay thin and call the pure functions per tick; all new gameplay state is `[Networked]` or derived per tick from networked state + input (resimulation-safe). The aura is a purely visual MonoBehaviour driven by the already-networked `TotalCoinValue`.

**Tech Stack:** Unity 6000.3.0f1, Photon Fusion 2 (host/server-authoritative, predicted input), NUnit EditMode tests.

**Spec:** `docs/superpowers/specs/2026-07-07-movement-combat-feel-design.md`

## Global Constraints

- Simulation code must be resimulation-safe: gameplay state is `[Networked]` or a pure per-tick function of networked state + input. No plain fields that affect gameplay (server-only cosmetic dedup sets are the allowed exception, per existing `dashStruck` pattern).
- Damage and hit detection run under `HasStateAuthority` only.
- Button edges come from `PlayerController`'s `PreviousButtons` (`GetPressed`/`GetReleased`); never read input devices outside `NetworkInputProvider`.
- Pure-core asmdefs have `"noEngineReferences": true` — **no `using UnityEngine`** in core classes (use `System.Math`). Mirror `Game.Combat.Core` asmdef settings exactly.
- Every new asset file needs a manually created `.meta` (the editor only generates them on focus). Templates in Appendix B.
- The Unity editor may hold the project lock. Verification procedure (compile gate + test harness fallback) in Appendix A.
- Do not touch: `NetworkTransform` (never used in this project), dash-strike buff path (`dashStruck`), `HitCooldownLedger`, `RPC_HitFeedback`, projectile code.
- Defaults are authored in ticks at the Fusion simulation tick rate; per-second values convert via `Runner.DeltaTime`.

---

### Task 1: `Game.PlayerMovement.Core` assembly + `StepHorizontalVelocity`

**Files:**
- Create: `Assets/Scripts/Player/Movement/Core/Game.PlayerMovement.Core.asmdef` (+ folder `Assets/Scripts/Player/Movement/Core/`, + `.meta` files per Appendix B)
- Create: `Assets/Scripts/Player/Movement/Core/MovementMath.cs` (+ `.meta`)
- Create: `Assets/Tests/EditMode/PlayerMovement/Game.PlayerMovement.Tests.asmdef` (+ folder, + `.meta` files)
- Test: `Assets/Tests/EditMode/PlayerMovement/MovementMathTests.cs` (+ `.meta`)

**Interfaces:**
- Consumes: nothing (pure core).
- Produces: `Game.PlayerMovement.Core.MoveParams` (struct: `float WalkSpeed, AccelPerTick, DecelPerTick, MomentumDecayPerTick`) and `Game.PlayerMovement.Core.MovementMath.StepHorizontalVelocity(float currentVx, int inputDir, in MoveParams p) -> float`. Task 3 calls this from `PlayerMovement`.

- [ ] **Step 1: Create the assembly scaffolding**

`Assets/Scripts/Player/Movement/Core/Game.PlayerMovement.Core.asmdef` (mirrors `Game.Combat.Core`):

```json
{
    "name": "Game.PlayerMovement.Core",
    "rootNamespace": "Game.PlayerMovement.Core",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
```

`Assets/Tests/EditMode/PlayerMovement/Game.PlayerMovement.Tests.asmdef` (mirrors `Game.Combat.Tests`):

```json
{
    "name": "Game.PlayerMovement.Tests",
    "rootNamespace": "",
    "references": [
        "Game.PlayerMovement.Core",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [ "Editor" ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [ "nunit.framework.dll" ],
    "autoReferenced": false,
    "defineConstraints": [ "UNITY_INCLUDE_TESTS" ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

Create `.meta` files for both new folders and both asmdefs (Appendix B templates; fresh random GUIDs).

- [ ] **Step 2: Write the failing tests**

`Assets/Tests/EditMode/PlayerMovement/MovementMathTests.cs`:

```csharp
using NUnit.Framework;
using Game.PlayerMovement.Core;

public class MovementMathTests
{
    // walkSpeed 5, accel 1.25/tick (4 ticks), decel ~1.667/tick (3 ticks), momentum decay 0.5/tick
    private static MoveParams Ground() => new MoveParams
    {
        WalkSpeed = 5f, AccelPerTick = 1.25f, DecelPerTick = 5f / 3f, MomentumDecayPerTick = 0.5f
    };

    [Test]
    public void Accelerates_Toward_Target_By_AccelPerTick()
    {
        float vx = MovementMath.StepHorizontalVelocity(0f, 1, Ground());
        Assert.AreEqual(1.25f, vx, 1e-4f);
    }

    [Test]
    public void Arrives_Exactly_At_Target_Without_Overshoot()
    {
        float vx = MovementMath.StepHorizontalVelocity(4.5f, 1, Ground());
        Assert.AreEqual(5f, vx, 1e-4f);
    }

    [Test]
    public void Decelerates_To_Zero_With_No_Input()
    {
        float vx = MovementMath.StepHorizontalVelocity(1f, 0, Ground());
        Assert.AreEqual(0f, vx, 1e-4f);
    }

    [Test]
    public void Counter_Input_Brakes_Through_Zero_At_Accel_Rate()
    {
        float vx = MovementMath.StepHorizontalVelocity(0.5f, -1, Ground());
        Assert.AreEqual(-0.75f, vx, 1e-4f);
    }

    [Test]
    public void OverSpeed_SameDirection_Input_Bleeds_By_MomentumDecay_Only()
    {
        float vx = MovementMath.StepHorizontalVelocity(9.75f, 1, Ground());
        Assert.AreEqual(9.25f, vx, 1e-4f);
    }

    [Test]
    public void OverSpeed_Neutral_Input_Bleeds_By_MomentumDecay_Only()
    {
        float vx = MovementMath.StepHorizontalVelocity(9.75f, 0, Ground());
        Assert.AreEqual(9.25f, vx, 1e-4f);
    }

    [Test]
    public void OverSpeed_Bleed_Floors_At_WalkSpeed()
    {
        float vx = MovementMath.StepHorizontalVelocity(5.2f, 1, Ground());
        Assert.AreEqual(5f, vx, 1e-4f);
    }

    [Test]
    public void OverSpeed_Counter_Input_Brakes_At_Accel_Rate_On_Full_Velocity()
    {
        // Braking is always effective: normal MoveToward, not the gentle momentum bleed.
        float vx = MovementMath.StepHorizontalVelocity(9.75f, -1, Ground());
        Assert.AreEqual(8.5f, vx, 1e-4f);
    }

    [Test]
    public void Negative_Direction_Is_Symmetric()
    {
        float vx = MovementMath.StepHorizontalVelocity(-9.75f, -1, Ground());
        Assert.AreEqual(-9.25f, vx, 1e-4f);

        vx = MovementMath.StepHorizontalVelocity(-0.5f, 1, Ground());
        Assert.AreEqual(0.75f, vx, 1e-4f);
    }

    [Test]
    public void At_Exactly_WalkSpeed_With_Input_Holds_WalkSpeed()
    {
        float vx = MovementMath.StepHorizontalVelocity(5f, 1, Ground());
        Assert.AreEqual(5f, vx, 1e-4f);
    }
}
```

Create the `.meta` for the test file.

- [ ] **Step 3: Run tests to verify they fail**

Follow Appendix A. Expected: compile error "MovementMath not defined" (or all tests fail if using the harness).

- [ ] **Step 4: Write the implementation**

`Assets/Scripts/Player/Movement/Core/MovementMath.cs`:

```csharp
namespace Game.PlayerMovement.Core
{
    /// <summary>Per-tick horizontal movement parameters, already converted to per-tick units
    /// by the caller (PlayerMovement picks the ground or air set each tick).</summary>
    public struct MoveParams
    {
        public float WalkSpeed;
        public float AccelPerTick;          // rate toward target while input is held
        public float DecelPerTick;          // rate toward zero with no input
        public float MomentumDecayPerTick;  // bleed rate of speed ABOVE WalkSpeed
    }

    /// <summary>
    /// Pure, engine-free movement math (no UnityEngine — this asmdef has noEngineReferences).
    /// Called every simulation tick by PlayerMovement; must stay a pure function of its inputs
    /// so prediction and resimulation agree.
    /// </summary>
    public static class MovementMath
    {
        /// <summary>
        /// Moves currentVx toward inputDir * WalkSpeed. Speed above WalkSpeed (dash carry-over)
        /// is never clamped instantly: with input along travel or neutral, only the excess bleeds
        /// off at MomentumDecayPerTick; counter-input always brakes at the normal rate.
        /// </summary>
        public static float StepHorizontalVelocity(float currentVx, int inputDir, in MoveParams p)
        {
            float speed = System.Math.Abs(currentVx);
            float velDir = currentVx >= 0f ? 1f : -1f;

            // Over-speed momentum rule (spec 1.2).
            if (speed > p.WalkSpeed && (inputDir == 0 || inputDir * velDir > 0f))
            {
                float newSpeed = System.Math.Max(p.WalkSpeed, speed - p.MomentumDecayPerTick);
                return velDir * newSpeed;
            }

            float target = inputDir * p.WalkSpeed;
            float rate = inputDir != 0 ? p.AccelPerTick : p.DecelPerTick;
            return MoveToward(currentVx, target, rate);
        }

        /// <summary>Engine-free Mathf.MoveTowards equivalent.</summary>
        public static float MoveToward(float current, float target, float maxDelta)
        {
            float delta = target - current;
            if (System.Math.Abs(delta) <= maxDelta) return target;
            return current + (delta > 0f ? maxDelta : -maxDelta);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Follow Appendix A. Expected: all 10 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/Player/Movement" "Assets/Tests/EditMode/PlayerMovement"
git commit -m "feat(movement): add engine-free MovementMath horizontal accel/momentum model"
```

---

### Task 2: Gravity, fast-fall, and terminal-clamp helpers

**Files:**
- Modify: `Assets/Scripts/Player/Movement/Core/MovementMath.cs`
- Test: `Assets/Tests/EditMode/PlayerMovement/MovementMathTests.cs` (append)

**Interfaces:**
- Consumes: Task 1's `MovementMath` class (adds static methods to it).
- Produces: `MovementMath.SelectGravityMultiplier(bool grounded, float vy, float apexThreshold, bool jumping, bool jumpCut, bool fastFalling, float apexMultiplier, float fallMultiplier) -> float`; `MovementMath.ShouldStartFastFall(bool grounded, bool downPressed, float vy, float apexThreshold, bool alreadyFastFalling) -> bool`; `MovementMath.ClampFallSpeed(float vy, float maxFallSpeed) -> float`. Task 4 calls all three from `PlayerMovement`.

- [ ] **Step 1: Write the failing tests** (append to `MovementMathTests.cs`)

```csharp
    // ---- Gravity / fast-fall (apexThreshold 1.5, apexMult 0.5, fallMult 1.7) ----

    [Test]
    public void Gravity_Grounded_Is_Neutral()
    {
        Assert.AreEqual(1f, MovementMath.SelectGravityMultiplier(
            grounded: true, vy: 0f, apexThreshold: 1.5f,
            jumping: false, jumpCut: false, fastFalling: false,
            apexMultiplier: 0.5f, fallMultiplier: 1.7f), 1e-4f);
    }

    [Test]
    public void Gravity_Rising_Is_Neutral()
    {
        Assert.AreEqual(1f, MovementMath.SelectGravityMultiplier(
            false, 8f, 1.5f, true, false, false, 0.5f, 1.7f), 1e-4f);
    }

    [Test]
    public void Gravity_Rising_After_JumpCut_Uses_FallMultiplier()
    {
        Assert.AreEqual(1.7f, MovementMath.SelectGravityMultiplier(
            false, 8f, 1.5f, true, true, false, 0.5f, 1.7f), 1e-4f);
    }

    [Test]
    public void Gravity_Apex_Window_While_Jumping_Hangs()
    {
        Assert.AreEqual(0.5f, MovementMath.SelectGravityMultiplier(
            false, 0.5f, 1.5f, true, false, false, 0.5f, 1.7f), 1e-4f);
        Assert.AreEqual(0.5f, MovementMath.SelectGravityMultiplier(
            false, -1.4f, 1.5f, true, false, false, 0.5f, 1.7f), 1e-4f);
    }

    [Test]
    public void Gravity_Apex_Window_Not_Jumping_Falls()
    {
        // Walking off a ledge (vy ~0, Jumping false) must NOT hang.
        Assert.AreEqual(1.7f, MovementMath.SelectGravityMultiplier(
            false, 0f, 1.5f, false, false, false, 0.5f, 1.7f), 1e-4f);
    }

    [Test]
    public void Gravity_Apex_Window_FastFalling_Falls()
    {
        Assert.AreEqual(1.7f, MovementMath.SelectGravityMultiplier(
            false, -1f, 1.5f, true, false, true, 0.5f, 1.7f), 1e-4f);
    }

    [Test]
    public void Gravity_Below_Apex_Window_Falls()
    {
        Assert.AreEqual(1.7f, MovementMath.SelectGravityMultiplier(
            false, -3f, 1.5f, true, false, false, 0.5f, 1.7f), 1e-4f);
    }

    [Test]
    public void FastFall_Starts_Airborne_Past_Apex_On_Down()
    {
        Assert.IsTrue(MovementMath.ShouldStartFastFall(
            grounded: false, downPressed: true, vy: 0.5f, apexThreshold: 1.5f, alreadyFastFalling: false));
    }

    [Test]
    public void FastFall_Does_Not_Start_While_Rising_Fast()
    {
        Assert.IsFalse(MovementMath.ShouldStartFastFall(false, true, 8f, 1.5f, false));
    }

    [Test]
    public void FastFall_Does_Not_Start_Grounded_Or_Twice()
    {
        Assert.IsFalse(MovementMath.ShouldStartFastFall(true, true, 0f, 1.5f, false));
        Assert.IsFalse(MovementMath.ShouldStartFastFall(false, true, -5f, 1.5f, true));
    }

    [Test]
    public void ClampFallSpeed_Limits_Downward_Only()
    {
        Assert.AreEqual(-20f, MovementMath.ClampFallSpeed(-35f, 20f), 1e-4f);
        Assert.AreEqual(-5f, MovementMath.ClampFallSpeed(-5f, 20f), 1e-4f);
        Assert.AreEqual(12f, MovementMath.ClampFallSpeed(12f, 20f), 1e-4f);
    }
```

- [ ] **Step 2: Run tests to verify the new ones fail**

Follow Appendix A. Expected: compile error "SelectGravityMultiplier not defined".

- [ ] **Step 3: Implement** (append inside `MovementMath` class)

```csharp
        /// <summary>
        /// Gravity-scale multiplier for the jump arc (spec 1.4). Rising = neutral; small |vy| near
        /// the top of an actual jump = apex hang; everything else = heavier fall. A jump-cut or
        /// fast-fall disqualifies the hang; walking off a ledge (jumping=false) never hangs.
        /// </summary>
        public static float SelectGravityMultiplier(
            bool grounded, float vy, float apexThreshold,
            bool jumping, bool jumpCut, bool fastFalling,
            float apexMultiplier, float fallMultiplier)
        {
            if (grounded) return 1f;
            if (vy > apexThreshold) return jumpCut ? fallMultiplier : 1f;
            bool apexEligible = jumping && !jumpCut && !fastFalling && vy > -apexThreshold;
            return apexEligible ? apexMultiplier : fallMultiplier;
        }

        /// <summary>Fast-fall trigger (spec 1.5): airborne, down pressed (edge), at/past the apex,
        /// not already fast-falling.</summary>
        public static bool ShouldStartFastFall(
            bool grounded, bool downPressed, float vy, float apexThreshold, bool alreadyFastFalling)
        {
            return !grounded && downPressed && !alreadyFastFalling && vy <= apexThreshold;
        }

        /// <summary>Terminal velocity: clamps downward speed only.</summary>
        public static float ClampFallSpeed(float vy, float maxFallSpeed)
        {
            return vy < -maxFallSpeed ? -maxFallSpeed : vy;
        }
```

- [ ] **Step 4: Run tests to verify all pass**

Follow Appendix A. Expected: all 21 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Player/Movement" "Assets/Tests/EditMode/PlayerMovement"
git commit -m "feat(movement): add gravity-arc, fast-fall, and terminal-clamp helpers"
```

---

### Task 3: PlayerStats accel fields + PlayerMovement horizontal integration + dash-jump carry

**Files:**
- Modify: `Assets/Scripts/Player/PlayerStats.cs`
- Modify: `Assets/Scripts/Player/PlayerMovement.cs`

**Interfaces:**
- Consumes: `MovementMath.StepHorizontalVelocity` / `MoveParams` (Task 1).
- Produces: no new public API. Behaviour change only. (Task 6 later adds a dash gate + `IsFacingRight()` here.)

- [ ] **Step 1: Add fields to PlayerStats**

In `PlayerStats.cs`, after the existing `dashCooldown` field, add:

```csharp
    [Header("Acceleration (ticks to reach walkSpeed)")]
    public int groundAccelTicks = 4;
    public int groundDecelTicks = 3;
    public int airAccelTicks = 10;
    public int airDecelTicks = 18;

    [Header("Momentum (decay of speed above walkSpeed, units/s^2)")]
    public float momentumDecayAir = 8f;
    public float momentumDecayGround = 40f;

    [Header("Dash-Jump")]
    [Range(0f, 1f)] public float dashJumpCarryFactor = 0.65f;
```

(Existing `PlayerStats` assets pick up these script defaults automatically; no asset edit needed.)

- [ ] **Step 2: Replace the horizontal-velocity write in PlayerMovement.Simulate**

Add `using Game.PlayerMovement.Core;` at the top of `PlayerMovement.cs`.

Replace the current `else` branch of the horizontal-velocity section (`rb.linearVelocity = new Vector2(input.Horizontal * stats.walkSpeed, rb.linearVelocity.y);`) with:

```csharp
        else
        {
            var p = new MoveParams
            {
                WalkSpeed = stats.walkSpeed,
                AccelPerTick = stats.walkSpeed /
                    System.Math.Max(1, grounded ? stats.groundAccelTicks : stats.airAccelTicks),
                DecelPerTick = stats.walkSpeed /
                    System.Math.Max(1, grounded ? stats.groundDecelTicks : stats.airDecelTicks),
                MomentumDecayPerTick =
                    (grounded ? stats.momentumDecayGround : stats.momentumDecayAir) * Runner.DeltaTime,
            };
            float newVx = MovementMath.StepHorizontalVelocity(rb.linearVelocity.x, input.Horizontal, p);
            rb.linearVelocity = new Vector2(newVx, rb.linearVelocity.y);
        }
```

Note: `EndDash()` already leaves velocity untouched — with the direct write gone, dash-exit velocity now survives and the over-speed rule decays it. No change needed there.

- [ ] **Step 3: Add the dash-jump carry**

In the jump-buffer section of `Simulate`, replace:

```csharp
        if (!stunned && pressed.IsSet((int)PlayerButton.Jump))
        {
            JumpBufferCounter = jumpBufferTicks;
            if (Dashing) EndDash(); // jump cancels dash
        }
```

with:

```csharp
        if (!stunned && pressed.IsSet((int)PlayerButton.Jump))
        {
            JumpBufferCounter = jumpBufferTicks;
            if (Dashing)
            {
                // Dash-jump (spec 1.3): cancel the dash and carry a fraction of dash speed
                // into the jump. DashDir is networked and still valid after EndDash.
                EndDash();
                rb.linearVelocity = new Vector2(
                    DashDir * stats.dashSpeed * stats.dashJumpCarryFactor, rb.linearVelocity.y);
            }
        }
```

(`DoJump` only writes `vy`, so the carried `vx` survives; next tick the over-speed rule takes over.)

- [ ] **Step 4: Compile gate**

Follow Appendix A compile gate. Expected: zero errors.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Player/PlayerStats.cs" "Assets/Scripts/Player/PlayerMovement.cs"
git commit -m "feat(movement): accel/momentum horizontal model + dash-jump carry-over"
```

---

### Task 4: Down button + jump-arc integration (gravity, fast-fall, terminal clamp)

**Files:**
- Modify: `Assets/Scripts/Player/NetInput.cs`
- Modify: `Assets/Scripts/Player/NetworkInputProvider.cs`
- Modify: `Assets/Scripts/Player/PlayerStats.cs`
- Modify: `Assets/Scripts/Player/PlayerMovement.cs`

**Interfaces:**
- Consumes: `MovementMath.SelectGravityMultiplier`, `ShouldStartFastFall`, `ClampFallSpeed` (Task 2).
- Produces: `PlayerButton.Down = 5` (button index; other systems may read it later).

- [ ] **Step 1: Add the Down button**

In `NetInput.cs`, extend the enum:

```csharp
public enum PlayerButton
{
    Jump = 0,
    Dash = 1,
    Melee = 2,
    Shoot = 3,
    Stealth = 4,
    Down = 5,     // held while pressing down (fast-fall edge detection via PreviousButtons)
}
```

In `NetworkInputProvider.OnInput`, after the existing `data.Buttons.Set(...)` lines, add:

```csharp
        // Down (held state; edge detection happens in PlayerController via PreviousButtons).
        // No tap latch needed: fast-fall is a hold-style input, not a tap.
        bool down = (keyboard != null && (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed))
                    || (gamepad != null && gamepad.leftStick.ReadValue().y < -0.5f);
        data.Buttons.Set((int)PlayerButton.Down, down);
```

- [ ] **Step 2: Add jump-arc fields to PlayerStats**

After the dash-jump fields added in Task 3:

```csharp
    [Header("Jump Arc")]
    public float fallGravityMult = 1.7f;
    public float apexGravityMult = 0.5f;
    [Tooltip("Vertical speed band around the jump apex that gets the apex-hang gravity")]
    public float apexThreshold = 1.5f;
    public float fastFallSpeed = 18f;
    public float maxFallSpeed = 20f;
```

- [ ] **Step 3: Integrate into PlayerMovement.Simulate**

Add the networked flag next to the other `[Networked]` properties:

```csharp
    [Networked] private NetworkBool FastFalling { get; set; }
```

Replace the gravity line (`rb.gravityScale = Dashing ? 0f : baseGravity;`) with:

```csharp
        // Gravity is a pure function of networked state + velocity (resimulation-safe).
        if (grounded) FastFalling = false;
        float gravityMult = MovementMath.SelectGravityMultiplier(
            grounded, rb.linearVelocity.y, stats.apexThreshold,
            Jumping, JumpCut, FastFalling,
            stats.apexGravityMult, stats.fallGravityMult);
        rb.gravityScale = Dashing ? 0f : baseGravity * gravityMult;
```

After the variable-jump-height (jump-cut) block, add fast-fall + terminal clamp as the LAST velocity writes in `Simulate`:

```csharp
        // ---- Fast-fall (spec 1.5): down pressed at/past the apex snaps to fast-fall speed ----
        if (!stunned && !Dashing && pressed.IsSet((int)PlayerButton.Down) &&
            MovementMath.ShouldStartFastFall(grounded, true, rb.linearVelocity.y,
                                             stats.apexThreshold, FastFalling))
        {
            FastFalling = true;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, -stats.fastFallSpeed);
        }

        // ---- Terminal velocity ----
        rb.linearVelocity = new Vector2(
            rb.linearVelocity.x,
            MovementMath.ClampFallSpeed(rb.linearVelocity.y, stats.maxFallSpeed));
```

- [ ] **Step 4: Compile gate**

Follow Appendix A compile gate. Expected: zero errors.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Player/NetInput.cs" "Assets/Scripts/Player/NetworkInputProvider.cs" "Assets/Scripts/Player/PlayerStats.cs" "Assets/Scripts/Player/PlayerMovement.cs"
git commit -m "feat(movement): asymmetric gravity + apex hang + fast-fall + terminal clamp"
```

---

### Task 5: `SwingPhase` in Game.Combat.Core

**Files:**
- Create: `Assets/Scripts/Combat/Core/SwingPhase.cs` (+ `.meta`, Appendix B)
- Test: `Assets/Tests/EditMode/Combat/SwingPhaseTests.cs` (+ `.meta`)

**Interfaces:**
- Consumes: nothing (pure core; lives in existing `Game.Combat.Core` asmdef — no asmdef changes needed, tests asmdef already references it).
- Produces: `Game.Combat.Core.SwingPhaseKind` (enum `None, Startup, Active, Recovery`); `SwingPhase.Resolve(int currentTick, int attackStartTick, int startupTicks, int activeTicks, int recoveryTicks) -> SwingPhaseKind`; `SwingPhase.IsFirstActiveTick(int currentTick, int attackStartTick, int startupTicks) -> bool`. Task 6 calls both from `PlayerCombat`.

- [ ] **Step 1: Write the failing tests**

`Assets/Tests/EditMode/Combat/SwingPhaseTests.cs`:

```csharp
using NUnit.Framework;
using Game.Combat.Core;

public class SwingPhaseTests
{
    // startup 3, active 5, recovery 10; swing starts at tick 100
    private static SwingPhaseKind At(int tick) => SwingPhase.Resolve(tick, 100, 3, 5, 10);

    [Test]
    public void No_Swing_When_StartTick_Unset()
    {
        Assert.AreEqual(SwingPhaseKind.None, SwingPhase.Resolve(100, 0, 3, 5, 10));
    }

    [Test]
    public void Before_Start_Is_None()
    {
        Assert.AreEqual(SwingPhaseKind.None, At(99));
    }

    [Test]
    public void Startup_Window_Is_Exact()
    {
        Assert.AreEqual(SwingPhaseKind.Startup, At(100));
        Assert.AreEqual(SwingPhaseKind.Startup, At(102));
    }

    [Test]
    public void Active_Window_Is_Exact()
    {
        Assert.AreEqual(SwingPhaseKind.Active, At(103));
        Assert.AreEqual(SwingPhaseKind.Active, At(107));
    }

    [Test]
    public void Recovery_Window_Is_Exact()
    {
        Assert.AreEqual(SwingPhaseKind.Recovery, At(108));
        Assert.AreEqual(SwingPhaseKind.Recovery, At(117));
    }

    [Test]
    public void After_Recovery_Is_None()
    {
        Assert.AreEqual(SwingPhaseKind.None, At(118));
    }

    [Test]
    public void Zero_Startup_Is_Active_Immediately()
    {
        Assert.AreEqual(SwingPhaseKind.Active, SwingPhase.Resolve(100, 100, 0, 5, 10));
    }

    [Test]
    public void FirstActiveTick_Detects_Exactly_One_Tick()
    {
        Assert.IsFalse(SwingPhase.IsFirstActiveTick(102, 100, 3));
        Assert.IsTrue(SwingPhase.IsFirstActiveTick(103, 100, 3));
        Assert.IsFalse(SwingPhase.IsFirstActiveTick(104, 100, 3));
        Assert.IsFalse(SwingPhase.IsFirstActiveTick(103, 0, 3)); // no swing
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Follow Appendix A. Expected: compile error "SwingPhase not defined".

- [ ] **Step 3: Implement**

`Assets/Scripts/Combat/Core/SwingPhase.cs`:

```csharp
namespace Game.Combat.Core
{
    /// <summary>Melee swing phase (spec Part 2): Startup -> Active -> Recovery -> None.</summary>
    public enum SwingPhaseKind { None, Startup, Active, Recovery }

    /// <summary>
    /// Pure, engine-free swing-phase derivation. The swing is fully described by its networked
    /// start tick + tick-count windows; deriving the phase per tick (instead of storing it)
    /// makes the whole system resimulation-proof.
    /// </summary>
    public static class SwingPhase
    {
        public static SwingPhaseKind Resolve(int currentTick, int attackStartTick,
                                             int startupTicks, int activeTicks, int recoveryTicks)
        {
            if (attackStartTick <= 0) return SwingPhaseKind.None;
            int elapsed = currentTick - attackStartTick;
            if (elapsed < 0) return SwingPhaseKind.None;
            if (elapsed < startupTicks) return SwingPhaseKind.Startup;
            if (elapsed < startupTicks + activeTicks) return SwingPhaseKind.Active;
            if (elapsed < startupTicks + activeTicks + recoveryTicks) return SwingPhaseKind.Recovery;
            return SwingPhaseKind.None;
        }

        /// <summary>True exactly on the first Active tick (used for the ground-pound impulse).</summary>
        public static bool IsFirstActiveTick(int currentTick, int attackStartTick, int startupTicks)
        {
            return attackStartTick > 0 && currentTick - attackStartTick == startupTicks;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Follow Appendix A. Expected: all 8 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Combat/Core/SwingPhase.cs" "Assets/Scripts/Combat/Core/SwingPhase.cs.meta" "Assets/Tests/EditMode/Combat/SwingPhaseTests.cs" "Assets/Tests/EditMode/Combat/SwingPhaseTests.cs.meta"
git commit -m "feat(combat): add engine-free SwingPhase derivation"
```

---

### Task 6: PlayerCombat swing-phase integration + dash gate

**Files:**
- Modify: `Assets/Scripts/Player/PlayerStats.cs`
- Modify: `Assets/Scripts/Player/PlayerCombat.cs`
- Modify: `Assets/Scripts/Player/PlayerMovement.cs`

**Interfaces:**
- Consumes: `SwingPhase.Resolve` / `IsFirstActiveTick` / `SwingPhaseKind` (Task 5).
- Produces: `PlayerCombat.IsSwingCommitted -> bool` (property; true for the whole Startup/Active/Recovery span) and `PlayerMovement.IsFacingRight() -> bool`.

- [ ] **Step 1: Add swing-phase fields to PlayerStats**

After the existing `attackCooldown`:

```csharp
    [Header("Melee Swing Phases (ticks)")]
    public int attackStartupTicks = 3;
    public int attackActiveTicks = 5;
    public int attackRecoveryTicks = 10;
```

- [ ] **Step 2: Add the facing accessor to PlayerMovement**

Next to the other public accessors (`IsDashing()` etc.):

```csharp
    public bool IsFacingRight() => FacingRight;
```

- [ ] **Step 3: Rework PlayerCombat to the phase model**

Add `using Game.Combat.Core;` to the top of `PlayerCombat.cs` (it is NOT currently there — the file's usings are `System.Collections.Generic`, `UnityEngine`, `Fusion`).

Add networked swing state + the per-swing dedup set next to the existing timers:

```csharp
    // Swing state (spec 2.2): the swing is its start tick + latched aim/facing; the phase is
    // derived per tick via SwingPhase.Resolve, so it predicts and resimulates correctly.
    [Networked] private int AttackStartTick { get; set; }
    [Networked] private int AttackAim { get; set; }
    [Networked] private NetworkBool AttackFacingRight { get; set; }
    [Networked] private NetworkBool AttackIsPound { get; set; }

    // Per-swing hit dedup: server-only, non-networked (same pattern as dashStruck).
    private readonly HashSet<Collider2D> swingStruck = new HashSet<Collider2D>();
```

Replace the melee section of `Simulate` (the `pressed.IsSet(Melee)` block) with:

```csharp
        SwingPhaseKind phase = CurrentSwingPhase();

        if (pressed.IsSet((int)PlayerButton.Melee) && phase == SwingPhaseKind.None &&
            AttackCooldownTimer.ExpiredOrNotRunning(Runner))
        {
            AttackCooldownTimer = TickTimer.CreateFromSeconds(Runner, stats.attackCooldown);
            BeginSwing();
            phase = CurrentSwingPhase(); // now Startup (or Active if startupTicks is 0)
        }

        if (phase == SwingPhaseKind.Active || phase == SwingPhaseKind.Startup)
        {
            SimulateSwingTick(phase);
        }
```

Delete the old `Attack()` method and add:

```csharp
    private SwingPhaseKind CurrentSwingPhase()
    {
        return SwingPhase.Resolve(Runner.Tick, AttackStartTick,
            stats.attackStartupTicks, stats.attackActiveTicks, stats.attackRecoveryTicks);
    }

    /// <summary>True while a swing owns the player's offense (Startup/Active/Recovery).
    /// PlayerMovement reads this to block dash starts (spec 2.3).</summary>
    public bool IsSwingCommitted => CurrentSwingPhase() != SwingPhaseKind.None;

    /// <summary>Latch a new swing: start tick + aim/facing/pound-ness frozen at commit
    /// (spec 2.2 — the swing never flips mid-animation). Runs on state authority and the
    /// predicting input authority, like the old Attack().</summary>
    private void BeginSwing()
    {
        AttackStartTick = Runner.Tick;
        AttackAim = verticalAim;
        AttackFacingRight = playerMovement != null ? playerMovement.IsFacingRight()
                                                   : transform.localScale.x >= 0f;

        bool isGrounded = groundCheck != null &&
                          Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        AttackIsPound = verticalAim < 0 && !isGrounded && downAttackPoint != null;

        swingStruck.Clear();

        if (playerAnimator != null)
        {
            if (AttackIsPound) playerAnimator.TriggerGroundPound();
            else playerAnimator.TriggerAttack();
        }
    }

    /// <summary>Per-tick swing behaviour. Pound impulse fires exactly once on the first Active
    /// tick (predicted + authoritative, like the old press-time write). Hit detection runs on
    /// every Active tick, server-only, at most one hit per target per swing (swingStruck).</summary>
    private void SimulateSwingTick(SwingPhaseKind phase)
    {
        if (useGroundPound && AttackIsPound &&
            SwingPhase.IsFirstActiveTick(Runner.Tick, AttackStartTick, stats.attackStartupTicks))
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, -groundPoundForce);
        }

        if (phase != SwingPhaseKind.Active) return;
        if (!HasStateAuthority) return;
        if (sideAttackPoint == null) return; // parity with old Attack()'s null guard

        ResolveSwingBox(out Vector2 center, out Vector2 area);
        ApplyMeleeHits(center, area, spawnHitMarkers: true, swingStruck);
    }

    /// <summary>Hitbox from the LATCHED aim/facing. The attack-point children flip with
    /// localScale (current facing); if facing changed mid-swing, mirror the offset back
    /// to the facing latched at commit.</summary>
    private void ResolveSwingBox(out Vector2 center, out Vector2 area)
    {
        Transform point = sideAttackPoint;
        area = sideAttackArea;

        if (AttackAim > 0 && upAttackPoint != null)
        {
            point = upAttackPoint;
            area = upAttackArea;
        }
        else if (AttackIsPound)
        {
            point = downAttackPoint;
            area = downAttackArea;
        }
        // (AttackAim < 0 while grounded falls through to the side box, matching old behaviour.)

        Vector2 offset = (Vector2)point.position - (Vector2)transform.position;
        bool facingRightNow = transform.localScale.x >= 0f;
        if (facingRightNow != (bool)AttackFacingRight) offset.x = -offset.x;
        center = (Vector2)transform.position + offset;
    }
```

Notes for the implementer:
- `verticalAim`, `playerMovement`, `rb`, `groundCheck`, `groundCheckRadius`, `groundLayer`, `useGroundPound`, `groundPoundForce`, and all attack points/areas already exist as fields in this file.
- `ApplyMeleeHits` already accepts an `alreadyHit` set and spawns hit markers only for dedup-passed targets — no changes to it.
- The dash-strike block at the bottom of `Simulate` (Quicker Dash T3) stays byte-identical.

- [ ] **Step 4: Gate dash starts on swing commitment in PlayerMovement**

Add a combat reference:

```csharp
    private PlayerCombat combat;
```

In `Spawned()`, after `mods = GetComponent<PlayerStatModifiers>();`:

```csharp
        combat = GetComponent<PlayerCombat>();
```

Extend the dash-start condition:

```csharp
        if (!stunned && pressed.IsSet((int)PlayerButton.Dash) && !Dashing &&
            DashCooldownTimer.ExpiredOrNotRunning(Runner) &&
            (combat == null || !combat.IsSwingCommitted))
```

- [ ] **Step 5: Compile gate**

Follow Appendix A compile gate. Expected: zero errors.

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/Player/PlayerStats.cs" "Assets/Scripts/Player/PlayerCombat.cs" "Assets/Scripts/Player/PlayerMovement.cs"
git commit -m "feat(combat): melee startup/active/recovery phases with latched aim + dash gate"
```

---

### Task 7: `AuraTiers` in Game.Hud.Core

**Files:**
- Create: `Assets/Scripts/Hud/Core/AuraTiers.cs` (+ `.meta`, Appendix B)
- Test: `Assets/Tests/EditMode/Hud/AuraTiersTests.cs` (+ `.meta`)

**Interfaces:**
- Consumes: nothing (pure core; existing `Game.Hud.Core` asmdef, existing `Game.Hud.Tests` asmdef already references it).
- Produces: `Game.Hud.Core.AuraTiers.Resolve(int totalValue, int[] thresholds) -> int` (0 = off, 1..N = highest threshold crossed). Task 8 calls this from `CoinCarrierAura`.

- [ ] **Step 1: Write the failing tests**

`Assets/Tests/EditMode/Hud/AuraTiersTests.cs`:

```csharp
using NUnit.Framework;
using Game.Hud.Core;

public class AuraTiersTests
{
    private static readonly int[] Thresholds = { 5, 15, 30 };

    [Test]
    public void Below_First_Threshold_Is_Tier_Zero()
    {
        Assert.AreEqual(0, AuraTiers.Resolve(0, Thresholds));
        Assert.AreEqual(0, AuraTiers.Resolve(4, Thresholds));
    }

    [Test]
    public void Thresholds_Are_Inclusive()
    {
        Assert.AreEqual(1, AuraTiers.Resolve(5, Thresholds));
        Assert.AreEqual(2, AuraTiers.Resolve(15, Thresholds));
        Assert.AreEqual(3, AuraTiers.Resolve(30, Thresholds));
    }

    [Test]
    public void Between_Thresholds_Uses_Lower_Tier()
    {
        Assert.AreEqual(1, AuraTiers.Resolve(14, Thresholds));
        Assert.AreEqual(2, AuraTiers.Resolve(29, Thresholds));
    }

    [Test]
    public void Above_Top_Threshold_Caps_At_Max_Tier()
    {
        Assert.AreEqual(3, AuraTiers.Resolve(9999, Thresholds));
    }

    [Test]
    public void Null_Or_Empty_Thresholds_Is_Tier_Zero()
    {
        Assert.AreEqual(0, AuraTiers.Resolve(50, null));
        Assert.AreEqual(0, AuraTiers.Resolve(50, new int[0]));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Follow Appendix A. Expected: compile error "AuraTiers not defined".

- [ ] **Step 3: Implement**

`Assets/Scripts/Hud/Core/AuraTiers.cs`:

```csharp
namespace Game.Hud.Core
{
    /// <summary>
    /// Maps a carried coin value to a carrier-aura tier: 0 = no aura, 1..N = highest
    /// (ascending) threshold crossed, inclusive. Pure and engine-free for EditMode tests.
    /// </summary>
    public static class AuraTiers
    {
        public static int Resolve(int totalValue, int[] thresholds)
        {
            if (thresholds == null) return 0;
            int tier = 0;
            for (int i = 0; i < thresholds.Length; i++)
            {
                if (totalValue >= thresholds[i]) tier = i + 1;
            }
            return tier;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Follow Appendix A. Expected: all 5 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Hud/Core/AuraTiers.cs" "Assets/Scripts/Hud/Core/AuraTiers.cs.meta" "Assets/Tests/EditMode/Hud/AuraTiersTests.cs" "Assets/Tests/EditMode/Hud/AuraTiersTests.cs.meta"
git commit -m "feat(hud): add AuraTiers value-to-tier mapping"
```

---

### Task 8: CoinCarrierAura component + editor wiring doc

**Files:**
- Create: `Assets/Scripts/Coin Scripts/CoinCarrierAura.cs` (+ `.meta`, Appendix B)
- Create: `docs/coin-carrier-aura-wiring.md`

**Interfaces:**
- Consumes: `AuraTiers.Resolve` (Task 7); `NetworkedPlayerInventory.CoinsChanged` event + `TotalCoinValue` (existing); `PlayerBuffs.IsStealthed` (existing networked bool).
- Produces: `CoinCarrierAura` MonoBehaviour for the player prefab (wired in-editor; no code consumers).

- [ ] **Step 1: Write the component**

`Assets/Scripts/Coin Scripts/CoinCarrierAura.cs`:

```csharp
using UnityEngine;
using Game.Hud.Core;

/// <summary>
/// World-space carrier aura: a soft glow behind the body sprite whose intensity tiers up
/// with the networked TotalCoinValue (spec Part 3). PURELY VISUAL — same role split as
/// FlagCarrierMarker. Hidden while stealthed, on every viewer (a stealthed coin-runner goes
/// dark). Death needs no special case: coins drop on death -> value 0 -> tier 0 -> aura off.
/// The aura renderer is a separate child sprite sorted BEHIND the body, so it cannot fight
/// the hit-flash, death-dim, or stealth transparency (which all touch the body sprite).
/// </summary>
public class CoinCarrierAura : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Child glow SpriteRenderer, sorted behind the body sprite. See docs/coin-carrier-aura-wiring.md")]
    [SerializeField] private SpriteRenderer auraRenderer;

    [Header("Tiers (TotalCoinValue thresholds, ascending)")]
    [SerializeField] private int[] tierThresholds = { 5, 15, 30 };
    [Tooltip("Aura alpha per tier (index 0 = tier 1)")]
    [SerializeField] private float[] tierAlphas = { 0.25f, 0.45f, 0.7f };
    [Tooltip("Aura local scale per tier (index 0 = tier 1)")]
    [SerializeField] private float[] tierScales = { 1.2f, 1.5f, 1.9f };

    [Header("Pulse")]
    [SerializeField] private float pulseHz = 0.8f;
    [Tooltip("Pulse speed at the top tier")]
    [SerializeField] private float topTierPulseHz = 2f;
    [SerializeField, Range(0f, 1f)] private float pulseFraction = 0.2f;

    private NetworkedPlayerInventory inventory;
    private PlayerBuffs buffs;
    private int tier;

    private void Awake()
    {
        inventory = GetComponent<NetworkedPlayerInventory>();
        buffs = GetComponent<PlayerBuffs>();
    }

    private void OnEnable()
    {
        if (inventory != null) inventory.CoinsChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (inventory != null) inventory.CoinsChanged -= Refresh;
    }

    private void Refresh()
    {
        tier = inventory != null ? AuraTiers.Resolve(inventory.TotalCoinValue, tierThresholds) : 0;
        if (auraRenderer != null && tier > 0)
        {
            int idx = Mathf.Clamp(tier - 1, 0, tierScales.Length - 1);
            auraRenderer.transform.localScale = Vector3.one * tierScales[idx];
        }
    }

    private void Update()
    {
        if (auraRenderer == null) return;

        bool stealthed = buffs != null && buffs.IsStealthed;
        bool visible = tier > 0 && !stealthed;
        if (auraRenderer.enabled != visible) auraRenderer.enabled = visible;
        if (!visible) return;

        int idx = Mathf.Clamp(tier - 1, 0, tierAlphas.Length - 1);
        float hz = tier >= tierThresholds.Length ? topTierPulseHz : pulseHz;
        float pulse = 1f - pulseFraction * (0.5f + 0.5f * Mathf.Sin(Time.time * hz * 2f * Mathf.PI));
        Color c = auraRenderer.color;
        c.a = tierAlphas[idx] * pulse;
        auraRenderer.color = c;
    }
}
```

- [ ] **Step 2: Write the wiring doc**

`docs/coin-carrier-aura-wiring.md`:

```markdown
# Coin Carrier Aura — Editor Wiring

One-time prefab wiring for the carrier glow (code: `Assets/Scripts/Coin Scripts/CoinCarrierAura.cs`).

1. Open the PlayerPrefab.
2. Add a child GameObject named `CoinAura` under the prefab root.
   - Add a `SpriteRenderer`.
   - Sprite: `Knob` (built-in soft radial circle; search "Knob" with the type filter set to
     Sprite and "Search: All"). Any soft radial glow sprite works if art provides one later.
   - Color: warm gold `#FFC64B`, alpha irrelevant (script drives it).
   - Sorting Layer: SAME as the visible body sprite; Order in Layer: body's order MINUS 1
     (the glow must render behind the body).
   - Local position (0, 0, 0). Leave scale at 1 — the script drives it per tier.
3. Add the `CoinCarrierAura` component to the prefab ROOT (next to FlagCarrierMarker).
   - Drag the `CoinAura` SpriteRenderer into its `auraRenderer` field.
4. Defaults: thresholds 5/15/30 (TotalCoinValue), alphas 0.25/0.45/0.7, scales 1.2/1.5/1.9.
   Retune in playtests.

Verify in a two-peer session: pick up coins past a threshold -> glow appears on BOTH peers;
activate Stealth -> glow disappears for everyone; die -> coins drop and glow clears.
```

- [ ] **Step 3: Compile gate**

Follow Appendix A compile gate. Expected: zero errors.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/Coin Scripts/CoinCarrierAura.cs" "Assets/Scripts/Coin Scripts/CoinCarrierAura.cs.meta" "docs/coin-carrier-aura-wiring.md"
git commit -m "feat(ctf): tiered coin-carrier glow aura with stealth hide"
```

---

### Task 9: Full verification + spec status update

**Files:**
- Modify: `docs/superpowers/specs/2026-07-07-movement-combat-feel-design.md` (status line only)

**Interfaces:**
- Consumes: everything above.
- Produces: verified branch ready for review/merge.

- [ ] **Step 1: Full-surface compile gate**

Run the Appendix A compile gate over the whole `Assets/Scripts` surface (not just changed files). Expected: zero errors.

- [ ] **Step 2: Run the FULL EditMode suite**

If the Unity editor is closed:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -batchmode -projectPath . -runTests -testPlatform EditMode -testResults "$env:TEMP\feel-results.xml" -quit
```

Then inspect the XML: every existing suite (Buffs, Combat, EnemyAI, Hud, Net, PlayerAnimation) plus the new PlayerMovement suite must pass.

If the editor holds the project lock: run the Appendix A harness for the three new/changed core assemblies and record that the full NUnit suite still needs an in-editor run (report this honestly — do not claim the suite passed).

- [ ] **Step 3: Update the spec status**

Change the spec's `**Status:**` line to: `Implemented (this branch); in-editor multi-peer verify pending`.

- [ ] **Step 4: Commit**

```bash
git add "docs/superpowers/specs/2026-07-07-movement-combat-feel-design.md"
git commit -m "docs(feel): mark spec implemented, pending in-editor verify"
```

- [ ] **Step 5: Report the manual verification checklist**

These require the Unity editor + a two-peer (host + client) session and CANNOT be automated here. Report them as pending to the user:

1. Ground feel: run left/right — reaches full speed in ~4 ticks, stops in ~3; no ice-skating.
2. Dash exit: dash to completion on flat ground — short (~0.25 s) slide, no abrupt snap.
3. Dash-jump: jump mid-dash — clear speed burst (~2× walk) decaying through the air; works on the client (predicted) without rubber-banding.
4. Jump arc: full jump has a readable hang at the top; falls feel faster than rises; jump-cut still works; walking off a ledge does NOT hang.
5. Fast-fall: press down past the apex — snappy drop; down+attack mid-air still ground-pounds.
6. Melee: swing whiffs are punishable (no dash during recovery); hitbox catches a strafing target (multi-tick window); one damage number per swing per target; up/side/pound boxes still correct; dash-strike (Quicker Dash T3) unchanged.
7. Aura: coins past thresholds glow on both peers; stealth hides it; death clears it; glow renders behind the body sprite.
8. Regression: flag pickup/carry/capture, stun, respawn teleport, team-buff damage lift all unchanged.

---

## Appendix A: Verification while the Unity editor may hold the project lock

**Compile gate (always available):** compile the changed scripts with Unity's bundled Roslyn. Build a response file in the scratchpad, then:

```powershell
$ed = "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor"
& "$ed\Data\NetCoreRuntime\dotnet.exe" exec "$ed\Data\DotNetSdkRoslyn\csc.dll" "@compile.rsp"
```

The `.rsp` must reference (quote every path INSIDE the rsp — "Program Files" has a space):
- `$ed\Data\Managed\UnityEngine\*.dll`
- the netstandard ref: `$ed\Data\NetStandard\ref\2.1.0\netstandard.dll`
- `Assets\Photon\Fusion\Assemblies\*.dll`
- `Library\ScriptAssemblies\{Fusion.Unity,Fusion.Addons.Physics,Game.PlayerMovement.Core,Game.Combat.Core,Game.Hud.Core,Game.Buffs.Core,Game.PlayerAnimation.Core,Game.Net,Game.EnemyAI,Unity.InputSystem,Unity.TextMeshPro,UnityEngine.UI}.dll`
- `-target:library -nologo -nowarn:0169` and every `.cs` under `Assets\Scripts` EXCEPT asmdef-owned folders (exclude with a trailing `\` on the prefix so `...\Scripts\Net\` does not also exclude `NetworkedSpawnManager.cs`).

For a NEW core assembly (`Game.PlayerMovement.Core`): its DLL will not exist in `Library\ScriptAssemblies` until the editor imports it. Compile the core sources directly into the gate instead: add `Assets\Scripts\Player\Movement\Core\*.cs` to the source list (engine-free, so it needs only the netstandard ref).

**Pure-core test fallback (when `-runTests` is blocked):** the new core classes are engine-free, so compile them + a plain assert harness with only the netstandard ref and run on the bundled runtime:

1. Copy the core `.cs` files and write a `Harness.cs` with a `static int Main()` that mirrors every NUnit case in the task (use `if (!(cond)) { System.Console.WriteLine("FAIL: <name>"); fails++; }`), returning `fails`.
2. Compile: `csc.dll -out:tests.dll -target:library` + sources, then a tiny runner, or simply `-target:exe`:

```powershell
$ed = "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor"
& "$ed\Data\NetCoreRuntime\dotnet.exe" exec "$ed\Data\DotNetSdkRoslyn\csc.dll" -nologo -target:exe -out:"$scratch\tests.exe" -r:"$ed\Data\NetStandard\ref\2.1.0\netstandard.dll" "Assets\Scripts\Player\Movement\Core\MovementMath.cs" "$scratch\Harness.cs"
```

3. Write `tests.runtimeconfig.json` next to the exe:

```json
{ "runtimeOptions": { "tfm": "net8.0", "framework": { "name": "Microsoft.NETCore.App", "version": "8.0.0" } } }
```

4. Run: `& "$ed\Data\NetCoreRuntime\dotnet.exe" "$scratch\tests.exe"` — expect `0 FAILS`.

The real NUnit EditMode suite must still be run (Task 9 step 2, or in-editor by the user) before merge claims.

## Appendix B: .meta file templates

Generate a fresh GUID per file: `-join ((1..32) | ForEach-Object { '{0:x}' -f (Get-Random -Max 16) })`

**Folder** (`<name>.meta` next to the folder):

```yaml
fileFormatVersion: 2
guid: <32-hex-guid>
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

**C# script** (`<name>.cs.meta`):

```yaml
fileFormatVersion: 2
guid: <32-hex-guid>
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

**asmdef** (`<name>.asmdef.meta`):

```yaml
fileFormatVersion: 2
guid: <32-hex-guid>
AssemblyDefinitionImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

## Post-merge follow-ups (NOT in this plan)

- Tune `PlayerAnimator.attackDuration` so the attack clip covers startup + active (~8 ticks); recovery shows locomotion.
- Playtest pass on all new defaults (they are starting points, not answers).
- Camera-feel-pass spec (`feat/camera-feel-pass` worktree) predates this work — refresh its dash-kick/hit assumptions before executing it.
- Melee damage rebalance vs projectiles (spec "Out of scope").
