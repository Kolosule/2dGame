# Individual Buff Layer — Implementation Plan (Scope 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Grow the individual deposit-earned buff catalog from three buffs to four (adding Flag Runner), make `MaxTier` an authored, loudly-validated field instead of a fragile division, and retune the unlock curve to 12 steps.

**Architecture:** The individual layer's existing shape is kept exactly: `PlayerBuffs` networks only `TotalDepositedValue` + `LoadoutOrder`, and every tier is **derived on query** through the pure, engine-free `BuffUnlock` helper in `Game.Buffs.Core`. This plan adds one new `BuffDefinition` subclass, two new `EffectiveStats` fields, one pure config-validation rule, one pure loadout byte codec (extracted so the 4-entry round-trip is testable), and an authored `maxTier` on `BuffLoadoutConfig`. No new networked state of any kind.

**Tech Stack:** Unity 6.3, Photon Fusion 2 (Host/Client + dedicated server), C#, NUnit EditMode tests, Unity's bundled Roslyn for out-of-editor compile/test.

**Spec:** [docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md](../specs/2026-07-29-coins-buffs-economy-design.md) — sections "Individual buff catalog", "Loadout UX", "`MaxTier` becomes explicit", "Individual curve". The earlier [2026-06-24 spec](../specs/2026-06-24-deposit-earned-buffs-design.md) remains accurate for the existing three buffs.

## Global Constraints

- **Tiers are DERIVED, never stored.** Tier is a pure function of `[Networked] TotalDepositedValue` + `LoadoutOrder` via `BuffUnlock`. This is what makes the system resimulation-safe. **Add no per-buff networked tier fields.**
- **`PlayerStatModifiers` must NEVER mutate the shared `PlayerStats` ScriptableObject.** It is a read-only combining view.
- **Gameplay in `Simulate()`, visuals in `Render()`.** `TickTimer` for all simulation timing; no `Time.time` in simulation paths.
- **Carrying state in simulation must come from `CTFGameManager.IsCarrying(PlayerRef)`** (networked flag state), never from the render-path `FlagCarrierMarker` bool.
- **New Input System only**; no `Keyboard.current` reads in simulation paths.
- **`Game.Buffs.Core` has `noEngineReferences: true`.** Anything added there must be engine-free C# (no `UnityEngine`), which is exactly what makes it testable outside Unity.
- **The player prefab has TWO Animators** — `GetComponentInChildren<Animator>()` returns the weapon one, not the visible body. Wire body renderers/animators explicitly if you touch visuals. (This plan touches no visuals.)
- **Design constraint — movement and utility ONLY.** No attack damage, no max health, no base move speed. Stealth remains the **only** active ability; add no input bindings and no new active-ability timers.
- **Out of scope, do not touch:** territory and team buffs (Scope 2), match phases (Scope 1), all HUD work (Scope 4), coin drop rates (Scope 4).

## Design decisions locked in here

Two small extractions are needed to make the required verification possible. Both are recorded here so they are not mistaken for scope creep:

1. **`EffectiveStats.CanDashWhileCarryingFlag`** (bool) is added alongside `CarrySpeedMultiplier`. The spec calls Flag Runner's T3 "lifts the existing carry-blocks-dash gate"; the gate lives in `PlayerMovement.Simulate`, so the tier must reach it through the same `EffectiveStats` channel every other passive uses. This is one bool, not a new mechanism.
2. **`LoadoutCodec`** (pure, in `Game.Buffs.Core`) is extracted from the two places that currently hand-roll the same `BuffId[] ↔ byte[]` loop (`PlayerBuffs.ToBytes`, `LobbyScreenUI.LoadoutAsBytes`). The prompt requires a "loadout byte round-tripping with 4 entries" test, and neither existing copy is reachable from an engine-free test assembly. This is a DRY extraction, not a redesign.

## File Structure

**Create:**
- `Assets/Scripts/Buffs/Core/LoadoutCodec.cs` — pure `BuffId[] ↔ byte[]` conversion with the 8-entry cap. Engine-free.
- `Assets/Scripts/Buffs/FlagRunnerBuffDefinition.cs` (+ `.meta`) — the fourth buff's tier table.
- `Assets/Settings/Buffs/FlagRunnerBuff.asset` (+ `.meta`) — its authored instance.
- `Assets/Scripts/Editor/LoadoutPickerBuilder.cs` (+ `.meta`) — one-click editor tool that adds the 4th row to the lobby picker in `MainMenu.unity` and rewires the three slot arrays.
- `Assets/Tests/EditMode/LoadoutCodecTests.cs` (+ `.meta`) — round-trip tests.

**Modify:**
- `Assets/Scripts/Buffs/Core/BuffId.cs` — add `FlagRunner = 3`.
- `Assets/Scripts/Buffs/Core/BuffUnlock.cs` — add the pure `IsCurveComplete` validation rule.
- `Assets/Scripts/Buffs/Core/EffectiveStats.cs` — add `CarrySpeedMultiplier`, `CanDashWhileCarryingFlag`.
- `Assets/Scripts/Buffs/BuffLoadoutConfig.cs` — authored `maxTier`, loud validation, 12-step default curve, 4-entry default order.
- `Assets/Scripts/Buffs/PlayerBuffs.cs` — use `LoadoutCodec`; log a loud config error once on spawn.
- `Assets/Scripts/Buffs/PlayerStatModifiers.cs` — surface `EffectiveWalkSpeed(bool carrying)` and `CanDashWhileCarryingFlag`.
- `Assets/Scripts/Player/PlayerMovement.cs` — hoist the carrying check, scale walk speed while carrying, let T3 lift the dash gate.
- `Assets/Scripts/UI/LobbyScreenUI.cs` — use `LoadoutCodec`.
- `Assets/Settings/Buffs/BuffLoadoutConfig.asset` — `maxTier`, 12-step thresholds, 4-entry `allBuffs` and `defaultOrder`.
- `Assets/Tests/EditMode/BuffUnlockTests.cs` — the 12-step curve, the 4-buff round-robin, the validation rule.

**Do not modify:** `CombatConfig`, `TeamManager`, `TeamScoreManager`, `MatchManager`, any file under `Assets/Scripts/Hud/`, `Enemy.cs`, `DifficultyRingConfig.cs`.

---

### Task 1: Pure curve-validation rule

The footgun: `BuffLoadoutConfig.MaxTier` is currently `thresholds.Length / BuffCount`, so adding a fourth buff without three more thresholds silently drops every buff from tier 3 to tier 2. The rule that catches it is pure arithmetic, so it goes in the engine-free core where it can be tested outside Unity.

**Files:**
- Modify: `Assets/Scripts/Buffs/Core/BuffUnlock.cs`
- Test: `Assets/Tests/EditMode/BuffUnlockTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static bool BuffUnlock.IsCurveComplete(int thresholdCount, int buffCount, int maxTier)` — true only when `buffCount > 0 && maxTier > 0 && thresholdCount == maxTier * buffCount`. Task 2 calls it from `BuffLoadoutConfig`.

- [x] **Step 1: Write the failing test**

Append to `Assets/Tests/EditMode/BuffUnlockTests.cs`, inside the `BuffUnlockTests` class:

```csharp
    // The curve must contain exactly one threshold per (buff x tier) cell. Anything else
    // silently mis-tiers every buff, which is the footgun this rule exists to catch.
    [TestCase(9, 3, 3, true)]    // the pre-Flag-Runner catalog
    [TestCase(12, 4, 3, true)]   // the four-buff catalog
    [TestCase(9, 4, 3, false)]   // 4th buff added, thresholds forgotten — the exact footgun
    [TestCase(13, 4, 3, false)]  // one threshold too many
    [TestCase(0, 0, 3, false)]   // no buffs authored
    [TestCase(0, 4, 0, false)]   // maxTier left at zero (an unserialized field deserializes to 0)
    public void IsCurveComplete_RequiresOneThresholdPerBuffPerTier(
        int thresholdCount, int buffCount, int maxTier, bool expected)
    {
        Assert.AreEqual(expected, BuffUnlock.IsCurveComplete(thresholdCount, buffCount, maxTier));
    }
```

- [x] **Step 2: Run the test to verify it fails**

The Unity editor usually holds the project lock, so run the pure suite with Unity's bundled Roslyn (see `docs/` and the workaround below). From the repo root in git-bash:

