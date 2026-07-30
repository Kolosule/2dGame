# Economy Feedback Surfaces + Deterministic Coin Supply — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the coins→buffs economy legible — deterministic enemy coin drops, a merged Team Power strip that folds Vanguard into the territory indicator, tier pips + next-unlock progress on the individual buff row, unlock toasts, and a Sudden Death banner.

**Architecture:** Every decision this HUD makes is extracted into engine-free pure statics so it is unit-testable outside Unity: `Game.Buffs.Core.BuffProgress` (next-threshold / progress-to-next-tier), `Game.Hud.Core.TerritoryReadout` (zone band with the Vanguard fold, delegating to the existing `Game.Combat.Core.TerritorialCombat`), `Game.Hud.Core.TierUpEdge` (client-side tier-up edge detection), `Game.Hud.Core.ToastFade`. The `MonoBehaviour` layer only binds and paints: it subscribes to the existing `PlayerBuffs.BuffsChanged` / `TeamScoreManager.ScoresChanged` / `TeamScoreManager.TeamBuffsChanged` / `MatchManager.PhaseChanged` events, never polls networked values, and adds **no new networked state**. Coin supply becomes computable by replacing `Random.Range(min, max+1)` with one authored `coinsToDrop` per archetype plus an integer `coinDropBonus` on `RingTier`.

**Tech Stack:** Unity 6.3 (6000.3.0f1), Photon Fusion 2 (Host/Client + dedicated server), C#, NUnit EditMode tests, TextMeshPro + uGUI, Unity Editor tooling (`Assets/Scripts/Editor`).

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md` — sections "Feedback surfaces", "Deterministic coin supply", "Sudden Death", plus audit finding 6.
- **Branch:** `feat/economy-feedback-surfaces`, cut from `feat/individual-buff-layer`, which already contains Scope 1 (PR #67), Scope 2 (PR #68) and Scope 3. Do **not** rebase onto `main` — `main` has none of them.
- HUD is **event-driven** under `Assets/Scripts/Hud/`, never polling networked values. Subscribe in `Bind`, unsubscribe in `Unbind`, follow the existing `IHudBindable` pattern. The one legitimate per-frame read is the local player's own **transform position** (positions are not events) and the ticking match clock — both already precedented in `MatchPhaseHud.LateUpdate`.
- Runtime-singleton managers (`TeamScoreManager`, `MatchManager`, `TeamManager`) may not exist yet when a HUD element binds. Use the lazy-subscribe pattern already in `TeamScoreDisplay.Update` / `MatchPhaseHud.LateUpdate`, and cache the subscribed instance so unsubscription never re-resolves a static that may already be null during teardown (the pattern `PlayerBuffs.subscribedMatchManager` uses).
- Visuals in `Render()` / `Update()` / `LateUpdate()`; **never** drive gameplay from HUD code. No `[Networked]` fields are added anywhere in this plan except none — read-only derivation only.
- Managers, flags and the carrier must stay **always-interested** under interest management, or the HUD vanishes for distant players in a 20-player match. This plan adds no new networked objects, so nothing new to register.
- Pure logic lives in engine-free asmdefs (`Game.Buffs.Core`, `Game.Combat.Core`, `Game.Hud.Core` — all `noEngineReferences: true`, so **no `UnityEngine.Mathf`, no `Color`, no `Time`** inside them). Clamp with plain `if`s, as `CooldownFill` and `BuffTierVisual` already do.
- `BuffId : byte { ExtraJump = 0, Stealth = 1, QuickerDash = 2, FlagRunner = 3 }`. `Team { None = 0, Team1 = 1, Team2 = 2, Team3AI = 3 }`. `MatchPhase : byte { Warmup, Countdown, Live, PostMatch, Intermission, SuddenDeath }`.
- **Out of scope — do not touch:** the debuff magnitude or either threshold curve (set in Scopes 2 and 3), match phase *transition* logic (Scope 1), the per-player stats scoreboard (K/D, captures, coins), `RPC_AddPoints`'s `RpcSources.All` cheat surface (wants its own security pass — do not make it worse), and the dead `GameSettingsManager.GetRespawnTime` / `respawnTimeMultiplier` / `TeamData.respawnDelay` path.
- **New asset `.meta` files:** the editor only generates them on focus/refresh, so create them by hand next to every new `.cs`. Template (fresh random 32-hex guid per file):

```yaml
fileFormatVersion: 2
guid: <32 random hex chars>
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

### Numbers, verbatim from the spec

| Thing | Value |
|---|---|
| Individual curve (4 buffs × 3 tiers) | `5, 10, 16, 24, 34, 46, 62, 80, 110, 150, 200, 260` |
| Individual max tier | 3 |
| Vanguard thresholds | `{12, 45}` **per-player-average** deposited value (`teamScore / rosterSize`) |
| Vanguard max tier | 2 |
| Enemy-third boundary | advantage `< -0.33` |
| Dealt multiplier in enemy third | ×0.33 / ×0.665 / ×1.00 at Vanguard tiers 0 / 1 / 2 |
| Coin point value | 1 (unchanged) |
| `coinsToDrop` per archetype | Box 2, Octagon 3, Circle 3, Flyer 4 |
| `coinDropBonus` per ring | +0 outer / +1 middle / +2 inner |

Both drop numbers are **starting values to tune in play**. The archetype ladder is the spec's "2 for the weakest rising to 4 for the strongest" spread linearly over four archetypes (25/50/75/100 max health → 2, 2.67, 3.33, 4 → rounded to 2, 3, 3, 4).

### How to run tests

EditMode tests run in Unity: **Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All**.

If the editor holds the project lock (`Unity.exe -batchmode -runTests` then fails), use the bundled-Roslyn workaround: compile the engine-free core `.cs` plus a hand-written assert harness against `netstandard 2.1` with
`C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Data\DotNetSdkRoslyn\csc.dll`, write a `net8.0` `runtimeconfig.json` beside the exe, and run it on
`C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Data\NetCoreRuntime\dotnet.exe`.
For the whole-surface compile gate, build a `@response.rsp` for `csc.dll` referencing the netstandard ref, `Editor\Data\Managed\UnityEngine\*.dll`, `Assets\Photon\Fusion\Assemblies\*.dll`, and `Library\ScriptAssemblies\*.dll` (skip `*Editor*` / `*CodeGen*` / `*Tests*`), compiling every `Assets/Scripts/**/*.cs` **except** the asmdef-owned folders (`Buffs/Core`, `Combat/Core`, `Enemy/AI`, `Hud/Core`, `Net`, `Player/Animation/Core`, `Player/Movement/Core`, `Match/Core`). Quote every path inside the `.rsp` ("Program Files" has a space). `Game.Buffs.Core` and `Game.Hud.Core` both change in this branch — drop their stale `Library\ScriptAssemblies\*.dll` from the references and compile those folders' `.cs` inline instead.

**A clean compile is not verification.** Report separately what was executed and what was only compiled.

---

## File Structure

**Created:**
- `Assets/Scripts/Buffs/Core/BuffProgress.cs` (+ `.meta`) — pure next-threshold / progress-to-next-tier math shared by the individual row and the team strip.
- `Assets/Scripts/Hud/Core/TerritoryReadout.cs` (+ `.meta`) — pure zone band **with the Vanguard fold**; the one place "is the local player penalised right now" is decided.
- `Assets/Scripts/Hud/Core/TierUpEdge.cs` (+ `.meta`) — pure, primeable rising-edge detector so a bind/late-join never toasts.
- `Assets/Scripts/Hud/Core/ToastFade.cs` (+ `.meta`) — pure hold-then-fade alpha.
- `Assets/Scripts/Hud/HudToastFeed.cs` (+ `.meta`) — the shared transient-notification surface (one queue, one label).
- `Assets/Scripts/Hud/TierPipRow.cs` (+ `.meta`) — the one pip-painting loop, shared by the buff row and the team strip. Engine-bound (it touches `Image`), so it cannot live in the `noEngineReferences` `Game.Hud.Core` next to `BuffTierVisual.PipFilled`.
- `Assets/Scripts/Editor/EconomyHudBuilder.cs` (+ `.meta`) — one-click builder that creates and wires every new UI object, mirroring `MatchHudBuilder`.
- `Assets/Tests/EditMode/BuffProgressTests.cs` (+ `.meta`) — in the existing `Game.Buffs.EditModeTests` asmdef.
- `Assets/Tests/EditMode/Hud/TerritoryReadoutTests.cs`, `TierUpEdgeTests.cs`, `ToastFadeTests.cs` (+ `.meta` each) — in the existing `Game.Hud.Tests` asmdef.

**Modified:**
- `Assets/Scripts/Enemy/AI/DifficultyRingConfig.cs` — `RingTier.coinDropBonus`.
- `Assets/Scripts/Enemy/Base/Enemy.cs:25-26,116-138,326-350` — single `coinsToDrop`, ring bonus folded in at spawn, `Random.Range` count roll deleted.
- `Assets/Scripts/Enemy/Prefabs/*.prefab` (7 files) — replace the two serialized coin fields with one.
- `Assets/Scripts/Buffs/Core/TeamBuffUnlock.cs` — `allUnlocked` parameter (Sudden Death parity with the individual layer).
- `Assets/Scripts/Coin Scripts/TeamScoreManager.cs` — Sudden Death tier override + read-only accessors the strip needs.
- `Assets/Scripts/Buffs/PlayerBuffs.cs` — `MaxTier`, `NextUnlockProgress01`, `NextUnlockThreshold` accessors.
- `Assets/Scripts/Hud/Core/BuffTierVisual.cs` — `PipFilled`.
- `Assets/Scripts/Hud/Core/Game.Hud.Core.asmdef` — reference `Game.Combat.Core`.
- `Assets/Scripts/Hud/BuffIconDisplay.cs` — pips, next-unlock fill, tier-up toast.
- `Assets/Scripts/Hud/TeamScoreDisplay.cs` — **rewritten in place** as the merged Team Power strip. Kept as the same class name and file on purpose: the component is already wired into the Gameplay scene with its two score-text references, and renaming the class would silently null every one of them.
- `Assets/Scripts/Hud/MatchPhaseHud.cs` — Sudden Death banner + timer visible in Sudden Death when the ops hard cap is armed.
- `Assets/Tests/EditMode/EnemyAI/DifficultyRingConfigTests.cs` — coin bonus band selection.
- `Assets/Tests/EditMode/Hud/BuffTierVisualTests.cs` — pip cases.
- `Assets/Tests/EditMode/TeamBuffUnlockTests.cs` — Sudden Death parity cases.

