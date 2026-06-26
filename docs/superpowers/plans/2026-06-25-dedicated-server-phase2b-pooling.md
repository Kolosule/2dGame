# Dedicated Server — Phase 2b: Projectile Object Pooling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate per-shot GC churn at 20 players by pooling high-churn networked prefabs (projectiles) through a custom Fusion object provider, instead of Instantiate/Destroy on every spawn/despawn.

**Architecture:** Subclass Fusion's `NetworkObjectProviderDefault` and override **only** its two safe extension points (`InstantiatePrefab`, `DestroyPrefabInstance`), leaving all prefab-id resolution and scene bookkeeping to the base — a mistake in the full `AcquirePrefabInstance` would break *all* spawning, so we don't touch it. Only prefabs carrying a `Poolable` marker are pooled; everything else uses the base path unchanged. Pooled instances are deactivated on release and reused on the next acquire. Because a reused `NetworkObject` keeps its old C# field values, `Projectile` resets its one transient flag (`hasHit`) in `Spawned()`.

**Tech Stack:** Unity, Photon Fusion 2 (dedicated `GameMode.Server`, server-authoritative), C#.

## Global Constraints

- Photon **Fusion 2**; dedicated server is state authority; tick rate **64**; `PlayerCount 20`.
- Override ONLY `NetworkObjectProviderDefault.InstantiatePrefab` and `DestroyPrefabInstance` (both `protected virtual`). Do NOT override `AcquirePrefabInstance`/`ReleaseInstance`/`GetPrefabId` — the base does prefab-load, scene-move, and `Prefabs.AddInstance/RemoveInstance` bookkeeping that must not change.
- Pool only prefabs with a `Poolable` component; all other prefabs (players, etc.) must fall through to base Instantiate/Destroy unchanged.
- A pooled (reused) `NetworkObject` retains its previous C# field values — any transient runtime state on a poolable prefab MUST be reset in `Spawned()`. For `Projectile` that is `hasHit`.
- New `MonoBehaviour`/provider classes live in `Assets/Scripts/Pooling/` (part of `Assembly-CSharp`, where `GameNetworkManager`/`Projectile` live), NOT the engine-free `Game.Net` asmdef.
- Server authority unchanged; friends-only, no anti-cheat.
- Unity cannot compile/build/run or run the Test Runner in this authoring environment. This phase has no pure-logic unit surface, so there are NO EditMode tests; it is verified by a user play-test (rapid fire → no GC spikes, projectiles still spawn/travel/hit/despawn and reuse correctly).

---

### Task 1: `Poolable` marker + `PooledNetworkObjectProvider`

**Files:**
- Create: `Assets/Scripts/Pooling/Poolable.cs`
- Create: `Assets/Scripts/Pooling/PooledNetworkObjectProvider.cs`

**Interfaces:**
- Produces:
  - `class Poolable : MonoBehaviour` with `[System.NonSerialized] public NetworkObject SourcePrefab;`
  - `class PooledNetworkObjectProvider : NetworkObjectProviderDefault` (assignable to `StartGameArgs.ObjectProvider`).

- [ ] **Step 1: Create the `Poolable` marker**

Create `Assets/Scripts/Pooling/Poolable.cs`:

```csharp
using Fusion;
using UnityEngine;

/// <summary>
/// Marks a NetworkObject prefab as poolable by PooledNetworkObjectProvider. Add this to high-churn
/// prefabs (e.g. the projectile) so they are reused instead of Instantiate/Destroy'd every shot.
/// SourcePrefab is stamped by the provider at runtime so a released instance returns to the pool
/// for the prefab it came from. It is non-serialized (runtime-only); do not set it in the Inspector.
/// </summary>
public class Poolable : MonoBehaviour
{
    [System.NonSerialized] public NetworkObject SourcePrefab;
}
```

- [ ] **Step 2: Create the pooling provider**

Create `Assets/Scripts/Pooling/PooledNetworkObjectProvider.cs`:

```csharp
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Object-pooling network object provider. Subclasses Fusion's default provider and overrides only
/// the instantiate/destroy extension points, so all prefab-id resolution and scene bookkeeping stay
/// in the base (NetworkObjectProviderDefault.AcquirePrefabInstance / ReleaseInstance). Only prefabs
/// carrying a Poolable component are pooled — everything else uses the base Instantiate/Destroy
/// unchanged. Pooled instances are SetActive(false) on release and reused on the next acquire of the
/// same prefab, removing per-spawn allocation/GC churn (the projectile spam at 20 players).
///
/// Assign an instance to StartGameArgs.ObjectProvider (see GameNetworkManager).
/// </summary>
public class PooledNetworkObjectProvider : NetworkObjectProviderDefault
{
    // Inactive, reusable instances keyed by the source prefab they were created from.
    private readonly Dictionary<NetworkObject, Stack<NetworkObject>> pools =
        new Dictionary<NetworkObject, Stack<NetworkObject>>();

    protected override NetworkObject InstantiatePrefab(NetworkRunner runner, NetworkObject prefab)
    {
        // Not poolable → default behaviour (Instantiate).
        if (prefab.GetComponent<Poolable>() == null)
            return base.InstantiatePrefab(runner, prefab);

        // Reuse an inactive instance if one is available for this prefab.
        if (pools.TryGetValue(prefab, out var stack) && stack.Count > 0)
        {
            var reused = stack.Pop();
            reused.gameObject.SetActive(true);
            return reused;
        }

        // None pooled yet → create one and remember which prefab/pool it belongs to.
        var instance = base.InstantiatePrefab(runner, prefab);
        var poolable = instance.GetComponent<Poolable>();
        poolable.SourcePrefab = prefab;
        return instance;
    }

    protected override void DestroyPrefabInstance(NetworkRunner runner, NetworkPrefabId prefabId, NetworkObject instance)
    {
        var poolable = instance.GetComponent<Poolable>();

        // Not a pooled instance → default behaviour (Destroy).
        if (poolable == null || poolable.SourcePrefab == null)
        {
            base.DestroyPrefabInstance(runner, prefabId, instance);
            return;
        }

        // Return to the pool instead of destroying: deactivate and keep for reuse.
        instance.gameObject.SetActive(false);
        if (!pools.TryGetValue(poolable.SourcePrefab, out var stack))
        {
            stack = new Stack<NetworkObject>();
            pools[poolable.SourcePrefab] = stack;
        }
        stack.Push(instance);
    }
}
```

- [ ] **Step 3: Self-review and commit**

No automated test (Fusion provider; Unity Test Runner unavailable). Self-review: only the two
`protected override` methods are defined; non-poolable prefabs call `base.` on both paths;
`SourcePrefab` is stamped exactly once (on first creation) and read on release; pooled instances are
deactivated, not destroyed.

```bash
git add Assets/Scripts/Pooling/Poolable.cs Assets/Scripts/Pooling/PooledNetworkObjectProvider.cs
git commit -m "feat(net): pooling INetworkObjectProvider (reuses Poolable-marked prefabs)"
```

---

### Task 2: Reset `Projectile` transient state for safe reuse

**Files:**
- Modify: `Assets/Scripts/Player/Projectile.cs` (`Spawned()`)

**Interfaces:**
- Consumes: nothing (independent; required for correctness once pooling is on).

- [ ] **Step 1: Reset `hasHit` at the start of `Spawned()`**

A pooled projectile keeps its `hasHit` field from its previous life (set `true` in `Hit()`), so on
reuse it would immediately treat itself as already-hit and refuse to damage anything. Reset it.

In `Assets/Scripts/Player/Projectile.cs`, make `Spawned()` begin by clearing the flag:

```csharp
    public override void Spawned()
    {
        // Pooled reuse: a recycled instance keeps its previous hasHit value, so clear transient
        // runtime state here. Every per-spawn field below is re-initialised regardless.
        hasHit = false;

        rb = GetComponent<Rigidbody2D>();
        var col = GetComponent<CircleCollider2D>();
        if (col != null) col.isTrigger = true;
        if (rb != null) rb.gravityScale = 1f;

        if (HasStateAuthority && rb != null)
            rb.linearVelocity = Direction * Speed;
    }
```

- [ ] **Step 2: Self-review and commit**