```bash
UNITY="/c/Program Files/Unity/Hub/Editor/6000.3.0f1/Editor/Data"; "$UNITY/NetCoreRuntime/dotnet.exe" exec "$UNITY/DotNetSdkRoslyn/csc.dll" -nologo -target:library -out:/dev/null "Assets/Scripts/Buffs/Core/BuffUnlock.cs"
```

Expected: this compiles (the test file is not compiled by it). The real gate is Step 4 — at this point simply confirm `IsCurveComplete` does **not** exist:

```bash
grep -c "IsCurveComplete" Assets/Scripts/Buffs/Core/BuffUnlock.cs
```

Expected: `0`

- [x] **Step 3: Write the minimal implementation**

Add to `BuffUnlock` in `Assets/Scripts/Buffs/Core/BuffUnlock.cs`, after `TierLevel`:

```csharp
        /// <summary>
        /// True when the threshold list holds exactly one entry per (buff x tier) cell. A
        /// mismatch mis-tiers every buff — adding a 4th buff without 3 more thresholds used to
        /// silently demote everyone from tier 3 to tier 2 — so callers must fail loudly on false.
        /// </summary>
        public static bool IsCurveComplete(int thresholdCount, int buffCount, int maxTier)
        {
            if (buffCount <= 0 || maxTier <= 0) return false;
            return thresholdCount == maxTier * buffCount;
        }
```

- [x] **Step 4: Run the pure test harness to verify it passes**

Write the throwaway harness to the scratchpad (this is the project's established editor-locked workaround — a plain-assert translation of the NUnit cases, compiled and run on Unity's bundled .NET runtime):

```bash
SP="$LOCALAPPDATA/Temp/claude/C--Users-1-Documents-GitHub-2dGame"; mkdir -p "$SP/buffcheck"; cat > "$SP/buffcheck/H.cs" <<'EOF'
using System;
using System.Collections.Generic;
using Game.Buffs.Core;
static class H {
  static int f = 0;
  static void Eq(int exp, int got, string what){ if(exp!=got){ Console.WriteLine($"FAIL {what}: expected {exp} got {got}"); f++; } }
  static void EqB(bool exp, bool got, string what){ if(exp!=got){ Console.WriteLine($"FAIL {what}: expected {exp} got {got}"); f++; } }
  static int Main(){
    EqB(true,  BuffUnlock.IsCurveComplete(9,3,3),  "curve 9/3/3");
    EqB(true,  BuffUnlock.IsCurveComplete(12,4,3), "curve 12/4/3");
    EqB(false, BuffUnlock.IsCurveComplete(9,4,3),  "curve 9/4/3");
    EqB(false, BuffUnlock.IsCurveComplete(13,4,3), "curve 13/4/3");
    EqB(false, BuffUnlock.IsCurveComplete(0,0,3),  "curve 0/0/3");
    EqB(false, BuffUnlock.IsCurveComplete(0,4,0),  "curve 0/4/0");
    Console.WriteLine(f==0 ? "ALL PASS" : $"{f} FAILURES");
    return f==0?0:1;
  }
}
EOF
cat > "$SP/buffcheck/H.runtimeconfig.json" <<'EOF'
{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"6.0.0"}}}
EOF
UNITY="/c/Program Files/Unity/Hub/Editor/6000.3.0f1/Editor/Data"; NS=$(cygpath -w "$UNITY/NetStandard/ref/2.1.0/netstandard.dll"); "$UNITY/NetCoreRuntime/dotnet.exe" exec "$UNITY/DotNetSdkRoslyn/csc.dll" -nologo -noconfig -nostdlib -target:exe -r:"$NS" -out:"$(cygpath -w "$SP/buffcheck/H.exe")" "Assets/Scripts/Buffs/Core/BuffUnlock.cs" "$(cygpath -w "$SP/buffcheck/H.cs")" && "$UNITY/NetCoreRuntime/dotnet.exe" "$SP/buffcheck/H.exe"
```

Expected output: `ALL PASS`

If the Unity editor is closed, prefer the real runner instead and skip the harness:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.0f1/Unity.exe" -batchmode -projectPath . -runTests -testPlatform EditMode -testFilter BuffUnlockTests -logFile -
```

- [x] **Step 5: Commit**

```bash
git add Assets/Scripts/Buffs/Core/BuffUnlock.cs Assets/Tests/EditMode/BuffUnlockTests.cs && git commit -m "feat(buffs): add pure curve-completeness rule to BuffUnlock"
```

---

### Task 2: Authored `MaxTier` on `BuffLoadoutConfig`, validated loudly

`MaxTier` stops being a division and becomes a serialized field. **Critical Unity detail:** a serialized field absent from a `.asset`'s YAML deserializes to `0`, **not** to its C# initializer — so the asset edit in Step 3 is mandatory, not cosmetic. Task 1's `IsCurveComplete(…, maxTier: 0)` returning `false` is what catches that mistake.

At this point the catalog is still three buffs and nine thresholds, so the config stays valid: `9 == 3 × 3`.

**Files:**
- Modify: `Assets/Scripts/Buffs/BuffLoadoutConfig.cs`
- Modify: `Assets/Settings/Buffs/BuffLoadoutConfig.asset`
- Modify: `Assets/Scripts/Buffs/PlayerBuffs.cs:45-49`

**Interfaces:**
- Consumes: `BuffUnlock.IsCurveComplete(int, int, int)` from Task 1.
- Produces: `BuffLoadoutConfig.MaxTier` (now the authored field), `BuffLoadoutConfig.IsCurveValid` (bool), `BuffLoadoutConfig.LogIfCurveInvalid()` (void, one-shot per session). Task 5 rewrites this file's default curve values.

- [x] **Step 1: Replace the derived `MaxTier` with an authored field plus validation**

In `Assets/Scripts/Buffs/BuffLoadoutConfig.cs`, replace lines 14-24 (the `thresholds` header through the `MaxTier` property) with:

```csharp
    [Header("Cumulative deposited-value thresholds — exactly maxTier x buffCount entries, ascending")]
    [SerializeField] private int[] thresholds = { 5, 10, 15, 30, 45, 60, 120, 180, 240 };

    [Header("Default priority order if a player submits none")]
    [SerializeField] private BuffId[] defaultOrder = { BuffId.ExtraJump, BuffId.Stealth, BuffId.QuickerDash };

    [Header("Highest tier any buff can reach. AUTHORED, not derived — see IsCurveValid.")]
    [SerializeField] private int maxTier = 3;

    public BuffDefinition[] AllBuffs => allBuffs;
    public int[] Thresholds => thresholds;
    public BuffId[] DefaultOrder => defaultOrder;
    public int BuffCount => allBuffs != null ? allBuffs.Length : 0;
    public int MaxTier => maxTier;

    /// <summary>
    /// The threshold list must hold exactly one entry per (buff x tier) cell. Authoring a new
    /// buff without extending the curve used to silently demote every buff a whole tier; now it
    /// is a loud error instead.
    /// </summary>
    public bool IsCurveValid =>
        BuffUnlock.IsCurveComplete(thresholds != null ? thresholds.Length : 0, BuffCount, maxTier);

    [System.NonSerialized] private bool curveErrorLogged;

    /// <summary>Logs the curve error at most once per session (callers hit this per player spawn).</summary>
    public void LogIfCurveInvalid()
    {
        if (IsCurveValid || curveErrorLogged) return;
        curveErrorLogged = true;
        Debug.LogError(
            $"BuffLoadoutConfig '{name}': thresholds.Length ({(thresholds != null ? thresholds.Length : 0)}) " +
            $"must equal maxTier ({maxTier}) x buffCount ({BuffCount}). Every buff tier is wrong until this is fixed.");
    }

    private void OnValidate()
    {
        curveErrorLogged = false;
        LogIfCurveInvalid();
    }
```

Leave lines 1-13 and `GetById` unchanged.

- [x] **Step 2: Make `PlayerBuffs` surface the error at runtime too**

`OnValidate` only fires in the editor. A dedicated-server build with a bad asset must still say so. In `Assets/Scripts/Buffs/PlayerBuffs.cs`, replace `Spawned()` (lines 45-49):

```csharp
    public override void Spawned()
    {
        if (!HasStateAuthority) return;
        if (config != null) config.LogIfCurveInvalid();
        if (LoadoutLength == 0) ApplyDefaultLoadout();
    }
