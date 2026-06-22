# Unified Damage Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route all player-melee and enemy-attack damage through one entry point (`CombatConfig.ResolveDamage`) that applies the distance-based territorial modifier lifted by coin-economy buffs, and delete the dead modifier systems.

**Architecture:** Meta-layer model — CTF is the win condition; coin milestones lift the territorial nerf. `CombatConfig.ResolveDamage` gathers the territorial modifier from `TeamManager` and the buff state from `TeamScoreManager`, composes them in the existing pure-math `CalculateFinalDamage`, and returns a rounded int. `PlayerCombat.Attack` and `Enemy.AttackPlayer` both call it.

**Tech Stack:** Unity 6.3 (6000.3.0f1), C# (`Assembly-CSharp`), Photon Fusion 2.0.9. No test assembly — verification is compile-clean + manual/observational (single-player Host, then Multiplayer Play Mode).

## Global Constraints

- New Input System only; single device-read site is `NetworkInputProvider` (not touched here).
- Gameplay timing in the simulation path uses `TickTimer`, not `Invoke`/`Time.time`/coroutines (no new timing added here).
- `Runner.Spawn`/`Despawn` and authoritative state changes happen under `HasStateAuthority` only. Damage is computed at the existing StateAuthority-gated call sites; do not move computation off the authority.
- Serialized authoring string fields stay `string` (normalized at read via `TeamUtil`). Do not convert them to enum fields.
- Before deleting a script, confirm it is not referenced by a scene/prefab/asset.
- Each task: compile clean in the Unity Editor (no console errors), then commit referencing **item #4**.
- Design reference: `docs/superpowers/specs/2026-06-22-unified-damage-pipeline-design.md`.

---

### Task 1: Delete the unused `TerritoryZone` "EXAMPLE"

Removing this first unblocks Task 3 — `TerritoryZone` is the only caller of the
`TeamScoreManager` getters that Task 3 deletes.

**Files:**
- Delete: `Assets/Scripts/Coin Scripts/TerritoryZone.cs`
- Delete: `Assets/Scripts/Coin Scripts/TerritoryZone.cs.meta`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing (removes `TerritoryZone`, `CalculateOutgoingDamage`, `CalculateIncomingDamage`).

- [ ] **Step 1: Confirm no scene/prefab/asset references**

Run (PowerShell, from repo root):
```
Get-ChildItem -Recurse -Include *.unity,*.prefab,*.asset Assets | Select-String -Pattern "TerritoryZone"
```
Expected: no output. (Already verified during design: only docs and the script itself mention it.)

- [ ] **Step 2: Delete the script and its meta file**

```bash
git rm "Assets/Scripts/Coin Scripts/TerritoryZone.cs" "Assets/Scripts/Coin Scripts/TerritoryZone.cs.meta"
```

- [ ] **Step 3: Compile clean**

Open the Unity Editor; confirm the Console shows no compile errors. (Nothing references `TerritoryZone`, so this is a clean removal.)

- [ ] **Step 4: Commit**

```bash
git commit -m "refactor(combat): remove unused TerritoryZone example (review item #4)"
```

---

### Task 2: Hoist the territorial-advantage formula into `TeamManager`

**Files:**
- Modify: `Assets/Scripts/Teams/TeamManager.cs` (add method after `GetDamageReceivedModifier`, ~line 69)
- Modify: `Assets/Scripts/Enemy/Base/PlayerTeamComponent.cs:55-73` (delegate `CalculateTerritorialAdvantage`)

**Interfaces:**
- Consumes: `TeamManager.GetTeamData(Team)`, `TeamData.basePosition` (Vector3).
- Produces: `float TeamManager.GetTerritorialAdvantage(Team team, Vector2 position)` — `+1` at own base, `-1` at enemy base, `0` at midfield/unknown, clamped `[-1, 1]`.

- [ ] **Step 1: Add `GetTerritorialAdvantage` to `TeamManager`**

Insert this method into `TeamManager` (after `GetDamageReceivedModifier`, before `AreEnemies`):

```csharp
    /// <summary>
    /// Distance-based territorial advantage for a team at a world position:
    /// +1 at own base, -1 at enemy base, 0 at midpoint (or when data is missing).
    /// Single source of the formula — players and the unified damage pipeline both use it.
    /// </summary>
    public float GetTerritorialAdvantage(Team team, Vector2 position)
    {
        if (team == Team.None) return 0f;

        TeamData myTeam = GetTeamData(team);
        if (myTeam == null) return 0f;

        Team opposing = team == Team.Team1 ? Team.Team2 : Team.Team1;
        TeamData enemyTeam = GetTeamData(opposing);
        if (enemyTeam == null) return 0f;

        float distToOwnBase = Vector2.Distance(position, myTeam.basePosition);
        float distToEnemyBase = Vector2.Distance(position, enemyTeam.basePosition);
        float totalDist = distToOwnBase + distToEnemyBase;
        if (totalDist < 0.01f) return 0f;

        float advantage = 1f - (2f * distToOwnBase / totalDist);
        return Mathf.Clamp(advantage, -1f, 1f);
    }
```

