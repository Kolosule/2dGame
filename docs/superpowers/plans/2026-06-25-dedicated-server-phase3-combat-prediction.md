# Dedicated Server — Phase 3: Cosmetic Combat Prediction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make shooting feel instant on the firing client by playing immediate, non-networked muzzle/tracer feedback the moment the shot input is accepted — while the server stays fully authoritative over the real projectile, damage, and hit detection.

**Architecture:** The shoot cooldown is already a predicted `TickTimer` on input authority, so the firing client reliably knows locally that a shot fired. On that client only, `PlayerCombat.ShootProjectile` plays a short-lived muzzle flash (optional prefab) + a code-generated `LineRenderer` tracer, guarded to fire exactly once (`Runner.IsForward`, so resimulation can't duplicate it) and only on non-authority clients (`HasInputAuthority && !HasStateAuthority`, so a host-as-player — whose real projectile is already instant — doesn't double up). **Lag compensation is dropped (per user):** the server keeps its existing `OnTriggerEnter2D`/`OverlapBoxAll` hit detection unchanged. This phase adds client-local visuals only — no authority, damage, or hit-detection change.

**Tech Stack:** Unity, Photon Fusion 2 (dedicated `GameMode.Server`, server-authoritative), C#.

## Global Constraints

- Photon **Fusion 2**; dedicated server is state authority. Cosmetic FX run only on the firing client (`HasInputAuthority && !HasStateAuthority`).
- Fire the cosmetic FX exactly once per shot: guard with `Runner.IsForward` (true only on forward, non-resimulated ticks) — `ShootProjectile` runs inside `FixedUpdateNetwork`/`Simulate`, which resimulates on the input-authority client.
- **No authority/hit-detection/damage change.** Projectile spawn, damage, stun, and the server's `OnTriggerEnter2D`/`OverlapBoxAll` detection stay exactly as today. Do NOT add lag compensation / Fusion Hitboxes (de-scoped).
- All cosmetic FX are **non-networked**, short-lived, and carry no gameplay effect. Everything is null-safe: with no muzzle prefab assigned, only the code-generated tracer shows; the feature never regresses existing behavior.
- New `MonoBehaviour`s live in `Assets/Scripts/Player/` (part of `Assembly-CSharp`, with `PlayerCombat`), NOT the engine-free `Game.Net` asmdef.
- Melee swing animation is already predicted; a predicted melee *hit marker* is explicitly NOT in scope (without lag comp it would produce false-positive markers).
- Unity cannot compile/build/run here; this phase has no pure-logic unit surface, so there are NO EditMode tests. Verified by a user play-test (a client sees instant muzzle/tracer on fire; the real projectile still governs travel/damage; no lingering double-projectile).

---

### Task 1: `CosmeticTracer` — self-animating, self-destroying muzzle tracer

**Files:**
- Create: `Assets/Scripts/Player/CosmeticTracer.cs`

**Interfaces:**
- Produces: `static void CosmeticTracer.Spawn(Vector3 origin, Vector2 dir, float length, float width, Color color, float duration)`.

- [ ] **Step 1: Create the tracer**

Create `Assets/Scripts/Player/CosmeticTracer.cs`:

```csharp
using UnityEngine;

/// <summary>
/// Self-animating, NON-networked muzzle tracer for client-side shot prediction. Draws a brief line
/// from the muzzle along the aim direction, fades out, then destroys itself. Purely cosmetic — no
/// gameplay effect. Spawned by PlayerCombat on the firing client; the real networked projectile
/// governs actual travel and damage. Code-generated so it needs no art asset.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class CosmeticTracer : MonoBehaviour
{
    private LineRenderer line;
    private float duration;
    private float elapsed;
    private Color startColor;

    /// <summary>Spawn a one-shot tracer. Safe to call every shot; it cleans itself up.</summary>
    public static void Spawn(Vector3 origin, Vector2 dir, float length, float width, Color color, float duration)
    {
        var go = new GameObject("CosmeticTracer");
        var tracer = go.AddComponent<CosmeticTracer>(); // RequireComponent adds the LineRenderer
        tracer.Init(origin, dir, length, width, color, duration);
    }

    private void Init(Vector3 origin, Vector2 dir, float length, float width, Color color, float duration)
    {
        this.duration = Mathf.Max(0.01f, duration);
        startColor = color;

        line = GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.positionCount = 2;
        line.SetPosition(0, origin);
        line.SetPosition(1, origin + (Vector3)(dir.normalized * length));
        line.startWidth = width;
        line.endWidth = width;
        line.startColor = color;
        line.endColor = color;
        line.sortingOrder = 100;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = elapsed / duration;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        Color c = startColor;
        c.a = startColor.a * (1f - t); // fade out
        line.startColor = c;
        line.endColor = c;
    }
}
```

- [ ] **Step 2: Self-review and commit**

No automated test (visual MonoBehaviour; Unity unavailable here). Self-review: `Spawn` creates a
GameObject, `RequireComponent` guarantees the `LineRenderer`, the line is set in world space, and
`Update` fades then destroys (no leak). Note for the user step: `Sprites/Default` must be available
at runtime (it is in a 2D project; if a build strips it, add it to Project Settings → Graphics →
Always Included Shaders).

```bash
git add Assets/Scripts/Player/CosmeticTracer.cs
git commit -m "feat(combat): self-destroying cosmetic muzzle tracer (non-networked)"
```

---

### Task 2: Cosmetic shoot prediction in `PlayerCombat`

**Files:**
- Modify: `Assets/Scripts/Player/PlayerCombat.cs` (serialized fields, `ShootProjectile`, new `PlayLocalShootFx`)

**Interfaces:**
- Consumes: `CosmeticTracer.Spawn(...)` (Task 1); `Runner.IsForward`, `HasInputAuthority`, `HasStateAuthority`.

- [ ] **Step 1: Add the serialized cosmetic-FX fields**

In `PlayerCombat`, add a new header block with the other `[SerializeField]` fields (e.g. just below
the `[Header("Projectile Settings")]` group):

```csharp
    [Header("Shoot Prediction (cosmetic, firing client only)")]
    [Tooltip("Optional muzzle-flash prefab spawned instantly on the firing client. Null = tracer only.")]
    [SerializeField] private GameObject muzzleFlashPrefab;
    [SerializeField] private float muzzleFlashLifetime = 0.2f;
    [Tooltip("Code-generated tracer shown instantly on fire (no art needed).")]
    [SerializeField] private Color tracerColor = new Color(1f, 0.9f, 0.3f, 1f);
    [SerializeField] private float tracerLength = 1.5f;
    [SerializeField] private float tracerWidth = 0.08f;
    [SerializeField] private float tracerDuration = 0.1f;
```

- [ ] **Step 2: Fire the cosmetic FX in `ShootProjectile`**

In `ShootProjectile(Vector2 aimWorldPoint)`, the current body is:

```csharp
    private void ShootProjectile(Vector2 aimWorldPoint)
    {
        if (playerAnimator != null) playerAnimator.TriggerShoot();
        if (projectilePrefab == null || projectileSpawnPoint == null) return;
        if (!HasStateAuthority)
        {
            return; // only the server spawns networked objects
        }
        ...
    }
```

Insert the cosmetic-prediction call **after** the `projectileSpawnPoint` null-check and **before**
the `if (!HasStateAuthority)` return:

```csharp
        if (playerAnimator != null) playerAnimator.TriggerShoot();
        if (projectilePrefab == null || projectileSpawnPoint == null) return;

        // Cosmetic local prediction: instant muzzle/tracer on the firing client only. IsForward
        // fires it exactly once (not on resimulation); !HasStateAuthority skips a host-as-player
        // whose real projectile is already instant. The server still spawns the authoritative one.
        if (HasInputAuthority && !HasStateAuthority && Runner.IsForward)
        {
            Vector2 dir = (aimWorldPoint - (Vector2)projectileSpawnPoint.position).normalized;
            PlayLocalShootFx(projectileSpawnPoint.position, dir);
        }

        if (!HasStateAuthority)
        {
            return; // only the server spawns networked objects
        }
```