```

- [x] **Step 3: Add `maxTier` to the config asset**

In `Assets/Settings/Buffs/BuffLoadoutConfig.asset`, add a `maxTier: 3` line after the `defaultOrder` line, so the tail of the file reads exactly:

```yaml
  thresholds: 050000000a0000000f0000001e0000002d0000003c00000078000000b4000000f0000000
  defaultOrder: 000201
  maxTier: 3
```

Without this line Unity deserializes `maxTier` as `0` and every buff reports tier 0.

- [x] **Step 4: Verify the whole compile surface still builds**

```bash
grep -n "maxTier" Assets/Settings/Buffs/BuffLoadoutConfig.asset Assets/Scripts/Buffs/BuffLoadoutConfig.cs
```

Expected: `maxTier: 3` present in the asset; `maxTier` field and `MaxTier => maxTier` present in the script. Then run the whole-surface Roslyn compile gate described in Task 8, Step 1. Expected: no errors.

- [x] **Step 5: Commit**

```bash
git add Assets/Scripts/Buffs/BuffLoadoutConfig.cs Assets/Scripts/Buffs/PlayerBuffs.cs Assets/Settings/Buffs/BuffLoadoutConfig.asset && git commit -m "fix(buffs): make MaxTier an authored, validated field"
```

---

### Task 3: Flag Runner — id, stats channel, definition, asset

The buff is authored and its asset created, but it is **not** added to `allBuffs` yet. The catalog stays at three buffs / nine thresholds so the config remains valid; Task 5 flips both together in one commit.

**Files:**
- Modify: `Assets/Scripts/Buffs/Core/BuffId.cs`
- Modify: `Assets/Scripts/Buffs/Core/EffectiveStats.cs`
- Create: `Assets/Scripts/Buffs/FlagRunnerBuffDefinition.cs`
- Create: `Assets/Scripts/Buffs/FlagRunnerBuffDefinition.cs.meta`
- Create: `Assets/Settings/Buffs/FlagRunnerBuff.asset`
- Create: `Assets/Settings/Buffs/FlagRunnerBuff.asset.meta`

**Interfaces:**
- Consumes: `BuffDefinition.ContributeStats(ref EffectiveStats, int)`, `EffectiveStats`.
- Produces: `BuffId.FlagRunner = 3`; `EffectiveStats.CarrySpeedMultiplier` (float, defaults to `1f`); `EffectiveStats.CanDashWhileCarryingFlag` (bool, defaults to `false`). Task 4 consumes both fields. Task 5 references the asset GUID `2d8b5f37c1a94e60b83f7d2e5a916c48`.

- [x] **Step 1: Add the network token**

Replace `Assets/Scripts/Buffs/Core/BuffId.cs` in full:

```csharp
namespace Game.Buffs.Core
{
    /// <summary>Stable network token for each buff. Serialized as a byte in PlayerBuffs.LoadoutOrder.</summary>
    public enum BuffId : byte
    {
        ExtraJump = 0,
        Stealth = 1,
        QuickerDash = 2,
        FlagRunner = 3,
    }
}
```

- [x] **Step 2: Add the two stat channels**

Replace `Assets/Scripts/Buffs/Core/EffectiveStats.cs` in full:

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

        /// <summary>Walk-speed multiplier applied ONLY while carrying the enemy flag.</summary>
        public float CarrySpeedMultiplier;

        /// <summary>Lifts the default rule that carrying the flag blocks dashing.</summary>
        public bool CanDashWhileCarryingFlag;

        public static EffectiveStats Default() => new EffectiveStats
        {
            BonusAirJumps = 0,
            UnlimitedAirJumps = false,
            DashCooldownMultiplier = 1f,
            DashTimeMultiplier = 1f,
            DashDealsDamage = false,
            CarrySpeedMultiplier = 1f,
            CanDashWhileCarryingFlag = false,
        };
    }
}
```

- [x] **Step 3: Write the buff definition**

Create `Assets/Scripts/Buffs/FlagRunnerBuffDefinition.cs`:

```csharp
using UnityEngine;
using Game.Buffs.Core;

/// <summary>
/// Passive. T1 +10% move speed while carrying the enemy flag, T2 +20%, T3 +20% and dashing is
/// permitted while carrying. T3 deliberately mirrors Stealth's T3 (UsableWhileCarryingFlag) so
/// "top tier lifts the flag restriction" reads the same way across the catalog.
/// </summary>
[CreateAssetMenu(menuName = "Buffs/Flag Runner", fileName = "FlagRunnerBuff")]
public class FlagRunnerBuffDefinition : BuffDefinition
{
    [Header("Move-speed multiplier while carrying the flag (index 0 = tier 1)")]
    [SerializeField] private float[] carrySpeedMultipliers = { 1.1f, 1.2f, 1.2f };

    [Header("Tier at which carrying the flag stops blocking dash")]
    [SerializeField] private int dashWhileCarryingFromTier = 3;

    public override void ContributeStats(ref EffectiveStats stats, int tierLevel)
    {
        if (tierLevel <= 0) return;
        int idx = Mathf.Clamp(tierLevel - 1, 0, carrySpeedMultipliers.Length - 1);
        stats.CarrySpeedMultiplier *= carrySpeedMultipliers[idx];
        if (tierLevel >= dashWhileCarryingFromTier) stats.CanDashWhileCarryingFlag = true;
    }
}
```

- [x] **Step 4: Hand-write both `.meta` files**

Unity only generates `.meta` files on editor focus/refresh, and the asset in Step 5 must reference the script's GUID, so both are authored by hand (the project has done this in earlier `chore(meta)` commits).

Create `Assets/Scripts/Buffs/FlagRunnerBuffDefinition.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 7c4f1a9e3b6d42f8a5e0c19d7b204f36
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

Create `Assets/Settings/Buffs/FlagRunnerBuff.asset.meta`:

```yaml
fileFormatVersion: 2
guid: 2d8b5f37c1a94e60b83f7d2e5a916c48
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 11400000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

- [x] **Step 5: Author the asset**

Create `Assets/Settings/Buffs/FlagRunnerBuff.asset`. `m_Script`'s guid is the script guid from Step 4; `id: 3` is `BuffId.FlagRunner`; `kind: 0` is `BuffKind.Passive`.

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
  m_Script: {fileID: 11500000, guid: 7c4f1a9e3b6d42f8a5e0c19d7b204f36, type: 3}
  m_Name: FlagRunnerBuff
  m_EditorClassIdentifier: Assembly-CSharp::FlagRunnerBuffDefinition
  id: 3
  displayName: Flag Runner
  icon: {fileID: 0}
  kind: 0
  carrySpeedMultipliers:
  - 1.1
  - 1.2
  - 1.2
  dashWhileCarryingFromTier: 3
```

- [x] **Step 6: Verify GUID uniqueness and compile**

```bash
grep -rn "7c4f1a9e3b6d42f8a5e0c19d7b204f36\|2d8b5f37c1a94e60b83f7d2e5a916c48" Assets/ --include=*.meta --include=*.asset
```

Expected: exactly two hits, one per new `.meta`. If any third hit appears, generate a different 32-hex GUID and update both the `.meta` and the asset's `m_Script` reference.

Then run the whole-surface compile gate from Task 8, Step 1. Expected: no errors.

- [x] **Step 7: Commit**

```bash
git add Assets/Scripts/Buffs/Core/BuffId.cs Assets/Scripts/Buffs/Core/EffectiveStats.cs Assets/Scripts/Buffs/FlagRunnerBuffDefinition.cs Assets/Scripts/Buffs/FlagRunnerBuffDefinition.cs.meta Assets/Settings/Buffs/FlagRunnerBuff.asset Assets/Settings/Buffs/FlagRunnerBuff.asset.meta && git commit -m "feat(buffs): add Flag Runner definition, id and stat channels"
```

---

### Task 4: Consume carry speed and the T3 dash lift in movement

Both hooks already exist in `PlayerMovement.Simulate`. The carrying check is currently computed inline inside the dash gate; hoist it once and use it for both.

**Files:**
- Modify: `Assets/Scripts/Buffs/PlayerStatModifiers.cs`
- Modify: `Assets/Scripts/Player/PlayerMovement.cs:63-135`

**Interfaces:**
- Consumes: `EffectiveStats.CarrySpeedMultiplier`, `EffectiveStats.CanDashWhileCarryingFlag` (Task 3); `CTFGameManager.Instance.IsCarrying(PlayerRef)`.
- Produces: `PlayerStatModifiers.EffectiveWalkSpeed(bool carryingFlag)` (float) and `PlayerStatModifiers.CanDashWhileCarryingFlag` (bool). Nothing later depends on these.

- [x] **Step 1: Surface the two new stats**

In `Assets/Scripts/Buffs/PlayerStatModifiers.cs`, replace the accessor block (lines 30-43) with:

```csharp
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

    /// <summary>Walk speed for this tick. The carry bonus applies ONLY while carrying the flag.</summary>
    public float EffectiveWalkSpeed(bool carryingFlag) =>
        carryingFlag ? stats.walkSpeed * Current().CarrySpeedMultiplier : stats.walkSpeed;

    public bool CanDashWhileCarryingFlag => Current().CanDashWhileCarryingFlag;
