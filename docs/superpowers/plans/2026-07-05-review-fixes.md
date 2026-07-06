# Code-Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement all fixes from the 2026-07-05 core-systems code review except the client-callable damage-RPC lockdown (explicitly out of scope).

**Architecture:** Twelve small, independent fixes across CTF, combat, enemy, pooling, and AoI systems. One new pure-logic class (`HitCooldownLedger`) gets full TDD in a new Fusion-free asmdef, following the existing `Game.Buffs.Core` / `Game.EnemyAI` / `Game.Net` pattern. Everything else is NetworkBehaviour surgery verified by compile + the existing EditMode suite + manual Unity playtest notes.

**Tech Stack:** Unity 6000.3.0f1, Photon Fusion 2 (Host/Server mode), NUnit EditMode tests.

## Global Constraints

- **DO NOT** change `RpcSources` on `PlayerStatsHandler.RPC_TakeDamage` or `Enemy.RPC_TakeDamage` — the damage-RPC lockdown is explicitly excluded by the user.
- Work directly on branch `feat/dash-buffs-retune-aoi-wiring`. The working tree has uncommitted changes (AoI wiring) that these fixes build on — do NOT stash, revert, or create a worktree from HEAD. Commit only the files each task names.
- This project never uses NetworkTransform. Positions sync via NetworkRigidbody2D or `[Networked]` values applied locally. Preserve that pattern.
- Anything read inside `FixedUpdateNetwork`/`Simulate` (the simulation path) must be `[Networked]` or derived from `[Networked]` state, because clients predict and resimulate ticks.
- Pure logic goes in a Fusion-free asmdef folder with a matching EditMode test asmdef (existing pattern: `Assets/Scripts/Buffs/Core` + `Assets/Tests/EditMode/BuffUnlockTests.cs`).
- New `.cs`/`.asmdef` files need `.meta` files. The batchmode test run (below) imports assets and generates them — commit the generated `.meta` files together with the task that created the files. This project has been bitten by missing `.meta` files before.
- Every commit message ends with the trailer line: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`

## Verification Commands

**EditMode tests (also generates .meta files for new assets):**

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Users\1\Documents\GitHub\2dGame" -runTests -testPlatform EditMode -testResults "C:\Users\1\Documents\GitHub\2dGame\TestResults\editmode.xml" -logFile "C:\Users\1\Documents\GitHub\2dGame\TestResults\editmode.log"
```

- This FAILS if the Unity editor already has the project open — close the editor first, or use the Test Runner window (Window → General → Test Runner → EditMode → Run All) instead.
- Exit code 0 = all tests passed. Inspect `TestResults\editmode.xml` for details.
- A compile error anywhere in the project fails the run — so a green run doubles as a compile check for the Fusion-behaviour tasks that have no unit tests.

**Manual playtest checks** are listed per task as "Unity verify" notes. They are for the user's multi-peer session; the executor only needs the batchmode run green.

---

### Task 1: HitCooldownLedger (pure logic, TDD)

The review found `PlayerStatsHandler`'s global 0.1 s hit cooldown eats legitimate hits when two different attackers land within the window. This task builds the pure per-attacker ledger; Task 2 wires it in.

**Files:**
- Create: `Assets/Scripts/Combat/Core/Game.Combat.Core.asmdef`
- Create: `Assets/Scripts/Combat/Core/HitCooldownLedger.cs`
- Create: `Assets/Tests/EditMode/Combat/Game.Combat.Tests.asmdef`
- Test: `Assets/Tests/EditMode/Combat/HitCooldownLedgerTests.cs`

**Interfaces:**
- Consumes: nothing (pure, no Unity/Fusion references).
- Produces: `Game.Combat.Core.HitCooldownLedger` with `bool TryRegisterHit(ulong attackerKey, int currentTick, int cooldownTicks)` and `void Clear()`. Task 2 depends on exactly these signatures.

- [ ] **Step 1: Create the runtime asmdef**

`Assets/Scripts/Combat/Core/Game.Combat.Core.asmdef` (mirrors `Game.Buffs.Core.asmdef`):

