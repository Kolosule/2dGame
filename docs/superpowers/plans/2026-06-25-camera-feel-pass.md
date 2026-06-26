# Camera Feel Pass Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the local player's camera feel tight, crosshair-led, and kinetic, and make that feel survive higher latency, by reworking `PlayerCamera` into an explicit sum of contributions (tight-X/deadzone-Y follow, network-correction absorb, aim lean, impulse channel) plus a dash kick and a predicted melee hit-stop.

**Architecture:** `PlayerCamera` stays the single scene rig that self-finds the local input-authority player. Its `LateUpdate` is restructured so the final position = follow (tight X, deadzone Y, on the predicted body) + correction-absorb (decaying residual from a reconciliation snap) + aim lean (capped, toward aim direction) + impulse offset (additive decaying channel feeding shake + dash kick). Feel events come from input-authority-gated code at their source: dash from a new `PlayerCameraFeelHandler` on the player prefab, hit-stop from a predicted overlap inside `PlayerCombat`.

**Tech Stack:** Unity 6.3 (6000.3.0f1), C#, Photon Fusion 2, new Input System, Fusion Physics Addon. Game code lives in `Assembly-CSharp`.

## Global Constraints

- **No test assembly.** Verification is manual/observational: compile clean in Unity, then check in single-player (`GameNetworkManager.singlePlayerMode = true`, Host) and in Multiplayer Play Mode (`singlePlayerMode = false`, 1 host/server + 1 client). Copy exact values verbatim from this plan.
- **Render-only, local-only.** Nothing in this plan touches authority, networked state, hit detection, `Time.timeScale`, or the Fusion tick. All new behavior runs on the local input-authority client and affects only the local camera.
- **No new dependencies.** No Cinemachine, no packages.
- **Follow existing conventions:** new Input System only; the sole device-read site stays `NetworkInputProvider.OnInput`; camera-feel triggers are gated to the local player via `Object.HasInputAuthority` (mirror `PlayerCameraRespawnHandler`, not the un-gated `PlayerCameraShakeHandler`).
- **AoI budget:** aim lean must stay small enough that the screen edge stays inside the per-player Area-of-Interest radius (`PlayerController.areaOfInterestRadius = 25`, view half-width ~14 with speed-zoom). Keep `aimLeanDistance` ≤ ~3 world units.
- **Commits** happen in the `feat/camera-feel-pass` worktree (`C:/Users/1/Documents/GitHub/2dGame-camera`). End each commit message with:
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`

---

## File Structure

- **Modify** `Assets/Scripts/Player/Playercamera.cs` — the rig. Restructure follow, add correction-absorb, aim lean, the impulse channel + `AddImpulse`/`Hold` API; route shake through the channel.
- **Modify** `Assets/Scripts/Player/PlayerCombat.cs` — cache `lastAimWorldPoint`; expose `GetAimDirection()`; fire predicted hit-stop on a local non-authoritative overlap.
- **Modify** `Assets/Scripts/Player/PlayerCameraShakeHandler.cs` — gate to `HasInputAuthority`.
- **Create** `Assets/Scripts/Player/PlayerCameraFeelHandler.cs` — input-authority-gated handler on the player prefab; detects the dash rising edge and triggers the dash kick; relays hit-stop to the camera.
- **Manual (Unity Editor):** add `PlayerCameraFeelHandler` to the Player prefab (let Unity generate the `.meta`).

---

### Task 1: Tight-X / deadzone-Y follow

Replace the uniform `SmoothDamp(followSmoothTime)` follow with near-instant horizontal tracking and a vertical deadzone, so running/dashing feels instant while hops don't jerk the view.

**Files:**
- Modify: `Assets/Scripts/Player/Playercamera.cs`

**Interfaces:**
- Consumes: existing `targetPlayer` (Transform), `currentFollowPosition`, `cameraZPosition`, shake fields, `enableSpeedZoom`.
- Produces: the follow computation that Tasks 2–5 extend. New private helper `Vector3 ComputeFollowPosition()` returning the desired camera XY (Z applied separately).

- [ ] **Step 1: Add the new serialized follow fields**

In `Assets/Scripts/Player/Playercamera.cs`, under the `📹 Camera Follow Settings` header, after the existing `followSmoothTime` field, add:

```csharp
    [Tooltip("Horizontal follow smoothing — keep very low so run/dash feel instant.")]
    [SerializeField] private float horizontalSmoothTime = 0.03f;

    [Tooltip("Vertical follow smoothing applied once the player leaves the deadzone band.")]
    [SerializeField] private float verticalSmoothTime = 0.16f;

    [Tooltip("Half-height (world units) of the vertical deadzone. The camera only moves in Y " +
             "when the player leaves this band, so jumps/hops don't jerk the view.")]
    [SerializeField] private float verticalDeadzone = 1.2f;