Self-review: `hasHit = false;` is the first statement in `Spawned()`; the rest of the method is
unchanged. (The `[Networked]` fields Direction/Speed/Damage/ShooterTeam are set by
`ServerInitialize` before each spawn, so they need no manual reset.)

```bash
git add Assets/Scripts/Player/Projectile.cs
git commit -m "fix(net): reset Projectile.hasHit in Spawned for pooled reuse"
```

---

### Task 3: Wire the pooling provider into the runner

**Files:**
- Modify: `Assets/Scripts/GameNetworkManager.cs` (`Start`, `StartHost`, `StartClient`, `StartServer`)

**Interfaces:**
- Consumes: `PooledNetworkObjectProvider` (Task 1).
- Produces: every `StartGameArgs` sets `ObjectProvider` to a single shared provider instance.

- [ ] **Step 1: Create the provider once in `Start()`**

In `GameNetworkManager.Start()`, next to the existing `gameObject.AddComponent<...>()` calls
(after the `RunnerSimulatePhysics2D` setup), add a field and create the provider:

Add the field with the other private fields (near `runner`):

```csharp
    private PooledNetworkObjectProvider objectProvider;
```

In `Start()`, after the `RunnerSimulatePhysics2D` block:

```csharp
        // Pool high-churn networked prefabs (projectiles) instead of Instantiate/Destroy each shot.
        objectProvider = gameObject.AddComponent<PooledNetworkObjectProvider>();
```

- [ ] **Step 2: Set `ObjectProvider` in all three `StartGameArgs`**

In each of `StartHost()`, `StartClient()`, and `StartServer()`, add `ObjectProvider = objectProvider,`
to the `StartGameArgs` initializer (alongside `GameMode`, `SessionName`, `SceneManager`). For example,
in `StartServer()`:

```csharp
        var args = new StartGameArgs()
        {
            GameMode = GameMode.Server,
            SessionName = sessionName,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
            ObjectProvider = objectProvider
        };
```

Apply the same `ObjectProvider = objectProvider` line to the `StartGameArgs` in `StartHost()` and
`StartClient()`. Change nothing else in those methods.

- [ ] **Step 3: Self-review and commit**

Self-review: one provider created in `Start` (not per-StartGame); all three `StartGameArgs` reference
the same `objectProvider`; no other StartGameArgs fields changed.

```bash
git add Assets/Scripts/GameNetworkManager.cs
git commit -m "feat(net): assign pooling provider to the runner in all start modes"
```

---

### Task 4: USER — mark the projectile prefab + verify (deferred, required)

**Files:** none (Unity Editor + play-test).

- [ ] **Step 1: Mark the projectile prefab poolable**

In the Unity Editor, open the **projectile prefab** (the `NetworkObject` assigned to
`PlayerCombat.projectilePrefab`) and add the **`Poolable`** component. Let Unity generate `.meta`
files for the new `Pooling/` scripts. No other prefab needs it (players etc. stay un-pooled).

- [ ] **Step 2: Play-test for correctness and GC**

- [ ] **Functional:** Fire repeatedly. Projectiles spawn, travel, hit (damage/stun apply), and
      despawn exactly as before. Crucially, a projectile fired *after* an earlier one hit something
      still deals damage (confirms `hasHit` reset on reuse — no "dead on arrival" recycled projectile).
- [ ] **Reuse path:** Fire many shots over time; confirm later shots behave identically to the first
      (no accumulating glitches, no stuck-active or invisible projectiles).
- [ ] **GC:** With the Unity Profiler (GC Alloc), confirm sustained rapid fire no longer produces the
      per-shot allocation/Destroy spikes (allocations from projectile spawn/despawn should drop toward
      zero after the pool warms up).
- [ ] **Non-pooled unaffected:** Players still spawn/despawn normally (they have no `Poolable`, so they
      use the default path).

---

## Notes

- If more transient (non-`[Networked]`) runtime state is later added to any poolable prefab, it must
  be reset in that prefab's `Spawned()` — same reason as `Projectile.hasHit`.
- Pool growth is unbounded (grows to the peak concurrent count of each poolable prefab); for
  projectiles this is naturally small. No eviction needed for this game's scale.