**Deliberately NOT created:** a `DifficultyRingConfig` `.asset`. The repo has none today (`GameSettingsManager.difficultyRingConfig` is unassigned, and `Enemy.ResolveEffectiveStats` logs "no DifficultyRingConfig/ArenaCenter; using base stats"), so ring bonuses are dormant until someone authors the bands — and band radii are map-specific numbers that must be authored against the real arena, not guessed here. Task 1 makes the bonus land the moment such an asset exists, and Task 10's manual checklist calls it out.

---

## Task 1: Deterministic coin supply

**Files:**
- Modify: `Assets/Scripts/Enemy/AI/DifficultyRingConfig.cs:9-25`
- Modify: `Assets/Scripts/Enemy/Base/Enemy.cs:24-26`, `:66-69`, `:116-138`, `:326-350`
- Modify: `Assets/Scripts/Enemy/Prefabs/{RedEnemy,VioletEnemy,Indigo Enemy,OrangeEnemy,BlueEnemy,YellowEnemy,GreenEnemy}.prefab`
- Test: `Assets/Tests/EditMode/EnemyAI/DifficultyRingConfigTests.cs`

**Interfaces:**
- Produces: `RingTier.coinDropBonus` (`public int`), `RingTier.Identity.coinDropBonus == 0`. Nothing else in this plan consumes them; the HUD never reads drop rates.

- [ ] **Step 1: Write the failing test**

Append to `Assets/Tests/EditMode/EnemyAI/DifficultyRingConfigTests.cs` (inside the existing test class, matching its existing style):

```csharp
    [Test]
    public void GetRing_ReturnsTheCoinDropBonusOfTheMatchedBand()
    {
        var config = ScriptableObject.CreateInstance<DifficultyRingConfig>();
        config.rings = new[]
        {
            new RingTier { maxDistanceFromCenter = 10f, healthMult = 3f, damageMult = 3f, speedMult = 1f, coinDropBonus = 2 },
            new RingTier { maxDistanceFromCenter = 25f, healthMult = 2f, damageMult = 2f, speedMult = 1f, coinDropBonus = 1 },
            new RingTier { maxDistanceFromCenter = 60f, healthMult = 1f, damageMult = 1f, speedMult = 1f, coinDropBonus = 0 },
        };

        Assert.AreEqual(2, config.GetRing(5f).coinDropBonus, "inner band");
        Assert.AreEqual(1, config.GetRing(20f).coinDropBonus, "middle band");
        Assert.AreEqual(0, config.GetRing(50f).coinDropBonus, "outer band");
        Assert.AreEqual(0, config.GetRing(999f).coinDropBonus, "beyond the outermost band clamps to it");

        Object.DestroyImmediate(config);
    }

    [Test]
    public void Identity_HasNoCoinDropBonus()
    {
        Assert.AreEqual(0, RingTier.Identity.coinDropBonus);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: **Test Runner ▸ EditMode ▸ Run All** (or the Roslyn compile gate).
Expected: FAIL — `'RingTier' does not contain a definition for 'coinDropBonus'`.

- [ ] **Step 3: Add the field to `RingTier`**

In `Assets/Scripts/Enemy/AI/DifficultyRingConfig.cs`, after `public float speedMult;`:

```csharp
    [Tooltip("Flat extra coins dropped by enemies in this band. INTEGER so total coin supply " +
             "stays exactly computable: total = kills x (coinsToDrop + coinDropBonus).")]
    public int coinDropBonus;
```

and inside `RingTier.Identity`'s initializer, after `speedMult = 1f`:

```csharp
        speedMult = 1f,
        coinDropBonus = 0
```

- [ ] **Step 4: Run the test to verify it passes**

Expected: PASS.

- [ ] **Step 5: Replace the random drop count in `Enemy`**

In `Assets/Scripts/Enemy/Base/Enemy.cs`, replace lines 24-26:

```csharp
    [Tooltip("How many coins to drop on death. AUTHORED PER ARCHETYPE, no randomness — pacing " +
             "cannot be tuned against a random drop. Stronger archetypes drop more. The ring's " +
             "coinDropBonus is added on top at spawn.")]
    [SerializeField] private int coinsToDrop = 2;
```

Add beside the other effective stats (after `private float effectiveMoveSpeed;`):

```csharp
    private int effectiveCoinDrop;
```

In `ResolveEffectiveStats()`, after the `effectiveMoveSpeed` line:

```csharp
        effectiveCoinDrop = Mathf.Max(0, coinsToDrop + tier.coinDropBonus);
```

In `SpawnCoins()`, replace the `Random.Range` line:

```csharp
        // Deterministic: archetype base + this enemy's ring bonus, both resolved once at spawn.
        int coinCount = effectiveCoinDrop;
```

Leave `Random.insideUnitCircle` scatter alone — it is cosmetic placement, server-side, and the spec only removes randomness from the *count*.

- [ ] **Step 6: Update the seven enemy prefabs**

Each prefab has exactly these two adjacent lines under the `Enemy` component:

```yaml
  coinsToDropMin: 1
  coinsToDropMax: 1
```

Replace **both lines** with a single `coinsToDrop: <N>` line per prefab. A field absent from prefab YAML deserializes to **0**, so missing one prefab means that archetype silently drops nothing — check all seven.

| Prefab | Archetype | `coinsToDrop` |
|---|---|---|
| `RedEnemy.prefab` | Box | 2 |
| `VioletEnemy.prefab` | Box | 2 |
| `Indigo Enemy.prefab` | Octagon | 3 |
| `OrangeEnemy.prefab` | Octagon | 3 |
| `BlueEnemy.prefab` | Circle | 3 |
| `YellowEnemy.prefab` | Circle | 3 |
| `GreenEnemy.prefab` | Flyer | 4 |

- [ ] **Step 7: Verify no `coinsToDropMin` / `coinsToDropMax` references survive**

Run:

```bash
grep -rn "coinsToDropM" Assets/
```

Expected: no output.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Enemy Assets/Tests/EditMode/EnemyAI/DifficultyRingConfigTests.cs
git commit -m "feat(economy): deterministic per-archetype coin drops with a ring bonus"
```

---

## Task 2: Pure unlock-progress math

**Files:**
- Create: `Assets/Scripts/Buffs/Core/BuffProgress.cs` (+ `.meta`)
- Test: `Assets/Tests/EditMode/BuffProgressTests.cs` (+ `.meta`)

**Interfaces:**
- Consumes: `Game.Buffs.Core.BuffUnlock.UnlockedSteps(IReadOnlyList<int>, int)`.
- Produces:
  - `BuffProgress.NextStepIndexFor(int unlockedSteps, int priorityPosition, int buffCount, int thresholdCount) -> int` (`-1` = none left)
  - `BuffProgress.HighestCrossed(IReadOnlyList<int> thresholds, int value) -> int` (`0` = none)
  - `BuffProgress.Fraction01(int value, int lower, int upper) -> float`
  - `BuffProgress.NextThresholdFor(IReadOnlyList<int> thresholds, int value, int priorityPosition, int buffCount) -> int` (`0` = none left)
  - `BuffProgress.ToNextTier01(IReadOnlyList<int> thresholds, int value, int priorityPosition, int buffCount) -> float`

  The team layer calls these with `priorityPosition: 0, buffCount: 1` — the same convention `TeamBuffUnlock` already uses — so there is exactly one progress implementation for both layers.

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/BuffProgressTests.cs`:

```csharp
using NUnit.Framework;
using Game.Buffs.Core;

/// <summary>
/// Progress math for the HUD's next-unlock fill. The 12-step individual curve
/// (4 buffs x 3 tiers) and the 2-step team curve (1 buff x 2 tiers) share it.
/// </summary>
public class BuffProgressTests
{
    private static readonly int[] Curve = { 5, 10, 16, 24, 34, 46, 62, 80, 110, 150, 200, 260 };
    private static readonly int[] Vanguard = { 12, 45 };

    [Test]
    public void NextStepIndex_IsTheNextRoundRobinStepForThisPosition()
    {
        // 6 steps unlocked, 4 buffs: position 0 already took steps 0 and 4, so its next is 8.
        Assert.AreEqual(8, BuffProgress.NextStepIndexFor(6, 0, 4, 12));
        // Position 2's steps are 2, 6, 10 — step 2 is crossed, so its next is 6.
        Assert.AreEqual(6, BuffProgress.NextStepIndexFor(6, 2, 4, 12));
        // Nothing unlocked: each position's first step is its own index.
        Assert.AreEqual(3, BuffProgress.NextStepIndexFor(0, 3, 4, 12));
    }

    [Test]
    public void NextStepIndex_IsMinusOneWhenTheCurveIsExhausted()
    {
        Assert.AreEqual(-1, BuffProgress.NextStepIndexFor(12, 0, 4, 12));
        Assert.AreEqual(-1, BuffProgress.NextStepIndexFor(12, 3, 4, 12));
    }

    [Test]
    public void NextStepIndex_GuardsNonsenseInputs()
    {
        Assert.AreEqual(-1, BuffProgress.NextStepIndexFor(0, 0, 0, 12), "buffCount 0");
        Assert.AreEqual(-1, BuffProgress.NextStepIndexFor(0, -1, 4, 12), "negative position");
    }

    [Test]
    public void HighestCrossed_IsTheLastThresholdAtOrBelowTheValue()
    {
        Assert.AreEqual(0, BuffProgress.HighestCrossed(Curve, 4));
        Assert.AreEqual(5, BuffProgress.HighestCrossed(Curve, 5), "exact boundary counts as crossed");
        Assert.AreEqual(46, BuffProgress.HighestCrossed(Curve, 55));
        Assert.AreEqual(260, BuffProgress.HighestCrossed(Curve, 999));
        Assert.AreEqual(0, BuffProgress.HighestCrossed(null, 100));
    }

    [Test]
    public void Fraction01_IsLinearBetweenBounds_AndClamps()
    {
        Assert.AreEqual(0f, BuffProgress.Fraction01(10, 10, 20), 1e-4f);
        Assert.AreEqual(0.5f, BuffProgress.Fraction01(15, 10, 20), 1e-4f);
        Assert.AreEqual(1f, BuffProgress.Fraction01(20, 10, 20), 1e-4f);
        Assert.AreEqual(0f, BuffProgress.Fraction01(3, 10, 20), 1e-4f, "below the lower bound");
        Assert.AreEqual(1f, BuffProgress.Fraction01(99, 10, 20), 1e-4f, "above the upper bound");
        Assert.AreEqual(1f, BuffProgress.Fraction01(5, 20, 20), 1e-4f, "degenerate range reads as full");
    }