```

- [ ] **Step 2: Replace the uniform follow with per-axis follow**

In `LateUpdate`, replace this block:

```csharp
        // Calculate target position (where we want the camera to be)
        Vector3 targetPosition = targetPlayer.position;
        targetPosition.z = cameraZPosition;

        // Smoothly move camera to target position
        currentFollowPosition = Vector3.SmoothDamp(
            currentFollowPosition,
            targetPosition,
            ref followVelocity,
            followSmoothTime
        );
```

with:

```csharp
        currentFollowPosition = ComputeFollowPosition();
```

Then add this method to the class (next to `HandleSpeedBasedZoom`):

```csharp
    /// <summary>
    /// Desired camera XY from the followed body: horizontal is near-instant; vertical uses a
    /// deadzone band so small hops don't move the camera, easing only once the player leaves it.
    /// Z is left at the current follow Z (applied by the caller).
    /// </summary>
    private Vector3 ComputeFollowPosition()
    {
        Vector3 body = targetPlayer.position;

        // Horizontal: tight follow.
        float newX = Mathf.SmoothDamp(currentFollowPosition.x, body.x,
                                      ref followVelocity.x, horizontalSmoothTime);

        // Vertical: deadzone. Only chase the part of the offset outside the band.
        float dy = body.y - currentFollowPosition.y;
        float targetY = currentFollowPosition.y;
        if (Mathf.Abs(dy) > verticalDeadzone)
        {
            float overshoot = dy - Mathf.Sign(dy) * verticalDeadzone;
            targetY = currentFollowPosition.y + overshoot;
        }
        float newY = Mathf.SmoothDamp(currentFollowPosition.y, targetY,
                                      ref followVelocity.y, verticalSmoothTime);

        return new Vector3(newX, newY, cameraZPosition);
    }
```

`followVelocity` is already a `Vector3` field, so `ref followVelocity.x` / `.y` work directly.

- [ ] **Step 3: Compile in Unity**

Switch to the Unity Editor (project open on the `feat/camera-feel-pass` worktree) and let it recompile.
Expected: no compile errors in the Console.

- [ ] **Step 4: Verify feel in single-player**

Set `GameNetworkManager.singlePlayerMode = true`, enter Play, move and dash horizontally, then jump.
Expected: horizontal follow feels immediate (no drag behind on run/dash); small jumps do not move the camera vertically; a large fall still brings the camera down smoothly.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Player/Playercamera.cs
git commit -m "feat(camera): tight-X / deadzone-Y follow

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Network-correction absorb

The camera follows the predicted body directly. When the server reconciles a misprediction (a position snap larger than legitimate motion), absorb the jump into a decaying offset so the camera eases rather than teleports.

**Files:**
- Modify: `Assets/Scripts/Player/Playercamera.cs`

**Interfaces:**
- Consumes: `ComputeFollowPosition()` (Task 1), `targetPlayer`, `OnPlayerRespawned()`, `SnapToPosition()`.
- Produces: `correctionOffset` applied inside the follow path; `ResetCorrection()` called on teleports.

- [ ] **Step 1: Add serialized fields + state**

Under a new header in `Assets/Scripts/Player/Playercamera.cs`:

```csharp
    [Header("🩹 Network Correction Absorb")]
    [Tooltip("Body movement faster than this (world units/sec) is treated as a reconciliation " +
             "snap, not real motion. Keep above dash speed so dashes are followed instantly.")]
    [SerializeField] private float maxFollowSpeed = 40f;

    [Tooltip("How quickly an absorbed correction eases out (higher = faster catch-up).")]
    [SerializeField] private float correctionRecoverRate = 9f;
