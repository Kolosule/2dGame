# Hit-Landed Feedback Effect — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a player lands damage on an enemy or an enemy player, the attacker sees an impact particle burst, a brief white flash on the target, and a floating damage number — attacker-only, delivered via one targeted RPC.

**Architecture:** The server (state authority) already detects every landed hit inside `PlayerCombat.ApplyMeleeHits()` and `Projectile.OnTriggerEnter2D()`. After the existing authoritative damage call, it fires a new `RPC_HitFeedback(NetworkId targetId, Vector2 hitPoint, int damage)` targeted at the attacker's `InputAuthority`. On the attacker's client, the handler resolves the target via `Runner.FindObject` and calls a scene singleton `HitFeedback.Instance.Play(...)`, which spawns a particle prefab, spawns a `DamageNumber` prefab, and triggers `HitFlash.PlayFlash()` on the target's sprite. The two animated behaviours delegate their per-frame math to pure, unit-tested helpers in `Game.Combat.Core`.

**Tech Stack:** Unity 2D, Photon Fusion (Host/Server/Client topology), TextMeshPro (for the damage number), NUnit EditMode tests, existing `Game.Combat.Core` / `Game.Combat.Tests` assemblies.

## Global Constraints

- Fusion topology is **Host/Server/Client** — only the server/host has state authority. Hit detection runs server-side; feedback must be delivered to the attacker via `[Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]`. (`GameNetworkManager.cs:124,153,174`)
- **No new `[Networked]` state.** The feature adds only RPCs and local cosmetic objects.
- This project never uses `NetworkTransform`; cosmetic objects here are plain (non-networked) `GameObject`s instantiated locally, exactly like `hitMarkerPrefab` and `impactEffect`. (see memory: Fusion no-NetworkTransform pattern)
- Pure logic that can be unit-tested must live in `Game.Combat.Core` (`noEngineReferences: true` — no `UnityEngine` types; use `System.Math` and return plain `float`s), tested in `Game.Combat.Tests`. Mirror `HitCooldownLedger` / `HitCooldownLedgerTests`.
- Null-guard every prefab reference and every resolved object, matching the existing `if (sr != null)` / `if (impactEffect != null)` style.

---

## File Structure

- Create `Assets/Scripts/Combat/Core/FlashCurve.cs` — pure flash-intensity math.
- Create `Assets/Scripts/Combat/Core/DamageNumberMotion.cs` — pure rise/fade math.
- Create `Assets/Tests/EditMode/Combat/FlashCurveTests.cs` — tests for FlashCurve.
- Create `Assets/Tests/EditMode/Combat/DamageNumberMotionTests.cs` — tests for DamageNumberMotion.
- Create `Assets/Scripts/Player/HitFlash.cs` — MonoBehaviour on Enemy + Player prefabs.
- Create `Assets/Scripts/Player/DamageNumber.cs` — MonoBehaviour on the damage-number prefab.
- Create `Assets/Scripts/Player/HitFeedback.cs` — scene-singleton MonoBehaviour holding prefab refs.
- Create prefab `Assets/Scripts/Player/Hit Feedback/DamageNumber.prefab` (TMP + `DamageNumber`).
- Create prefab `Assets/Scripts/Player/Hit Feedback/HitParticles.prefab` (ParticleSystem).
- Modify `Assets/Scripts/Player/PlayerCombat.cs` — add `RPC_HitFeedback`, call it from `ApplyMeleeHits()`.
- Modify `Assets/Scripts/Player/Projectile.cs` — add `RPC_HitFeedback`, call it from `OnTriggerEnter2D()`.
- Modify the game scene — add a `HitFeedback` singleton GameObject; add `HitFlash` to Enemy + Player prefabs.

---

## Task 1: Pure flash & damage-number math (TDD)

**Files:**
- Create: `Assets/Scripts/Combat/Core/FlashCurve.cs`
- Create: `Assets/Scripts/Combat/Core/DamageNumberMotion.cs`
- Test: `Assets/Tests/EditMode/Combat/FlashCurveTests.cs`
- Test: `Assets/Tests/EditMode/Combat/DamageNumberMotionTests.cs`