```

- [x] **Step 2: Hoist the carrying check in `PlayerMovement.Simulate`**

In `Assets/Scripts/Player/PlayerMovement.cs`, replace lines 67-69 (the `grounded` / `stunned` block at the top of `Simulate`) with:

```csharp
        bool grounded = groundCheck != null &&
                        Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        bool stunned = IsStunned();

        // Carrying-state must come from networked flag state (resim-safe), not the render-path
        // FlagCarrierMarker bool — see CTFGameManager.IsCarrying. Read once: both the Flag Runner
        // speed bonus and the dash gate below need it.
        bool carryingFlag = CTFGameManager.Instance != null &&
                            CTFGameManager.Instance.IsCarrying(Object.InputAuthority);
```

- [x] **Step 3: Scale walk speed while carrying**

Still in `PlayerMovement.cs`, replace the `else` branch of the horizontal-velocity block (currently lines 92-106) with:

```csharp
        else
        {
            // Flag Runner scales the walk target while carrying; accel/decel are expressed as
            // "reach walk speed in N ticks", so they scale with it and the feel stays consistent.
            float walkSpeed = mods != null ? mods.EffectiveWalkSpeed(carryingFlag) : stats.walkSpeed;
            var p = new MoveParams
            {
                WalkSpeed = walkSpeed,
                AccelPerTick = walkSpeed /
                    System.Math.Max(1, grounded ? stats.groundAccelTicks : stats.airAccelTicks),
                DecelPerTick = walkSpeed /
                    System.Math.Max(1, grounded ? stats.groundDecelTicks : stats.airDecelTicks),
                MomentumDecayPerTick =
                    (grounded ? stats.momentumDecayGround : stats.momentumDecayAir) * Runner.DeltaTime,
            };
            float newVx = MovementMath.StepHorizontalVelocity(rb.linearVelocity.x, input.Horizontal, p);
            rb.linearVelocity = new Vector2(newVx, rb.linearVelocity.y);
        }
```

- [x] **Step 4: Let Flag Runner T3 lift the dash gate**

Replace the dash-start block (currently lines 126-135) with:

```csharp
        // ---- Dash start / cancel ----
        if (!stunned && pressed.IsSet((int)PlayerButton.Dash) && !Dashing &&
            DashCooldownTimer.ExpiredOrNotRunning(Runner) &&
            (combat == null || !combat.IsSwingCommitted))
        {
            // Carrying blocks dash by default; Flag Runner T3 lifts that restriction.
            bool dashBlocked = carryingFlag && (mods == null || !mods.CanDashWhileCarryingFlag);
            if (!dashBlocked) StartDash();
        }
```

- [x] **Step 5: Verify the compile gate and the absence of a duplicate carrying read**

```bash
grep -c "IsCarrying(Object.InputAuthority)" Assets/Scripts/Player/PlayerMovement.cs
```

Expected: `1`

Then run the whole-surface Roslyn compile gate from Task 8, Step 1. Expected: no errors.

There is no pure unit test here: `PlayerMovement` is a Fusion `NetworkBehaviour` and `FlagRunnerBuffDefinition` lives in `Assembly-CSharp`, so neither is reachable from the engine-free test assembly. Both are covered by the manual in-editor checks in Task 8.

- [x] **Step 6: Commit**

```bash
git add Assets/Scripts/Buffs/PlayerStatModifiers.cs Assets/Scripts/Player/PlayerMovement.cs && git commit -m "feat(movement): apply Flag Runner carry speed and T3 dash lift"
```

---

### Task 5: Flip the catalog to four buffs on the 12-step curve

Tests first: the four expected outcomes from the spec are asserted before the curve is authored, so a wrong hex blob is caught by arithmetic rather than by playtest.

The curve is `5, 10, 16, 24, 34, 46, 62, 80, 110, 150, 200, 260`.

**Files:**
- Test: `Assets/Tests/EditMode/BuffUnlockTests.cs`
- Modify: `Assets/Scripts/Buffs/BuffLoadoutConfig.cs:15-21`
- Modify: `Assets/Settings/Buffs/BuffLoadoutConfig.asset`

**Interfaces:**
- Consumes: `BuffUnlock.UnlockedSteps`, `BuffUnlock.TierLevel`; the `FlagRunnerBuff.asset` GUID `2d8b5f37c1a94e60b83f7d2e5a916c48` from Task 3.
- Produces: a valid 4-buff / 12-threshold / maxTier-3 config. Task 7's picker shows its 4-entry `defaultOrder`.

- [x] **Step 1: Write the failing tests**

Append to `Assets/Tests/EditMode/BuffUnlockTests.cs`, inside the class:

```csharp
    // The shipped four-buff curve: 4 buffs x 3 tiers, one threshold per cell.
    private static readonly List<int> Curve12 =
        new List<int> { 5, 10, 16, 24, 34, 46, 62, 80, 110, 150, 200, 260 };

    [TestCase(0, 0)]
    [TestCase(4, 0)]
    [TestCase(5, 1)]
    [TestCase(15, 2)]
    [TestCase(16, 3)]
    [TestCase(55, 6)]
    [TestCase(120, 9)]
    [TestCase(220, 11)]
    [TestCase(259, 11)]
    [TestCase(260, 12)]
    [TestCase(9999, 12)]
    public void UnlockedSteps_Curve12(int total, int expected)
    {
        Assert.AreEqual(expected, BuffUnlock.UnlockedSteps(Curve12, total));
    }

    // The spec's four target outcomes, expressed as the tier of each of the four priority
    // positions under the round-robin. Default order is [ExtraJump, Stealth, QuickerDash, FlagRunner].
    [TestCase(55, 2, 2, 1, 1)]
    [TestCase(120, 3, 2, 2, 2)]
    [TestCase(220, 3, 3, 3, 2)]
    [TestCase(260, 3, 3, 3, 3)]
    public void TierLevel_FourBuffRoundRobin_MatchesSpecTargets(
        int banked, int t0, int t1, int t2, int t3)
    {
        int steps = BuffUnlock.UnlockedSteps(Curve12, banked);
        Assert.AreEqual(t0, BuffUnlock.TierLevel(steps, 0, buffCount: 4, maxTier: 3), "priority #1");
        Assert.AreEqual(t1, BuffUnlock.TierLevel(steps, 1, buffCount: 4, maxTier: 3), "priority #2");
        Assert.AreEqual(t2, BuffUnlock.TierLevel(steps, 2, buffCount: 4, maxTier: 3), "priority #3");
        Assert.AreEqual(t3, BuffUnlock.TierLevel(steps, 3, buffCount: 4, maxTier: 3), "priority #4");
    }

    [Test]
    public void Curve12_IsAComplete4x3Curve()
    {
        Assert.IsTrue(BuffUnlock.IsCurveComplete(Curve12.Count, buffCount: 4, maxTier: 3));
    }