    [Test]
    public void NextThresholdFor_IsTheDepositThatRaisesThisBuff()
    {
        // 55 banked -> 6 steps. Position 0 next tiers at step 8 (110); position 3 at step 7 (80).
        Assert.AreEqual(110, BuffProgress.NextThresholdFor(Curve, 55, 0, 4));
        Assert.AreEqual(80, BuffProgress.NextThresholdFor(Curve, 55, 3, 4));
        // Fully banked: nothing left for anyone.
        Assert.AreEqual(0, BuffProgress.NextThresholdFor(Curve, 260, 0, 4));
        Assert.AreEqual(0, BuffProgress.NextThresholdFor(null, 55, 0, 4));
    }

    [Test]
    public void ToNextTier01_RunsFromTheLastCrossedThresholdToThisBuffsNextOne()
    {
        // 55 banked: last crossed is 46, position 0's target is 110 -> (55-46)/(110-46).
        Assert.AreEqual(9f / 64f, BuffProgress.ToNextTier01(Curve, 55, 0, 4), 1e-4f);
        // Sitting exactly on a threshold reads as empty toward the NEXT one, not full.
        Assert.AreEqual(0f, BuffProgress.ToNextTier01(Curve, 46, 0, 4), 1e-4f);
        // One point short of the target reads nearly full.
        Assert.AreEqual(63f / 64f, BuffProgress.ToNextTier01(Curve, 109, 0, 4), 1e-4f);
    }

    [Test]
    public void ToNextTier01_IsFullWhenNothingIsLeftToUnlock()
    {
        Assert.AreEqual(1f, BuffProgress.ToNextTier01(Curve, 260, 0, 4), 1e-4f);
        Assert.AreEqual(1f, BuffProgress.ToNextTier01(Curve, 5000, 3, 4), 1e-4f);
        Assert.AreEqual(1f, BuffProgress.ToNextTier01(null, 55, 0, 4), 1e-4f);
    }

    [Test]
    public void TeamCurve_UsesTheSameMathWithBuffCountOne()
    {
        // A team averaging 30: Vanguard T1 crossed at 12, next milestone is 45.
        Assert.AreEqual(45, BuffProgress.NextThresholdFor(Vanguard, 30, 0, 1));
        Assert.AreEqual(18f / 33f, BuffProgress.ToNextTier01(Vanguard, 30, 0, 1), 1e-4f);
        // Maxed out.
        Assert.AreEqual(0, BuffProgress.NextThresholdFor(Vanguard, 45, 0, 1));
        Assert.AreEqual(1f, BuffProgress.ToNextTier01(Vanguard, 45, 0, 1), 1e-4f);
        // Nothing banked: fill runs 0 -> 12.
        Assert.AreEqual(12, BuffProgress.NextThresholdFor(Vanguard, 0, 0, 1));
        Assert.AreEqual(0f, BuffProgress.ToNextTier01(Vanguard, 0, 0, 1), 1e-4f);
    }
}
```

Create `Assets/Tests/EditMode/BuffProgressTests.cs.meta` from the Global Constraints template.

- [ ] **Step 2: Run the test to verify it fails**

Expected: FAIL — `The name 'BuffProgress' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Buffs/Core/BuffProgress.cs`:

```csharp
using System.Collections.Generic;

namespace Game.Buffs.Core
{
    /// <summary>
    /// Pure progress math for the unlock curves: where the next tier-up sits, and how far along
    /// the way a total is. Used by the HUD only — nothing here decides a tier (BuffUnlock does),
    /// so a bug here cannot desync gameplay.
    /// The individual layer calls it with the player's priority position and buffCount 4; the team
    /// layer with position 0 and buffCount 1, matching TeamBuffUnlock's convention. One
    /// implementation serves both.
    /// Engine-free (this asmdef sets noEngineReferences) so it is testable outside Unity.
    /// See docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md, "Feedback surfaces".
    /// </summary>
    public static class BuffProgress
    {
        /// <summary>
        /// Index of the next unlock step that would raise the buff at this priority position, or
        /// -1 once the curve is exhausted. Steps for a position are position, position + buffCount,
        /// position + 2*buffCount, ... under the round-robin.
        /// </summary>
        public static int NextStepIndexFor(int unlockedSteps, int priorityPosition, int buffCount,
                                           int thresholdCount)
        {
            if (buffCount <= 0 || priorityPosition < 0) return -1;
            int i = priorityPosition;
            while (i < unlockedSteps) i += buffCount;
            return i < thresholdCount ? i : -1;
        }

        /// <summary>Highest threshold at or below the value, or 0 when none is crossed yet.</summary>
        public static int HighestCrossed(IReadOnlyList<int> thresholds, int value)
        {
            if (thresholds == null) return 0;
            int steps = BuffUnlock.UnlockedSteps(thresholds, value);
            return steps <= 0 ? 0 : thresholds[steps - 1];
        }

        /// <summary>
        /// Where value sits between lower and upper, clamped to 0..1. A degenerate range
        /// (upper &lt;= lower) reads as full, so an exhausted curve never renders as an empty bar.
        /// </summary>
        public static float Fraction01(int value, int lower, int upper)
        {
            if (upper <= lower) return 1f;
            if (value <= lower) return 0f;
            if (value >= upper) return 1f;
            return (float)(value - lower) / (upper - lower);
        }

        /// <summary>
        /// The deposited value at which the buff at this position next tiers up; 0 when it can
        /// rise no further.
        /// </summary>
        public static int NextThresholdFor(IReadOnlyList<int> thresholds, int value,
                                           int priorityPosition, int buffCount)
        {
            if (thresholds == null) return 0;
            int steps = BuffUnlock.UnlockedSteps(thresholds, value);
            int next = NextStepIndexFor(steps, priorityPosition, buffCount, thresholds.Count);
            return next < 0 ? 0 : thresholds[next];
        }

        /// <summary>
        /// Fill 0..1 from the last threshold crossed by ANY buff to the next one that raises THIS
        /// buff. Reaches exactly 1 on the deposit that tiers it up, and reads 1 when nothing is
        /// left to unlock.
        /// </summary>
        public static float ToNextTier01(IReadOnlyList<int> thresholds, int value,
                                         int priorityPosition, int buffCount)
        {
            if (thresholds == null) return 1f;
            int steps = BuffUnlock.UnlockedSteps(thresholds, value);
            int next = NextStepIndexFor(steps, priorityPosition, buffCount, thresholds.Count);
            if (next < 0) return 1f;
            return Fraction01(value, HighestCrossed(thresholds, value), thresholds[next]);
        }
    }
}
```

Create `Assets/Scripts/Buffs/Core/BuffProgress.cs.meta` from the template.

- [ ] **Step 4: Run the tests to verify they pass**

Expected: PASS, all 8 cases.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Buffs/Core/BuffProgress.cs Assets/Scripts/Buffs/Core/BuffProgress.cs.meta Assets/Tests/EditMode/BuffProgressTests.cs Assets/Tests/EditMode/BuffProgressTests.cs.meta
git commit -m "feat(buffs): pure next-unlock progress math shared by both layers"
```

---

## Task 3: Pure HUD readout math

**Files:**
- Create: `Assets/Scripts/Hud/Core/TerritoryReadout.cs` (+ `.meta`)
- Create: `Assets/Scripts/Hud/Core/TierUpEdge.cs` (+ `.meta`)
- Create: `Assets/Scripts/Hud/Core/ToastFade.cs` (+ `.meta`)
- Modify: `Assets/Scripts/Hud/Core/BuffTierVisual.cs`
- Modify: `Assets/Scripts/Hud/Core/Game.Hud.Core.asmdef:4`
- Test: `Assets/Tests/EditMode/Hud/TerritoryReadoutTests.cs`, `TierUpEdgeTests.cs`, `ToastFadeTests.cs` (+ `.meta` each), `Assets/Tests/EditMode/Hud/BuffTierVisualTests.cs`

**Interfaces:**
- Consumes: `Game.Combat.Core.TerritorialCombat.InEnemyThird(float)`, `.DealtMultiplier(float, int)`.
- Produces:
  - `enum Game.Hud.Core.TerritoryDisplay { Clear, Penalised, Lifted }`
  - `TerritoryReadout.Resolve(float territorialAdvantage, int vanguardTier) -> TerritoryDisplay`
  - `struct Game.Hud.Core.TierUpEdge` with `bool Observe(int tier)` and `void Reset()`
  - `ToastFade.Alpha01(float elapsed, float holdSeconds, float fadeSeconds) -> float`
  - `BuffTierVisual.PipFilled(int pipIndex, int tier) -> bool`

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/Hud/TerritoryReadoutTests.cs`:

```csharp
using NUnit.Framework;
using Game.Hud.Core;

/// <summary>
/// The zone indicator's displayed state folds in the team's Vanguard tier: the same position
/// stops reading as penalised once the team has bought the debuff away. That fold is the whole
/// point of the merged Team Power strip — the buff is taught by the thing it changes.
/// </summary>
public class TerritoryReadoutTests
{
    [Test]
    public void OwnHalfAndMidfieldAreClearAtEveryTier()
    {
        Assert.AreEqual(TerritoryDisplay.Clear, TerritoryReadout.Resolve(1f, 0), "own base");
        Assert.AreEqual(TerritoryDisplay.Clear, TerritoryReadout.Resolve(0f, 0), "midpoint");
        Assert.AreEqual(TerritoryDisplay.Clear, TerritoryReadout.Resolve(-0.32f, 0), "just outside the enemy third");
        Assert.AreEqual(TerritoryDisplay.Clear, TerritoryReadout.Resolve(0f, 2), "clear stays clear when maxed");
    }

    [Test]
    public void TheBoundaryItselfIsClear_AndJustPastItIsPenalised()
    {
        Assert.AreEqual(TerritoryDisplay.Clear, TerritoryReadout.Resolve(-0.33f, 0), "boundary is not the enemy third");
        Assert.AreEqual(TerritoryDisplay.Penalised, TerritoryReadout.Resolve(-0.34f, 0));
    }