(Leave the rest of `ShootProjectile` — the `Runner.Spawn` block — unchanged.)

- [ ] **Step 3: Add `PlayLocalShootFx`**

Add the helper method to `PlayerCombat` (e.g. just below `ShootProjectile`):

```csharp
    /// <summary>
    /// Client-local, non-networked shot feedback (muzzle flash + tracer). No gameplay effect — the
    /// server's networked projectile is authoritative. Called only on the firing input-authority
    /// client, once per shot.
    /// </summary>
    private void PlayLocalShootFx(Vector3 origin, Vector2 dir)
    {
        if (muzzleFlashPrefab != null)
        {
            GameObject flash = Instantiate(muzzleFlashPrefab, origin, Quaternion.identity);
            Destroy(flash, muzzleFlashLifetime);
        }
        CosmeticTracer.Spawn(origin, dir, tracerLength, tracerWidth, tracerColor, tracerDuration);
    }
```

- [ ] **Step 4: Self-review and commit**

Self-review: the FX call is guarded by `HasInputAuthority && !HasStateAuthority && Runner.IsForward`
and sits before the `!HasStateAuthority` return; the authoritative `Runner.Spawn` block is unchanged;
no damage/stun/hit-detection code was touched; `PlayLocalShootFx` is null-safe on the muzzle prefab.

```bash
git add Assets/Scripts/Player/PlayerCombat.cs
git commit -m "feat(combat): cosmetic muzzle/tracer prediction on the firing client"
```

---

### Task 3: USER — wire + verify (deferred, required)

**Files:** none (Unity Editor + play-test).

- [ ] **Step 1: (Optional) assign a muzzle-flash prefab**

On the player prefab's `PlayerCombat`, optionally assign `muzzleFlashPrefab` (any short particle/
sprite effect). Leaving it null is fine — the code-generated tracer still provides instant feedback.
Tune `tracerColor/Length/Width/Duration` to taste. Let Unity generate `.meta` for `CosmeticTracer.cs`.

- [ ] **Step 2: Verify on a real client (dedicated-server path)**

Run the headless server + a client (`singlePlayerMode = false`). On the client:
- [ ] **Instant feedback:** firing shows the muzzle/tracer the moment you press shoot, with no
      round-trip delay (compare to before: the projectile only appeared after ~½ RTT).
- [ ] **Real projectile still governs gameplay:** the networked projectile still travels and deals
      damage/stun exactly as before; the cosmetic tracer is brief and does not linger beside it.
- [ ] **No duplicates:** rapid fire / lag does not spawn multiple tracers per shot (the `IsForward`
      guard holds under resimulation).
- [ ] **Host-as-player path unaffected:** in solo-dev host mode (`singlePlayerMode = true`), no
      double projectile — the cosmetic FX is skipped (`!HasStateAuthority`), and the real projectile
      is already instant.
- [ ] **No new GC concern under fire** (the tracer allocates a tiny GameObject + material per shot;
      acceptable, but note it — if it ever matters, pool the tracer like Phase 2b pools projectiles).

---

## Notes

- Per-shot tracer allocates a `GameObject` + a `Material` (`Sprites/Default`). Negligible for normal
  fire rates; if it ever shows on the profiler, give the tracer a shared material or pool it.
- A full traveling cosmetic *ghost projectile* (hiding the projectile's ½-RTT travel delay entirely)
  was intentionally not built — it risks double-vision with the real projectile. The muzzle/tracer
  gives the instant "I fired" cue at much lower risk. Revisit only if travel-delay still feels bad.