```

- [x] **Step 2: Run them and verify they pass**

These exercise only `BuffUnlock`, which Task 1 already finished, so they should pass immediately — that is the point: they pin the *numbers* before the asset is edited. Extend the Task 1 harness with these cases:

```bash
SP="$LOCALAPPDATA/Temp/claude/C--Users-1-Documents-GitHub-2dGame"; cat > "$SP/buffcheck/H.cs" <<'EOF'
using System;
using System.Collections.Generic;
using Game.Buffs.Core;
static class H {
  static int f = 0;
  static readonly List<int> C = new List<int>{5,10,16,24,34,46,62,80,110,150,200,260};
  static void Eq(int exp, int got, string what){ if(exp!=got){ Console.WriteLine($"FAIL {what}: expected {exp} got {got}"); f++; } }
  static void Outcome(int banked,int a,int b,int c,int d){
    int s = BuffUnlock.UnlockedSteps(C, banked);
    Eq(a, BuffUnlock.TierLevel(s,0,4,3), $"banked {banked} #1");
    Eq(b, BuffUnlock.TierLevel(s,1,4,3), $"banked {banked} #2");
    Eq(c, BuffUnlock.TierLevel(s,2,4,3), $"banked {banked} #3");
    Eq(d, BuffUnlock.TierLevel(s,3,4,3), $"banked {banked} #4");
  }
  static int Main(){
    if(!BuffUnlock.IsCurveComplete(9,3,3)  ){Console.WriteLine("FAIL curve 9/3/3");f++;}
    if(!BuffUnlock.IsCurveComplete(12,4,3) ){Console.WriteLine("FAIL curve 12/4/3");f++;}
    if( BuffUnlock.IsCurveComplete(9,4,3)  ){Console.WriteLine("FAIL curve 9/4/3");f++;}
    if( BuffUnlock.IsCurveComplete(13,4,3) ){Console.WriteLine("FAIL curve 13/4/3");f++;}
    if( BuffUnlock.IsCurveComplete(0,0,3)  ){Console.WriteLine("FAIL curve 0/0/3");f++;}
    if( BuffUnlock.IsCurveComplete(0,4,0)  ){Console.WriteLine("FAIL curve 0/4/0");f++;}
    Eq(0,  BuffUnlock.UnlockedSteps(C,4),   "steps 4");
    Eq(1,  BuffUnlock.UnlockedSteps(C,5),   "steps 5");
    Eq(2,  BuffUnlock.UnlockedSteps(C,15),  "steps 15");
    Eq(3,  BuffUnlock.UnlockedSteps(C,16),  "steps 16");
    Eq(6,  BuffUnlock.UnlockedSteps(C,55),  "steps 55");
    Eq(9,  BuffUnlock.UnlockedSteps(C,120), "steps 120");
    Eq(11, BuffUnlock.UnlockedSteps(C,220), "steps 220");
    Eq(11, BuffUnlock.UnlockedSteps(C,259), "steps 259");
    Eq(12, BuffUnlock.UnlockedSteps(C,260), "steps 260");
    Eq(12, BuffUnlock.UnlockedSteps(C,9999),"steps 9999");
    Outcome(55, 2,2,1,1);
    Outcome(120,3,2,2,2);
    Outcome(220,3,3,3,2);
    Outcome(260,3,3,3,3);
    Console.WriteLine(f==0 ? "ALL PASS" : $"{f} FAILURES");
    return f==0?0:1;
  }
}
EOF
UNITY="/c/Program Files/Unity/Hub/Editor/6000.3.0f1/Editor/Data"; NS=$(cygpath -w "$UNITY/NetStandard/ref/2.1.0/netstandard.dll"); "$UNITY/NetCoreRuntime/dotnet.exe" exec "$UNITY/DotNetSdkRoslyn/csc.dll" -nologo -noconfig -nostdlib -target:exe -r:"$NS" -out:"$(cygpath -w "$SP/buffcheck/H.exe")" "Assets/Scripts/Buffs/Core/BuffUnlock.cs" "$(cygpath -w "$SP/buffcheck/H.cs")" && "$UNITY/NetCoreRuntime/dotnet.exe" "$SP/buffcheck/H.exe"
```

Expected output: `ALL PASS`

If any `Outcome` line fails, the curve numbers are wrong — **stop and fix the curve, not the test.**

- [x] **Step 3: Update the C# defaults**

In `Assets/Scripts/Buffs/BuffLoadoutConfig.cs`, replace the `thresholds` and `defaultOrder` field declarations with:

```csharp
    [Header("Cumulative deposited-value thresholds — exactly maxTier x buffCount entries, ascending")]
    [SerializeField] private int[] thresholds =
        { 5, 10, 16, 24, 34, 46, 62, 80, 110, 150, 200, 260 };

    [Header("Default priority order if a player submits none")]
    [SerializeField] private BuffId[] defaultOrder =
        { BuffId.ExtraJump, BuffId.Stealth, BuffId.QuickerDash, BuffId.FlagRunner };
```

- [x] **Step 4: Update the config asset**

In `Assets/Settings/Buffs/BuffLoadoutConfig.asset`, replace everything from the `allBuffs:` line to the end of the file with:

```yaml
  allBuffs:
  - {fileID: 11400000, guid: 4632087f1471cdc4c8fb91c9208aa046, type: 2}
  - {fileID: 11400000, guid: 87072e9a6b5e28b40a1f5dfa81379274, type: 2}
  - {fileID: 11400000, guid: 3dea78701a9fd144b9467f9edcbeab94, type: 2}
  - {fileID: 11400000, guid: 2d8b5f37c1a94e60b83f7d2e5a916c48, type: 2}
  thresholds: 050000000a0000001000000018000000220000002e0000003e000000500000006e00000096000000c800000004010000
  defaultOrder: 00010203
  maxTier: 3
