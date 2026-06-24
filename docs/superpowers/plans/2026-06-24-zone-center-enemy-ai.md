# Zone-Bound, Center-Scaled Enemy AI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the enemy AI with one data-driven system: enemies wander a home zone, engage the nearest nearby player, leash back home, and are made tougher (health/damage/speed) at spawn based on how close their zone is to map center.

**Architecture:** Keep the existing component split (`Enemy` NetworkBehaviour, `EnemyAI` brain, `EnemyStats` SO, spawner, team component). Add a shared `DifficultyRingConfig` SO and an `ArenaCenter` scene singleton. `Enemy.Spawned` resolves a difficulty ring from `distance(home, center)` and applies multipliers once. `EnemyAI` becomes a leashed state machine (Guard/wander → Chase → Telegraph → Attack → Return). Pure decision logic (ring lookup, leash math) lives in a small dedicated runtime assembly so it can be unit-tested in EditMode.

**Tech Stack:** Unity 6, Photon Fusion 2 (authority-only sim, no NetworkTransform — proxies interpolate via NetworkRigidbody2D + existing `[Networked]` visual bools), C#, Unity Test Framework 1.6.0 (NUnit, EditMode).

## Global Constraints

- **Authority-only simulation.** The AI state machine, difficulty resolution, and Rigidbody2D writes run ONLY on `HasStateAuthority`. Proxies read networked visual state in `Render()`. Never add a `NetworkTransform`.
- **Targeting: players only.** Engage non-stealthed, living players within range. Retain the existing `PlayerStatsHandler.IsPlayerDead()` and `PlayerBuffs.IsStealthed` checks. Never target other AI.
- **Only health, attack damage, and move speed scale with center proximity.** Detection range, leash radius, attack range, and attack cooldown do NOT scale.
- **Difficulty is static per enemy**, computed once in `Enemy.Spawned` from the enemy's own home position. No per-tick scaling, no new networked state for it.
- **Damage still flows through `CombatConfig.ResolveDamage`** (the single damage entry point). The only change is that the base damage passed in becomes the effective (ring-scaled) value.
- **Fusion timing uses `TickTimer`** (e.g. `TickTimer.CreateFromSeconds(Runner, seconds)`), never `Time.time` for simulation decisions. `Time.time` is allowed only for cosmetic Render-path effects (the telegraph flash already does this).
- **No new third-party dependencies.**

---

## File Structure

**New runtime assembly (isolated, unit-testable pure logic):**
- `Assets/Scripts/Enemy/AI/Game.EnemyAI.asmdef` — runtime assembly, `autoReferenced: true` so `Assembly-CSharp` can use its types.
- `Assets/Scripts/Enemy/AI/DifficultyRingConfig.cs` — `RingTier` struct + `DifficultyRingConfig` SO with pure `GetRing(distance)`.
- `Assets/Scripts/Enemy/AI/EnemyAILeash.cs` — static pure helpers: leash clamp + disengage decision.

**New EditMode test assembly:**
- `Assets/Tests/EditMode/Game.EnemyAI.Tests.asmdef`
- `Assets/Tests/EditMode/DifficultyRingConfigTests.cs`
- `Assets/Tests/EditMode/EnemyAILeashTests.cs`

**New gameplay code (stays in `Assembly-CSharp`):**
- `Assets/Scripts/Enemy/Base/ArenaCenter.cs` — scene singleton marking map center.

**Modified (all in `Assembly-CSharp`):**
- `Assets/Scripts/Enemy/Base/EnemyStats.cs` — add range fields.
- `Assets/Scripts/ScriptableObjects/Game Settings Manager.cs` — hold + expose the `DifficultyRingConfig`.
- `Assets/Scripts/Enemy/Base/Enemy.cs` — resolve ring & effective stats in `Spawned`; use effective values.
- `Assets/Scripts/Enemy/Base/EnemyAI.cs` — full rewrite to leashed wander/chase state machine.
- `Assets/Scripts/Enemy/Base/EnemySpawner.cs` — remove patrol-point creation/assignment.

**Note on `.meta` files:** Unity generates `.meta` files on import for every new asset (scripts, asmdefs). After creating files, let Unity import (focus the editor) so `.meta` files are created, then commit the `.cs`/`.asmdef` **and** their `.meta` files together.

---

## Task 1: Testable difficulty-ring config + test harness

Sets up the new runtime assembly, the EditMode test assembly, and the `DifficultyRingConfig` SO with a tested `GetRing`. Assembly setup is folded in here because the first test needs it.

**Files:**
- Create: `Assets/Scripts/Enemy/AI/Game.EnemyAI.asmdef`
- Create: `Assets/Scripts/Enemy/AI/DifficultyRingConfig.cs`
- Create: `Assets/Tests/EditMode/Game.EnemyAI.Tests.asmdef`
- Create: `Assets/Tests/EditMode/DifficultyRingConfigTests.cs`

**Interfaces:**
- Produces:
  - `struct RingTier { public float maxDistanceFromCenter; public float healthMult; public float damageMult; public float speedMult; }`
  - `static RingTier RingTier.Identity` → `{ maxDistanceFromCenter = float.MaxValue, healthMult = 1f, damageMult = 1f, speedMult = 1f }`
  - `class DifficultyRingConfig : ScriptableObject { public RingTier[] rings; public RingTier GetRing(float distance); }`

- [ ] **Step 1: Create the runtime assembly definition**

Create `Assets/Scripts/Enemy/AI/Game.EnemyAI.asmdef`:

```json
{
    "name": "Game.EnemyAI",
    "rootNamespace": "",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Create the EditMode test assembly definition**

Create `Assets/Tests/EditMode/Game.EnemyAI.Tests.asmdef`:

```json
{
    "name": "Game.EnemyAI.Tests",
    "rootNamespace": "",
    "references": [
        "Game.EnemyAI",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 3: Write the failing tests**

Create `Assets/Tests/EditMode/DifficultyRingConfigTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

public class DifficultyRingConfigTests
{
    // rings ordered INNER -> OUTER (ascending maxDistanceFromCenter)
    private static DifficultyRingConfig MakeConfig()
    {
        var config = ScriptableObject.CreateInstance<DifficultyRingConfig>();
        config.rings = new[]
        {
            new RingTier { maxDistanceFromCenter = 10f, healthMult = 3f, damageMult = 3f, speedMult = 1.5f },
            new RingTier { maxDistanceFromCenter = 25f, healthMult = 2f, damageMult = 2f, speedMult = 1.25f },
            new RingTier { maxDistanceFromCenter = 50f, healthMult = 1f, damageMult = 1f, speedMult = 1f },
        };
        return config;
    }

    [Test]
    public void GetRing_AtCenter_ReturnsInnermostToughestRing()
    {
        var ring = MakeConfig().GetRing(0f);
        Assert.AreEqual(3f, ring.healthMult);
    }

    [Test]
    public void GetRing_OnBandBoundary_ReturnsThatBand()
    {
        // distance exactly == a band's max belongs to that band (>= comparison)
        var ring = MakeConfig().GetRing(10f);
        Assert.AreEqual(3f, ring.healthMult);
    }

    [Test]
    public void GetRing_MidBand_ReturnsContainingBand()
    {
        var ring = MakeConfig().GetRing(20f);
        Assert.AreEqual(2f, ring.healthMult);
    }

    [Test]
    public void GetRing_BeyondOutermost_ClampsToOutermostBaseline()
    {
        var ring = MakeConfig().GetRing(9999f);
        Assert.AreEqual(1f, ring.healthMult);
        Assert.AreEqual(1f, ring.speedMult);
    }

    [Test]
    public void GetRing_EmptyConfig_ReturnsIdentity()
    {
        var config = ScriptableObject.CreateInstance<DifficultyRingConfig>();
        config.rings = new RingTier[0];
        var ring = config.GetRing(5f);
        Assert.AreEqual(1f, ring.healthMult);
        Assert.AreEqual(1f, ring.damageMult);
        Assert.AreEqual(1f, ring.speedMult);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

In Unity: **Window → General → Test Runner → EditMode → Run All**.
Expected: compile error / FAIL — `DifficultyRingConfig` and `RingTier` do not exist yet.

(CLI alternative, only if the editor is closed — replace `<UnityVersion>`:
`"C:/Program Files/Unity/Hub/Editor/<UnityVersion>/Editor/Unity.exe" -runTests -batchmode -projectPath "C:/Users/1/Documents/GitHub/2dGame" -testPlatform EditMode -testResults "C:/Users/1/Documents/GitHub/2dGame/TestResults.xml"`)

- [ ] **Step 5: Implement `DifficultyRingConfig`**

Create `Assets/Scripts/Enemy/AI/DifficultyRingConfig.cs`:

```csharp
using UnityEngine;

/// <summary>
/// One concentric difficulty band. Bands are authored INNER -> OUTER
/// (ascending <see cref="maxDistanceFromCenter"/>). Multipliers apply to the
/// enemy's base stats at spawn; near-center bands are the toughest.
/// </summary>
[System.Serializable]
public struct RingTier
{
    [Tooltip("Upper bound (inclusive) of this band's distance-from-center range.")]
    public float maxDistanceFromCenter;
    public float healthMult;
    public float damageMult;
    public float speedMult;

    /// <summary>Neutral 1.0x band used when no config/center is available.</summary>
    public static RingTier Identity => new RingTier
    {
        maxDistanceFromCenter = float.MaxValue,
        healthMult = 1f,
        damageMult = 1f,
        speedMult = 1f
    };
}

/// <summary>
/// Shared, single-asset difficulty curve. Maps an enemy's distance from map
/// center to stat multipliers via discrete concentric rings.
/// </summary>
[CreateAssetMenu(fileName = "DifficultyRingConfig", menuName = "Enemy/Difficulty Ring Config")]
public class DifficultyRingConfig : ScriptableObject
{
    [Tooltip("Concentric bands, ordered INNER -> OUTER (ascending Max Distance From Center).")]
    public RingTier[] rings;

    /// <summary>
    /// Returns the multipliers for the given distance from center. Picks the first
    /// band whose maxDistanceFromCenter &gt;= distance (innermost match wins).
    /// Distances beyond the outermost band clamp to it. Empty config returns Identity.
    /// </summary>
    public RingTier GetRing(float distance)
    {
        if (rings == null || rings.Length == 0)
        {
            return RingTier.Identity;
        }

        for (int i = 0; i < rings.Length; i++)
        {
            if (distance <= rings[i].maxDistanceFromCenter)
            {
                return rings[i];
            }
        }

        // Beyond the outermost authored band: clamp to the outermost band.
        return rings[rings.Length - 1];
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

In Unity Test Runner: **EditMode → Run All**.
Expected: all 5 `DifficultyRingConfigTests` PASS.

- [ ] **Step 7: Commit**

```bash
git add "Assets/Scripts/Enemy/AI" "Assets/Tests/EditMode"
git commit -m "feat(enemy-ai): difficulty ring config with tested GetRing"
```

---

## Task 2: Leash decision helpers (pure, tested)

**Files:**
- Create: `Assets/Scripts/Enemy/AI/EnemyAILeash.cs`
- Create: `Assets/Tests/EditMode/EnemyAILeashTests.cs`

**Interfaces:**
- Consumes: nothing (pure math, `UnityEngine.Vector2` only).
- Produces:
  - `static Vector2 EnemyAILeash.ClampToLeash(Vector2 home, Vector2 target, float leashRadius)` — returns `target` if within `leashRadius` of `home`, else the point on the leash circle nearest `target`.
  - `static bool EnemyAILeash.ShouldDisengage(Vector2 enemyPos, Vector2 home, Vector2 target, float detectionRange, float leashRadius)` — true if `target` is farther than `detectionRange` from `enemyPos`, OR farther than `leashRadius` from `home`.

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/EnemyAILeashTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

public class EnemyAILeashTests
{
    [Test]
    public void ClampToLeash_TargetInsideLeash_ReturnsTarget()
    {
        var home = Vector2.zero;
        var target = new Vector2(3f, 0f);
        var result = EnemyAILeash.ClampToLeash(home, target, 5f);
        Assert.AreEqual(target, result);
    }

    [Test]
    public void ClampToLeash_TargetBeyondLeash_ReturnsPointOnCircle()
    {
        var home = Vector2.zero;
        var target = new Vector2(10f, 0f);
        var result = EnemyAILeash.ClampToLeash(home, target, 5f);
        Assert.AreEqual(5f, result.x, 0.0001f);
        Assert.AreEqual(0f, result.y, 0.0001f);
    }

    [Test]
    public void ShouldDisengage_TargetWithinBothRanges_False()
    {
        var enemy = new Vector2(1f, 0f);
        var home = Vector2.zero;
        var target = new Vector2(2f, 0f);
        Assert.IsFalse(EnemyAILeash.ShouldDisengage(enemy, home, target, detectionRange: 10f, leashRadius: 8f));
    }

    [Test]
    public void ShouldDisengage_TargetBeyondDetection_True()
    {
        var enemy = new Vector2(1f, 0f);
        var home = Vector2.zero;
        var target = new Vector2(20f, 0f);
        Assert.IsTrue(EnemyAILeash.ShouldDisengage(enemy, home, target, detectionRange: 10f, leashRadius: 100f));
    }

    [Test]
    public void ShouldDisengage_TargetBeyondLeashFromHome_True()
    {
        var enemy = new Vector2(7f, 0f);
        var home = Vector2.zero;
        var target = new Vector2(9f, 0f); // close to enemy, but beyond leash from home
        Assert.IsTrue(EnemyAILeash.ShouldDisengage(enemy, home, target, detectionRange: 10f, leashRadius: 8f));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Unity Test Runner: **EditMode → Run All**.
Expected: FAIL — `EnemyAILeash` does not exist.

- [ ] **Step 3: Implement `EnemyAILeash`**

Create `Assets/Scripts/Enemy/AI/EnemyAILeash.cs`:

```csharp
using UnityEngine;

/// <summary>
/// Pure leash math for zone-bound enemy AI. No Unity runtime dependencies beyond
/// Vector2, so it is unit-testable in EditMode.
/// </summary>
public static class EnemyAILeash
{
    /// <summary>
    /// Clamps a desired steer target so it never lies outside the leash circle
    /// (radius <paramref name="leashRadius"/> around <paramref name="home"/>).
    /// </summary>
    public static Vector2 ClampToLeash(Vector2 home, Vector2 target, float leashRadius)
    {
        Vector2 offset = target - home;
        if (offset.sqrMagnitude <= leashRadius * leashRadius)
        {
            return target;
        }
        return home + offset.normalized * leashRadius;
    }

    /// <summary>
    /// True when the enemy should stop chasing: the target has left detection
    /// range, or has moved outside the guarded zone (leash radius from home).
    /// </summary>
    public static bool ShouldDisengage(Vector2 enemyPos, Vector2 home, Vector2 target,
                                       float detectionRange, float leashRadius)
    {
        if ((target - enemyPos).sqrMagnitude > detectionRange * detectionRange)
        {
            return true;
        }
        if ((target - home).sqrMagnitude > leashRadius * leashRadius)
        {
            return true;
        }
        return false;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Unity Test Runner: **EditMode → Run All**.
Expected: all `EnemyAILeashTests` + Task 1 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Enemy/AI/EnemyAILeash.cs" "Assets/Tests/EditMode/EnemyAILeashTests.cs"
git commit -m "feat(enemy-ai): pure leash clamp + disengage helpers with tests"
```

---

## Task 3: Extend `EnemyStats` with range fields

Data-only change. No unit test (a plain SO with no logic); verified by compile.

**Files:**
- Modify: `Assets/Scripts/Enemy/Base/EnemyStats.cs`

**Interfaces:**
- Produces (new public fields on `EnemyStats`): `float detectionRange`, `float attackRange`, `float leashRadius`, `float wanderRadius`.

- [ ] **Step 1: Add the range fields**

Replace the `Movement` header block in `Assets/Scripts/Enemy/Base/EnemyStats.cs` so the file's stat section reads:

```csharp
    [Header("Movement")]
    public float moveSpeed = 3f;

    [Header("AI Ranges")]
    [Tooltip("How far the enemy senses players.")]
    public float detectionRange = 10f;

    [Tooltip("How close a player must be to start an attack.")]
    public float attackRange = 1.5f;

    [Tooltip("Hard max distance from home the enemy will ever travel (chase leash).")]
    public float leashRadius = 12f;

    [Tooltip("Area around home the enemy roams while idle. Keep <= leashRadius.")]
    public float wanderRadius = 5f;
```

- [ ] **Step 2: Verify it compiles**

Focus the Unity editor; wait for recompile. Expected: no console errors.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Enemy/Base/EnemyStats.cs"
git commit -m "feat(enemy-ai): add detection/attack/leash/wander ranges to EnemyStats"
```

---

## Task 4: `ArenaCenter` scene singleton

**Files:**
- Create: `Assets/Scripts/Enemy/Base/ArenaCenter.cs`

**Interfaces:**
- Produces: `class ArenaCenter : MonoBehaviour { public static ArenaCenter Instance { get; } public Vector2 Position { get; } }`

- [ ] **Step 1: Implement `ArenaCenter`**

Create `Assets/Scripts/Enemy/Base/ArenaCenter.cs`:

```csharp
using UnityEngine;

/// <summary>
/// Marks the map center used to scale enemy difficulty. Place exactly one in each
/// gameplay scene at the contested center point.
/// </summary>
public class ArenaCenter : MonoBehaviour
{
    public static ArenaCenter Instance { get; private set; }

    /// <summary>World-space center position (XY).</summary>
    public Vector2 Position => transform.position;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"Multiple ArenaCenter instances; keeping {Instance.name}, ignoring {name}.");
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 1f);
    }
}
```

- [ ] **Step 2: Verify it compiles**

Focus the Unity editor; wait for recompile. Expected: no console errors.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Enemy/Base/ArenaCenter.cs"
git commit -m "feat(enemy-ai): ArenaCenter scene singleton for difficulty center"
```

---

## Task 5: Expose `DifficultyRingConfig` from `GameSettingsManager`

Mirrors the existing `combatConfig` / `GetCombatConfig()` pattern.

**Files:**
- Modify: `Assets/Scripts/ScriptableObjects/Game Settings Manager.cs`

**Interfaces:**
- Produces: `DifficultyRingConfig GameSettingsManager.GetDifficultyRingConfig()` (returns the serialized asset or null).

- [ ] **Step 1: Add the serialized field**

In `Assets/Scripts/ScriptableObjects/Game Settings Manager.cs`, directly below the existing combat field:

```csharp
    [Header("Combat Configuration")]
    [SerializeField] private CombatConfig combatConfig;

    [Header("Enemy Difficulty")]
    [Tooltip("Concentric difficulty rings applied to enemies by distance from center.")]
    [SerializeField] private DifficultyRingConfig difficultyRingConfig;
```

- [ ] **Step 2: Add the getter**

Below the existing `GetCombatConfig()` method:

```csharp
    /// <summary>
    /// Get the shared enemy difficulty ring configuration (may be null if unassigned).
    /// </summary>
    public DifficultyRingConfig GetDifficultyRingConfig()
    {
        return difficultyRingConfig;
    }
```

- [ ] **Step 3: Verify it compiles**

Focus the Unity editor; wait for recompile. Expected: no console errors.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/ScriptableObjects/Game Settings Manager.cs"
git commit -m "feat(enemy-ai): expose DifficultyRingConfig via GameSettingsManager"
```

---

## Task 6: Resolve effective stats at spawn in `Enemy`

Authority-only: capture home, resolve ring, apply multipliers, init health, hand off to the AI. Verified by compile + the Task 9 play-test.

**Files:**
- Modify: `Assets/Scripts/Enemy/Base/Enemy.cs`

**Interfaces:**
- Consumes: `DifficultyRingConfig.GetRing(float)`, `RingTier`, `ArenaCenter.Instance.Position`, `GameSettingsManager.GetDifficultyRingConfig()`, and (from Task 7) `EnemyAI.Initialize(Vector2 home, float effectiveMoveSpeed, EnemyStats stats)`.
- Produces: `Vector2 Enemy.Home { get; }`, `int Enemy.GetMaxHealth()` returning the effective max, effective damage used in `AttackPlayer`.

- [ ] **Step 1: Add effective-stat fields**

In `Enemy.cs`, add private fields near the other component fields (after `private EnemyAI ai;`):

```csharp
    // Effective (ring-scaled) stats, resolved once on the authority in Spawned().
    private int effectiveMaxHealth;
    private int effectiveAttackDamage;
    private float effectiveMoveSpeed;

    // Home anchor captured at spawn (authority); the AI leashes to this point.
    public Vector2 Home { get; private set; }
```

- [ ] **Step 2: Resolve the ring and apply it in `Spawned()`**

Replace the health-init block inside `Spawned()` (the `if (stats != null) { if (HasStateAuthority) { CurrentHealth = stats.maxHealth; } } else { ... }`) with a call that also resolves difficulty. The full revised `Spawned()` body:

```csharp
    public override void Spawned()
    {
        // Get components first (needed by both authority and proxies).
        teamComponent = GetComponent<EnemyTeamComponent>();
        rb = GetComponent<Rigidbody2D>();
        ai = GetComponent<EnemyAI>();

        if (stats == null)
        {
            Debug.LogError($"Enemy on {gameObject.name} has no EnemyStats assigned!");
            return;
        }

        if (HasStateAuthority)
        {
            ResolveEffectiveStats();
            CurrentHealth = effectiveMaxHealth;

            if (ai != null)
            {
                ai.Initialize(Home, effectiveMoveSpeed, stats);
            }
        }

        if (coinPrefab == null)
        {
            Debug.LogWarning($"{gameObject.name} has no coin prefab assigned - won't drop coins on death!");
        }
    }

    /// <summary>
    /// Authority-only: capture home and scale base stats by the difficulty ring for
    /// this enemy's distance from the arena center. Falls back to base stats (x1.0)
    /// if the ring config or arena center is missing.
    /// </summary>
    private void ResolveEffectiveStats()
    {
        Home = transform.position;

        RingTier tier = RingTier.Identity;
        DifficultyRingConfig ringConfig = GameSettingsManager.Instance != null
            ? GameSettingsManager.Instance.GetDifficultyRingConfig()
            : null;

        if (ringConfig != null && ArenaCenter.Instance != null)
        {
            float distance = Vector2.Distance(Home, ArenaCenter.Instance.Position);
            tier = ringConfig.GetRing(distance);
        }
        else
        {
            Debug.LogWarning($"{gameObject.name}: no DifficultyRingConfig/ArenaCenter; using base stats.");
        }

        effectiveMaxHealth = Mathf.Max(1, Mathf.RoundToInt(stats.maxHealth * tier.healthMult));
        effectiveAttackDamage = Mathf.Max(0, Mathf.RoundToInt(stats.attackDamage * tier.damageMult));
        effectiveMoveSpeed = stats.moveSpeed * tier.speedMult;
    }
```

(Delete the old `Spawned()` body that this replaces.)

- [ ] **Step 3: Use effective damage in `AttackPlayer`**

In `AttackPlayer`, change the base damage seed from `stats.attackDamage` to `effectiveAttackDamage`. The two affected lines become:

```csharp
        // Calculate damage through the unified pipeline (review item #4).
        int finalDamage = effectiveAttackDamage;
```

and inside the `if (config != null)` block:

```csharp
            finalDamage = config.ResolveDamage(effectiveAttackDamage, myTeam, transform.position,
                                               defenderTeam, player.transform.position);
```

- [ ] **Step 4: Return effective max from `GetMaxHealth`**

Replace `GetMaxHealth`:

```csharp
    public int GetMaxHealth()
    {
        return effectiveMaxHealth;
    }
```

- [ ] **Step 5: Verify it compiles**

Focus the Unity editor; wait for recompile. Expected: a single expected error referencing `ai.Initialize` ONLY if Task 7 is not yet done — that method is defined in Task 7. If implementing strictly in order, do Task 7 before recompiling, or temporarily expect this one error. (When executed in order with subagents, run Step 5's compile check after Task 7.)

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/Enemy/Base/Enemy.cs"
git commit -m "feat(enemy-ai): resolve ring-scaled effective stats at spawn"
```

---

## Task 7: Rewrite `EnemyAI` as a leashed wander/chase state machine

The core behavior change. Replaces patrol A/B with home-anchored wander + leashed chase, reusing the existing telegraph/attack flow. Verified by compile + Task 9 play-test.

**Files:**
- Modify (full rewrite): `Assets/Scripts/Enemy/Base/EnemyAI.cs`

**Interfaces:**
- Consumes: `EnemyAILeash.ClampToLeash`, `EnemyAILeash.ShouldDisengage`, `Enemy.IsKnockedBack()`, `Enemy.Runner`, `Enemy.IsTelegraphing`, `Enemy.FacingLeft`, `Enemy.AttackPlayer(PlayerStatsHandler)`, `PlayerStatsHandler.IsPlayerDead()`, `PlayerBuffs.IsStealthed`.
- Produces: `void EnemyAI.Initialize(Vector2 home, float effectiveMoveSpeed, EnemyStats stats)`, `void EnemyAI.Tick()`, `void EnemyAI.RenderVisuals()`. (`SetPatrolPoints` is removed.)

- [ ] **Step 1: Replace the file contents**

Replace the entire contents of `Assets/Scripts/Enemy/Base/EnemyAI.cs` with:

```csharp
using UnityEngine;
using Fusion;

/// <summary>
/// Networked, zone-bound enemy AI. Wanders around a home anchor, engages the nearest
/// valid player who enters detection range, leashes back to home, and reuses the
/// telegraph/attack flow.
///
/// The state machine and Rigidbody2D are driven ONLY on the state authority via
/// <see cref="Tick"/> (from Enemy.FixedUpdateNetwork). Proxies interpolate position via
/// NetworkRigidbody2D and reproduce facing + telegraph from networked state via
/// <see cref="RenderVisuals"/> (from Enemy.Render).
/// </summary>
public class EnemyAI : MonoBehaviour
{
    [Header("Attack Telegraph Settings")]
    [Tooltip("How long to show the attack warning before attacking (seconds).")]
    [SerializeField] private float attackTelegraphDuration = 0.5f;

    [Tooltip("Color to flash when telegraphing an attack.")]
    [SerializeField] private Color telegraphColor = new Color(1f, 0.3f, 0.3f, 1f);

    [Tooltip("Freeze movement during the attack telegraph?")]
    [SerializeField] private bool freezeDuringTelegraph = true;

    [Header("Wander")]
    [Tooltip("Seconds to pause at a wander point before picking the next one.")]
    [SerializeField] private float wanderPauseDuration = 1f;

    private enum State { Guard, Chasing, Telegraphing, Attacking, Returning }

    // Config resolved at Initialize (authority only).
    private Vector2 home;
    private float moveSpeed;
    private float detectionRange;
    private float attackRange;
    private float leashRadius;
    private float wanderRadius;
    private bool initialized;

    private State currentState = State.Guard;
    private Transform currentPlayer;

    // Wander bookkeeping.
    private Vector2 wanderTarget;
    private TickTimer wanderPauseTimer;
    private bool hasWanderTarget;

    // Components.
    private Rigidbody2D rb;
    private Enemy enemyComponent;
    private SpriteRenderer spriteRenderer;

    // Telegraph.
    private TickTimer telegraphTimer;
    private Color originalColor;

    // Allocation-free detection buffer (authority-only, results consumed immediately).
    private static readonly System.Collections.Generic.List<Collider2D> DetectionResults =
        new System.Collections.Generic.List<Collider2D>(32);
    private ContactFilter2D playerFilter;
    private LayerMask playerLayer;

    [Header("Detection")]
    [Tooltip("Layer mask used to find players.")]
    [SerializeField] private LayerMask playerLayerMask;

    private const float ArriveThreshold = 0.5f;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        enemyComponent = GetComponent<Enemy>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        playerLayer = playerLayerMask;
        playerFilter = new ContactFilter2D { useTriggers = true };
        playerFilter.SetLayerMask(playerLayer);

        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
        else
        {
            Debug.LogWarning($"{gameObject.name}: No SpriteRenderer - telegraph flash disabled.");
        }
    }

    /// <summary>
    /// Authority-only setup from Enemy.Spawned. Captures home + effective speed and the
    /// per-archetype ranges from stats.
    /// </summary>
    public void Initialize(Vector2 homeAnchor, float effectiveMoveSpeed, EnemyStats stats)
    {
        home = homeAnchor;
        moveSpeed = effectiveMoveSpeed;
        if (stats != null)
        {
            detectionRange = stats.detectionRange;
            attackRange = stats.attackRange;
            leashRadius = stats.leashRadius;
            wanderRadius = Mathf.Min(stats.wanderRadius, stats.leashRadius);
        }
        currentState = State.Guard;
        hasWanderTarget = false;
        initialized = true;
    }

    /// <summary>Authority-only AI step (from Enemy.FixedUpdateNetwork).</summary>
    public void Tick()
    {
        if (!initialized || rb == null || enemyComponent == null) return;

        if (enemyComponent.IsKnockedBack()) return;

        if (currentState == State.Telegraphing)
        {
            if (telegraphTimer.Expired(enemyComponent.Runner))
            {
                CompleteTelegraph();
            }
            return;
        }

        switch (currentState)
        {
            case State.Guard:
                Wander();
                AcquireTarget();
                break;

            case State.Chasing:
                ChasePlayer();
                break;

            case State.Attacking:
                Attack();
                break;

            case State.Returning:
                ReturnHome();
                AcquireTarget();
                break;
        }
    }

    /// <summary>Runs on every client (from Enemy.Render): facing + telegraph flash.</summary>
    public void RenderVisuals()
    {
        if (spriteRenderer == null || enemyComponent == null) return;

        spriteRenderer.flipX = enemyComponent.FacingLeft;

        if (enemyComponent.IsTelegraphing)
        {
            float t = Mathf.PingPong(Time.time * 8f, 1f);
            spriteRenderer.color = Color.Lerp(originalColor, telegraphColor, t);
        }
        else
        {
            spriteRenderer.color = originalColor;
        }
    }

    // ---- Guard / wander -------------------------------------------------

    private void Wander()
    {
        // Pausing between wander points.
        if (!wanderPauseTimer.ExpiredOrNotRunning(enemyComponent.Runner))
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        if (!hasWanderTarget)
        {
            PickWanderTarget();
        }

        MoveToward(wanderTarget);

        if (Vector2.Distance(rb.position, wanderTarget) < ArriveThreshold)
        {
            hasWanderTarget = false;
            wanderPauseTimer = TickTimer.CreateFromSeconds(enemyComponent.Runner, wanderPauseDuration);
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
    }

    private void PickWanderTarget()
    {
        Vector2 offset = Random.insideUnitCircle * wanderRadius;
        wanderTarget = home + offset;
        hasWanderTarget = true;
    }

    // ---- Targeting ------------------------------------------------------

    /// <summary>
    /// Find the nearest living, non-stealthed player within detectionRange AND within
    /// leashRadius of home. Enters Chasing if found.
    /// </summary>
    private void AcquireTarget()
    {
        int count = Physics2D.OverlapCircle(transform.position, detectionRange, playerFilter, DetectionResults);

        Transform best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            PlayerStatsHandler player = DetectionResults[i].GetComponent<PlayerStatsHandler>();
            if (player == null || player.IsPlayerDead()) continue;

            PlayerBuffs buffs = DetectionResults[i].GetComponent<PlayerBuffs>();
            if (buffs != null && buffs.IsStealthed) continue;

            Vector2 playerPos = player.transform.position;
            if ((playerPos - home).sqrMagnitude > leashRadius * leashRadius) continue;

            float sqr = (playerPos - (Vector2)transform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = player.transform;
            }
        }

        if (best != null)
        {
            currentPlayer = best;
            currentState = State.Chasing;
        }
    }

    // ---- Chase / attack -------------------------------------------------

    private void ChasePlayer()
    {
        if (currentPlayer == null)
        {
            currentState = State.Returning;
            return;
        }

        Vector2 playerPos = currentPlayer.position;

        if (EnemyAILeash.ShouldDisengage(rb.position, home, playerPos, detectionRange, leashRadius)
            || IsTargetInvalid())
        {
            currentPlayer = null;
            currentState = State.Returning;
            return;
        }

        // Close enough to attack?
        if (Vector2.Distance(transform.position, playerPos) <= attackRange)
        {
            StartTelegraph();
            return;
        }

        Vector2 steer = EnemyAILeash.ClampToLeash(home, playerPos, leashRadius);
        MoveToward(steer);
    }

    private bool IsTargetInvalid()
    {
        if (currentPlayer == null) return true;
        PlayerStatsHandler player = currentPlayer.GetComponent<PlayerStatsHandler>();
        if (player == null || player.IsPlayerDead()) return true;
        PlayerBuffs buffs = currentPlayer.GetComponent<PlayerBuffs>();
        return buffs != null && buffs.IsStealthed;
    }

    private void StartTelegraph()
    {
        currentState = State.Telegraphing;
        telegraphTimer = TickTimer.CreateFromSeconds(enemyComponent.Runner, attackTelegraphDuration);
        enemyComponent.IsTelegraphing = true;

        if (freezeDuringTelegraph)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
    }

    private void CompleteTelegraph()
    {
        enemyComponent.IsTelegraphing = false;

        if (currentPlayer == null || IsTargetInvalid())
        {
            currentPlayer = null;
            currentState = State.Returning;
            return;
        }

        float distance = Vector2.Distance(transform.position, currentPlayer.position);
        currentState = distance <= attackRange ? State.Attacking : State.Chasing;
    }

    private void Attack()
    {
        if (currentPlayer == null)
        {
            currentState = State.Returning;
            return;
        }

        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        PlayerStatsHandler player = currentPlayer.GetComponent<PlayerStatsHandler>();
        if (player != null && enemyComponent != null)
        {
            enemyComponent.AttackPlayer(player);
        }

        currentState = State.Chasing;
    }

    // ---- Return ---------------------------------------------------------

    private void ReturnHome()
    {
        if (Vector2.Distance(rb.position, home) < ArriveThreshold)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            hasWanderTarget = false;
            currentState = State.Guard;
            return;
        }
        MoveToward(home);
    }

    // ---- Movement / facing ---------------------------------------------

    private void MoveToward(Vector2 target)
    {
        Vector2 direction = (target - rb.position).normalized;
        rb.linearVelocity = new Vector2(direction.x * moveSpeed, rb.linearVelocity.y);
        SetFacing(direction.x);
    }

    private void SetFacing(float directionX)
    {
        if (enemyComponent == null) return;
        if (directionX > 0f) enemyComponent.FacingLeft = false;
        else if (directionX < 0f) enemyComponent.FacingLeft = true;
    }

    private void OnDrawGizmosSelected()
    {
        Vector2 center = Application.isPlaying && initialized ? home : (Vector2)transform.position;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(center, detectionRange > 0f ? detectionRange : 10f);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange > 0f ? attackRange : 1.5f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(center, leashRadius > 0f ? leashRadius : 12f);

        Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(center, wanderRadius > 0f ? wanderRadius : 5f);
    }
}
```

- [ ] **Step 2: Verify it compiles**

Focus the Unity editor; wait for recompile. Expected: no console errors across `Enemy.cs` + `EnemyAI.cs` (Task 6's `ai.Initialize` now resolves).

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Enemy/Base/EnemyAI.cs"
git commit -m "feat(enemy-ai): leashed wander/chase state machine replacing patrol A/B"
```

---

## Task 8: Simplify `NetworkedEnemySpawner`

Remove patrol-point creation/assignment (the `Enemy` now captures home itself). Keep team/territory assignment and spawn limiting.

**Files:**
- Modify: `Assets/Scripts/Enemy/Base/EnemySpawner.cs`

**Interfaces:**
- Consumes: nothing new. Drops all references to `EnemyAI.SetPatrolPoints` (removed in Task 7).

- [ ] **Step 1: Remove patrol-point fields**

Delete the entire `[Header("Patrol Points")]` block and the relative-patrol fields:

```csharp
    [Header("Patrol Points")]
    [Tooltip("Assign patrol points for enemies spawned from this spawner")]
    [SerializeField] private Transform patrolPointA;
    [SerializeField] private Transform patrolPointB;

    [Tooltip("Create patrol points relative to spawn position (if manual points not assigned)")]
    [SerializeField] private bool useRelativePatrolPoints = true;
    [SerializeField] private Vector2 relativePointA = new Vector2(-5f, 0f);
    [SerializeField] private Vector2 relativePointB = new Vector2(5f, 0f);
```

Also delete the two cached transform fields:

```csharp
    private Transform autoPatrolPointA;
    private Transform autoPatrolPointB;
```

- [ ] **Step 2: Remove patrol-point creation from `Spawned()`**

Delete this block from `Spawned()`:

```csharp
        // Create automatic patrol points if using relative positioning
        if (useRelativePatrolPoints && (patrolPointA == null || patrolPointB == null))
        {
            CreateRelativePatrolPoints();
        }
```

Then delete the whole `CreateRelativePatrolPoints()` method.

- [ ] **Step 3: Remove patrol assignment from `InitializeEnemy()`**

Replace the `// Assign patrol points to AI ...` block (the entire `EnemyAI enemyAI = ...` section through its `else { Debug.LogWarning(...) }`) so `InitializeEnemy` keeps only team assignment and counting:

```csharp
    private void InitializeEnemy(NetworkObject enemyNetObj)
    {
        GameObject enemyObj = enemyNetObj.gameObject;

        // Assign team component values
        EnemyTeamComponent teamComponent = enemyObj.GetComponent<EnemyTeamComponent>();
        if (teamComponent != null)
        {
            teamComponent.teamID = teamID;
            teamComponent.territorialAdvantage = territorialAdvantage;
        }

        // Track enemy count
        CurrentEnemyCount++;

        // Subscribe to enemy despawn to update count
        StartCoroutine(WaitForEnemyDespawn(enemyNetObj));
    }
```

- [ ] **Step 4: Remove patrol gizmos**

In `OnDrawGizmos()`, delete the patrol-point drawing (everything after the spawn-point wire sphere that references `patrolPointA` / `patrolPointB` / `relativePointA` / `relativePointB`). Keep only:

```csharp
    private void OnDrawGizmos()
    {
        if (!showGizmo) return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
```

- [ ] **Step 5: Verify it compiles**

Focus the Unity editor; wait for recompile. Expected: no console errors.

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/Enemy/Base/EnemySpawner.cs"
git commit -m "refactor(enemy-ai): drop patrol points from spawner (home captured at spawn)"
```

---

## Task 9: Author assets + scene wiring + play-test verification

Create the shared config asset, place the center marker, set per-archetype ranges, and verify behavior in play mode. No code; this is the integration gate.

**Files:**
- Create (Unity asset): `Assets/Settings/DifficultyRingConfig.asset` (via menu).
- Modify (Unity assets): the `EnemyStats` asset(s), the `GameSettingsManager` object, enemy prefab(s), gameplay scene(s).

- [ ] **Step 1: Create the ring config asset**

In Project view: **Create → Enemy → Difficulty Ring Config**. Save as `Assets/Settings/DifficultyRingConfig.asset`. Author rings INNER → OUTER, e.g.:
- `[0]` maxDistance 10, health 3, damage 2.5, speed 1.4
- `[1]` maxDistance 25, health 2, damage 1.8, speed 1.2
- `[2]` maxDistance 50, health 1, damage 1, speed 1

- [ ] **Step 2: Wire the config into GameSettingsManager**

Select the `GameSettingsManager` object in the scene; drag `DifficultyRingConfig.asset` into the new **Difficulty Ring Config** field.

- [ ] **Step 3: Place an ArenaCenter**

Create an empty GameObject named `ArenaCenter` at the map's center point; add the `ArenaCenter` component. Confirm exactly one exists in the gameplay scene.

- [ ] **Step 4: Set EnemyStats ranges and the player layer**

For each `EnemyStats` asset, set `detectionRange`, `attackRange`, `leashRadius`, and `wanderRadius` (`wanderRadius <= leashRadius`). On the enemy prefab's `EnemyAI` component, set **Player Layer Mask** to the player layer (the old `detectionRange`/`attackRange`/patrol fields are gone).

- [ ] **Step 5: Play-test the behavior**

Enter Play mode (host) and verify each:
- Idle enemies wander within `wanderRadius` of spawn and pause between points.
- An enemy engages the nearest player who enters detection range (and is within leash of home).
- The enemy never travels beyond `leashRadius` from home; when the player runs past it, the enemy disengages and walks back home, then resumes wandering.
- Telegraph flash + dodge window still work; attacks still deal damage.
- Stealthed and dead players are ignored.
- Enemies spawned near `ArenaCenter` have visibly more health/damage/speed than far ones (check `GetCurrentHealth`/`GetMaxHealth` or a health bar).
- In a 2-client multiplayer-playmode session, proxies show correct facing + telegraph and interpolate movement; no console errors.

- [ ] **Step 6: Commit the asset/scene wiring**

```bash
git add Assets/Settings Assets/Scenes Assets/Prefabs Assets/Settings/*.meta
git commit -m "chore(enemy-ai): author ring config, arena center, and enemy range wiring"
```

(Adjust paths to wherever your `EnemyStats`/prefabs/scenes actually live; include the corresponding `.meta` files.)

---

## Self-Review

**Spec coverage:**
- Stay in zone / leash → Tasks 2, 7 (`EnemyAILeash`, Returning state). ✓
- Wander within zone → Task 7 (`Wander`/`PickWanderTarget`). ✓
- Target nearest valid player when close → Task 7 (`AcquireTarget`), players-only with dead/stealth checks. ✓
- Static difficulty by enemy distance to center → Tasks 1, 6 (`GetRing`, `ResolveEffectiveStats`). ✓
- Only health/damage/speed scale → Task 6 (ranges read from stats unchanged). ✓
- Discrete rings, inner→outer, center transform → Tasks 1, 4, 9. ✓
- Config via GameSettingsManager → Task 5. ✓
- Fallback when center/config missing → Task 6 (`RingTier.Identity` + warning). ✓
- Authority-only sim, networked visuals unchanged → Tasks 6, 7 (no NetworkTransform; reuses `IsTelegraphing`/`FacingLeft`). ✓
- Patrol A/B removed → Tasks 7, 8. ✓
- Testable pure functions → Tasks 1, 2 with EditMode tests. ✓
- No ring tint (deferred) → not implemented, per spec non-goals. ✓

**Type consistency:** `RingTier`/`DifficultyRingConfig.GetRing` (Task 1) consumed in Tasks 6; `EnemyAILeash.ClampToLeash`/`ShouldDisengage` (Task 2) consumed in Task 7; `EnemyAI.Initialize(Vector2, float, EnemyStats)` produced in Task 7, called in Task 6; `GetDifficultyRingConfig()` produced in Task 5, called in Task 6; `Enemy.Home`/`effectiveAttackDamage`/`effectiveMaxHealth` defined and used within Task 6. Consistent.

**Placeholder scan:** No TBD/TODO; all code steps contain full implementations; the only intentional "expected error" note (Task 6 Step 5) is resolved by Task 7 and called out explicitly.

**Cross-task ordering note:** Tasks 6 and 7 are mutually referencing at the symbol level (`Enemy` calls `EnemyAI.Initialize`; `EnemyAI` calls `Enemy` members). Implement 6 then 7 and run the compile check after Task 7.