```

Add to the internal-variables section:

```csharp
    // Network-correction absorb
    private Vector3 lastBodyPosition;
    private Vector3 correctionOffset;
    private bool hasLastBodyPosition;
```

- [ ] **Step 2: Absorb large body deltas inside the follow path**

Replace the body line at the top of `ComputeFollowPosition()`:

```csharp
        Vector3 body = targetPlayer.position;
```

with:

```csharp
        Vector3 body = AbsorbCorrection(targetPlayer.position);
```

Add the helper:

```csharp
    /// <summary>
    /// Detects reconciliation snaps: if the body moved faster than maxFollowSpeed this frame, the
    /// excess is moved into correctionOffset (so the followed point stays put) and then eased out
    /// over the next frames. Normal motion (including dashes) passes through untouched.
    /// </summary>
    private Vector3 AbsorbCorrection(Vector3 bodyPos)
    {
        if (!hasLastBodyPosition)
        {
            lastBodyPosition = bodyPos;
            hasLastBodyPosition = true;
        }

        float dt = Time.deltaTime;
        float maxStep = maxFollowSpeed * Mathf.Max(dt, 0.0001f);
        Vector3 delta = bodyPos - lastBodyPosition;
        if (delta.magnitude > maxStep)
        {
            // Treat the whole jump as a correction to absorb.
            correctionOffset += delta;
        }
        lastBodyPosition = bodyPos;

        // Ease the absorbed offset out.
        correctionOffset = Vector3.Lerp(correctionOffset, Vector3.zero, correctionRecoverRate * dt);

        return bodyPos - correctionOffset;
    }

    /// <summary>Clears absorb state on a legitimate teleport (respawn/snap).</summary>
    private void ResetCorrection()
    {
        correctionOffset = Vector3.zero;
        hasLastBodyPosition = false;
    }
```

- [ ] **Step 3: Reset absorb on teleports**

In `SnapToPosition`, after `currentFollowPosition = position;`, add:

```csharp
        ResetCorrection();
```

In `OnPlayerRespawned`, at the end of the method, add:

```csharp
        ResetCorrection();
```

- [ ] **Step 4: Compile in Unity**

Expected: no compile errors.

- [ ] **Step 5: Verify in MPPM with latency**

Run MPPM (`singlePlayerMode = false`, 1 host + 1 client). In the Fusion `NetworkProjectConfig` (or the Fusion runner's simulation settings) enable simulated latency if available; otherwise verify the no-snap baseline. On the client window, run and dash into walls / reverse direction rapidly.
Expected: normal motion and dashes track instantly; any reconciliation snap is eased out over a few frames instead of a hard jump. Respawn still snaps cleanly to the respawn point (no residual drift).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Player/Playercamera.cs
git commit -m "feat(camera): absorb network reconciliation snaps

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Aim lean toward the cursor

Bias the camera a small, capped amount toward the player's aim direction so you see more of your firing lane. Driven by aim *direction* (not cursor distance) to keep the camera↔cursor feedback loop bounded.

**Files:**
- Modify: `Assets/Scripts/Player/PlayerCombat.cs`
- Modify: `Assets/Scripts/Player/Playercamera.cs`

**Interfaces:**
- Produces (PlayerCombat): `public Vector2 GetAimDirection()` — unit vector from the player toward the last aim world point, or `Vector2.zero` if unset/degenerate. Input-authority-meaningful.
- Consumes (PlayerCamera): `GetAimDirection()` from the followed player's `PlayerCombat`, cached as `targetCombat`.

- [ ] **Step 1: Cache the aim point and expose direction in PlayerCombat**

In `Assets/Scripts/Player/PlayerCombat.cs`, add a field near the other private refs (after `private int verticalAim;`):

```csharp
    private Vector2 lastAimWorldPoint;
