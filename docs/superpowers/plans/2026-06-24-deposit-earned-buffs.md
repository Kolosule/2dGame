# Deposit-Earned Buffs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reward individual players for depositing coins by progressively unlocking a player-chosen, priority-ordered set of three tiered buffs (Extra Jump, Stealth, Quicker Dash).

**Architecture:** Server-authoritative, tick-based (Photon Fusion). Each buff's current tier is *derived* purely from two networked values (`TotalDepositedValue` + `LoadoutOrder`) via a Fusion-free `BuffUnlock` helper, so there is no Apply/Remove state to replay on resimulation. Passive buffs feed an effective-stats facade that `PlayerMovement`/`PlayerCombat` read through (the shared `PlayerStats` SO is never mutated). The one active buff (Stealth) is a `TickTimer`-driven networked flag activated through `NetInput`, consumed visually in `Render()` and by `EnemyAI`.

**Tech Stack:** Unity (C#), Photon Fusion 2, Unity Test Framework (EditMode), Unity Input System.

## Global Constraints

- All gameplay-affecting state lives in `[Networked]` properties; all durations/cooldowns use `TickTimer`. No local bools, no `Time.time` in simulation. Mirror `PlayerMovement.cs`.
- No `NetworkTransform`. Stealth is visual/targeting only.
- Input flows: `NetInput`/`PlayerButton` → `NetworkInputProvider.OnInput` → `PlayerController.FixedUpdateNetwork` → `*.Simulate(...)`. No `Keyboard.current` reads in simulation.
- Gameplay in `Simulate()`, visuals in `Render()`. Stealth visuals derive from networked state.
- The player prefab has **two Animators / multiple SpriteRenderers**; the visible body uses `Player.controller`. Target **explicitly-serialized** body renderers, never `GetComponentInChildren`.
- Thresholds count **deposited value (points)**, not coin count. Threshold list (cumulative): `5,10,15, 30,45,60, 120,180,240`.
- Default loadout order: `[ExtraJump, Stealth, QuickerDash]`. Stealth cooldown: flat 20s, begins when the effect ends. Dash-T3 damage reuses melee `attackDamage` via `ResolveMeleeDamage`. Stealth alpha: owner/teammate `0.5`, enemy `0.05`. Stealth key: `Q`.
- Do not modify `TeamScoreManager` or the existing deposit scoring — buffs are additive.

## File Structure

**New (pure, in their own asmdef — no Unity/Fusion deps):**
- `Assets/Scripts/Buffs/Core/Game.Buffs.Core.asmdef` — leaf assembly, auto-referenced.
- `Assets/Scripts/Buffs/Core/BuffId.cs` — `enum BuffId : byte { ExtraJump, Stealth, QuickerDash }`.
- `Assets/Scripts/Buffs/Core/EffectiveStats.cs` — mutable struct of derived stat contributions.
- `Assets/Scripts/Buffs/Core/ActiveBuffParams.cs` — struct (duration, cooldown, usableWhileCarryingFlag).
- `Assets/Scripts/Buffs/Core/BuffUnlock.cs` — pure unlock/tier math.

**New tests:**
- `Assets/Tests/EditMode/Game.Buffs.EditModeTests.asmdef`
- `Assets/Tests/EditMode/BuffUnlockTests.cs`

**New (Unity/Fusion, in `Assembly-CSharp`):**
- `Assets/Scripts/Buffs/BuffDefinition.cs` — abstract SO + hooks.
- `Assets/Scripts/Buffs/JumpBuffDefinition.cs`, `StealthBuffDefinition.cs`, `DashBuffDefinition.cs`
- `Assets/Scripts/Buffs/BuffLoadoutConfig.cs` — registry SO (allBuffs, thresholds).
- `Assets/Scripts/Buffs/PlayerBuffs.cs` — networked core.
- `Assets/Scripts/Buffs/PlayerStatModifiers.cs` — effective-stats facade.
- `Assets/Scripts/Buffs/PlayerStealthVisual.cs` — render-side fade.

**Modified:**
- `Assets/Scripts/Player/NetInput.cs` — add `Stealth = 4`.
- `Assets/Scripts/Player/NetworkInputProvider.cs` — bind `Q`/gamepad.
- `Assets/Scripts/Player/PlayerController.cs` — call `buffs.Simulate`.
- `Assets/Scripts/Player/PlayerMovement.cs` — read effective jump/dash.
- `Assets/Scripts/Player/PlayerCombat.cs` — dash-strike branch + `ApplyMeleeHits` refactor.
- `Assets/Scripts/Coin Scripts/HomeBase.cs` — deposit → `ServerAddDepositedValue`.
- `Assets/Scripts/Enemy/Base/EnemyAI.cs` — ignore stealthed players.
- `Assets/Scripts/GameNetworkManager.cs` — loadout reliable channel + `LobbyLoadoutChoices`.
- `Assets/Scripts/NetworkSpawnManager.cs` — init loadout at spawn.
- `Assets/Scripts/Player/Teamselectionui.cs` — reorder picker.

**Editor assets (created manually in Unity):** three `BuffDefinition` assets, one `BuffLoadoutConfig` asset, and prefab component wiring on `PlayerPrefab`.

## Verifying Unity tests

EditMode tests run either way:
- **In-editor:** Window → General → Test Runner → EditMode → Run All.
- **CLI (headless):** `"<UnityEditorPath>/Unity.exe" -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -runTests -testPlatform EditMode -testResults "C:\Users\1\Documents\GitHub\2dGame\test-results.xml" -logFile -` then inspect `test-results.xml` for `result="Passed"`. Replace `<UnityEditorPath>` with the installed editor (e.g. `C:/Program Files/Unity/Hub/Editor/<version>/Editor`).

Networked behavior is verified by entering Play mode as Host (single-player mode is already the default in `GameNetworkManager`) and observing the documented effect.

---

### Task 1: Pure unlock math + tests (Buffs.Core asmdef)

**Files:**
- Create: `Assets/Scripts/Buffs/Core/Game.Buffs.Core.asmdef`
- Create: `Assets/Scripts/Buffs/Core/BuffId.cs`
- Create: `Assets/Scripts/Buffs/Core/EffectiveStats.cs`
- Create: `Assets/Scripts/Buffs/Core/ActiveBuffParams.cs`
- Create: `Assets/Scripts/Buffs/Core/BuffUnlock.cs`
- Create: `Assets/Tests/EditMode/Game.Buffs.EditModeTests.asmdef`
- Test: `Assets/Tests/EditMode/BuffUnlockTests.cs`

**Interfaces:**
- Produces:
  - `enum BuffId : byte { ExtraJump = 0, Stealth = 1, QuickerDash = 2 }`
  - `struct EffectiveStats { int BonusAirJumps; bool UnlimitedAirJumps; float DashCooldownMultiplier; float DashTimeMultiplier; bool DashDealsDamage; static EffectiveStats Default(); }`
  - `struct ActiveBuffParams { float Duration; float Cooldown; bool UsableWhileCarryingFlag; }`
  - `static class BuffUnlock { int UnlockedSteps(IReadOnlyList<int> thresholds, int totalValue); int TierLevel(int unlockedSteps, int priorityPosition, int buffCount, int maxTier); }`

- [ ] **Step 1: Create the Core assembly definition**

Create `Assets/Scripts/Buffs/Core/Game.Buffs.Core.asmdef`:

```json
{
    "name": "Game.Buffs.Core",
    "rootNamespace": "Game.Buffs.Core",
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

`autoReferenced: true` makes `Assembly-CSharp` see these types automatically; `noEngineReferences: true` keeps it pure C# (fast tests, no UnityEngine).

- [ ] **Step 2: Write the pure value types**

Create `Assets/Scripts/Buffs/Core/BuffId.cs`:

```csharp
namespace Game.Buffs.Core
{
    /// <summary>Stable network token for each buff. Serialized as a byte in PlayerBuffs.LoadoutOrder.</summary>
    public enum BuffId : byte
    {
        ExtraJump = 0,
        Stealth = 1,
        QuickerDash = 2,
    }
}
```

Create `Assets/Scripts/Buffs/Core/EffectiveStats.cs`:

```csharp
namespace Game.Buffs.Core
{
    /// <summary>
    /// Derived per-player stat contributions, built fresh each query by summing every
    /// loadout buff's ContributeStats at its current tier. Never persisted/networked.
    /// </summary>
    public struct EffectiveStats
    {
        public int BonusAirJumps;
        public bool UnlimitedAirJumps;
        public float DashCooldownMultiplier;
        public float DashTimeMultiplier;
        public bool DashDealsDamage;

        public static EffectiveStats Default() => new EffectiveStats
        {
            BonusAirJumps = 0,
            UnlimitedAirJumps = false,
            DashCooldownMultiplier = 1f,
            DashTimeMultiplier = 1f,
            DashDealsDamage = false,
        };
    }
}
```

Create `Assets/Scripts/Buffs/Core/ActiveBuffParams.cs`:

```csharp
namespace Game.Buffs.Core
{
    /// <summary>Runtime parameters for an active buff at a given tier (0 = locked).</summary>
    public struct ActiveBuffParams
    {
        public bool Unlocked;
        public float Duration;
        public float Cooldown;
        public bool UsableWhileCarryingFlag;
    }
}
```

- [ ] **Step 3: Write the failing test**

Create `Assets/Tests/EditMode/BuffUnlockTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Game.Buffs.Core;

public class BuffUnlockTests
{
    private static readonly List<int> Thresholds =
        new List<int> { 5, 10, 15, 30, 45, 60, 120, 180, 240 };

    [TestCase(0, 0)]
    [TestCase(4, 0)]
    [TestCase(5, 1)]
    [TestCase(14, 2)]
    [TestCase(15, 3)]
    [TestCase(60, 6)]
    [TestCase(240, 9)]
    [TestCase(9999, 9)]
    public void UnlockedSteps_CountsThresholdsAtOrBelowTotal(int total, int expected)
    {
        Assert.AreEqual(expected, BuffUnlock.UnlockedSteps(Thresholds, total));
    }

    // Order [Jump=pos0, Stealth=pos1, Dash=pos2]. After 4 steps: Jump T2, Stealth T1, Dash T1.
    [TestCase(4, 0, 2)]
    [TestCase(4, 1, 1)]
    [TestCase(4, 2, 1)]
    [TestCase(1, 0, 1)]
    [TestCase(1, 1, 0)]
    [TestCase(0, 0, 0)]
    [TestCase(9, 0, 3)]
    [TestCase(9, 2, 3)]
    [TestCase(3, 2, 1)]
    public void TierLevel_RoundRobinsAcrossPriority(int steps, int position, int expected)
    {
        Assert.AreEqual(expected, BuffUnlock.TierLevel(steps, position, buffCount: 3, maxTier: 3));
    }

    [Test]
    public void TierLevel_ClampsToMaxTier()
    {
        Assert.AreEqual(3, BuffUnlock.TierLevel(9, 0, buffCount: 3, maxTier: 3));
    }
}
```

Create `Assets/Tests/EditMode/Game.Buffs.EditModeTests.asmdef`:

```json
{
    "name": "Game.Buffs.EditModeTests",
    "rootNamespace": "",
    "references": [
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

- [ ] **Step 4: Run the test to verify it fails**

In-editor Test Runner (EditMode) → Run All. Expected: compile error / FAIL — `BuffUnlock` does not exist yet.

- [ ] **Step 5: Implement `BuffUnlock`**

Create `Assets/Scripts/Buffs/Core/BuffUnlock.cs`:

```csharp
using System.Collections.Generic;

namespace Game.Buffs.Core
{
    /// <summary>
    /// Pure, Fusion-free unlock math. Nine ordered unlock steps are gated by a cumulative
    /// deposited-value threshold list; step i unlocks priority[i % buffCount] to tier i / buffCount + 1.
    /// A buff's tier is therefore derivable from (unlocked steps, its priority position).
    /// </summary>
    public static class BuffUnlock
    {
        /// <summary>Number of unlock steps reached: how many thresholds are at or below the total.</summary>
        public static int UnlockedSteps(IReadOnlyList<int> thresholds, int totalValue)
        {
            if (thresholds == null) return 0;
            int count = 0;
            for (int i = 0; i < thresholds.Count; i++)
            {
                if (thresholds[i] <= totalValue) count++;
                else break; // thresholds are ascending
            }
            return count;
        }

        /// <summary>
        /// Tier (0..maxTier) of the buff at the given priority position, given how many steps
        /// are unlocked. Counts how many unlocked step indices land on this position under the
        /// round-robin (i % buffCount == position).
        /// </summary>
        public static int TierLevel(int unlockedSteps, int priorityPosition, int buffCount, int maxTier)
        {
            if (buffCount <= 0 || unlockedSteps <= priorityPosition) return 0;
            int tier = (unlockedSteps - priorityPosition - 1) / buffCount + 1;
            if (tier < 0) return 0;
            return tier > maxTier ? maxTier : tier;
        }
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

In-editor Test Runner (EditMode) → Run All. Expected: all `BuffUnlockTests` PASS.

- [ ] **Step 7: Commit**

```bash
git add "Assets/Scripts/Buffs/Core" "Assets/Tests/EditMode"
git commit -m "feat(buffs): pure unlock/tier math + EditMode tests"
```

---

### Task 2: Buff definitions + loadout config (SO data layer)

**Files:**
- Create: `Assets/Scripts/Buffs/BuffDefinition.cs`
- Create: `Assets/Scripts/Buffs/JumpBuffDefinition.cs`
- Create: `Assets/Scripts/Buffs/StealthBuffDefinition.cs`
- Create: `Assets/Scripts/Buffs/DashBuffDefinition.cs`
- Create: `Assets/Scripts/Buffs/BuffLoadoutConfig.cs`

**Interfaces:**
- Consumes: `BuffId`, `EffectiveStats`, `ActiveBuffParams` (Task 1).
- Produces:
  - `abstract class BuffDefinition : ScriptableObject` with `BuffId Id`, `string DisplayName`, `Sprite Icon`, `BuffKind Kind`, `int MaxTier`, `void ContributeStats(ref EffectiveStats, int tierLevel)`, `ActiveBuffParams GetActiveParams(int tierLevel)`.
  - `enum BuffKind { Passive, Active }`
  - `class BuffLoadoutConfig : ScriptableObject` with `BuffDefinition[] AllBuffs`, `int[] Thresholds`, `int BuffCount`, `int MaxTier`, `BuffDefinition GetById(BuffId)`, `BuffId[] DefaultOrder`.

- [ ] **Step 1: Write the abstract base**

Create `Assets/Scripts/Buffs/BuffDefinition.cs`:

```csharp
using UnityEngine;
using Game.Buffs.Core;

public enum BuffKind { Passive, Active }

/// <summary>
/// One buff, described by a 3-entry tier table in the concrete subclass. Passive buffs
/// contribute to EffectiveStats; active buffs expose per-tier ActiveBuffParams. Adding a
/// buff = new subclass + asset added to BuffLoadoutConfig.AllBuffs. No core-loop edits.
/// </summary>
public abstract class BuffDefinition : ScriptableObject
{
    [SerializeField] private BuffId id;
    [SerializeField] private string displayName;
    [SerializeField] private Sprite icon;
    [SerializeField] private BuffKind kind;

    public BuffId Id => id;
    public string DisplayName => displayName;
    public Sprite Icon => icon;
    public BuffKind Kind => kind;

    /// <summary>Highest tier this buff defines (3 for all v1 buffs).</summary>
    public virtual int MaxTier => 3;

    /// <summary>Passive contribution. tierLevel 0 = locked (contribute nothing).</summary>
    public virtual void ContributeStats(ref EffectiveStats stats, int tierLevel) { }

    /// <summary>Active params at the given tier. Default: locked/zero.</summary>
    public virtual ActiveBuffParams GetActiveParams(int tierLevel) => default;
}
```

- [ ] **Step 2: Write the three concrete definitions**

Create `Assets/Scripts/Buffs/JumpBuffDefinition.cs`:

```csharp
using UnityEngine;
using Game.Buffs.Core;

/// <summary>Passive. T1 +1 air jump, T2 +2 air jumps, T3 unlimited air jumps.</summary>
[CreateAssetMenu(menuName = "Buffs/Extra Jump", fileName = "ExtraJumpBuff")]
public class JumpBuffDefinition : BuffDefinition
{
    [Header("Air jumps granted per tier (index 0 = tier 1)")]
    [SerializeField] private int[] bonusAirJumps = { 1, 2, 0 };
    [SerializeField] private int unlimitedAtTier = 3;

    public override void ContributeStats(ref EffectiveStats stats, int tierLevel)
    {
        if (tierLevel <= 0) return;
        if (tierLevel >= unlimitedAtTier) { stats.UnlimitedAirJumps = true; return; }
        int idx = Mathf.Clamp(tierLevel - 1, 0, bonusAirJumps.Length - 1);
        stats.BonusAirJumps += bonusAirJumps[idx];
    }
}
```

Create `Assets/Scripts/Buffs/StealthBuffDefinition.cs`:

```csharp
using UnityEngine;
using Game.Buffs.Core;

/// <summary>Active. T1 1s, T2 3s, T3 10s + usable while carrying the flag. Flat 20s cooldown.</summary>
[CreateAssetMenu(menuName = "Buffs/Stealth", fileName = "StealthBuff")]
public class StealthBuffDefinition : BuffDefinition
{
    [Header("Duration per tier (index 0 = tier 1)")]
    [SerializeField] private float[] durations = { 1f, 3f, 10f };
    [SerializeField] private float cooldown = 20f;
    [SerializeField] private int flagUsableFromTier = 3;

    public override ActiveBuffParams GetActiveParams(int tierLevel)
    {
        if (tierLevel <= 0) return default;
        int idx = Mathf.Clamp(tierLevel - 1, 0, durations.Length - 1);
        return new ActiveBuffParams
        {
            Unlocked = true,
            Duration = durations[idx],
            Cooldown = cooldown,
            UsableWhileCarryingFlag = tierLevel >= flagUsableFromTier,
        };
    }
}
```

Create `Assets/Scripts/Buffs/DashBuffDefinition.cs`:

```csharp
using UnityEngine;
using Game.Buffs.Core;

// NOTE (revised 2026-06-26): tiers re-tuned to cumulative
// T1 +50% range / T2 +cooldown x0.5 / T3 +front damage. Snippet below reflects current code.
/// <summary>
/// Passive(+on-dash), cumulative across tiers:
///   T1 +50% dash range (longer dash duration), T2 also halves dash cooldown,
///   T3 also deals melee damage in the front swing box while dashing.
/// </summary>
[CreateAssetMenu(menuName = "Buffs/Quicker Dash", fileName = "QuickerDashBuff")]
public class DashBuffDefinition : BuffDefinition
{
    [Header("Tier 1: dash range (range = dashSpeed x dashTime, so this extends dash duration)")]
    [SerializeField] private int rangeFromTier = 1;
    [SerializeField] private float rangeMultiplier = 1.5f;

    [Header("Tier 2: dash cooldown")]
    [SerializeField] private int cooldownFromTier = 2;
    [SerializeField] private float cooldownMultiplier = 0.5f;

    [Header("Tier 3: dash deals damage in front of the dasher")]
    [SerializeField] private int dashDamageFromTier = 3;

    public override void ContributeStats(ref EffectiveStats stats, int tierLevel)
    {
        if (tierLevel <= 0) return;
        if (tierLevel >= rangeFromTier) stats.DashTimeMultiplier *= rangeMultiplier;
        if (tierLevel >= cooldownFromTier) stats.DashCooldownMultiplier *= cooldownMultiplier;
        if (tierLevel >= dashDamageFromTier) stats.DashDealsDamage = true;
    }
}
```

- [ ] **Step 3: Write the loadout config**

Create `Assets/Scripts/Buffs/BuffLoadoutConfig.cs`:

```csharp
using UnityEngine;
using Game.Buffs.Core;

/// <summary>
/// Single project-wide registry + tuning. AllBuffs index is irrelevant to the network
/// (LoadoutOrder serializes BuffId), but every buff a player can equip must be listed here.
/// </summary>
[CreateAssetMenu(menuName = "Buffs/Loadout Config", fileName = "BuffLoadoutConfig")]
public class BuffLoadoutConfig : ScriptableObject
{
    [Header("Every equippable buff (one asset per buff)")]
    [SerializeField] private BuffDefinition[] allBuffs;

    [Header("Cumulative deposited-value thresholds for the 9 unlock steps")]
    [SerializeField] private int[] thresholds = { 5, 10, 15, 30, 45, 60, 120, 180, 240 };

    [Header("Default priority order if a player submits none")]
    [SerializeField] private BuffId[] defaultOrder = { BuffId.ExtraJump, BuffId.Stealth, BuffId.QuickerDash };

    public BuffDefinition[] AllBuffs => allBuffs;
    public int[] Thresholds => thresholds;
    public BuffId[] DefaultOrder => defaultOrder;
    public int BuffCount => allBuffs != null ? allBuffs.Length : 0;
    public int MaxTier => thresholds != null && BuffCount > 0 ? thresholds.Length / BuffCount : 3;

    public BuffDefinition GetById(BuffId id)
    {
        if (allBuffs == null) return null;
        for (int i = 0; i < allBuffs.Length; i++)
            if (allBuffs[i] != null && allBuffs[i].Id == id) return allBuffs[i];
        return null;
    }
}
```

- [ ] **Step 4: Verify compilation**

Return to Unity, let it compile. Expected: Console shows no compile errors.

- [ ] **Step 5: Create the SO assets (Unity Editor)**

In `Assets/Settings/Buffs/` (create the folder):
1. Right-click → Create → Buffs → Extra Jump. Set its `Id` = `ExtraJump`, Display Name = "Extra Jump", Kind = `Passive`.
2. Create → Buffs → Stealth. `Id` = `Stealth`, Display Name = "Stealth", Kind = `Active`.
3. Create → Buffs → Quicker Dash. `Id` = `QuickerDash`, Display Name = "Quicker Dash", Kind = `Passive`.
4. Create → Buffs → Loadout Config. Assign all three assets into `AllBuffs`. Leave thresholds/defaultOrder at defaults.

Expected: `BuffLoadoutConfig` shows 3 buffs and 9 thresholds in the Inspector.

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/Buffs" "Assets/Settings/Buffs"
git commit -m "feat(buffs): buff definition SOs + loadout config registry"
```

---

### Task 3: PlayerBuffs networked component

**Files:**
- Create: `Assets/Scripts/Buffs/PlayerBuffs.cs`

**Interfaces:**
- Consumes: `BuffLoadoutConfig`, `BuffDefinition`, `BuffId`, `BuffUnlock`, `EffectiveStats`, `ActiveBuffParams`, `FlagCarrierMarker.IsCarryingFlag()`, `PlayerButton.Stealth` (Task 7 adds the enum value; until then reference the literal index but Task 7 is a prerequisite for activation to compile — see note).
- Produces:
  - `bool IsStealthed { get; }`
  - `int TierOf(BuffId id)`
  - `void BuildEffectiveStats(ref EffectiveStats stats)`
  - `void ServerInitLoadout(byte[] order)`
  - `void ServerAddDepositedValue(int points)`
  - `void Simulate(NetInput input, NetworkButtons pressed)`
  - `int TotalDeposited { get; }`

> **Ordering note:** This task references `PlayerButton.Stealth`. Do Task 7 Step 1 (add the enum value) first, or add the enum value as part of this task's Step 1. The plan assumes you add `Stealth = 4` to `NetInput.cs` before compiling this file.

- [ ] **Step 1: Ensure `PlayerButton.Stealth` exists**

In `Assets/Scripts/Player/NetInput.cs`, the enum must include `Stealth = 4`. If not yet present, add it now (Task 7 covers the input binding):

```csharp
public enum PlayerButton
{
    Jump = 0,
    Dash = 1,
    Melee = 2,
    Shoot = 3,
    Stealth = 4,
}
```

- [ ] **Step 2: Write `PlayerBuffs`**

Create `Assets/Scripts/Buffs/PlayerBuffs.cs`:

```csharp
using UnityEngine;
using Fusion;
using Game.Buffs.Core;

/// <summary>
/// Per-player, server-authoritative buff state. Tiers are DERIVED from TotalDepositedValue +
/// LoadoutOrder (both networked) via BuffUnlock — nothing to replay on resimulation. The one
/// active buff (Stealth) is a TickTimer-driven networked flag, activated in Simulate (mirrors dash).
/// </summary>
public class PlayerBuffs : NetworkBehaviour
{
    [SerializeField] private BuffLoadoutConfig config;

    [Networked, Capacity(8)] private NetworkArray<byte> LoadoutOrder { get; }
    [Networked] private int LoadoutLength { get; set; }
    [Networked] public int TotalDepositedValue { get; private set; }
    [Networked] public NetworkBool IsStealthed { get; private set; }
    [Networked] private TickTimer StealthDurationTimer { get; set; }
    [Networked] private TickTimer StealthCooldownTimer { get; set; }

    private FlagCarrierMarker flagMarker;

    public int TotalDeposited => TotalDepositedValue;

    public override void Spawned()
    {
        flagMarker = GetComponent<FlagCarrierMarker>();

        if (HasStateAuthority && LoadoutLength == 0)
            ApplyDefaultLoadout();
    }

    private void ApplyDefaultLoadout()
    {
        if (config == null || config.DefaultOrder == null) return;
        ServerInitLoadout(ToBytes(config.DefaultOrder));
    }

    private static byte[] ToBytes(BuffId[] order)
    {
        var bytes = new byte[order.Length];
        for (int i = 0; i < order.Length; i++) bytes[i] = (byte)order[i];
        return bytes;
    }

    /// <summary>SERVER: set this player's priority order (from the lobby choice or default).</summary>
    public void ServerInitLoadout(byte[] order)
    {
        if (!HasStateAuthority || order == null) return;
        int n = Mathf.Min(order.Length, 8);
        for (int i = 0; i < n; i++) LoadoutOrder.Set(i, order[i]);
        LoadoutLength = n;
    }

    /// <summary>SERVER: add deposited point value; tiers re-derive automatically from this.</summary>
    public void ServerAddDepositedValue(int points)
    {
        if (!HasStateAuthority || points <= 0) return;
        TotalDepositedValue += points;
    }

    /// <summary>Priority position of a buff in this player's loadout, or -1 if not equipped.</summary>
    private int PositionOf(BuffId id)
    {
        for (int i = 0; i < LoadoutLength; i++)
            if ((BuffId)LoadoutOrder.Get(i) == id) return i;
        return -1;
    }

    /// <summary>Current tier (0 = locked) of the given buff for this player.</summary>
    public int TierOf(BuffId id)
    {
        if (config == null) return 0;
        int pos = PositionOf(id);
        if (pos < 0) return 0;
        int steps = BuffUnlock.UnlockedSteps(config.Thresholds, TotalDepositedValue);
        return BuffUnlock.TierLevel(steps, pos, config.BuffCount, config.MaxTier);
    }

    /// <summary>Sum every equipped buff's passive contribution at its current tier.</summary>
    public void BuildEffectiveStats(ref EffectiveStats stats)
    {
        if (config == null) return;
        for (int i = 0; i < LoadoutLength; i++)
        {
            BuffId id = (BuffId)LoadoutOrder.Get(i);
            BuffDefinition def = config.GetById(id);
            if (def == null) continue;
            def.ContributeStats(ref stats, TierOf(id));
        }
    }

    /// <summary>Called from PlayerController.FixedUpdateNetwork after movement/combat.</summary>
    public void Simulate(NetInput input, NetworkButtons pressed)
    {
        // Stealth expiry first (pure function of the networked timer).
        if (IsStealthed && StealthDurationTimer.ExpiredOrNotRunning(Runner))
        {
            IsStealthed = false;
            StealthCooldownTimer = TickTimer.CreateFromSeconds(Runner, CurrentStealthCooldown());
        }

        // Activation.
        if (pressed.IsSet((int)PlayerButton.Stealth) && CanActivateStealth())
        {
            ActiveBuffParams p = StealthParams();
            IsStealthed = true;
            StealthDurationTimer = TickTimer.CreateFromSeconds(Runner, p.Duration);
        }
    }

    private ActiveBuffParams StealthParams()
    {
        BuffDefinition def = config != null ? config.GetById(BuffId.Stealth) : null;
        return def != null ? def.GetActiveParams(TierOf(BuffId.Stealth)) : default;
    }

    private float CurrentStealthCooldown()
    {
        ActiveBuffParams p = StealthParams();
        return p.Cooldown;
    }

    private bool CanActivateStealth()
    {
        if (IsStealthed) return false;
        if (!StealthCooldownTimer.ExpiredOrNotRunning(Runner)) return false;
        ActiveBuffParams p = StealthParams();
        if (!p.Unlocked) return false;
        bool carrying = flagMarker != null && flagMarker.IsCarryingFlag();
        if (carrying && !p.UsableWhileCarryingFlag) return false;
        return true;
    }
}
```

- [ ] **Step 3: Verify compilation**

Return to Unity. Expected: Console shows no compile errors.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/Buffs/PlayerBuffs.cs" "Assets/Scripts/Player/NetInput.cs"
git commit -m "feat(buffs): networked PlayerBuffs (derived tiers + stealth activation)"
```

---

### Task 4: PlayerStatModifiers effective-stats facade

**Files:**
- Create: `Assets/Scripts/Buffs/PlayerStatModifiers.cs`

**Interfaces:**
- Consumes: `PlayerBuffs.BuildEffectiveStats`, `PlayerStats` (`maxAirJumps`, `dashCooldown`, `dashTime`), `EffectiveStats`.
- Produces:
  - `int EffectiveMaxAirJumps`
  - `bool UnlimitedAirJumps`
  - `float EffectiveDashCooldown`
  - `float EffectiveDashTime`
  - `bool DashDealsDamage`

- [ ] **Step 1: Write the facade**

Create `Assets/Scripts/Buffs/PlayerStatModifiers.cs`:

```csharp
using UnityEngine;
using Game.Buffs.Core;

/// <summary>
/// Read-only effective-stats view: base PlayerStats SO combined with the player's current buff
/// tiers from PlayerBuffs. NEVER mutates the shared SO. PlayerMovement/PlayerCombat read through
/// this; if PlayerBuffs is absent it returns the unbuffed base values.
/// </summary>
public class PlayerStatModifiers : MonoBehaviour
{
    [SerializeField] private PlayerStats stats;
    [Tooltip("Air-jump count reported when a buff grants unlimited air jumps.")]
    [SerializeField] private int unlimitedAirJumpSentinel = 99;

    private PlayerBuffs buffs;

    private void Awake()
    {
        buffs = GetComponent<PlayerBuffs>();
        if (stats == null) Debug.LogError("PlayerStatModifiers: PlayerStats not assigned.");
    }

    private EffectiveStats Current()
    {
        EffectiveStats es = EffectiveStats.Default();
        if (buffs != null) buffs.BuildEffectiveStats(ref es);
        return es;
    }

    public bool UnlimitedAirJumps => Current().UnlimitedAirJumps;

    public int EffectiveMaxAirJumps
    {
        get
        {
            EffectiveStats es = Current();
            return es.UnlimitedAirJumps ? unlimitedAirJumpSentinel : stats.maxAirJumps + es.BonusAirJumps;
        }
    }

    public float EffectiveDashCooldown => stats.dashCooldown * Current().DashCooldownMultiplier;
    public float EffectiveDashTime => stats.dashTime * Current().DashTimeMultiplier;
    public bool DashDealsDamage => Current().DashDealsDamage;
}
```

- [ ] **Step 2: Verify compilation**

Return to Unity. Expected: no compile errors.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Buffs/PlayerStatModifiers.cs"
git commit -m "feat(buffs): PlayerStatModifiers effective-stats facade"
```

---

### Task 5: Wire PlayerMovement to effective stats

**Files:**
- Modify: `Assets/Scripts/Player/PlayerMovement.cs`

**Interfaces:**
- Consumes: `PlayerStatModifiers` (Task 4).

- [ ] **Step 1: Add the modifier reference and resolve it in Spawned**

In `PlayerMovement.cs`, add a field near the component refs (after `private FlagCarrierMarker flagCarrierMarker;`):

```csharp
    private PlayerStatModifiers mods;
```

In `Spawned()`, after `flagCarrierMarker = GetComponent<FlagCarrierMarker>();`, add:

```csharp
        mods = GetComponent<PlayerStatModifiers>();
```

And change the air-jump init. Replace:

```csharp
            RemainingAirJumps = stats.maxAirJumps;
```

with:

```csharp
            RemainingAirJumps = mods != null ? mods.EffectiveMaxAirJumps : stats.maxAirJumps;
```

- [ ] **Step 2: Use effective air jumps on ground reset**

In `Simulate(...)`, replace the grounded reset:

```csharp
            RemainingAirJumps = stats.maxAirJumps;
```

with:

```csharp
            RemainingAirJumps = mods != null ? mods.EffectiveMaxAirJumps : stats.maxAirJumps;
```

- [ ] **Step 3: Don't consume air jumps when unlimited**

In `DoJump(bool grounded)`, replace the air-jump branch:

```csharp
        else if (RemainingAirJumps > 0)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, stats.jumpForce);
            RemainingAirJumps--;
        }
```

with:

```csharp
        else if (RemainingAirJumps > 0)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, stats.jumpForce);
            if (mods == null || !mods.UnlimitedAirJumps) RemainingAirJumps--;
        }
```

- [ ] **Step 4: Use effective dash time / cooldown**

In `StartDash()`, replace:

```csharp
        DashDurationTimer = TickTimer.CreateFromSeconds(Runner, stats.dashTime);
```

with:

```csharp
        DashDurationTimer = TickTimer.CreateFromSeconds(Runner, mods != null ? mods.EffectiveDashTime : stats.dashTime);
```

In `EndDash()`, replace:

```csharp
        DashCooldownTimer = TickTimer.CreateFromSeconds(Runner, stats.dashCooldown);
```

with:

```csharp
        DashCooldownTimer = TickTimer.CreateFromSeconds(Runner, mods != null ? mods.EffectiveDashCooldown : stats.dashCooldown);
```

- [ ] **Step 5: Use effective cooldown for the HUD bar**

In `GetDashCooldownPercent()`, replace the body:

```csharp
        if (stats.dashCooldown <= 0f) return 1f;
        float remaining = DashCooldownTimer.RemainingTime(Runner) ?? 0f;
        return 1f - Mathf.Clamp01(remaining / stats.dashCooldown);
```

with:

```csharp
        float effectiveCd = mods != null ? mods.EffectiveDashCooldown : stats.dashCooldown;
        if (effectiveCd <= 0f) return 1f;
        float remaining = DashCooldownTimer.RemainingTime(Runner) ?? 0f;
        return 1f - Mathf.Clamp01(remaining / effectiveCd);
```

- [ ] **Step 6: Verify compilation**

Return to Unity. Expected: no compile errors. (No in-editor behavior verification yet — the prefab is wired in Task 9.)

- [ ] **Step 7: Commit**

```bash
git add "Assets/Scripts/Player/PlayerMovement.cs"
git commit -m "feat(buffs): PlayerMovement reads effective air-jump/dash stats"
```

---

### Task 6: PlayerCombat dash-strike (dash-T3 damage)

**Files:**
- Modify: `Assets/Scripts/Player/PlayerCombat.cs`

**Interfaces:**
- Consumes: `PlayerStatModifiers.DashDealsDamage`, `PlayerMovement.IsDashing()`.

- [ ] **Step 1: Add the modifier reference**

In `PlayerCombat.cs`, add a field after `private PlayerMovement playerMovement;`:

```csharp
    private PlayerStatModifiers mods;
```

In `Awake()`, after `playerMovement = GetComponent<PlayerMovement>();`, add:

```csharp
        mods = GetComponent<PlayerStatModifiers>();
```

- [ ] **Step 2: Refactor the hit loop out of Attack into a reusable method**

In `Attack()`, replace this block:

```csharp
        // Damage + hit detection only on the server (avoids double-apply across clients).
        if (!HasStateAuthority) return;

        Collider2D[] objectsHit = Physics2D.OverlapBoxAll(
            attackTransform.position, attackArea, 0f, attackableLayer);

        foreach (Collider2D hit in objectsHit)
        {
            if (hitMarkerPrefab != null)
            {
                GameObject marker = Instantiate(hitMarkerPrefab, hit.transform.position, Quaternion.identity);
                SpriteRenderer sr = marker.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = hitMarkerColor;
                Destroy(marker, hitMarkerDuration);
            }

            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null)
            {
                Vector2 knockbackDirection = (hit.transform.position - transform.position).normalized;
                Vector2 knockbackForce = new Vector2(knockbackDirection.x * stats.attackForce, knockbackUpward);
                int finalDamage = ResolveMeleeDamage(hit.gameObject, hit.transform.position);
                enemy.TakeDamage(finalDamage, knockbackForce, hit.transform.position);
                continue;
            }

            // Player hit. Skip ourselves and friendly players (no melee friendly-fire),
            // mirroring the projectile's team check. Damage runs through the same RPC the
            // projectile uses so spawn-immunity / hit-cooldown are respected on the server.
            PlayerStatsHandler targetPlayer = hit.GetComponent<PlayerStatsHandler>();
            if (targetPlayer != null && targetPlayer != statsHandler)
            {
                PlayerTeamData targetTeam = hit.GetComponent<PlayerTeamData>();
                Team myTeam = teamComponent != null ? teamComponent.Team : Team.None;
                Team otherTeam = targetTeam != null ? targetTeam.Team : Team.None;
                if (!TeamUtil.AreEnemies(myTeam, otherTeam)) continue;

                int finalDamage = ResolveMeleeDamage(hit.gameObject, hit.transform.position);
                targetPlayer.RPC_TakeDamage(finalDamage);

                Rigidbody2D targetRb = hit.GetComponent<Rigidbody2D>();
                if (targetRb != null)
                {
                    Vector2 knockbackDirection = (hit.transform.position - transform.position).normalized;
                    targetRb.AddForce(new Vector2(knockbackDirection.x * stats.attackForce, knockbackUpward),
                                      ForceMode2D.Impulse);
                }
            }
        }
    }
```

with:

```csharp
        // Damage + hit detection only on the server (avoids double-apply across clients).
        if (!HasStateAuthority) return;

        ApplyMeleeHits(attackTransform.position, attackArea, spawnHitMarkers: true);
    }

    /// <summary>
    /// SERVER: overlap the given box and apply melee damage/knockback to enemies and enemy
    /// players. Shared by the normal swing and the dash-strike (Quicker Dash tier 3).
    /// </summary>
    private void ApplyMeleeHits(Vector2 center, Vector2 area, bool spawnHitMarkers)
    {
        Collider2D[] objectsHit = Physics2D.OverlapBoxAll(center, area, 0f, attackableLayer);

        foreach (Collider2D hit in objectsHit)
        {
            if (spawnHitMarkers && hitMarkerPrefab != null)
            {
                GameObject marker = Instantiate(hitMarkerPrefab, hit.transform.position, Quaternion.identity);
                SpriteRenderer sr = marker.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = hitMarkerColor;
                Destroy(marker, hitMarkerDuration);
            }

            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null)
            {
                Vector2 knockbackDirection = (hit.transform.position - transform.position).normalized;
                Vector2 knockbackForce = new Vector2(knockbackDirection.x * stats.attackForce, knockbackUpward);
                int finalDamage = ResolveMeleeDamage(hit.gameObject, hit.transform.position);
                enemy.TakeDamage(finalDamage, knockbackForce, hit.transform.position);
                continue;
            }

            // Player hit. Skip ourselves and friendly players (no melee friendly-fire). Damage
            // goes through RPC_TakeDamage so spawn-immunity / hit-cooldown are respected — which
            // also throttles the dash-strike's per-tick calls to one hit per 0.1s per target.
            PlayerStatsHandler targetPlayer = hit.GetComponent<PlayerStatsHandler>();
            if (targetPlayer != null && targetPlayer != statsHandler)
            {
                PlayerTeamData targetTeam = hit.GetComponent<PlayerTeamData>();
                Team myTeam = teamComponent != null ? teamComponent.Team : Team.None;
                Team otherTeam = targetTeam != null ? targetTeam.Team : Team.None;
                if (!TeamUtil.AreEnemies(myTeam, otherTeam)) continue;

                int finalDamage = ResolveMeleeDamage(hit.gameObject, hit.transform.position);
                targetPlayer.RPC_TakeDamage(finalDamage);

                Rigidbody2D targetRb = hit.GetComponent<Rigidbody2D>();
                if (targetRb != null)
                {
                    Vector2 knockbackDirection = (hit.transform.position - transform.position).normalized;
                    targetRb.AddForce(new Vector2(knockbackDirection.x * stats.attackForce, knockbackUpward),
                                      ForceMode2D.Impulse);
                }
            }
        }
    }
```

- [ ] **Step 3: Run the dash-strike each tick while dashing at tier 3**

In `Simulate(NetInput input, NetworkButtons pressed)`, after the shoot block (the final `}` of the `if (pressed.IsSet((int)PlayerButton.Shoot))` block), add:

```csharp
        // Quicker Dash tier 3: deal melee damage in the front swing box while dashing.
        if (HasStateAuthority && playerMovement != null && playerMovement.IsDashing()
            && mods != null && mods.DashDealsDamage && sideAttackPoint != null)
        {
            ApplyMeleeHits(sideAttackPoint.position, sideAttackArea, spawnHitMarkers: false);
        }
```

- [ ] **Step 4: Verify compilation**

Return to Unity. Expected: no compile errors.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Player/PlayerCombat.cs"
git commit -m "feat(buffs): dash-strike damage for Quicker Dash tier 3"
```

---

### Task 7: Stealth input binding + PlayerController wiring

**Files:**
- Modify: `Assets/Scripts/Player/NetInput.cs` (if `Stealth = 4` not already added in Task 3)
- Modify: `Assets/Scripts/Player/NetworkInputProvider.cs`
- Modify: `Assets/Scripts/Player/PlayerController.cs`

**Interfaces:**
- Consumes: `PlayerBuffs.Simulate` (Task 3), `PlayerButton.Stealth`.

- [ ] **Step 1: Confirm the enum value**

Ensure `Assets/Scripts/Player/NetInput.cs` contains `Stealth = 4` in `PlayerButton` (added in Task 3 Step 1). No change if already present.

- [ ] **Step 2: Bind the Stealth button in the input provider**

In `NetworkInputProvider.OnInput`, after the `shoot` bool line, add:

```csharp
        bool stealth = (keyboard != null && keyboard.qKey.isPressed) || (gamepad != null && gamepad.buttonEast.isPressed);
```

After `data.Buttons.Set((int)PlayerButton.Shoot, shoot);`, add:

```csharp
        data.Buttons.Set((int)PlayerButton.Stealth, stealth);
```

- [ ] **Step 3: Cache PlayerBuffs in PlayerController**

In `PlayerController.cs`, add a field after `private PlayerAnimator animator;`:

```csharp
    private PlayerBuffs buffs;
```

In `Awake()`, after `animator = GetComponent<PlayerAnimator>();`, add:

```csharp
        buffs = GetComponent<PlayerBuffs>();
```

- [ ] **Step 4: Call buffs.Simulate after combat**

In `FixedUpdateNetwork()`, after the line `if (combat.enabled) combat.Simulate(input, pressed);`, add:

```csharp
            if (buffs != null) buffs.Simulate(input, pressed);
```

- [ ] **Step 5: Verify compilation**

Return to Unity. Expected: no compile errors.

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/Player/NetInput.cs" "Assets/Scripts/Player/NetworkInputProvider.cs" "Assets/Scripts/Player/PlayerController.cs"
git commit -m "feat(buffs): bind Stealth (Q) input and drive PlayerBuffs.Simulate"
```

---

### Task 8: Deposit hook → buff progression

**Files:**
- Modify: `Assets/Scripts/Coin Scripts/HomeBase.cs`

**Interfaces:**
- Consumes: `PlayerBuffs.ServerAddDepositedValue` (Task 3).

- [ ] **Step 1: Credit deposited value to the depositing player's buffs**

In `NetworkedHomeBase.RPC_RequestDeposit`, inside `if (points > 0)`, after the `RPC_OnDeposit(...)` call (still inside the `scoreManager != null` block, after notifying clients), add:

```csharp
                    // Additive: credit the player's personal deposited-value total so buffs
                    // progress. Team scoring above is untouched.
                    PlayerBuffs buffs = playerNetObj.GetComponent<PlayerBuffs>();
                    if (buffs != null) buffs.ServerAddDepositedValue(points);
```

- [ ] **Step 2: Verify compilation**

Return to Unity. Expected: no compile errors.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Coin Scripts/HomeBase.cs"
git commit -m "feat(buffs): credit deposited value to PlayerBuffs on deposit"
```

---

### Task 9: Prefab wiring + first in-editor verification

**Files:**
- Modify (Unity Editor): `Assets/Scripts/Player/PlayerPrefab.prefab`

**Interfaces:**
- Consumes: all components from Tasks 3–8.

- [ ] **Step 1: Add components to the player prefab**

Open `PlayerPrefab` (the networked player prefab) in the Inspector. Add these components to the **root** GameObject (same object that has `PlayerMovement`/`PlayerCombat`):
1. `PlayerBuffs` — assign `Config` = the `BuffLoadoutConfig` asset.
2. `PlayerStatModifiers` — assign `Stats` = the same `PlayerStats` asset used by `PlayerMovement`/`PlayerCombat`.

Save the prefab.

- [ ] **Step 2: Confirm Fusion picks up the new NetworkBehaviour**

`PlayerBuffs` is a `NetworkBehaviour`, so it must be on a GameObject under the prefab's `NetworkObject`. Confirm the prefab's `NetworkObject` lists `PlayerBuffs` among its networked behaviours (Fusion auto-collects on save; if a "rebake"/"Networked Behaviours" list is shown, ensure it includes `PlayerBuffs`).

- [ ] **Step 3: Play-mode verification — passive dash/jump buffs**

Temporarily lower thresholds to make unlocks fast: select the `BuffLoadoutConfig` asset and set `Thresholds` to `1,2,3, 4,5,6, 7,8,9`. Enter Play mode (Host). With default loadout `[Jump, Stealth, Dash]`:
- Deposit 1 coin's worth of value → Jump T1: you can now air-jump one extra time.
- Deposit to total ≥ 3 → Dash T1: dash covers ~50% more distance (longer dash duration).
- Deposit to total ≥ 6 → Dash T2: dash cooldown visibly halved (HUD bar refills twice as fast).
- Deposit to total ≥ 7 → Jump T3: unlimited air jumps.

Expected: each effect appears right after the deposit crossing its threshold. Restore `Thresholds` to `5,10,15,30,45,60,120,180,240` afterward.

- [ ] **Step 4: Play-mode verification — stealth activation timing**

With Stealth unlocked (≥ tier 1), press `Q`. Expected: `PlayerBuffs.IsStealthed` flips true for the tier's duration (1s at T1), then false; pressing `Q` again within 20s does nothing (cooldown). (Visual fade comes in Task 10; for now confirm via the Inspector's networked `IsStealthed` on the player object, or a temporary `Debug.Log`.)

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Player/PlayerPrefab.prefab" "Assets/Settings/Buffs"
git commit -m "chore(buffs): wire PlayerBuffs/PlayerStatModifiers onto player prefab"
```

---

### Task 10: Stealth visual fade (Render)

**Files:**
- Create: `Assets/Scripts/Buffs/PlayerStealthVisual.cs`
- Modify (Unity Editor): `PlayerPrefab.prefab`

**Interfaces:**
- Consumes: `PlayerBuffs.IsStealthed`, `PlayerTeamData.Team`, `NetworkBehaviour.HasInputAuthority`.

- [ ] **Step 1: Write the stealth visual**

Create `Assets/Scripts/Buffs/PlayerStealthVisual.cs`:

```csharp
using UnityEngine;
using Fusion;

/// <summary>
/// Render-side stealth fade. Derives alpha from the networked PlayerBuffs.IsStealthed plus the
/// LOCAL viewer's team, so every client agrees on the state but renders its own perspective:
/// the owner/teammates see a light fade, enemies see near-invisible. Targets explicitly-assigned
/// BODY renderers (the prefab has multiple SpriteRenderers; do not auto-find).
/// </summary>
public class PlayerStealthVisual : NetworkBehaviour
{
    [Tooltip("Body sprite renderers to fade. Assign the visible body sprite(s), NOT the weapon.")]
    [SerializeField] private SpriteRenderer[] bodyRenderers;

    [SerializeField, Range(0f, 1f)] private float ownerAlpha = 0.5f;
    [SerializeField, Range(0f, 1f)] private float teammateAlpha = 0.5f;
    [SerializeField, Range(0f, 1f)] private float enemyAlpha = 0.05f;

    private PlayerBuffs buffs;
    private PlayerTeamData myTeam;

    public override void Spawned()
    {
        buffs = GetComponent<PlayerBuffs>();
        myTeam = GetComponent<PlayerTeamData>();
    }

    public override void Render()
    {
        if (buffs == null || bodyRenderers == null) return;
        float alpha = buffs.IsStealthed ? AlphaForLocalViewer() : 1f;
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            SpriteRenderer r = bodyRenderers[i];
            if (r == null) continue;
            Color c = r.color;
            // Preserve the death-dim (0.5) interaction by never raising above it would be wrong;
            // stealth simply overrides alpha while active, full opacity when inactive.
            c.a = alpha;
            r.color = c;
        }
    }

    private float AlphaForLocalViewer()
    {
        if (HasInputAuthority) return ownerAlpha; // this client owns the stealthed player

        Team viewer = LocalViewerTeam();
        Team mine = myTeam != null ? myTeam.Team : Team.None;
        if (viewer != Team.None && mine != Team.None && TeamUtil.AreEnemies(viewer, mine))
            return enemyAlpha;
        return teammateAlpha;
    }

    /// <summary>The team of the local input-authority player (the one looking at the screen).</summary>
    private Team LocalViewerTeam()
    {
        if (Runner == null) return Team.None;
        foreach (var po in Runner.GetAllBehaviours<PlayerTeamData>())
        {
            if (po != null && po.Object != null && po.Object.HasInputAuthority)
                return po.Team;
        }
        return Team.None;
    }
}
```

> If `Runner.GetAllBehaviours<T>()` is unavailable in this Fusion version, replace `LocalViewerTeam()` with a cached lookup: on `Spawned`, if `HasInputAuthority`, register this `PlayerTeamData` in a `static PlayerTeamData LocalPlayerTeam`; read that here. Verify the API in `PlayerCamera`/existing code first (it already self-finds the local player via `HasInputAuthority`).

- [ ] **Step 2: Wire it on the prefab**

Add `PlayerStealthVisual` to the player prefab root. Assign `Body Renderers` = the visible body `SpriteRenderer` (the one driven by `Player.controller` — the same renderer the body Animator targets), NOT the weapon renderer. Save.

- [ ] **Step 3: Play-mode verification**

Enter Play mode (Host), unlock Stealth, press `Q`. Expected: the owner's body fades to ~0.5 alpha for the duration, then returns to full. (Enemy-perspective near-invisibility requires a second client; with one local player the owner path is what's observable.)

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/Buffs/PlayerStealthVisual.cs" "Assets/Scripts/Player/PlayerPrefab.prefab"
git commit -m "feat(buffs): stealth render-side fade by viewer team"
```

---

### Task 11: EnemyAI ignores stealthed players

**Files:**
- Modify: `Assets/Scripts/Enemy/Base/EnemyAI.cs`

**Interfaces:**
- Consumes: `PlayerBuffs.IsStealthed`.

- [ ] **Step 1: Skip stealthed players during detection**

In `EnemyAI.CheckForPlayers()`, replace:

```csharp
            PlayerStatsHandler player = DetectionResults[i].GetComponent<PlayerStatsHandler>();
            if (player != null && !player.IsPlayerDead())
            {
```

with:

```csharp
            PlayerStatsHandler player = DetectionResults[i].GetComponent<PlayerStatsHandler>();
            PlayerBuffs buffs = DetectionResults[i].GetComponent<PlayerBuffs>();
            bool stealthed = buffs != null && buffs.IsStealthed;
            if (player != null && !player.IsPlayerDead() && !stealthed)
            {
```

- [ ] **Step 2: Drop a target that becomes stealthed**

In `EnemyAI.CheckIfPlayerEscaped()`, replace:

```csharp
        PlayerStatsHandler player = currentPlayer.GetComponent<PlayerStatsHandler>();
        if (distance > detectionRange || (player != null && player.IsPlayerDead()))
        {
            currentPlayer = null;
            currentState = State.Patrolling;
        }
```

with:

```csharp
        PlayerStatsHandler player = currentPlayer.GetComponent<PlayerStatsHandler>();
        PlayerBuffs buffs = currentPlayer.GetComponent<PlayerBuffs>();
        bool stealthed = buffs != null && buffs.IsStealthed;
        if (distance > detectionRange || stealthed || (player != null && player.IsPlayerDead()))
        {
            currentPlayer = null;
            currentState = State.Patrolling;
        }
```

- [ ] **Step 3: Play-mode verification**

Enter Play mode near an enemy, unlock Stealth, let the enemy start chasing, then press `Q`. Expected: the enemy loses the target and returns to patrol for the stealth duration; while stealthed it will not begin a new chase.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/Enemy/Base/EnemyAI.cs"
git commit -m "feat(buffs): enemies ignore stealthed players"
```

---

### Task 12: Lobby loadout channel + spawn init

**Files:**
- Modify: `Assets/Scripts/GameNetworkManager.cs`
- Modify: `Assets/Scripts/NetworkSpawnManager.cs`

**Interfaces:**
- Consumes: `PlayerBuffs.ServerInitLoadout` (Task 3), `LobbyTeamChoices` pattern.
- Produces:
  - `static class LobbyLoadoutChoices { Set(PlayerRef, byte[]); bool TryGet(PlayerRef, out byte[]); Remove(PlayerRef); Clear(); }`
  - `GameNetworkManager.SubmitLocalLoadoutChoice(byte[] order)`

- [ ] **Step 1: Add the LobbyLoadoutChoices store**

At the bottom of `GameNetworkManager.cs`, after the `LobbyTeamChoices` class, add:

```csharp
/// <summary>
/// Per-player buff loadout (priority order as BuffId bytes) collected by the host during the lobby,
/// parallel to LobbyTeamChoices. Read by NetworkedSpawnManager on the host to initialise each
/// player's PlayerBuffs. A missing entry falls back to the BuffLoadoutConfig default order.
/// </summary>
public static class LobbyLoadoutChoices
{
    private static readonly Dictionary<PlayerRef, byte[]> choices = new Dictionary<PlayerRef, byte[]>();

    public static void Set(PlayerRef player, byte[] order) => choices[player] = order;
    public static bool TryGet(PlayerRef player, out byte[] order) => choices.TryGetValue(player, out order);
    public static void Remove(PlayerRef player) => choices.Remove(player);
    public static void Clear() => choices.Clear();
}
```

- [ ] **Step 2: Add a reliable-data key for loadout**

In `GameNetworkManager`, after the `TeamChoiceKey` field, add:

```csharp
    // Reliable-data channel tag for a client sending its buff loadout to the host.
    private static readonly Fusion.Sockets.ReliableKey LoadoutKey =
        Fusion.Sockets.ReliableKey.FromInts(0x4C4F4144, 0x55, 0, 0); // "LOAD"
```

- [ ] **Step 3: Add SubmitLocalLoadoutChoice**

In `GameNetworkManager`, after `SubmitLocalTeamChoice`, add:

```csharp
    /// <summary>
    /// Called by TeamSelectionUI when the local player confirms their buff order. Records on the host
    /// (directly if we are the host, else over reliable-data), parallel to SubmitLocalTeamChoice.
    /// </summary>
    public void SubmitLocalLoadoutChoice(byte[] order)
    {
        if (order == null || runner == null || !runner.IsRunning) return;

        if (runner.IsServer)
            LobbyLoadoutChoices.Set(runner.LocalPlayer, order);
        else
            runner.SendReliableDataToServer(LoadoutKey, order);
    }
```

- [ ] **Step 4: Receive loadout on the host**

In `OnReliableDataReceived`, replace:

```csharp
        // Host receives clients' team choices here (clients use SendReliableDataToServer).
        if (!runner.IsServer || key != TeamChoiceKey)
            return;

        if (data.Count < 1 || data.Array == null)
        {
            Debug.LogError($"❌ [HOST] Empty team-choice payload from Player {player.PlayerId}");
            return;
        }

        int teamNumber = data.Array[data.Offset];
        RecordChoice(player, teamNumber);
```

with:

```csharp
        if (!runner.IsServer) return;

        if (key == TeamChoiceKey)
        {
            if (data.Count < 1 || data.Array == null)
            {
                Debug.LogError($"❌ [HOST] Empty team-choice payload from Player {player.PlayerId}");
                return;
            }
            int teamNumber = data.Array[data.Offset];
            RecordChoice(player, teamNumber);
            return;
        }

        if (key == LoadoutKey)
        {
            if (data.Count < 1 || data.Array == null) return;
            var order = new byte[data.Count];
            System.Array.Copy(data.Array, data.Offset, order, 0, data.Count);
            LobbyLoadoutChoices.Set(player, order);
        }
```

- [ ] **Step 5: Clear loadout choices alongside team choices**

In `Start()` replace `LobbyTeamChoices.Clear();` with:

```csharp
        LobbyTeamChoices.Clear();
        LobbyLoadoutChoices.Clear();
```

In `OnShutdown` replace `LobbyTeamChoices.Clear();` with:

```csharp
        LobbyTeamChoices.Clear();
        LobbyLoadoutChoices.Clear();
```

In `OnPlayerLeft`, after `LobbyTeamChoices.Remove(player);`, add:

```csharp
            LobbyLoadoutChoices.Remove(player);
```

- [ ] **Step 6: Initialise the loadout at spawn**

In `NetworkSpawnManager.cs`, in `OnPlayerSpawned`, after the `teamData.SetTeam(...)` block, add:

```csharp
        // Initialise the player's buff loadout from their lobby choice (host-authoritative).
        PlayerBuffs buffs = obj.GetComponent<PlayerBuffs>();
        if (buffs != null && LobbyLoadoutChoices.TryGet(obj.InputAuthority, out byte[] order))
            buffs.ServerInitLoadout(order);
        // If no lobby choice, PlayerBuffs.Spawned applies the config default order.
```

- [ ] **Step 7: Verify compilation**

Return to Unity. Expected: no compile errors.

- [ ] **Step 8: Commit**

```bash
git add "Assets/Scripts/GameNetworkManager.cs" "Assets/Scripts/NetworkSpawnManager.cs"
git commit -m "feat(buffs): lobby loadout reliable channel + spawn-time init"
```

---

### Task 13: Loadout reorder picker UI + end-to-end verification

**Files:**
- Modify: `Assets/Scripts/Player/Teamselectionui.cs`
- Modify (Unity Editor): the MainMenu scene's team-selection panel.

**Interfaces:**
- Consumes: `GameNetworkManager.SubmitLocalLoadoutChoice`, `BuffLoadoutConfig`, `BuffId`.

- [ ] **Step 1: Add loadout state + UI references to TeamSelectionUI**

In `TeamSelectionUI`, add fields after the `networkManager` field:

```csharp
    [Header("🧪 Loadout Picker")]
    [Tooltip("The buff loadout config (same asset used by the player prefab).")]
    [SerializeField] private BuffLoadoutConfig buffConfig;
    [Tooltip("One row per loadout slot, top = highest priority. Each needs a label + Up/Down buttons.")]
    [SerializeField] private Text[] slotLabels;
    [SerializeField] private Button[] slotUpButtons;
    [SerializeField] private Button[] slotDownButtons;

    private System.Collections.Generic.List<Game.Buffs.Core.BuffId> loadoutOrder;
```

- [ ] **Step 2: Initialise the order and wire reorder buttons in Start**

In `Start()`, before the end of the method, add:

```csharp
        InitLoadoutOrder();
        WireLoadoutButtons();
        RefreshLoadoutLabels();
```

Then add these methods to the class:

```csharp
    private void InitLoadoutOrder()
    {
        loadoutOrder = new System.Collections.Generic.List<Game.Buffs.Core.BuffId>();
        if (buffConfig != null && buffConfig.DefaultOrder != null)
        {
            foreach (var id in buffConfig.DefaultOrder) loadoutOrder.Add(id);
        }
    }

    private void WireLoadoutButtons()
    {
        if (slotUpButtons != null)
            for (int i = 0; i < slotUpButtons.Length; i++)
            {
                int idx = i;
                if (slotUpButtons[i] != null) slotUpButtons[i].onClick.AddListener(() => MoveSlot(idx, -1));
            }
        if (slotDownButtons != null)
            for (int i = 0; i < slotDownButtons.Length; i++)
            {
                int idx = i;
                if (slotDownButtons[i] != null) slotDownButtons[i].onClick.AddListener(() => MoveSlot(idx, +1));
            }
    }

    private void MoveSlot(int index, int delta)
    {
        if (loadoutOrder == null) return;
        int target = index + delta;
        if (index < 0 || index >= loadoutOrder.Count || target < 0 || target >= loadoutOrder.Count) return;
        (loadoutOrder[index], loadoutOrder[target]) = (loadoutOrder[target], loadoutOrder[index]);
        RefreshLoadoutLabels();
    }

    private void RefreshLoadoutLabels()
    {
        if (slotLabels == null || loadoutOrder == null || buffConfig == null) return;
        for (int i = 0; i < slotLabels.Length; i++)
        {
            if (slotLabels[i] == null) continue;
            if (i < loadoutOrder.Count)
            {
                var def = buffConfig.GetById(loadoutOrder[i]);
                slotLabels[i].text = $"{i + 1}. {(def != null ? def.DisplayName : loadoutOrder[i].ToString())}";
            }
            else slotLabels[i].text = "";
        }
    }

    private byte[] LoadoutAsBytes()
    {
        if (loadoutOrder == null) return null;
        var bytes = new byte[loadoutOrder.Count];
        for (int i = 0; i < loadoutOrder.Count; i++) bytes[i] = (byte)loadoutOrder[i];
        return bytes;
    }
```

- [ ] **Step 3: Submit the loadout alongside the team choice**

In `OnTeamButtonClicked`, replace:

```csharp
        networkManager.SubmitLocalTeamChoice(teamNumber);
        SetButtonsInteractable(false);
```

with:

```csharp
        networkManager.SubmitLocalLoadoutChoice(LoadoutAsBytes());
        networkManager.SubmitLocalTeamChoice(teamNumber);
        SetButtonsInteractable(false);
        SetLoadoutInteractable(false);
```

Then add the helper:

```csharp
    private void SetLoadoutInteractable(bool interactable)
    {
        if (slotUpButtons != null)
            foreach (var b in slotUpButtons) if (b != null) b.interactable = interactable;
        if (slotDownButtons != null)
            foreach (var b in slotDownButtons) if (b != null) b.interactable = interactable;
    }
```

Also re-enable it when the panel is (re)shown: in `ShowTeamSelection`, after `SetButtonsInteractable(true);`, add:

```csharp
        SetLoadoutInteractable(true);
        RefreshLoadoutLabels();
```

- [ ] **Step 4: Build the UI in the MainMenu scene**

In the team-selection panel, add a "Loadout" sub-panel with **3 rows**, each row containing: a `Text` label and two `Button`s (▲ Up, ▼ Down). Wire on the `TeamSelectionUI` component:
- `Buff Config` = the `BuffLoadoutConfig` asset.
- `Slot Labels` = the 3 row labels (top to bottom = priority 1→3).
- `Slot Up Buttons` / `Slot Down Buttons` = the matching buttons per row, same order as labels.

- [ ] **Step 5: Verify compilation + UI behavior**

Return to Unity, enter Play mode → Host → team selection. Expected: the 3 buffs show in default order `Extra Jump / Stealth / Quicker Dash`; Up/Down reorders them and labels renumber; picking a team locks the rows.

- [ ] **Step 6: End-to-end verification**

With temporary low thresholds (`1,2,3,4,5,6,7,8,9`): reorder loadout to `Stealth / Quicker Dash / Extra Jump`, pick a team, start the match, and deposit coins. Expected unlock order matches the new priority: Stealth T1 first (Q activates a 1s stealth), then Quicker Dash T1, then Extra Jump T1, then Stealth T2 (3s), etc. Restore real thresholds afterward.

- [ ] **Step 7: Commit**

```bash
git add "Assets/Scripts/Player/Teamselectionui.cs"
git commit -m "feat(buffs): lobby loadout reorder picker + submit with team choice"
```

---

## Self-Review

**Spec coverage:**
- Loadout priority ordering + reorder picker → Tasks 12, 13. ✓
- Round-robin tiered unlock, 9 thresholds (value-based) → Tasks 1, 2, 3. ✓
- Tier tables (jump +1/+2/unlimited; stealth 1/3/10s + flag@T3; dash +50% range / +cooldown ×0.5 / +front damage — revised 2026-06-26) → Task 2 (data), Tasks 5/6 (passive application), Task 3 (stealth params/flag gate). ✓
- Effective-stats facade, never mutate SO → Task 4; consumed in Tasks 5, 6. ✓
- Stealth active via TickTimer + networked flag, flat 20s cooldown, flag gating → Task 3. ✓
- Stealth input via NetInput/PlayerButton → Task 7. ✓
- Stealth visual in Render, explicit body renderers, viewer-team alpha → Task 10. ✓
- AI ignores stealthed players → Task 11. ✓
- Deposit hook additive, TeamScoreManager untouched → Task 8. ✓
- Spawn-time loadout init, default fallback → Tasks 3 (default), 12 (spawn). ✓
- Pure logic unit-tested → Task 1. ✓
- Adding a 4th buff = new subclass + asset → Task 2 structure. ✓

**Placeholder scan:** No TBD/TODO. The one conditional (Fusion `GetAllBehaviours` availability) in Task 10 includes a concrete fallback. ✓

**Type consistency:** `BuffId`, `EffectiveStats`, `ActiveBuffParams`, `BuffUnlock.UnlockedSteps/TierLevel`, `PlayerBuffs.{IsStealthed,TierOf,BuildEffectiveStats,ServerInitLoadout,ServerAddDepositedValue,Simulate}`, `PlayerStatModifiers.{EffectiveMaxAirJumps,UnlimitedAirJumps,EffectiveDashCooldown,EffectiveDashTime,DashDealsDamage}`, `LobbyLoadoutChoices`, `SubmitLocalLoadoutChoice` are named identically across all tasks that produce/consume them. ✓

**Known cross-task ordering:** `PlayerButton.Stealth` is introduced in Task 3 Step 1 (needed for `PlayerBuffs` to compile) and re-confirmed in Task 7; this is intentional and called out in both tasks.
