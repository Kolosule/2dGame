# Player HUD Rebuild Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the polling `UIManager.cs` god-script with an event-driven, component-based in-match HUD (segmented health, coins, per-player buff tiers with cooldown radials, team score + team-buff badge) using a minimal visual language.

**Architecture:** Pure, engine-free presentation math lives in a new `Game.Hud.Core` assembly (unit-tested in EditMode, mirroring `Game.Combat.Core`). Networked source scripts expose plain C# change events (raised from Fusion `OnChangedRender` callbacks) so HUD components update only when data changes — no per-frame value polling. A `PlayerHud` root discovers the local player once (bounded, self-terminating), then binds each focused display component to its data source.

**Tech Stack:** Unity, Photon Fusion 2 (`NetworkBehaviour`, `[Networked]`, `OnChangedRender`, `TickTimer`), TextMeshPro, Unity UI (`Image`/filled radial), NUnit EditMode tests.

## Global Constraints

- Functional color coding only — no team-tinted HUD panels (red/white = health, gold = coins, distinct per-buff accent colors). Team color appears only in the flag HUD (untouched) and the team-score text.
- Flag HUD (directional arrows, carrier icon, notifications) and combat feedback (hit markers, damage numbers) are OUT OF SCOPE — do not modify their scripts. Only verify no visual clash at the end.
- The team-wide damage/defense buff mechanic (`TeamScoreManager`) is REUSED as-is — re-skin the indicator only, do not change unlock logic.
- Pure logic in `Game.Hud.Core` must be engine-free (`noEngineReferences: true`) — no `UnityEngine` types (no `Mathf`, no `Color`); return `int`/`float`/`bool` only, matching `Game.Combat.Core/FlashCurve.cs`.
- Buff tiers range 0..`config.MaxTier` (currently 3); tier 0 = locked. Buff ids: `Game.Buffs.Core.BuffId` = `ExtraJump`, `Stealth`, `QuickerDash`.
- Cooldown fill convention: `1` = ready, `0` = just used (matches `PlayerMovement.GetDashCooldownPercent()`).
- Run EditMode tests with Unity's bundled test runner. If the editor holds the project lock, compile/test outside Unity with its bundled Roslyn (see memory: unity-locked-verification-workaround). Never claim tests pass without running them.

---

## File Structure

**New — `Game.Hud.Core` assembly (engine-free pure logic):**
- `Assets/Scripts/Hud/Core/Game.Hud.Core.asmdef` — assembly def, `noEngineReferences: true`, `autoReferenced: true`
- `Assets/Scripts/Hud/Core/HealthSegments.cs` — segmented-bar math
- `Assets/Scripts/Hud/Core/BuffTierVisual.cs` — tier → glow intensity / locked
- `Assets/Scripts/Hud/Core/CooldownFill.cs` — remaining/total → 0..1 fill

**New — EditMode tests:**
- `Assets/Tests/EditMode/Hud/Game.Hud.Tests.asmdef`
- `Assets/Tests/EditMode/Hud/HealthSegmentsTests.cs`
- `Assets/Tests/EditMode/Hud/BuffTierVisualTests.cs`
- `Assets/Tests/EditMode/Hud/CooldownFillTests.cs`

**New — HUD MonoBehaviours (Assembly-CSharp, alongside existing gameplay scripts):**
- `Assets/Scripts/Hud/PlayerHud.cs` — root: discovers local player, binds displays
- `Assets/Scripts/Hud/HealthSegmentDisplay.cs`
- `Assets/Scripts/Hud/CoinDisplay.cs`
- `Assets/Scripts/Hud/BuffIconDisplay.cs` — one per buff (tier glow + cooldown radial)
- `Assets/Scripts/Hud/TeamScoreDisplay.cs` — score text + team-buff badge

**Modified — networked sources (add change events; no logic changes):**
- `Assets/Scripts/Player/PlayerStatsHandler.cs` — add `event Action HealthChanged`
- `Assets/Scripts/Coin Scripts/PlayerInventory.cs` — add `event Action CoinsChanged`
- `Assets/Scripts/Buffs/PlayerBuffs.cs` — add `event Action BuffsChanged`, `event Action StealthStateChanged`, `float StealthCooldownFill01()`
- `Assets/Scripts/Coin Scripts/TeamScoreManager.cs` — add `event Action ScoresChanged`, `event Action TeamBuffsChanged`

**Retired:**
- `Assets/Scripts/Coin Scripts/UIManager.cs` — deleted in the final task after the scene is rewired.

---

## Task 1: Hud.Core assembly + HealthSegments math

**Files:**
- Create: `Assets/Scripts/Hud/Core/Game.Hud.Core.asmdef`
- Create: `Assets/Scripts/Hud/Core/HealthSegments.cs`
- Create: `Assets/Tests/EditMode/Hud/Game.Hud.Tests.asmdef`
- Test: `Assets/Tests/EditMode/Hud/HealthSegmentsTests.cs`

**Interfaces:**
- Produces: `Game.Hud.Core.HealthSegments.FilledSegments(float current, float max, int segmentCount) → int` (fully-lit segment count), `Game.Hud.Core.HealthSegments.PartialFill01(float current, float max, int segmentCount) → float` (0..1 fill of the one partially-lit segment above the filled ones).

- [ ] **Step 1: Create the Core assembly definition**

Create `Assets/Scripts/Hud/Core/Game.Hud.Core.asmdef`:

```json
{
    "name": "Game.Hud.Core",
    "rootNamespace": "Game.Hud.Core",
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

- [ ] **Step 2: Create the test assembly definition**

Create `Assets/Tests/EditMode/Hud/Game.Hud.Tests.asmdef`:

```json
{
    "name": "Game.Hud.Tests",
    "rootNamespace": "",
    "references": [
        "Game.Hud.Core",
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

- [ ] **Step 3: Write the failing test**

Create `Assets/Tests/EditMode/Hud/HealthSegmentsTests.cs`:

```csharp
using NUnit.Framework;
using Game.Hud.Core;

public class HealthSegmentsTests
{
    [Test]
    public void FullHealth_AllSegmentsLit()
    {
        Assert.AreEqual(10, HealthSegments.FilledSegments(100f, 100f, 10));
    }

    [Test]
    public void ZeroHealth_NoSegmentsLit()
    {
        Assert.AreEqual(0, HealthSegments.FilledSegments(0f, 100f, 10));
    }

    [Test]
    public void HalfHealth_HalfSegmentsLit()
    {
        Assert.AreEqual(5, HealthSegments.FilledSegments(50f, 100f, 10));
    }

    [Test]
    public void PartialSegment_ReportsFractionalFill()
    {
        // 55/100 over 10 segments = 5 full + 0.5 of the 6th.
        Assert.AreEqual(5, HealthSegments.FilledSegments(55f, 100f, 10));
        Assert.AreEqual(0.5f, HealthSegments.PartialFill01(55f, 100f, 10), 1e-4f);
    }

    [Test]
    public void FilledSegments_NeverExceedsCount_AndNeverNegative()
    {
        Assert.AreEqual(10, HealthSegments.FilledSegments(150f, 100f, 10));
        Assert.AreEqual(0, HealthSegments.FilledSegments(-20f, 100f, 10));
    }

    [Test]
    public void ZeroOrNegativeMax_IsSafe()
    {
        Assert.AreEqual(0, HealthSegments.FilledSegments(50f, 0f, 10));
        Assert.AreEqual(0f, HealthSegments.PartialFill01(50f, 0f, 10), 1e-4f);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run the EditMode suite (Unity Test Runner, or bundled runner per memory workaround), filtering `HealthSegmentsTests`.
Expected: FAIL — `HealthSegments` does not exist / not found.

- [ ] **Step 5: Write the minimal implementation**

Create `Assets/Scripts/Hud/Core/HealthSegments.cs`:

```csharp
namespace Game.Hud.Core
{
    /// <summary>
    /// Pure, engine-free segmented-health-bar math. A bar of <c>segmentCount</c> discrete blocks
    /// shows <c>FilledSegments</c> fully-lit blocks plus one partially-lit block at <c>PartialFill01</c>.
    /// Unit-testable; no UnityEngine dependency.
    /// </summary>
    public static class HealthSegments
    {
        private static float Fraction(float current, float max)
        {
            if (max <= 0f) return 0f;
            float f = current / max;
            if (f < 0f) return 0f;
            if (f > 1f) return 1f;
            return f;
        }

        /// <summary>Number of fully-lit segments (0..segmentCount).</summary>
        public static int FilledSegments(float current, float max, int segmentCount)
        {
            if (segmentCount <= 0) return 0;
            int filled = (int)(Fraction(current, max) * segmentCount);
            if (filled > segmentCount) filled = segmentCount;
            return filled;
        }

        /// <summary>Fractional fill (0..1) of the single partially-lit segment above the filled ones.</summary>
        public static float PartialFill01(float current, float max, int segmentCount)
        {
            if (segmentCount <= 0) return 0f;
            float exact = Fraction(current, max) * segmentCount;
            float partial = exact - (int)exact;
            if (partial < 0f) return 0f;
            if (partial > 1f) return 1f;
            return partial;
        }
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run the EditMode suite filtering `HealthSegmentsTests`.
Expected: PASS (6 tests).

- [ ] **Step 7: Commit**

```bash
git add "Assets/Scripts/Hud/Core" "Assets/Tests/EditMode/Hud"
git commit -m "feat(hud): add Game.Hud.Core assembly and segmented-health math"
```

---

## Task 2: BuffTierVisual math

**Files:**
- Create: `Assets/Scripts/Hud/Core/BuffTierVisual.cs`
- Test: `Assets/Tests/EditMode/Hud/BuffTierVisualTests.cs`

**Interfaces:**
- Consumes: nothing (new pure module in `Game.Hud.Core`).
- Produces: `Game.Hud.Core.BuffTierVisual.IsLocked(int tier) → bool` (tier <= 0), `Game.Hud.Core.BuffTierVisual.Intensity01(int tier, int maxTier) → float` (0 when locked, ramps linearly to 1 at maxTier). HUD components map `Intensity01` onto icon color/glow via `Color.Lerp`.

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/Hud/BuffTierVisualTests.cs`:

```csharp
using NUnit.Framework;
using Game.Hud.Core;

public class BuffTierVisualTests
{
    [Test]
    public void Tier0_IsLocked()
    {
        Assert.IsTrue(BuffTierVisual.IsLocked(0));
    }

    [Test]
    public void PositiveTier_IsNotLocked()
    {
        Assert.IsFalse(BuffTierVisual.IsLocked(1));
        Assert.IsFalse(BuffTierVisual.IsLocked(3));
    }

    [Test]
    public void Intensity_LockedTier_IsZero()
    {
        Assert.AreEqual(0f, BuffTierVisual.Intensity01(0, 3), 1e-4f);
    }

    [Test]
    public void Intensity_MaxTier_IsOne()
    {
        Assert.AreEqual(1f, BuffTierVisual.Intensity01(3, 3), 1e-4f);
    }

    [Test]
    public void Intensity_MidTier_IsProportional()
    {
        Assert.AreEqual(1f / 3f, BuffTierVisual.Intensity01(1, 3), 1e-4f);
        Assert.AreEqual(2f / 3f, BuffTierVisual.Intensity01(2, 3), 1e-4f);
    }

    [Test]
    public void Intensity_AboveMax_ClampsToOne()
    {
        Assert.AreEqual(1f, BuffTierVisual.Intensity01(5, 3), 1e-4f);
    }

    [Test]
    public void Intensity_ZeroOrNegativeMax_IsSafe()
    {
        Assert.AreEqual(0f, BuffTierVisual.Intensity01(2, 0), 1e-4f);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run the EditMode suite filtering `BuffTierVisualTests`.
Expected: FAIL — `BuffTierVisual` not found.

- [ ] **Step 3: Write the minimal implementation**

Create `Assets/Scripts/Hud/Core/BuffTierVisual.cs`:

```csharp
namespace Game.Hud.Core
{
    /// <summary>
    /// Pure, engine-free mapping from a buff's tier to its visual intensity. Tier 0 is locked
    /// (dim/gray); intensity ramps linearly to 1 at maxTier. Callers map intensity onto icon
    /// color/glow, e.g. Color.Lerp(dimColor, accentColor, Intensity01(tier, maxTier)).
    /// </summary>
    public static class BuffTierVisual
    {
        public static bool IsLocked(int tier) => tier <= 0;

        public static float Intensity01(int tier, int maxTier)
        {
            if (maxTier <= 0 || tier <= 0) return 0f;
            float t = (float)tier / maxTier;
            if (t < 0f) return 0f;
            if (t > 1f) return 1f;
            return t;
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run the EditMode suite filtering `BuffTierVisualTests`.
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Hud/Core/BuffTierVisual.cs" "Assets/Tests/EditMode/Hud/BuffTierVisualTests.cs"
git commit -m "feat(hud): add buff-tier visual intensity math"
```

---

## Task 3: CooldownFill math

**Files:**
- Create: `Assets/Scripts/Hud/Core/CooldownFill.cs`
- Test: `Assets/Tests/EditMode/Hud/CooldownFillTests.cs`

**Interfaces:**
- Consumes: nothing (new pure module in `Game.Hud.Core`).
- Produces: `Game.Hud.Core.CooldownFill.Fill01(float remaining, float total) → float`. Returns `1` when ready (remaining <= 0 or total <= 0), `0` at the instant of use, ramping up as it recharges — matching `PlayerMovement.GetDashCooldownPercent()`.

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/Hud/CooldownFillTests.cs`:

```csharp
using NUnit.Framework;
using Game.Hud.Core;

public class CooldownFillTests
{
    [Test]
    public void Ready_WhenNoRemaining_IsOne()
    {
        Assert.AreEqual(1f, CooldownFill.Fill01(0f, 5f), 1e-4f);
    }

    [Test]
    public void JustUsed_FullRemaining_IsZero()
    {
        Assert.AreEqual(0f, CooldownFill.Fill01(5f, 5f), 1e-4f);
    }

    [Test]
    public void Halfway_IsHalf()
    {
        Assert.AreEqual(0.5f, CooldownFill.Fill01(2.5f, 5f), 1e-4f);
    }

    [Test]
    public void ZeroOrNegativeTotal_IsReady()
    {
        Assert.AreEqual(1f, CooldownFill.Fill01(3f, 0f), 1e-4f);
    }

    [Test]
    public void RemainingAboveTotal_ClampsToZero()
    {
        Assert.AreEqual(0f, CooldownFill.Fill01(10f, 5f), 1e-4f);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run the EditMode suite filtering `CooldownFillTests`.
Expected: FAIL — `CooldownFill` not found.

- [ ] **Step 3: Write the minimal implementation**

Create `Assets/Scripts/Hud/Core/CooldownFill.cs`:

```csharp
namespace Game.Hud.Core
{
    /// <summary>
    /// Pure, engine-free cooldown fill fraction. 1 = ready, 0 = just used, ramping up while it
    /// recharges. Mirrors PlayerMovement.GetDashCooldownPercent so dash and stealth radials read
    /// identically. Callers set Image.fillAmount = Fill01(remaining, total).
    /// </summary>
    public static class CooldownFill
    {
        public static float Fill01(float remaining, float total)
        {
            if (total <= 0f) return 1f;
            float frac = remaining / total;
            if (frac < 0f) frac = 0f;
            if (frac > 1f) frac = 1f;
            return 1f - frac;
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run the EditMode suite filtering `CooldownFillTests`.
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Hud/Core/CooldownFill.cs" "Assets/Tests/EditMode/Hud/CooldownFillTests.cs"
git commit -m "feat(hud): add cooldown fill math"
```

---

## Task 4: Change events on health + coin sources

**Files:**
- Modify: `Assets/Scripts/Player/PlayerStatsHandler.cs` (add `using System;`, event, invoke in `OnHealthChanged`)
- Modify: `Assets/Scripts/Coin Scripts/PlayerInventory.cs` (add `using System;`, event, `OnChangedRender` callback)

**Interfaces:**
- Produces: `PlayerStatsHandler.HealthChanged` (`event Action`, fires on every health change), `NetworkedPlayerInventory.CoinsChanged` (`event Action`, fires when `CoinCount`/`TotalCoinValue` change). Consumed by Tasks 8/9/10.

- [ ] **Step 1: Add the HealthChanged event to PlayerStatsHandler**

In `Assets/Scripts/Player/PlayerStatsHandler.cs`, add `using System;` to the top (after `using Fusion;`). Then add the event field just above the `[Networked, OnChangedRender(nameof(OnHealthChanged))]` property (around line 35):

```csharp
    /// <summary>Fires whenever CurrentHealth changes (Fusion render callback). HUD subscribes.</summary>
    public event Action HealthChanged;
```

Then update the existing `OnHealthChanged` method (around line 86) to raise it:

```csharp
    private void OnHealthChanged()
    {
        UpdateHealthBar();
        HealthChanged?.Invoke();
    }
```

- [ ] **Step 2: Add the CoinsChanged event to NetworkedPlayerInventory**

In `Assets/Scripts/Coin Scripts/PlayerInventory.cs`, add `using System;` to the top. Add the event field near the other fields:

```csharp
    /// <summary>Fires whenever CoinCount / TotalCoinValue change (Fusion render callback). HUD subscribes.</summary>
    public event Action CoinsChanged;
```

Change the two networked properties (currently lines 27-32) to route through a render callback:

```csharp
    [Networked, OnChangedRender(nameof(OnCoinsChanged))]
    public int CoinCount { get; private set; }

    [Networked, OnChangedRender(nameof(OnCoinsChanged))]
    public int TotalCoinValue { get; private set; }
```

Add the callback method inside the class:

```csharp
    private void OnCoinsChanged()
    {
        CoinsChanged?.Invoke();
    }
```

- [ ] **Step 3: Verify it compiles**

Compile the project (Unity, or bundled Roslyn per the unity-locked-verification-workaround memory).
Expected: no compile errors; both `OnChangedRender` method names resolve.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/Player/PlayerStatsHandler.cs" "Assets/Scripts/Coin Scripts/PlayerInventory.cs"
git commit -m "feat(hud): expose HealthChanged and CoinsChanged events on player sources"
```

---

## Task 5: Change events + stealth cooldown fill on PlayerBuffs

**Files:**
- Modify: `Assets/Scripts/Buffs/PlayerBuffs.cs`

**Interfaces:**
- Consumes: `Game.Hud.Core.CooldownFill.Fill01` (Task 3), existing private `CurrentStealthCooldown()`, existing `[Networked] IsStealthed`, existing `StealthCooldownTimer`.
- Produces: `PlayerBuffs.BuffsChanged` (`event Action`, fires when `TotalDepositedValue` changes → tiers re-derive), `PlayerBuffs.StealthStateChanged` (`event Action`, fires when `IsStealthed` changes), `PlayerBuffs.StealthCooldownFill01() → float` (1 = ready, 0 = just used). `TierOf(BuffId)` and `IsStealthed` are already public.

- [ ] **Step 1: Add usings and event fields**

In `Assets/Scripts/Buffs/PlayerBuffs.cs`, add `using System;` and `using Game.Hud.Core;` to the top (after `using Game.Buffs.Core;`). Add the event fields near the other fields (after line 21 `public int TotalDeposited => TotalDepositedValue;`):

```csharp
    /// <summary>Fires when TotalDepositedValue changes (tiers re-derive). HUD subscribes.</summary>
    public event Action BuffsChanged;

    /// <summary>Fires when the networked stealth-active flag flips. HUD subscribes.</summary>
    public event Action StealthStateChanged;
```

- [ ] **Step 2: Route the networked properties through render callbacks**

Change the `TotalDepositedValue` declaration (line 16) and `IsStealthed` declaration (line 17) to:

```csharp
    [Networked, OnChangedRender(nameof(OnBuffsChanged))] public int TotalDepositedValue { get; private set; }
    [Networked, OnChangedRender(nameof(OnStealthChanged))] public NetworkBool IsStealthed { get; private set; }
```

Add the two callback methods inside the class:

```csharp
    private void OnBuffsChanged() => BuffsChanged?.Invoke();
    private void OnStealthChanged() => StealthStateChanged?.Invoke();
```

- [ ] **Step 3: Add the public stealth cooldown fill accessor**

Add this method to `PlayerBuffs` (it reuses the existing private `CurrentStealthCooldown()`):

```csharp
    /// <summary>
    /// Radial fill for the stealth icon: 1 = ready, 0 = just used, ramping up while it recharges.
    /// While stealth is ACTIVE the ability is unavailable, so report 0 (not ready).
    /// </summary>
    public float StealthCooldownFill01()
    {
        if (IsStealthed) return 0f;
        float total = CurrentStealthCooldown();
        float remaining = StealthCooldownTimer.RemainingTime(Runner) ?? 0f;
        return CooldownFill.Fill01(remaining, total);
    }
```

- [ ] **Step 4: Verify it compiles**

Compile the project.
Expected: no errors; `Game.Hud.Core` is auto-referenced so `CooldownFill` resolves; both `OnChangedRender` names resolve.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Buffs/PlayerBuffs.cs"
git commit -m "feat(hud): expose buff change events and stealth cooldown fill"
```

---

## Task 6: Change events on TeamScoreManager

**Files:**
- Modify: `Assets/Scripts/Coin Scripts/TeamScoreManager.cs`

**Interfaces:**
- Produces: `TeamScoreManager.ScoresChanged` (`event Action`, fires when either score changes), `TeamScoreManager.TeamBuffsChanged` (`event Action`, fires when any of the 4 team-buff bools change). Existing `Instance`, `Team1Score`, `Team2Score`, `HasDamageBuff(Team)`, `HasDefenseBuff(Team)` are consumed by Task 11.

- [ ] **Step 1: Add usings and event fields**

In `Assets/Scripts/Coin Scripts/TeamScoreManager.cs`, add `using System;` to the top. Add the events near the other fields (after line 33, the `onDefenseBuffUnlocked` UnityEvent):

```csharp
    /// <summary>Fires when Team1Score / Team2Score change. HUD subscribes.</summary>
    public event Action ScoresChanged;

    /// <summary>Fires when any team damage/defense buff flag changes. HUD subscribes.</summary>
    public event Action TeamBuffsChanged;
```

- [ ] **Step 2: Route the networked properties through render callbacks**

Change the score properties (lines 15-16) and the four buff bools (lines 26-29) to:

```csharp
    [Networked, OnChangedRender(nameof(OnScoresChanged))] public int Team1Score { get; set; }
    [Networked, OnChangedRender(nameof(OnScoresChanged))] public int Team2Score { get; set; }
```

```csharp
    [Networked, OnChangedRender(nameof(OnTeamBuffsChanged))] public bool Team1DamageBuff { get; set; }
    [Networked, OnChangedRender(nameof(OnTeamBuffsChanged))] public bool Team2DamageBuff { get; set; }
    [Networked, OnChangedRender(nameof(OnTeamBuffsChanged))] public bool Team1DefenseBuff { get; set; }
    [Networked, OnChangedRender(nameof(OnTeamBuffsChanged))] public bool Team2DefenseBuff { get; set; }
```

Add the callback methods inside the class:

```csharp
    private void OnScoresChanged() => ScoresChanged?.Invoke();
    private void OnTeamBuffsChanged() => TeamBuffsChanged?.Invoke();
```

- [ ] **Step 3: Verify it compiles**

Compile the project.
Expected: no errors; the four `OnChangedRender` references resolve.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/Coin Scripts/TeamScoreManager.cs"
git commit -m "feat(hud): expose score and team-buff change events"
```

---

## Task 7: PlayerHud root + local-player binding

**Files:**
- Create: `Assets/Scripts/Hud/PlayerHud.cs`

**Interfaces:**
- Consumes: `NetworkedPlayerInventory` (has `HasInputAuthority`), `PlayerStatsHandler`, `PlayerBuffs`, `PlayerTeamData` (all `GetComponent`-able on the local player object).
- Produces: `PlayerHud` broadcasts a bind once the local player is found. Display components (Tasks 8-11) implement `IHudBindable.Bind(HudContext ctx)` / `Unbind()`. `HudContext` carries the resolved source references.

- [ ] **Step 1: Create the HUD context + bindable interface + root**

Create `Assets/Scripts/Hud/PlayerHud.cs`:

```csharp
using UnityEngine;

/// <summary>References to the local player's networked sources, handed to each HUD display on bind.</summary>
public readonly struct HudContext
{
    public readonly NetworkedPlayerInventory Inventory;
    public readonly PlayerStatsHandler Stats;
    public readonly PlayerBuffs Buffs;
    public readonly PlayerTeamData Team;

    public HudContext(NetworkedPlayerInventory inventory, PlayerStatsHandler stats,
                      PlayerBuffs buffs, PlayerTeamData team)
    {
        Inventory = inventory;
        Stats = stats;
        Buffs = buffs;
        Team = team;
    }
}

/// <summary>A HUD display that binds to the local player's sources and updates event-driven.</summary>
public interface IHudBindable
{
    void Bind(HudContext ctx);
    void Unbind();
}

/// <summary>
/// In-match HUD root. Discovers the local (input-authority) player ONCE — a bounded,
/// self-terminating search, not per-frame value polling — then binds every child display.
/// Replaces the old polling UIManager.
/// </summary>
public class PlayerHud : MonoBehaviour
{
    [Tooltip("Every child display implementing IHudBindable. Populated in the inspector.")]
    [SerializeField] private MonoBehaviour[] displays;

    private bool bound;

    private void Update()
    {
        if (bound) return;
        TryBind();
    }

    private void TryBind()
    {
        NetworkedPlayerInventory[] players =
            FindObjectsByType<NetworkedPlayerInventory>(FindObjectsSortMode.None);

        foreach (var inv in players)
        {
            if (!inv.HasInputAuthority) continue;

            var ctx = new HudContext(
                inv,
                inv.GetComponent<PlayerStatsHandler>(),
                inv.GetComponent<PlayerBuffs>(),
                inv.GetComponent<PlayerTeamData>());

            foreach (var display in displays)
            {
                if (display is IHudBindable bindable)
                    bindable.Bind(ctx);
            }

            bound = true;
            return;
        }
    }

    private void OnDisable()
    {
        if (!bound) return;
        foreach (var display in displays)
        {
            if (display is IHudBindable bindable)
                bindable.Unbind();
        }
        bound = false;
    }
}
```

- [ ] **Step 2: Verify it compiles**

Compile the project.
Expected: no errors. (`PlayerTeamData` already exists — used in `PlayerStatsHandler.ResolveSpawnPosition`.)

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Hud/PlayerHud.cs"
git commit -m "feat(hud): add PlayerHud root with one-time local-player binding"
```

---

## Task 8: HealthSegmentDisplay

**Files:**
- Create: `Assets/Scripts/Hud/HealthSegmentDisplay.cs`

**Interfaces:**
- Consumes: `HudContext.Stats` (`PlayerStatsHandler.GetCurrentHealth()`, `GetMaxHealth()`, `HealthChanged` event), `Game.Hud.Core.HealthSegments`.
- Produces: nothing (leaf display).

- [ ] **Step 1: Create the display**

Create `Assets/Scripts/Hud/HealthSegmentDisplay.cs`:

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Game.Hud.Core;

/// <summary>
/// Segmented health bar. Discrete blocks light up whole; the block above the last full one
/// shows a fractional fill. Event-driven — repaints only on PlayerStatsHandler.HealthChanged.
/// </summary>
public class HealthSegmentDisplay : MonoBehaviour, IHudBindable
{
    [Tooltip("One filled Image per segment, left-to-right. Image Type = Filled, Horizontal.")]
    [SerializeField] private Image[] segments;

    [Tooltip("Color of a lit segment.")]
    [SerializeField] private Color litColor = Color.white;

    [Tooltip("Color of an empty segment.")]
    [SerializeField] private Color emptyColor = new Color(1f, 1f, 1f, 0.15f);

    [Tooltip("Optional numeric readout, e.g. 80 / 100.")]
    [SerializeField] private TextMeshProUGUI healthText;

    private PlayerStatsHandler stats;

    public void Bind(HudContext ctx)
    {
        stats = ctx.Stats;
        if (stats == null) return;
        stats.HealthChanged += Repaint;
        Repaint();
    }

    public void Unbind()
    {
        if (stats != null) stats.HealthChanged -= Repaint;
        stats = null;
    }

    private void Repaint()
    {
        if (stats == null || segments == null || segments.Length == 0) return;

        float current = stats.GetCurrentHealth();
        float max = stats.GetMaxHealth();
        int count = segments.Length;

        int filled = HealthSegments.FilledSegments(current, max, count);
        float partial = HealthSegments.PartialFill01(current, max, count);

        for (int i = 0; i < count; i++)
        {
            Image seg = segments[i];
            if (seg == null) continue;

            if (i < filled)
            {
                seg.color = litColor;
                seg.fillAmount = 1f;
            }
            else if (i == filled)
            {
                seg.color = partial > 0f ? litColor : emptyColor;
                seg.fillAmount = partial;
            }
            else
            {
                seg.color = emptyColor;
                seg.fillAmount = 1f;
            }
        }

        if (healthText != null)
            healthText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
    }

    private void OnDisable() => Unbind();
}
```

- [ ] **Step 2: Verify it compiles**

Compile the project.
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Hud/HealthSegmentDisplay.cs"
git commit -m "feat(hud): add segmented health display"
```

---

## Task 9: CoinDisplay

**Files:**
- Create: `Assets/Scripts/Hud/CoinDisplay.cs`

**Interfaces:**
- Consumes: `HudContext.Inventory` (`NetworkedPlayerInventory.CoinCount`, `TotalCoinValue`, `CoinsChanged` event).
- Produces: nothing (leaf display).

- [ ] **Step 1: Create the display**

Create `Assets/Scripts/Hud/CoinDisplay.cs`:

```csharp
using UnityEngine;
using TMPro;

/// <summary>
/// Coin count / total value readout. Event-driven — repaints only on
/// NetworkedPlayerInventory.CoinsChanged.
/// </summary>
public class CoinDisplay : MonoBehaviour, IHudBindable
{
    [Tooltip("Coin count text, e.g. the number carried.")]
    [SerializeField] private TextMeshProUGUI coinCountText;

    [Tooltip("Optional total coin value text.")]
    [SerializeField] private TextMeshProUGUI coinValueText;

    private NetworkedPlayerInventory inventory;

    public void Bind(HudContext ctx)
    {
        inventory = ctx.Inventory;
        if (inventory == null) return;
        inventory.CoinsChanged += Repaint;
        Repaint();
    }

    public void Unbind()
    {
        if (inventory != null) inventory.CoinsChanged -= Repaint;
        inventory = null;
    }

    private void Repaint()
    {
        if (inventory == null) return;
        if (coinCountText != null) coinCountText.text = inventory.CoinCount.ToString();
        if (coinValueText != null) coinValueText.text = inventory.TotalCoinValue.ToString();
    }

    private void OnDisable() => Unbind();
}
```

- [ ] **Step 2: Verify it compiles**

Compile the project.
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Hud/CoinDisplay.cs"
git commit -m "feat(hud): add coin display"
```

---

## Task 10: BuffIconDisplay

**Files:**
- Create: `Assets/Scripts/Hud/BuffIconDisplay.cs`

**Interfaces:**
- Consumes: `HudContext.Buffs` (`PlayerBuffs.TierOf(BuffId)`, `BuffsChanged`, `StealthStateChanged`, `StealthCooldownFill01()`), `HudContext.Inventory` (to reach `PlayerMovement` for dash fill via `GetComponent`), `Game.Hud.Core.BuffTierVisual`, `Game.Buffs.Core.BuffId`. One instance per buff (dash/jump/stealth), configured by `buffId` in the inspector.
- Produces: nothing (leaf display).

**Notes for the implementer:** Tier color/glow is discrete (event-driven). The cooldown radial is a smooth countdown that can only animate per-frame WHILE a cooldown is running — so `Update()` drives the radial only, and only when this buff is an active ability (`QuickerDash` or `Stealth`). `ExtraJump` has no cooldown radial. Dash cooldown comes from `PlayerMovement.GetDashCooldownPercent()`; stealth from `PlayerBuffs.StealthCooldownFill01()`.

- [ ] **Step 1: Create the display**

Create `Assets/Scripts/Hud/BuffIconDisplay.cs`:

```csharp
using UnityEngine;
using UnityEngine.UI;
using Game.Hud.Core;
using Game.Buffs.Core;

/// <summary>
/// One buff icon. Tier drives color/glow (event-driven via PlayerBuffs.BuffsChanged); active
/// abilities (dash, stealth) also show a per-frame radial cooldown sweep. maxTier is read from
/// the loadout config, defaulting to 3.
/// </summary>
public class BuffIconDisplay : MonoBehaviour, IHudBindable
{
    [Header("Identity")]
    [SerializeField] private BuffId buffId;
    [SerializeField] private int maxTier = 3;

    [Header("Icon color/glow")]
    [Tooltip("Main icon image whose color is lerped by tier.")]
    [SerializeField] private Image icon;
    [SerializeField] private Color lockedColor = new Color(1f, 1f, 1f, 0.25f);
    [SerializeField] private Color accentColor = Color.yellow;

    [Header("Cooldown radial (dash / stealth only)")]
    [Tooltip("Image Type = Filled, Radial. fillAmount 1 = ready. Leave null for passive buffs.")]
    [SerializeField] private Image cooldownRadial;

    private PlayerBuffs buffs;
    private PlayerMovement movement;

    public void Bind(HudContext ctx)
    {
        buffs = ctx.Buffs;
        movement = ctx.Inventory != null ? ctx.Inventory.GetComponent<PlayerMovement>() : null;
        if (buffs != null)
        {
            buffs.BuffsChanged += RepaintTier;
            buffs.StealthStateChanged += RepaintTier;
        }
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
    }

    private void RepaintTier()
    {
        if (buffs == null || icon == null) return;
        int tier = buffs.TierOf(buffId);
        float intensity = BuffTierVisual.Intensity01(tier, maxTier);
        icon.color = Color.Lerp(lockedColor, accentColor, intensity);
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

- [ ] **Step 2: Verify it compiles**

Compile the project.
Expected: no errors; `PlayerMovement.GetDashCooldownPercent()` and `PlayerBuffs.StealthCooldownFill01()` resolve.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Hud/BuffIconDisplay.cs"
git commit -m "feat(hud): add buff icon display with tier glow and cooldown radial"
```

---

## Task 11: TeamScoreDisplay + team-buff badge

**Files:**
- Create: `Assets/Scripts/Hud/TeamScoreDisplay.cs`

**Interfaces:**
- Consumes: `TeamScoreManager.Instance` (`Team1Score`, `Team2Score`, `HasDamageBuff(Team)`, `HasDefenseBuff(Team)`, `ScoresChanged`, `TeamBuffsChanged`), `HudContext.Team` (`PlayerTeamData.Team`), `Game.*` `Team` enum + `TeamUtil` (already used across the project).
- Produces: nothing (leaf display).

**Notes for the implementer:** `TeamScoreManager` is a singleton spawned at runtime, so — like the local player — it may not exist when `Bind` runs. Resolve it lazily: subscribe on the first frame `Instance` is non-null. The badge is a single GameObject shown only while THIS player's team has an active team buff (replacing the old four always-present icons).

- [ ] **Step 1: Create the display**

Create `Assets/Scripts/Hud/TeamScoreDisplay.cs`:

```csharp
using UnityEngine;
using TMPro;

/// <summary>
/// Team score readout plus a single badge shown only while the local player's team has an active
/// coin-milestone buff (damage or defense). Event-driven off TeamScoreManager; the manager is a
/// runtime singleton, so subscription is deferred until Instance exists.
/// </summary>
public class TeamScoreDisplay : MonoBehaviour, IHudBindable
{
    [SerializeField] private TextMeshProUGUI team1ScoreText;
    [SerializeField] private TextMeshProUGUI team2ScoreText;

    [Tooltip("Badge shown only when THIS player's team has an active team buff.")]
    [SerializeField] private GameObject teamBuffBadge;

    private Team localTeam = Team.None;
    private TeamScoreManager scoreManager;

    public void Bind(HudContext ctx)
    {
        localTeam = ctx.Team != null ? ctx.Team.Team : Team.None;
        if (teamBuffBadge != null) teamBuffBadge.SetActive(false);
        // Manager subscription happens lazily in Update once Instance is live.
    }

    public void Unbind()
    {
        if (scoreManager != null)
        {
            scoreManager.ScoresChanged -= RepaintScores;
            scoreManager.TeamBuffsChanged -= RepaintBadge;
        }
        scoreManager = null;
    }

    private void Update()
    {
        if (scoreManager != null) return;

        TeamScoreManager mgr = TeamScoreManager.Instance;
        if (mgr == null || mgr.Object == null || !mgr.Object.IsValid) return;

        scoreManager = mgr;
        scoreManager.ScoresChanged += RepaintScores;
        scoreManager.TeamBuffsChanged += RepaintBadge;
        RepaintScores();
        RepaintBadge();
    }

    private void RepaintScores()
    {
        if (scoreManager == null) return;
        if (team1ScoreText != null) team1ScoreText.text = scoreManager.Team1Score.ToString();
        if (team2ScoreText != null) team2ScoreText.text = scoreManager.Team2Score.ToString();
    }

    private void RepaintBadge()
    {
        if (scoreManager == null || teamBuffBadge == null) return;
        bool active = localTeam != Team.None &&
                      (scoreManager.HasDamageBuff(localTeam) || scoreManager.HasDefenseBuff(localTeam));
        teamBuffBadge.SetActive(active);
    }

    private void OnDisable() => Unbind();
}
```

- [ ] **Step 2: Verify it compiles**

Compile the project.
Expected: no errors; `Team`, `TeamScoreManager.Instance`, `HasDamageBuff`/`HasDefenseBuff` resolve. (`Team` enum is global — used by `PlayerTeamData` / `TeamUtil` throughout.)

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Hud/TeamScoreDisplay.cs"
git commit -m "feat(hud): add team score display with single team-buff badge"
```

---

## Task 12: Retire UIManager, wire the scene, verify end-to-end

**Files:**
- Delete: `Assets/Scripts/Coin Scripts/UIManager.cs` (and its `.meta`)
- Modify: `Assets/Scenes/Gameplay.unity` (in Unity Editor — rebuild the HUD Canvas)

**Notes for the implementer:** This is the integration task and requires the Unity Editor. It has no unit test; the deliverable is verified manually in-editor and in a multi-peer run.

- [ ] **Step 1: Confirm nothing references UIManager**

Run:
```bash
grep -rn "UIManager" Assets --include=*.cs
```
Expected: only `Assets/Scripts/Coin Scripts/UIManager.cs` itself (its own class). If any other script references it, stop and reconcile before deleting. (Note: `.prefab`/`.unity` scene references are handled in Step 3.)

- [ ] **Step 2: Delete the old script**

```bash
git rm "Assets/Scripts/Coin Scripts/UIManager.cs" "Assets/Scripts/Coin Scripts/UIManager.cs.meta"
```

- [ ] **Step 3: Rebuild the HUD Canvas in the Unity Editor**

In `Assets/Scenes/Gameplay.unity`:
1. Find the existing HUD Canvas that hosted the old `UIManager` component; remove the (now-missing) `UIManager` script component from it.
2. Add an empty child `PlayerHud` GameObject; add the `PlayerHud` component.
3. Build the bottom-cluster panel (dark translucent background) containing, left→right:
   - **Health**: a horizontal row of N filled `Image` segments (e.g. 10) + optional TMP readout. Add `HealthSegmentDisplay`; assign the segment `Image[]` and text.
   - **Coins**: coin icon + TMP count (+ optional value). Add `CoinDisplay`; assign texts.
   - **Buffs**: three icon objects. Add one `BuffIconDisplay` each; set `buffId` to `QuickerDash`, `ExtraJump`, `Stealth`; assign `icon`, `accentColor` (a distinct color per buff), and a radial `Image` for dash + stealth (leave the jump radial null).
4. Top-right: team score TMP texts + a `teamBuffBadge` GameObject (hidden by default). Add `TeamScoreDisplay`; assign the texts and badge, and set `localTeam` source via the bound `PlayerTeamData` (assigned automatically at bind — no inspector field needed).
5. On the `PlayerHud` component, populate the `displays` array with every display component created above (HealthSegmentDisplay, CoinDisplay, 3× BuffIconDisplay, TeamScoreDisplay).
6. Confirm the flag HUD objects (directional arrow, carrier icon, notification text) are untouched and still positioned top-center.

- [ ] **Step 4: Run the full EditMode suite**

Run the entire EditMode test suite.
Expected: all HUD core tests (Tasks 1-3) pass; no other suite regresses.

- [ ] **Step 5: Manual in-editor verification (single peer)**

Enter Play mode as host. Verify:
- Health segments deplete in whole blocks + a partial block as you take damage; refill on respawn.
- Coin count updates when picking up / depositing coins.
- Buff icons brighten toward their accent color as deposited value crosses tier thresholds.
- Dash icon radial sweeps on dash and refills to full when ready; stealth icon radial behaves the same on stealth use.
- Team score text updates on deposit; the team-buff badge appears only after the team crosses a milestone.
- The flag HUD renders unchanged and does not overlap the new panels.

- [ ] **Step 6: Manual multi-peer verification**

Run host + at least one client (per the project's dedicated-server/multi-peer testing guide). Verify each client's HUD reflects ITS OWN local player (health/coins/buffs) and the shared team score/badge, with no cross-player leakage.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(hud): retire UIManager, wire event-driven HUD into Gameplay scene"
```

---

## Self-Review Notes

- **Spec coverage:** segmented health (Tasks 1,8), coins (Tasks 4,9), per-player buff tier glow (Tasks 2,5,10), dash/stealth cooldown radials built into buff icons (Tasks 3,5,10), team score + single re-skinned team-buff badge (Tasks 6,11), event-driven architecture replacing polling (Tasks 4-7 events + Task 7 one-time bind), flag HUD untouched + consistency check (Task 12 Step 3.6 / Step 5), combat feedback deferred (not in any task). All spec sections map to tasks.
- **Type consistency:** `Bind(HudContext)`/`Unbind()` from `IHudBindable` used identically in Tasks 8-11; `CooldownFill.Fill01(remaining, total)` defined in Task 3 and consumed in Task 5; `BuffTierVisual.Intensity01(tier, maxTier)` defined Task 2, consumed Task 10; `HealthSegments.FilledSegments`/`PartialFill01` defined Task 1, consumed Task 8; event names (`HealthChanged`, `CoinsChanged`, `BuffsChanged`, `StealthStateChanged`, `ScoresChanged`, `TeamBuffsChanged`) defined in Tasks 4-6 and consumed in Tasks 8-11 match exactly.
- **No placeholders:** every code step contains complete, compilable code; every test step contains real assertions; every command has expected output.
