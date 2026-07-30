# Territorial Combat Rework + Vanguard Team Buff — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the invisible, untuned 9× two-sided territorial damage swing with a single legible 3× debuff on damage *dealt* in the enemy third, and replace the two hollow team booleans with one derived, tiered team buff (Vanguard) that lifts that debuff in halves.

**Architecture:** All new decision logic lands in two engine-free pure static classes so it is unit-testable outside Unity: `Game.Combat.Core.TerritorialCombat` (zone classification + Vanguard-adjusted multiplier) and `Game.Buffs.Core.TeamBuffUnlock` (per-player-average → tier, delegating to the existing `BuffUnlock`). The Fusion layer only wires them: `TeamManager` quantizes its existing advantage output, `TeamScoreManager` holds team score + a once-captured networked roster size and *derives* the Vanguard tier on query, and `CombatConfig.ResolveDamage` stays the single damage entry point with its received-side deleted.

**Tech Stack:** Unity 6.3 (6000.3.0f1), Photon Fusion 2 (Host/Client + dedicated server), C#, NUnit EditMode tests, TextMeshPro (HUD badge only).

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md` — sections "Territorial combat — one debuff, 3× swing", "Team buff catalog — one buff, two tiers", "Team thresholds are per-player-average", "Team curve", and audit findings 2, 3, 4.
- `CombatConfig.ResolveDamage` is **THE** single entry point for all combat damage. `PlayerCombat` and `Enemy` both route through it. Keep it that way.
- Replicated state is `[Networked]` with `OnChangedRender` for render reactions. Clients and late joiners must derive correct state from networked state, never from a missed RPC.
- All authoritative writes (including the roster capture) happen **only** under `HasStateAuthority`, inside `FixedUpdateNetwork`. Simulation-path timing via `TickTimer` only — no `Time.time`, no coroutines.
- `Runner.Spawn` / `Runner.Despawn` only under `HasStateAuthority` (no new spawns in this work).
- `TeamScoreManager` must stay **always-interested** under interest management, or the team layer and its HUD vanish for distant players in a 20-player match.
- Tiers are **derived on query**, never stored as independent networked state. The only new networked state is the frozen roster size. Preserve resimulation safety: no monotonic latches, no per-tier fields.
- Pure, unit-tested logic lives in engine-free asmdefs (`Game.Combat.Core`, `Game.Buffs.Core` — both have `noEngineReferences: true`, so **no `UnityEngine.Mathf`** in them). Fusion `NetworkBehaviour`s stay in `Assembly-CSharp` (no asmdef).
- `Team` enum: `None=0, Team1=1, Team2=2, Team3AI=3`.
- **Pre-existing cheat surface, do NOT fix here:** `TeamScoreManager.RPC_AddPoints` is `RpcSources.All`, so any client can inflate team score. It wants its own security pass. Do not make it worse.
- **Out of scope — do not touch:** the individual buff catalog (Scope 3), coin drop rates and HUD surfaces beyond keeping `TeamScoreDisplay` compiling and correct (Scope 4), match phases (Scope 1), and **any respawn timing** — in particular do NOT collapse the dead `GameSettingsManager.GetRespawnTime` / `respawnTimeMultiplier` / `TeamData.respawnDelay` path.

### Numbers, verbatim from the spec

| Thing | Value |
|---|---|
| Enemy-third boundary | territorial advantage `< -0.33` is the enemy third; `>= -0.33` is clear |
| Clear zone, damage dealt | ×1.00 |
| Enemy third, Vanguard locked | ×0.33 (total swing 3×) |
| Vanguard formula | `dealt = 1 - 0.67 * (1 - 0.5 * tier)` → **0.33 / 0.665 / 1.00** at tiers 0/1/2 (the spec's "0.67" is 0.665 rounded for display) |
| Vanguard thresholds | `{12, 45}` **per-player-average** deposited value, compared against `teamScore / rosterSize` |
| Vanguard max tier | 2 (one team buff, so `buffCount == 1`, one threshold per tier) |

**The single most misreadable number in this design:** 12 and 45 are per-player averages, *not* absolute team scores. On a 10-player team they correspond to absolute team scores of 120 and 450. If you ever find yourself comparing a raw threshold to `Team1Score` / `Team2Score`, you have got it wrong.

### How to run tests

EditMode tests run in Unity: **Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All**.

If the editor holds the project lock (`Unity.exe -batchmode -runTests` then fails), use the bundled-Roslyn workaround: compile the engine-free core `.cs` plus a hand-written assert harness against `netstandard 2.1` with
`C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Data\DotNetSdkRoslyn\csc.dll`, write a `net8.0` `runtimeconfig.json` beside the exe, and run it on
`C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Data\NetCoreRuntime\dotnet.exe`.
For the whole-surface compile gate, build a `@response.rsp` for `csc.dll` referencing the netstandard ref, `Editor\Data\Managed\UnityEngine\*.dll`, `Assets\Photon\Fusion\Assemblies\*.dll`, and `Library\ScriptAssemblies\*.dll` (skip `*Editor*` / `*CodeGen*` / `*Tests*`), compiling every `Assets/Scripts/**/*.cs` **except** the asmdef-owned folders (`Buffs/Core`, `Combat/Core`, `Enemy/AI`, `Hud/Core`, `Net`, `Player/Animation/Core`). Quote every path inside the `.rsp` ("Program Files" has a space). When a core assembly changed in this branch, drop its stale `Library\ScriptAssemblies\Game.*.Core.dll` from the references and compile that folder's `.cs` inline instead.

**A clean compile is not verification.** Report separately what was executed and what was only compiled.

---

## File Structure

**Created:**
- `Assets/Scripts/Combat/Core/TerritorialCombat.cs` — pure zone classification + Vanguard-adjusted dealt multiplier (`namespace Game.Combat.Core`, engine-free).
- `Assets/Scripts/Combat/Core/TerritorialCombat.cs.meta`
- `Assets/Tests/EditMode/Combat/TerritorialCombatTests.cs` (+ `.meta`) — NUnit, in the existing `Game.Combat.Tests` asmdef.
- `Assets/Scripts/Buffs/Core/TeamBuffUnlock.cs` — pure per-player-average → team tier, delegating to `BuffUnlock` (`namespace Game.Buffs.Core`, engine-free).
- `Assets/Scripts/Buffs/Core/TeamBuffUnlock.cs.meta`
- `Assets/Tests/EditMode/TeamBuffUnlockTests.cs` (+ `.meta`) — NUnit, in the existing `Game.Buffs.EditModeTests` asmdef.

**Modified:**
- `Assets/Scripts/Coin Scripts/TeamScoreManager.cs` — delete the four buff bools, both thresholds, `CheckMilestones`, both `UnityEvent`s, and `HasDamageBuff`/`HasDefenseBuff`; add authored Vanguard thresholds, the once-captured networked roster sizes, and the derived `VanguardTier(Team)`.
- `Assets/Scripts/Teams/TeamManager.cs` — `GetDamageDealtModifier` takes a Vanguard tier and returns the quantized multiplier; `GetDamageReceivedModifier` and the `minDamageMultiplier`/`maxDamageMultiplier` lerp endpoints are deleted. `GetTerritorialAdvantage` is untouched.
- `Assets/Scripts/ScriptableObjects/CombatConfig.cs` — `CalculateFinalDamage` loses `receivedModifier`; `ResolveDamage` loses the defender parameters and gathers the attacker's Vanguard tier; a one-time warning fires if `territorialAdvantageEnabled` is false.
- `Assets/Scripts/Player/PlayerCombat.cs` — `ResolveMeleeDamage` drops its now-dead defender lookup and parameters; two call sites updated.
- `Assets/Scripts/Enemy/Base/Enemy.cs` — drops its now-dead defender-team lookup at the `ResolveDamage` call.
- `Assets/Scripts/Hud/TeamScoreDisplay.cs` — badge reads `VanguardTier(localTeam) > 0` instead of the deleted bools. (Scope 4 replaces this surface entirely; this is the minimum to keep it correct.)

**Deliberately NOT modified:** `Assets/Scenes/Gameplay.unity`. The dead `damageBuffThreshold` / `defenseBuffThreshold` / `_Team*Buff` / `onDamageBuffUnlocked` / `onDefenseBuffUnlocked` YAML keys on the `TeamScoreManager` component (lines ~639-650) are harmless — Unity ignores serialized keys with no matching field and drops them the next time the scene is saved. New serialized fields absent from YAML keep their C# field-initializer values (`{12, 45}`, `2`). Hand-editing scene YAML risks the user's scene for zero behavioural gain. Task 6 has the user confirm the inspector values instead.

---

## Task 1: Pure territorial math (`TerritorialCombat`)

**Files:**
- Create: `Assets/Scripts/Combat/Core/TerritorialCombat.cs`
- Create: `Assets/Scripts/Combat/Core/TerritorialCombat.cs.meta`
- Test: `Assets/Tests/EditMode/Combat/TerritorialCombatTests.cs` (+ `.meta`)

**Interfaces:**
- Consumes: nothing (engine-free leaf).
- Produces:
  - `Game.Combat.Core.TerritorialCombat.EnemyThirdBoundary` → `const float` = `-0.33f`
  - `TerritorialCombat.FullDebuff` → `const float` = `0.33f`
  - `TerritorialCombat.VanguardMaxTier` → `const int` = `2`
  - `static bool InEnemyThird(float territorialAdvantage)`
  - `static float DebuffWithVanguard(int vanguardTier)`
  - `static float DealtMultiplier(float territorialAdvantage, int vanguardTier)`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/Combat/TerritorialCombatTests.cs`:

```csharp
using NUnit.Framework;
using Game.Combat.Core;

public class TerritorialCombatTests
{
    // The boundary is the enemy THIRD, not the midline: advantage is +1 at own base,
    // -1 at the enemy base, 0 at the midpoint.
    [TestCase(1.0f, false)]    // own base
    [TestCase(0.0f, false)]    // midfield is clean and neutral
    [TestCase(-0.32f, false)]  // just outside the enemy third
    [TestCase(-0.33f, false)]  // exactly on the boundary: NOT debuffed (>= -0.33 is clear)
    [TestCase(-0.34f, true)]   // just inside the enemy third
    [TestCase(-1.0f, true)]    // enemy base
    public void InEnemyThird_SplitsAtMinusOneThird(float advantage, bool expected)
    {
        Assert.AreEqual(expected, TerritorialCombat.InEnemyThird(advantage));
    }

    // dealt = 1 - 0.67 * (1 - 0.5 * tier)  =>  0.33 / 0.665 / 1.00
    [TestCase(0, 0.33f)]
    [TestCase(1, 0.665f)]
    [TestCase(2, 1.0f)]
    public void DebuffWithVanguard_LiftsTheDebuffInHalves(int tier, float expected)
    {
        Assert.AreEqual(expected, TerritorialCombat.DebuffWithVanguard(tier), 1e-4f);
    }

    [TestCase(-1)]
    [TestCase(3)]
    [TestCase(99)]
    public void DebuffWithVanguard_ClampsTierOutOfRange(int tier)
    {
        float value = TerritorialCombat.DebuffWithVanguard(tier);
        Assert.GreaterOrEqual(value, 0.33f - 1e-4f);
        Assert.LessOrEqual(value, 1.0f + 1e-4f);
    }

    // Outside the enemy third nothing is applied, at any tier: the debuff is one-sided.
    [TestCase(0.5f, 0, 1.0f)]
    [TestCase(0.5f, 2, 1.0f)]
    [TestCase(-0.33f, 0, 1.0f)]
    [TestCase(-0.5f, 0, 0.33f)]
    [TestCase(-0.5f, 1, 0.665f)]
    [TestCase(-0.5f, 2, 1.0f)]
    public void DealtMultiplier_AppliesOnlyInsideTheEnemyThird(float advantage, int tier, float expected)
    {
        Assert.AreEqual(expected, TerritorialCombat.DealtMultiplier(advantage, tier), 1e-4f);
    }

    // The whole point of quantizing: the total swing is exactly 3x, not the old 9x.
    [Test]
    public void FullSwingIsThreeTimes()
    {
        float clear = TerritorialCombat.DealtMultiplier(0f, 0);
        float debuffed = TerritorialCombat.DealtMultiplier(-1f, 0);
        Assert.AreEqual(3.0f, clear / debuffed, 1e-2f);
    }
}
```

Create `Assets/Tests/EditMode/Combat/TerritorialCombatTests.cs.meta` (Unity only generates `.meta` on editor focus; write it by hand so the file is not orphaned — use a fresh random 32-hex GUID, do not reuse this one verbatim if it collides):

