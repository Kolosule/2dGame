# Friendly Fire, Friendly Collision, and Self Marker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unify friendly-fire gating behind one predicate (fixing a `Team.None` damage/self-hit gap on projectiles), fix teammate collision suppression so it no longer permanently gives up after 5 seconds or fails to restore collision on a team reassignment, and add a local-only overhead marker so a player can always find their own body.

**Architecture:** A new engine-free predicate, `Game.Combat.Core.FriendlyFire.CanDamagePlayer`, becomes the single gate both `PlayerCombat` (melee) and `Projectile` call before applying player-vs-player damage. `PlayerTeamData` gains a `TeamChanged` event (mirroring the existing `NetworkedPlayerInventory.CoinsChanged` pattern) that a new `FriendlyCollision` component subscribes to, replacing `PlayerController`'s existing but flawed `SetupTeammateCollisionsWhenReady` coroutine. A new `LocalPlayerMarker` component enables a child marker object only on the client with input authority.

**Tech Stack:** Unity 6.3 (6000.3.0f1), Photon Fusion 2, NUnit (EditMode tests), C#.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-05-friendly-fire-collision-self-marker-design.md`. All 13 decisions in that spec apply; deviations found necessary during planning are called out explicitly below (Task 1 and Task 5).
- **Planning-time correction — signature.** The spec's pseudocode for `FriendlyFire.CanDamagePlayer` takes `Team attackerTeam, Team defenderTeam`. `Game.Combat.Core.asmdef` has `"references": []` and `"noEngineReferences": true`; `Team`/`TeamUtil` (`Assets/Scripts/Teams/`) have no asmdef of their own, so they live in the default assembly, which `Game.Combat.Core` cannot reference. The existing codebase's established fix for this exact situation is to use the plain `int` team number instead of the `Team` enum at the engine-free boundary — see `Assets/Scripts/Hud/Core/ScoreboardSort.cs:15` (`public int Team;`) and `Assets/Scripts/Match/Core/MatchResolver.cs:5` (int winner codes, doc-commented as "matches `TeamUtil.ToNumber`"). `FriendlyFire.CanDamagePlayer` follows the same convention: `int` parameters, converted at each call site via `TeamUtil.ToNumber(...)`. Team number 0 = `Team.None` (unassigned), matching the `Team` enum's underlying values (`Team.cs:8-11`). Behavior is unchanged from the spec — only the parameter type.
- **Planning-time correction — friendly collision already exists, partially.** `PlayerController.cs:97-122` already has a `SetupTeammateCollisionsWhenReady` coroutine (started from `Spawned()`) that calls `Physics2D.IgnoreCollision(myCol, otherCol, true)` once teams resolve. It has two real bugs the spec's decisions require fixing: (a) it polls for up to a 5-second timeout and then **silently gives up forever** if team assignment hasn't resolved yet — collision stays on permanently for that player, which is not fail-safe-then-recovering as spec decision 7 requires; (b) it only ever passes `true` to `IgnoreCollision` — it can never restore collision, so it cannot satisfy spec decision 6 (a team reassignment must restore collision against ex-teammates). Task 5 replaces this coroutine with a new dedicated `FriendlyCollision` component (event-driven off `PlayerTeamData.TeamChanged`, no timeout, symmetric restore) and deletes the superseded coroutine and its `Awake`-less inline `GetComponent` calls from `PlayerController.cs`. No other file referenced `SetupTeammateCollisionsWhenReady`, so removal is safe (confirmed via grep before this plan was written).
- Server-authoritative gating is unchanged: `FriendlyFire.CanDamagePlayer` is called from code paths already gated on `HasStateAuthority` (`PlayerCombat.ApplyMeleeHits`'s caller and `Projectile.OnTriggerEnter2D`'s existing `if (!HasStateAuthority || hasHit) return;` guard) — this plan does not add or change any authority check.
- No new `[Networked]` state anywhere in this plan. `FriendlyCollision` and `LocalPlayerMarker` both derive everything from state that is already networked (`PlayerTeamData.Team`, `HasInputAuthority`).
- Only non-trigger colliders are affected by collision suppression — trigger colliders (coin pickup, flag capture, home base) are never touched, by construction (`FriendlyCollision` only ever calls `GetComponent<Collider2D>()` on the player root, which is the existing non-trigger `BoxCollider2D` — `Assets/Scripts/Player/PlayerPrefab.prefab:269,298` confirms `m_IsTrigger: 0`).
- Coverage note: `FriendlyFire` is the only new logic that is pure/engine-free, so it is the only piece with new NUnit tests (Task 1). `FriendlyCollision`, `LocalPlayerMarker`, and the `PlayerCombat`/`Projectile`/`PlayerTeamData`/`PlayerController` edits are thin `NetworkBehaviour`/`MonoBehaviour` wrappers with zero independent branching logic beyond what `FriendlyFire`'s tests already cover — this matches the existing project pattern (`FlagCarrierMarker`, `CoinCarrierAura`, `PlayerStealthVisual` have no EditMode tests either). These are verified via the Task 7 multi-peer checklist instead.
- Editor-closed test command (do NOT add `-nographics` — it kills the run silently on this machine):
  ```
  "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log
  ```
  Trust `Test run completed` in `r.log` over the shell's exit code. An `[Licensing::Module] Error: Access token is unavailable` log line is a red herring.