**Interfaces:**
- Produces: `Game.Combat.Core.FlashCurve.Intensity(float elapsed, float duration) -> float` (returns 1 at elapsed 0, linearly to 0 at elapsed ≥ duration, clamped to [0,1]).
- Produces: `Game.Combat.Core.DamageNumberMotion.YOffset(float elapsed, float riseSpeed) -> float` and `Game.Combat.Core.DamageNumberMotion.Alpha(float elapsed, float lifetime) -> float` (alpha 1 → 0 over lifetime, clamped; YOffset = riseSpeed * elapsed).
- These live in the existing `Game.Combat.Core` asmdef (already present — no asmdef edits needed). Tests live in the existing `Game.Combat.Tests` asmdef.

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/Combat/FlashCurveTests.cs`:

```csharp
using NUnit.Framework;
using Game.Combat.Core;

public class FlashCurveTests
{
    [Test]
    public void Intensity_AtStart_IsOne()
    {
        Assert.AreEqual(1f, FlashCurve.Intensity(0f, 0.1f), 1e-4f);
    }

    [Test]
    public void Intensity_AtHalf_IsHalf()
    {
        Assert.AreEqual(0.5f, FlashCurve.Intensity(0.05f, 0.1f), 1e-4f);
    }

    [Test]
    public void Intensity_AtEnd_IsZero()
    {
        Assert.AreEqual(0f, FlashCurve.Intensity(0.1f, 0.1f), 1e-4f);
    }

    [Test]
    public void Intensity_PastEnd_ClampsToZero()
    {
        Assert.AreEqual(0f, FlashCurve.Intensity(0.5f, 0.1f), 1e-4f);
    }

    [Test]
    public void Intensity_ZeroOrNegativeDuration_IsZero()
    {
        Assert.AreEqual(0f, FlashCurve.Intensity(0f, 0f), 1e-4f);
    }
}
```

Create `Assets/Tests/EditMode/Combat/DamageNumberMotionTests.cs`:

```csharp
using NUnit.Framework;
using Game.Combat.Core;

public class DamageNumberMotionTests
{
    [Test]
    public void YOffset_GrowsLinearlyWithTime()
    {
        Assert.AreEqual(0f, DamageNumberMotion.YOffset(0f, 1f), 1e-4f);
        Assert.AreEqual(0.5f, DamageNumberMotion.YOffset(0.5f, 1f), 1e-4f);
    }

    [Test]
    public void Alpha_AtStart_IsOne()
    {
        Assert.AreEqual(1f, DamageNumberMotion.Alpha(0f, 0.7f), 1e-4f);
    }

    [Test]
    public void Alpha_AtEnd_IsZero()
    {
        Assert.AreEqual(0f, DamageNumberMotion.Alpha(0.7f, 0.7f), 1e-4f);
    }

    [Test]
    public void Alpha_PastEnd_ClampsToZero()
    {
        Assert.AreEqual(0f, DamageNumberMotion.Alpha(2f, 0.7f), 1e-4f);
    }