- [ ] **Step 2: Delegate from `PlayerTeamComponent`**

Replace the body of `CalculateTerritorialAdvantage` (`PlayerTeamComponent.cs:55-73`) with a delegation. The full method becomes:

```csharp
    /// <summary>+1 at own base, -1 at enemy base, 0 at midpoint.</summary>
    private float CalculateTerritorialAdvantage()
    {
        if (TeamManager.Instance == null || Team == Team.None) return 0f;
        return TeamManager.Instance.GetTerritorialAdvantage(Team, transform.position);
    }
```

(`GetCurrentTerritorialAdvantage`, `GetDamageDealtModifier`, `GetDamageReceivedModifier` are unchanged — they keep working via this method, and `DebugTeamDisplay` keeps reading them.)

- [ ] **Step 3: Compile clean**

Unity Console shows no errors.

- [ ] **Step 4: Commit**

```bash
git commit -am "refactor(teams): hoist territorial-advantage formula into TeamManager (review item #4)"
```

---

### Task 3: Replace `TeamScoreManager` territory getters with buff queries

**Files:**
- Modify: `Assets/Scripts/Coin Scripts/TeamScoreManager.cs:163-195` (replace the two getters)

**Interfaces:**
- Consumes: `[Networked]` bools `Team1DamageBuff`, `Team2DamageBuff`, `Team1DefenseBuff`, `Team2DefenseBuff` (already present).
- Produces: `bool TeamScoreManager.HasDamageBuff(Team team)`, `bool TeamScoreManager.HasDefenseBuff(Team team)` — `false` for `Team.None`/AI.

- [ ] **Step 1: Replace the getters**

Delete `GetTerritoryDamageMultiplier(string)` and `GetTerritoryDefenseMultiplier(string)` (lines 163-195) and replace with:

```csharp
    /// <summary>True once the team has unlocked its coin-milestone damage buff.</summary>
    public bool HasDamageBuff(Team team)
    {
        if (team == Team.Team1) return Team1DamageBuff;
        if (team == Team.Team2) return Team2DamageBuff;
        return false;
    }

    /// <summary>True once the team has unlocked its coin-milestone defense buff.</summary>
    public bool HasDefenseBuff(Team team)
    {
        if (team == Team.Team1) return Team1DefenseBuff;
        if (team == Team.Team2) return Team2DefenseBuff;
        return false;
    }
```

- [ ] **Step 2: Update the file-header comment to record the model**

Replace the class summary block at the top of `TeamScoreManager.cs` (the `DIAGNOSTIC VERSION …` block, lines 5-10) with:

```csharp
/// <summary>
/// Singleton manager that tracks team scores and unlocks coin-milestone buffs.
/// META-LAYER MODEL (review item #4): CTF is the win condition; coin milestones lift the
/// territorial combat nerf. HasDamageBuff/HasDefenseBuff feed CombatConfig.ResolveDamage.
/// See docs/superpowers/specs/2026-06-22-unified-damage-pipeline-design.md.
/// Place one on an empty GameObject in the Gameplay scene. PHOTON FUSION networked.
/// </summary>
```

- [ ] **Step 3: Compile clean**

Unity Console shows no errors. (The only previous caller of the removed getters, `TerritoryZone`, was deleted in Task 1. `UIManager` reads the `[Networked]` bools directly — unaffected.)

- [ ] **Step 4: Commit**

```bash
git commit -am "refactor(coins): expose buff state as HasDamageBuff/HasDefenseBuff (review item #4)"
```

---

### Task 4: Add `CombatConfig.ResolveDamage` — the single entry point

**Files:**
- Modify: `Assets/Scripts/ScriptableObjects/CombatConfig.cs` (generalize `CalculateFinalDamage`, add `ResolveDamage`, remove dead territorial-range fields, add header note)