```

At the top of `Simulate`, after `verticalAim = input.VerticalAim;`, add:

```csharp
        lastAimWorldPoint = input.AimWorldPoint;
```

Add the public getter (near the other public accessors / end of the class, before `OnDrawGizmosSelected`):

```csharp
    /// <summary>
    /// Local aim direction (unit vector) from this player toward the last mouse aim point. Used by
    /// PlayerCamera for the aim lean. Returns Vector2.zero before any input or if degenerate.
    /// </summary>
    public Vector2 GetAimDirection()
    {
        Vector2 d = lastAimWorldPoint - (Vector2)transform.position;
        return d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.zero;
    }
```

- [ ] **Step 2: Add aim-lean fields + target cache in PlayerCamera**

In `Assets/Scripts/Player/Playercamera.cs`, add a header + fields:

```csharp
    [Header("🎯 Aim Lean")]
    [Tooltip("Bias the camera toward the mouse aim direction.")]
    [SerializeField] private bool enableAimLean = true;

    [Tooltip("Max camera offset (world units) toward the aim direction. Keep small so the screen " +
             "edge stays within the player's Area-of-Interest radius.")]
    [SerializeField] private float aimLeanDistance = 2.0f;

    [Tooltip("Smoothing for the aim lean so fast cursor flicks don't jerk the view.")]
    [SerializeField] private float aimLeanSmoothTime = 0.2f;
```

Add to the internal-variables section:

```csharp
    // Aim lean
    private PlayerCombat targetCombat;
    private Vector3 currentAimLean;
    private Vector3 aimLeanVelocity;
```

- [ ] **Step 3: Cache the PlayerCombat when the local player is found**

In `FindLocalPlayer`, inside the `if (player.HasInputAuthority)` block, after `targetRigidbody = player.GetComponent<Rigidbody2D>();`, add:

```csharp
                targetCombat = player.GetComponent<PlayerCombat>();
```

In `RefreshTarget`, after `targetRigidbody = null;`, add:

```csharp
        targetCombat = null;
```

- [ ] **Step 4: Apply the lean in LateUpdate**

In `LateUpdate`, the final position is currently set from `currentFollowPosition` (+ shake). Change the position assembly so the lean is added. Replace:

```csharp
        // Apply camera shake if active
        Vector3 finalPosition = currentFollowPosition;
```

with:

```csharp
        // Aim lean (additive, capped, smoothed).
        Vector3 targetLean = Vector3.zero;
        if (enableAimLean && targetCombat != null)
        {
            Vector2 aimDir = targetCombat.GetAimDirection();
            targetLean = (Vector3)(aimDir * aimLeanDistance);
        }
        currentAimLean = Vector3.SmoothDamp(currentAimLean, targetLean,
                                            ref aimLeanVelocity, aimLeanSmoothTime);

        // Apply camera shake if active
        Vector3 finalPosition = currentFollowPosition + currentAimLean;
        finalPosition.z = cameraZPosition;