    [Test]
    public void EnemyThirdReadsPenalisedUntilVanguardIsMaxed()
    {
        Assert.AreEqual(TerritoryDisplay.Penalised, TerritoryReadout.Resolve(-1f, 0), "locked");
        Assert.AreEqual(TerritoryDisplay.Penalised, TerritoryReadout.Resolve(-1f, 1), "half lifted is still a penalty");
        Assert.AreEqual(TerritoryDisplay.Lifted, TerritoryReadout.Resolve(-1f, 2), "fully lifted");
    }

    [Test]
    public void TiersBeyondTheMaximumStillReadAsLifted()
    {
        Assert.AreEqual(TerritoryDisplay.Lifted, TerritoryReadout.Resolve(-1f, 5));
        Assert.AreEqual(TerritoryDisplay.Penalised, TerritoryReadout.Resolve(-1f, -1), "negative clamps to locked");
    }
}
```

Create `Assets/Tests/EditMode/Hud/TierUpEdgeTests.cs`:

```csharp
using NUnit.Framework;
using Game.Hud.Core;

/// <summary>
/// Toasts fire on a genuine tier-up only. Detection is client-side, so it must survive the two
/// ways a client legitimately sees a tier "appear": the first paint after binding, and a late
/// joiner receiving mid-match state.
/// </summary>
public class TierUpEdgeTests
{
    [Test]
    public void TheFirstObservationNeverFires()
    {
        var edge = new TierUpEdge();
        Assert.IsFalse(edge.Observe(0), "bind at tier 0");
    }

    [Test]
    public void ALateJoinerAtAHighTierDoesNotToastOnArrival()
    {
        var edge = new TierUpEdge();
        Assert.IsFalse(edge.Observe(3), "first paint already at tier 3");
        Assert.IsFalse(edge.Observe(3), "repaint at the same tier");
    }

    [Test]
    public void ARiseFires_Once()
    {
        var edge = new TierUpEdge();
        edge.Observe(0);
        Assert.IsTrue(edge.Observe(1));
        Assert.IsFalse(edge.Observe(1), "a repaint at the same tier is not a new unlock");
        Assert.IsTrue(edge.Observe(2));
    }

    [Test]
    public void AJumpOfSeveralTiersFiresOnce()
    {
        var edge = new TierUpEdge();
        edge.Observe(0);
        Assert.IsTrue(edge.Observe(3), "a big deposit crossing several steps is one moment");
    }

    [Test]
    public void AFallNeverFires()
    {
        var edge = new TierUpEdge();
        edge.Observe(3);
        Assert.IsFalse(edge.Observe(0), "leaving Sudden Death / rematch reset");
        Assert.IsTrue(edge.Observe(1), "and the next genuine rise still fires");
    }

    [Test]
    public void ResetReprimes()
    {
        var edge = new TierUpEdge();
        edge.Observe(0);
        edge.Reset();
        Assert.IsFalse(edge.Observe(3), "after Unbind/rebind the first paint is silent again");
    }
}
```

Create `Assets/Tests/EditMode/Hud/ToastFadeTests.cs`:

```csharp
using NUnit.Framework;
using Game.Hud.Core;

public class ToastFadeTests
{
    [Test]
    public void FullyOpaqueThroughTheHold()
    {
        Assert.AreEqual(1f, ToastFade.Alpha01(0f, 2f, 0.5f), 1e-4f);
        Assert.AreEqual(1f, ToastFade.Alpha01(2f, 2f, 0.5f), 1e-4f, "the hold boundary is still opaque");
    }

    [Test]
    public void FadesLinearlyThenStaysAtZero()
    {
        Assert.AreEqual(0.5f, ToastFade.Alpha01(2.25f, 2f, 0.5f), 1e-4f);
        Assert.AreEqual(0f, ToastFade.Alpha01(2.5f, 2f, 0.5f), 1e-4f);
        Assert.AreEqual(0f, ToastFade.Alpha01(99f, 2f, 0.5f), 1e-4f);
    }

    [Test]
    public void AZeroFadeCutsStraightToInvisible()
    {
        Assert.AreEqual(1f, ToastFade.Alpha01(2f, 2f, 0f), 1e-4f);
        Assert.AreEqual(0f, ToastFade.Alpha01(2.01f, 2f, 0f), 1e-4f);
    }
}
```

Append to `Assets/Tests/EditMode/Hud/BuffTierVisualTests.cs` (inside the existing class):

```csharp
    [Test]
    public void PipFilled_FillsExactlyTierPips()
    {
        Assert.IsFalse(BuffTierVisual.PipFilled(0, 0), "tier 0 fills nothing");
        Assert.IsTrue(BuffTierVisual.PipFilled(0, 1));
        Assert.IsFalse(BuffTierVisual.PipFilled(1, 1));
        Assert.IsTrue(BuffTierVisual.PipFilled(2, 3), "top pip at max tier");
        Assert.IsFalse(BuffTierVisual.PipFilled(-1, 3), "negative index is never filled");
    }
```

Create the three `.meta` files from the template.

- [ ] **Step 2: Run the tests to verify they fail**

Expected: FAIL — `TerritoryReadout`, `TierUpEdge`, `ToastFade`, `PipFilled` all undefined.

- [ ] **Step 3: Let `Game.Hud.Core` see `Game.Combat.Core`**

In `Assets/Scripts/Hud/Core/Game.Hud.Core.asmdef`, change `"references": []` to:

```json
    "references": [ "Game.Combat.Core" ],
```

Both assemblies are `noEngineReferences: true`, so this stays engine-free. The reference exists so the zone boundary and the Vanguard formula have exactly one definition — the HUD must never re-derive what combat uses.

- [ ] **Step 4: Write the implementations**

Create `Assets/Scripts/Hud/Core/TerritoryReadout.cs`:

```csharp
using Game.Combat.Core;

namespace Game.Hud.Core
{
    /// <summary>What the zone indicator should show for the local player right now.</summary>
    public enum TerritoryDisplay
    {
        /// <summary>Own half or midfield — no territorial tax applies here.</summary>
        Clear,
        /// <summary>Deep in the enemy third with the debuff still biting.</summary>
        Penalised,
        /// <summary>Deep in the enemy third, but the team's Vanguard has lifted the debuff entirely.</summary>
        Lifted
    }

    /// <summary>
    /// Pure zone-to-display mapping that FOLDS IN the team's Vanguard tier. Deliberately derived
    /// from TerritorialCombat.DealtMultiplier rather than re-deriving the thresholds: the indicator
    /// is penalised exactly when combat actually penalises you, so the display can never drift from
    /// the damage math.
    /// Engine-free (this asmdef sets noEngineReferences) so it is testable outside Unity.
    /// See docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md, "Merged Team Power strip".
    /// </summary>
    public static class TerritoryReadout
    {
        public static TerritoryDisplay Resolve(float territorialAdvantage, int vanguardTier)
        {
            if (!TerritorialCombat.InEnemyThird(territorialAdvantage)) return TerritoryDisplay.Clear;

            return TerritorialCombat.DealtMultiplier(territorialAdvantage, vanguardTier) >= 1f
                ? TerritoryDisplay.Lifted
                : TerritoryDisplay.Penalised;
        }
    }
}
```

Create `Assets/Scripts/Hud/Core/TierUpEdge.cs`:

```csharp
namespace Game.Hud.Core
{
    /// <summary>
    /// Client-side rising-edge detector for tier-ups. A tier-up is a discrete EVENT, but the only
    /// thing replicated is the state it derives from, so the client has to spot the edge itself
    /// inside its OnChangedRender repaint.
    ///
    /// It primes on its first observation and reports nothing for it. That is what stops a late
    /// joiner who arrives already at tier 3 from being greeted by three toasts — the same reason
    /// server-side UnityEvents were the wrong mechanism (they fire behind HasStateAuthority, so on
    /// a dedicated server they fire headless where no client can see them).
    /// Engine-free (this asmdef sets noEngineReferences) so it is testable outside Unity.
    /// </summary>
    public struct TierUpEdge
    {
        private int previous;
        private bool primed;

        /// <summary>Record the current tier; true only on a genuine rise after priming.</summary>
        public bool Observe(int tier)
        {
            if (!primed)
            {
                previous = tier;
                primed = true;
                return false;
            }

            bool rose = tier > previous;
            previous = tier;
            return rose;
        }

        /// <summary>Forget history so the next observation primes silently (call on Unbind).</summary>
        public void Reset()
        {
            previous = 0;
            primed = false;
        }
    }
}
```

Create `Assets/Scripts/Hud/Core/ToastFade.cs`:

```csharp
namespace Game.Hud.Core
{
    /// <summary>
    /// Pure hold-then-fade alpha for transient notifications: opaque through holdSeconds, then
    /// linear to 0 across fadeSeconds, then 0 forever. Callers drive it with their own elapsed
    /// time and set CanvasGroup.alpha.
    /// Engine-free (this asmdef sets noEngineReferences) so it is testable outside Unity.
    /// </summary>
    public static class ToastFade
    {
        public static float Alpha01(float elapsed, float holdSeconds, float fadeSeconds)
        {
            if (elapsed <= holdSeconds) return 1f;
            if (fadeSeconds <= 0f) return 0f;

            float t = (elapsed - holdSeconds) / fadeSeconds;
            if (t >= 1f) return 0f;
            return 1f - t;
        }
    }
}
```

In `Assets/Scripts/Hud/Core/BuffTierVisual.cs`, add inside the class:

```csharp
        /// <summary>
        /// Whether the pip at this index (0-based, ascending) is filled at the given tier. Pips
        /// make the tier readable EXACTLY, instead of inferred from a colour lerp.
        /// </summary>
        public static bool PipFilled(int pipIndex, int tier) => pipIndex >= 0 && pipIndex < tier;
```

Create the three new `.cs.meta` files from the template.

- [ ] **Step 5: Run the tests to verify they pass**

Expected: PASS — 4 territory cases, 6 edge cases, 3 fade cases, 1 pip case.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Hud/Core Assets/Tests/EditMode/Hud
git commit -m "feat(hud): pure zone readout, tier-up edge and toast fade math"
```

---

## Task 4: Sudden Death parity for the team layer

The spec's Sudden Death section requires that "every player has every individual buff at max tier **and both teams have every team buff at max tier**". Scope 1 implemented the individual half (`PlayerBuffs.TierOf` → `allUnlocked`) but `TeamScoreManager.VanguardTier` has no such override, so today the territorial debuff still bites during Sudden Death. Scope 4's Sudden Death display would otherwise have to either lie about the damage math or contradict the spec. Closing it here is a three-line read-time override that exactly mirrors the individual side — no new state, nothing to reset.