- This plan touches prefab wiring (adding two components to `PlayerPrefab.prefab`). Per this project's established pattern, that wiring is done **in the Unity Editor by a human/agent with editor access**, not by hand-authoring prefab YAML — see Task 7. Every other task is pure `.cs` file changes.

## Setup

- [ ] Before Task 1: create a new branch off `main`:
  ```bash
  git checkout main && git pull && git checkout -b feat/friendly-fire-collision-self-marker
  ```

---

### Task 1: `FriendlyFire.CanDamagePlayer` — pure predicate + tests

**Files:**
- Create: `Assets/Scripts/Combat/Core/FriendlyFire.cs`
- Test: `Assets/Tests/EditMode/Combat/FriendlyFireTests.cs`

**Interfaces:**
- Produces: `Game.Combat.Core.FriendlyFire.CanDamagePlayer(int attackerTeam, int defenderTeam, bool isSelf) -> bool`. Consumed by `PlayerCombat.ApplyMeleeHits` (Task 2) and `Projectile.OnTriggerEnter2D` (Task 3), both via `TeamUtil.ToNumber(Team)` conversions at the call site (`TeamUtil` already exists at `Assets/Scripts/Teams/TeamUtil.cs` — not modified by this plan). Team number convention: 0 = `Team.None`, 1 = `Team.Team1`, 2 = `Team.Team2`, 3 = `Team.Team3AI` (`Team.cs:6-12`).

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/Combat/FriendlyFireTests.cs`:

```csharp
using NUnit.Framework;
using Game.Combat.Core;

public class FriendlyFireTests
{
    [TestCase(1, 1)]
    [TestCase(2, 2)]
    public void SameTeam_CannotDamage(int attacker, int defender)
    {
        Assert.IsFalse(FriendlyFire.CanDamagePlayer(attacker, defender, isSelf: false));
    }

    [TestCase(1, 2)]
    [TestCase(2, 1)]
    public void OpposingHumanTeams_CanDamage(int attacker, int defender)
    {
        Assert.IsTrue(FriendlyFire.CanDamagePlayer(attacker, defender, isSelf: false));
    }