```

- [ ] **Step 5: Compile in Unity**

Expected: no compile errors.

- [ ] **Step 6: Verify in single-player**

Play, move the mouse around the player.
Expected: the view biases a small amount toward the cursor and settles; fast cursor flicks don't snap the camera; the lean magnitude is capped (never reveals more than ~`aimLeanDistance` units).

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Player/PlayerCombat.cs Assets/Scripts/Player/Playercamera.cs
git commit -m "feat(camera): subtle capped aim lean toward the cursor

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Impulse channel + dash kick

Add an additive, decaying impulse channel to `PlayerCamera` and a `PlayerCameraFeelHandler` that pushes a directional kick when a dash starts.

**Files:**
- Modify: `Assets/Scripts/Player/Playercamera.cs`
- Create: `Assets/Scripts/Player/PlayerCameraFeelHandler.cs`
- Manual: add `PlayerCameraFeelHandler` to the Player prefab.

**Interfaces:**
- Produces (PlayerCamera): `public void AddImpulse(Vector2 direction, float magnitude, float duration)`.
- Produces (PlayerCameraFeelHandler): `public void TriggerHitStop()` (used by Task 5); reads `PlayerMovement.IsDashing()` for the dash edge.
- Consumes: `PlayerMovement.IsDashing()`, `PlayerMovement` facing via `transform.localScale.x` sign on the player.

- [ ] **Step 1: Add the impulse channel to PlayerCamera**

In `Assets/Scripts/Player/Playercamera.cs`, add to the internal-variables section:

```csharp
    // Impulse channel (dash kick, etc.) — additive, decaying.
    private struct CamImpulse { public Vector3 dir; public float magnitude; public float duration; public float elapsed; }
    private readonly System.Collections.Generic.List<CamImpulse> impulses = new System.Collections.Generic.List<CamImpulse>();
```

Add the public method (near `TriggerShake`):

```csharp
    /// <summary>
    /// Push a directional camera impulse that decays to zero over <paramref name="duration"/>.
    /// Local cosmetic feedback only.
    /// </summary>
    public void AddImpulse(Vector2 direction, float magnitude, float duration)
    {
        if (duration <= 0f || magnitude <= 0f) return;
        Vector3 d = direction.sqrMagnitude > 0.0001f ? (Vector3)direction.normalized : Vector3.zero;
        impulses.Add(new CamImpulse { dir = d, magnitude = magnitude, duration = duration, elapsed = 0f });
    }

    /// <summary>Sums + advances all active impulses, dropping expired ones. Call once per frame.</summary>
    private Vector3 EvaluateImpulses()
    {
        Vector3 sum = Vector3.zero;
        for (int i = impulses.Count - 1; i >= 0; i--)
        {
            CamImpulse imp = impulses[i];
            imp.elapsed += Time.deltaTime;
            if (imp.elapsed >= imp.duration) { impulses.RemoveAt(i); continue; }
            float t = 1f - (imp.elapsed / imp.duration); // linear decay
            sum += imp.dir * (imp.magnitude * t);
            impulses[i] = imp;
        }
        return sum;
    }
```

- [ ] **Step 2: Add the impulse offset into the final position**

In `LateUpdate`, after the shake block sets `finalPosition`, add the impulse contribution. Locate:

```csharp
        // Set camera position
        transform.position = finalPosition;
```

and insert immediately before it:

```csharp
        finalPosition += EvaluateImpulses();
```

- [ ] **Step 3: Create PlayerCameraFeelHandler**

Create `Assets/Scripts/Player/PlayerCameraFeelHandler.cs`:

```csharp
using UnityEngine;
using Fusion;