```yaml
fileFormatVersion: 2
guid: 7c1d4a9e5b3f4e2ab8d6c0f1a2e3d4b5
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

- [ ] **Step 2: Run the test to verify it fails**

Run: Test Runner ▸ EditMode ▸ Run All (or the Roslyn harness from "How to run tests").
Expected: FAIL / compile error — `TerritorialCombat` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Combat/Core/TerritorialCombat.cs`:

```csharp
namespace Game.Combat.Core
{
    /// <summary>
    /// Pure territorial-combat math: ONE debuff, on ONE side (damage dealt), in ONE direction.
    /// Replaces the old lerped two-sided model whose modifiers compounded to a 9x swing
    /// (dealt 1.5 x received 1.5 at own base vs 0.5 x 0.5 at the enemy base) that was invisible
    /// to players and never tuned. Two discrete states, not a gradient — that is what makes it
    /// displayable as an icon.
    /// Engine-free (this asmdef sets noEngineReferences) so it is testable outside Unity.
    /// See docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md.
    /// </summary>
    public static class TerritorialCombat
    {
        /// <summary>
        /// Territorial advantage strictly below this is the enemy third. Advantage is +1 at your own
        /// base, -1 at the enemy base, 0 at the midpoint (TeamManager.GetTerritorialAdvantage).
        /// The boundary is the enemy THIRD, not the midline, so midfield fighting stays neutral and
        /// only committing deep — where the enemy flag sits — carries the tax.
        /// </summary>
        public const float EnemyThirdBoundary = -0.33f;

        /// <summary>Damage dealt multiplier inside the enemy third with Vanguard locked. Total swing 3x.</summary>
        public const float FullDebuff = 0.33f;

        /// <summary>Vanguard's top tier. Each tier removes half of the debuff.</summary>
        public const int VanguardMaxTier = 2;

        /// <summary>True when the attacker is deep enough in enemy territory to take the debuff.</summary>
        public static bool InEnemyThird(float territorialAdvantage)
        {
            return territorialAdvantage < EnemyThirdBoundary;
        }

        /// <summary>
        /// Debuff strength after the team's Vanguard tier: 1 - 0.67 * (1 - 0.5 * tier),
        /// giving even thirds 0.33 -> 0.665 -> 1.00 across tiers 0/1/2.
        /// </summary>
        public static float DebuffWithVanguard(int vanguardTier)
        {
            int tier = vanguardTier < 0 ? 0 : (vanguardTier > VanguardMaxTier ? VanguardMaxTier : vanguardTier);
            return 1f - (1f - FullDebuff) * (1f - 0.5f * tier);
        }

        /// <summary>Final damage-dealt multiplier for an attacker at the given advantage.</summary>
        public static float DealtMultiplier(float territorialAdvantage, int vanguardTier)
        {
            return InEnemyThird(territorialAdvantage) ? DebuffWithVanguard(vanguardTier) : 1f;
        }
    }
}
```

Create `Assets/Scripts/Combat/Core/TerritorialCombat.cs.meta` with the same `MonoImporter` template as Step 1 and a different fresh 32-hex GUID.

- [ ] **Step 4: Run the tests to verify they pass**

Run: Test Runner ▸ EditMode ▸ Run All (or the Roslyn harness).
Expected: PASS — all `TerritorialCombatTests` cases green.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Combat/Core/TerritorialCombat.cs" "Assets/Scripts/Combat/Core/TerritorialCombat.cs.meta" "Assets/Tests/EditMode/Combat/TerritorialCombatTests.cs" "Assets/Tests/EditMode/Combat/TerritorialCombatTests.cs.meta" && git commit -m "feat(combat): quantize territory to one 3x debuff on damage dealt"
```

---

## Task 2: Pure team-tier math (`TeamBuffUnlock`)

**Files:**
- Create: `Assets/Scripts/Buffs/Core/TeamBuffUnlock.cs` (+ `.meta`)
- Test: `Assets/Tests/EditMode/TeamBuffUnlockTests.cs` (+ `.meta`)

**Interfaces:**
- Consumes: `Game.Buffs.Core.BuffUnlock.UnlockedSteps(IReadOnlyList<int>, int)` and `BuffUnlock.TierLevel(int unlockedSteps, int priorityPosition, int buffCount, int maxTier)` — both already exist, unchanged.
- Produces:
  - `static int Game.Buffs.Core.TeamBuffUnlock.PerPlayerAverage(int teamScore, int rosterSize)`
  - `static int TeamBuffUnlock.TeamTier(IReadOnlyList<int> thresholds, int teamScore, int rosterSize, int maxTier)`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/TeamBuffUnlockTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Game.Buffs.Core;

public class TeamBuffUnlockTests
{
    // PER-PLAYER-AVERAGE deposited value, not absolute team score.
    private static readonly List<int> Vanguard = new List<int> { 12, 45 };
    private const int MaxTier = 2;

    [TestCase(0, 10, 0)]
    [TestCase(119, 10, 11)]
    [TestCase(120, 10, 12)]
    [TestCase(55, 1, 55)]
    [TestCase(7, 2, 3)]     // integer floor, deterministic across peers
    [TestCase(100, 0, 0)]   // empty roster: no divide, no tier
    [TestCase(-5, 10, 0)]   // defensive: negative score never reads as progress
    public void PerPlayerAverage_FloorsAndGuardsEmptyRosters(int score, int roster, int expected)
    {
        Assert.AreEqual(expected, TeamBuffUnlock.PerPlayerAverage(score, roster));
    }

    // Solo player: the thresholds are literally the per-player numbers.
    [TestCase(0, 1, 0)]
    [TestCase(11, 1, 0)]
    [TestCase(12, 1, 1)]    // exact boundary unlocks
    [TestCase(44, 1, 1)]
    [TestCase(45, 1, 2)]    // exact boundary unlocks
    [TestCase(9999, 1, 2)]  // hard-capped at MaxTier
    public void TeamTier_SoloRoster(int score, int roster, int expected)
    {
        Assert.AreEqual(expected, TeamBuffUnlock.TeamTier(Vanguard, score, roster, MaxTier));
    }

    // 10-player team: 12 and 45 correspond to ABSOLUTE team scores of 120 and 450.
    // If these fail with tier 1/2 at scores like 12 and 45, the divisor was dropped.
    [TestCase(119, 10, 0)]
    [TestCase(120, 10, 1)]
    [TestCase(449, 10, 1)]
    [TestCase(450, 10, 2)]
    [TestCase(12, 10, 0)]   // the old failure mode: unlocked within seconds
    [TestCase(45, 10, 0)]
    public void TeamTier_TenPlayerRosterNormalises(int score, int roster, int expected)
    {
        Assert.AreEqual(expected, TeamBuffUnlock.TeamTier(Vanguard, score, roster, MaxTier));
    }

    [Test]
    public void TeamTier_EmptyRosterIsLocked()
    {
        Assert.AreEqual(0, TeamBuffUnlock.TeamTier(Vanguard, 1000, 0, MaxTier));
    }

    [Test]
    public void TeamTier_NullThresholdsIsLocked()
    {
        Assert.AreEqual(0, TeamBuffUnlock.TeamTier(null, 1000, 10, MaxTier));
    }

    // Expected pacing from the spec: typical play is around 55 per player, so a normal team
    // fully lifts the debuff around mid-match.
    [Test]
    public void TypicalTeamReachesTierTwo()
    {
        Assert.AreEqual(2, TeamBuffUnlock.TeamTier(Vanguard, 55 * 10, 10, MaxTier));
    }
}
```