**Interfaces:**
- Consumes: `TeamManager.Instance.GetTerritorialAdvantage(Team, Vector2)`, `TeamManager.Instance.GetDamageDealtModifier(Team, float)`, `TeamManager.Instance.GetDamageReceivedModifier(Team, float)`, `TeamScoreManager.Instance.HasDamageBuff(Team)`, `TeamScoreManager.Instance.HasDefenseBuff(Team)`, `RollCritical()`.
- Produces: `int CombatConfig.ResolveDamage(float baseDamage, Team attackerTeam, Vector2 attackerPos, Team defenderTeam, Vector2 defenderPos)`; and the generalized `float CombatConfig.CalculateFinalDamage(float baseDamage, float dealtModifier, float receivedModifier, bool isCritical = false)`.

- [ ] **Step 1: Add the model note to the class header**

Insert above the `[CreateAssetMenu...]` line at the top of `CombatConfig.cs`:

```csharp
// META-LAYER DAMAGE MODEL (review item #4): every attack is resolved by ResolveDamage below.
// finalDamage = base x globalDamageMultiplier x dealtModifier(attacker) x receivedModifier(defender) x crit.
// dealtModifier/receivedModifier come from TeamManager's distance-based territorial system;
// coin-milestone buffs (TeamScoreManager) lift the nerf: DamageBuff floors the outgoing
// modifier at 1.0, DefenseBuff caps the incoming modifier at 1.0.
// See docs/superpowers/specs/2026-06-22-unified-damage-pipeline-design.md.
```

- [ ] **Step 2: Remove the dead duplicate territorial-range fields**

Delete these two fields from the `[Header("Territorial Combat")]` block (lines 36-42); keep `territorialAdvantageEnabled`:

```csharp
    [Tooltip("Minimum damage multiplier at enemy base")]
    [Range(0.1f, 1.0f)]
    public float minTerritorialDamage = 0.5f;

    [Tooltip("Maximum damage multiplier at own base")]
    [Range(1.0f, 3.0f)]
    public float maxTerritorialDamage = 1.5f;
```

(The 0.5–1.5 range now lives solely in `TeamManager`. Verified no code references these fields.)

- [ ] **Step 3: Generalize `CalculateFinalDamage`**

Replace the existing `CalculateFinalDamage` method with the two-modifier version:

```csharp
    /// <summary>
    /// Pure-math composition from already-resolved modifiers. Called by ResolveDamage;
    /// kept separate so the arithmetic is trivial to reason about.
    /// </summary>
    public float CalculateFinalDamage(float baseDamage, float dealtModifier, float receivedModifier, bool isCritical = false)
    {
        float damage = baseDamage * globalDamageMultiplier;

        if (territorialAdvantageEnabled)
        {
            damage *= dealtModifier * receivedModifier;
        }

        if (isCritical)
        {
            damage *= criticalMultiplier;
        }

        return damage;
    }
```

- [ ] **Step 4: Add `ResolveDamage`**

Add directly below `CalculateFinalDamage`:

```csharp
    /// <summary>
    /// THE single entry point for all combat damage (review item #4). Gathers the distance-based
    /// territorial modifiers from TeamManager, applies the coin-economy buff lift from
    /// TeamScoreManager, rolls crit, and composes via CalculateFinalDamage. Returns a rounded,
    /// non-negative int. Call only on StateAuthority (the call sites already gate on it).
    /// </summary>
    public int ResolveDamage(float baseDamage,
                             Team attackerTeam, Vector2 attackerPos,
                             Team defenderTeam, Vector2 defenderPos)
    {
        float dealt = 1f;
        float received = 1f;

        TeamManager teams = TeamManager.Instance;
        if (teams != null)
        {
            float attackerAdvantage = teams.GetTerritorialAdvantage(attackerTeam, attackerPos);
            dealt = teams.GetDamageDealtModifier(attackerTeam, attackerAdvantage);

            float defenderAdvantage = teams.GetTerritorialAdvantage(defenderTeam, defenderPos);
            received = teams.GetDamageReceivedModifier(defenderTeam, defenderAdvantage);
        }

        TeamScoreManager scores = TeamScoreManager.Instance;
        if (scores != null && scores.Object != null && scores.Object.IsValid)
        {
            // DamageBuff lifts the outgoing nerf: never below neutral 1.0x.
            if (scores.HasDamageBuff(attackerTeam)) dealt = Mathf.Max(dealt, 1f);
            // DefenseBuff removes enemy-territory vulnerability: never above neutral 1.0x.
            if (scores.HasDefenseBuff(defenderTeam)) received = Mathf.Min(received, 1f);
        }

        bool isCritical = RollCritical();
        float finalDamage = CalculateFinalDamage(baseDamage, dealt, received, isCritical);
        return Mathf.Max(0, Mathf.RoundToInt(finalDamage));
    }
```

- [ ] **Step 5: Compile clean**