```

`thresholds` is Unity's hex blob for an `int[]`, little-endian, 8 hex chars per entry: `05000000`=5, `0a000000`=10, `10000000`=16, `18000000`=24, `22000000`=34, `2e000000`=46, `3e000000`=62, `50000000`=80, `6e000000`=110, `96000000`=150, `c8000000`=200, `04010000`=260. `defaultOrder` is the `BuffId` byte blob: `00`=ExtraJump, `01`=Stealth, `02`=QuickerDash, `03`=FlagRunner.

- [x] **Step 5: Verify the blob decodes to the intended curve**

```bash
python -c "
import re,struct
s=re.search(r'thresholds: (\w+)',open('Assets/Settings/Buffs/BuffLoadoutConfig.asset').read()).group(1)
b=bytes.fromhex(s)
print(len(b)//4,'entries:',list(struct.unpack('<%di'%(len(b)//4),b)))"
```

Expected: `12 entries: [5, 10, 16, 24, 34, 46, 62, 80, 110, 150, 200, 260]`

If `python` is unavailable, verify by eye against the mapping in Step 4 — every entry must be 8 hex characters and the total must be 96 characters.

- [x] **Step 6: Commit**

```bash
git add Assets/Tests/EditMode/BuffUnlockTests.cs Assets/Scripts/Buffs/BuffLoadoutConfig.cs Assets/Settings/Buffs/BuffLoadoutConfig.asset && git commit -m "feat(buffs): four-buff catalog on the 12-step unlock curve"
```

---

### Task 6: Extract `LoadoutCodec` and make the 4-entry round-trip testable

`PlayerBuffs.ToBytes` and `LobbyScreenUI.LoadoutAsBytes` are the same loop written twice, and `PlayerBuffs.ServerInitLoadout` hand-rolls the 8-entry cap. One pure codec in `Game.Buffs.Core` replaces all three and can be unit-tested outside Unity.

**Files:**
- Create: `Assets/Scripts/Buffs/Core/LoadoutCodec.cs`
- Create: `Assets/Tests/EditMode/LoadoutCodecTests.cs`
- Create: `Assets/Tests/EditMode/LoadoutCodecTests.cs.meta`
- Modify: `Assets/Scripts/Buffs/PlayerBuffs.cs:57-72`
- Modify: `Assets/Scripts/UI/LobbyScreenUI.cs:214-220`

**Interfaces:**
- Consumes: `BuffId` (Task 3).
- Produces: `LoadoutCodec.MaxEntries` (const int = 8); `static byte[] LoadoutCodec.ToBytes(IReadOnlyList<BuffId> order)`; `static BuffId[] LoadoutCodec.FromBytes(IReadOnlyList<byte> bytes)`. Both return an empty array for null input and truncate at `MaxEntries`.

- [x] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/LoadoutCodecTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Game.Buffs.Core;

public class LoadoutCodecTests
{
    [Test]
    public void RoundTrip_PreservesFourEntryDefaultOrder()
    {
        var order = new List<BuffId>
        {
            BuffId.ExtraJump, BuffId.Stealth, BuffId.QuickerDash, BuffId.FlagRunner
        };

        BuffId[] back = LoadoutCodec.FromBytes(LoadoutCodec.ToBytes(order));

        CollectionAssert.AreEqual(order, back);
    }

    [Test]
    public void RoundTrip_PreservesAReorderedFourEntryLoadout()
    {
        var order = new List<BuffId>
        {
            BuffId.FlagRunner, BuffId.QuickerDash, BuffId.ExtraJump, BuffId.Stealth
        };

        CollectionAssert.AreEqual(order, LoadoutCodec.FromBytes(LoadoutCodec.ToBytes(order)));
    }

    [Test]
    public void ToBytes_EncodesBuffIdAsItsByteValue()
    {
        byte[] bytes = LoadoutCodec.ToBytes(new List<BuffId> { BuffId.FlagRunner, BuffId.ExtraJump });

        Assert.AreEqual(new byte[] { 3, 0 }, bytes);
    }

    [Test]
    public void ToBytes_NullOrderYieldsEmpty()
    {
        Assert.AreEqual(0, LoadoutCodec.ToBytes(null).Length);
    }

    [Test]
    public void FromBytes_NullYieldsEmpty()
    {
        Assert.AreEqual(0, LoadoutCodec.FromBytes(null).Length);
    }

    [Test]
    public void ToBytes_TruncatesAtMaxEntries()
    {
        var tooMany = new List<BuffId>();
        for (int i = 0; i < LoadoutCodec.MaxEntries + 3; i++) tooMany.Add(BuffId.ExtraJump);

        Assert.AreEqual(LoadoutCodec.MaxEntries, LoadoutCodec.ToBytes(tooMany).Length);
    }
}
```

Create `Assets/Tests/EditMode/LoadoutCodecTests.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 9a1c74e0b28d4f13ae65302cf7d81b94
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

- [x] **Step 2: Run it to verify it fails**

```bash
grep -rn "class LoadoutCodec" Assets/Scripts/ || echo "ABSENT — test would fail to compile, as expected"
```

Expected: `ABSENT — test would fail to compile, as expected`

- [x] **Step 3: Write the codec**

Create `Assets/Scripts/Buffs/Core/LoadoutCodec.cs`:

```csharp
using System.Collections.Generic;

namespace Game.Buffs.Core
{
    /// <summary>
    /// Pure, Fusion-free conversion between a priority order and the byte payload that travels
    /// over reliable-data and lands in PlayerBuffs.LoadoutOrder. One implementation so the lobby
    /// UI and the server cannot drift apart on encoding or on the capacity cap.
    /// </summary>
    public static class LoadoutCodec
    {
        /// <summary>Matches the NetworkArray capacity on PlayerBuffs.LoadoutOrder.</summary>
        public const int MaxEntries = 8;

        public static byte[] ToBytes(IReadOnlyList<BuffId> order)
        {
            if (order == null) return new byte[0];
            int n = order.Count < MaxEntries ? order.Count : MaxEntries;
            var bytes = new byte[n];
            for (int i = 0; i < n; i++) bytes[i] = (byte)order[i];
            return bytes;
        }

        public static BuffId[] FromBytes(IReadOnlyList<byte> bytes)
        {
            if (bytes == null) return new BuffId[0];
            int n = bytes.Count < MaxEntries ? bytes.Count : MaxEntries;
            var order = new BuffId[n];
            for (int i = 0; i < n; i++) order[i] = (BuffId)bytes[i];
            return order;
        }
    }
}
```

- [x] **Step 4: Run the pure harness to verify it passes**

```bash
SP="$LOCALAPPDATA/Temp/claude/C--Users-1-Documents-GitHub-2dGame"; mkdir -p "$SP/codeccheck"; cat > "$SP/codeccheck/H.cs" <<'EOF'
using System;
using System.Collections.Generic;
using Game.Buffs.Core;
static class H {
  static int f = 0;
  static void Same(IList<BuffId> exp, IList<BuffId> got, string what){
    if(exp.Count!=got.Count){ Console.WriteLine($"FAIL {what}: length {exp.Count} vs {got.Count}"); f++; return; }
    for(int i=0;i<exp.Count;i++) if(exp[i]!=got[i]){ Console.WriteLine($"FAIL {what}: [{i}] {exp[i]} vs {got[i]}"); f++; return; }
  }
  static int Main(){
    var def = new List<BuffId>{BuffId.ExtraJump,BuffId.Stealth,BuffId.QuickerDash,BuffId.FlagRunner};
    Same(def, LoadoutCodec.FromBytes(LoadoutCodec.ToBytes(def)), "default round-trip");
    var re = new List<BuffId>{BuffId.FlagRunner,BuffId.QuickerDash,BuffId.ExtraJump,BuffId.Stealth};
    Same(re, LoadoutCodec.FromBytes(LoadoutCodec.ToBytes(re)), "reordered round-trip");
    var enc = LoadoutCodec.ToBytes(new List<BuffId>{BuffId.FlagRunner,BuffId.ExtraJump});
    if(enc.Length!=2 || enc[0]!=3 || enc[1]!=0){ Console.WriteLine("FAIL byte encoding"); f++; }
    if(LoadoutCodec.ToBytes(null).Length!=0){ Console.WriteLine("FAIL null ToBytes"); f++; }
    if(LoadoutCodec.FromBytes(null).Length!=0){ Console.WriteLine("FAIL null FromBytes"); f++; }
    var many = new List<BuffId>(); for(int i=0;i<LoadoutCodec.MaxEntries+3;i++) many.Add(BuffId.ExtraJump);
    if(LoadoutCodec.ToBytes(many).Length!=LoadoutCodec.MaxEntries){ Console.WriteLine("FAIL truncation"); f++; }
    Console.WriteLine(f==0 ? "ALL PASS" : $"{f} FAILURES");
    return f==0?0:1;
  }
}
EOF
cat > "$SP/codeccheck/H.runtimeconfig.json" <<'EOF'
{"runtimeOptions":{"tfm":"net6.0","framework":{"name":"Microsoft.NETCore.App","version":"6.0.0"}}}
EOF
UNITY="/c/Program Files/Unity/Hub/Editor/6000.3.0f1/Editor/Data"; NS=$(cygpath -w "$UNITY/NetStandard/ref/2.1.0/netstandard.dll"); "$UNITY/NetCoreRuntime/dotnet.exe" exec "$UNITY/DotNetSdkRoslyn/csc.dll" -nologo -noconfig -nostdlib -target:exe -r:"$NS" -out:"$(cygpath -w "$SP/codeccheck/H.exe")" "Assets/Scripts/Buffs/Core/BuffId.cs" "Assets/Scripts/Buffs/Core/LoadoutCodec.cs" "$(cygpath -w "$SP/codeccheck/H.cs")" && "$UNITY/NetCoreRuntime/dotnet.exe" "$SP/codeccheck/H.exe"
```

Expected output: `ALL PASS`

- [x] **Step 5: Route `PlayerBuffs` through the codec**

In `Assets/Scripts/Buffs/PlayerBuffs.cs`, delete the private `ToBytes` helper (lines 57-62) and replace `ApplyDefaultLoadout` and `ServerInitLoadout` with:

```csharp
    private void ApplyDefaultLoadout()
    {
        if (config == null || config.DefaultOrder == null) return;
        ServerInitLoadout(LoadoutCodec.ToBytes(config.DefaultOrder));
    }

    /// <summary>SERVER: set this player's priority order (from the lobby choice or default).</summary>
    public void ServerInitLoadout(byte[] order)
    {
        if (!HasStateAuthority || order == null) return;
        if (order.Length == 0) return; // empty choice: keep the default loadout applied in Spawned
        int n = Mathf.Min(order.Length, LoadoutCodec.MaxEntries);
        for (int i = 0; i < n; i++) LoadoutOrder.Set(i, order[i]);
        LoadoutLength = n;
    }
```

- [x] **Step 6: Route `LobbyScreenUI` through the codec**

In `Assets/Scripts/UI/LobbyScreenUI.cs`, replace `LoadoutAsBytes` (lines 214-220) with:

```csharp
    private byte[] LoadoutAsBytes() => Game.Buffs.Core.LoadoutCodec.ToBytes(loadoutOrder);
```

- [x] **Step 7: Verify no hand-rolled copies remain, then compile**

```bash
grep -rn "(byte)order\[i\]\|(byte)loadoutOrder\[i\]" Assets/Scripts/ | grep -v LoadoutCodec.cs
```

Expected: no output.

Then run the whole-surface Roslyn compile gate from Task 8, Step 1. Expected: no errors.

- [x] **Step 8: Commit**

```bash
git add Assets/Scripts/Buffs/Core/LoadoutCodec.cs Assets/Tests/EditMode/LoadoutCodecTests.cs Assets/Tests/EditMode/LoadoutCodecTests.cs.meta Assets/Scripts/Buffs/PlayerBuffs.cs Assets/Scripts/UI/LobbyScreenUI.cs && git commit -m "refactor(buffs): single pure LoadoutCodec for the loadout byte payload"
```

---

### Task 7: Extend the lobby picker to four slots

`LobbyScreenUI` is already count-agnostic — it loops over `buffConfig.DefaultOrder` and over the `slotLabels` / `slotUpButtons` / `slotDownButtons` arrays. The missing piece is purely scene data: `MainMenu.unity` has three rows (`Slot0Label`/`Slot0Up`/`Slot0Down` … `Slot2*`) as flat siblings under `LoadoutPanel`, with the three arrays wired to them.

Hand-editing scene YAML risks fileID collisions (`100425` is already the Start button), so this follows the project's established editor-tool pattern — `MatchHudBuilder` in the same folder does the same job for the results panel. Running the tool is a manual in-editor step for the user, recorded in Task 8.

Until the tool is run, the game is still correct: Flag Runner is equipped at priority #4 from the default order; it just cannot be reordered in the UI.

**Files:**
- Create: `Assets/Scripts/Editor/LoadoutPickerBuilder.cs`
- Create: `Assets/Scripts/Editor/LoadoutPickerBuilder.cs.meta`

**Interfaces:**
- Consumes: `LobbyScreenUI`'s serialized fields `loadoutPanel`, `slotLabels`, `slotUpButtons`, `slotDownButtons` (all private — reached via `SerializedObject`, as `MatchHudBuilder` does).
- Produces: nothing consumed by later tasks.

- [x] **Step 1: Write the builder**

Create `Assets/Scripts/Editor/LoadoutPickerBuilder.cs`:

```csharp
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-click builder that grows the lobby loadout picker to match the buff catalog. Finds the
/// LobbyScreenUI in the open scene and, for every buff beyond the rows that already exist, clones
/// the last row's three objects (label / up / down), offsets them by the existing row pitch, and
/// appends them to the three serialized slot arrays.
///
/// Re-runnable: it adds only the missing rows and leaves existing ones alone. Uses the Unity API
/// rather than raw scene YAML, so it cannot collide with existing fileIDs. Undo-friendly.
///
/// Mirrors the MatchHudBuilder editor-tool pattern in this folder.
/// </summary>
public static class LoadoutPickerBuilder
{
    private const string UndoLabel = "Extend Loadout Picker";

    [MenuItem("Tools/Lobby/Extend Loadout Picker")]
    public static void Build()
    {
        var lobby = Object.FindFirstObjectByType<LobbyScreenUI>(FindObjectsInactive.Include);
        if (lobby == null)
        {
            EditorUtility.DisplayDialog("Loadout Picker Builder",
                "No LobbyScreenUI found in the open scene.\n\nOpen Assets/Scenes/MainMenu.unity and run this again.",
                "OK");
            return;
        }

        var so = new SerializedObject(lobby);
        var configProp = so.FindProperty("buffConfig");
        var labels = so.FindProperty("slotLabels");
        var ups = so.FindProperty("slotUpButtons");
        var downs = so.FindProperty("slotDownButtons");

        var config = configProp.objectReferenceValue as BuffLoadoutConfig;
        if (config == null)
        {
            EditorUtility.DisplayDialog("Loadout Picker Builder",
                "LobbyScreenUI has no BuffLoadoutConfig assigned. Assign it and run this again.", "OK");
            return;
        }

        int want = config.BuffCount;
        int have = labels.arraySize;
        if (have == 0)
        {
            EditorUtility.DisplayDialog("Loadout Picker Builder",
                "The picker has no rows to clone from. Build at least one row by hand first.", "OK");
            return;
        }
        if (want <= have)
        {
            EditorUtility.DisplayDialog("Loadout Picker Builder",
                $"Nothing to do: the picker already has {have} rows for {want} buffs.", "OK");
            return;
        }

        // Row pitch from the two existing rows, so cloned rows land on the same grid.
        float pitch = -40f;
        if (have >= 2)
        {
            var r0 = ((TMP_Text)labels.GetArrayElementAtIndex(0).objectReferenceValue).rectTransform;
            var r1 = ((TMP_Text)labels.GetArrayElementAtIndex(1).objectReferenceValue).rectTransform;
            pitch = r1.anchoredPosition.y - r0.anchoredPosition.y;
        }

        for (int slot = have; slot < want; slot++)
        {
            CloneRow(labels, slot, pitch, $"Slot{slot}Label");
            CloneRow(ups, slot, pitch, $"Slot{slot}Up");
            CloneRow(downs, slot, pitch, $"Slot{slot}Down");
        }

        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(lobby.gameObject.scene);
        Debug.Log($"LoadoutPickerBuilder: picker now has {want} rows. Save the scene to keep them.");
    }

    /// <summary>
    /// Clones the last element of a serialized object-reference array into a new row at
    /// <paramref name="slot"/>, shifted one pitch down, and appends it to the array.
    /// </summary>
    private static void CloneRow(SerializedProperty array, int slot, float pitch, string name)
    {
        if (array.arraySize == 0) return;
        var last = array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue as Component;
        if (last == null) return;

        var clone = Object.Instantiate(last.gameObject, last.transform.parent);
        Undo.RegisterCreatedObjectUndo(clone, UndoLabel);
        clone.name = name;

        var rt = clone.GetComponent<RectTransform>();
        var srcRt = last.GetComponent<RectTransform>();
        if (rt != null && srcRt != null)
            rt.anchoredPosition = srcRt.anchoredPosition + new Vector2(0f, pitch);

        // Cloned buttons carry the source row's persistent listeners; MoveSlot is wired in code
        // by LobbyScreenUI.WireLoadoutButtons, so strip anything the clone inherited.
        var button = clone.GetComponent<Button>();
        if (button != null) button.onClick = new Button.ButtonClickedEvent();

        Component added = clone.GetComponent(last.GetType());
        array.arraySize++;
        array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue = added;
    }
}
```

- [x] **Step 2: Write the `.meta`**

Create `Assets/Scripts/Editor/LoadoutPickerBuilder.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 5e3a08c96b7d4a21bf49d5c8e017a2f3
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

- [x] **Step 3: Verify GUID uniqueness and compile**

```bash
grep -rn "5e3a08c96b7d4a21bf49d5c8e017a2f3" Assets/ | wc -l
```

Expected: `1`

The editor assembly is not part of the runtime compile gate, so compile it separately by adding `-r:"<Editor>/Data/Managed/UnityEditor.dll"` and the `Assets/Scripts/Editor/*.cs` sources to the Task 8 gate's response file. Expected: no errors.

- [x] **Step 4: Commit**

```bash
git add Assets/Scripts/Editor/LoadoutPickerBuilder.cs Assets/Scripts/Editor/LoadoutPickerBuilder.cs.meta && git commit -m "feat(editor): one-click builder to extend the lobby loadout picker"
```

---

### Task 8: Full verification pass and handoff notes

Nothing new is written here. This task runs every gate together on the finished branch and writes down, honestly, which claims rest on evidence and which still need a human in the editor.

**Files:**
- Modify: `docs/superpowers/plans/2026-07-29-individual-buff-layer.md` (tick the boxes)

- [x] **Step 1: Whole-surface Roslyn compile gate**

This is the gate referenced by Tasks 2, 3, 4, 6 and 7. It compiles the entire `Assembly-CSharp` surface against Unity's DLLs without needing the editor. `Game.Buffs.Core` is compiled **inline** because this branch changes it and its `Library/ScriptAssemblies/Game.Buffs.Core.dll` is stale until Unity re-imports.

```bash
SP="$LOCALAPPDATA/Temp/claude/C--Users-1-Documents-GitHub-2dGame"; mkdir -p "$SP/gate"; UNITY="/c/Program Files/Unity/Hub/Editor/6000.3.0f1/Editor/Data"; RSP="$SP/gate/gate.rsp"
{
  echo "-nologo"; echo "-noconfig"; echo "-target:library"; echo "-nostdlib"
  echo "-out:\"$(cygpath -w "$SP/gate/out.dll")\""
  echo "-r:\"$(cygpath -w "$UNITY/NetStandard/ref/2.1.0/netstandard.dll")\""
  for d in "$UNITY/Managed/UnityEngine"/*.dll; do echo "-r:\"$(cygpath -w "$d")\""; done
  for d in Assets/Photon/Fusion/Assemblies/*.dll; do echo "-r:\"$(cygpath -w "$PWD/$d")\""; done
  for d in Library/ScriptAssemblies/*.dll; do
    case "$d" in *Editor*|*CodeGen*|*Tests*|*Game.Buffs.Core.dll) continue;; esac
    echo "-r:\"$(cygpath -w "$PWD/$d")\""
  done
  # Assembly-CSharp sources: everything under Assets/Scripts except asmdef-owned folders.
  find Assets/Scripts -name '*.cs' \
    -not -path 'Assets/Scripts/Buffs/Core/*' \
    -not -path 'Assets/Scripts/Combat/Core/*' \
    -not -path 'Assets/Scripts/Enemy/AI/*' \
    -not -path 'Assets/Scripts/Hud/Core/*' \
    -not -path 'Assets/Scripts/Net/*' \
    -not -path 'Assets/Scripts/Match/Core/*' \
    -not -path 'Assets/Scripts/Player/Animation/Core/*' \
    -not -path 'Assets/Scripts/Player/Movement/Core/*' \
    -not -path 'Assets/Scripts/Editor/*' | sed 's/^/"/;s/$/"/'
  # Game.Buffs.Core inline — changed on this branch, so its Library DLL is stale. Globbed, so
  # the gate works at every task, before and after LoadoutCodec.cs exists.
  for s in Assets/Scripts/Buffs/Core/*.cs; do echo "\"$s\""; done
} > "$RSP"
"$UNITY/NetCoreRuntime/dotnet.exe" exec "$UNITY/DotNetSdkRoslyn/csc.dll" @"$RSP"
```

Expected: no output, exit code 0. If `Library/ScriptAssemblies` is missing (fresh worktree), point those refs at the main checkout's `Library`. If the exclusion list is wrong for this branch, correct it — asmdef folders are the ones containing a `.asmdef` file: `find Assets/Scripts -name '*.asmdef'`.

- [x] **Step 2: Run both pure test harnesses**

Re-run the Task 5 Step 2 harness and the Task 6 Step 4 harness. Expected: `ALL PASS` from each.

- [ ] **Step 3: Run the real NUnit EditMode suite — only if the editor is closed** — **SKIPPED, editor was running (project lock held). Still owed.**

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.0f1/Unity.exe" -batchmode -projectPath . -runTests -testPlatform EditMode -logFile - -testResults "$LOCALAPPDATA/Temp/claude/C--Users-1-Documents-GitHub-2dGame/editmode-results.xml"
```

Expected: the run completes and the results XML reports zero failures, including `BuffUnlockTests` and `LoadoutCodecTests`. If Unity reports a project lock, skip this step and say so explicitly in the report — the harnesses in Step 2 cover the same assertions but are **not** the NUnit suite.

- [x] **Step 4: Write the honest verification report**

Report to the user, separating what was executed from what was not:

**Verified by execution:**
- The 12-step curve's step counts and the four spec target outcomes (55/120/220/260) under the 4-buff round-robin.
- `IsCurveComplete` rejecting the exact footgun case (4 buffs, 9 thresholds) and the unserialized-`maxTier`-is-0 case.
- 4-entry loadout byte round-trip, byte encoding, null handling, and truncation at 8.
- The config asset's threshold blob decoding to the intended 12 integers.
- Whole-surface compile.

**NOT verified — needs the user in the Unity editor.** State plainly that a clean compile is not verification:
1. Unity re-imports `FlagRunnerBuff.asset` and `FlagRunnerBuffDefinition.cs` without a script-reference error (check the asset's inspector shows the Flag Runner fields, not "script missing").
2. `BuffLoadoutConfig` inspector shows 4 buffs, 12 thresholds, `maxTier` 3, and logs **no** curve error.
3. Deliberately break it — remove one threshold in the inspector — and confirm the `BuffLoadoutConfig` error appears in the console. Then undo.
4. Run `Tools > Lobby > Extend Loadout Picker` with `Assets/Scenes/MainMenu.unity` open, confirm a 4th row appears reading "4. Flag Runner", that its up/down buttons reorder correctly, and **save the scene**.
5. In play: bank ~24 points, confirm Flag Runner reaches T1; grab the enemy flag and confirm the carry is visibly faster than an unbuffed carry.
6. Bank to Flag Runner T3 (put it at priority #1 and bank ~110) and confirm dashing while carrying the flag now works, and that at T2 or below it is still blocked.
7. Multi-peer: confirm a client sees the same tiers as the host and that a late joiner derives correct tiers from `TotalDepositedValue` alone.

- [x] **Step 5: Commit the ticked plan**

```bash
git add docs/superpowers/plans/2026-07-29-individual-buff-layer.md && git commit -m "docs(plan): individual buff layer implementation plan"
```

---

## Execution notes (2026-07-30)

Deviations from the plan as written, recorded for the reviewer:

1. **`LoadoutCodec.cs.meta` was missing from the plan.** Task 6's file list named the `.cs`
   but not its `.meta`. Added during execution with GUID `4b70e21d9c8a45f7bd36a0e1f582c73d`.
2. **Task 3, Step 6 expected "exactly two hits"** for the GUID uniqueness grep; the real
   answer is three, because `FlagRunnerBuff.asset`'s `m_Script` also references the script
   GUID by design. Each GUID still appears in exactly one `.meta`, which is the property
   that matters.
3. **A second compile gate was needed.** `Assets/Scripts/Editor/*.cs` is a separate assembly,
   so Task 7's builder is checked by `gate-editor.sh` (references `UnityEditor.dll` plus the
   runtime gate's `out.dll` standing in for Assembly-CSharp). Both gates pass.
4. **The NUnit EditMode suite was not run** — the Unity editor held the project lock for the
   whole session. The two pure harnesses assert the same cases, but they are not the NUnit
   runner. This is still owed.

## Appendix — reference values

**The 12-step curve and what it yields under the default order** `[ExtraJump, Stealth, QuickerDash, FlagRunner]`:

| Banked | Steps | #1 | #2 | #3 | #4 |
|---|---|---|---|---|---|
| 55 | 6 | T2 | T2 | T1 | T1 |
| 120 | 9 | T3 | T2 | T2 | T2 |
| 220 | 11 | T3 | T3 | T3 | T2 |
| 260 | 12 | T3 | T3 | T3 | T3 |

**Threshold hex blob mapping** (Unity `int[]`, little-endian, 8 hex chars each):

| Value | 5 | 10 | 16 | 24 | 34 | 46 | 62 | 80 | 110 | 150 | 200 | 260 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Hex | `05000000` | `0a000000` | `10000000` | `18000000` | `22000000` | `2e000000` | `3e000000` | `50000000` | `6e000000` | `96000000` | `c8000000` | `04010000` |

**New GUIDs introduced by this plan** (verify uniqueness before use):

| File | GUID |
|---|---|
| `FlagRunnerBuffDefinition.cs.meta` | `7c4f1a9e3b6d42f8a5e0c19d7b204f36` |
| `FlagRunnerBuff.asset.meta` | `2d8b5f37c1a94e60b83f7d2e5a916c48` |
| `LoadoutCodecTests.cs.meta` | `9a1c74e0b28d4f13ae65302cf7d81b94` |
| `LoadoutPickerBuilder.cs.meta` | `5e3a08c96b7d4a21bf49d5c8e017a2f3` |
