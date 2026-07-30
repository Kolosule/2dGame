# Win-Condition Boundary + Sudden Death Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make CTF capture the only win condition — delete the coin tiebreak and `scoreLimit`, and add a `SuddenDeath` phase on timer expiry in which every buff is force-unlocked at read time and the next capture ends the match.

**Architecture:** `MatchPhase` moves into the engine-free `Game.Match.Core` assembly and gains a sixth value, `SuddenDeath`, appended **last** (it is a `[Networked]` byte enum — inserting mid-list would renumber existing phases). A new pure `MatchRules` holds two predicates — `IsPlayActive(phase)` (Live **or** SuddenDeath) and `AllBuffsMaxed(phase)` (SuddenDeath only) — so the several gameplay gates that today test `Phase == Live` cannot drift apart. `MatchManager` transitions `Live → SuddenDeath` on timer expiry instead of resolving a coin winner, arms no timer there unless an operator sets the off-by-default `suddenDeathHardCap`, and accepts captures whenever play is active. Sudden Death's buff unlock is a **read-time override** inside tier resolution: `BuffUnlock.ResolveTier(..., allUnlocked)` returns `maxTier` when the phase says so. No per-player networked state is added, `TotalDepositedValue` is never mutated, and tiers stay derived — so there is nothing to reset and nothing to replay on resimulation.

**Tech Stack:** Unity 6.3 (6000.3.0f1), C#, Photon Fusion 2 (Host/Client + dedicated server), NUnit EditMode tests in engine-free `Game.*.Core` assemblies, Unity's bundled Roslyn (`csc.dll`) + `NetCoreRuntime\dotnet.exe` for compiling and running pure logic while the editor holds the project lock.

**Spec:** [docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md](../specs/2026-07-29-coins-buffs-economy-design.md) — §"Win-condition boundary", decisions 10 and 11, seam 1 of §"Scope note".

## Global Constraints

- **Scope is seam 1 only.** Do not touch: the territorial damage system, `TeamManager`, `CombatConfig`, `TeamScoreManager`, the individual buff catalog, `BuffLoadoutConfig`'s thresholds or `MaxTier`, coin drop rates, or respawn timing (including the dead `GetRespawnTime` / `respawnTimeMultiplier` / `TeamData.respawnDelay` path). No new HUD surfaces — a Sudden Death banner is seam 4.
- **`MatchResolver.WinnerLabel` stays.** It still formats the results banner. Only `ResolveTimerWinner` goes.
- **Coins may never decide or tiebreak a match**, in any code path, after this work.
- All phase transitions are decided **only** under `HasStateAuthority`, inside `FixedUpdateNetwork`.
- Simulation-path timing via `TickTimer` only. No `Time.time` in simulation.
- Replicated state is `[Networked]` with `OnChangedRender` for render reactions. Clients and late joiners must derive the correct phase from networked state, never from a missed RPC.
- `Runner.Spawn`/`Despawn` only under `HasStateAuthority`.
- `MatchManager` must stay always-interested under interest management, or the phase and results vanish for distant players in a 20-player match. (It already is — do not change its interest configuration.)
- Tiers are **derived** on query, never stored. Add no per-buff or per-player networked tier fields.
- Draws must be **unreachable in default play**: `suddenDeathHardCap` defaults to `0` = off.
- New `.cs` files under `Assets/` need a hand-written `.cs.meta` (the editor only generates them on focus/refresh). Format is exactly two lines: `fileFormatVersion: 2` and `guid: <32 lowercase hex>`.
- A clean compile is **not** verification. Report separately what was executed versus what still needs in-editor play.

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `Assets/Scripts/Match/Core/MatchPhase.cs` | **create** | The `MatchPhase` enum, moved out of `MatchManager.cs` into the engine-free core so pure rules can switch on it. |
| `Assets/Scripts/Match/Core/MatchRules.cs` | **create** | Pure phase predicates: `IsPlayActive`, `AllBuffsMaxed`. |
| `Assets/Tests/EditMode/Match/MatchRulesTests.cs` | **create** | NUnit coverage of both predicates across all six phases. |
| `Assets/Scripts/Match/MatchManager.cs` | modify | Drop the enum declaration; `Live` expiry → `SuddenDeath`; hard-cap draw; capture guard via `IsPlayActive`; expose `IsPlayActive` / `AllBuffsMaxed`; delete `ResolveByTimer`. |
| `Assets/Scripts/Match/Core/MatchResolver.cs` | modify | Delete `ResolveTimerWinner`. Keep `WinnerLabel`. |
| `Assets/Tests/EditMode/Match/MatchResolverTests.cs` | modify | Delete the `ResolveTimerWinner_HigherScoreWins_EqualIsDraw` case. |
| `Assets/Scripts/ScriptableObjects/Game Settings Manager.cs` | modify | Delete `scoreLimit`; add `suddenDeathHardCap` (minutes, 0 = off). |
| `Assets/Scripts/CTF Flag/CTFGameManager.cs` | modify | `using Game.Match.Core;`; two `IsLive` gates → `IsPlayActive`. |
| `Assets/Scripts/Enemy/Base/EnemyAI.cs` | modify | One `IsLive` gate → `IsPlayActive`. |
| `Assets/Scripts/Buffs/Core/BuffUnlock.cs` | modify | Add pure `ResolveTier(..., bool allUnlocked)` — the Sudden Death override. |
| `Assets/Tests/EditMode/BuffUnlockTests.cs` | modify | Cover `ResolveTier` with and without the override. |
| `Assets/Scripts/Buffs/PlayerBuffs.cs` | modify | `TierOf` routes through `ResolveTier`, passing the phase-derived flag. |