**Files:**
- Modify: `Assets/Scripts/Buffs/Core/TeamBuffUnlock.cs:32-37`
- Modify: `Assets/Scripts/Coin Scripts/TeamScoreManager.cs:212-233`
- Test: `Assets/Tests/EditMode/TeamBuffUnlockTests.cs`

**Interfaces:**
- Consumes: `BuffUnlock.ResolveTier(int, int, int, int, bool)` (already exists, added by Scope 1).
- Produces: `TeamBuffUnlock.TeamTier(IReadOnlyList<int> thresholds, int teamScore, int rosterSize, int maxTier, bool allUnlocked = false) -> int`. The existing 4-argument call sites keep compiling unchanged.

- [ ] **Step 1: Write the failing test**

Append to `Assets/Tests/EditMode/TeamBuffUnlockTests.cs` (inside the existing class):

```csharp
    [Test]
    public void SuddenDeath_ForcesVanguardToMaxRegardlessOfScore()
    {
        int[] thresholds = { 12, 45 };

        Assert.AreEqual(0, TeamBuffUnlock.TeamTier(thresholds, 0, 10, 2), "locked in normal play");
        Assert.AreEqual(2, TeamBuffUnlock.TeamTier(thresholds, 0, 10, 2, allUnlocked: true),
            "Sudden Death maxes a team that has banked nothing");
        Assert.AreEqual(2, TeamBuffUnlock.TeamTier(thresholds, 500, 10, 2, allUnlocked: true),
            "and does not exceed the max for a rich team");
    }

    [Test]
    public void SuddenDeath_MaxesEvenAnUncountedRoster()
    {
        int[] thresholds = { 12, 45 };

        Assert.AreEqual(0, TeamBuffUnlock.TeamTier(thresholds, 300, 0, 2),
            "roster of 0 has no average, so no tier in normal play");
        Assert.AreEqual(2, TeamBuffUnlock.TeamTier(thresholds, 300, 0, 2, allUnlocked: true),
            "Sudden Death does not care about the divisor");
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Expected: FAIL — no overload of `TeamTier` takes 5 arguments.

- [ ] **Step 3: Add the parameter**

In `Assets/Scripts/Buffs/Core/TeamBuffUnlock.cs`, replace `TeamTier` with:

```csharp
        /// <summary>
        /// Tier (0 = locked, up to maxTier) of the single team buff. Pure: same inputs, same tier,
        /// which is what keeps the team layer resimulation-safe with no stored tier state.
        /// allUnlocked (Sudden Death) forces maxTier, mirroring the individual layer's read-time
        /// override — applied at READ time by the caller's query, so no tier is ever stored.
        /// </summary>
        public static int TeamTier(IReadOnlyList<int> thresholds, int teamScore, int rosterSize,
                                   int maxTier, bool allUnlocked = false)
        {
            int average = PerPlayerAverage(teamScore, rosterSize);
            int steps = BuffUnlock.UnlockedSteps(thresholds, average);
            return BuffUnlock.ResolveTier(steps, priorityPosition: 0, buffCount: 1, maxTier: maxTier,
                                          allUnlocked: allUnlocked);
        }
```

- [ ] **Step 4: Wire it in `TeamScoreManager`**

In `Assets/Scripts/Coin Scripts/TeamScoreManager.cs`, replace the last line of `VanguardTier` with:

```csharp
        return TeamBuffUnlock.TeamTier(vanguardThresholds, score, roster, vanguardMaxTier,
                                       allUnlocked: SuddenDeathMaxesTiers);
```

and add below `VanguardTier`:

```csharp
    /// <summary>
    /// Sudden Death forces Vanguard to its top tier, matching PlayerBuffs.TierOf on the individual
    /// side. Derived from MatchManager's [Networked] Phase, so it resolves identically on clients
    /// and during resimulation, adds no state, and needs nothing reset when the phase ends.
    /// </summary>
    private bool SuddenDeathMaxesTiers =>
        MatchManager.Instance != null && MatchManager.Instance.AllBuffsMaxed;
```

- [ ] **Step 5: Raise `TeamBuffsChanged` when the phase changes**

The strip derives Vanguard from `Phase` now, so a phase change is a tier change. In `TeamScoreManager`, add the field and lifecycle hooks (mirroring `PlayerBuffs.subscribedMatchManager`):

```csharp
    // Cached at subscribe time: MatchManager.Instance can already be null during scene teardown,
    // so Despawned must unsubscribe via this reference rather than re-resolving the static.
    private MatchManager subscribedMatchManager;
```

At the end of `Spawned()`:

```csharp
        // VanguardTier now reads MatchManager.Phase as well as score, so a phase change is a tier
        // change too. PhaseChanged fires on every peer via OnChangedRender — no new networking.
        if (MatchManager.Instance != null)
        {
            subscribedMatchManager = MatchManager.Instance;
            subscribedMatchManager.PhaseChanged += OnTeamBuffsChanged;
        }
```

And add the override (the class has none today):

```csharp
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (subscribedMatchManager != null)
            subscribedMatchManager.PhaseChanged -= OnTeamBuffsChanged;
        subscribedMatchManager = null;
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Expected: PASS, including every pre-existing `TeamBuffUnlockTests` case (the default argument keeps them valid).

- [ ] **Step 7: Commit**

```bash
git add "Assets/Scripts/Buffs/Core/TeamBuffUnlock.cs" "Assets/Scripts/Coin Scripts/TeamScoreManager.cs" Assets/Tests/EditMode/TeamBuffUnlockTests.cs
git commit -m "fix(buffs): max Vanguard during Sudden Death, mirroring the individual layer"
```

---

## Task 5: Read-only accessors the surfaces need

No new networked state — every accessor derives from state that already replicates.

**Files:**
- Modify: `Assets/Scripts/Buffs/PlayerBuffs.cs`
- Modify: `Assets/Scripts/Coin Scripts/TeamScoreManager.cs`

**Interfaces:**
- Consumes: `Game.Buffs.Core.BuffProgress` (Task 2), `TeamBuffUnlock.PerPlayerAverage` (existing).
- Produces:
  - `PlayerBuffs.MaxTier -> int`
  - `PlayerBuffs.NextUnlockProgress01(BuffId) -> float`
  - `PlayerBuffs.NextUnlockThreshold(BuffId) -> int`
  - `TeamScoreManager.VanguardMaxTier -> int`
  - `TeamScoreManager.ScoreOf(Team) -> int`
  - `TeamScoreManager.RosterSizeOf(Team) -> int`
  - `TeamScoreManager.PerPlayerAverageOf(Team) -> int`
  - `TeamScoreManager.NextVanguardAverage(Team) -> int`
  - `TeamScoreManager.VanguardProgress01(Team) -> float`

- [ ] **Step 1: Add the `PlayerBuffs` accessors**

In `Assets/Scripts/Buffs/PlayerBuffs.cs`, add after `TierOf`:

```csharp
    /// <summary>Top tier any buff can reach, from the shared loadout config (0 if unconfigured).</summary>
    public int MaxTier => config != null ? config.MaxTier : 0;

    /// <summary>
    /// HUD fill 0..1 toward the deposit that next raises this buff's tier. 1 means "nothing left
    /// to earn" — already at the top of the curve, or Sudden Death has maxed everything.
    /// Read-only derivation from the same networked state TierOf uses.
    /// </summary>
    public float NextUnlockProgress01(BuffId id)
    {
        if (config == null || SuddenDeathMaxesTiers) return 1f;
        int pos = PositionOf(id);
        if (pos < 0) return 1f;
        return BuffProgress.ToNextTier01(config.Thresholds, TotalDepositedValue, pos, config.BuffCount);
    }

    /// <summary>
    /// Total deposited value at which this buff next tiers up; 0 when it can rise no further
    /// (top of the curve, not equipped, or Sudden Death).
    /// </summary>
    public int NextUnlockThreshold(BuffId id)
    {
        if (config == null || SuddenDeathMaxesTiers) return 0;
        int pos = PositionOf(id);
        if (pos < 0) return 0;
        return BuffProgress.NextThresholdFor(config.Thresholds, TotalDepositedValue, pos, config.BuffCount);
    }
```

- [ ] **Step 2: Add the `TeamScoreManager` accessors**

In `Assets/Scripts/Coin Scripts/TeamScoreManager.cs`, add after `VanguardTier`:

```csharp
    /// <summary>Vanguard's top tier, so the HUD can size its pip row without hard-coding 2.</summary>
    public int VanguardMaxTier => vanguardMaxTier;

    public int ScoreOf(Team team) =>
        team == Team.Team1 ? Team1Score : (team == Team.Team2 ? Team2Score : 0);

    public int RosterSizeOf(Team team) =>
        team == Team.Team1 ? Team1RosterSize : (team == Team.Team2 ? Team2RosterSize : 0);

    /// <summary>
    /// The team's deposited value PER PLAYER — the unit Vanguard thresholds are authored in, and
    /// therefore the only number the HUD should ever compare against them. The raw team score is
    /// not comparable across roster sizes.
    /// </summary>
    public int PerPlayerAverageOf(Team team) =>
        TeamBuffUnlock.PerPlayerAverage(ScoreOf(team), RosterSizeOf(team));

    /// <summary>Per-player average at which this team's Vanguard next tiers up; 0 when maxed.</summary>
    public int NextVanguardAverage(Team team) =>
        BuffProgress.NextThresholdFor(vanguardThresholds, PerPlayerAverageOf(team), 0, 1);

    /// <summary>HUD fill 0..1 toward the next Vanguard milestone; 1 when maxed.</summary>
    public float VanguardProgress01(Team team) =>
        BuffProgress.ToNextTier01(vanguardThresholds, PerPlayerAverageOf(team), 0, 1);
```

- [ ] **Step 3: Verify it compiles**