Create `Assets/Tests/EditMode/TeamBuffUnlockTests.cs.meta` using the `MonoImporter` template from Task 1 Step 1 with a fresh 32-hex GUID.

- [ ] **Step 2: Run the test to verify it fails**

Run: Test Runner ▸ EditMode ▸ Run All (or the Roslyn harness).
Expected: FAIL / compile error — `TeamBuffUnlock` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Buffs/Core/TeamBuffUnlock.cs`:

```csharp
using System.Collections.Generic;

namespace Game.Buffs.Core
{
    /// <summary>
    /// Team-side unlock math, sharing the individual layer's vocabulary:
    /// cumulative deposited value -> ordered unlock steps -> tiers, via the same BuffUnlock helper
    /// (buffCount == 1, because the team catalog holds exactly one buff and has no ordering to pick).
    ///
    /// Team score is the sum of a whole roster's deposits, so a raw threshold is meaningless across
    /// roster sizes. Thresholds are therefore authored as PER-PLAYER-AVERAGE deposited value and
    /// compared against teamScore / rosterSize. On a 10-player team, {12, 45} means absolute team
    /// scores of 120 and 450.
    /// Engine-free (this asmdef sets noEngineReferences) so it is testable outside Unity.
    /// </summary>
    public static class TeamBuffUnlock
    {
        /// <summary>
        /// Floor of the per-player average deposited value; 0 for an empty roster or a
        /// non-positive score. Integer division keeps derivation deterministic across peers.
        /// </summary>
        public static int PerPlayerAverage(int teamScore, int rosterSize)
        {
            if (rosterSize <= 0 || teamScore <= 0) return 0;
            return teamScore / rosterSize;
        }

        /// <summary>
        /// Tier (0 = locked, up to maxTier) of the single team buff. Pure: same inputs, same tier,
        /// which is what keeps the team layer resimulation-safe with no stored tier state.
        /// </summary>
        public static int TeamTier(IReadOnlyList<int> thresholds, int teamScore, int rosterSize, int maxTier)
        {
            int average = PerPlayerAverage(teamScore, rosterSize);
            int steps = BuffUnlock.UnlockedSteps(thresholds, average);
            return BuffUnlock.TierLevel(steps, priorityPosition: 0, buffCount: 1, maxTier: maxTier);
        }
    }
}
```

Create `Assets/Scripts/Buffs/Core/TeamBuffUnlock.cs.meta` with the `MonoImporter` template and a fresh 32-hex GUID.

- [ ] **Step 4: Run the tests to verify they pass**

Run: Test Runner ▸ EditMode ▸ Run All (or the Roslyn harness).
Expected: PASS — all `TeamBuffUnlockTests` cases green, and the existing `BuffUnlockTests` still green (that helper is unchanged).

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Buffs/Core/TeamBuffUnlock.cs" "Assets/Scripts/Buffs/Core/TeamBuffUnlock.cs.meta" "Assets/Tests/EditMode/TeamBuffUnlockTests.cs" "Assets/Tests/EditMode/TeamBuffUnlockTests.cs.meta" && git commit -m "feat(buffs): derive team tiers from per-player-average deposits"
```

---

## Task 3: `TeamScoreManager` — delete the booleans, derive Vanguard

**Files:**
- Modify: `Assets/Scripts/Coin Scripts/TeamScoreManager.cs` (whole file rewritten below)

**Interfaces:**
- Consumes: `Game.Buffs.Core.TeamBuffUnlock.TeamTier(...)` (Task 2); `MatchManager.Instance.Phase` / `MatchPhase.Live` (existing); `PlayerTeamData.Team` (existing).
- Produces:
  - `int TeamScoreManager.VanguardTier(Team team)` — 0..2, derived, never stored.
  - `byte TeamScoreManager.Team1RosterSize` / `Team2RosterSize` — `[Networked]`, frozen at Live.
  - Existing events `ScoresChanged` and `TeamBuffsChanged` are kept; **`TeamBuffsChanged` now also fires on score changes**, because the Vanguard tier derives from score as well as roster.
  - **Removed:** `HasDamageBuff`, `HasDefenseBuff`, `Team1DamageBuff`, `Team2DamageBuff`, `Team1DefenseBuff`, `Team2DefenseBuff`, `damageBuffThreshold`, `defenseBuffThreshold`, `CheckMilestones`, `onDamageBuffUnlocked`, `onDefenseBuffUnlocked`.

- [ ] **Step 1: Write the whole new file**

Replace the entire contents of `Assets/Scripts/Coin Scripts/TeamScoreManager.cs`:

```csharp
using UnityEngine;
using Fusion;
using Game.Buffs.Core;

/// <summary>
/// Singleton manager that tracks team scores and derives the one team buff, Vanguard.
/// META-LAYER MODEL: CTF capture is the only win condition; coin deposits buy Vanguard, which
/// lifts the territorial debuff on damage dealt in the enemy third (CombatConfig.ResolveDamage).
/// Tiers are DERIVED on query from networked score + a once-frozen roster size — never stored —
/// so there is nothing to replay on resimulation and no monotonic latch to maintain.
/// Place one on an empty GameObject in the Gameplay scene. PHOTON FUSION networked; must be
/// always-interested under interest management.
/// See docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md.
/// </summary>
public class TeamScoreManager : NetworkBehaviour
{
    [Header("Score Tracking")]
    [Networked, OnChangedRender(nameof(OnScoresChanged))] public int Team1Score { get; set; }
    [Networked, OnChangedRender(nameof(OnScoresChanged))] public int Team2Score { get; set; }

    [Header("Vanguard (the entire team buff catalog)")]
    [Tooltip("PER-PLAYER-AVERAGE deposited value per Vanguard tier. Compared against " +
             "teamScore / roster size — NOT against the raw team score. On a 10-player team, " +
             "{12, 45} means absolute team scores of 120 and 450.")]
    [SerializeField] private int[] vanguardThresholds = { 12, 45 };

    [Tooltip("Vanguard's top tier. One team buff means one threshold per tier, so this must " +
             "equal vanguardThresholds.Length.")]
    [SerializeField] private int vanguardMaxTier = 2;

    [Header("Roster (frozen once, on entering Live)")]
    [Networked, OnChangedRender(nameof(OnTeamBuffsChanged))] public byte Team1RosterSize { get; set; }
    [Networked, OnChangedRender(nameof(OnTeamBuffsChanged))] public byte Team2RosterSize { get; set; }
    [Networked] private NetworkBool RosterCaptured { get; set; }

    /// <summary>Fires when Team1Score / Team2Score change. HUD subscribes.</summary>
    public event System.Action ScoresChanged;

    /// <summary>
    /// Fires whenever the Vanguard tier could have moved — i.e. on score changes AND on the
    /// roster capture, since the tier derives from both. HUD subscribes.
    /// </summary>
    public event System.Action TeamBuffsChanged;

    private void OnScoresChanged()
    {
        ScoresChanged?.Invoke();
        TeamBuffsChanged?.Invoke();
    }

    private void OnTeamBuffsChanged() => TeamBuffsChanged?.Invoke();

    // Singleton instance
    private static TeamScoreManager instance;

    public static TeamScoreManager Instance => instance;

    private void Awake()
    {
        // Ensure only one instance exists
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            // Never Destroy() a spawned NetworkObject locally — that desyncs Fusion's
            // object table on this peer. Disable the duplicate and leave it inert.
            Debug.LogWarning("Multiple TeamScoreManagers detected! Disabling duplicate.");
            enabled = false;
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    public override void Spawned()
    {
        // Fail loudly on a mis-authored catalog rather than silently locking or over-unlocking a tier.
        int authored = vanguardThresholds != null ? vanguardThresholds.Length : 0;
        if (authored != vanguardMaxTier)
        {
            Debug.LogError($"❌ TeamScoreManager: vanguardThresholds has {authored} entries but " +
                           $"vanguardMaxTier is {vanguardMaxTier}. One team buff needs exactly one " +
                           $"threshold per tier.");
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || RosterCaptured) return;

        // MatchManager owns the phase. If it is absent (a scene with no match loop), capture at once
        // so the team layer is never dead just because the phase machine is missing.
        MatchManager match = MatchManager.Instance;
        if (match != null && match.Phase != MatchPhase.Live) return;

        CaptureRosterSizes();
    }

    /// <summary>
    /// SERVER: freeze each team's head-count once, on entering Live, and use it as the divisor for
    /// the rest of the match. This is what keeps tier derivation pure: roster churn afterwards
    /// cannot retroactively unlock or revoke Vanguard, so no stored tier state is needed.
    /// </summary>
    private void CaptureRosterSizes()
    {
        int t1 = 0;
        int t2 = 0;

        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            if (!Runner.TryGetPlayerObject(player, out NetworkObject playerObject) || playerObject == null)
                continue;

            PlayerTeamData team = playerObject.GetComponent<PlayerTeamData>();
            if (team == null) continue;

            if (team.Team == Team.Team1) t1++;
            else if (team.Team == Team.Team2) t2++;
        }

        Team1RosterSize = (byte)Mathf.Clamp(t1, 0, 255);
        Team2RosterSize = (byte)Mathf.Clamp(t2, 0, 255);
        RosterCaptured = true;
    }

    /// <summary>
    /// Adds points to a team's score.
    /// Handles multiple team naming conventions: Team1/Blue and Team2/Red
    /// RPC so any client can request adding points, but only server executes
    /// </summary>
    /// <param name="team">The team receiving points</param>
    /// <param name="points">Number of points to add</param>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_AddPoints(string team, int points)
    {
        // RpcTargets.StateAuthority means this body only runs on the server.
        if (!HasStateAuthority) return;

        Team scoring = TeamUtil.Normalize(team);

        if (scoring == Team.Team1)
        {
            Team1Score += points;
        }
        else if (scoring == Team.Team2)
        {
            Team2Score += points;
        }
        else
        {
            Debug.LogError($"[SERVER] Unrecognized team: '{team}'. Expected Team1, Team2, Blue, or Red.");
        }
    }

    /// <summary>
    /// Local version for backward compatibility - calls RPC
    /// </summary>
    public void AddPoints(string team, int points)
    {
        RPC_AddPoints(team, points);
    }

    /// <summary>
    /// Current Vanguard tier (0 = locked, 1 = half the territorial debuff removed, 2 = all of it).
    /// Derived on query from networked state — never stored.
    /// </summary>
    public int VanguardTier(Team team)
    {
        int score;
        int roster;

        if (team == Team.Team1)
        {
            score = Team1Score;
            roster = Team1RosterSize;
        }
        else if (team == Team.Team2)
        {
            score = Team2Score;
            roster = Team2RosterSize;
        }
        else
        {
            return 0; // Team3AI and None have no economy.
        }

        return TeamBuffUnlock.TeamTier(vanguardThresholds, score, roster, vanguardMaxTier);
    }
}
```

- [ ] **Step 2: Verify nothing still references the deleted members**

Run:

```bash
git grep -n "HasDamageBuff\|HasDefenseBuff\|Team1DamageBuff\|Team2DamageBuff\|Team1DefenseBuff\|Team2DefenseBuff\|CheckMilestones\|onDamageBuffUnlocked\|onDefenseBuffUnlocked\|damageBuffThreshold\|defenseBuffThreshold" -- "Assets/Scripts"
```

Expected: exactly two remaining hits, both in files Task 4 and Task 5 fix — `Assets/Scripts/ScriptableObjects/CombatConfig.cs` (`HasDamageBuff` / `HasDefenseBuff`) and `Assets/Scripts/Hud/TeamScoreDisplay.cs` (same two). Any other hit means something else was wired to the booleans and must be handled before continuing.