    [Test]
    public void Alpha_ZeroOrNegativeLifetime_IsZero()
    {
        Assert.AreEqual(0f, DamageNumberMotion.Alpha(0f, 0f), 1e-4f);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run the EditMode suite (Unity Test Runner, or headless):
```
Unity -runTests -testPlatform EditMode -projectPath . -testFilter "FlashCurveTests|DamageNumberMotionTests"
```
Expected: FAIL — `FlashCurve` / `DamageNumberMotion` do not exist (compile error / type not found).

(If Unity holds the project lock, compile with the bundled Roslyn per the "Unity-locked verification workaround" memory.)

- [ ] **Step 3: Write minimal implementations**

Create `Assets/Scripts/Combat/Core/FlashCurve.cs`:

```csharp
using System;

namespace Game.Combat.Core
{
    /// <summary>
    /// Pure, engine-free flash intensity. Returns 1 at the moment of impact and
    /// decays linearly to 0 over <paramref name="duration"/> seconds. Callers
    /// map this to Color.Lerp(baseColor, white, intensity). Unit-testable.
    /// </summary>
    public static class FlashCurve
    {
        public static float Intensity(float elapsed, float duration)
        {
            if (duration <= 0f) return 0f;
            float t = 1f - (elapsed / duration);
            if (t < 0f) return 0f;
            if (t > 1f) return 1f;
            return t;
        }
    }
}
```

Create `Assets/Scripts/Combat/Core/DamageNumberMotion.cs`:

```csharp
namespace Game.Combat.Core
{
    /// <summary>
    /// Pure, engine-free motion for a floating damage number: constant upward
    /// drift plus a linear alpha fade over its lifetime. Unit-testable.
    /// </summary>
    public static class DamageNumberMotion
    {
        public static float YOffset(float elapsed, float riseSpeed) => riseSpeed * elapsed;

        public static float Alpha(float elapsed, float lifetime)
        {
            if (lifetime <= 0f) return 0f;
            float a = 1f - (elapsed / lifetime);
            if (a < 0f) return 0f;
            if (a > 1f) return 1f;
            return a;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run the same filter as Step 2.
Expected: PASS — all 10 tests green.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Combat/Core/FlashCurve.cs" \
        "Assets/Scripts/Combat/Core/DamageNumberMotion.cs" \
        "Assets/Tests/EditMode/Combat/FlashCurveTests.cs" \
        "Assets/Tests/EditMode/Combat/DamageNumberMotionTests.cs"
git commit -m "feat(combat): pure flash + damage-number motion helpers with tests"
```

> **Note:** Unity generates `.meta` files for the new `.cs` files on next editor focus. Commit those in the same commit if the editor is open, or in the next commit if working headless.

---

## Task 2: HitFlash MonoBehaviour

**Files:**
- Create: `Assets/Scripts/Player/HitFlash.cs`
- Modify (in editor): Enemy prefabs + `PlayerPrefab.prefab` — add `HitFlash` component wired to the visible body `SpriteRenderer`.

**Interfaces:**
- Consumes: `Game.Combat.Core.FlashCurve.Intensity(float, float)` (Task 1).
- Produces: `HitFlash.PlayFlash()` — public, called by `HitFeedback` (Task 4). Component discovered via `GetComponentInChildren<HitFlash>()`.

- [ ] **Step 1: Write the component**

Create `Assets/Scripts/Player/HitFlash.cs`:

```csharp
using System.Collections;
using UnityEngine;
using Game.Combat.Core;

/// <summary>
/// Brief white flash on a target's SpriteRenderer when it is hit. Cosmetic and
/// local — spawned by HitFeedback on the attacker's client only. The coroutine
/// lives on the target so rapid repeated hits simply restart it, and the base
/// color is captured once so a hit mid-flash still restores correctly.
/// </summary>
public class HitFlash : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private float flashDuration = 0.1f;

    private Color baseColor;
    private bool baseColorCaptured;
    private Coroutine running;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            baseColor = spriteRenderer.color;
            baseColorCaptured = true;
        }
    }

    public void PlayFlash()
    {
        if (spriteRenderer == null || !baseColorCaptured) return;
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            float t = FlashCurve.Intensity(elapsed, flashDuration);
            spriteRenderer.color = Color.Lerp(baseColor, Color.white, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        spriteRenderer.color = baseColor;
        running = null;
    }
}
```

- [ ] **Step 2: Verify it compiles**

Focus the Unity editor (or compile headless per the "Unity-locked verification workaround" memory). Expected: no compile errors; `HitFlash` appears as an addable component.

- [ ] **Step 3: Wire onto prefabs (manual editor step)**

For each of the 7 enemy prefabs under `Assets/Scripts/Enemy/Prefabs/` and for `Assets/Scripts/Player/PlayerPrefab.prefab`:
1. Add the `HitFlash` component to the prefab root.
2. Drag the **visible body** `SpriteRenderer` into the `spriteRenderer` field. For the player, this is the body renderer (NOT the weapon child — see memory "PlayerPrefab has two Animators"; the body renderer sits under the same object as `Player.controller`). If left empty, `Awake` falls back to `GetComponentInChildren<SpriteRenderer>()`, which may pick the wrong child — so set it explicitly.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/Player/HitFlash.cs" \
        "Assets/Scripts/Enemy/Prefabs" \
        "Assets/Scripts/Player/PlayerPrefab.prefab"
git commit -m "feat(combat): HitFlash component + wire onto enemy/player prefabs"
```

---

## Task 3: DamageNumber MonoBehaviour + prefab

**Files:**
- Create: `Assets/Scripts/Player/DamageNumber.cs`
- Create (in editor): `Assets/Scripts/Player/Hit Feedback/DamageNumber.prefab`

**Interfaces:**
- Consumes: `Game.Combat.Core.DamageNumberMotion.YOffset(float, float)` and `.Alpha(float, float)` (Task 1).
- Produces: `DamageNumber.Init(int amount)` — called by `HitFeedback` (Task 4) immediately after instantiating the prefab.

- [ ] **Step 1: Write the component**

Create `Assets/Scripts/Player/DamageNumber.cs`:

```csharp
using TMPro;
using UnityEngine;
using Game.Combat.Core;

/// <summary>
/// World-space floating damage number. Spawned locally on the attacker's client
/// by HitFeedback. Drifts upward and fades out, then destroys itself. The rise
/// and fade math is the pure DamageNumberMotion helper (unit-tested).
/// </summary>
public class DamageNumber : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private float lifetime = 0.7f;
    [SerializeField] private float riseSpeed = 1f;

    private Vector3 startPos;
    private float elapsed;
    private Color baseColor;

    public void Init(int amount)
    {
        if (label == null) label = GetComponentInChildren<TMP_Text>();
        if (label != null)
        {
            label.text = amount.ToString();
            baseColor = label.color;
        }
        startPos = transform.position;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        transform.position = startPos + Vector3.up * DamageNumberMotion.YOffset(elapsed, riseSpeed);

        if (label != null)
        {
            Color c = baseColor;
            c.a = DamageNumberMotion.Alpha(elapsed, lifetime);
            label.color = c;
        }

        if (elapsed >= lifetime) Destroy(gameObject);
    }
}
```

- [ ] **Step 2: Verify it compiles**

Focus the editor / compile headless. Expected: no errors. (TMPro is already used in the project — `Assets/Scripts/Coin Scripts/UIManager.cs`, `CTFGameManager.cs`.)

- [ ] **Step 3: Build the prefab (manual editor step)**

1. Create an empty GameObject `DamageNumber`.
2. Add a child with a `TextMeshPro` (3D / world-space, the `TextMeshPro` component — not `TextMeshProUGUI`, so it renders in world space without a Canvas). Set a default font, size, a bright color (e.g. white or yellow), center alignment.
3. Add the `DamageNumber` component to the root; drag the child `TMP_Text` into its `label` field.
4. Set the root's sorting so it renders above sprites (on the `TextMeshPro` renderer, set Sorting Layer / Order to appear above the play field).
5. Save as `Assets/Scripts/Player/Hit Feedback/DamageNumber.prefab`.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/Player/DamageNumber.cs" \
        "Assets/Scripts/Player/Hit Feedback/DamageNumber.prefab"
git commit -m "feat(combat): floating DamageNumber component + prefab"
```

---

## Task 4: HitFeedback singleton + particle prefab + scene wiring

**Files:**
- Create: `Assets/Scripts/Player/HitFeedback.cs`
- Create (in editor): `Assets/Scripts/Player/Hit Feedback/HitParticles.prefab`
- Modify (in editor): the game scene — add one `HitFeedback` GameObject.

**Interfaces:**
- Consumes: `DamageNumber.Init(int)` (Task 3), `HitFlash.PlayFlash()` (Task 2).
- Produces: `HitFeedback.Instance` (static) and `HitFeedback.Play(GameObject target, Vector2 hitPoint, int damage)` — called by the RPC handlers in Tasks 5 & 6.

- [ ] **Step 1: Write the singleton**

Create `Assets/Scripts/Player/HitFeedback.cs`:

```csharp
using UnityEngine;

/// <summary>
/// Scene singleton that plays attacker-only hit feedback: an impact particle
/// burst, a floating damage number, and a flash on the target sprite. Holds the
/// prefab references so they are wired once in the scene rather than on every
/// player and projectile prefab. Invoked from the InputAuthority-targeted
/// RPC handlers in PlayerCombat and Projectile.
/// </summary>
public class HitFeedback : MonoBehaviour
{
    public static HitFeedback Instance { get; private set; }

    [SerializeField] private GameObject particleBurstPrefab;
    [SerializeField] private GameObject damageNumberPrefab;
    [SerializeField] private float particleLifetime = 2f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Play the three cosmetic effects. Each is independently null-guarded, so a
    /// missing prefab or a culled/despawned target simply skips that one effect.
    /// </summary>
    public void Play(GameObject target, Vector2 hitPoint, int damage)
    {
        if (particleBurstPrefab != null)
        {
            GameObject fx = Instantiate(particleBurstPrefab, hitPoint, Quaternion.identity);
            Destroy(fx, particleLifetime);
        }

        if (damageNumberPrefab != null)
        {
            GameObject num = Instantiate(damageNumberPrefab, hitPoint, Quaternion.identity);
            DamageNumber dn = num.GetComponent<DamageNumber>();
            if (dn != null) dn.Init(damage);
        }

        if (target != null)
        {
            HitFlash flash = target.GetComponentInChildren<HitFlash>();
            if (flash != null) flash.PlayFlash();
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

Focus the editor / compile headless. Expected: no errors.

- [ ] **Step 3: Build the particle prefab (manual editor step)**

1. Create a GameObject with a `ParticleSystem`. Configure a short one-shot burst: `Duration` ~0.5s, `Looping` off, a small `Start Speed`, a `Burst` of ~8–12 particles at time 0, short `Start Lifetime` (~0.3s), a spark-ish color.
2. Save as `Assets/Scripts/Player/Hit Feedback/HitParticles.prefab`.
3. (Optional) Set `HitFeedback.particleLifetime` to comfortably exceed the system duration so `Destroy` doesn't cut particles short.

- [ ] **Step 4: Place the singleton in the scene (manual editor step)**

1. Open the main gameplay scene.
2. Create an empty GameObject named `HitFeedback`, add the `HitFeedback` component.
3. Drag `HitParticles.prefab` into `particleBurstPrefab` and `DamageNumber.prefab` into `damageNumberPrefab`.
4. Save the scene.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Player/HitFeedback.cs" \
        "Assets/Scripts/Player/Hit Feedback/HitParticles.prefab" \
        "Assets/Scenes"
git commit -m "feat(combat): HitFeedback singleton + particle prefab + scene wiring"
```

> Adjust the `Assets/Scenes` path to wherever the main scene lives if different.

---

## Task 5: Wire RPC into PlayerCombat (melee + dash-strike)

**Files:**
- Modify: `Assets/Scripts/Player/PlayerCombat.cs` — enemy branch (~line 212) and player branch (~line 229) of `ApplyMeleeHits()`; add `RPC_HitFeedback`.

**Interfaces:**
- Consumes: `HitFeedback.Instance.Play(GameObject, Vector2, int)` (Task 4).
- Produces: `PlayerCombat.RPC_HitFeedback(NetworkId, Vector2, int)`.

**Context:** `PlayerCombat` is a `NetworkBehaviour`; `Object.InputAuthority` is the attacking player. `ApplyMeleeHits` runs under `HasStateAuthority` only (server), so the RPC source is correct. Both `Enemy` and `PlayerStatsHandler` are `NetworkBehaviour`s exposing `Object.Id`. Dash-strike routes through this same method, so it is covered automatically.

- [ ] **Step 1: Add the RPC handler**

Add this method to `PlayerCombat` (near the other RPC/effect code):

```csharp
/// <summary>
/// Attacker-only hit feedback. Server calls this on the attacker's client after
/// a landed melee hit; it resolves the target locally and plays cosmetic FX.
/// No networked state — mirrors Projectile.RPC_Impact / Enemy.RPC_TakeDamage.
/// </summary>
[Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
private void RPC_HitFeedback(NetworkId targetId, Vector2 hitPoint, int damage)
{
    if (HitFeedback.Instance == null) return;
    GameObject targetGo = null;
    if (Runner.TryFindObject(targetId, out NetworkObject targetObj) && targetObj != null)
        targetGo = targetObj.gameObject;
    HitFeedback.Instance.Play(targetGo, hitPoint, damage);
}
```

> `NetworkRunner.TryFindObject(NetworkId, out NetworkObject)` returns false if the id is unknown/culled on this client; the particle burst and number still play at `hitPoint`, only the flash is skipped.

- [ ] **Step 2: Call it in the enemy branch**

In `ApplyMeleeHits()`, enemy branch, immediately after `enemy.TakeDamage(finalDamage, knockbackForce, hit.transform.position);` (currently `PlayerCombat.cs:212`), before `continue;`:

```csharp
                enemy.TakeDamage(finalDamage, knockbackForce, hit.transform.position);
                RPC_HitFeedback(enemy.Object.Id, hit.transform.position, finalDamage);
                continue;
```

- [ ] **Step 3: Call it in the player branch**

In `ApplyMeleeHits()`, player branch, immediately after `targetPlayer.ServerApplyDamage(finalDamage, Object.Id);` (currently `PlayerCombat.cs:229`):

```csharp
                int finalDamage = ResolveMeleeDamage(hit.gameObject, hit.transform.position);
                targetPlayer.ServerApplyDamage(finalDamage, Object.Id);
                RPC_HitFeedback(targetPlayer.Object.Id, hit.transform.position, finalDamage);
```

- [ ] **Step 4: Verify it compiles**

Focus the editor / compile headless. Expected: no errors; Fusion CodeGen regenerates the RPC surrogate without warnings.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Player/PlayerCombat.cs"
git commit -m "feat(combat): fire attacker-only hit feedback on melee + dash-strike hits"
```

---

## Task 6: Wire RPC into Projectile

**Files:**
- Modify: `Assets/Scripts/Player/Projectile.cs` — enemy branch (~line 107) and player branch (~line 91) of `OnTriggerEnter2D()`; add `RPC_HitFeedback`.

**Interfaces:**
- Consumes: `HitFeedback.Instance.Play(GameObject, Vector2, int)` (Task 4).
- Produces: `Projectile.RPC_HitFeedback(NetworkId, Vector2, int)`.

**Context:** `Projectile` is a `NetworkBehaviour`; `Object.InputAuthority` is the shooter. `OnTriggerEnter2D` runs under `HasStateAuthority` only. The RPC must be sent **before** `Hit()` despawns the projectile — the exact ordering `RPC_Impact` already relies on.

- [ ] **Step 1: Add the RPC handler**

Add to `Projectile` (near `RPC_Impact`):

```csharp
/// <summary>
/// Attacker-only hit feedback, delivered to the shooter's client. Resolves the
/// target locally and plays cosmetic FX. No networked state.
/// </summary>
[Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
private void RPC_HitFeedback(NetworkId targetId, Vector2 hitPoint, int damage)
{
    if (HitFeedback.Instance == null) return;
    GameObject targetGo = null;
    if (Runner.TryFindObject(targetId, out NetworkObject targetObj) && targetObj != null)
        targetGo = targetObj.gameObject;
    HitFeedback.Instance.Play(targetGo, hitPoint, damage);
}
```

- [ ] **Step 2: Call it in the player branch**

In `OnTriggerEnter2D()`, player branch, after `playerStats.ServerApplyDamage(Damage, attackerId);` and before `Hit();` (currently `Projectile.cs:91–97`):

```csharp
                playerStats.ServerApplyDamage(Damage, attackerId);
                RPC_HitFeedback(playerStats.Object.Id, other.transform.position, Damage);
                if (stunPlayers)
                {
                    PlayerMovement pm = other.GetComponent<PlayerMovement>();
                    if (pm != null) pm.ApplyStun(stunDuration);
                }
                Hit();
```

- [ ] **Step 3: Call it in the enemy branch**

In `OnTriggerEnter2D()`, enemy branch, after `enemy.TakeDamage(Damage, dir * 5f, other.transform.position);` and before `Hit();` (currently `Projectile.cs:107–108`):

```csharp
            enemy.TakeDamage(Damage, dir * 5f, other.transform.position);
            RPC_HitFeedback(enemy.Object.Id, other.transform.position, Damage);
            Hit();
```

- [ ] **Step 4: Verify it compiles**

Focus the editor / compile headless. Expected: no errors; Fusion CodeGen clean.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Player/Projectile.cs"
git commit -m "feat(combat): fire attacker-only hit feedback on projectile hits"
```

---

## Task 7: Multi-peer manual verification

**Files:** none (verification only).

This feature's runtime behavior (RPC delivery, prefab instantiation, flash on the correct sprite) cannot be exercised by EditMode tests — it needs a live `NetworkRunner`. Verify manually.

- [ ] **Step 1: Run a host + one client**

Start one Host peer and one Client peer (per the project's normal multi-peer run setup). Both join a match with enemies present.

- [ ] **Step 2: Verify host's attacks**

As the **host** player: melee an AI enemy, land a dash-strike (Quicker Dash tier 3), fire a projectile into an enemy, and hit an enemy player.
Expected on the host's screen for each: particle burst at the hit point, a brief white flash on the target sprite, and a rising/fading damage number showing the dealt damage.
Expected on the client's screen: **no** extra feedback from the host's hits (attacker-only).

- [ ] **Step 3: Verify client's attacks**

As the **client** player: repeat the same four attacks.
Expected on the client's screen: all three effects appear.
Expected on the host's screen: no extra feedback from the client's hits.

- [ ] **Step 4: Verify robustness**

- Rapidly hit the same enemy several times: the flash restarts cleanly each hit and the sprite always returns to its base color (no stuck-white sprite).
- Kill an enemy with a hit: no errors if the enemy despawns the same frame (target resolves to null → particles + number still play, flash skipped).

- [ ] **Step 5: Record the result**

Note pass/fail per attack type. If all pass, the feature is complete. If any effect appears on the wrong screen, re-check the RPC target is `RpcTargets.InputAuthority` and that the `HitFeedback` singleton exists in the scene.

---

## Self-Review Notes

- **Spec coverage:** particle burst (Task 4), sprite flash (Tasks 1–2), damage number (Tasks 1, 3), attacker-only RPC delivery (Tasks 5–6), enemies + players + dash-strike + projectiles (Tasks 5–6), no `[Networked]` state (RPC-only). All covered.
- **Type consistency:** `RPC_HitFeedback(NetworkId, Vector2, int)`, `HitFeedback.Play(GameObject, Vector2, int)`, `DamageNumber.Init(int)`, `HitFlash.PlayFlash()`, `FlashCurve.Intensity(float,float)`, `DamageNumberMotion.YOffset/Alpha(float,float)` — used consistently across tasks.
- **API confirmed:** `NetworkRunner.TryFindObject(NetworkId, out NetworkObject)` exists in this Fusion version (`Assets/Photon/Fusion/Assemblies/Fusion.Runtime.xml:14027`), alongside `FindObject(NetworkId)` (`:14020`). The plan uses `TryFindObject`.