/// <summary>
/// Local-player camera juice: triggers a directional camera kick when a dash starts, and relays
/// the predicted melee hit-stop to the camera. Lives on the Player prefab; only the local
/// input-authority instance drives the (single) gameplay camera — mirrors
/// PlayerCameraRespawnHandler's gating so a remote player's events never move your camera.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerCameraFeelHandler : MonoBehaviour
{
    [Header("💥 Dash Kick")]
    [Tooltip("Strength of the camera kick when a dash starts (world units).")]
    [SerializeField] private float dashKickMagnitude = 0.5f;
    [Tooltip("How long the dash kick decays over.")]
    [SerializeField] private float dashKickDuration = 0.12f;

    [Header("🛑 Melee Hit-Stop")]
    [Tooltip("How long the camera holds (freezes follow) when your melee lands.")]
    [SerializeField] private float hitStopDuration = 0.07f;
    [Tooltip("Small upward punch added on a landed hit.")]
    [SerializeField] private float hitStopPunch = 0.15f;

    private NetworkObject netObj;
    private PlayerMovement movement;
    private PlayerCamera playerCamera;
    private bool wasDashing;

    private void Awake()
    {
        netObj = GetComponent<NetworkObject>();
        movement = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        // Only the local player's handler may drive the single gameplay camera.
        if (netObj == null || !netObj.HasInputAuthority) return;

        if (playerCamera == null)
        {
            playerCamera = FindFirstObjectByType<PlayerCamera>();
            if (playerCamera == null) return;
        }

        bool dashing = movement != null && movement.IsDashing();
        if (dashing && !wasDashing)
        {
            // Kick in the dash travel direction (player faces dash direction via localScale.x sign).
            float dir = transform.localScale.x >= 0f ? 1f : -1f;
            playerCamera.AddImpulse(new Vector2(dir, 0f), dashKickMagnitude, dashKickDuration);
        }
        wasDashing = dashing;
    }

    /// <summary>Called by PlayerCombat when a local melee swing is predicted to connect.</summary>
    public void TriggerHitStop()
    {
        if (netObj == null || !netObj.HasInputAuthority) return;
        if (playerCamera == null)
        {
            playerCamera = FindFirstObjectByType<PlayerCamera>();
            if (playerCamera == null) return;
        }
        playerCamera.Hold(hitStopDuration);
        playerCamera.AddImpulse(Vector2.up, hitStopPunch, hitStopDuration);
    }
}
```

> Note: `Hold` is added in Task 5; this file compiles only after Task 5's `PlayerCamera.Hold` exists. Implement Task 5's Step 1 (the `Hold` method) together with this task if compiling between tasks — or accept that the project first compiles cleanly at the end of Task 5. To keep tasks independently compilable, **do Step 4 of THIS task (the `Hold` stub) now.**

- [ ] **Step 4: Add a `Hold` method to PlayerCamera (so this task compiles)**

In `Assets/Scripts/Player/Playercamera.cs`, add the field to the internal-variables section:

```csharp
    // Hit-stop hold
    private float holdTimer;
```

Add the public method (near `AddImpulse`):

```csharp
    /// <summary>Briefly freezes follow advancement (render-only hit-stop). Never affects the sim.</summary>
    public void Hold(float duration)
    {
        if (duration > holdTimer) holdTimer = duration;
    }
```

(The `holdTimer` is consumed by the follow path in Task 5; for now it just decays harmlessly. Add the decay in `LateUpdate` at the very top of the active branch, right after the `isTransitioningToRespawn` handling:)

```csharp
        if (holdTimer > 0f) holdTimer -= Time.deltaTime;
```

- [ ] **Step 5: Add PlayerCameraFeelHandler to the Player prefab (Unity Editor)**

In the Unity Editor, open the Player prefab (the one with `PlayerController` / `PlayerCombat` / `PlayerCameraRespawnHandler`). Add Component → `PlayerCameraFeelHandler`. Leave default values. Save the prefab. Let Unity generate the `.meta`.

- [ ] **Step 6: Compile + verify in single-player**

Expected: no compile errors. Play, dash left and right.
Expected: a small, quick camera kick in the dash direction that decays within ~0.12 s; no kick when simply walking.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Player/Playercamera.cs Assets/Scripts/Player/PlayerCameraFeelHandler.cs Assets/Scripts/Player/PlayerCameraFeelHandler.cs.meta "Assets/Scripts/Player/Player Prefab.prefab"
git commit -m "feat(camera): impulse channel + dash kick

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

> If the Player prefab path differs, `git add` the actual prefab file Unity modified (check `git status`).

---

### Task 5: Predicted melee hit-stop

When the local player's melee swing is predicted to connect with an enemy, briefly hold the camera. Render-only, client-predicted; never touches `Time.timeScale` or hit detection.

**Files:**
- Modify: `Assets/Scripts/Player/Playercamera.cs`
- Modify: `Assets/Scripts/Player/PlayerCombat.cs`

**Interfaces:**
- Consumes: `PlayerCamera.Hold(float)` (Task 4), `PlayerCameraFeelHandler.TriggerHitStop()` (Task 4).
- Produces: predicted hit detection inside `PlayerCombat.Attack()`; `bool PredictWouldHitEnemy(Vector2 center, Vector2 area)`.

- [ ] **Step 1: Make the follow path honor the hold**

In `Assets/Scripts/Player/Playercamera.cs`, in `ComputeFollowPosition()`, at the very top, freeze advancement while held:

```csharp
        if (holdTimer > 0f)
            return currentFollowPosition; // hit-stop: keep the camera put (offsets still apply)