Run the Roslyn whole-surface compile gate (see "How to run tests"), or let the editor recompile.
Expected: no errors. `Game.Buffs.Core` is already imported by both files.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Buffs/PlayerBuffs.cs "Assets/Scripts/Coin Scripts/TeamScoreManager.cs"
git commit -m "feat(hud): expose derived unlock progress on both buff layers"
```

---

## Task 6: Unlock toasts + the individual buff row

**Files:**
- Create: `Assets/Scripts/Hud/HudToastFeed.cs` (+ `.meta`)
- Create: `Assets/Scripts/Hud/TierPipRow.cs` (+ `.meta`)
- Modify: `Assets/Scripts/Hud/BuffIconDisplay.cs`

**Interfaces:**
- Consumes: `ToastFade.Alpha01`, `TierUpEdge`, `BuffTierVisual.PipFilled`, `BuffTierVisual.Intensity01`, `PlayerBuffs.{TierOf, MaxTier, NextUnlockProgress01}`, `MatchManager.Instance.AllBuffsMaxed`.
- Produces:
  - `HudToastFeed.Show(string message)` — the shared notification entry point Task 7 also calls.
  - `TierPipRow.Paint(Image[] pips, int tier, int maxTier, Color filled, Color empty)` — the shared pip loop Task 7 also calls.

- [ ] **Step 1: Write `HudToastFeed`**

Create `Assets/Scripts/Hud/HudToastFeed.cs`:

```csharp
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Game.Hud.Core;

/// <summary>
/// One shared transient-notification surface for unlock moments, fed by the individual buff row
/// and the Team Power strip. Messages queue so two tier-ups in the same frame are both seen.
///
/// PURELY VISUAL: it is driven by Time.deltaTime in Update (render path), never by simulation, and
/// it decides nothing. Tier-ups are detected CLIENT-SIDE by the displays that call Show(), because
/// the server-side UnityEvents this replaces fired behind a HasStateAuthority guard — on a
/// dedicated server they fired headless where no client could ever observe them.
/// See docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md, "Unlock moments".
/// </summary>
public class HudToastFeed : MonoBehaviour
{
    [Tooltip("CanvasGroup faded by this feed. Usually on the same object as the label.")]
    [SerializeField] private CanvasGroup group;

    [SerializeField] private TMP_Text label;

    [Tooltip("Seconds fully opaque before the fade starts.")]
    [SerializeField] private float holdSeconds = 2f;

    [Tooltip("Seconds spent fading out.")]
    [SerializeField] private float fadeSeconds = 0.6f;

    private readonly Queue<string> pending = new Queue<string>();
    private float elapsed;
    private bool showing;

    /// <summary>Queue a message. Safe to call before Awake and from any display.</summary>
    public void Show(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        pending.Enqueue(message);
    }

    private void Awake()
    {
        if (group != null) group.alpha = 0f;
    }

    private void Update()
    {
        if (!showing)
        {
            if (pending.Count == 0) return;
            if (label != null) label.text = pending.Dequeue();
            else pending.Dequeue();
            elapsed = 0f;
            showing = true;
        }

        elapsed += Time.deltaTime;
        float alpha = ToastFade.Alpha01(elapsed, holdSeconds, fadeSeconds);
        if (group != null) group.alpha = alpha;
        if (alpha <= 0f) showing = false;
    }
}
```

Create `Assets/Scripts/Hud/HudToastFeed.cs.meta` from the template.

- [ ] **Step 2: Write the shared pip painter**

Both surfaces paint a pip row identically. Extract it once rather than duplicating the loop.

Create `Assets/Scripts/Hud/TierPipRow.cs`:

```csharp
using UnityEngine;
using UnityEngine.UI;
using Game.Hud.Core;

/// <summary>
/// Paints a row of tier pips: pips below the current tier are filled, the rest are empty, and
/// pips past the buff's max tier are hidden entirely. Shared by the individual buff row and the
/// Team Power strip so the two surfaces read identically and the loop exists in one place.
///
/// Lives in Assembly-CSharp rather than beside BuffTierVisual.PipFilled in Game.Hud.Core because
/// it touches UnityEngine.UI.Image and that assembly is noEngineReferences. The decision
/// (which pips are filled) stays pure there; only the painting is here.
/// </summary>
public static class TierPipRow
{
    public static void Paint(Image[] pips, int tier, int maxTier, Color filled, Color empty)
    {
        if (pips == null) return;

        for (int i = 0; i < pips.Length; i++)
        {
            if (pips[i] == null) continue;
            pips[i].gameObject.SetActive(i < maxTier);
            pips[i].color = BuffTierVisual.PipFilled(i, tier) ? filled : empty;
        }
    }
}
```

Create `Assets/Scripts/Hud/TierPipRow.cs.meta` from the template.

- [ ] **Step 3: Extend `BuffIconDisplay`**

Replace `Assets/Scripts/Hud/BuffIconDisplay.cs` with:

```csharp
using UnityEngine;
using UnityEngine.UI;
using Game.Hud.Core;
using Game.Buffs.Core;

/// <summary>
/// One buff icon. Tier drives colour/glow AND an exact pip row; a progress fill shows how close
/// the next tier is, so a player can see what a deposit run is worth before making it. Active
/// abilities (dash, stealth) keep their per-frame radial cooldown sweep.
///
/// Everything repaints off PlayerBuffs.BuffsChanged (which Scope 1 also raises on phase changes),
/// never by polling. Tier-ups are detected client-side here, inside that repaint.
/// </summary>
public class BuffIconDisplay : MonoBehaviour, IHudBindable
{
    [Header("Identity")]
    [SerializeField] private BuffId buffId;

    [Tooltip("Fallback max tier used only if the loadout config is unavailable.")]
    [SerializeField] private int maxTier = 3;

    [Tooltip("Name used in the unlock toast, e.g. \"Flag Runner\".")]
    [SerializeField] private string displayName = "Buff";

    [Header("Icon color/glow")]
    [Tooltip("Main icon image whose color is lerped by tier.")]
    [SerializeField] private Image icon;
    [SerializeField] private Color lockedColor = new Color(1f, 1f, 1f, 0.25f);
    [SerializeField] private Color accentColor = Color.yellow;

    [Header("Tier pips (index 0 = tier 1). Exact tier, not inferred from colour.")]
    [SerializeField] private Image[] pips;
    [SerializeField] private Color pipFilledColor = Color.white;
    [SerializeField] private Color pipEmptyColor = new Color(1f, 1f, 1f, 0.18f);

    [Header("Next-unlock progress")]
    [Tooltip("Image Type = Filled. fillAmount tracks progress toward this buff's next tier.")]
    [SerializeField] private Image nextUnlockFill;

    [Header("Cooldown radial (dash / stealth only)")]
    [Tooltip("Image Type = Filled, Radial. fillAmount 1 = ready. Leave null for passive buffs.")]
    [SerializeField] private Image cooldownRadial;

    [Header("Unlock toast")]
    [SerializeField] private HudToastFeed toastFeed;

    private PlayerBuffs buffs;
    private PlayerMovement movement;
    private TierUpEdge tierEdge;

    public void Bind(HudContext ctx)
    {
        buffs = ctx.Buffs;
        movement = ctx.Inventory != null ? ctx.Inventory.GetComponent<PlayerMovement>() : null;
        if (buffs != null)
        {
            buffs.BuffsChanged += RepaintTier;
            buffs.StealthStateChanged += RepaintTier;
        }
        // The first RepaintTier primes the edge detector, so binding (and a late joiner arriving
        // already at tier 3) never toasts.
        RepaintTier();
    }

    public void Unbind()
    {
        if (buffs != null)
        {
            buffs.BuffsChanged -= RepaintTier;
            buffs.StealthStateChanged -= RepaintTier;
        }
        buffs = null;
        movement = null;
        tierEdge.Reset();
    }

    private void RepaintTier()
    {
        if (buffs == null) return;

        int tier = buffs.TierOf(buffId);
        int max = buffs.MaxTier > 0 ? buffs.MaxTier : maxTier;

        if (icon != null)
            icon.color = Color.Lerp(lockedColor, accentColor, BuffTierVisual.Intensity01(tier, max));

        TierPipRow.Paint(pips, tier, max, pipFilledColor, pipEmptyColor);

        if (nextUnlockFill != null)
            nextUnlockFill.fillAmount = buffs.NextUnlockProgress01(buffId);

        // Sudden Death maxes every tier at once; the banner announces that, so a burst of four
        // toasts would be noise rather than information.
        bool suddenDeath = MatchManager.Instance != null && MatchManager.Instance.AllBuffsMaxed;
        if (tierEdge.Observe(tier) && !suddenDeath && toastFeed != null)
            toastFeed.Show($"{displayName}  T{tier}");
    }

    private void Update()
    {
        if (buffs == null || cooldownRadial == null) return;

        if (buffId == BuffId.QuickerDash && movement != null)
            cooldownRadial.fillAmount = movement.GetDashCooldownPercent();
        else if (buffId == BuffId.Stealth)
            cooldownRadial.fillAmount = buffs.StealthCooldownFill01();
    }

    private void OnDisable() => Unbind();
}
```

- [ ] **Step 4: Verify it compiles**

Run the compile gate.
Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hud/HudToastFeed.cs Assets/Scripts/Hud/HudToastFeed.cs.meta Assets/Scripts/Hud/TierPipRow.cs Assets/Scripts/Hud/TierPipRow.cs.meta Assets/Scripts/Hud/BuffIconDisplay.cs
git commit -m "feat(hud): tier pips, next-unlock fill and unlock toasts on the buff row"
```

---

## Task 7: The merged Team Power strip

Territory and team buffs are one subject — how strong is my team's position right now — so they share one readout. The zone indicator's displayed state already folds in Vanguard, so a player pushing into enemy turf watches it stop reading as penalised the moment the team unlocks the buff.

**Note on the "next-milestone tick":** the spec asks for a tick on the score bar. Team score has no fixed maximum, so an absolute bar has no scale to place a tick on. This implements the same information as a *progress-to-next-milestone* fill plus a label naming the milestone in its real unit ("NEXT 45 avg"), which is the number the tick would have marked.

**Files:**
- Modify: `Assets/Scripts/Hud/TeamScoreDisplay.cs` (full rewrite, same class name)

**Interfaces:**
- Consumes: `TerritoryReadout.Resolve`, `TierUpEdge`, `TierPipRow.Paint`, `HudToastFeed.Show`, `TeamManager.Instance.GetTerritorialAdvantage(Team, Vector2)`, `TeamScoreManager.{ScoresChanged, TeamBuffsChanged, Team1Score, Team2Score, VanguardTier, VanguardMaxTier, PerPlayerAverageOf, NextVanguardAverage, VanguardProgress01}`.

- [ ] **Step 1: Rewrite the display**

Replace `Assets/Scripts/Hud/TeamScoreDisplay.cs` with:

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Game.Hud.Core;