```json
{
    "name": "Game.Combat.Core",
    "rootNamespace": "Game.Combat.Core",
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

- [ ] **Step 2: Create the test asmdef**

`Assets/Tests/EditMode/Combat/Game.Combat.Tests.asmdef` (mirrors `Game.Buffs.EditModeTests.asmdef`):

```json
{
    "name": "Game.Combat.Tests",
    "rootNamespace": "",
    "references": [
        "Game.Combat.Core",
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

- [ ] **Step 3: Write the failing tests**

`Assets/Tests/EditMode/Combat/HitCooldownLedgerTests.cs`:

```csharp
using NUnit.Framework;
using Game.Combat.Core;

public class HitCooldownLedgerTests
{
    private const ulong AttackerA = 101;
    private const ulong AttackerB = 202;

    [Test]
    public void FirstHitFromAnAttackerIsAllowed()
    {
        var ledger = new HitCooldownLedger();
        Assert.IsTrue(ledger.TryRegisterHit(AttackerA, currentTick: 100, cooldownTicks: 6));
    }

    [Test]
    public void SecondHitFromSameAttackerWithinCooldownIsBlocked()
    {
        var ledger = new HitCooldownLedger();
        ledger.TryRegisterHit(AttackerA, 100, 6);
        Assert.IsFalse(ledger.TryRegisterHit(AttackerA, 103, 6));
    }

    [Test]
    public void HitFromSameAttackerAfterCooldownIsAllowed()
    {
        var ledger = new HitCooldownLedger();
        ledger.TryRegisterHit(AttackerA, 100, 6);
        Assert.IsTrue(ledger.TryRegisterHit(AttackerA, 106, 6));
    }

    [Test]
    public void DifferentAttackersAreIndependent()
    {
        var ledger = new HitCooldownLedger();
        ledger.TryRegisterHit(AttackerA, 100, 6);
        // The review bug: attacker B landing 1 tick later must NOT be eaten.
        Assert.IsTrue(ledger.TryRegisterHit(AttackerB, 101, 6));
    }

    [Test]
    public void ZeroCooldownAlwaysAllows()
    {
        var ledger = new HitCooldownLedger();
        ledger.TryRegisterHit(AttackerA, 100, 0);
        Assert.IsTrue(ledger.TryRegisterHit(AttackerA, 100, 0));
    }

    [Test]
    public void ClearForgetsAllAttackers()
    {
        var ledger = new HitCooldownLedger();
        ledger.TryRegisterHit(AttackerA, 100, 6);
        ledger.Clear();
        Assert.IsTrue(ledger.TryRegisterHit(AttackerA, 101, 6));
    }

    [Test]
    public void BlockedHitDoesNotResetTheCooldownWindow()
    {
        var ledger = new HitCooldownLedger();
        ledger.TryRegisterHit(AttackerA, 100, 6);
        ledger.TryRegisterHit(AttackerA, 103, 6); // blocked — must not re-arm
        Assert.IsTrue(ledger.TryRegisterHit(AttackerA, 106, 6));
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run the batchmode command from the Verification section.
Expected: run FAILS with compile error `The type or namespace name 'HitCooldownLedger' could not be found` (the test file references a class that doesn't exist yet).

- [ ] **Step 5: Write the implementation**

`Assets/Scripts/Combat/Core/HitCooldownLedger.cs`:

```csharp
using System.Collections.Generic;

namespace Game.Combat.Core
{
    /// <summary>
    /// Pure, Fusion-free per-attacker hit-cooldown tracking. Replaces the old single global
    /// hit-cooldown TickTimer on PlayerStatsHandler, which silently ate a second attacker's
    /// hit landing within the window. Keyed by an opaque attacker id (NetworkId.Raw of the
    /// attacking object); ticks are plain ints so this is unit-testable in EditMode.
    /// Server-only state — never networked, cleared on respawn.
    /// </summary>
    public class HitCooldownLedger
    {
        private readonly Dictionary<ulong, int> lastHitTick = new Dictionary<ulong, int>();

        /// <summary>
        /// True if this attacker may hit now; records the hit tick when allowed.
        /// A blocked hit does NOT re-arm the window.
        /// </summary>
        public bool TryRegisterHit(ulong attackerKey, int currentTick, int cooldownTicks)
        {
            if (lastHitTick.TryGetValue(attackerKey, out int last) &&
                currentTick - last < cooldownTicks)
            {
                return false;
            }
            lastHitTick[attackerKey] = currentTick;
            return true;
        }

        public void Clear() => lastHitTick.Clear();
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run the batchmode command again.
Expected: exit code 0; `TestResults\editmode.xml` shows all `HitCooldownLedgerTests` passed alongside the existing suites.

- [ ] **Step 7: Commit (including generated .meta files)**

```powershell
git add "Assets/Scripts/Combat" "Assets/Tests/EditMode/Combat"
git status   # confirm the 4 new files + their .meta files (and folder .meta files) are staged, nothing else
git commit -m @'
feat(combat): add pure per-attacker HitCooldownLedger with EditMode tests

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 2: Per-attacker hit cooldown in PlayerStatsHandler + call sites

**Files:**
- Modify: `Assets/Scripts/Player/PlayerStatsHandler.cs`
- Modify: `Assets/Scripts/Player/PlayerCombat.cs` (the `ApplyMeleeHits` player branch, ~line 262)
- Modify: `Assets/Scripts/Player/Projectile.cs` (the player-hit branch in `OnTriggerEnter2D`, ~line 76)
- Modify: `Assets/Scripts/Enemy/Base/Enemy.cs` (`AttackPlayer`, ~line 248)

**Interfaces:**
- Consumes: `Game.Combat.Core.HitCooldownLedger` from Task 1 (`TryRegisterHit(ulong, int, int)`, `Clear()`).
- Produces: `public void ServerApplyDamage(float damage, NetworkId attackerId)` on `PlayerStatsHandler`. All server-side damage callers use it. `RPC_TakeDamage(float)` remains (unchanged signature and `RpcSources.All` — see Global Constraints) and now delegates to `ServerApplyDamage(damage, default)`.

- [ ] **Step 1: Rework PlayerStatsHandler damage entry points**

In `Assets/Scripts/Player/PlayerStatsHandler.cs`:

Add at the top:

```csharp
using Game.Combat.Core;
```

Delete the networked `HitCooldownTimer` property:

```csharp
// DELETE this line:
[Networked] private TickTimer HitCooldownTimer { get; set; }
```

Add the ledger field next to the other private fields:

```csharp
// Per-attacker rapid-hit guard (server-only; keyed by the attacking NetworkObject's id).
// Replaces the old single global HitCooldownTimer, which ate a second attacker's hit
// landing inside the window. Never networked; cleared on respawn.
private readonly HitCooldownLedger hitLedger = new HitCooldownLedger();
```

Replace the entire `RPC_TakeDamage` method body with a delegation, and add `ServerApplyDamage` below it:

```csharp
    /// <summary>
    /// SERVER: Damages the player. Only runs on server.
    /// Unknown-attacker path (legacy/RPC callers) — shares one cooldown bucket (key 0),
    /// which matches the old global-cooldown behaviour for these callers.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TakeDamage(float damage)
    {
        ServerApplyDamage(damage, default);
    }

    /// <summary>
    /// SERVER: apply damage attributed to a specific attacker (the attacking NetworkObject's
    /// id: the melee player, the shooter, or the enemy). Spawn immunity is global; the
    /// rapid-hit guard is PER ATTACKER so two players hitting in the same 0.1s both land.
    /// </summary>
    public void ServerApplyDamage(float damage, NetworkId attackerId)
    {
        if (!HasStateAuthority) return;
        if (IsDead) return;

        // Spawn immunity: ignore damage while the immunity timer is still running.
        if (!SpawnImmunityTimer.ExpiredOrNotRunning(Runner))
        {
            return;
        }

        // Rapid-hit guard, per attacker.
        int cooldownTicks = Mathf.Max(1, Mathf.RoundToInt(hitCooldown * Runner.TickRate));
        if (!hitLedger.TryRegisterHit((ulong)attackerId.Raw, Runner.Tick, cooldownTicks))
        {
            return;
        }

        CurrentHealth -= damage;
        CurrentHealth = Mathf.Max(0, CurrentHealth);

        if (CurrentHealth <= 0)
        {
            CurrentHealth = 0;
            Die();
        }
    }
```

In `Respawn()`, after `SpawnImmunityTimer = TickTimer.CreateFromSeconds(...)`, add:

```csharp
        hitLedger.Clear(); // fresh life, no stale attacker cooldowns
```

- [ ] **Step 2: Update the melee call site**

In `Assets/Scripts/Player/PlayerCombat.cs`, inside `ApplyMeleeHits`, replace:

```csharp
                int finalDamage = ResolveMeleeDamage(hit.gameObject, hit.transform.position);
                targetPlayer.RPC_TakeDamage(finalDamage);
```

with:

```csharp
                int finalDamage = ResolveMeleeDamage(hit.gameObject, hit.transform.position);
                targetPlayer.ServerApplyDamage(finalDamage, Object.Id);
```

Also update the comment above that block (it currently says damage "goes through RPC_TakeDamage"); replace the comment with:

```csharp
            // Player hit. Skip ourselves and friendly players (no melee friendly-fire). Damage
            // goes through ServerApplyDamage keyed by this attacker's NetworkObject id, so
            // spawn-immunity is respected and the rapid-hit guard is per attacker — which also
            // throttles the dash-strike's per-tick calls to one hit per 0.1s per target.
```

- [ ] **Step 3: Update the projectile call site**

In `Assets/Scripts/Player/Projectile.cs`, in the player-hit branch of `OnTriggerEnter2D`, replace:

```csharp
            if (!friendly)
            {
                playerStats.RPC_TakeDamage(Damage);
```

with:

```csharp
            if (!friendly)
            {
                // Attribute the hit to the SHOOTER (so their next projectile respects the same
                // per-attacker window), falling back to this projectile's own id if the shooter's
                // player object can't be resolved (e.g. they disconnected mid-flight).
                NetworkId attackerId = Object.Id;
                if (Runner.TryGetPlayerObject(Object.InputAuthority, out NetworkObject shooterObj))
                    attackerId = shooterObj.Id;
                playerStats.ServerApplyDamage(Damage, attackerId);
```

- [ ] **Step 4: Update the enemy call site**

In `Assets/Scripts/Enemy/Base/Enemy.cs`, in `AttackPlayer`, replace:

```csharp
        // Deal damage to player
        player.TakeDamage(finalDamage);
```

with:

```csharp
        // Deal damage to player, attributed to this enemy (per-attacker hit cooldown).
        player.ServerApplyDamage(finalDamage, Object.Id);
```

(Leave `PlayerStatsHandler.TakeDamage(float)` itself in place — it is a public legacy entry point.)

- [ ] **Step 5: Run the EditMode suite (compile check + regressions)**

Run the batchmode command. Expected: exit code 0, all tests pass. A compile error here means a signature mismatch — fix before committing.

- [ ] **Step 6: Commit**

```powershell
git add Assets/Scripts/Player/PlayerStatsHandler.cs Assets/Scripts/Player/PlayerCombat.cs Assets/Scripts/Player/Projectile.cs Assets/Scripts/Enemy/Base/Enemy.cs
git commit -m @'
fix(combat): make rapid-hit guard per-attacker instead of global

Two attackers landing hits within 0.1s of each other both count now.
RPC_TakeDamage keeps its signature and delegates to the shared path.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

Unity verify (user, later): host + 2 clients, both clients melee the host simultaneously — host loses HP from both hits.

---

### Task 3: Flag auto-drop on carrier disconnect + drop-position sentinel

**Files:**
- Modify: `Assets/Scripts/CTF Flag/Flag.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: no API changes — behaviour only. (Task 4 uses the existing `Flag.IsCarriedBy(PlayerRef)`.)

- [ ] **Step 1: Add the HasDropPosition sentinel**

In `Flag.cs`, below the `DropPosition` property, add:

```csharp
    // True while DropPosition holds a real drop location. Replaces the Vector3.zero sentinel
    // check, which would break for a flag legitimately dropped at the world origin.
    [Networked] private NetworkBool HasDropPosition { get; set; }
```

- [ ] **Step 2: Set/clear the sentinel at every state change**

In `DropFlag()`, immediately after `DropPosition = transform.position;` add:

```csharp
        HasDropPosition = true;
```

In `PickupFlag(...)`, immediately after `AutoReturnTimer = default;` add:

```csharp
        HasDropPosition = false;
```

In `ReturnFlag()`, immediately after `AutoReturnTimer = default;` add:

```csharp
        HasDropPosition = false;
```

In `Update()`, replace the Dropped case:

```csharp
            case FlagState.Dropped:
                if (DropPosition != Vector3.zero) transform.position = DropPosition;
                break;
```

with:

```csharp
            case FlagState.Dropped:
                if (HasDropPosition) transform.position = DropPosition;
                break;
```

- [ ] **Step 3: Auto-drop when the carrier's player object is gone**

In `FixedUpdateNetwork()`, after the existing `if (!HasStateAuthority) return;` line, add this BEFORE the auto-return check:

```csharp
        // Carrier disconnect/crash: death drops the flag via PlayerStatsHandler.Die(), but a
        // player who vanishes without dying leaves the flag stuck in Carried forever (the
        // auto-return timer only arms on Drop). If the carrier's player object no longer
        // exists, drop the flag where it is so the auto-return countdown starts.
        if (CurrentState == FlagState.Carried &&
            !Runner.TryGetPlayerObject(CarrierPlayerRef, out _))
        {
            DropFlag();
        }
```

Note: in this path `carrierGameObject` is already null/destroyed, so `DropFlag()`'s carrier-cleanup block (marker + AoI un-register) is skipped — that is fine: the destroyed carrier's registrar entry is pruned by Task 8.

- [ ] **Step 4: Run the EditMode suite (compile check)**

Run the batchmode command. Expected: exit code 0.

- [ ] **Step 5: Commit**

```powershell
git add "Assets/Scripts/CTF Flag/Flag.cs"
git commit -m @'
fix(ctf): drop the flag when the carrier disconnects; real drop-position sentinel

A carrier who alt-F4s no longer soft-locks the flag in Carried state,
and a drop at the world origin is no longer treated as "unset".

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

Unity verify (user, later): client picks up flag, kill the client process — flag drops at their last position and auto-returns 15 s later.

---

### Task 4: Derive flag-carrying from networked state in the simulation path

**Files:**
- Modify: `Assets/Scripts/CTF Flag/CTFGameManager.cs`
- Modify: `Assets/Scripts/Player/PlayerMovement.cs`
- Modify: `Assets/Scripts/Buffs/PlayerBuffs.cs`
- Modify: `Assets/Scripts/CTF Flag/FlagCarrierMarker.cs` (comment only)

**Interfaces:**
- Consumes: existing `Flag.IsCarriedBy(PlayerRef)` (reads `[Networked]` `CurrentState` + `CarrierPlayerRef`).
- Produces: `public bool IsCarrying(PlayerRef player)` on `CTFGameManager`. `FlagCarrierMarker.IsCarryingFlag()` is no longer read by any simulation code (visual-only from here on).

- [ ] **Step 1: Add the networked carrying check to CTFGameManager**

In `CTFGameManager.cs`, inside the `Public Getters` region, add:

```csharp
    /// <summary>
    /// Is this player currently carrying either flag? Derived purely from the flags'
    /// [Networked] state (CurrentState + CarrierPlayerRef), so it is safe to read inside
    /// FixedUpdateNetwork/Simulate on a predicting client — unlike FlagCarrierMarker's
    /// local bool, which is render-path state and lags/never rewinds on resimulation.
    /// </summary>
    public bool IsCarrying(PlayerRef player)
    {
        return (team1Flag != null && team1Flag.IsCarriedBy(player)) ||
               (team2Flag != null && team2Flag.IsCarriedBy(player));
    }
```

- [ ] **Step 2: Use it in the dash gate**

In `Assets/Scripts/Player/PlayerMovement.cs`:

Remove the field declaration and its assignment:

```csharp
// DELETE from the field block:
    private FlagCarrierMarker flagCarrierMarker;
// DELETE from Spawned():
        flagCarrierMarker = GetComponent<FlagCarrierMarker>();
```

In `Simulate(...)`, replace:

```csharp
            bool carrying = flagCarrierMarker != null && flagCarrierMarker.IsCarryingFlag();
            if (!carrying) StartDash();
```

with:

```csharp
            // Carrying-state must come from networked flag state (resim-safe), not the
            // render-path FlagCarrierMarker bool — see CTFGameManager.IsCarrying.
            bool carrying = CTFGameManager.Instance != null &&
                            CTFGameManager.Instance.IsCarrying(Object.InputAuthority);
            if (!carrying) StartDash();
```

- [ ] **Step 3: Use it in the stealth gate**

In `Assets/Scripts/Buffs/PlayerBuffs.cs`:

Remove the field and its assignment:

```csharp
// DELETE:
    private FlagCarrierMarker flagMarker;
// DELETE from Spawned():
        flagMarker = GetComponent<FlagCarrierMarker>();
```

In `CanActivateStealth()`, replace:

```csharp
        bool carrying = flagMarker != null && flagMarker.IsCarryingFlag();
```

with:

```csharp
        bool carrying = CTFGameManager.Instance != null &&
                        CTFGameManager.Instance.IsCarrying(Object.InputAuthority);
```

- [ ] **Step 4: Update FlagCarrierMarker's class comment**

In `FlagCarrierMarker.cs`, replace the summary comment:

```csharp
/// <summary>
/// Attach to player prefabs. Shows a floating icon above the player's head while
/// they carry a flag, on every peer. Dash suppression is handled elsewhere via
/// IsCarryingFlag(); this component is purely the head-icon visual.
/// </summary>
```

with:

```csharp
/// <summary>
/// Attach to player prefabs. Shows a floating icon above the player's head while
/// they carry a flag, on every peer. PURELY VISUAL: gameplay (dash/stealth gating)
/// derives carrying-state from networked flag state via CTFGameManager.IsCarrying —
/// never read IsCarryingFlag() from simulation code.
/// </summary>
```

- [ ] **Step 5: Run the EditMode suite (compile check)**

Run the batchmode command. Expected: exit code 0.

- [ ] **Step 6: Commit**

```powershell
git add "Assets/Scripts/CTF Flag/CTFGameManager.cs" Assets/Scripts/Player/PlayerMovement.cs Assets/Scripts/Buffs/PlayerBuffs.cs "Assets/Scripts/CTF Flag/FlagCarrierMarker.cs"
git commit -m @'
fix(ctf): gate dash/stealth on networked flag state, not the local marker bool

FlagCarrierMarker.isCarryingFlag is render-path state that lags the server
and never rewinds on resimulation; predicting clients could dash right
after pickup. Derive from Flag.CarrierPlayerRef instead.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

Unity verify (user, later): as a client, grab a flag and mash dash immediately — no dash, and no rubber-band.

---

### Task 5: Network the enemy's team

**Files:**
- Modify: `Assets/Scripts/Enemy/Base/Enemy.cs`
- Modify: `Assets/Scripts/Enemy/Base/EnemyTeamComponent.cs`
- Modify: `Assets/Scripts/Enemy/Base/EnemySpawner.cs` (class `NetworkedEnemySpawner`, `InitializeEnemy`)

**Interfaces:**
- Consumes: existing `TeamUtil.Normalize(string)` / `TeamUtil.ToId(Team)`.
- Produces: `[Networked] public Team Team` on `Enemy` + `public void ServerSetTeam(Team team)`; `public void ApplyTeam(Team team)` on `EnemyTeamComponent`. Task 9 also edits `Enemy.cs` and `EnemySpawner.cs` — apply this task first.

- [ ] **Step 1: Add the networked team to Enemy**

In `Enemy.cs`, below the `FacingLeft` property, add:

```csharp
    // The enemy's team, networked so CLIENTS colorize correctly. The spawner used to set
    // EnemyTeamComponent.teamID in the spawn callback, which runs on the server only —
    // every client kept the prefab default and showed the wrong team color.
    [Networked, OnChangedRender(nameof(OnTeamChanged))]
    public Team Team { get; private set; }
```

Add these methods after `ResolveEffectiveStats()`:

```csharp
    /// <summary>SERVER: assign this enemy's team (called from the spawner's spawn callback).</summary>
    public void ServerSetTeam(Team team)
    {
        if (!HasStateAuthority) return;
        Team = team;
        OnTeamChanged(); // apply immediately on the authority; clients get OnChangedRender
    }

    /// <summary>Render-time callback: push the networked team into the visual/team component.</summary>
    private void OnTeamChanged()
    {
        if (Team == global::Team.None) return;
        EnemyTeamComponent tc = teamComponent != null ? teamComponent : GetComponent<EnemyTeamComponent>();
        if (tc != null) tc.ApplyTeam(Team);
    }
```

In `Spawned()`, after the `ai = GetComponent<EnemyAI>();` line, add:

```csharp
        // OnChangedRender does not fire for the value a late joiner receives as initial
        // state (same pattern as PlayerTeamData), so apply once here.
        OnTeamChanged();
```

- [ ] **Step 2: Make EnemyTeamComponent consume the networked value**

Replace the entire body of `EnemyTeamComponent.cs` class (keep the file header comment) with:

```csharp
public class EnemyTeamComponent : MonoBehaviour
{
    [Header("Team Assignment")]
    [Tooltip("Authored fallback (scene-placed enemies). Spawned enemies get their team " +
             "from Enemy's [Networked] Team via ApplyTeam.")]
    public string teamID = "Team1";

    [Header("Territorial Advantage")]
    [Tooltip("Territorial advantage: -1 (at enemy base) to +1 (at own base). 0 = neutral ground")]
    [Range(-1f, 1f)]
    public float territorialAdvantage = 0f;

    [Header("Visual Feedback (Optional)")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Enemy enemy;

    /// <summary>
    /// This enemy's team. Prefers the networked Enemy.Team (correct on every client);
    /// falls back to the authored teamID string for scene-placed/unspawned enemies.
    /// </summary>
    public Team Team
    {
        get
        {
            if (enemy == null) enemy = GetComponent<Enemy>();
            if (enemy != null && enemy.Object != null && enemy.Object.IsValid &&
                enemy.Team != Team.None)
            {
                return enemy.Team;
            }
            return TeamUtil.Normalize(teamID);
        }
    }

    /// <summary>Called by Enemy when the networked team changes: sync the string + color.</summary>
    public void ApplyTeam(Team team)
    {
        teamID = TeamUtil.ToId(team);
        ApplyTeamColor(team);
    }

    private void Start()
    {
        ApplyTeamColor(Team);
    }

    private void ApplyTeamColor(Team team)
    {
        if (spriteRenderer == null || TeamManager.Instance == null) return;
        TeamData teamData = TeamManager.Instance.GetTeamData(team);
        if (teamData != null)
        {
            spriteRenderer.color = teamData.teamColor;
        }
    }
}
```

- [ ] **Step 3: Set the team in the spawner callback**

In `EnemySpawner.cs`, in `InitializeEnemy(NetworkObject enemyNetObj)`, after the existing `teamComponent` block, add:

```csharp
        // Networked team so clients colorize correctly (the teamComponent fields above are
        // server-local; they remain as the authored fallback for scene-placed enemies).
        Enemy enemy = enemyObj.GetComponent<Enemy>();
        if (enemy != null)
        {
            enemy.ServerSetTeam(TeamUtil.Normalize(teamID));
        }
```

- [ ] **Step 4: Run the EditMode suite (compile check)**

Run the batchmode command. Expected: exit code 0. Watch for a name-collision compile error between the `Team` property and the `Team` enum — the `global::Team.None` qualifier in `OnTeamChanged` handles the one ambiguous site (same pattern as `PlayerTeamData.Team`).

- [ ] **Step 5: Commit**

```powershell
git add Assets/Scripts/Enemy/Base/Enemy.cs Assets/Scripts/Enemy/Base/EnemyTeamComponent.cs Assets/Scripts/Enemy/Base/EnemySpawner.cs
git commit -m @'
fix(enemy): network the enemy team so clients colorize correctly

Spawner set EnemyTeamComponent.teamID server-side only; clients kept the
prefab default. Enemy now carries [Networked] Team applied on every peer.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

Unity verify (user, later): join as client near a Team2 spawner — enemies show Team2's color.

---

### Task 6: Pool provider survives session restarts

**Files:**
- Modify: `Assets/Scripts/Pooling/PooledNetworkObjectProvider.cs`
- Modify: `Assets/Scripts/GameNetworkManager.cs` (`OnShutdown`)

**Interfaces:**
- Consumes: nothing new.
- Produces: `public void ClearPools()` on `PooledNetworkObjectProvider`.

**Why:** `GameNetworkManager` creates the provider once on a `DontDestroyOnLoad` object. After a session shutdown, pooled instances are scene objects that get destroyed, leaving destroyed (`== null`) references in the pool. A second session would pop a destroyed instance and crash on `SetActive`.

- [ ] **Step 1: Skip destroyed instances on acquire + add ClearPools**

In `PooledNetworkObjectProvider.cs`, replace the reuse block in `InstantiatePrefab`:

```csharp
        // Reuse an inactive instance if one is available for this prefab.
        if (pools.TryGetValue(prefab, out var stack) && stack.Count > 0)
        {
            var reused = stack.Pop();
            reused.gameObject.SetActive(true);
            return reused;
        }
```

with:

```csharp
        // Reuse an inactive instance if one is available for this prefab. Skip entries that
        // were destroyed since being pooled (scene unload / previous session shutdown) —
        // Unity's overloaded == makes destroyed objects compare null.
        if (pools.TryGetValue(prefab, out var stack))
        {
            while (stack.Count > 0)
            {
                var reused = stack.Pop();
                if (reused == null) continue;
                reused.gameObject.SetActive(true);
                return reused;
            }
        }
```

And add at the bottom of the class:

```csharp
    /// <summary>
    /// Forget every pooled instance. Call on runner shutdown: pooled instances are scene
    /// objects that die with the session, and the next session must not pop their corpses.
    /// </summary>
    public void ClearPools() => pools.Clear();
```

- [ ] **Step 2: Clear pools on shutdown**

In `GameNetworkManager.cs`, at the top of `OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)`, add:

```csharp
        // Pooled instances belong to the session that just died — drop them so a
        // restarted session doesn't reuse destroyed objects.
        if (objectProvider != null)
            objectProvider.ClearPools();
```

- [ ] **Step 3: Run the EditMode suite (compile check)**

Run the batchmode command. Expected: exit code 0.

- [ ] **Step 4: Commit**

```powershell
git add Assets/Scripts/Pooling/PooledNetworkObjectProvider.cs Assets/Scripts/GameNetworkManager.cs
git commit -m @'
fix(pooling): survive session restarts (skip destroyed instances, clear on shutdown)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 7: Replicate projectile scale (pooling-safe)

**Files:**
- Modify: `Assets/Scripts/Player/Projectile.cs`
- Modify: `Assets/Scripts/Player/PlayerCombat.cs` (`ShootProjectile` spawn callback)

**Interfaces:**
- Consumes: nothing new.
- Produces: `ServerInitialize(Vector2 dir, float speed, int damage, Team team, float scale)` — note the NEW fifth parameter. `PlayerCombat.ShootProjectile` is its only caller.

**Why:** scale was set via `obj.transform.localScale` in the server's spawn callback — never replicated (clients used prefab scale) and never reset on pooled reuse.

- [ ] **Step 1: Network the scale on Projectile**

In `Projectile.cs`, below the `ShooterTeam` property, add:

```csharp
    // Networked so every peer (and every pooled reuse) applies the same visual scale.
    // The old spawn-callback localScale write was server-only and leaked across pool reuses.
    [Networked] private float Scale { get; set; }
```

Replace the `ServerInitialize` method with:

```csharp
    /// <summary>SERVER: set from PlayerCombat's spawn callback before Spawned runs.</summary>
    public void ServerInitialize(Vector2 dir, float speed, int damage, Team team, float scale)
    {
        Direction = dir.normalized;
        Speed = speed;
        Damage = damage;
        ShooterTeam = team;
        Scale = scale > 0f ? scale : 1f;
    }
```

In `Spawned()`, after the `hasHit = false;` line, add:

```csharp
        // Apply the replicated scale on every peer (and reset any stale pooled scale).
        // Scale is 0 only before the first server write reaches a client — keep prefab scale then.
        if (Scale > 0f) transform.localScale = Vector3.one * Scale;
```

- [ ] **Step 2: Update the spawn callback in PlayerCombat**

In `PlayerCombat.cs`, `ShootProjectile`, replace the spawn callback:

```csharp
            (runner, obj) =>
            {
                obj.transform.localScale = Vector3.one * projectileScale;
                Projectile p = obj.GetComponent<Projectile>();
                if (p != null) p.ServerInitialize(aimDirection, projectileSpeed, projectileDamage, shooterTeam);
            });
```

with:

```csharp
            (runner, obj) =>
            {
                Projectile p = obj.GetComponent<Projectile>();
                if (p != null) p.ServerInitialize(aimDirection, projectileSpeed, projectileDamage, shooterTeam, projectileScale);
            });
```

- [ ] **Step 3: Run the EditMode suite (compile check)**

Run the batchmode command. Expected: exit code 0. (If any other `ServerInitialize` caller exists the compile will catch it — the review found only this one.)

- [ ] **Step 4: Commit**

```powershell
git add Assets/Scripts/Player/Projectile.cs Assets/Scripts/Player/PlayerCombat.cs
git commit -m @'
fix(projectile): replicate scale via [Networked] instead of server-only localScale

Clients now render the configured projectile scale, and pooled reuse
can no longer leak a stale scale.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 8: Prune destroyed objects from the AoI registrar

**Files:**
- Modify: `Assets/Scripts/AreaOfInterest/AreaOfInterestRegistrar.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: no API change (private pruning only).

- [ ] **Step 1: Add pruning**

In `AreaOfInterestRegistrar.cs`, add below `ServerInitialize`:

```csharp
    /// <summary>
    /// Drop entries whose NetworkObject has been destroyed (e.g. a flag carrier who
    /// disconnected — Flag can't un-register an already-destroyed carrier). Destroyed
    /// Unity objects compare == null. Called from the rare mutation/join paths, not per tick.
    /// </summary>
    private void PruneDestroyed() => alwaysInterested.RemoveWhere(o => o == null);
```

Call it at the top of `AddAlwaysInterested` (before the null/guard checks are fine — after the runner guard):

```csharp
    public void AddAlwaysInterested(NetworkObject obj)
    {
        if (runner == null || !runner.IsServer || obj == null) return;
        PruneDestroyed();
        if (!alwaysInterested.Add(obj)) return; // already registered
        foreach (var player in runner.ActivePlayers)
            obj.SetPlayerAlwaysInterested(player, true);
    }
```

And at the top of `OnPlayerJoined`:

```csharp
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (!runner.IsServer) return;
        PruneDestroyed();
        // A late joiner must immediately be interested in every always-interested object.
        foreach (var obj in alwaysInterested)
            if (obj != null) obj.SetPlayerAlwaysInterested(player, true);
    }
```

- [ ] **Step 2: Run the EditMode suite (compile check)**

Run the batchmode command. Expected: exit code 0.

- [ ] **Step 3: Commit**

```powershell
git add Assets/Scripts/AreaOfInterest/AreaOfInterestRegistrar.cs
git commit -m @'
fix(aoi): prune destroyed NetworkObjects from the always-interested set

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 9: Event-driven enemy count (replace polling coroutines)

**Files:**
- Modify: `Assets/Scripts/Enemy/Base/Enemy.cs` (Task 5's edits to this file must already be applied)
- Modify: `Assets/Scripts/Enemy/Base/EnemySpawner.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `public void ServerSetOwnerSpawner(NetworkedEnemySpawner spawner)` on `Enemy`; `public void NotifyEnemyDespawned()` on `NetworkedEnemySpawner`.

**Why:** 10 spawners × 10 enemies = 100 idle `WaitUntil` coroutines polling every frame, and a coroutine dies silently if its spawner is disabled. Fusion already calls `Despawned` for us.

- [ ] **Step 1: Let Enemy notify its spawner on despawn**

In `Enemy.cs`, next to the other private fields, add:

```csharp
    // Server-only backref to the spawner that created us, for its live-count bookkeeping.
    private NetworkedEnemySpawner ownerSpawner;
```

Add below `ServerSetTeam`:

```csharp
    /// <summary>SERVER: called by the spawner's spawn callback so we can report our despawn.</summary>
    public void ServerSetOwnerSpawner(NetworkedEnemySpawner spawner)
    {
        ownerSpawner = spawner;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        // Event-driven count decrement (replaces the spawner's per-enemy polling coroutine).
        // ownerSpawner is set on the server only; Unity's == also guards a destroyed spawner.
        if (ownerSpawner != null)
        {
            ownerSpawner.NotifyEnemyDespawned();
            ownerSpawner = null;
        }
    }
```

- [ ] **Step 2: Rework the spawner**

In `EnemySpawner.cs`:

In `InitializeEnemy`, replace:

```csharp
        // Track enemy count
        CurrentEnemyCount++;

        // Subscribe to enemy despawn to update count
        StartCoroutine(WaitForEnemyDespawn(enemyNetObj));
```

with:

```csharp
        // Track enemy count; the Enemy reports back via NotifyEnemyDespawned when it dies
        // (event-driven — replaces one polling coroutine per live enemy).
        CurrentEnemyCount++;
        if (enemy != null)
        {
            enemy.ServerSetOwnerSpawner(this);
        }
```

(`enemy` is the `Enemy` local variable added in Task 5 Step 3. If Task 5 was somehow skipped, add `Enemy enemy = enemyObj.GetComponent<Enemy>();` above this block.)

Delete the entire `WaitForEnemyDespawn` coroutine method.

Add this method after `InitializeEnemy`:

```csharp
    /// <summary>SERVER: called by a spawned Enemy from its Despawned() callback.</summary>
    public void NotifyEnemyDespawned()
    {
        if (!HasStateAuthority) return;
        CurrentEnemyCount = Mathf.Max(0, CurrentEnemyCount - 1);
    }
```

- [ ] **Step 3: Run the EditMode suite (compile check)**

Run the batchmode command. Expected: exit code 0.

- [ ] **Step 4: Commit**

```powershell
git add Assets/Scripts/Enemy/Base/Enemy.cs Assets/Scripts/Enemy/Base/EnemySpawner.cs
git commit -m @'
refactor(enemy): event-driven spawner count via Despawned instead of polling coroutines

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

Unity verify (user, later): kill enemies near a spawner — replacements keep spawning up to the cap (count doesn't leak).

---

### Task 10: Full-inventory player no longer blocks coin pickup

**Files:**
- Modify: `Assets/Scripts/Coin Scripts/CoinPickup.cs` (`TryServerPickup`)

**Interfaces:** none.

- [ ] **Step 1: Continue past a full inventory**

In `TryServerPickup`, replace:

```csharp
            if (inventory.ServerAddCoin(coinData))
            {
                IsCollected = true;
                RPC_OnCoinCollected(transform.position);
                Runner.Despawn(Object);
            }
            return; // one player per tick is enough; coin is gone or inventory was full
```

with:

```csharp
            if (inventory.ServerAddCoin(coinData))
            {
                IsCollected = true;
                RPC_OnCoinCollected(transform.position);
                Runner.Despawn(Object);
                return; // coin is gone
            }
            // This player's inventory is full (maxCoins cap) — let another overlapping
            // player collect it this tick instead of permanently blocking the coin.
```

- [ ] **Step 2: Run the EditMode suite (compile check)**

Run the batchmode command. Expected: exit code 0.

- [ ] **Step 3: Commit**

```powershell
git add "Assets/Scripts/Coin Scripts/CoinPickup.cs"
git commit -m @'
fix(coins): a full-inventory player no longer blocks others from picking up a coin

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 11: TeamScoreManager duplicate guard must not Destroy a NetworkObject

**Files:**
- Modify: `Assets/Scripts/Coin Scripts/TeamScoreManager.cs`

**Interfaces:** none.

- [ ] **Step 1: Disable instead of destroy; reset the singleton on destroy**

In `Awake()`, replace:

```csharp
        else
        {
            Debug.LogWarning("Multiple TeamScoreManagers detected! Destroying duplicate.");
            Destroy(gameObject);
        }
```

with:

```csharp
        else
        {
            // Never Destroy() a spawned NetworkObject locally — that desyncs Fusion's
            // object table on this peer. Disable the duplicate and leave it inert.
            Debug.LogWarning("Multiple TeamScoreManagers detected! Disabling duplicate.");
            enabled = false;
        }
```

Add after `Awake()`:

```csharp
    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }
```

- [ ] **Step 2: Run the EditMode suite (compile check)**

Run the batchmode command. Expected: exit code 0.

- [ ] **Step 3: Commit**

```powershell
git add "Assets/Scripts/Coin Scripts/TeamScoreManager.cs"
git commit -m @'
fix(score): disable duplicate TeamScoreManager instead of destroying a NetworkObject

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 12: Server-side auto-deposit for players standing in their base

**Files:**
- Modify: `Assets/Scripts/Coin Scripts/HomeBase.cs` (class `NetworkedHomeBase`)

**Interfaces:**
- Consumes: existing server-only `occupants` HashSet, `NetworkedPlayerInventory.ServerDepositCoins()`, `CoinCount`, `TeamScoreManager.RPC_AddPoints`, `PlayerBuffs.ServerAddDepositedValue`.
- Produces: private `ServerDeposit(NetworkObject, NetworkedPlayerInventory)`; `RPC_RequestDeposit` now delegates to it.

**Why:** deposits only fired on trigger *enter*. A player standing in their base when coins scatter onto them never auto-deposited until leaving and re-entering.

- [ ] **Step 1: Extract the server deposit logic**

In `HomeBase.cs`, replace the body of `RPC_RequestDeposit` so validation stays and the deposit core moves to a reusable method:

```csharp
    /// <summary>
    /// RPC to request coin deposit. Called by client (manual deposit key / trigger enter),
    /// executed on server. Receives the NetworkObject directly to avoid lookup issues.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDeposit(NetworkObject playerNetObj)
    {
        // Validate the player NetworkObject
        if (playerNetObj == null || !playerNetObj.IsValid)
        {
            Debug.LogError("[SERVER] Invalid player NetworkObject passed to RPC_RequestDeposit");
            return;
        }

        NetworkedPlayerInventory inventory = playerNetObj.GetComponent<NetworkedPlayerInventory>();
        if (inventory == null)
        {
            Debug.LogError($"[SERVER] No NetworkedPlayerInventory component found on {playerNetObj.name}!");
            return;
        }

        // Verify player is on correct team (server-side check)
        if (!IsPlayerOnCorrectTeam(inventory))
        {
            Debug.LogWarning("[SERVER] Player tried to deposit at wrong base!");
            return;
        }

        ServerDeposit(playerNetObj, inventory);
    }

    /// <summary>
    /// SERVER: deposit this player's coins into the team score. Shared by the client-request
    /// RPC and the server-side occupant sweep. Safe to call with an empty inventory
    /// (ServerDepositCoins returns 0 and nothing happens).
    /// </summary>
    private void ServerDeposit(NetworkObject playerNetObj, NetworkedPlayerInventory inventory)
    {
        int points = inventory.ServerDepositCoins();
        if (points <= 0) return;

        TeamScoreManager scoreManager = TeamScoreManager.Instance;
        if (scoreManager == null)
        {
            Debug.LogError("[SERVER] TeamScoreManager not found in scene!");
            return;
        }

        scoreManager.RPC_AddPoints(baseTeam, points);

        // Notify all clients to play effects
        RPC_OnDeposit(playerNetObj.transform.position, points);

        // Credit the player's personal deposited-value total so buffs progress.
        PlayerBuffs buffs = playerNetObj.GetComponent<PlayerBuffs>();
        if (buffs != null) buffs.ServerAddDepositedValue(points);
    }
```

- [ ] **Step 2: Sweep occupants every tick on the server**

Add this method to `NetworkedHomeBase`:

```csharp
    /// <summary>
    /// SERVER: auto-deposit for anyone STANDING in their base when coins land on them
    /// (the trigger-enter path only fires on entry). Occupants whose player object is
    /// gone (disconnect) are pruned first so their PlayerRef doesn't linger forever.
    /// Cheap: occupant sets are tiny and the sweep skips players with no coins.
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || !autoDeposit || occupants.Count == 0) return;

        occupants.RemoveWhere(p => !Runner.TryGetPlayerObject(p, out _));

        foreach (PlayerRef occupant in occupants)
        {
            if (!Runner.TryGetPlayerObject(occupant, out NetworkObject playerObj)) continue;

            NetworkedPlayerInventory inventory = playerObj.GetComponent<NetworkedPlayerInventory>();
            if (inventory == null || inventory.CoinCount == 0) continue;
            if (!IsPlayerOnCorrectTeam(inventory)) continue;

            ServerDeposit(playerObj, inventory);
        }
    }
```

- [ ] **Step 3: Run the EditMode suite (compile check)**

Run the batchmode command. Expected: exit code 0.

- [ ] **Step 4: Commit**

```powershell
git add "Assets/Scripts/Coin Scripts/HomeBase.cs"
git commit -m @'
fix(coins): server-side auto-deposit sweep for players already standing in base

Deposits used to fire only on trigger enter, so coins picked up while
inside your base sat undeposited until you left and re-entered.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

Unity verify (user, later): stand inside your base, have a teammate toss coins onto you (kill an enemy nearby) — coins deposit without leaving the base.

---

## Final verification

- [ ] Run the full EditMode suite one last time — exit code 0, all suites green (including the new `HitCooldownLedgerTests`).
- [ ] `git log --oneline` shows 12 commits for this plan (one per task).
- [ ] `git status` shows only the pre-existing uncommitted files from the AoI/dash work (`NetworkProjectConfig.fusion`, `GameNetworkManager.cs`*, `NetworkBootMode.cs`, `NetworkedSpawnManager.cs`, `NetworkInputProvider.cs`, `PlayerCombat.cs`*, `PlayerController.cs`, `NetworkBootModeTests.cs`) — files marked * were also edited by this plan and are expected to contain both changes; do NOT commit the pre-existing hunks separately here.
- [ ] Remind the user: multi-peer Unity verify checklist is embedded per task ("Unity verify" notes) — flag drop on disconnect, dash gate while carrying, enemy colors on client, dual-attacker hits, standing-deposit.