```

(Place this as the first line of `ComputeFollowPosition()`, before `Vector3 body = ...`.) The `holdTimer` decay added in Task 4 Step 4 already runs in `LateUpdate`.

- [ ] **Step 2: Cache the feel handler in PlayerCombat**

In `Assets/Scripts/Player/PlayerCombat.cs`, add a field near the other refs:

```csharp
    private PlayerCameraFeelHandler feelHandler;
```

In `Awake`, after `mods = GetComponent<PlayerStatModifiers>();`, add:

```csharp
        feelHandler = GetComponent<PlayerCameraFeelHandler>();
```

- [ ] **Step 3: Add the predicted-hit helper**

In `Assets/Scripts/Player/PlayerCombat.cs`, add this method (near `ApplyMeleeHits`):

```csharp
    /// <summary>
    /// CLIENT-LOCAL prediction: would this swing box overlap an enemy (enemy AI or an enemy-team
    /// player)? Read-only — applies no damage. Used only to fire the cosmetic hit-stop on the
    /// firing client; a rare false positive is an acceptable, render-only artifact.
    /// </summary>
    private bool PredictWouldHitEnemy(Vector2 center, Vector2 area)
    {
        Collider2D[] hits = Physics2D.OverlapBoxAll(center, area, 0f, attackableLayer);
        Team myTeam = teamComponent != null ? teamComponent.Team : Team.None;
        foreach (Collider2D hit in hits)
        {
            if (hit.GetComponent<Enemy>() != null) return true;

            PlayerStatsHandler other = hit.GetComponent<PlayerStatsHandler>();
            if (other != null && other != statsHandler)
            {
                PlayerTeamData otherTeam = hit.GetComponent<PlayerTeamData>();
                Team ot = otherTeam != null ? otherTeam.Team : Team.None;
                if (TeamUtil.AreEnemies(myTeam, ot)) return true;
            }
        }
        return false;
    }
```

- [ ] **Step 4: Fire the hit-stop from Attack()**

In `Attack()`, after the attack transform/area are resolved and the `if (attackTransform == null) return;` guard, but using the predicted path on input authority. Locate the animation-latch block:

```csharp
        if (playerAnimator != null)
        {
            if (isGroundPound)
                playerAnimator.TriggerGroundPound();
            else
                playerAnimator.TriggerAttack();
        }
```

Immediately **after** that block, add:

```csharp
        // Cosmetic local hit-stop: on the firing client, on the forward tick only, predict whether
        // the swing connects and briefly hold the camera. Render-only; the server still owns damage.
        if (HasInputAuthority && Runner.IsForward && feelHandler != null &&
            PredictWouldHitEnemy(attackTransform.position, attackArea))
        {
            feelHandler.TriggerHitStop();
        }