Unity Console shows no errors. (`Team` is global-namespace; `TeamManager`/`TeamScoreManager` are in the same `Assembly-CSharp`. `scores.Object.IsValid` is member access — no extra `using` needed.)

- [ ] **Step 6: Commit**

```bash
git commit -am "feat(combat): add CombatConfig.ResolveDamage unified pipeline (review item #4)"
```

---

### Task 5: Route player melee through `ResolveDamage`

**Files:**
- Modify: `Assets/Scripts/Player/PlayerCombat.cs` (add helper; update the enemy-hit branch at lines 160-166)

**Interfaces:**
- Consumes: `GameSettingsManager.Instance.GetCombatConfig()`, `CombatConfig.ResolveDamage(...)`, `PlayerTeamComponent.Team` (cached `teamComponent`), `EnemyTeamComponent.Team`.
- Produces: nothing public. `damageAmount` (serialized, default 25) is now the *base* damage.

- [ ] **Step 1: Add the melee-resolve helper**

Add this private method to `PlayerCombat` (e.g. directly after `Attack()`):

```csharp
    /// <summary>
    /// Resolves melee damage to a hit target through the unified pipeline (review item #4).
    /// Falls back to raw base damage if no CombatConfig is available.
    /// </summary>
    private int ResolveMeleeDamage(GameObject target, Vector2 targetPos)
    {
        CombatConfig config = GameSettingsManager.Instance != null
            ? GameSettingsManager.Instance.GetCombatConfig()
            : null;
        if (config == null) return damageAmount;

        Team myTeam = teamComponent != null ? teamComponent.Team : Team.None;

        Team targetTeam = Team.None;
        EnemyTeamComponent etc = target.GetComponent<EnemyTeamComponent>();
        if (etc != null)
        {
            targetTeam = etc.Team;
        }
        else
        {
            PlayerTeamComponent ptc = target.GetComponent<PlayerTeamComponent>();
            if (ptc != null) targetTeam = ptc.Team;
        }

        return config.ResolveDamage(damageAmount, myTeam, transform.position, targetTeam, targetPos);
    }
```

- [ ] **Step 2: Use it in the enemy-hit branch**

In `Attack()`, replace the enemy damage call (lines 160-166):

```csharp
            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null)
            {
                Vector2 knockbackDirection = (hit.transform.position - transform.position).normalized;
                Vector2 knockbackForce = new Vector2(knockbackDirection.x * knockbackStrength, knockbackUpward);
                enemy.TakeDamage(damageAmount, knockbackForce, hit.transform.position);
            }
```

with:

```csharp
            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null)
            {
                Vector2 knockbackDirection = (hit.transform.position - transform.position).normalized;
                Vector2 knockbackForce = new Vector2(knockbackDirection.x * knockbackStrength, knockbackUpward);
                int finalDamage = ResolveMeleeDamage(hit.gameObject, hit.transform.position);
                enemy.TakeDamage(finalDamage, knockbackForce, hit.transform.position);
            }
```

- [ ] **Step 3: Compile clean**

Unity Console shows no errors. (`teamComponent` is the existing cached `PlayerTeamComponent`; `damageAmount` is the existing serialized field.)

- [ ] **Step 4: Commit**

```bash
git commit -am "feat(combat): route player melee through unified damage pipeline (review item #4)"
```

---

### Task 6: Route enemy attacks through `ResolveDamage`

**Files:**
- Modify: `Assets/Scripts/Enemy/Base/Enemy.cs:174-184` (the territorial-modifier block in `AttackPlayer`)

**Interfaces:**
- Consumes: `GameSettingsManager.Instance.GetCombatConfig()`, `CombatConfig.ResolveDamage(...)`, `EnemyTeamComponent.Team` (cached `teamComponent`), `PlayerStatsHandler` + sibling `PlayerTeamComponent.Team`.
- Produces: nothing public.

- [ ] **Step 1: Replace the damage calculation**

In `AttackPlayer`, replace the current modifier block + damage call (lines 174-184):

```csharp
        // Calculate damage with territorial modifier
        int finalDamage = stats.attackDamage;
        if (teamComponent != null)
        {
            float attackModifier = teamComponent.GetDamageDealtModifier();
            finalDamage = Mathf.RoundToInt(stats.attackDamage * attackModifier);
        }

        // Deal damage to player
        player.TakeDamage(finalDamage);
        lastAttackTime = Time.time;
```

with:

```csharp
        // Calculate damage through the unified pipeline (review item #4).
        int finalDamage = stats.attackDamage;
        CombatConfig config = GameSettingsManager.Instance != null
            ? GameSettingsManager.Instance.GetCombatConfig()
            : null;
        if (config != null)
        {
            Team myTeam = teamComponent != null ? teamComponent.Team : Team.None;
            PlayerTeamComponent playerTeam = player.GetComponent<PlayerTeamComponent>();
            Team defenderTeam = playerTeam != null ? playerTeam.Team : Team.None;
            finalDamage = config.ResolveDamage(stats.attackDamage, myTeam, transform.position,
                                               defenderTeam, player.transform.position);
        }

        // Deal damage to player
        player.TakeDamage(finalDamage);
        lastAttackTime = Time.time;
```

- [ ] **Step 2: Compile clean**

Unity Console shows no errors. (`teamComponent` is the existing cached `EnemyTeamComponent`; AI team gets `1.0` dealt via `aiUsesTerritory == false`, so enemy behavior is unchanged except the player defender's territorial vulnerability + DefenseBuff now apply.)

- [ ] **Step 3: Commit**

```bash
git commit -am "feat(combat): route enemy attacks through unified damage pipeline (review item #4)"
```

---

### Task 7: End-to-end verification

**Files:** none (observational).

**Interfaces:** none.

- [ ] **Step 1: Full compile check**

Open the Unity Editor. Confirm the Console has zero compile errors and zero new warnings introduced by this work.

- [ ] **Step 2: Single-player territorial behavior**

Set `GameNetworkManager.singlePlayerMode = true`, run as Host. Confirm:
- Melee an enemy while standing near your own base → damage clearly above 25.
- Melee while pushed near the enemy base → damage clearly below 25 (~0.5x nerf).

- [ ] **Step 3: Single-player buff lift**

Kill enemies and deposit ≥50 coins at HomeBase until the Team1 DamageBuff icon lights in the UI. Confirm:
- Melee near the enemy base now does ~1.0x (≈25) instead of ~0.5x — the aggression nerf is gone, home-base bonus still present.
- Deposit to ≥100 coins → DefenseBuff icon lights → incoming enemy/territory damage to the player in enemy territory is capped at 1.0x.

- [ ] **Step 4: Multiplayer Play Mode**

Set `singlePlayerMode = false`, run 1 host + 1 virtual client. Confirm:
- Enemy/player health changes from melee match across both windows (authoritative, no divergence).
- A buff unlocked on the host is reflected in client-side combat (networked buff state).

- [ ] **Step 5: Final confirmation commit (if any tuning tweaks were made)**

If Steps 2-4 surfaced tuning changes (e.g. `globalDamageMultiplier`), commit them:

```bash
git commit -am "tune(combat): adjust unified damage values after verification (review item #4)"
```

Otherwise no commit needed — the pipeline work is already committed task-by-task.

---

## Self-Review

**Spec coverage:**
- Product decision recorded at top of changed files → Task 3 (TeamScoreManager header), Task 4 (CombatConfig header). ✓
- Single entry point taking base + attacker/defender team + position → Task 4 `ResolveDamage`. ✓
- Territorial-nerf-lifting (DamageBuff floors dealt at 1.0; DefenseBuff caps received at 1.0) → Task 4 Step 4. ✓
- Both melee sides apply → Task 4 composes `dealt * received`; call sites pass both positions (Tasks 5, 6). ✓
- PlayerCombat + Enemy both call it → Tasks 5, 6. ✓
- AI unaffected (aiUsesTerritory false → 1.0) → noted Task 6 Step 2; logic unchanged in TeamManager. ✓
- Projectile left for item #5 → not in plan (intentional). ✓
- Delete TerritoryZone → Task 1. ✓
- TeamScoreManager getters → buff queries → Task 3. ✓
- TeamManager `GetTerritorialAdvantage` hoist + PlayerTeamComponent delegate → Task 2. ✓
- Remove dead CombatConfig range fields → Task 4 Step 2. ✓
- StateAuthority-only computation → call sites already gated (PlayerCombat.Attack line 145, Enemy.AttackPlayer line 157); noted in Global Constraints. ✓
- Verification single-player + MPPM → Task 7. ✓

**Placeholder scan:** No TBD/TODO/"handle edge cases"; every code step shows full code. ✓

**Type consistency:** `ResolveDamage(float, Team, Vector2, Team, Vector2) → int`, `CalculateFinalDamage(float, float, float, bool) → float`, `HasDamageBuff(Team)/HasDefenseBuff(Team) → bool`, `GetTerritorialAdvantage(Team, Vector2) → float` — names/signatures match across Tasks 2-6. `damageAmount`/`stats.attackDamage` passed as the `baseDamage` float; `Enemy.TakeDamage(int,...)` and `PlayerStatsHandler.TakeDamage(float)` both accept the returned int. ✓