`Assets/Scripts/Hud/MatchPhaseHud.cs` is deliberately **unmodified**: it already gates every panel on an explicit phase equality, so in `SuddenDeath` the countdown, live timer and results panel are all correctly hidden and nothing breaks. The banner is seam 4.

---

### Task 1: `MatchPhase` in the core assembly + pure `MatchRules`

Moves the enum so pure rules can switch on it, adds `SuddenDeath`, and lands the two predicates with tests. After this task the game compiles and plays exactly as before — `SuddenDeath` is declared but unreachable.

**Files:**
- Create: `Assets/Scripts/Match/Core/MatchPhase.cs` (+ `.cs.meta`)
- Create: `Assets/Scripts/Match/Core/MatchRules.cs` (+ `.cs.meta`)
- Create: `Assets/Tests/EditMode/Match/MatchRulesTests.cs` (+ `.cs.meta`)
- Modify: `Assets/Scripts/Match/MatchManager.cs:7` (remove the enum declaration)
- Modify: `Assets/Scripts/CTF Flag/CTFGameManager.cs` (add `using Game.Match.Core;`)

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `Game.Match.Core.MatchPhase : byte { Warmup, Countdown, Live, PostMatch, Intermission, SuddenDeath }`
  - `static bool Game.Match.Core.MatchRules.IsPlayActive(MatchPhase phase)`
  - `static bool Game.Match.Core.MatchRules.AllBuffsMaxed(MatchPhase phase)`

`Game.Match.Core.asmdef` already has `noEngineReferences: true` and no references, and the test asmdef `Assets/Tests/EditMode/Match/Game.Match.Core.Tests.asmdef` already references `Game.Match.Core` — no asmdef edits are needed in this task.

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/Match/MatchRulesTests.cs`:

```csharp
using NUnit.Framework;
using Game.Match.Core;

public class MatchRulesTests
{
    // Play runs in Live and SuddenDeath only: input live, enemies thinking, captures counted.
    [TestCase(MatchPhase.Warmup, false)]
    [TestCase(MatchPhase.Countdown, false)]
    [TestCase(MatchPhase.Live, true)]
    [TestCase(MatchPhase.SuddenDeath, true)]
    [TestCase(MatchPhase.PostMatch, false)]
    [TestCase(MatchPhase.Intermission, false)]
    public void IsPlayActive_TrueInLiveAndSuddenDeathOnly(MatchPhase phase, bool expected)
    {
        Assert.AreEqual(expected, MatchRules.IsPlayActive(phase));
    }

    // The same predicate is the capture guard: a capture counts in Live and SuddenDeath,
    // and is rejected in Countdown and PostMatch.
    [Test]
    public void IsPlayActive_IsTheCaptureGuard()
    {
        Assert.IsTrue(MatchRules.IsPlayActive(MatchPhase.Live));
        Assert.IsTrue(MatchRules.IsPlayActive(MatchPhase.SuddenDeath));
        Assert.IsFalse(MatchRules.IsPlayActive(MatchPhase.Countdown));
        Assert.IsFalse(MatchRules.IsPlayActive(MatchPhase.PostMatch));
    }

    [TestCase(MatchPhase.Warmup, false)]
    [TestCase(MatchPhase.Countdown, false)]
    [TestCase(MatchPhase.Live, false)]
    [TestCase(MatchPhase.SuddenDeath, true)]
    [TestCase(MatchPhase.PostMatch, false)]
    [TestCase(MatchPhase.Intermission, false)]
    public void AllBuffsMaxed_TrueInSuddenDeathOnly(MatchPhase phase, bool expected)
    {
        Assert.AreEqual(expected, MatchRules.AllBuffsMaxed(phase));
    }

