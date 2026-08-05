# Meta-Damage Layer Simplification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Simplify the currently-inert meta-damage layer (`GameSettingsManager.combatConfig`/`.difficultyRingConfig` are unassigned and no `.asset` exists — see `docs/superpowers/specs/2026-08-04-integration-state-audit.md` §3.1): remove crit, delete the ring-based enemy-scaling and coin-bonus systems, redesign the territorial debuff into a defender-side own-base-distance vulnerability with Vanguard mitigation, and author + wire a real `CombatConfig.asset` with loud null-fallbacks.

**Architecture:** `Game.Combat.Core` (engine-free asmdef) keeps the pure vulnerability math in `TerritorialCombat.cs`; `TeamManager` normalizes a defender's position into a 0–1 own-base distance and applies the AI-exemption; `CombatConfig.ResolveDamage` is still the single entry point, now keyed by defender info instead of attacker info; `Game.Hud.Core`'s `TerritoryReadout` re-derives its display bucket from the same math so it can't drift. `PlayerCombat`/`Enemy`/`Projectile` all thread the defender's team+position into `ResolveDamage` at the point where the defender is actually known (which, for projectiles, is impact — not fire time, since there's no defender yet at fire time under a defender-side model).

**Tech Stack:** Unity 6.3 (6000.3.0f1), Photon Fusion 2.0.9, NUnit (EditMode tests), C#.

## Global Constraints

- Server-authoritative: `ResolveDamage`/`TakeDamage`/`AttackPlayer` logic only runs where `HasStateAuthority` is already checked by the existing call sites — do not change that gating.
- Positions sync via `NetworkRigidbody2D` / `[Networked]` anchors — never `NetworkTransform`. This plan reads existing synced positions; it does not add new position sync.
- `Game.Combat.Core` and `Game.Hud.Core` asmdefs have `noEngineReferences: true` — `TerritorialCombat.cs` and `TerritoryReadout.cs` must never `using UnityEngine` or reference any Unity type.
- Confirmed design values (from project-owner sign-off): continuous gradient, normalized against the human base-to-base distance (`Vector2.Distance(myBase, enemyBase)`, computed from existing `TeamData.basePosition` — no new arena-bounds asset, since `ArenaCenter` is being deleted). Max malus at full distance is **+150%** (multiplier 1.0x → 2.5x) at Vanguard tier 0. Vanguard tier 1 = half the malus (1.0x → 1.75x). Vanguard tier 2 = none (always 1.0x, at any distance). Enemies (`Team3AI`) and `Team.None` are **exempt as defenders** — always a 1.0x received modifier.
- `globalDamageMultiplier`, `knockbackMultiplier`, `scaledKnockback` on `CombatConfig` are out of scope — do not touch them.
- `normalDamageColor` / `bonusDamageColor` on `CombatConfig` are out of scope — do not touch them. Only `criticalDamageColor` is removed (crit deletion).
- Editor-closed test command (do NOT add `-nographics` — it kills the run silently on this machine):
  ```
  "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log
  ```
  Trust `Test run completed` in `r.log` over the shell's exit code. The `[Licensing::Module] Error: Access token is unavailable` log line is a red herring.
- Coverage note for this plan: only `Game.Combat.Core` (`TerritorialCombat.cs`) and `Game.Hud.Core` (`TerritoryReadout.cs`) are engine-free and NUnit-covered today. `CombatConfig`, `TeamManager`, `PlayerCombat`, `Enemy`, and `Projectile` have zero direct unit tests in this codebase (they're MonoBehaviour/ScriptableObject/NetworkBehaviour singletons) — verification for those tasks is compile-clean via the Unity batch command plus the grep-based evidence and in-editor play checks in Task 11, not new NUnit tests.
- Script GUID for `CombatConfig.cs` (needed when hand-authoring the new asset's `.meta`... actually needed for the *asset's* `m_Script` reference, not its own `.meta`): `fff48ee354ebcf244b97e4024cf8e1b4`.
- Already checked, no task needed: `Assets\Tests\EditMode\TeamBuffUnlockTests.cs` and `Assets\Tests\EditMode\BuffProgressTests.cs` test the generic `TeamBuffUnlock`/`BuffProgress` curve math only — they never touch `TerritorialCombat`/`TerritoryReadout` and need no changes for this redesign.

## Setup

- [ ] Before Task 1: create a new branch off `main` for this work, e.g. `git checkout main && git pull && git checkout -b feat/meta-damage-simplification`. All tasks below assume commits land on this branch. (The repo was on `feat/economy-feedback-surfaces` when this plan was written — do not build this work on top of that branch.)

---

### Task 1: Rewrite `TerritorialCombat.cs` — pure own-base-distance vulnerability math

**Files:**
- Modify: `Assets\Scripts\Combat\Core\TerritorialCombat.cs`
- Test: `Assets\Tests\EditMode\Combat\TerritorialCombatTests.cs`

**Interfaces:**
- Produces: `Game.Combat.Core.TerritorialCombat.ReceivedMultiplier(float ownBaseDistance01, int vanguardTier) -> float`, `TerritorialCombat.MaxVulnerabilityMalus` (const float, 1.5f), `TerritorialCombat.VanguardMaxTier` (const int, 2). Consumed by `TeamManager.GetDamageReceivedModifier` (Task 4) and `Game.Hud.Core.TerritoryReadout.Resolve` (Task 2).

- [ ] **Step 1: Write the failing tests**

Replace the full contents of `Assets\Tests\EditMode\Combat\TerritorialCombatTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Game.Combat.Core;
using Game.Buffs.Core;

public class TerritorialCombatTests
{
    [Test]
    public void ReceivedMultiplier_AtOwnBase_IsAlwaysNeutral()
    {
        Assert.AreEqual(1.0f, TerritorialCombat.ReceivedMultiplier(0f, 0), 1e-4f);
        Assert.AreEqual(1.0f, TerritorialCombat.ReceivedMultiplier(0f, 1), 1e-4f);
        Assert.AreEqual(1.0f, TerritorialCombat.ReceivedMultiplier(0f, 2), 1e-4f);
    }

    // At max distance, tier 0 = full +150% malus (x2.5), tier 1 = half (x1.75), tier 2 = none (x1.0).
    [TestCase(0, 2.5f)]
    [TestCase(1, 1.75f)]
    [TestCase(2, 1.0f)]
    public void ReceivedMultiplier_AtMaxDistance_ScalesWithVanguardTier(int tier, float expected)
    {
        Assert.AreEqual(expected, TerritorialCombat.ReceivedMultiplier(1f, tier), 1e-4f);
    }

    [Test]
    public void ReceivedMultiplier_ScalesLinearlyWithDistance()
    {
        Assert.AreEqual(1.75f, TerritorialCombat.ReceivedMultiplier(0.5f, 0), 1e-4f);
        Assert.AreEqual(1.375f, TerritorialCombat.ReceivedMultiplier(0.5f, 1), 1e-4f);
        Assert.AreEqual(1.0f, TerritorialCombat.ReceivedMultiplier(0.5f, 2), 1e-4f);
    }

    [TestCase(-1f)]
    [TestCase(-0.5f)]
    public void ReceivedMultiplier_ClampsNegativeDistanceToZero(float distance01)
    {
        Assert.AreEqual(1.0f, TerritorialCombat.ReceivedMultiplier(distance01, 0), 1e-4f);
    }

    [TestCase(1.5f)]
    [TestCase(99f)]
    public void ReceivedMultiplier_ClampsOverlongDistanceToOne(float distance01)
    {
        Assert.AreEqual(2.5f, TerritorialCombat.ReceivedMultiplier(distance01, 0), 1e-4f);
    }

    [TestCase(-1)]
    [TestCase(3)]
    [TestCase(99)]
    public void ReceivedMultiplier_ClampsTierOutOfRange(int tier)
    {
        float value = TerritorialCombat.ReceivedMultiplier(1f, tier);
        Assert.GreaterOrEqual(value, 1.0f - 1e-4f);
        Assert.LessOrEqual(value, 2.5f + 1e-4f);
    }

    // End-to-end pacing: team score -> Vanguard tier -> damage TAKEN far from own base.
    // A 10-player team, so the {12, 45} per-player averages are absolute scores of 120 and 450.
    private static readonly List<int> VanguardThresholds = new List<int> { 12, 45 };

    [TestCase(0, 2.5f)]      // match start: full malus at max distance
    [TestCase(119, 2.5f)]    // just short of T1
    [TestCase(120, 1.75f)]   // T1: half the malus removed
    [TestCase(449, 1.75f)]
    [TestCase(450, 1.0f)]    // T2: malus fully removed
    [TestCase(550, 1.0f)]    // typical end-state (~55 per player)
    public void MaxDistanceDamageTaken_TracksTeamEconomy(int teamScore, float expectedMultiplier)
    {
        int tier = TeamBuffUnlock.TeamTier(VanguardThresholds, teamScore, rosterSize: 10, maxTier: 2);
        float received = TerritorialCombat.ReceivedMultiplier(1f, tier);
        Assert.AreEqual(expectedMultiplier, received, 1e-4f);
    }

    // Standing on your own base is never penalised, however poor the team's economy is.
    [TestCase(0)]
    [TestCase(550)]
    public void OwnBaseDamageTaken_IsAlwaysNeutral(int teamScore)
    {
        int tier = TeamBuffUnlock.TeamTier(VanguardThresholds, teamScore, rosterSize: 10, maxTier: 2);
        Assert.AreEqual(1.0f, TerritorialCombat.ReceivedMultiplier(0f, tier), 1e-4f);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail (or don't compile against the old API)**

Run: `"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log`
Expected: compile error in `r.log` — `TerritorialCombat.ReceivedMultiplier` does not exist yet (the old class only has `InEnemyThird`/`DebuffWithVanguard`/`DealtMultiplier`/etc).

- [ ] **Step 3: Replace `TerritorialCombat.cs`**

Replace the full contents of `Assets\Scripts\Combat\Core\TerritorialCombat.cs`:

```csharp
namespace Game.Combat.Core
{
    /// <summary>
    /// Pure territorial-combat math: a DEFENDER takes more damage the farther they are from
    /// their OWN base — from enemy AI and the opposing human team alike. Vanguard tiers reduce
    /// a team's own vulnerability: tier 0 = full malus, tier 1 = half, tier 2 = none.
    /// Continuous, not quantized: the malus scales smoothly with distance from 1.0x at the own
    /// base up to a capped maximum at (or beyond) the enemy base.
    /// Replaces the old attacker-side "enemy third" model, which was one-sided (damage DEALT
    /// only) and quantized into two discrete states specifically for HUD legibility. This model
    /// is defender-side and continuous; Game.Hud.Core.TerritoryReadout buckets it back down for
    /// display without re-deriving the thresholds.
    /// Engine-free (this asmdef sets noEngineReferences) so it is testable outside Unity.
    /// See docs/superpowers/plans/2026-08-05-meta-damage-simplification.md.
    /// </summary>
    public static class TerritorialCombat
    {
        /// <summary>
        /// Extra damage-taken fraction at maximum distance from own base, before Vanguard.
        /// A defender at their own base always takes x1.0; at max distance with no Vanguard
        /// (tier 0) they take x(1 + this) = x2.5.
        /// </summary>
        public const float MaxVulnerabilityMalus = 1.5f;

        /// <summary>Vanguard's top tier. Each tier removes half of the remaining malus.</summary>
        public const int VanguardMaxTier = 2;

        /// <summary>
        /// Damage-taken multiplier for a defender at the given normalized own-base distance
        /// (0 = at their own base, 1 = at or beyond the enemy base, clamped) and Vanguard tier
        /// (clamped to [0, VanguardMaxTier]).
        /// </summary>
        public static float ReceivedMultiplier(float ownBaseDistance01, int vanguardTier)
        {
            float distance = Clamp01(ownBaseDistance01);
            int tier = ClampTier(vanguardTier);
            float remaining = 1f - 0.5f * tier;
            return 1f + MaxVulnerabilityMalus * distance * remaining;
        }

        private static float Clamp01(float value)
        {
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }

        private static int ClampTier(int tier)
        {
            return tier < 0 ? 0 : (tier > VanguardMaxTier ? VanguardMaxTier : tier);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run the same batch command as Step 2. Expected: `r.log` contains `Test run completed` and all `TerritorialCombatTests` cases pass (0 failures for this fixture).

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Combat/Core/TerritorialCombat.cs Assets/Tests/EditMode/Combat/TerritorialCombatTests.cs
git commit -m "refactor(combat): replace enemy-third attacker debuff with own-base-distance defender vulnerability"
```

---

### Task 2: Rewrite `TerritoryReadout.cs` — re-derive the HUD zone bucket from the new math

**Files:**
- Modify: `Assets\Scripts\Hud\Core\TerritoryReadout.cs`
- Test: `Assets\Tests\EditMode\Hud\TerritoryReadoutTests.cs`

**Interfaces:**
- Consumes: `Game.Combat.Core.TerritorialCombat.ReceivedMultiplier(float, int)` (Task 1).
- Produces: `Game.Hud.Core.TerritoryReadout.Resolve(float ownBaseDistance01, int vanguardTier) -> TerritoryDisplay` (same enum `Clear`/`Penalised`/`Lifted`, same signature shape as before but the float argument's meaning flips from "advantage" to "own-base distance"). Consumed by `TeamScoreDisplay.LateUpdate` (Task 9).

- [ ] **Step 1: Write the failing tests**

Replace the full contents of `Assets\Tests\EditMode\Hud\TerritoryReadoutTests.cs`:

```csharp
using NUnit.Framework;
using Game.Hud.Core;

/// <summary>
/// The zone indicator's displayed state folds in the team's Vanguard tier: the same distance
/// stops reading as penalised once the team has bought the vulnerability away. That fold is the
/// whole point of the merged Team Power strip — the buff is taught by the thing it changes.
/// </summary>
public class TerritoryReadoutTests
{
    [Test]
    public void AtOwnBase_AlwaysReadsClear()
    {
        Assert.AreEqual(TerritoryDisplay.Clear, TerritoryReadout.Resolve(0f, 0));
        Assert.AreEqual(TerritoryDisplay.Clear, TerritoryReadout.Resolve(0f, 2));
    }

    [Test]
    public void NearBaseThreshold_IsInclusive()
    {
        Assert.AreEqual(TerritoryDisplay.Clear, TerritoryReadout.Resolve(0.05f, 0), "at the threshold");
        Assert.AreEqual(TerritoryDisplay.Penalised, TerritoryReadout.Resolve(0.051f, 0), "just past it");
    }

    [Test]
    public void FarFromBase_ReadsPenalisedUntilVanguardIsMaxed()
    {
        Assert.AreEqual(TerritoryDisplay.Penalised, TerritoryReadout.Resolve(1f, 0), "locked");
        Assert.AreEqual(TerritoryDisplay.Penalised, TerritoryReadout.Resolve(1f, 1), "half lifted is still a penalty");
        Assert.AreEqual(TerritoryDisplay.Lifted, TerritoryReadout.Resolve(1f, 2), "fully lifted");
    }

    [Test]
    public void MaxVanguardTier_ReadsLiftedAtAnyDistance()
    {
        Assert.AreEqual(TerritoryDisplay.Lifted, TerritoryReadout.Resolve(0.5f, 2));
        Assert.AreEqual(TerritoryDisplay.Lifted, TerritoryReadout.Resolve(1f, 2));
    }

    [Test]
    public void TiersBeyondTheMaximumStillReadAsLifted()
    {
        Assert.AreEqual(TerritoryDisplay.Lifted, TerritoryReadout.Resolve(1f, 5));
        Assert.AreEqual(TerritoryDisplay.Penalised, TerritoryReadout.Resolve(1f, -1), "negative clamps to locked");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run the batch test command from Task 1 Step 2. Expected: failures — `TerritoryReadout.Resolve` still uses the old `InEnemyThird`/`DealtMultiplier` API and returns wrong buckets for these new inputs (e.g. `Resolve(0.05f, 0)` currently misreads the float as an "advantage" value, not a distance).

- [ ] **Step 3: Replace `TerritoryReadout.cs`**

Replace the full contents of `Assets\Scripts\Hud\Core\TerritoryReadout.cs`:

```csharp
using Game.Combat.Core;

namespace Game.Hud.Core
{
    /// <summary>What the zone indicator should show for the local player right now.</summary>
    public enum TerritoryDisplay
    {
        /// <summary>Close enough to your own base that the vulnerability is negligible.</summary>
        Clear,
        /// <summary>Far enough from your own base to be taking bonus damage from any attacker.</summary>
        Penalised,
        /// <summary>Far from your own base, but your team's Vanguard has removed the malus entirely.</summary>
        Lifted
    }

    /// <summary>
    /// Pure own-base-distance-to-display mapping that FOLDS IN the team's Vanguard tier.
    /// Deliberately calls TerritorialCombat.ReceivedMultiplier rather than re-deriving the malus,
    /// so "am I currently taking bonus damage" can't drift from the real damage math WHILE
    /// TERRITORIAL ADVANTAGE IS ENABLED. It does not consult CombatConfig.territorialAdvantageEnabled
    /// or TeamManager's AI-exemption, both of which gate the real damage path — if the flag is ever
    /// turned off, or for a non-human defender, the real path applies x1.0 while this can still
    /// resolve to Penalised. Low risk today: the flag defaults true, the local player driving this
    /// HUD is always a human defender, and a loud warning fires when the flag is off.
    /// The near-base cutoff below which this reads Clear is display-only — the real math has no
    /// such cutoff; it is continuous from the own base outward.
    /// Engine-free (this asmdef sets noEngineReferences) so it is testable outside Unity.
    /// See docs/superpowers/plans/2026-08-05-meta-damage-simplification.md.
    /// </summary>
    public static class TerritoryReadout
    {
        private const float NearBaseThreshold = 0.05f;

        public static TerritoryDisplay Resolve(float ownBaseDistance01, int vanguardTier)
        {
            if (ownBaseDistance01 <= NearBaseThreshold) return TerritoryDisplay.Clear;

            float multiplier = TerritorialCombat.ReceivedMultiplier(ownBaseDistance01, vanguardTier);
            return multiplier <= 1f ? TerritoryDisplay.Lifted : TerritoryDisplay.Penalised;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run the batch test command. Expected: `Test run completed`, all `TerritoryReadoutTests` cases pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hud/Core/TerritoryReadout.cs Assets/Tests/EditMode/Hud/TerritoryReadoutTests.cs
git commit -m "refactor(hud): re-derive TerritoryReadout from the own-base-distance vulnerability"
```

---

### Task 3: Delete the ring-based enemy-difficulty and coin-bonus system (A2 + A3)

**Files:**
- Delete: `Assets\Scripts\Enemy\AI\DifficultyRingConfig.cs`, `Assets\Scripts\Enemy\AI\DifficultyRingConfig.cs.meta`
- Delete: `Assets\Scripts\Enemy\Base\ArenaCenter.cs`, `Assets\Scripts\Enemy\Base\ArenaCenter.cs.meta`
- Delete: `Assets\Tests\EditMode\EnemyAI\DifficultyRingConfigTests.cs`, `Assets\Tests\EditMode\EnemyAI\DifficultyRingConfigTests.cs.meta`
- Modify: `Assets\Scripts\ScriptableObjects\Game Settings Manager.cs`

**Interfaces:**
- Produces: `GameSettingsManager` no longer has `difficultyRingConfig`/`GetDifficultyRingConfig()`. `GameSettingsManager.GetCombatConfig()` is unaffected (consumed in later tasks).

- [ ] **Step 1: Delete the ring files**

```bash
git rm Assets/Scripts/Enemy/AI/DifficultyRingConfig.cs Assets/Scripts/Enemy/AI/DifficultyRingConfig.cs.meta
git rm Assets/Scripts/Enemy/Base/ArenaCenter.cs Assets/Scripts/Enemy/Base/ArenaCenter.cs.meta
git rm Assets/Tests/EditMode/EnemyAI/DifficultyRingConfigTests.cs Assets/Tests/EditMode/EnemyAI/DifficultyRingConfigTests.cs.meta
```

- [ ] **Step 2: Remove the ring field + accessor from `GameSettingsManager`**

In `Assets\Scripts\ScriptableObjects\Game Settings Manager.cs`, remove:

```csharp
    [Header("Enemy Difficulty")]
    [Tooltip("Concentric difficulty rings applied to enemies by distance from center.")]
    [SerializeField] private DifficultyRingConfig difficultyRingConfig;
```

and remove:

```csharp
    /// <summary>
    /// Get the shared enemy difficulty ring configuration (may be null if unassigned).
    /// </summary>
    public DifficultyRingConfig GetDifficultyRingConfig()
    {
        return difficultyRingConfig;
    }
```

The file should now read (in full):

```csharp
using UnityEngine;

/// <summary>
/// Host/match configuration holder: the shared config assets every gameplay system reads, plus the
/// two server-authoritative match rules MatchManager consumes.
///
/// This is NOT where client preferences live. Volume, resolution, camera shake and damage numbers
/// are client-local and belong to SettingsStore / the options menu — they are per-player, must not
/// affect simulation, and are needed in MainMenu.unity, where this Gameplay-scene singleton does
/// not exist. See docs/superpowers/specs/2026-07-29-options-settings-design.md.
///
/// The fields below are authored by whoever runs the host or dedicated server. A client must never
/// be able to write them: they decide when the match ends for everyone.
/// </summary>
public class GameSettingsManager : MonoBehaviour
{
    public static GameSettingsManager Instance { get; private set; }

    [Header("Combat Configuration")]
    [SerializeField] private CombatConfig combatConfig;

    [Header("Match Settings")]
    [Tooltip("Match time limit in minutes (0 = no limit)")]
    public float matchTimeLimit = 0f;

    [Tooltip("Sudden Death hard cap in minutes (0 = off). Operations safety valve only: on " +
             "expiry the match resolves as a draw so a headless server cannot wedge on an " +
             "unwinnable match. Leave at 0 — draws are unreachable in default play.")]
    public float suddenDeathHardCap = 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Get the combat configuration
    /// </summary>
    public CombatConfig GetCombatConfig()
    {
        return combatConfig;
    }
}
```

- [ ] **Step 3: Grep-verify nothing else references the deleted types**

Run:
```bash
grep -rli "DifficultyRingConfig\|ArenaCenter\|RingTier" Assets/Scripts Assets/Tests
```
Expected: no output (Task 8 will remove `Enemy.cs`'s references — if this grep still shows `Enemy.cs`, that's expected until Task 8 lands; everything else should be clean now).

- [ ] **Step 4: Commit**

```bash
git add -A Assets/Scripts/Enemy/AI Assets/Scripts/Enemy/Base/ArenaCenter.cs* Assets/Tests/EditMode/EnemyAI "Assets/Scripts/ScriptableObjects/Game Settings Manager.cs"
git commit -m "refactor(enemy): delete ring-based difficulty scaling and coin-drop bonus system"
```

---

### Task 4: Rewrite `TeamManager.cs` and `TeamData.cs` — own-base-distance API, drop the AI toggle

**Files:**
- Modify: `Assets\Scripts\Teams\TeamManager.cs`
- Modify: `Assets\Scripts\Teams\TeamData.cs`

**Interfaces:**
- Consumes: `Game.Combat.Core.TerritorialCombat.ReceivedMultiplier(float, int)` (Task 1).
- Produces: `TeamManager.GetOwnBaseDistance01(Team team, Vector2 position) -> float`, `TeamManager.GetDamageReceivedModifier(Team defender, float ownBaseDistance01, int vanguardTier) -> float`. Consumed by `CombatConfig.ResolveDamage` (Task 5) and `TeamScoreDisplay.LateUpdate` (Task 9).

- [ ] **Step 1: Rewrite `TeamManager.cs`**

Replace the full contents of `Assets\Scripts\Teams\TeamManager.cs`:

```csharp
using UnityEngine;
using Game.Combat.Core;

public class TeamManager : MonoBehaviour
{
    public static TeamManager Instance { get; private set; }

    [Header("Team Configuration")]
    [SerializeField] private TeamData team1Data;
    [SerializeField] private TeamData team2Data;
    [SerializeField] private TeamData team3Data; // AI/NPC team

    private void Awake()
    {
        // Singleton pattern - initialize as early as possible
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Validate team data
        if (team1Data == null)
            Debug.LogError("⚠️ Team1Data not assigned in TeamManager!");

        if (team2Data == null)
            Debug.LogError("⚠️ Team2Data not assigned in TeamManager!");
    }

    // ---- Enum-keyed API. Bridges to the configured TeamData assets via TeamUtil. ----

    /// <summary>Get the TeamData asset for a Team enum value.</summary>
    public TeamData GetTeamData(Team team)
    {
        if (team == Team.None) return null;
        if (team1Data != null && TeamUtil.Normalize(team1Data.teamID) == team) return team1Data;
        if (team2Data != null && TeamUtil.Normalize(team2Data.teamID) == team) return team2Data;
        if (team3Data != null && TeamUtil.Normalize(team3Data.teamID) == team) return team3Data;
        return null;
    }

    /// <summary>
    /// Damage-received modifier for a DEFENDING team. Enemies (Team3AI) and unassigned teams have
    /// no meaningful home base and are exempt — always x1.0. Human defenders (Team1/Team2) take
    /// the own-base-distance vulnerability, reduced by their team's Vanguard tier.
    /// </summary>
    public float GetDamageReceivedModifier(Team defender, float ownBaseDistance01, int vanguardTier)
    {
        if (defender != Team.Team1 && defender != Team.Team2) return 1.0f;
        return TerritorialCombat.ReceivedMultiplier(ownBaseDistance01, vanguardTier);
    }

    /// <summary>
    /// A team's distance from their OWN base, normalized 0 (at base) to 1 (at or beyond the
    /// enemy base, clamped). The reference distance is the gap between the two human bases —
    /// no separate arena-bounds asset needed. Returns 0 when team/opposing data is missing.
    /// Single source of the formula — the damage pipeline and the HUD zone readout both use it.
    /// </summary>
    public float GetOwnBaseDistance01(Team team, Vector2 position)
    {
        if (team == Team.None) return 0f;

        TeamData myTeam = GetTeamData(team);
        if (myTeam == null) return 0f;

        Team opposing = team == Team.Team1 ? Team.Team2 : Team.Team1;
        TeamData enemyTeam = GetTeamData(opposing);
        if (enemyTeam == null) return 0f;

        float maxDistance = Vector2.Distance(myTeam.basePosition, enemyTeam.basePosition);
        if (maxDistance < 0.01f) return 0f;

        float distToOwnBase = Vector2.Distance(position, myTeam.basePosition);
        return Mathf.Clamp01(distToOwnBase / maxDistance);
    }

    /// <summary>PvPvE: distinct assigned teams are hostile.</summary>
    public bool AreEnemies(Team a, Team b)
    {
        return TeamUtil.AreEnemies(a, b);
    }

    /// <summary>True if the team is the AI team.</summary>
    public bool IsAITeam(Team team)
    {
        return team == Team.Team3AI;
    }

    /// <summary>The two human teams.</summary>
    public Team[] GetPlayerTeamsEnum()
    {
        return new Team[] { Team.Team1, Team.Team2 };
    }
}
```

Note: `aiUsesTerritory` is gone — the AI exemption from Task's B3/B4 decision is now the unconditional `defender != Team.Team1 && defender != Team.Team2` check inside `GetDamageReceivedModifier`, so there is no longer a separate toggle to keep in sync. `Gameplay.unity`'s `aiUsesTerritory: 0` line (scene, ~line 13568, on the `TeamManager` component) becomes orphaned data that Unity silently drops next time it saves the scene — same self-healing behavior as the `combatConfig`/`difficultyRingConfig` fields noted in the audit. No manual scene edit needed for this removal.

- [ ] **Step 2: Remove the dead per-team toggle from `TeamData.cs`**

In `Assets\Scripts\Teams\TeamData.cs`, remove:

```csharp
    [Tooltip("Should players on this team use territorial advantage?")]
    public bool usesTerritorialAdvantage = true;
```

(Confirmed dead: it was declared but never read by any code path — `GetDamageDealtModifier`/`GetTerritorialAdvantage` never consulted it.) The three `Team*Data.asset` files' `usesTerritorialAdvantage: N` lines become orphaned data and self-heal the same way — no manual `.asset` edits needed.

- [ ] **Step 3: Compile-check**

Run: `"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log`
Expected: `CombatConfig.cs` (still calling the old `GetTerritorialAdvantage`/`GetDamageDealtModifier` API at this point) will fail to compile — that's expected until Task 5. Confirm the ONLY compile errors in `r.log` are in `CombatConfig.cs` referencing the now-removed `TeamManager` methods, and that `TeamManager.cs`/`TeamData.cs` themselves compile clean.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Teams/TeamManager.cs Assets/Scripts/Teams/TeamData.cs
git commit -m "refactor(teams): own-base-distance API replaces territorial-advantage/enemy-third API"
```

---

### Task 5: Rewrite `CombatConfig.cs` — remove crit, flip `ResolveDamage` to defender-side, add loud missing-config warning

**Files:**
- Modify: `Assets\Scripts\ScriptableObjects\CombatConfig.cs`

**Interfaces:**
- Consumes: `TeamManager.Instance.GetOwnBaseDistance01`/`GetDamageReceivedModifier` (Task 4), `TeamScoreManager.Instance.VanguardTier(Team)` (unchanged, already generic).
- Produces: `CombatConfig.ResolveDamage(float baseDamage, Team defenderTeam, Vector2 defenderPos) -> int` (attacker→defender signature flip). `CombatConfig.WarnMissingOnce()` (new static method) — consumed by `PlayerCombat` (Task 6), `Projectile` (Task 7), `Enemy` (Task 8) wherever `GameSettingsManager.Instance.GetCombatConfig()` returns null.

- [ ] **Step 1: Replace `CombatConfig.cs`**

Replace the full contents of `Assets\Scripts\ScriptableObjects\CombatConfig.cs`:

```csharp
using UnityEngine;

// META-LAYER DAMAGE MODEL: every attack is resolved by ResolveDamage below.
// finalDamage = base x globalDamageMultiplier x receivedModifier(defender).
// receivedModifier is the own-base-distance vulnerability: a DEFENDER takes more damage the
// farther they are from their OWN base, from any attacker — enemy AI and the opposing human
// team alike. Enemies (Team3AI) have no meaningful home base and are exempt (always x1.0).
// Vanguard tiers reduce a team's own vulnerability: tier 0 = full malus, tier 1 = half,
// tier 2 = none. There is no attacker-side modifier and no crit — one modifier, one side,
// applied to the character taking the hit.
// See docs/superpowers/plans/2026-08-05-meta-damage-simplification.md.
[CreateAssetMenu(fileName = "CombatConfig", menuName = "Game/Combat Configuration")]
public class CombatConfig : ScriptableObject
{
    [Header("Damage Settings")]
    [Tooltip("Base damage multiplier for all attacks")]
    [Range(0.1f, 5.0f)]
    public float globalDamageMultiplier = 1.0f;

    [Header("Knockback Settings")]
    [Tooltip("Global knockback strength multiplier")]
    [Range(0.1f, 3.0f)]
    public float knockbackMultiplier = 1.0f;

    [Tooltip("Should knockback be affected by damage dealt?")]
    public bool scaledKnockback = true;

    [Header("Attack Timing")]
    [Tooltip("Global attack speed multiplier (higher = faster)")]
    [Range(0.5f, 2.0f)]
    public float attackSpeedMultiplier = 1.0f;

    [Header("Territorial Combat")]
    [Tooltip("Enable the own-base-distance vulnerability. Turning this OFF also makes the ENTIRE " +
             "team-buff layer inert, because Vanguard exists only to reduce this vulnerability.")]
    public bool territorialAdvantageEnabled = true;

    [Header("Visual Feedback")]
    [Tooltip("Damage number prefab")]
    public GameObject damageNumberPrefab;

    [Tooltip("Color for normal damage")]
    public Color normalDamageColor = Color.white;

    [Tooltip("Color for territorial bonus damage")]
    public Color bonusDamageColor = Color.yellow;

    [Header("Hit Effects")]
    public GameObject hitEffectPrefab;
    public AudioClip hitSound;
    [Range(0f, 1f)]
    public float hitSoundVolume = 0.5f;

    // Not serialized: resets on domain reload, which is exactly the cadence we want for a
    // once-per-session operator warning.
    [System.NonSerialized] private bool warnedTerritoryDisabled;
    private static bool warnedConfigMissing;

    /// <summary>
    /// Pure-math composition from an already-resolved modifier. Called by ResolveDamage;
    /// kept separate so the arithmetic is trivial to reason about.
    /// </summary>
    public float CalculateFinalDamage(float baseDamage, float receivedModifier)
    {
        float damage = baseDamage * globalDamageMultiplier;

        if (territorialAdvantageEnabled)
        {
            damage *= receivedModifier;
        }

        return damage;
    }

    /// <summary>
    /// THE single entry point for all combat damage. Reads the DEFENDER's own-base-distance
    /// vulnerability and their team's Vanguard tier, and composes via CalculateFinalDamage.
    /// Returns a rounded, non-negative int. Call only on StateAuthority (the call sites already
    /// gate on it).
    /// </summary>
    public int ResolveDamage(float baseDamage, Team defenderTeam, Vector2 defenderPos)
    {
        float received = 1f;

        if (territorialAdvantageEnabled)
        {
            TeamManager teams = TeamManager.Instance;
            if (teams != null)
            {
                int vanguardTier = 0;

                TeamScoreManager scores = TeamScoreManager.Instance;
                if (scores != null && scores.Object != null && scores.Object.IsValid)
                    vanguardTier = scores.VanguardTier(defenderTeam);

                float distance01 = teams.GetOwnBaseDistance01(defenderTeam, defenderPos);
                received = teams.GetDamageReceivedModifier(defenderTeam, distance01, vanguardTier);
            }
        }
        else
        {
            WarnTerritoryDisabledOnce();
        }

        float finalDamage = CalculateFinalDamage(baseDamage, received);
        return Mathf.Max(0, Mathf.RoundToInt(finalDamage));
    }

    /// <summary>
    /// The team buffs are silent no-ops whenever territorialAdvantageEnabled is false (they are
    /// only ever multiplied in inside that flag's branch). Say so out loud instead.
    /// </summary>
    private void WarnTerritoryDisabledOnce()
    {
        if (warnedTerritoryDisabled) return;
        warnedTerritoryDisabled = true;
        Debug.LogWarning("⚠️ CombatConfig.territorialAdvantageEnabled is FALSE — the own-base " +
                         "vulnerability and the entire Vanguard team-buff layer are inert. Coin " +
                         "deposits then buy nothing at the team level.");
    }

    /// <summary>
    /// GameSettingsManager.combatConfig is unassigned. Loud, once-per-session: this exact silent-
    /// fallback shape (raw base damage, no global multiplier, no vulnerability, no Vanguard) is
    /// what let the whole meta-damage layer no-op for weeks behind a green test suite.
    /// </summary>
    public static void WarnMissingOnce()
    {
        if (warnedConfigMissing) return;
        warnedConfigMissing = true;
        Debug.LogWarning("⚠️ GameSettingsManager.combatConfig is unassigned — combat damage is " +
                         "falling back to raw base values: no global multiplier, no own-base " +
                         "vulnerability, no Vanguard scaling.");
    }
}
```

- [ ] **Step 2: Compile-check**

Run the batch test command. Expected: `PlayerCombat.cs` and `Enemy.cs` (still calling `ResolveDamage(baseDamage, attackerTeam, attackerPos)` with attacker semantics at this point) still compile — the signature shape (`float, Team, Vector2`) is unchanged, only the *meaning* of the last two arguments changed, so this alone won't break the build. Confirm `r.log` shows `Test run completed` with no NEW compile errors introduced by this file (crit is gone from `CombatConfig.cs` itself; call sites still pass attacker info, which is a semantic bug fixed in Tasks 6–8, not a compile error).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/ScriptableObjects/CombatConfig.cs
git commit -m "refactor(combat): remove crit; ResolveDamage now keyed by defender team+position"
```

---

### Task 6: Update `PlayerCombat.cs` — thread the defender through melee, resolve projectile damage at impact instead of at fire time

**Files:**
- Modify: `Assets\Scripts\Player\PlayerCombat.cs`

**Interfaces:**
- Consumes: `CombatConfig.ResolveDamage(float, Team, Vector2)` (Task 5), `CombatConfig.WarnMissingOnce()` (Task 5), `Enemy.Team` (public networked property, unchanged).
- Produces: `PlayerCombat.ResolveMeleeDamage(Team defenderTeam, Vector2 defenderPos) -> int` (was parameterless). `ResolveProjectileDamage()` is DELETED — `Projectile.ServerInitialize` now takes the raw, unresolved base damage (Task 7 resolves it at impact).

- [ ] **Step 1: Update `ApplyMeleeHits` to pass the defender through**

In `Assets\Scripts\Player\PlayerCombat.cs`, in `ApplyMeleeHits`, change the enemy branch:

```csharp
            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null)
            {
                Vector2 knockbackDirection = (hit.transform.position - transform.position).normalized;
                Vector2 knockbackForce = new Vector2(knockbackDirection.x * stats.attackForce, knockbackUpward);
                int finalDamage = ResolveMeleeDamage(enemy.Team, hit.transform.position);
                enemy.TakeDamage(finalDamage, knockbackForce, hit.transform.position);
                RPC_HitFeedback(enemy.Object.Id, hit.transform.position, finalDamage);
                continue;
            }
```

and the player branch:

```csharp
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

- [ ] **Step 2: Replace `ResolveMeleeDamage()` with a defender-parameterized version, and delete `ResolveProjectileDamage()`**

Replace:

```csharp
    /// <summary>
    /// Resolves melee damage through the unified pipeline. The defender no longer participates in
    /// the calculation (the received-side territorial modifier is gone), so no target lookup here.
    /// Falls back to raw base damage if no CombatConfig is available.
    /// </summary>
    private int ResolveMeleeDamage()
    {
        CombatConfig config = GameSettingsManager.Instance != null
            ? GameSettingsManager.Instance.GetCombatConfig()
            : null;
        if (config == null) return Mathf.RoundToInt(stats.attackDamage);

        Team myTeam = teamComponent != null ? teamComponent.Team : Team.None;
        return config.ResolveDamage(stats.attackDamage, myTeam, transform.position);
    }

    /// <summary>
    /// Resolves projectile damage through the unified pipeline, taxed by where the shot was FIRED
    /// from (the shooter's position at the moment of firing) — committing deep into enemy
    /// territory is what carries the tax, not where the shot lands. Mirrors ResolveMeleeDamage().
    /// Falls back to the raw authored projectileDamage if no CombatConfig is available.
    /// Note: crit (via ResolveDamage) is now rolled once per shot at fire time; previously
    /// projectiles bypassed the pipeline entirely and never crit.
    /// </summary>
    private int ResolveProjectileDamage()
    {
        CombatConfig config = GameSettingsManager.Instance != null
            ? GameSettingsManager.Instance.GetCombatConfig()
            : null;
        if (config == null) return projectileDamage;

        Team myTeam = teamComponent != null ? teamComponent.Team : Team.None;
        return config.ResolveDamage(projectileDamage, myTeam, transform.position);
    }
```

with:

```csharp
    /// <summary>
    /// Resolves melee damage through the unified pipeline, keyed by the DEFENDER's team and
    /// position — a defender takes more damage the farther they are from their own base.
    /// Falls back to raw base damage (with a loud one-time warning) if no CombatConfig is assigned.
    /// </summary>
    private int ResolveMeleeDamage(Team defenderTeam, Vector2 defenderPos)
    {
        CombatConfig config = GameSettingsManager.Instance != null
            ? GameSettingsManager.Instance.GetCombatConfig()
            : null;
        if (config == null)
        {
            CombatConfig.WarnMissingOnce();
            return Mathf.RoundToInt(stats.attackDamage);
        }

        return config.ResolveDamage(stats.attackDamage, defenderTeam, defenderPos);
    }
```

(`ResolveProjectileDamage()` is gone — projectile damage is now resolved at impact in `Projectile.cs`, Task 7, because the defender isn't known yet at fire time under the defender-side model.)

- [ ] **Step 3: Update `ShootProjectile` to spawn with raw base damage**

Find:

```csharp
        Team shooterTeam = teamComponent != null ? teamComponent.Team : Team.None;

        // Resolve once per shot, before spawning, at the shooter's fire position — not inside the
        // spawn callback and not per hit (that would tax/crit on landing position or per-hit,
        // neither of which is the intent).
        int resolvedDamage = ResolveProjectileDamage();

        NetworkObject spawned = Runner.Spawn(
            projectilePrefab,
            projectileSpawnPoint.position,
            Quaternion.identity,
            Object.InputAuthority,
            (runner, obj) =>
            {
                Projectile p = obj.GetComponent<Projectile>();
                if (p != null) p.ServerInitialize(aimDirection, projectileSpeed, resolvedDamage, shooterTeam, projectileScale);
            });
```

Replace with:

```csharp
        Team shooterTeam = teamComponent != null ? teamComponent.Team : Team.None;

        // The raw base damage travels with the projectile; final damage is resolved at impact
        // (Projectile.cs) against the DEFENDER's team+position, since the defender isn't known
        // until something is actually hit.
        NetworkObject spawned = Runner.Spawn(
            projectilePrefab,
            projectileSpawnPoint.position,
            Quaternion.identity,
            Object.InputAuthority,
            (runner, obj) =>
            {
                Projectile p = obj.GetComponent<Projectile>();
                if (p != null) p.ServerInitialize(aimDirection, projectileSpeed, projectileDamage, shooterTeam, projectileScale);
            });
```

- [ ] **Step 4: Compile-check**

Run the batch test command. Expected: `PlayerCombat.cs` compiles. `Projectile.cs` will still fail (its `ServerInitialize`/`Damage` API hasn't changed yet — Task 7) — confirm that's the ONLY remaining compile error surface.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Player/PlayerCombat.cs
git commit -m "refactor(player): melee damage keyed by defender; projectile damage deferred to impact"
```

---

### Task 7: Update `Projectile.cs` — resolve damage at impact against the defender

**Files:**
- Modify: `Assets\Scripts\Player\Projectile.cs`

**Interfaces:**
- Consumes: `CombatConfig.ResolveDamage(float, Team, Vector2)` (Task 5), `CombatConfig.WarnMissingOnce()` (Task 5), `Enemy.Team` (public, unchanged), `PlayerCombat.ServerInitialize` call site (Task 6, already updated to pass raw `projectileDamage`).
- Produces: `Projectile.ServerInitialize(Vector2 dir, float speed, int baseDamage, Team team, float scale)` (param renamed `damage` → `baseDamage`, semantics changed from resolved to raw).

- [ ] **Step 1: Rename the networked field and update `ServerInitialize`**

In `Assets\Scripts\Player\Projectile.cs`, change:

```csharp
    [Networked] private int Damage { get; set; }
```

to:

```csharp
    [Networked] private int BaseDamage { get; set; }
```

Change:

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

to:

```csharp
    /// <summary>SERVER: set from PlayerCombat's spawn callback before Spawned runs. baseDamage is
    /// the RAW, unresolved damage — final damage is resolved at impact against the defender.</summary>
    public void ServerInitialize(Vector2 dir, float speed, int baseDamage, Team team, float scale)
    {
        Direction = dir.normalized;
        Speed = speed;
        BaseDamage = baseDamage;
        ShooterTeam = team;
        Scale = scale > 0f ? scale : 1f;
    }
```

- [ ] **Step 2: Resolve damage at impact in `OnTriggerEnter2D`, and add the resolution helper**

Replace the full `OnTriggerEnter2D` method:

```csharp
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!HasStateAuthority || hasHit) return;


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

        // Enemy hit
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy != null)
        {
            Vector2 dir = ((Vector2)other.transform.position - (Vector2)transform.position).normalized;
            int finalDamage = ResolveDamage(enemy.Team, other.transform.position);
            enemy.TakeDamage(finalDamage, dir * 5f, other.transform.position);
            RPC_HitFeedback(enemy.Object.Id, other.transform.position, finalDamage);
            Hit();
            return;
        }

        // Ground / wall
        if (other.gameObject.layer == LayerMask.NameToLayer("Ground") || other.CompareTag("Wall"))
            Hit();
    }

    /// <summary>
    /// Resolves impact damage through the unified pipeline, keyed by the DEFENDER's team and
    /// position at the moment of impact — a defender takes more damage the farther they are from
    /// their own base. Resolved here (not at fire time) because the defender is only known on
    /// hit. Falls back to the raw authored base damage (with a loud one-time warning) if no
    /// CombatConfig is assigned.
    /// </summary>
    private int ResolveDamage(Team defenderTeam, Vector2 defenderPos)
    {
        CombatConfig config = GameSettingsManager.Instance != null
            ? GameSettingsManager.Instance.GetCombatConfig()
            : null;
        if (config == null)
        {
            CombatConfig.WarnMissingOnce();
            return BaseDamage;
        }

        return config.ResolveDamage(BaseDamage, defenderTeam, defenderPos);
    }
```

- [ ] **Step 3: Compile-check**

Run the batch test command. Expected: `Projectile.cs` and `PlayerCombat.cs` both compile. `Enemy.cs` still fails (Task 8 not done yet) — confirm that's the only remaining error surface.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Player/Projectile.cs
git commit -m "refactor(projectile): resolve impact damage against the defender, not at fire time"
```

---

### Task 8: Update `Enemy.cs` — simplify effective-stats resolution (A2/A3), thread the defender through `AttackPlayer`

**Files:**
- Modify: `Assets\Scripts\Enemy\Base\Enemy.cs`

**Interfaces:**
- Consumes: `CombatConfig.ResolveDamage(float, Team, Vector2)` (Task 5), `CombatConfig.WarnMissingOnce()` (Task 5), `PlayerTeamData` (existing global type).
- Produces: `Enemy.effectiveMaxHealth`/`effectiveAttackDamage`/`effectiveMoveSpeed`/`effectiveCoinDrop` now equal the raw prefab-authored values exactly (no ring scaling). `Enemy.AttackPlayer` now resolves damage against the target player's team+position instead of the enemy's own.

- [ ] **Step 1: Simplify `ResolveEffectiveStats`**

Replace:

```csharp
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
        effectiveCoinDrop = Mathf.Max(0, coinsToDrop + tier.coinDropBonus);
    }
```

with:

```csharp
    /// <summary>
    /// Authority-only: capture home and copy this enemy's base stats. Difficulty is no longer
    /// scaled automatically by distance from a map center — it is tuned manually per-color-
    /// prefab via each prefab's EnemyStats asset (and coinsToDrop below).
    /// </summary>
    private void ResolveEffectiveStats()
    {
        Home = transform.position;

        effectiveMaxHealth = stats.maxHealth;
        effectiveAttackDamage = stats.attackDamage;
        effectiveMoveSpeed = stats.moveSpeed;
        effectiveCoinDrop = coinsToDrop;
    }
```

- [ ] **Step 2: Update the `coinsToDrop` tooltip (A3 — no more ring bonus)**

Replace:

```csharp
    [Tooltip("How many coins to drop on death. AUTHORED PER ARCHETYPE, no randomness — pacing " +
             "cannot be tuned against a random drop. Stronger archetypes drop more. The ring's " +
             "coinDropBonus is added on top at spawn.")]
    [SerializeField] private int coinsToDrop = 2;
```

with:

```csharp
    [Tooltip("How many coins to drop on death. AUTHORED PER ARCHETYPE, no randomness — pacing " +
             "cannot be tuned against a random drop. Stronger archetypes drop more.")]
    [SerializeField] private int coinsToDrop = 2;
```

- [ ] **Step 3: Thread the defender (target player) through `AttackPlayer`'s damage resolution**

Replace:

```csharp
        // Calculate damage through the unified pipeline (review item #4).
        int finalDamage = effectiveAttackDamage;
        CombatConfig config = GameSettingsManager.Instance != null
            ? GameSettingsManager.Instance.GetCombatConfig()
            : null;
        if (config != null)
        {
            Team myTeam = teamComponent != null ? teamComponent.Team : Team.None;
            finalDamage = config.ResolveDamage(effectiveAttackDamage, myTeam, transform.position);
        }
```

with:

```csharp
        // Calculate damage through the unified pipeline, keyed by the DEFENDER (the player being
        // attacked): they take more damage the farther they are from their own base.
        int finalDamage = effectiveAttackDamage;
        CombatConfig config = GameSettingsManager.Instance != null
            ? GameSettingsManager.Instance.GetCombatConfig()
            : null;
        if (config != null)
        {
            PlayerTeamData targetTeam = player.GetComponent<PlayerTeamData>();
            Team defenderTeam = targetTeam != null ? targetTeam.Team : Team.None;
            finalDamage = config.ResolveDamage(effectiveAttackDamage, defenderTeam, player.transform.position);
        }
        else
        {
            CombatConfig.WarnMissingOnce();
        }
```

- [ ] **Step 4: Compile-check and confirm the ring types are fully gone**

Run the batch test command. Expected: `Test run completed`, zero compile errors across the whole project now (this was the last file referencing `DifficultyRingConfig`/`ArenaCenter`/`RingTier`).

Run:
```bash
grep -rli "DifficultyRingConfig\|ArenaCenter\|RingTier" Assets/Scripts Assets/Tests
```
Expected: no output at all.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Enemy/Base/Enemy.cs
git commit -m "refactor(enemy): drop ring-scaled stats, resolve AttackPlayer damage against the defender"
```

---

### Task 9: Update `TeamScoreDisplay.cs` — re-derive the HUD zone strip from the new formula

**Files:**
- Modify: `Assets\Scripts\Hud\TeamScoreDisplay.cs`

**Interfaces:**
- Consumes: `TeamManager.GetOwnBaseDistance01(Team, Vector2)` (Task 4), `Game.Hud.Core.TerritoryReadout.Resolve(float, int)` (Task 2, same call shape, new float meaning).

- [ ] **Step 1: Update `LateUpdate` to use the own-base-distance API**

In `Assets\Scripts\Hud\TeamScoreDisplay.cs`, in `LateUpdate`, replace:

```csharp
        int tier = scoreManager.VanguardTier(localTeam);
        float advantage = teams.GetTerritorialAdvantage(localTeam, localPlayer.position);
        TerritoryDisplay next = TerritoryReadout.Resolve(advantage, tier);
```

with:

```csharp
        int tier = scoreManager.VanguardTier(localTeam);
        float ownBaseDistance01 = teams.GetOwnBaseDistance01(localTeam, localPlayer.position);
        TerritoryDisplay next = TerritoryReadout.Resolve(ownBaseDistance01, tier);
```

- [ ] **Step 2: Update `RepaintZone()` text for the flipped semantics**

Replace:

```csharp
    private void RepaintZone()
    {
        Color color;
        string text;

        switch (zone)
        {
            case TerritoryDisplay.Penalised:
                color = zonePenalisedColor;
                text = "ENEMY TERRITORY  −DAMAGE";
                break;
            case TerritoryDisplay.Lifted:
                color = zoneLiftedColor;
                text = "ENEMY TERRITORY  CLEAR";
                break;
            default:
                color = zoneClearColor;
                text = "OWN TERRITORY";
                break;
        }

        if (zoneIcon != null) zoneIcon.color = color;
        if (zoneText != null)
        {
            zoneText.color = color;
            zoneText.text = text;
        }
    }
```

with:

```csharp
    private void RepaintZone()
    {
        Color color;
        string text;

        switch (zone)
        {
            case TerritoryDisplay.Penalised:
                color = zonePenalisedColor;
                text = "EXPOSED  +DAMAGE TAKEN";
                break;
            case TerritoryDisplay.Lifted:
                color = zoneLiftedColor;
                text = "EXPOSED  VANGUARD SHIELDED";
                break;
            default:
                color = zoneClearColor;
                text = "OWN TERRITORY";
                break;
        }

        if (zoneIcon != null) zoneIcon.color = color;
        if (zoneText != null)
        {
            zoneText.color = color;
            zoneText.text = text;
        }
    }
```

- [ ] **Step 3: Update the class doc comment's stale framing**

Replace the doc comment paragraph:

```csharp
/// The zone indicator's displayed state FOLDS IN the unlocked Vanguard tier: a player pushing into
/// enemy turf watches it stop reading as penalised once the team buys the debuff away, and learns
/// the buff from the thing it changes. Kept as TeamScoreDisplay (not renamed) so the component
/// already wired into the Gameplay scene keeps its score-text references.
```

with:

```csharp
/// The zone indicator's displayed state FOLDS IN the unlocked Vanguard tier: a player standing far
/// from their own base watches it stop reading as penalised once the team buys the vulnerability
/// away, and learns the buff from the thing it changes. Kept as TeamScoreDisplay (not renamed) so
/// the component already wired into the Gameplay scene keeps its score-text references.
```

- [ ] **Step 4: Compile-check**

Run the batch test command. Expected: `Test run completed`, no compile errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hud/TeamScoreDisplay.cs
git commit -m "refactor(hud): re-derive Team Power strip zone indicator from own-base distance"
```

---

### Task 10: Author `CombatConfig.asset` and wire it into `Gameplay.unity`

**Files:**
- Create: `Assets\Scripts\ScriptableObjects\CombatConfig.asset`
- Create: `Assets\Scripts\ScriptableObjects\CombatConfig.asset.meta`
- Modify: `Assets\Scenes\Gameplay.unity` (line ~17253)

**Interfaces:**
- Produces: a real `CombatConfig` asset assigned to `GameSettingsManager.combatConfig` in the Gameplay scene, so `GameSettingsManager.Instance.GetCombatConfig()` returns non-null at runtime.

- [ ] **Step 1: Generate a fresh random GUID for the asset's `.meta`**

Run:
```bash
python3 -c "import uuid; print(uuid.uuid4().hex)"
```
(Or any other method that produces a random 32-character lowercase hex string with no dashes.) Save this value — call it `<ASSET_GUID>` below.

- [ ] **Step 2: Create `CombatConfig.asset.meta`**

Write `Assets\Scripts\ScriptableObjects\CombatConfig.asset.meta`:

```
fileFormatVersion: 2
guid: <ASSET_GUID>
```

- [ ] **Step 3: Create `CombatConfig.asset`**

Write `Assets\Scripts\ScriptableObjects\CombatConfig.asset` with field defaults matching the `CombatConfig.cs` C# defaults exactly (the script GUID `fff48ee354ebcf244b97e4024cf8e1b4` is `CombatConfig.cs`'s own `.meta` GUID, unrelated to `<ASSET_GUID>`):

```yaml
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: fff48ee354ebcf244b97e4024cf8e1b4, type: 3}
  m_Name: CombatConfig
  m_EditorClassIdentifier: Assembly-CSharp::CombatConfig
  globalDamageMultiplier: 1
  knockbackMultiplier: 1
  scaledKnockback: 1
  attackSpeedMultiplier: 1
  territorialAdvantageEnabled: 1
  damageNumberPrefab: {fileID: 0}
  normalDamageColor: {r: 1, g: 1, b: 1, a: 1}
  bonusDamageColor: {r: 1, g: 0.92156863, b: 0.015686275, a: 1}
  hitEffectPrefab: {fileID: 0}
  hitSound: {fileID: 0}
  hitSoundVolume: 0.5
```

- [ ] **Step 4: Wire the asset into `Gameplay.unity`**

In `Assets\Scenes\Gameplay.unity`, find the `GameSettingsManager` MonoBehaviour block (object `&1526479891`, around line 17253):

```yaml
  combatConfig: {fileID: 0}
```

Replace with:

```yaml
  combatConfig: {fileID: 11400000, guid: <ASSET_GUID>, type: 2}
```

(using the same `<ASSET_GUID>` from Step 1/2). Leave the orphaned `difficultyRingConfig: {fileID: 0}` line on the line below it alone — it self-heals to nothing the next time Unity saves the scene, per Task 3's note.

- [ ] **Step 5: Grep-verify the wiring**

Run:
```bash
grep -n "combatConfig" Assets/Scenes/Gameplay.unity
```
Expected: one line, `combatConfig: {fileID: 11400000, guid: <the actual GUID you generated>, type: 2}` — NOT `{fileID: 0}`.

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/ScriptableObjects/CombatConfig.asset" "Assets/Scripts/ScriptableObjects/CombatConfig.asset.meta" Assets/Scenes/Gameplay.unity
git commit -m "feat(combat): author CombatConfig.asset and wire it into Gameplay.unity"
```

---

### Task 11: Full verification — compile, tests, evidence, in-editor play check, report

Use the `superpowers:verification-before-completion` skill for this task.

**Files:** none (verification only).

- [ ] **Step 1: Full editor-closed compile + test run**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log
```
Read `r.log` and confirm `Test run completed` appears. Read `r.xml` and record the exact pass/fail/total counts. Do NOT trust the shell exit code alone. Baseline before this plan was 386/386 with 0 compile errors; this plan removes `DifficultyRingConfigTests.cs` (7 tests) and rewrites `TerritorialCombatTests.cs` (27 → 20 tests) and `TerritoryReadoutTests.cs` (4 → 5 tests), so the expected new total is approximately 386 − 7 − 7 + 1 = **373**, all green — but report the ACTUAL number from `r.xml`, not this estimate.

- [ ] **Step 2: Prove `combatConfig` is really referenced**

Run:
```bash
grep -n "combatConfig\|difficultyRingConfig" Assets/Scenes/Gameplay.unity
```
Confirm `combatConfig` shows `type: 2` and a real, non-zero GUID (not `{fileID: 0}`).

- [ ] **Step 3: Prove crit is fully gone**

Run:
```bash
git diff main -- Assets/Scripts/ScriptableObjects/CombatConfig.cs | grep -i "crit"
```
Confirm no `crit`-related additions remain (only removals should show, or nothing at all if `main` didn't have it either — this should show only `-` lines for the deleted crit code). Then run:
```bash
grep -in "crit" Assets/Scripts/ScriptableObjects/CombatConfig.cs
```
Expected: no output.

- [ ] **Step 4: Prove the ring system is fully gone**

Run:
```bash
ls Assets/Scripts/Enemy/AI/DifficultyRingConfig.cs Assets/Scripts/Enemy/Base/ArenaCenter.cs 2>&1
```
Expected: both report "No such file or directory". Then:
```bash
grep -rli "DifficultyRingConfig\|ArenaCenter\|RingTier" Assets/Scripts Assets/Tests
```
Expected: no output.

- [ ] **Step 5: In-editor play verification — own-base vulnerability and Vanguard, evidence required**

Open the project in the Unity Editor (this step needs the editor OPEN, unlike Steps 1–4 which need it closed) and enter Play mode in `Gameplay.unity`. State explicitly which environment is used: prefer `singlePlayerMode=true` for the base vulnerability check (simplest, no MPPM identity footgun — MPPM peers share one `PlayerPrefs` identity and cannot be used for this). Confirm:
  a. Standing at/near your own team's base, take a hit from an enemy (or, in a 2-peer test, from an opposing player) and note the damage number.
  b. Move far toward the enemy base and take the same kind of hit; the damage number should be measurably higher (roughly proportional to distance, capped around 2.5x the base-standing value at tier 0 Vanguard).
  c. Deposit coins for your team (or otherwise drive `TeamScoreManager`'s per-player average past the Vanguard thresholds) to reach tier 1, then tier 2, and repeat the far-from-base hit at each tier — damage should measurably decrease as tier rises, reaching parity with the near-base hit at tier 2.
  d. Confirm an enemy taking a hit does NOT get more expensive to kill based on its position (A2/A3: no ring scaling) — its effective health/damage should match its `EnemyStats` asset values exactly regardless of where it spawned.

Record the actual damage numbers observed at each step (not just "it worked") in the final report.

- [ ] **Step 6: Report**

Write a verification report covering: exact test counts (Step 1), the `combatConfig` scene grep output (Step 2), the crit grep output (Step 3), the ring-deletion grep output (Step 4), and the in-editor damage numbers with the environment used (Step 5). If any step could not be completed (e.g. no Unity Editor GUI access in this session for Step 5), say so explicitly — do not claim success without the evidence.

- [ ] **Step 7: Request code review**

Use the `superpowers:requesting-code-review` skill before merging.