```

This sits before the `if (!HasStateAuthority) return;` line, so it runs on clients (which are not state authority) as well as on a host/SP player.

- [ ] **Step 5: Compile in Unity**

Expected: no compile errors.

- [ ] **Step 6: Verify in single-player + MPPM**

Single-player: swing melee (mouse-left) into an enemy.
Expected: a brief (~70 ms) camera hold + tiny upward punch on connect; swinging at empty air produces no hold.
MPPM: on the client, swing into an enemy player.
Expected: hold fires for the swinging player's own camera only; no timescale change; no hold when swinging at a friendly (same-team) player.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Player/Playercamera.cs Assets/Scripts/Player/PlayerCombat.cs
git commit -m "feat(camera): predicted render-only melee hit-stop

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: Shake cleanup — input-authority gating

Fix the latent bug where every player's `PlayerCameraShakeHandler` shakes the single camera on any player's damage. Gate it to the local input-authority player, matching `PlayerCameraRespawnHandler`.

**Files:**
- Modify: `Assets/Scripts/Player/PlayerCameraShakeHandler.cs`

**Interfaces:**
- Consumes: `statsHandler.Object.HasInputAuthority` (the player's `NetworkObject`).
- Produces: shake only triggers for the local player.

- [ ] **Step 1: Gate the Update loop to the local player**

In `Assets/Scripts/Player/PlayerCameraShakeHandler.cs`, at the very top of `Update()`, add the same guard `PlayerCameraRespawnHandler` uses:

```csharp
        // Only the LOCAL player's handler may drive the (single) gameplay camera. Without this,
        // every player's handler shakes the camera when ANY player takes damage.
        if (statsHandler == null || statsHandler.Object == null || !statsHandler.Object.HasInputAuthority)
            return;
```

Place it as the first lines inside `Update()`, before the `if (playerCamera == null)` block.

- [ ] **Step 2: Compile in Unity**

Expected: no compile errors.

- [ ] **Step 3: Verify in MPPM**

Run MPPM with two players. Damage player A (e.g. let an enemy or the other player hit them).
Expected: only A's window camera shakes; B's camera does not shake from A's damage. Each player's camera still shakes when that player takes damage.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Player/PlayerCameraShakeHandler.cs
git commit -m "fix(camera): gate damage shake to the local player

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage** (against `docs/superpowers/specs/2026-06-25-camera-feel-pass-design.md`):
- Tight-X / deadzone-Y follow → Task 1. ✓
- Network-correction absorb → Task 2. ✓
- Aim lean (capped, direction-based, suppressed during respawn) → Task 3 (respawn path returns early in `LateUpdate`, so lean is not applied during the transition). ✓
- One additive offset channel (follow + correction + aim + impulse[shake+kick]) → assembled across Tasks 1–4 in `LateUpdate`. ✓
- Dash kick → Task 4. ✓
- Predicted render-only melee hit-stop (enemy-filtered, `Runner.IsForward`, no timescale) → Task 5. ✓
- Shake routed through the channel + input-authority gating fix → Task 6 (gating). Note: shake retains its own random-offset block and is summed into `finalPosition` alongside the impulse channel; the felt "single channel" is the additive `finalPosition` assembly. The spec's required behavioral fix (input-authority gating) is delivered. ✓
- Server/headless no-op (input-authority + render-only) → all triggers gated by `HasInputAuthority`; camera is stripped on the server build (Phase 1). ✓

**Placeholder scan:** No TBD/TODO; every code step shows complete code. ✓

**Type consistency:** `AddImpulse(Vector2, float, float)`, `Hold(float)`, `GetAimDirection() : Vector2`, `TriggerHitStop()`, `PredictWouldHitEnemy(Vector2, Vector2) : bool`, `ComputeFollowPosition()`, `AbsorbCorrection(Vector3) : Vector3`, `ResetCorrection()`, `EvaluateImpulses() : Vector3` — names used consistently across tasks. `followVelocity` is the existing `Vector3` field reused for per-axis SmoothDamp. ✓

**Cross-task compile ordering note:** Task 4 introduces `PlayerCameraFeelHandler`, which calls `PlayerCamera.Hold`; Task 4 Step 4 adds `Hold` in the same task so the project compiles at each task boundary. Task 5 then makes `Hold` actually freeze the follow. ✓