/// <summary>
/// The merged Team Power strip: team scores, Vanguard's tier and next milestone, and the
/// territory zone indicator — one surface, because they are one subject (how strong is my team's
/// position right now).
///
/// The zone indicator's displayed state FOLDS IN the unlocked Vanguard tier: a player pushing into
/// enemy turf watches it stop reading as penalised once the team buys the debuff away, and learns
/// the buff from the thing it changes. Kept as TeamScoreDisplay (not renamed) so the component
/// already wired into the Gameplay scene keeps its score-text references.
///
/// Event-driven off TeamScoreManager + MatchManager; both are runtime singletons, so subscription
/// is deferred until Instance exists. The only per-frame work is sampling the LOCAL player's
/// position (positions are not events) and comparing the resulting band — repaints happen on band
/// CHANGE only, so with two discrete states this costs nothing at 20 players.
/// See docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md, "Feedback surfaces".
/// </summary>
public class TeamScoreDisplay : MonoBehaviour, IHudBindable
{
    [Header("Scores")]
    [SerializeField] private TextMeshProUGUI team1ScoreText;
    [SerializeField] private TextMeshProUGUI team2ScoreText;

    [Tooltip("Label prefixed before each team's score, e.g. \"BLUE\" / \"RED\".")]
    [SerializeField] private string team1Label = "BLUE";
    [SerializeField] private string team2Label = "RED";

    [Header("Vanguard (this player's team)")]
    [Tooltip("Tier pips, index 0 = tier 1. Vanguard has two tiers.")]
    [SerializeField] private Image[] vanguardPips;
    [SerializeField] private Color pipFilledColor = new Color(1f, 0.86f, 0.40f);
    [SerializeField] private Color pipEmptyColor = new Color(1f, 1f, 1f, 0.18f);

    [Tooltip("Image Type = Filled. Progress toward the next Vanguard milestone.")]
    [SerializeField] private Image vanguardProgressFill;

    [Tooltip("Names the next milestone in its real unit (per-player average deposited value).")]
    [SerializeField] private TextMeshProUGUI vanguardMilestoneText;

    [Header("Zone indicator")]
    [SerializeField] private Image zoneIcon;
    [SerializeField] private TextMeshProUGUI zoneText;
    [SerializeField] private Color zoneClearColor = new Color(0.62f, 0.68f, 0.78f);
    [SerializeField] private Color zonePenalisedColor = new Color(0.90f, 0.35f, 0.30f);
    [SerializeField] private Color zoneLiftedColor = new Color(0.35f, 0.85f, 0.50f);

    [Header("Unlock toast")]
    [SerializeField] private HudToastFeed toastFeed;

    private Team localTeam = Team.None;
    private Transform localPlayer;

    private TeamScoreManager scoreManager;
    private MatchManager matchManager;

    private TierUpEdge vanguardEdge;
    private TerritoryDisplay zone = TerritoryDisplay.Clear;
    private bool zonePainted;

    public void Bind(HudContext ctx)
    {
        localTeam = ctx.Team != null ? ctx.Team.Team : Team.None;
        localPlayer = ctx.Inventory != null ? ctx.Inventory.transform : null;
        zonePainted = false;
        // Manager subscriptions happen lazily in Update once the singletons are live.
    }

    public void Unbind()
    {
        if (scoreManager != null)
        {
            scoreManager.ScoresChanged -= RepaintScores;
            scoreManager.TeamBuffsChanged -= RepaintVanguard;
        }
        if (matchManager != null) matchManager.PhaseChanged -= RepaintVanguard;

        scoreManager = null;
        matchManager = null;
        localPlayer = null;
        vanguardEdge.Reset();
    }

    private void Update()
    {
        if (scoreManager == null)
        {
            TeamScoreManager mgr = TeamScoreManager.Instance;
            if (mgr != null && mgr.Object != null && mgr.Object.IsValid)
            {
                scoreManager = mgr;
                scoreManager.ScoresChanged += RepaintScores;
                scoreManager.TeamBuffsChanged += RepaintVanguard;
                RepaintScores();
                // Primes the edge detector, so joining mid-match never toasts.
                RepaintVanguard();
            }
        }

        if (matchManager == null && MatchManager.Instance != null)
        {
            matchManager = MatchManager.Instance;
            matchManager.PhaseChanged += RepaintVanguard;
        }
    }

    /// <summary>
    /// Sample the local player's own position and repaint the zone only when the band changes.
    /// Position is the one value here that is not event-driven, so it is read on the render path;
    /// the band it maps to changes rarely, and everything downstream is change-gated.
    /// </summary>
    private void LateUpdate()
    {
        if (localPlayer == null || localTeam == Team.None) return;

        TeamManager teams = TeamManager.Instance;
        if (teams == null) return;

        int tier = scoreManager != null ? scoreManager.VanguardTier(localTeam) : 0;
        float advantage = teams.GetTerritorialAdvantage(localTeam, localPlayer.position);
        TerritoryDisplay next = TerritoryReadout.Resolve(advantage, tier);

        if (zonePainted && next == zone) return;
        zone = next;
        zonePainted = true;
        RepaintZone();
    }

    private void RepaintScores()
    {
        if (scoreManager == null) return;
        if (team1ScoreText != null) team1ScoreText.text = $"{team1Label}  {scoreManager.Team1Score}";
        if (team2ScoreText != null) team2ScoreText.text = $"{team2Label}  {scoreManager.Team2Score}";
    }

    private void RepaintVanguard()
    {
        if (scoreManager == null || localTeam == Team.None) return;

        int tier = scoreManager.VanguardTier(localTeam);
        int max = scoreManager.VanguardMaxTier;

        TierPipRow.Paint(vanguardPips, tier, max, pipFilledColor, pipEmptyColor);

        if (vanguardProgressFill != null)
            vanguardProgressFill.fillAmount = scoreManager.VanguardProgress01(localTeam);

        if (vanguardMilestoneText != null)
        {
            int next = scoreManager.NextVanguardAverage(localTeam);
            vanguardMilestoneText.text = next > 0
                ? $"VANGUARD T{tier}   {scoreManager.PerPlayerAverageOf(localTeam)}/{next} avg"
                : $"VANGUARD T{tier}   MAX";
        }

        // Sudden Death maxes Vanguard for both teams at once; its banner announces that.
        bool suddenDeath = matchManager != null && matchManager.AllBuffsMaxed;
        if (vanguardEdge.Observe(tier) && !suddenDeath && toastFeed != null)
            toastFeed.Show($"VANGUARD  T{tier}");

        // The zone's displayed meaning folds in the tier, so force a re-evaluation next frame.
        zonePainted = false;
    }

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

    private void OnDisable() => Unbind();
}
```

- [ ] **Step 2: Verify it compiles**

Run the compile gate.
Expected: no errors. Note `teams.GetTerritorialAdvantage` takes a `Vector2`; `localPlayer.position` is a `Vector3` and converts implicitly.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hud/TeamScoreDisplay.cs
git commit -m "feat(hud): merged Team Power strip with the Vanguard-folded zone indicator"
```

---

## Task 8: Sudden Death banner

**Files:**
- Modify: `Assets/Scripts/Hud/MatchPhaseHud.cs:22-27`, `:58-75`, `:83-92`, `:112-117`

**Interfaces:**
- Consumes: `MatchPhase.SuddenDeath`, `MatchManager.PhaseTimeRemaining`.
- Produces: a `suddenDeathRoot` serialized field for Task 9's builder to wire.

- [ ] **Step 1: Add the banner field**

In `Assets/Scripts/Hud/MatchPhaseHud.cs`, after the "Live match timer" block:

```csharp
    [Header("Sudden Death banner")]
    [Tooltip("Shown for the whole SuddenDeath phase. The buff row and Team Power strip need no " +
             "special case — they derive maxed tiers from Phase like everything else.")]
    [SerializeField] private GameObject suddenDeathRoot;
```

- [ ] **Step 2: Show the timer in Sudden Death when the ops hard cap is armed**

In `LateUpdate`, change the `case MatchPhase.Live:` label to cover both phases:

```csharp
            case MatchPhase.Live:
            case MatchPhase.SuddenDeath:
                if (matchTimerRoot != null) matchTimerRoot.SetActive(remaining.HasValue);
                if (remaining.HasValue && matchTimerText != null)
                    matchTimerText.text = FormatClock(remaining.Value);
                break;
```

Sudden Death normally arms no timer (`suddenDeathHardCap` defaults to 0 = off), so `remaining` is null and the clock stays hidden. When an operator arms the cap, the countdown to the draw is visible instead of silently ticking.

- [ ] **Step 3: Toggle the banner in `Render`**

In `Render()`, replace the `matchTimerRoot` line and add the banner:

```csharp
        if (matchTimerRoot != null)
            matchTimerRoot.SetActive(
                (phase == MatchPhase.Live || phase == MatchPhase.SuddenDeath) &&
                bound.PhaseTimeRemaining.HasValue);

        if (suddenDeathRoot != null) suddenDeathRoot.SetActive(phase == MatchPhase.SuddenDeath);
```

- [ ] **Step 4: Hide it with the rest**

In `HideAll()`:

```csharp
        if (suddenDeathRoot != null) suddenDeathRoot.SetActive(false);
```

- [ ] **Step 5: Verify it compiles**

Run the compile gate.
Expected: no errors.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Hud/MatchPhaseHud.cs
git commit -m "feat(hud): Sudden Death banner and hard-cap countdown"
```

---

## Task 9: One-click builder for the new HUD objects

Everything above is inert until someone creates and wires the UI objects. This mirrors `MatchHudBuilder` exactly: Unity API (not raw scene YAML), so it is safe with the editor open, idempotent, and undo-friendly.

**Files:**
- Create: `Assets/Scripts/Editor/EconomyHudBuilder.cs` (+ `.meta`)

**Interfaces:**
- Consumes: the serialized field names introduced in Tasks 6-8 — `TeamScoreDisplay.{vanguardPips, vanguardProgressFill, vanguardMilestoneText, zoneIcon, zoneText, toastFeed}`, `BuffIconDisplay.{pips, nextUnlockFill, toastFeed, displayName}`, `MatchPhaseHud.suddenDeathRoot`, `HudToastFeed.{group, label}`.

- [ ] **Step 1: Write the builder**

Create `Assets/Scripts/Editor/EconomyHudBuilder.cs`:

```csharp
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-click builder for the Scope 4 economy HUD: the shared unlock-toast feed, the Team Power
/// strip's Vanguard pips / progress / zone indicator, per-icon tier pips and next-unlock fills on
/// every BuffIconDisplay, and the Sudden Death banner. Finds the existing HUD components in the
/// open scene and wires their private [SerializeField] references via SerializedObject.
///
/// Safe with the editor open (Unity API, not raw scene YAML), re-runnable (it rebuilds only the
/// containers it owns, by name) and undo-friendly. Mirrors MatchHudBuilder.
/// </summary>
public static class EconomyHudBuilder
{
    private const string UndoLabel = "Build Economy HUD";