- [ ] **Step 3: Commit**

Compilation is still broken at this point (the two known call sites), so this commit is deliberately paired with Task 4. Commit anyway to keep the diff readable — do not run the compile gate yet.

```bash
git add "Assets/Scripts/Coin Scripts/TeamScoreManager.cs" && git commit -m "feat(teams): replace team buff booleans with derived Vanguard tier"
```

---

## Task 4: Wire the debuff through `TeamManager` and `CombatConfig`

**Files:**
- Modify: `Assets/Scripts/Teams/TeamManager.cs:12-17` (delete the lerp endpoints), `:55-69` (rewrite `GetDamageDealtModifier`, delete `GetDamageReceivedModifier`)
- Modify: `Assets/Scripts/ScriptableObjects/CombatConfig.cs:1-8` (header comment), `:76-128` (both damage methods)
- Modify: `Assets/Scripts/Player/PlayerCombat.cs:242`, `:260`, `:294-316`
- Modify: `Assets/Scripts/Enemy/Base/Enemy.cs:286-292`

**Interfaces:**
- Consumes: `TerritorialCombat.DealtMultiplier(float, int)` (Task 1); `TeamScoreManager.VanguardTier(Team)` (Task 3).
- Produces (signature changes other code must match):
  - `float TeamManager.GetDamageDealtModifier(Team attacker, float territorialAdvantage, int vanguardTier)`
  - `float CombatConfig.CalculateFinalDamage(float baseDamage, float dealtModifier, bool isCritical = false)`
  - `int CombatConfig.ResolveDamage(float baseDamage, Team attackerTeam, Vector2 attackerPos)`
  - `int PlayerCombat.ResolveMeleeDamage()` — no parameters
  - **Removed:** `TeamManager.GetDamageReceivedModifier`, `TeamManager.minDamageMultiplier`, `TeamManager.maxDamageMultiplier`.

- [ ] **Step 1: Rewrite the TeamManager modifier API**

In `Assets/Scripts/Teams/TeamManager.cs`, add the core namespace to the usings at the top of the file:

```csharp
using UnityEngine;
using Game.Combat.Core;
```

Delete the whole `[Header("Damage Scaling")]` block (the `minDamageMultiplier` and `maxDamageMultiplier` fields with their tooltips) — the lerp endpoints they parameterised no longer exist. The `[Header("AI Team Behavior")]` block and `aiUsesTerritory` stay.

Replace both modifier methods (currently lines 55-69) with the single dealt-side method:

```csharp
    /// <summary>
    /// Damage dealt modifier for an attacking team: x1 everywhere except the enemy third, where the
    /// quantized territorial debuff applies, lifted in halves by the team's Vanguard tier.
    /// Only quantizes GetTerritorialAdvantage's output — the advantage formula itself is unchanged.
    /// There is deliberately NO received-side counterpart: one debuff, one side, one direction.
    /// </summary>
    public float GetDamageDealtModifier(Team attacker, float territorialAdvantage, int vanguardTier)
    {
        if (attacker == Team.Team3AI && !aiUsesTerritory) return 1.0f;
        return TerritorialCombat.DealtMultiplier(territorialAdvantage, vanguardTier);
    }
```

`GetTerritorialAdvantage`, `GetTeamData`, `AreEnemies`, `IsAITeam` and `GetPlayerTeamsEnum` are untouched.

- [ ] **Step 2: Rewrite CombatConfig's damage composition**

In `Assets/Scripts/ScriptableObjects/CombatConfig.cs`, replace the file-header comment block (lines 3-8) with:

```csharp
// META-LAYER DAMAGE MODEL: every attack is resolved by ResolveDamage below.
// finalDamage = base x globalDamageMultiplier x dealtModifier(attacker) x crit.
// dealtModifier is the quantized territorial debuff (x0.33 in the enemy third, x1 elsewhere),
// lifted in halves by the attacking team's Vanguard tier from TeamScoreManager. There is no
// received-side modifier: one debuff, one side, one direction.
// See docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md.
```

Update the `territorialAdvantageEnabled` tooltip so the coupling is stated where it is authored:

```csharp
    [Header("Territorial Combat")]
    [Tooltip("Enable the territorial debuff. Turning this OFF also makes the ENTIRE team-buff " +
             "layer inert, because Vanguard exists only to lift this debuff.")]
    public bool territorialAdvantageEnabled = true;
```

Replace `CalculateFinalDamage` and `ResolveDamage` (lines 72-128) with:

```csharp
    /// <summary>
    /// Pure-math composition from an already-resolved modifier. Called by ResolveDamage;
    /// kept separate so the arithmetic is trivial to reason about.
    /// </summary>
    public float CalculateFinalDamage(float baseDamage, float dealtModifier, bool isCritical = false)
    {
        float damage = baseDamage * globalDamageMultiplier;

        if (territorialAdvantageEnabled)
        {
            damage *= dealtModifier;
        }

        if (isCritical)
        {
            damage *= criticalMultiplier;
        }

        return damage;
    }

    /// <summary>
    /// THE single entry point for all combat damage. Reads the attacker's territorial advantage and
    /// its team's Vanguard tier, rolls crit, and composes via CalculateFinalDamage. Returns a
    /// rounded, non-negative int. Call only on StateAuthority (the call sites already gate on it).
    /// The defender no longer participates: the received-side modifier was deleted with the old
    /// two-sided model.
    /// </summary>
    public int ResolveDamage(float baseDamage, Team attackerTeam, Vector2 attackerPos)
    {
        float dealt = 1f;

        TeamManager teams = TeamManager.Instance;
        if (teams != null)
        {
            int vanguardTier = 0;

            TeamScoreManager scores = TeamScoreManager.Instance;
            if (scores != null && scores.Object != null && scores.Object.IsValid)
                vanguardTier = scores.VanguardTier(attackerTeam);

            float advantage = teams.GetTerritorialAdvantage(attackerTeam, attackerPos);
            dealt = teams.GetDamageDealtModifier(attackerTeam, advantage, vanguardTier);
        }

        if (!territorialAdvantageEnabled) WarnTerritoryDisabledOnce();

        bool isCritical = RollCritical();
        float finalDamage = CalculateFinalDamage(baseDamage, dealt, isCritical);
        return Mathf.Max(0, Mathf.RoundToInt(finalDamage));
    }

    // Not serialized: resets on domain reload, which is exactly the cadence we want for a
    // once-per-session operator warning.
    [System.NonSerialized] private bool warnedTerritoryDisabled;

    /// <summary>
    /// The old team buffs were silent no-ops whenever territorialAdvantageEnabled was false
    /// (they were only ever multiplied in inside that flag's branch). Say so out loud instead.
    /// </summary>
    private void WarnTerritoryDisabledOnce()
    {
        if (warnedTerritoryDisabled) return;
        warnedTerritoryDisabled = true;
        Debug.LogWarning("⚠️ CombatConfig.territorialAdvantageEnabled is FALSE — the territorial " +
                         "debuff and the entire Vanguard team-buff layer are inert. Coin deposits " +
                         "then buy nothing at the team level.");
    }
```