    [TestCase(3, 1)]
    [TestCase(1, 3)]
    [TestCase(3, 2)]
    [TestCase(2, 3)]
    public void AiTeamVsHumanTeam_CanDamage(int attacker, int defender)
    {
        Assert.IsTrue(FriendlyFire.CanDamagePlayer(attacker, defender, isSelf: false));
    }

    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(0, 0)]
    public void UnassignedTeamOnEitherSide_CannotDamage(int attacker, int defender)
    {
        Assert.IsFalse(FriendlyFire.CanDamagePlayer(attacker, defender, isSelf: false));
    }

    [TestCase(1, 1)]
    [TestCase(1, 2)]
    [TestCase(2, 1)]
    public void Self_CannotDamage_RegardlessOfTeam(int attacker, int defender)
    {
        Assert.IsTrue(!FriendlyFire.CanDamagePlayer(attacker, defender, isSelf: true));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (compile error — `FriendlyFire` doesn't exist yet)**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testFilter FriendlyFireTests -testResults r.xml -logFile r.log
```
Expected: `r.log` shows a compile error referencing `FriendlyFire` (type not found), or `r.xml` shows the run failed to build. If the editor holds the project lock instead (command hangs or errors with a lock message), skip straight to Step 3 and verify via Step 4's full run once the editor is free.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Combat/Core/FriendlyFire.cs`:

```csharp
namespace Game.Combat.Core
{
    /// <summary>
    /// The single gate every player-damaging source must pass before dealing player-vs-player
    /// damage. Takes team NUMBERS (TeamUtil.ToNumber convention: 0 = unassigned) rather than the
    /// Team enum itself -- this assembly is engine-free (Game.Combat.Core.asmdef has
    /// noEngineReferences: true and references: []) and Team/TeamUtil live in the default
    /// assembly, which this cannot reference. Convert at the call site via TeamUtil.ToNumber.
    ///
    /// isSelf always blocks, regardless of team. A team number of 0 (unassigned, or not yet
    /// replicated to this peer) is non-hostile on either side -- matches TeamUtil.AreEnemies's
    /// treatment of Team.None -- so a hit can never land on a player whose team hasn't
    /// replicated yet (the spawn/late-join/reconnect window).
    /// </summary>
    public static class FriendlyFire
    {
        public static bool CanDamagePlayer(int attackerTeam, int defenderTeam, bool isSelf)
        {
            if (isSelf) return false;
            if (attackerTeam == 0 || defenderTeam == 0) return false;
            return attackerTeam != defenderTeam;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testFilter FriendlyFireTests -testResults r.xml -logFile r.log
```
Expected: `r.log` contains `Test run completed`, `r.xml` shows all `FriendlyFireTests` cases with `result="Passed"`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Combat/Core/FriendlyFire.cs Assets/Scripts/Combat/Core/FriendlyFire.cs.meta Assets/Tests/EditMode/Combat/FriendlyFireTests.cs
git commit -m "feat(combat): add FriendlyFire.CanDamagePlayer predicate"
```
Note: Unity generates the `.meta` file for the new script the next time the editor has focus. If it doesn't exist yet at commit time, commit the `.cs` file alone and add the `.meta` in Task 2's commit (Unity will have generated it by then) — do not hand-author a `.meta` file.

---

### Task 2: Wire `FriendlyFire` into melee (`PlayerCombat.ApplyMeleeHits`)

**Files:**
- Modify: `Assets/Scripts/Player/PlayerCombat.cs:248-271`

**Interfaces:**
- Consumes: `Game.Combat.Core.FriendlyFire.CanDamagePlayer(int, int, bool) -> bool` (Task 1). `PlayerCombat.cs` already has `using Game.Combat.Core;` (line 4) — no new using needed.

- [ ] **Step 1: Replace the player-hit branch**

In `Assets/Scripts/Player/PlayerCombat.cs`, the current block inside `ApplyMeleeHits`:

```csharp
            // Player hit. Skip ourselves and friendly players (no melee friendly-fire). Damage
            // goes through ServerApplyDamage keyed by this attacker's NetworkObject id, so
            // spawn-immunity is respected and the rapid-hit guard is per attacker — which also
            // throttles the dash-strike's per-tick calls to one hit per 0.1s per target.
            PlayerStatsHandler targetPlayer = hit.GetComponent<PlayerStatsHandler>();
            if (targetPlayer != null && targetPlayer != statsHandler)
            {
                PlayerTeamData targetTeam = hit.GetComponent<PlayerTeamData>();
                Team myTeam = teamComponent != null ? teamComponent.Team : Team.None;
                Team otherTeam = targetTeam != null ? targetTeam.Team : Team.None;
                if (!TeamUtil.AreEnemies(myTeam, otherTeam)) continue;

                int finalDamage = ResolveMeleeDamage(otherTeam, hit.transform.position);
                targetPlayer.ServerApplyDamage(finalDamage, Object.Id);
                RPC_HitFeedback(targetPlayer.Object.Id, hit.transform.position, finalDamage);

                Rigidbody2D targetRb = hit.GetComponent<Rigidbody2D>();
                if (targetRb != null)
                {
                    Vector2 knockbackDirection = (hit.transform.position - transform.position).normalized;
                    targetRb.AddForce(new Vector2(knockbackDirection.x * stats.attackForce, knockbackUpward),
                                      ForceMode2D.Impulse);
                }
            }
```

becomes:

```csharp
            // Player hit. Damage goes through ServerApplyDamage keyed by this attacker's
            // NetworkObject id, so spawn-immunity is respected and the rapid-hit guard is per
            // attacker — which also throttles the dash-strike's per-tick calls to one hit per
            // 0.1s per target. Friendly-fire and self-hit are both gated by FriendlyFire, the
            // same predicate Projectile uses, so the two damage sources agree.
            PlayerStatsHandler targetPlayer = hit.GetComponent<PlayerStatsHandler>();
            if (targetPlayer != null)
            {
                PlayerTeamData targetTeam = hit.GetComponent<PlayerTeamData>();
                Team myTeam = teamComponent != null ? teamComponent.Team : Team.None;
                Team otherTeam = targetTeam != null ? targetTeam.Team : Team.None;
                bool isSelf = targetPlayer == statsHandler;
                if (!FriendlyFire.CanDamagePlayer(TeamUtil.ToNumber(myTeam), TeamUtil.ToNumber(otherTeam), isSelf))
                    continue;

                int finalDamage = ResolveMeleeDamage(otherTeam, hit.transform.position);
                targetPlayer.ServerApplyDamage(finalDamage, Object.Id);
                RPC_HitFeedback(targetPlayer.Object.Id, hit.transform.position, finalDamage);

                Rigidbody2D targetRb = hit.GetComponent<Rigidbody2D>();
                if (targetRb != null)
                {
                    Vector2 knockbackDirection = (hit.transform.position - transform.position).normalized;
                    targetRb.AddForce(new Vector2(knockbackDirection.x * stats.attackForce, knockbackUpward),
                                      ForceMode2D.Impulse);
                }
            }
```

Behavior is identical for every case the old code handled (self skipped, same-team skipped, enemy damaged); the only behavior change is that `Team.None` vs `Team.None` (or `Team.None` vs anything) is now explicitly non-hostile via the same rule `Projectile` will use after Task 3, instead of relying solely on `TeamUtil.AreEnemies`'s pre-existing `None` handling — the outcome for melee is the same either way since `AreEnemies` already treated `None` as non-hostile.

- [ ] **Step 2: Compile-check**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log
```
Expected: `r.log` contains `Test run completed` with no new compile errors; the full existing EditMode suite still passes (this file has no dedicated unit tests — see Global Constraints coverage note — so this run is a compile/regression check, not new coverage).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Player/PlayerCombat.cs
git commit -m "fix(combat): gate melee player damage through FriendlyFire.CanDamagePlayer"
```

---

### Task 3: Wire `FriendlyFire` into projectiles (`Projectile.OnTriggerEnter2D`)

**Files:**
- Modify: `Assets/Scripts/Player/Projectile.cs:1-2` (usings), `Projectile.cs:76-104` (player-hit branch)

**Interfaces:**
- Consumes: `Game.Combat.Core.FriendlyFire.CanDamagePlayer(int, int, bool) -> bool` (Task 1).

- [ ] **Step 1: Add the missing using**

In `Assets/Scripts/Player/Projectile.cs`, change:

```csharp
using UnityEngine;
using Fusion;
```

to:

```csharp
using UnityEngine;
using Fusion;
using Game.Combat.Core;
```

- [ ] **Step 2: Replace the player-hit branch**

The current block:

```csharp
        // Player hit (skip same team)
        PlayerStatsHandler playerStats = other.GetComponent<PlayerStatsHandler>();
        if (playerStats != null)
        {
            PlayerTeamData pt = other.GetComponent<PlayerTeamData>();
            Team targetTeam = pt != null ? pt.Team : Team.None;
            bool friendly = targetTeam != Team.None && targetTeam == ShooterTeam;
            if (!friendly)
            {
                // Attribute the hit to the SHOOTER (so their next projectile respects the same
                // per-attacker window), falling back to this projectile's own id if the shooter's
                // player object can't be resolved (e.g. they disconnected mid-flight).
                NetworkId attackerId = Object.Id;
                if (Runner.TryGetPlayerObject(Object.InputAuthority, out NetworkObject shooterObj))
                    attackerId = shooterObj.Id;

                int finalDamage = ResolveDamage(targetTeam, other.transform.position);
                playerStats.ServerApplyDamage(finalDamage, attackerId);
                RPC_HitFeedback(playerStats.Object.Id, other.transform.position, finalDamage);
                if (stunPlayers)
                {
                    PlayerMovement pm = other.GetComponent<PlayerMovement>();
                    if (pm != null) pm.ApplyStun(stunDuration);
                }
                Hit();
            }
            return;
        }
```

becomes:

```csharp
        // Player hit. Friendly-fire and self-hit are both gated by FriendlyFire, the same
        // predicate PlayerCombat uses for melee, so the two damage sources agree. A friendly
        // (or self) hit falls through without calling Hit() -- the projectile keeps travelling
        // and can still hit an enemy behind the teammate.
        PlayerStatsHandler playerStats = other.GetComponent<PlayerStatsHandler>();
        if (playerStats != null)
        {
            PlayerTeamData pt = other.GetComponent<PlayerTeamData>();
            Team targetTeam = pt != null ? pt.Team : Team.None;
            bool isSelf = playerStats.Object != null && playerStats.Object.InputAuthority == Object.InputAuthority;

            if (FriendlyFire.CanDamagePlayer(TeamUtil.ToNumber(ShooterTeam), TeamUtil.ToNumber(targetTeam), isSelf))
            {
                // Attribute the hit to the SHOOTER (so their next projectile respects the same
                // per-attacker window), falling back to this projectile's own id if the shooter's
                // player object can't be resolved (e.g. they disconnected mid-flight).
                NetworkId attackerId = Object.Id;
                if (Runner.TryGetPlayerObject(Object.InputAuthority, out NetworkObject shooterObj))
                    attackerId = shooterObj.Id;

                int finalDamage = ResolveDamage(targetTeam, other.transform.position);
                playerStats.ServerApplyDamage(finalDamage, attackerId);
                RPC_HitFeedback(playerStats.Object.Id, other.transform.position, finalDamage);
                if (stunPlayers)
                {
                    PlayerMovement pm = other.GetComponent<PlayerMovement>();
                    if (pm != null) pm.ApplyStun(stunDuration);
                }
                Hit();
            }
            return;
        }
```

This closes two real gaps: `Team.None` vs `ShooterTeam` (e.g. a target whose team hasn't replicated yet) is now non-hostile instead of always-damageable, and a shooter can no longer hit themselves (`isSelf` was never checked before).

- [ ] **Step 3: Compile-check**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log
```
Expected: `r.log` contains `Test run completed`, no new compile errors, full existing suite still passes.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Player/Projectile.cs
git commit -m "fix(combat): gate projectile player damage through FriendlyFire.CanDamagePlayer"
```

---

### Task 4: `PlayerTeamData.TeamChanged` event

**Files:**
- Modify: `Assets/Scripts/Player/PlayerTeamData.cs`

**Interfaces:**
- Produces: `public event System.Action TeamChanged` on `PlayerTeamData`, raised whenever the networked `Team` changes to a non-`None` value (including the initial value a late joiner receives). Consumed by `FriendlyCollision` (Task 5).

- [ ] **Step 1: Add the `using` and the event field**

Change:

```csharp
using UnityEngine;
using Fusion;
```

to:

```csharp
using System;
using UnityEngine;
using Fusion;
```

Add the event next to the existing `Team` property (after line 14, `public Team Team { get; set; }`):

```csharp
    /// <summary>Fires whenever the networked Team changes to a real value (Team1/Team2/Team3AI),
    /// including the initial value a late joiner receives. Never fires while Team is still None.
    /// FriendlyCollision subscribes to re-derive teammate collision ignores; mirrors the existing
    /// NetworkedPlayerInventory.CoinsChanged event pattern.</summary>
    public event Action TeamChanged;
```

- [ ] **Step 2: Raise it from `OnTeamChanged`**

Change:

```csharp
    private void OnTeamChanged()
    {
        if (Team == Team.None) return;
        ApplyTeamColor();
    }
```

to:

```csharp
    private void OnTeamChanged()
    {
        if (Team == Team.None) return;
        ApplyTeamColor();
        TeamChanged?.Invoke();
    }
```

- [ ] **Step 3: Compile-check**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log
```
Expected: `r.log` contains `Test run completed`, no new compile errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Player/PlayerTeamData.cs
git commit -m "feat(teams): add PlayerTeamData.TeamChanged event"
```

---

### Task 5: `FriendlyCollision` component, replacing `PlayerController`'s coroutine

**Files:**
- Create: `Assets/Scripts/Player/FriendlyCollision.cs`
- Modify: `Assets/Scripts/Player/PlayerController.cs:38-43` (Spawned), `PlayerController.cs:97-122` (delete the coroutine)

**Interfaces:**
- Consumes: `PlayerTeamData.Team` (existing), `PlayerTeamData.TeamChanged` (Task 4).
- Produces: `FriendlyCollision` — a `NetworkBehaviour`, no public API consumed by later tasks. Self-contained.

- [ ] **Step 1: Create `FriendlyCollision.cs`**

```csharp
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Suppresses physics collision between same-team players. Local physics decision, computed
/// independently on every peer from the replicated Team -- every client runs its own Physics2D
/// world, so every client must derive the same ignores. Re-derives on every
/// PlayerTeamData.TeamChanged (the initial spawn assignment, and any later reassignment), not
/// per frame. Only the player's primary non-trigger body collider is affected; trigger
/// colliders (coin pickup, flag capture, home base) are untouched.
///
/// Replaces PlayerController's old SetupTeammateCollisionsWhenReady coroutine, which gave up
/// permanently if team assignment took longer than a 5-second timeout, and never restored
/// collision if a team was ever reassigned (it only ever called IgnoreCollision(..., true)).
/// </summary>
public class FriendlyCollision : NetworkBehaviour
{
    private static readonly List<FriendlyCollision> Active = new List<FriendlyCollision>();

    private PlayerTeamData teamData;
    private Collider2D bodyCollider;

    private void Awake()
    {
        teamData = GetComponent<PlayerTeamData>();
        bodyCollider = GetComponent<Collider2D>();
    }

    public override void Spawned()
    {
        Active.Add(this);
        if (teamData != null) teamData.TeamChanged += RefreshAllPairs;
        RefreshAllPairs();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (teamData != null) teamData.TeamChanged -= RefreshAllPairs;
        Active.Remove(this);
    }

    /// <summary>Re-derive this player's IgnoreCollision pairing against every other currently
    /// active player. Team.None on either side is never "same team", so collision stays on
    /// until both sides have a real team (fail-safe, matching FriendlyFire's None handling).</summary>
    private void RefreshAllPairs()
    {
        if (bodyCollider == null) return;
        Team myTeam = teamData != null ? teamData.Team : Team.None;

        foreach (FriendlyCollision other in Active)
        {
            if (other == this || other == null || other.bodyCollider == null) continue;

            Team otherTeam = other.teamData != null ? other.teamData.Team : Team.None;
            bool sameTeam = myTeam == otherTeam && myTeam != Team.None;
            Physics2D.IgnoreCollision(bodyCollider, other.bodyCollider, sameTeam);
        }
    }
}
```

- [ ] **Step 2: Remove the superseded coroutine from `PlayerController.cs`**

Change:

```csharp
    public override void Spawned()
    {
        // The gameplay camera (PlayerCamera) self-finds the local player via
        // HasInputAuthority, so no explicit camera binding is needed here.
        StartCoroutine(SetupTeammateCollisionsWhenReady());
    }
```

to:

```csharp
    public override void Spawned()
    {
        // The gameplay camera (PlayerCamera) self-finds the local player via
        // HasInputAuthority, so no explicit camera binding is needed here.
        // Teammate collision suppression is handled by the FriendlyCollision component.
    }
```

Then delete the entire coroutine method (and its preceding comment) at the end of the file:

```csharp
    // Ignore collisions between same-team players (replaces NetworkPlayerWrapper's coroutine).
    // Local physics decision; identical on every client because team data is networked.
    private System.Collections.IEnumerator SetupTeammateCollisionsWhenReady()
    {
        PlayerTeamData myTeam = GetComponent<PlayerTeamData>();
        Collider2D myCol = GetComponent<Collider2D>();
        if (myTeam == null || myCol == null) yield break;

        float timeout = 5f;
        while (myTeam.Team == Team.None && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
        if (myTeam.Team == Team.None) yield break;

        var players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        foreach (var other in players)
        {
            if (other == this) continue;
            PlayerTeamData otherTeam = other.GetComponent<PlayerTeamData>();
            Collider2D otherCol = other.GetComponent<Collider2D>();
            if (otherTeam != null && otherCol != null && otherTeam.Team == myTeam.Team)
                Physics2D.IgnoreCollision(myCol, otherCol, true);
        }
    }
}
```

so the file ends immediately after the `FixedUpdateNetwork` closing brace, with a single closing `}` for the class.

- [ ] **Step 3: Compile-check**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log
```
Expected: `r.log` contains `Test run completed`, no compile errors (in particular, confirm no other file referenced `SetupTeammateCollisionsWhenReady` — it was grepped as unreferenced elsewhere before this plan was written, but the compiler is the ground truth).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Player/FriendlyCollision.cs Assets/Scripts/Player/FriendlyCollision.cs.meta Assets/Scripts/Player/PlayerController.cs
git commit -m "fix(teams): replace timeout-based teammate collision coroutine with event-driven FriendlyCollision"
```
(Same `.meta`-timing note as Task 1 applies if the file is new when the editor hasn't generated its `.meta` yet.)

---

### Task 6: `LocalPlayerMarker` component

**Files:**
- Create: `Assets/Scripts/Player/LocalPlayerMarker.cs`

**Interfaces:**
- Produces: `LocalPlayerMarker` — a `NetworkBehaviour`, no public API consumed by other tasks. Self-contained.

- [ ] **Step 1: Create `LocalPlayerMarker.cs`**

```csharp
using UnityEngine;
using Fusion;

/// <summary>
/// Marks the locally-controlled player so its owner can pick their own body out of a pile of
/// same-colored teammates (teammates now pass through each other via FriendlyCollision, so
/// overlapping stacks are common). Purely local: markerRoot is enabled ONLY on the client that
/// has input authority over this player, so no other peer ever sees it. No networked state, no
/// per-frame logic -- the enabled flag is set once in Spawned() and never revisited, including
/// across death/respawn (the player object is teleported on respawn, never despawned --
/// PlayerStatsHandler.Respawn -- so this component and its marker stay put throughout).
/// </summary>
public class LocalPlayerMarker : NetworkBehaviour
{
    [Tooltip("Pre-authored marker child (e.g. a chevron sprite). Leave unassigned to use the " +
             "code-generated fallback triangle -- see markerHeight/markerColor below.")]
    [SerializeField] private GameObject markerRoot;

    [Tooltip("Fallback-only: height above the player root the generated triangle is placed at. " +
             "Ignored if markerRoot is assigned -- that object's own position is authored instead.")]
    [SerializeField] private float markerHeight = 2.6f;

    [Tooltip("Fallback-only: color of the generated triangle. Ignored if markerRoot is assigned.")]
    [SerializeField] private Color markerColor = Color.white;

    public override void Spawned()
    {
        if (markerRoot == null)
        {
            markerRoot = BuildFallbackMarker(markerColor);
            markerRoot.transform.SetParent(transform, false);
            markerRoot.transform.localPosition = Vector3.up * markerHeight;
        }

        markerRoot.SetActive(HasInputAuthority);
    }

    /// <summary>Code-generated downward-pointing triangle, no art dependency. Mirrors
    /// CosmeticTracer's "no art needed" pattern.</summary>
    private static GameObject BuildFallbackMarker(Color color)
    {
        var go = new GameObject("LocalPlayerMarker_Fallback");
        var meshFilter = go.AddComponent<MeshFilter>();
        var meshRenderer = go.AddComponent<MeshRenderer>();

        var mesh = new Mesh();
        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.2f, 0.3f, 0f),
            new Vector3(0.2f, 0.3f, 0f),
            new Vector3(0f, 0f, 0f),
        };
        mesh.triangles = new int[] { 0, 1, 2 };
        mesh.RecalculateBounds();
        meshFilter.mesh = mesh;

        var material = new Material(Shader.Find("Sprites/Default"));
        material.color = color;
        meshRenderer.material = material;
        meshRenderer.sortingOrder = 100;

        return go;
    }
}
```

- [ ] **Step 2: Compile-check**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log
```
Expected: `r.log` contains `Test run completed`, no compile errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Player/LocalPlayerMarker.cs Assets/Scripts/Player/LocalPlayerMarker.cs.meta
git commit -m "feat(hud): add LocalPlayerMarker local-only overhead identifier"
```

---

### Task 7: Unity Editor prefab wiring + multi-peer verification

This task has no `.cs` changes. It must be done inside the Unity Editor (not by hand-authoring prefab YAML — see Global Constraints), by whoever is running this plan with editor access. If the executor has no Unity Editor access, stop here and hand the checklist below to the project owner instead of attempting it.

**Files:**
- Modify (in-editor only): `Assets/Scripts/Player/PlayerPrefab.prefab`

- [ ] **Step 1: Add the two new components to `PlayerPrefab`**

  1. Open the project in Unity, select `Assets/Scripts/Player/PlayerPrefab.prefab`, open it for editing (double-click, or the Prefab Mode pencil icon).
  2. On the prefab's root GameObject (`PlayerPrefab`, the same object with `PlayerCombat`, `PlayerTeamData`, `PlayerController`, `NetworkRigidbody2D`, and the root `BoxCollider2D`), click **Add Component** → search `FriendlyCollision` → add it. No fields to assign — it resolves `PlayerTeamData` and `Collider2D` via `GetComponent` in `Awake`.
  3. On the same root GameObject, **Add Component** → search `LocalPlayerMarker` → add it. Leave `Marker Root` unassigned to use the generated white triangle (default `Marker Height` 2.6, `Marker Color` white), or assign a custom chevron sprite child object if one exists — if assigning a custom object, position it yourself in the prefab (its transform is used as-is; `Marker Height`/`Marker Color` are ignored once `Marker Root` is assigned).
  4. Save the prefab (Ctrl+S in Prefab Mode, then exit Prefab Mode).

- [ ] **Step 2: Confirm the Physics2D layer matrix is untouched**

  Open **Edit → Project Settings → Physics 2D**, confirm the `Player` row/column intersection with itself is still checked (enabled) in the collision matrix. This plan does not change the matrix — `FriendlyCollision` selectively defeats it per-pair at runtime — so this is a sanity check, not a change. If it is unchecked, something else modified it; stop and investigate before proceeding, since that would also silently disable enemy-vs-enemy collision (out of scope for this plan to fix).

- [ ] **Step 3: Multi-peer verification**

  Using this project's existing multi-peer test flow (dedicated server or host + 2+ clients; at minimum two peers on Team1 and one on Team2), verify:

  1. Two Team1 players walk into each other: they pass through completely, no blocking, no shove. A Team1 and a Team2 player still collide/block normally.
  2. Melee: a Team1 player's melee swing does zero damage, zero knockback, and no hit-feedback FX to a Team1 teammate standing in the swing box; it still damages/knocks back a Team2 player normally.
  3. Melee self-hit: attacking with a wide swing box does not damage the attacker even if their own collider is in the box (should already have been true before this plan; confirm no regression).
  4. Projectiles: a Team1 player's shot passes through a Team1 teammate without despawning (no impact FX, no stun) and goes on to hit a Team2 player standing behind the teammate, dealing normal damage.
  5. Projectile self-hit: a player cannot be hit by their own projectile at any point in its flight.
  6. Self marker: each client sees a white chevron/triangle above only their own player, at no point above any other player (teammate or enemy) on their own screen, and no other client sees a chevron above that player.
  7. Death/respawn: the chevron stays visible over the player's corpse during the death timer and after respawn without re-appearing/disappearing; teammate pass-through and enemy collision are both still correct immediately after respawn.
  8. Spawn timing: have a client join and confirm teammate pass-through is established within a second or two of spawning (not stuck on indefinitely, which was the old coroutine's 5-second-timeout failure mode this plan fixes).
  9. Team reassignment (best-effort, only if the test build exposes a way to call `PlayerTeamData.SetTeam` again mid-match — today's codebase calls it exactly once, from `NetworkedSpawnManager`, so this may not be triggerable without a temporary debug hook): reassigning a player to the other team should restore collision against their former teammates and suppress it against their new ones within one `TeamChanged` refresh. Skip this check if there's no way to trigger it without extra debug code — it isn't a live path today.

  If any check fails, do not mark this task complete — fix the relevant task's code and re-run the full checklist.

- [ ] **Step 4: Commit the prefab change**

```bash
git add Assets/Scripts/Player/PlayerPrefab.prefab
git commit -m "feat(player): wire FriendlyCollision and LocalPlayerMarker onto PlayerPrefab"
```