    // SuddenDeath is appended LAST because Phase is a [Networked] byte enum: inserting it
    // between Live and PostMatch would renumber the existing wire values.
    [Test]
    public void SuddenDeath_IsAppendedLast_SoWireValuesAreStable()
    {
        Assert.AreEqual(0, (int)MatchPhase.Warmup);
        Assert.AreEqual(1, (int)MatchPhase.Countdown);
        Assert.AreEqual(2, (int)MatchPhase.Live);
        Assert.AreEqual(3, (int)MatchPhase.PostMatch);
        Assert.AreEqual(4, (int)MatchPhase.Intermission);
        Assert.AreEqual(5, (int)MatchPhase.SuddenDeath);
    }
}
```

Create its meta, `Assets/Tests/EditMode/Match/MatchRulesTests.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 7c1d4e0a9b3f42a5b8e6d0c17f2a4b93
```

- [ ] **Step 2: Run the test to verify it fails**

The editor may hold the project lock, so verify pure logic with Unity's bundled Roslyn. Write the reusable harness runner once, at `%TEMP%`-equivalent scratchpad path `C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\run-core-tests.ps1`:

```powershell
# Compiles engine-free .cs sources + a plain assert harness and runs it on Unity's .NET 6 runtime.
param([string[]]$Sources, [string]$Harness, [string]$OutName = "coretests")
$ErrorActionPreference = "Stop"
$data = "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Data"
$csc  = "$data\DotNetSdkRoslyn\csc.dll"
$net  = "$data\NetCoreRuntime\dotnet.exe"
$ref  = "$data\NetStandard\ref\2.1.0\netstandard.dll"
$out  = Join-Path $env:TEMP "$OutName"
New-Item -ItemType Directory -Force $out | Out-Null
$exe  = Join-Path $out "$OutName.exe"
$rsp  = Join-Path $out "$OutName.rsp"
$lines = @("-target:exe", "-nologo", "-nostdlib+", "-langversion:9", "`"-out:$exe`"", "`"-r:$ref`"")
foreach ($s in $Sources) { $lines += "`"$s`"" }
$lines += "`"$Harness`""
Set-Content -Encoding utf8 $rsp $lines
& $net exec $csc "@$rsp"
if ($LASTEXITCODE -ne 0) { throw "compile failed" }
Set-Content -Encoding utf8 (Join-Path $out "$OutName.runtimeconfig.json") @'
{ "runtimeOptions": { "tfm": "net6.0",
  "framework": { "name": "Microsoft.NETCore.App", "version": "6.0.0" } } }
'@
& $net exec $exe
exit $LASTEXITCODE
```

Then write the harness `...\scratchpad\MatchRulesHarness.cs`, mirroring each NUnit case as a plain assert:

```csharp
using System;
using Game.Match.Core;

static class H
{
    static int fails = 0;
    static void Check(bool ok, string name)
    {
        if (ok) Console.WriteLine("PASS " + name);
        else { Console.WriteLine("FAIL " + name); fails++; }
    }

    static int Main()
    {
        Check(!MatchRules.IsPlayActive(MatchPhase.Warmup), "play/warmup");
        Check(!MatchRules.IsPlayActive(MatchPhase.Countdown), "play/countdown");
        Check(MatchRules.IsPlayActive(MatchPhase.Live), "play/live");
        Check(MatchRules.IsPlayActive(MatchPhase.SuddenDeath), "play/suddendeath");
        Check(!MatchRules.IsPlayActive(MatchPhase.PostMatch), "play/postmatch");
        Check(!MatchRules.IsPlayActive(MatchPhase.Intermission), "play/intermission");

        Check(!MatchRules.AllBuffsMaxed(MatchPhase.Live), "maxed/live");
        Check(MatchRules.AllBuffsMaxed(MatchPhase.SuddenDeath), "maxed/suddendeath");
        Check(!MatchRules.AllBuffsMaxed(MatchPhase.PostMatch), "maxed/postmatch");

        Check((int)MatchPhase.Warmup == 0 && (int)MatchPhase.Countdown == 1 &&
              (int)MatchPhase.Live == 2 && (int)MatchPhase.PostMatch == 3 &&
              (int)MatchPhase.Intermission == 4 && (int)MatchPhase.SuddenDeath == 5,
              "wire values stable");

        Console.WriteLine(fails == 0 ? "ALL PASS" : fails + " FAILURES");
        return fails == 0 ? 0 : 1;
    }
}
```

Run:

```bash
powershell -File "C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\run-core-tests.ps1" -Sources "C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Match\Core\MatchResolver.cs","C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Match\Core\MatchPhase.cs","C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Match\Core\MatchRules.cs" -Harness "C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\MatchRulesHarness.cs" -OutName matchrules
```

Expected: `compile failed`, with csc reporting `error CS2001: Source file ...MatchPhase.cs could not be found` and `...MatchRules.cs could not be found`.

- [ ] **Step 3: Write the minimal implementation**

Create `Assets/Scripts/Match/Core/MatchPhase.cs`:

```csharp
namespace Game.Match.Core
{
    /// <summary>
    /// Explicit match life-cycle phases. Lives in the engine-free core assembly so the pure
    /// phase rules in MatchRules are unit-testable outside Unity.
    ///
    /// SuddenDeath is appended LAST on purpose: MatchManager.Phase is a [Networked] byte enum,
    /// so inserting a value between Live and PostMatch would renumber every phase on the wire.
    /// Nothing compares phases by ordering — only by equality — so declaration order is free.
    /// </summary>
    public enum MatchPhase : byte { Warmup, Countdown, Live, PostMatch, Intermission, SuddenDeath }
}
```

Create `Assets/Scripts/Match/Core/MatchPhase.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 1f6b28d4c05a4e93a7d1e4b60f39c2a8
```

Create `Assets/Scripts/Match/Core/MatchRules.cs`:

```csharp
namespace Game.Match.Core
{
    /// <summary>
    /// Pure, engine-free match-phase rules. The single place that answers "is play running?"
    /// and "are all buffs force-unlocked?", so the several gameplay gates that used to test
    /// Phase == Live directly cannot drift apart from each other.
    /// </summary>
    public static class MatchRules
    {
        /// <summary>
        /// Play is running: player input is live, enemies think, flags can be carried, and a
        /// capture counts. True in Live AND SuddenDeath — Sudden Death is normal play with no
        /// clock, so every gate that means "the match is being played" must use this.
        /// </summary>
        public static bool IsPlayActive(MatchPhase phase) =>
            phase == MatchPhase.Live || phase == MatchPhase.SuddenDeath;

        /// <summary>
        /// Every buff tier is forced to its maximum for every player. Applied as a READ-TIME
        /// override on tier resolution, so no per-player state is written, nothing is mutated,
        /// and there is nothing to reset or replay on resimulation.
        /// </summary>
        public static bool AllBuffsMaxed(MatchPhase phase) => phase == MatchPhase.SuddenDeath;
    }
}
```

Create `Assets/Scripts/Match/Core/MatchRules.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 3a90f7c15d6b4c82b04e9a2c8d71e6f5
```

Delete the old declaration at `Assets/Scripts/Match/MatchManager.cs:6-7` — remove exactly these two lines:

```csharp
/// <summary>Explicit match life-cycle phases. Replaces CTFGameManager's lone GameIsOver bool.</summary>
public enum MatchPhase : byte { Warmup, Countdown, Live, PostMatch, Intermission }
```

`MatchManager.cs` already has `using Game.Match.Core;` at line 4, so it still resolves `MatchPhase`.

In `Assets/Scripts/CTF Flag/CTFGameManager.cs`, add the using alongside the existing ones at the top of the file (it references `MatchPhase.PostMatch` / `MatchPhase.Intermission` at lines 158-159):

```csharp
using Game.Match.Core;
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
powershell -File "C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\run-core-tests.ps1" -Sources "C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Match\Core\MatchResolver.cs","C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Match\Core\MatchPhase.cs","C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Match\Core\MatchRules.cs" -Harness "C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\MatchRulesHarness.cs" -OutName matchrules
```

Expected: 10 `PASS` lines then `ALL PASS`, exit code 0.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Match/Core/MatchPhase.cs" "Assets/Scripts/Match/Core/MatchPhase.cs.meta" "Assets/Scripts/Match/Core/MatchRules.cs" "Assets/Scripts/Match/Core/MatchRules.cs.meta" "Assets/Tests/EditMode/Match/MatchRulesTests.cs" "Assets/Tests/EditMode/Match/MatchRulesTests.cs.meta" "Assets/Scripts/Match/MatchManager.cs" "Assets/Scripts/CTF Flag/CTFGameManager.cs" && git commit -m "feat(match): add SuddenDeath phase + pure MatchRules predicates"
```

---

### Task 2: Timer expiry enters Sudden Death; coin tiebreak and `scoreLimit` deleted

The behavioural core of the seam. After this task a match whose timer expires enters Sudden Death, play continues (input, enemies, flags), and the next capture wins. Coins no longer decide anything.

**Files:**
- Modify: `Assets/Scripts/Match/Core/MatchResolver.cs:10-16` (delete `ResolveTimerWinner`)
- Modify: `Assets/Tests/EditMode/Match/MatchResolverTests.cs:6-13` (delete its test case)
- Modify: `Assets/Scripts/Match/MatchManager.cs` (phase machine, gates, exposed predicates)
- Modify: `Assets/Scripts/ScriptableObjects/Game Settings Manager.cs:48-49`
- Modify: `Assets/Scripts/CTF Flag/CTFGameManager.cs:99,123`
- Modify: `Assets/Scripts/Enemy/Base/EnemyAI.cs:167`

**Interfaces:**
- Consumes: `MatchRules.IsPlayActive(MatchPhase)`, `MatchRules.AllBuffsMaxed(MatchPhase)`, `MatchPhase.SuddenDeath` (Task 1).
- Produces:
  - `bool MatchManager.IsPlayActive { get; }` — replaces the deleted `bool IsLive`.
  - `bool MatchManager.AllBuffsMaxed { get; }` — consumed by Task 3.
  - `float GameSettingsManager.suddenDeathHardCap` — public field, **minutes**, default `0` = off.
  - `MatchResolver.ResolveTimerWinner` no longer exists.

- [ ] **Step 1: Write the failing test**

There is no pure seam for the Fusion phase machine, so the test change here is a **deletion**: the coin tiebreak's own test must go with it. In `Assets/Tests/EditMode/Match/MatchResolverTests.cs`, delete lines 6-13 exactly:

```csharp
    [TestCase(3, 1, 1)] // team1 higher
    [TestCase(1, 3, 2)] // team2 higher
    [TestCase(2, 2, 0)] // equal -> draw
    [TestCase(0, 0, 0)] // both zero -> draw
    public void ResolveTimerWinner_HigherScoreWins_EqualIsDraw(int t1, int t2, int expected)
    {
        Assert.AreEqual(expected, MatchResolver.ResolveTimerWinner(t1, t2));
    }
```

The file keeps `WinnerLabel_MapsWinnerToText` unchanged, including its `[TestCase(0, "It's a Draw!")]` — the draw label is still reachable via the hard cap.

Also delete `ResolveTimerWinner` from `Assets/Scripts/Match/Core/MatchResolver.cs` (lines 10-16):

```csharp
        /// <summary>Timer expired with no capture: higher coin score wins, exactly equal is a draw.</summary>
        public static int ResolveTimerWinner(int team1Score, int team2Score)
        {
            if (team1Score > team2Score) return 1;
            if (team2Score > team1Score) return 2;
            return 0;
        }
```

and retitle the class doc comment so it no longer advertises a timer winner — replace the summary block at lines 3-7 with:

```csharp
    /// <summary>
    /// Pure match-outcome logic, engine-free so it is unit-testable. Formats the results banner
    /// from a winner code: 0 = draw, 1 = Team1, 2 = Team2 (matches TeamUtil.ToNumber). Capture is
    /// the only win condition — coins neither decide nor tiebreak a match — so there is no
    /// score-comparison resolver here by design.
    /// </summary>
```

- [ ] **Step 2: Run to verify it fails**

`MatchManager.ResolveByTimer()` still calls the deleted method, so the compile must break — that failing compile is this step's expected output.

```bash
powershell -File "C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\run-core-tests.ps1" -Sources "C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Match\Core\MatchResolver.cs","C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Match\Core\MatchPhase.cs","C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Match\Core\MatchRules.cs" -Harness "C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\MatchRulesHarness.cs" -OutName matchrules
```

Expected: still `ALL PASS` (the core assembly itself is consistent). The real gate is the whole-surface compile in Task 4; at this point in the task `MatchManager` is knowingly broken, which is what Step 3 fixes.

- [ ] **Step 3: Write the implementation**

In `Assets/Scripts/Match/MatchManager.cs`, replace the class doc comment (lines 9-13 as they read after Task 1) with:

```csharp
/// <summary>
/// Server-authoritative match life cycle. Owns the phase enum, one reused TickTimer, and the
/// results banner's winner code. Capture is the ONLY win condition: a Live timer expiry hands off
/// to SuddenDeath rather than resolving a winner from coin score. One per Gameplay scene. Must be
/// always-interested under AoI so every player sees the phase/timer/results.
/// </summary>
```

Add the hard-cap tooltip note under the existing `[Header("Phase Durations (seconds)")]` block — no new serialized field on `MatchManager`; the cap is a match rule and lives with `matchTimeLimit` in `GameSettingsManager`.

Replace the two predicate properties (lines 35-36) with:

```csharp
    /// <summary>
    /// Play is running: input live, enemies thinking, flags carryable, captures counted. TRUE in
    /// SuddenDeath as well as Live — every gameplay gate must use this rather than testing
    /// Phase == Live, or Sudden Death would freeze the match it is supposed to decide.
    /// </summary>
    public bool IsPlayActive => MatchRules.IsPlayActive(Phase);

    /// <summary>
    /// Sudden Death forces every buff to max tier. PlayerBuffs reads this at tier-resolve time;
    /// no tier is stored, so this costs no networked state.
    /// </summary>
    public bool AllBuffsMaxed => MatchRules.AllBuffsMaxed(Phase);

    public bool InputEnabled => IsPlayActive;
```

Replace the `Live` case in `FixedUpdateNetwork` and add the `SuddenDeath` case:

```csharp
            case MatchPhase.Live:
                // Timer expiry no longer resolves a winner: coins cannot decide a match.
                if (PhaseTimer.Expired(Runner)) EnterPhase(MatchPhase.SuddenDeath);
                break;
            case MatchPhase.SuddenDeath:
                // Only armed when an operator sets suddenDeathHardCap; TickTimer.None never expires.
                if (PhaseTimer.Expired(Runner)) ResolveAsDraw();
                break;
```

Add the `SuddenDeath` case to `EnterPhase`, immediately after the `Live` case:

```csharp
            case MatchPhase.SuddenDeath:
                float cap = (GameSettingsManager.Instance != null)
                    ? GameSettingsManager.Instance.suddenDeathHardCap * 60f
                    : 0f;
                // Default 0 = off: no timer at all, so the next capture is the only end condition.
                PhaseTimer = cap > 0f ? TickTimer.CreateFromSeconds(Runner, cap) : TickTimer.None;
                break;
```

Widen the capture guard:

```csharp
    /// <summary>Server-only. A team carried the enemy flag home while play was active — instant win.</summary>
    public void ReportCapture(Team winningTeam)
    {
        if (!HasStateAuthority || !MatchRules.IsPlayActive(Phase)) return;
        Winner = (byte)TeamUtil.ToNumber(winningTeam);
        EnterPhase(MatchPhase.PostMatch);
    }
```

Replace `ResolveByTimer()` (lines 161-168) with:

```csharp
    /// <summary>
    /// Server-only OPS SAFETY VALVE: the operator-set Sudden Death hard cap elapsed, so end the
    /// match as a draw rather than let a headless dedicated server wedge on an unwinnable match.
    /// Unreachable in default play — suddenDeathHardCap defaults to 0 = off.
    /// </summary>
    private void ResolveAsDraw()
    {
        Winner = 0; // MatchResolver.WinnerLabel(0) reads as a draw.
        EnterPhase(MatchPhase.PostMatch);
    }
```

In `Assets/Scripts/ScriptableObjects/Game Settings Manager.cs`, replace lines 48-49:

```csharp
    [Tooltip("Score limit to win (0 = no limit)")]
    public int scoreLimit = 0;
```

with:

```csharp
    [Tooltip("Sudden Death hard cap in minutes (0 = off). Operations safety valve only: on " +
             "expiry the match resolves as a draw so a headless server cannot wedge on an " +
             "unwinnable match. Leave at 0 — draws are unreachable in default play.")]
    public float suddenDeathHardCap = 0f;
```

Repoint the three `IsLive` call sites. `Assets/Scripts/CTF Flag/CTFGameManager.cs:99`:

```csharp
        if (MatchManager.Instance == null || !MatchManager.Instance.IsPlayActive) return;
```

`Assets/Scripts/CTF Flag/CTFGameManager.cs:123`:

```csharp
        if (MatchManager.Instance != null && !MatchManager.Instance.IsPlayActive) return;
```

`Assets/Scripts/Enemy/Base/EnemyAI.cs:167`:

```csharp
        if (MatchManager.Instance != null && !MatchManager.Instance.IsPlayActive) return;
```

Then confirm no `IsLive` references survive:

```bash
cd "C:/Users/1/Documents/GitHub/2dGame" && grep -rn "IsLive\|ResolveTimerWinner\|ResolveByTimer\|scoreLimit" Assets/Scripts Assets/Tests
```

Expected: no output.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
powershell -File "C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\run-core-tests.ps1" -Sources "C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Match\Core\MatchResolver.cs","C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Match\Core\MatchPhase.cs","C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Match\Core\MatchRules.cs" -Harness "C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\MatchRulesHarness.cs" -OutName matchrules
```

Expected: `ALL PASS`, exit 0. (The whole-surface compile gate that proves `MatchManager` builds runs in Task 4 — do not claim this task compiles until then.)

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Match/MatchManager.cs" "Assets/Scripts/Match/Core/MatchResolver.cs" "Assets/Tests/EditMode/Match/MatchResolverTests.cs" "Assets/Scripts/ScriptableObjects/Game Settings Manager.cs" "Assets/Scripts/CTF Flag/CTFGameManager.cs" "Assets/Scripts/Enemy/Base/EnemyAI.cs" && git commit -m "feat(match): timer expiry enters Sudden Death; drop coin tiebreak and scoreLimit"
```

---

### Task 3: Sudden Death forces every buff to max tier

The read-time override. Pure math lands in `BuffUnlock`; `PlayerBuffs.TierOf` passes a phase-derived flag into it.

**Files:**
- Modify: `Assets/Scripts/Buffs/Core/BuffUnlock.cs` (add `ResolveTier`)
- Modify: `Assets/Tests/EditMode/BuffUnlockTests.cs` (cover it)
- Modify: `Assets/Scripts/Buffs/PlayerBuffs.cs:89-97` (`TierOf`)

**Interfaces:**
- Consumes: `bool MatchManager.AllBuffsMaxed` (Task 2).
- Produces: `static int BuffUnlock.ResolveTier(int unlockedSteps, int priorityPosition, int buffCount, int maxTier, bool allUnlocked)`.

- [ ] **Step 1: Write the failing test**

Append to `Assets/Tests/EditMode/BuffUnlockTests.cs`, inside the existing `BuffUnlockTests` class:

```csharp
    // Sudden Death: every buff resolves to maxTier regardless of deposited value or position.
    [TestCase(0, 0)]
    [TestCase(0, 2)]
    [TestCase(9, 0)]
    [TestCase(4, 1)]
    public void ResolveTier_AllUnlocked_ReturnsMaxTierRegardlessOfStepsOrPosition(int steps, int position)
    {
        Assert.AreEqual(3, BuffUnlock.ResolveTier(steps, position, buffCount: 3, maxTier: 3,
                                                  allUnlocked: true));
    }

    // Not Sudden Death: identical to TierLevel, so normal play is untouched.
    [TestCase(4, 0, 2)]
    [TestCase(4, 1, 1)]
    [TestCase(1, 1, 0)]
    [TestCase(0, 0, 0)]
    [TestCase(9, 2, 3)]
    public void ResolveTier_NotUnlocked_MatchesTierLevel(int steps, int position, int expected)
    {
        Assert.AreEqual(expected, BuffUnlock.ResolveTier(steps, position, buffCount: 3, maxTier: 3,
                                                         allUnlocked: false));
        Assert.AreEqual(BuffUnlock.TierLevel(steps, position, 3, 3),
                        BuffUnlock.ResolveTier(steps, position, 3, 3, allUnlocked: false));
    }

    // A misconfigured maxTier must not produce a negative tier under the override.
    [Test]
    public void ResolveTier_AllUnlocked_ClampsNonPositiveMaxTierToZero()
    {
        Assert.AreEqual(0, BuffUnlock.ResolveTier(5, 0, buffCount: 3, maxTier: 0, allUnlocked: true));
        Assert.AreEqual(0, BuffUnlock.ResolveTier(5, 0, buffCount: 3, maxTier: -2, allUnlocked: true));
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Write the harness `...\scratchpad\BuffUnlockHarness.cs`:

```csharp
using System;
using System.Collections.Generic;
using Game.Buffs.Core;

static class H
{
    static int fails = 0;
    static void Eq(int expected, int actual, string name)
    {
        if (expected == actual) Console.WriteLine("PASS " + name);
        else { Console.WriteLine($"FAIL {name}: expected {expected}, got {actual}"); fails++; }
    }

    static int Main()
    {
        var thresholds = new List<int> { 5, 10, 15, 30, 45, 60, 120, 180, 240 };

        // Existing behaviour must not regress.
        Eq(0, BuffUnlock.UnlockedSteps(thresholds, 4), "steps/4");
        Eq(6, BuffUnlock.UnlockedSteps(thresholds, 60), "steps/60");
        Eq(9, BuffUnlock.UnlockedSteps(thresholds, 9999), "steps/9999");
        Eq(2, BuffUnlock.TierLevel(4, 0, 3, 3), "tier/4,0");
        Eq(1, BuffUnlock.TierLevel(4, 2, 3, 3), "tier/4,2");
        Eq(3, BuffUnlock.TierLevel(9, 0, 3, 3), "tier/clamped");

        // Sudden Death override.
        Eq(3, BuffUnlock.ResolveTier(0, 0, 3, 3, true), "sd/0,0");
        Eq(3, BuffUnlock.ResolveTier(0, 2, 3, 3, true), "sd/0,2");
        Eq(3, BuffUnlock.ResolveTier(9, 0, 3, 3, true), "sd/9,0");
        Eq(3, BuffUnlock.ResolveTier(4, 1, 3, 3, true), "sd/4,1");
        Eq(0, BuffUnlock.ResolveTier(5, 0, 3, 0, true), "sd/maxtier0");
        Eq(0, BuffUnlock.ResolveTier(5, 0, 3, -2, true), "sd/maxtier-2");

        // Normal play unchanged.
        Eq(2, BuffUnlock.ResolveTier(4, 0, 3, 3, false), "normal/4,0");
        Eq(1, BuffUnlock.ResolveTier(4, 1, 3, 3, false), "normal/4,1");
        Eq(0, BuffUnlock.ResolveTier(1, 1, 3, 3, false), "normal/1,1");
        Eq(3, BuffUnlock.ResolveTier(9, 2, 3, 3, false), "normal/9,2");

        Console.WriteLine(fails == 0 ? "ALL PASS" : fails + " FAILURES");
        return fails == 0 ? 0 : 1;
    }
}
```

Run it against the real core sources:

```bash
powershell -File "C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\run-core-tests.ps1" -Sources "C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Buffs\Core\BuffUnlock.cs" -Harness "C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\BuffUnlockHarness.cs" -OutName buffunlock
```

Expected: `compile failed` with `error CS0117: 'BuffUnlock' does not contain a definition for 'ResolveTier'`.

- [ ] **Step 3: Write the minimal implementation**

Add to `Assets/Scripts/Buffs/Core/BuffUnlock.cs`, after `TierLevel`:

```csharp
        /// <summary>
        /// Tier of the buff at the given priority position, with an all-unlocked override.
        /// When allUnlocked is true (Sudden Death) every buff resolves to maxTier regardless of
        /// deposited value or priority. The override is applied at READ time by the caller's
        /// query, so no tier is ever stored and nothing needs replaying on resimulation.
        /// </summary>
        public static int ResolveTier(int unlockedSteps, int priorityPosition, int buffCount,
                                      int maxTier, bool allUnlocked)
        {
            if (allUnlocked) return maxTier > 0 ? maxTier : 0;
            return TierLevel(unlockedSteps, priorityPosition, buffCount, maxTier);
        }
```

In `Assets/Scripts/Buffs/PlayerBuffs.cs`, replace `TierOf` (lines 89-97) with:

```csharp
    /// <summary>Current tier (0 = locked) of the given buff for this player.</summary>
    public int TierOf(BuffId id)
    {
        if (config == null) return 0;
        int pos = PositionOf(id);
        if (pos < 0) return 0; // not equipped — the loadout always holds the whole catalog
        int steps = BuffUnlock.UnlockedSteps(config.Thresholds, TotalDepositedValue);
        return BuffUnlock.ResolveTier(steps, pos, config.BuffCount, config.MaxTier,
                                      allUnlocked: SuddenDeathMaxesTiers);
    }

    /// <summary>
    /// Sudden Death forces every tier to MaxTier. Derived from MatchManager's [Networked] Phase,
    /// so it resolves identically on clients and during resimulation. Deliberately adds no
    /// per-player state and never mutates TotalDepositedValue — tiers stay derived, so leaving
    /// Sudden Death (scene reload on rematch) restores normal tiers with nothing to reset.
    /// </summary>
    private bool SuddenDeathMaxesTiers =>
        MatchManager.Instance != null && MatchManager.Instance.AllBuffsMaxed;
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
powershell -File "C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\run-core-tests.ps1" -Sources "C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Buffs\Core\BuffUnlock.cs" -Harness "C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\BuffUnlockHarness.cs" -OutName buffunlock
```

Expected: 16 `PASS` lines then `ALL PASS`, exit 0.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Buffs/Core/BuffUnlock.cs" "Assets/Tests/EditMode/BuffUnlockTests.cs" "Assets/Scripts/Buffs/PlayerBuffs.cs" && git commit -m "feat(buffs): force every tier to max during Sudden Death (read-time override)"
```

---

### Task 4: Whole-surface compile gate and verification report

Proves the Fusion-side code actually builds (the pure harnesses cannot), then states plainly what is verified versus what needs in-editor play.

**Files:**
- Create: scratchpad `compile-gate.ps1` (throwaway, not committed)
- Modify: none in `Assets/`

**Interfaces:**
- Consumes: everything from Tasks 1-3.
- Produces: the verification report handed back to the user.

- [ ] **Step 1: Write the compile gate**

Create `C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\compile-gate.ps1`:

```powershell
# Compiles the whole Assembly-CSharp surface with Unity's bundled Roslyn.
# Game.Match.Core and Game.Buffs.Core changed in this branch, so their Library DLLs are stale:
# drop those refs and compile their sources INLINE instead.
$ErrorActionPreference = "Stop"
$repo = "C:\Users\1\Documents\GitHub\2dGame"
$data = "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Data"
$out  = Join-Path $env:TEMP "compilegate"
New-Item -ItemType Directory -Force $out | Out-Null
$rsp  = Join-Path $out "gate.rsp"

$inlineCores = @("$repo\Assets\Scripts\Match\Core\", "$repo\Assets\Scripts\Buffs\Core\")
$otherCores  = @("$repo\Assets\Scripts\Combat\Core\", "$repo\Assets\Scripts\Enemy\AI\",
                 "$repo\Assets\Scripts\Hud\Core\", "$repo\Assets\Scripts\Net\",
                 "$repo\Assets\Scripts\Player\Animation\Core\",
                 "$repo\Assets\Scripts\Player\Movement\Core\")

$refs = @("$data\NetStandard\ref\2.1.0\netstandard.dll")
$refs += (Get-ChildItem "$data\Managed\UnityEngine\*.dll").FullName
$refs += (Get-ChildItem "$repo\Assets\Photon\Fusion\Assemblies\*.dll").FullName
$refs += (Get-ChildItem "$repo\Library\ScriptAssemblies\*.dll" |
          Where-Object { $_.Name -notmatch "Editor|CodeGen|Tests" -and
                         $_.Name -ne "Game.Match.Core.dll" -and
                         $_.Name -ne "Game.Buffs.Core.dll" }).FullName

$sources = Get-ChildItem "$repo\Assets\Scripts" -Recurse -Filter *.cs | Where-Object {
    $p = $_.FullName
    $inInline = $false; foreach ($c in $inlineCores) { if ($p.StartsWith($c)) { $inInline = $true } }
    $inOther  = $false; foreach ($c in $otherCores)  { if ($p.StartsWith($c)) { $inOther  = $true } }
    $inInline -or -not $inOther
}

$lines = @("-target:library", "-nologo", "-nostdlib+", "-langversion:9",
           "`"-out:$out\gate.dll`"")
foreach ($r in $refs)    { $lines += "`"-r:$r`"" }
foreach ($s in $sources) { $lines += "`"$($s.FullName)`"" }
Set-Content -Encoding utf8 $rsp $lines
& "$data\NetCoreRuntime\dotnet.exe" exec "$data\DotNetSdkRoslyn\csc.dll" "@$rsp"
Write-Output "exit=$LASTEXITCODE  sources=$($sources.Count)"
```

- [ ] **Step 2: Run the gate**

```bash
powershell -File "C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\compile-gate.ps1"
```

Expected: no `error CS...` lines and `exit=0`. Warnings are acceptable. If csc reports `CS0246: The type or namespace name 'MatchPhase' could not be found` in a file, that file is missing `using Game.Match.Core;` — add it and re-run.

- [ ] **Step 3: Re-run both pure suites**

```bash
powershell -File "C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\run-core-tests.ps1" -Sources "C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Match\Core\MatchResolver.cs","C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Match\Core\MatchPhase.cs","C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Match\Core\MatchRules.cs" -Harness "C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\MatchRulesHarness.cs" -OutName matchrules
```

```bash
powershell -File "C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\run-core-tests.ps1" -Sources "C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Buffs\Core\BuffUnlock.cs" -Harness "C:\Users\1\AppData\Local\Temp\claude\C--Users-1-Documents-GitHub-2dGame\2184a4f4-f51b-47ca-8125-be5ea77fd517\scratchpad\BuffUnlockHarness.cs" -OutName buffunlock
```

Expected: `ALL PASS` from both.

- [ ] **Step 4: Confirm no coin-win path survives**

```bash
cd "C:/Users/1/Documents/GitHub/2dGame" && grep -rn "ResolveTimerWinner\|ResolveByTimer\|scoreLimit\|IsLive" Assets/Scripts Assets/Tests
```

Expected: no output. Note separately that `Assets/Scenes/Gameplay.unity:14174` still carries a serialized `scoreLimit: 0` line for the now-deleted field; Unity drops unknown keys on next save, so leave it — do **not** hand-edit the scene YAML.

- [ ] **Step 5: Write the verification report and commit the plan**

Report to the user, split honestly:

- **Executed:** the two pure suites (list the assertion counts) and the whole-surface compile gate (`exit=0`, source count).
- **Not executed:** the NUnit EditMode suites via Unity's test runner (needs the editor unlocked), and all in-editor play.
- **Needs in-editor play (host + at least one client):** with `Gameplay.unity`'s existing `matchTimeLimit: 1`, let the clock expire and confirm (a) the phase becomes Sudden Death rather than a results screen, (b) input and enemies keep working and the Live timer disappears, (c) every buff behaves at max tier — unlimited air jumps, 10 s stealth, dash while carrying the flag — for a player who has banked nothing, (d) the next capture ends the match with the correct winner banner, and (e) with `suddenDeathHardCap` temporarily set to a small value, expiry yields the draw banner and the normal return-to-lobby flow.

```bash
git add docs/superpowers/plans/2026-07-29-win-condition-sudden-death.md && git commit -m "docs(plan): win-condition boundary + Sudden Death implementation plan"
```

---

## Notes for the implementer

- **Why `MatchPhase` moves.** The prompt asks for an EditMode test of the capture phase guard. `Game.Match.Core` is engine-free (`noEngineReferences: true`) and cannot reference `Assembly-CSharp`, where the enum lived — so a pure, testable guard requires the enum to live in the core. Moving it is the smallest change that satisfies that; the alternative (passing raw bytes into the pure layer) throws away type safety.
- **Why `IsLive` is deleted rather than kept alongside `IsPlayActive`.** All three of its call sites (two in `CTFGameManager`, one in `EnemyAI`) mean "the match is being played", and all three must be true in Sudden Death — if input and enemies freeze, or the base trigger stops reporting, Sudden Death can never end. Keeping a same-shaped `IsLive` next to `IsPlayActive` is exactly the footgun that would reintroduce the bug later. Nothing outside `MatchManager` needs strict `Phase == Live`; `MatchPhaseHud` already tests `Phase` directly for its panels.
- **Why the hard cap lives on `GameSettingsManager`.** It is a match rule, and it belongs beside `matchTimeLimit`, which it backstops — and it replaces `scoreLimit` in the same `[Header("Match Settings")]` block, so the operator-facing inspector gains a valve exactly where it loses a footgun. It is in **minutes**, matching `matchTimeLimit`'s unit.
- **`TickTimer.None` never expires**, so the `SuddenDeath` case in `FixedUpdateNetwork` is inert in default play — no branch is needed around the cap check.