- [ ] **Step 3: Update the two call sites**

In `Assets/Scripts/Player/PlayerCombat.cs`, replace `ResolveMeleeDamage` (lines 290-316) with:

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
```

Then update both callers — line 242 and line 260 currently read
`int finalDamage = ResolveMeleeDamage(hit.gameObject, hit.transform.position);` and both become:

```csharp
                int finalDamage = ResolveMeleeDamage();
```

Leave the surrounding team-hostility checks (`TeamUtil.AreEnemies`) and the knockback/`RPC_HitFeedback` code exactly as they are — those still need the target.

In `Assets/Scripts/Enemy/Base/Enemy.cs`, replace the resolve block (lines 286-292) with:

```csharp
        if (config != null)
        {
            Team myTeam = teamComponent != null ? teamComponent.Team : Team.None;
            finalDamage = config.ResolveDamage(effectiveAttackDamage, myTeam, transform.position);
        }
```

(The `PlayerTeamData playerTeam` / `Team defenderTeam` locals inside that block are deleted with it; `player` is still used immediately afterwards for `ServerApplyDamage`.)

- [ ] **Step 4: Fix the HUD badge so the project compiles**

In `Assets/Scripts/Hud/TeamScoreDisplay.cs`, replace `RepaintBadge` (lines 62-68) with:

```csharp
    private void RepaintBadge()
    {
        if (scoreManager == null || teamBuffBadge == null) return;
        bool active = localTeam != Team.None && scoreManager.VanguardTier(localTeam) > 0;
        teamBuffBadge.SetActive(active);
    }