    [MenuItem("Tools/Economy/Build Economy HUD")]
    public static void Build()
    {
        var canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Economy HUD Builder",
                "No Canvas found in the open scene.", "OK");
            return;
        }

        HudToastFeed feed = BuildToastFeed(canvas);
        int icons = BuildBuffIcons(feed);
        bool strip = BuildTeamPowerStrip(feed);
        bool banner = BuildSuddenDeathBanner(canvas);

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Debug.Log($"[Economy] HUD built: toast feed ✔, {icons} buff icon(s) extended, " +
                  $"Team Power strip {(strip ? "✔" : "SKIPPED — no TeamScoreDisplay in scene")}, " +
                  $"Sudden Death banner {(banner ? "✔" : "SKIPPED — no MatchPhaseHud in scene")}. " +
                  $"Save the scene (Ctrl+S).");
    }

    private static HudToastFeed BuildToastFeed(Canvas canvas)
    {
        var existing = Object.FindFirstObjectByType<HudToastFeed>(FindObjectsInactive.Include);
        if (existing != null) return existing;

        var go = new GameObject("UnlockToast", typeof(RectTransform), typeof(CanvasGroup));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(canvas.transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(680f, 70f);
        rt.anchoredPosition = new Vector2(0f, -160f);

        var label = MakeText("UnlockToastLabel", go.transform, 34, new Color(1f, 0.86f, 0.40f),
            Vector2.zero, new Vector2(680f, 70f), "EXTRA JUMP  T2");
        label.fontStyle = FontStyles.Bold;

        var feed = Undo.AddComponent<HudToastFeed>(go);
        var so = new SerializedObject(feed);
        so.FindProperty("group").objectReferenceValue = go.GetComponent<CanvasGroup>();
        so.FindProperty("label").objectReferenceValue = label;
        so.ApplyModifiedProperties();

        go.GetComponent<CanvasGroup>().alpha = 0f;
        return feed;
    }

    private static int BuildBuffIcons(HudToastFeed feed)
    {
        var icons = Object.FindObjectsByType<BuffIconDisplay>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var icon in icons)
        {
            Undo.RecordObject(icon, UndoLabel);
            var so = new SerializedObject(icon);

            var pips = BuildPipRow(icon.transform, 3, new Vector2(0f, -34f), 14f);
            var pipsProp = so.FindProperty("pips");
            pipsProp.arraySize = pips.Length;
            for (int i = 0; i < pips.Length; i++)
                pipsProp.GetArrayElementAtIndex(i).objectReferenceValue = pips[i];

            var fill = BuildBar("NextUnlockFill", icon.transform, new Vector2(0f, -48f),
                new Vector2(64f, 6f), new Color(0.35f, 0.65f, 1f));
            so.FindProperty("nextUnlockFill").objectReferenceValue = fill;
            so.FindProperty("toastFeed").objectReferenceValue = feed;
            so.ApplyModifiedProperties();
        }

        return icons.Length;
    }

    private static bool BuildTeamPowerStrip(HudToastFeed feed)
    {
        var strip = Object.FindFirstObjectByType<TeamScoreDisplay>(FindObjectsInactive.Include);
        if (strip == null) return false;

        Undo.RecordObject(strip, UndoLabel);
        var so = new SerializedObject(strip);

        var pips = BuildPipRow(strip.transform, 2, new Vector2(0f, -30f), 18f);
        var pipsProp = so.FindProperty("vanguardPips");
        pipsProp.arraySize = pips.Length;
        for (int i = 0; i < pips.Length; i++)
            pipsProp.GetArrayElementAtIndex(i).objectReferenceValue = pips[i];

        var fill = BuildBar("VanguardProgressFill", strip.transform, new Vector2(0f, -46f),
            new Vector2(220f, 8f), new Color(1f, 0.86f, 0.40f));
        so.FindProperty("vanguardProgressFill").objectReferenceValue = fill;

        var milestone = MakeText("VanguardMilestoneText", strip.transform, 20,
            new Color(0.80f, 0.83f, 0.90f), new Vector2(0f, -66f), new Vector2(320f, 28f),
            "VANGUARD T0   0/12 avg");
        so.FindProperty("vanguardMilestoneText").objectReferenceValue = milestone;

        var zoneGo = Rebuild("ZoneIndicator", strip.transform, new Vector2(0f, -96f),
            new Vector2(24f, 24f));
        var zoneIcon = Undo.AddComponent<Image>(zoneGo);
        zoneIcon.color = new Color(0.62f, 0.68f, 0.78f);
        so.FindProperty("zoneIcon").objectReferenceValue = zoneIcon;

        var zoneText = MakeText("ZoneText", strip.transform, 20, new Color(0.62f, 0.68f, 0.78f),
            new Vector2(0f, -124f), new Vector2(360f, 28f), "OWN TERRITORY");
        so.FindProperty("zoneText").objectReferenceValue = zoneText;

        so.FindProperty("toastFeed").objectReferenceValue = feed;
        so.ApplyModifiedProperties();
        return true;
    }

    private static bool BuildSuddenDeathBanner(Canvas canvas)
    {
        var hud = Object.FindFirstObjectByType<MatchPhaseHud>(FindObjectsInactive.Include);
        if (hud == null) return false;

        Undo.RecordObject(hud, UndoLabel);
        var so = new SerializedObject(hud);

        var banner = Rebuild("SuddenDeathBanner", canvas.transform, new Vector2(0f, -70f),
            new Vector2(900f, 64f));
        var bg = Undo.AddComponent<Image>(banner);
        bg.color = new Color(0.55f, 0.06f, 0.10f, 0.92f);

        var rt = banner.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);

        var label = MakeText("SuddenDeathText", banner.transform, 30, Color.white,
            Vector2.zero, new Vector2(900f, 64f),
            "SUDDEN DEATH · all buffs unlocked · next capture wins");
        label.fontStyle = FontStyles.Bold;

        so.FindProperty("suddenDeathRoot").objectReferenceValue = banner;
        so.ApplyModifiedProperties();

        banner.SetActive(false); // MatchPhaseHud.Awake hides it anyway; keep the scene tidy.
        return true;
    }

    // ---- primitives ----

    private static GameObject Rebuild(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var old = parent.Find(name);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return go;
    }

    private static Image[] BuildPipRow(Transform parent, int count, Vector2 origin, float spacing)
    {
        var row = Rebuild("TierPips", parent, origin, new Vector2(spacing * count, spacing));
        var pips = new Image[count];

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"Pip{i + 1}", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, UndoLabel);
            go.transform.SetParent(row.transform, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(spacing * 0.6f, spacing * 0.6f);
            rt.anchoredPosition = new Vector2((i - (count - 1) * 0.5f) * spacing, 0f);

            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.18f);
            img.raycastTarget = false;
            pips[i] = img;
        }

        return pips;
    }

    private static Image BuildBar(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
    {
        var track = Rebuild(name + "Track", parent, pos, size);
        var trackImg = track.AddComponent<Image>();
        trackImg.color = new Color(1f, 1f, 1f, 0.12f);
        trackImg.raycastTarget = false;

        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(track.transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillAmount = 0f;
        return img;
    }

    private static TextMeshProUGUI MakeText(string name, Transform parent, int fontSize,
        Color color, Vector2 anchoredPos, Vector2 size, string sample)
    {
        var old = parent.Find(name);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = fontSize;
        t.alignment = TextAlignmentOptions.Center;
        t.color = color;
        t.text = sample;
        t.raycastTarget = false;

        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
        return t;
    }
}
```

Create `Assets/Scripts/Editor/EconomyHudBuilder.cs.meta` from the template.

- [ ] **Step 2: Verify it compiles**

The editor assembly is excluded from the Roslyn gate's source list, so add it explicitly (reference `UnityEditor.dll` alongside the `UnityEngine` modules), or let the Unity editor compile it.
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Editor/EconomyHudBuilder.cs Assets/Scripts/Editor/EconomyHudBuilder.cs.meta
git commit -m "feat(editor): one-click builder for the economy HUD surfaces"
```

---

## Task 10: Verification pass

- [ ] **Step 1: Run the whole EditMode suite**

Run: **Test Runner ▸ EditMode ▸ Run All** (or the Roslyn harness for the engine-free assemblies).
Expected: PASS, including every pre-existing test. Record the actual counts.

- [ ] **Step 2: Run the whole-surface compile gate**

Per "How to run tests". `Game.Buffs.Core` and `Game.Hud.Core` both changed, so compile their sources inline instead of referencing their stale `Library\ScriptAssemblies` DLLs.
Expected: no errors.

- [ ] **Step 3: Confirm nothing polls and nothing new is networked**

Run:

```bash
grep -rn "Networked" Assets/Scripts/Hud/
```

Expected: no output — the HUD holds no networked state.

- [ ] **Step 4: Write the manual in-editor checklist into the PR body**

These CANNOT be verified from code and must be reported as observed-vs-inferred:

1. Run `Tools ▸ Economy ▸ Build Economy HUD` on the Gameplay scene, save, and check the buff row and Team Power strip are laid out sensibly (the builder places primitives; positioning is a design pass).
2. Deposit coins and confirm pips fill and the next-unlock bar tracks, and that a toast fires **once** per tier-up rather than on every deposit.
3. Walk into the enemy third and confirm the zone indicator flips to penalised; then reach Vanguard T2 and confirm the same position now reads as clear **without moving**. This is the fold — the single most important thing to observe.
4. Let the match clock expire and confirm the Sudden Death banner appears, every pip fills, and the zone reads clear in the enemy third.
5. Join a second peer mid-match and confirm it gets the correct pips/fills/zone with **no** toast burst on arrival.
6. Kill one enemy of each colour and confirm the drop counts are exactly the authored numbers.
7. **Ring bonuses are dormant** until a `DifficultyRingConfig` asset is authored and assigned to `GameSettingsManager.difficultyRingConfig` — none exists in the repo today. Author it (bands INNER → OUTER with `coinDropBonus` +2 / +1 / +0) if the ring payout is wanted this pass.

- [ ] **Step 5: Push and open the PR**

```bash
git push -u origin feat/economy-feedback-surfaces
```