```

Also update the class summary comment at the top of the file:

```csharp
/// <summary>
/// Team score readout plus a single badge shown only while the local player's team has unlocked
/// Vanguard (tier >= 1). Event-driven off TeamScoreManager; the manager is a runtime singleton, so
/// subscription is deferred until Instance exists. Scope 4 replaces this with the merged Team Power
/// strip that shows the tier and the zone state properly.
/// </summary>
```

The existing subscriptions need no change: `TeamBuffsChanged` now also fires on score changes, which is exactly when the derived tier can move.

- [ ] **Step 5: Verify no stale references remain**

Run:

```bash
git grep -n "GetDamageReceivedModifier\|HasDamageBuff\|HasDefenseBuff\|minDamageMultiplier\|maxDamageMultiplier" -- "Assets/Scripts"
```

Expected: no output.

Run:

```bash
git grep -n "ResolveDamage\|ResolveMeleeDamage" -- "Assets/Scripts"
```

Expected: 5 lines — the definition and the doc comment in `CombatConfig.cs`, the call in `Enemy.cs`, and in `PlayerCombat.cs` the definition plus its two calls.

- [ ] **Step 6: Compile the whole surface**

Run the bundled-Roslyn whole-surface compile gate described in "How to run tests" (or let the Unity editor recompile if it is open and unlocked). Because `Game.Combat.Core` and `Game.Buffs.Core` both changed in this branch, drop `Library\ScriptAssemblies\Game.Combat.Core.dll` and `Game.Buffs.Core.dll` from the references and compile `Assets/Scripts/Combat/Core/*.cs` and `Assets/Scripts/Buffs/Core/*.cs` inline instead.
Expected: 0 errors. Note that this is a compile gate, **not** verification of behaviour.

- [ ] **Step 7: Commit**

```bash
git add "Assets/Scripts/Teams/TeamManager.cs" "Assets/Scripts/ScriptableObjects/CombatConfig.cs" "Assets/Scripts/Player/PlayerCombat.cs" "Assets/Scripts/Enemy/Base/Enemy.cs" "Assets/Scripts/Hud/TeamScoreDisplay.cs" && git commit -m "feat(combat): route damage through the one-sided Vanguard-lifted debuff"
```

---

## Task 5: Pacing regression test over the whole resolved chain

A test that fails if anyone later reconnects the two halves wrongly — it asserts the *product* the spec promises (0.33 / 0.665 / 1.00 at the team scores that should produce them), not just each half in isolation.

**Files:**
- Modify: `Assets/Tests/EditMode/Combat/TerritorialCombatTests.cs`

**Interfaces:**
- Consumes: `TerritorialCombat.DealtMultiplier` (Task 1) and `TeamBuffUnlock.TeamTier` (Task 2).
- Produces: nothing (test-only).

- [ ] **Step 1: Add the Game.Buffs.Core reference to the Combat test asmdef**

The pacing test spans both pure assemblies, so `Assets/Tests/EditMode/Combat/Game.Combat.Tests.asmdef` must reference both. Replace its `references` array so the file reads:

```json
{
    "name": "Game.Combat.Tests",
    "rootNamespace": "",
    "references": [
        "Game.Combat.Core",
        "Game.Buffs.Core",
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

- [ ] **Step 2: Write the failing test**

Append to `Assets/Tests/EditMode/Combat/TerritorialCombatTests.cs` (and add `using System.Collections.Generic;` and `using Game.Buffs.Core;` to its usings):

```csharp
    // End-to-end pacing: team score -> Vanguard tier -> damage dealt deep in the enemy third.
    // A 10-player team, so the {12, 45} per-player averages are absolute scores of 120 and 450.
    private static readonly List<int> VanguardThresholds = new List<int> { 12, 45 };

    [TestCase(0, 0.33f)]     // match start: full debuff
    [TestCase(119, 0.33f)]   // just short of T1
    [TestCase(120, 0.665f)]  // T1: half the debuff removed
    [TestCase(449, 0.665f)]
    [TestCase(450, 1.0f)]    // T2: fully lifted
    [TestCase(550, 1.0f)]    // typical end-state (~55 per player)
    public void EnemyThirdDamage_TracksTeamEconomy(int teamScore, float expectedMultiplier)
    {
        int tier = TeamBuffUnlock.TeamTier(VanguardThresholds, teamScore, rosterSize: 10, maxTier: 2);
        float dealt = TerritorialCombat.DealtMultiplier(-0.8f, tier);
        Assert.AreEqual(expectedMultiplier, dealt, 1e-4f);
    }

    // Own half is never debuffed, however poor the team's economy is.
    [TestCase(0)]
    [TestCase(550)]
    public void OwnTerritoryDamage_IsAlwaysNeutral(int teamScore)
    {
        int tier = TeamBuffUnlock.TeamTier(VanguardThresholds, teamScore, rosterSize: 10, maxTier: 2);
        Assert.AreEqual(1.0f, TerritorialCombat.DealtMultiplier(0.5f, tier), 1e-4f);
    }
```

- [ ] **Step 3: Run the tests**

Run: Test Runner ▸ EditMode ▸ Run All (or the Roslyn harness — this file now needs both core folders compiled in).
Expected: PASS. If `EnemyThirdDamage_TracksTeamEconomy` unlocks at scores 12 and 45 instead of 120 and 450, the roster divisor has been lost somewhere.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Tests/EditMode/Combat/TerritorialCombatTests.cs" "Assets/Tests/EditMode/Combat/Game.Combat.Tests.asmdef" && git commit -m "test(combat): assert team-economy pacing through the resolved debuff"
```

---

## Task 6: Full verification pass and honest reporting

**Files:** none modified (unless a defect is found).

- [ ] **Step 1: Run the entire EditMode suite**

Run: Test Runner ▸ EditMode ▸ Run All.
Expected: everything green, including the pre-existing `BuffUnlockTests`, `MatchResolverTests`, `HitCooldownLedgerTests`, `SwingPhaseTests`, and the two new files. Record the actual pass/fail counts — do not paraphrase them.

- [ ] **Step 2: Whole-surface compile gate**

Run the bundled-Roslyn gate from "How to run tests" with both changed core folders compiled inline.
Expected: 0 errors.

- [ ] **Step 3: In-editor smoke test (single player)**

Open `Assets/Scenes/Gameplay.unity`, select the `TeamScoreManager` GameObject, and confirm the inspector shows **Vanguard Thresholds = [12, 45]** and **Vanguard Max Tier = 2** (they come from the field initializers because the scene YAML has no entry for them yet), and that the old Damage/Defense threshold and buff-unlock event rows are gone once the scene is saved.

Then play with `GameNetworkManager.singlePlayerMode` (Host):
1. Walk into the enemy third and hit an enemy — damage should be roughly a third of what the same swing does at home.
2. Bank coins until your team's score passes `12 × roster` (solo: 12), then hit again in the enemy third — damage should be roughly two thirds.
3. Pass `45 × roster` (solo: 45) — enemy-third damage should match home damage.

- [ ] **Step 4: Multi-peer check (1 host + 1 client)**

Run Multiplayer Play Mode with 1 host + 1 client on opposite teams and confirm:
1. The roster freeze happened once at Live: both peers agree on the badge state.
2. A client-side attacker sees the same damage numbers the host resolves (the client does not resolve damage; it must not diverge visually).
3. A late joiner sees the correct badge state without any deposit happening after they join.

- [ ] **Step 5: Report**

State plainly, item by item: what was **executed** (EditMode suite with counts, compile gate), what was **observed in play** (each smoke-test step), and what was **not** checked. Do not describe a clean compile as verification, and do not claim a play observation that was not actually made.

---

## Self-Review

**Spec coverage**

| Spec / prompt requirement | Task |
|---|---|
| Reuse `GetTerritorialAdvantage` unchanged as input, quantize only its output | 4 (Step 1); the method is untouched |
| Two zones split at −0.33, full debuff ×0.33, swing 3× | 1 |
| Delete `GetDamageReceivedModifier` and the `receivedModifier` parameter from `CalculateFinalDamage` / `ResolveDamage` | 4 (Steps 1-3) |
| The debuff must not silently vanish when `territorialAdvantageEnabled` is false | 4 (Step 2 — tooltip + one-time warning) |
| Delete the four buff bools, both thresholds, and `CheckMilestones` | 3 |
| Delete `onDamageBuffUnlocked` / `onDefenseBuffUnlocked` | 3 |
| Vanguard: 2 tiers, `1 - 0.67 * (1 - 0.5 * tier)` → 0.33 / 0.665 / 1.00 | 1 (impl + test) |
| Derive the team tier through `BuffUnlock` with `buffCount == 1`, `maxTier == 2` | 2 |
| Thresholds `{12, 45}` as per-player averages vs `teamScore / TeamRosterSize` | 2, 3 |
| `TeamRosterSize` captured once as `[Networked]` state on entering Live | 3 |
| Tests: Vanguard formula at tiers 0/1/2 | 1 (`DebuffWithVanguard_LiftsTheDebuffInHalves`) |
| Tests: zone classification at the −0.33 boundary from both sides | 1 (`InEnemyThird_SplitsAtMinusOneThird`) |
| Tests: threshold normalisation, roster 1, empty team, exact 12 and 45 | 2 (`TeamTier_SoloRoster`, `TeamTier_EmptyRosterIsLocked`, `TeamTier_TenPlayerRosterNormalises`) |
| Report what was actually run; compile ≠ verification | 6 |
| Out of scope untouched: individual catalog, coin drops, HUD beyond compile-correctness, match phases, respawn path | verified by the Task 4/5 grep steps and the Global Constraints |

**Type consistency:** `VanguardTier(Team)` is the name used in Tasks 3, 4 and 6. `TeamBuffUnlock.TeamTier(thresholds, teamScore, rosterSize, maxTier)` is used identically in Tasks 2, 3 and 5. `TerritorialCombat.DealtMultiplier(advantage, tier)` is used identically in Tasks 1, 4 and 5. `GetDamageDealtModifier(Team, float, int)` is defined and called only in Task 4.

**Known consequence, flagged not fixed:** with `Team3AI` enemies ignoring territory (`aiUsesTerritory = false`, the current scene value) the debuff applies to players only, which is the pre-existing behaviour and unchanged by this work.
