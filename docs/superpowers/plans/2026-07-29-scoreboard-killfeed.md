# In-Match Scoreboard / Roster / Stats — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a server-authoritative, AoI-safe per-player stat table (kills, deaths, captures, coins deposited, flag carry seconds, flag returns) and a hold-Tab full scoreboard UI that also auto-shows as the PostMatch results screen, sorted by a derived Overall Score.

**Architecture:** One new central, always-interested `MatchStatsManager` singleton (not a per-player component — Fusion's AoI is per-`NetworkObject`, and a stats component on each player's own culled avatar would force that whole avatar always-interested for everyone). Pure math (the score formula, the flag-carry-time accumulator, roster index bounds) lives in a new engine-free `Game.Stats.Core` assembly, unit-tested outside Unity. Six existing server call sites (`PlayerStatsHandler.Die/Respawn`, `CTFGameManager.OnCarrierEnteredBase`, `HomeBase.ServerDeposit`, `Flag.FixedUpdateNetwork/ReturnFlag`, `PlayerTeamData.SetTeam`, `NetworkedSpawnManager.TrySpawnPlayer`) each grow one or two lines to report into it. A new `Game.Hud.Core.ScoreboardSort` (also engine-free, unit-tested) does the pure group/sort math for the UI; `ScoreboardPanel`/`ScoreboardRowView` are the Fusion/Unity-facing MonoBehaviours, built via a one-click Editor tool mirroring `MatchHudBuilder`.

**Tech Stack:** Unity 6.3 (6000.3.0f1), Photon Fusion 2 (Host/Client + dedicated server), C#, NUnit EditMode tests, TextMeshPro, new Input System.

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-07-29-scoreboard-killfeed-design.md` — read it first; this plan implements every decision in it verbatim.
- **No live killfeed.** The spec explicitly cuts a real-time "X eliminated Y" event feed (Decision 9). Do not add one. Kills/deaths/captures are visible only as accumulating stats on the board.
- **`MatchStatsManager` must be always-interested** under Area-of-Interest, exactly like `TeamScoreManager` and `MatchManager` — mark its GameObject with `AlwaysInterestedMarker` in the scene (Task 2 has the exact step).
- **AoI is per-`NetworkObject`, not per-component.** Do not add a stats `NetworkBehaviour` to the player avatar prefab. All stat/identity state for the scoreboard lives on `MatchStatsManager` only.
- **All authoritative writes happen only under `HasStateAuthority`.** Every `MatchStatsManager` method starts with that guard.
- **The Overall Score is derived, never networked.** `ScoreFormula.Compute(...)` is called client-side from already-replicated inputs; do not add a networked `OverallScore` field anywhere.
- **`Team` enum:** `None=0, Team1=1, Team2=2, Team3AI=3` (`Assets/Scripts/Teams/Team.cs`). AI (`Team3AI`) is never registered in `MatchStatsManager` — only `NetworkedSpawnManager.TrySpawnPlayer` (human players only) calls `RegisterPlayer`.
- **Captures are tracked and displayed but are NOT an input to the score formula** (spec Decision 3). Do not add them to `ScoreFormula.Compute`.
- **Roster capacity is 20**, matching `GameNetworkManager.maxPlayers` (`Assets/Scripts/GameNetworkManager.cs:29`). Slots are indexed directly by Fusion `PlayerId`.
- **Pure, unit-tested logic lives in engine-free asmdefs** (`Game.Stats.Core`, and additions to `Game.Hud.Core`) — both have/get `noEngineReferences: true`, so no `UnityEngine.Mathf`, no `Fusion` types, in those files. `NetworkBehaviour`s stay in the default assembly (no asmdef), matching `PlayerStatsHandler`, `Flag`, `CTFGameManager`, `TeamScoreManager`, `HomeBase`, `PlayerTeamData`, `NetworkedSpawnManager`, `GameNetworkManager` today.
- **A leaving player's row is dropped, not preserved** (spec Decision 7). `MatchStatsManager` never clears a slot on disconnect; the UI filters by `Runner.ActivePlayers` membership at render time. Do not add a `Connected` field.
- **Out of scope — do not touch:** assists, a "disconnected" ghost row, any change to `HudToastFeed` or the buff-unlock toast surfaces, in-place networked reset of `MatchStatsManager` (scene reload already handles reset per the match-lifecycle contract), and re-weighting the score formula from a player-facing UI.

### Numbers, verbatim from the spec

| Thing | Value |
|---|---|
| Roster capacity | 20 (`MatchStatsManager.RosterCapacity`, matches `GameNetworkManager.maxPlayers`) |
| Score weight — kill | `+10` |
| Score weight — death | `−10` |
| Score weight — coin deposited | `+0.75` |
| Score weight — flag carry second | `+1` |
| Score weight — flag return | `+20` |
| Display-name capacity | `NetworkString<_64>` (matches `LobbyProtocol.MaxNicknameBytes`) |
| Input | Hold `<Keyboard>/tab`, new `UI/Scoreboard` action, no interaction modifier (plain press/release) |

### How to run tests

EditMode tests run in Unity: **Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All**.

If the editor holds the project lock (`Unity.exe -batchmode -runTests` then fails), use the bundled-Roslyn workaround: compile the engine-free core `.cs` plus a hand-written assert harness against `netstandard 2.1` with
`C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Data\DotNetSdkRoslyn\csc.dll`, write a `net8.0` `runtimeconfig.json` beside the exe, and run it on
`C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Data\NetCoreRuntime\dotnet.exe`.
For the whole-surface compile gate, build a `@response.rsp` for `csc.dll` referencing the netstandard ref, `Editor\Data\Managed\UnityEngine\*.dll`, `Assets\Photon\Fusion\Assemblies\*.dll`, and `Library\ScriptAssemblies\*.dll` (skip `*Editor*` / `*CodeGen*` / `*Tests*`), compiling every `Assets/Scripts/**/*.cs` **except** the asmdef-owned folders (`Buffs/Core`, `Combat/Core`, `Enemy/AI`, `Hud/Core`, `Match/Core`, `Net`, `Player/Animation/Core`, `Player/Movement/Core`, `Stats/Core` once Task 1 lands). Quote every path inside the `.rsp` ("Program Files" has a space). When a core assembly changed in this branch, drop its stale `Library\ScriptAssemblies\Game.*.Core.dll` from the references and compile that folder's `.cs` inline instead.

Networked/Fusion behavior (Tasks 2–10 beyond their pure-math pieces) is verified by entering Play mode as Host (single-player mode is already the default in `GameNetworkManager`) and observing the documented effect. **A clean compile is not verification.** Report separately what was executed and what was only compiled.

---

## File Structure

**Created:**
- `Assets/Scripts/Stats/Core/Game.Stats.Core.asmdef` (+ `.meta`) — new engine-free assembly.
- `Assets/Scripts/Stats/Core/ScoreWeights.cs`, `ScoreFormula.cs`, `FlagCarryAccumulator.cs`, `RosterIndex.cs` (+ `.meta` each) — pure math.
- `Assets/Tests/EditMode/Stats/Game.Stats.Core.Tests.asmdef` (+ `.meta`) — new test assembly.
- `Assets/Tests/EditMode/Stats/ScoreFormulaTests.cs`, `FlagCarryAccumulatorTests.cs`, `RosterIndexTests.cs` (+ `.meta` each).
- `Assets/Scripts/Stats/MatchStatsManager.cs` (+ `.meta`) — the networked singleton + `PlayerStatEntry` struct.
- `Assets/Scripts/Hud/Core/ScoreboardSort.cs` (+ `.meta`) — `ScoreboardRow` + pure sort, in the existing `Game.Hud.Core` asmdef.
- `Assets/Tests/EditMode/Hud/ScoreboardSortTests.cs` (+ `.meta`) — in the existing `Game.Hud.Tests` asmdef.
- `Assets/Scripts/Hud/ScoreboardRowView.cs`, `ScoreboardPanel.cs`, `ScoreboardInputReader.cs` (+ `.meta` each) — the on-screen board.
- `Assets/Scripts/Editor/ScoreboardHudBuilder.cs` (+ `.meta`) — one-click scene wiring, mirrors `MatchHudBuilder`.

**Modified:**
- `Assets/Scripts/Player/PlayerStatsHandler.cs` — `lastAttackerId` field, `ServerApplyDamage` records it, `Die()`/`Respawn()` report into `MatchStatsManager`.
- `Assets/Scripts/CTF Flag/CTFGameManager.cs` — `OnCarrierEnteredBase` records a capture.
- `Assets/Scripts/Coin Scripts/HomeBase.cs` — `ServerDeposit` records the deposit.
- `Assets/Scripts/CTF Flag/Flag.cs` — carry-time accumulation in `FixedUpdateNetwork`; `ReturnFlag` gains an optional attributed-returner parameter.
- `Assets/Scripts/Player/PlayerTeamData.cs` — `SetTeam` mirrors into `MatchStatsManager`.
- `Assets/Scripts/GameNetworkManager.cs` — new `LobbyNicknameChoices` static class, wired at the same four points as `LobbyTeamChoices`.
- `Assets/Scripts/NetworkedSpawnManager.cs` — `TrySpawnPlayer` calls `MatchStatsManager.RegisterPlayer`.
- `Assets/Scripts/Hud/MatchPhaseHud.cs` — forces the scoreboard visible for the whole `PostMatch` phase.
- `Assets/InputSystem_Actions.inputactions` — new `UI/Scoreboard` action bound to `<Keyboard>/tab`.

**Also modified (manual Unity editor steps, not hand-edited YAML):** `Assets/Scenes/Gameplay.unity` — gains the `MatchStatsManager` GameObject (Task 2), the `ScoreboardPanel` hierarchy built by `ScoreboardHudBuilder` (Task 9), and the `ScoreboardInputReader` + `MatchPhaseHud` field wiring (Task 10). Every scene change happens through the Unity Editor UI or the one-click builder, never by hand-editing scene YAML. No existing HUD surface (`TeamScoreDisplay`, `HudToastFeed`, `BuffIconDisplay`) changes.

---

## Task 1: Pure stats math (`Game.Stats.Core`)

**Files:**
- Create: `Assets/Scripts/Stats/Core/Game.Stats.Core.asmdef` (+ `.meta`)
- Create: `Assets/Scripts/Stats/Core/ScoreWeights.cs` (+ `.meta`)
- Create: `Assets/Scripts/Stats/Core/ScoreFormula.cs` (+ `.meta`)
- Create: `Assets/Scripts/Stats/Core/FlagCarryAccumulator.cs` (+ `.meta`)
- Create: `Assets/Scripts/Stats/Core/RosterIndex.cs` (+ `.meta`)
- Create: `Assets/Tests/EditMode/Stats/Game.Stats.Core.Tests.asmdef` (+ `.meta`)
- Test: `Assets/Tests/EditMode/Stats/ScoreFormulaTests.cs` (+ `.meta`)
- Test: `Assets/Tests/EditMode/Stats/FlagCarryAccumulatorTests.cs` (+ `.meta`)
- Test: `Assets/Tests/EditMode/Stats/RosterIndexTests.cs` (+ `.meta`)

Two new folders need their own folder `.meta` too (Unity requires one per folder): `Assets/Scripts/Stats/` and `Assets/Scripts/Stats/Core/` do not exist yet, nor does `Assets/Tests/EditMode/Stats/`.

**Interfaces:**
- Consumes: nothing (engine-free leaf).
- Produces:
  - `Game.Stats.Core.ScoreWeights` — struct with `float Kill, Death, Coin, FlagCarrySecond, FlagReturn;` and `static ScoreWeights Default`.
  - `static float Game.Stats.Core.ScoreFormula.Compute(int kills, int deaths, int coinsDeposited, int flagCarrySeconds, int flagReturns, ScoreWeights weights)`
  - `static int Game.Stats.Core.FlagCarryAccumulator.Tick(ref float remainderSeconds, float deltaTime)`
  - `static bool Game.Stats.Core.RosterIndex.TryResolve(int playerId, int capacity, out int index)`

- [ ] **Step 1: Create the folder structure and the Core assembly definition**

Create `Assets/Scripts/Stats.meta`:

```yaml
fileFormatVersion: 2
guid: 52df140b279041b2823ef4fa48baea4c
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

Create `Assets/Scripts/Stats/Core.meta`:

```yaml
fileFormatVersion: 2
guid: 1a606e2c54d941aebd024b69efce13a5
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

Create `Assets/Scripts/Stats/Core/Game.Stats.Core.asmdef`:

```json
{
    "name": "Game.Stats.Core",
    "rootNamespace": "Game.Stats.Core",
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

Create `Assets/Scripts/Stats/Core/Game.Stats.Core.asmdef.meta`:

```yaml
fileFormatVersion: 2
guid: 572dda2724924ccda27faf2ac4e389c4
AssemblyDefinitionImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

`autoReferenced: true` makes the default assembly see these types automatically; `noEngineReferences: true` keeps it pure C# so it compiles and runs outside Unity.

- [ ] **Step 2: Write the pure value types**

Create `Assets/Scripts/Stats/Core/ScoreWeights.cs`:

```csharp
namespace Game.Stats.Core
{
    /// <summary>
    /// Per-stat weights for the derived Overall Score. Authored/tunable on MatchStatsManager --
    /// see the design spec's weight table for the starting values and their rationale
    /// (objective-first, then explicitly revised so kills/deaths sit at parity).
    /// </summary>
    public struct ScoreWeights
    {
        public float Kill;
        public float Death;
        public float Coin;
        public float FlagCarrySecond;
        public float FlagReturn;

        public static ScoreWeights Default => new ScoreWeights
        {
            Kill = 10f,
            Death = -10f,
            Coin = 0.75f,
            FlagCarrySecond = 1f,
            FlagReturn = 20f
        };
    }
}
```

Create `Assets/Scripts/Stats/Core/ScoreWeights.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 9bbf47d8046143b6b3d28cdb5f644a14
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

Create `Assets/Scripts/Stats/Core/ScoreFormula.cs`:

```csharp
namespace Game.Stats.Core
{
    /// <summary>
    /// The scoreboard's headline stat, derived on query from five networked inputs -- never
    /// stored, so there is nothing to keep in sync with its own inputs and nothing to reset.
    /// Captures are deliberately NOT an input (tracked/displayed separately) -- see
    /// docs/superpowers/specs/2026-07-29-scoreboard-killfeed-design.md, "Overall score".
    /// </summary>
    public static class ScoreFormula
    {
        public static float Compute(int kills, int deaths, int coinsDeposited, int flagCarrySeconds,
                                     int flagReturns, ScoreWeights weights)
        {
            return kills * weights.Kill
                 + deaths * weights.Death
                 + coinsDeposited * weights.Coin
                 + flagCarrySeconds * weights.FlagCarrySecond
                 + flagReturns * weights.FlagReturn;
        }
    }
}
```

Create `Assets/Scripts/Stats/Core/ScoreFormula.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 61ba4520ebc04e3eb0202a91d7854a8f
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

Create `Assets/Scripts/Stats/Core/FlagCarryAccumulator.cs`:

```csharp
namespace Game.Stats.Core
{
    /// <summary>
    /// Converts a per-tick delta time into whole seconds to flush to the networked stat table,
    /// keeping the sub-second remainder for the next tick. Bounds the networked write rate to at
    /// most once per second per carried flag, regardless of tick rate.
    /// </summary>
    public static class FlagCarryAccumulator
    {
        public static int Tick(ref float remainderSeconds, float deltaTime)
        {
            remainderSeconds += deltaTime;
            int whole = (int)remainderSeconds;
            remainderSeconds -= whole;
            return whole;
        }
    }
}
```

Create `Assets/Scripts/Stats/Core/FlagCarryAccumulator.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 211c66b790a345faad07b6541745bf67
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

Create `Assets/Scripts/Stats/Core/RosterIndex.cs`:

```csharp
namespace Game.Stats.Core
{
    /// <summary>Maps a Fusion PlayerId directly to a MatchStatsManager roster slot, bounds-checked.</summary>
    public static class RosterIndex
    {
        public static bool TryResolve(int playerId, int capacity, out int index)
        {
            index = playerId;
            return playerId >= 0 && playerId < capacity;
        }
    }
}
```

Create `Assets/Scripts/Stats/Core/RosterIndex.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 30806d15c50b4c9fa732cc67f8ccc262
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

- [ ] **Step 3: Write the failing tests**

Create `Assets/Tests/EditMode/Stats.meta`:

```yaml
fileFormatVersion: 2
guid: eeafc07787004667a5e5cba3d8c23f3f
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

Create `Assets/Tests/EditMode/Stats/Game.Stats.Core.Tests.asmdef`:

```json
{
    "name": "Game.Stats.Core.Tests",
    "rootNamespace": "",
    "references": [
        "Game.Stats.Core",
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

Create `Assets/Tests/EditMode/Stats/Game.Stats.Core.Tests.asmdef.meta`:

```yaml
fileFormatVersion: 2
guid: c774aa1e635545298caba0df30546894
AssemblyDefinitionImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

Create `Assets/Tests/EditMode/Stats/ScoreFormulaTests.cs`:

```csharp
using NUnit.Framework;
using Game.Stats.Core;

public class ScoreFormulaTests
{
    private static readonly ScoreWeights Weights = ScoreWeights.Default;

    [Test]
    public void AllZeroInputsScoreZero()
    {
        Assert.AreEqual(0f, ScoreFormula.Compute(0, 0, 0, 0, 0, Weights));
    }

    [Test]
    public void KillsAndDeathsAreWeightedAtParity()
    {
        // 10 kills = +100, 10 deaths = -100 -> net zero, per the design spec's weight table.
        float score = ScoreFormula.Compute(kills: 10, deaths: 10, coinsDeposited: 0,
            flagCarrySeconds: 0, flagReturns: 0, Weights);
        Assert.AreEqual(0f, score);
    }

    [Test]
    public void DeathsSubtractFromScore()
    {
        float score = ScoreFormula.Compute(kills: 0, deaths: 5, coinsDeposited: 0,
            flagCarrySeconds: 0, flagReturns: 0, Weights);
        Assert.AreEqual(-50f, score);
    }

    [Test]
    public void CoinsContributeAtThreeQuarterWeight()
    {
        float score = ScoreFormula.Compute(0, 0, coinsDeposited: 100, 0, 0, Weights);
        Assert.AreEqual(75f, score);
    }

    [Test]
    public void FlagCarrySecondsContributeOneToOne()
    {
        float score = ScoreFormula.Compute(0, 0, 0, flagCarrySeconds: 120, 0, Weights);
        Assert.AreEqual(120f, score);
    }

    [Test]
    public void FlagReturnsAreWorthTwentyEach()
    {
        float score = ScoreFormula.Compute(0, 0, 0, 0, flagReturns: 3, Weights);
        Assert.AreEqual(60f, score);
    }

    [Test]
    public void AllFiveInputsCombineAdditively()
    {
        // 2 kills(+20) - 1 death(-10) + 40 coins(+30) + 30 carry-seconds(+30) + 2 returns(+40) = 110
        float score = ScoreFormula.Compute(kills: 2, deaths: 1, coinsDeposited: 40,
            flagCarrySeconds: 30, flagReturns: 2, Weights);
        Assert.AreEqual(110f, score, 0.001f);
    }
}
```

Create `Assets/Tests/EditMode/Stats/ScoreFormulaTests.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 781ff3cb5f7d41229fde22bd9c95bbc2
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

Create `Assets/Tests/EditMode/Stats/FlagCarryAccumulatorTests.cs`:

```csharp
using NUnit.Framework;
using Game.Stats.Core;

public class FlagCarryAccumulatorTests
{
    [Test]
    public void SubSecondDeltaFlushesNothingAndKeepsRemainder()
    {
        float remainder = 0f;
        int whole = FlagCarryAccumulator.Tick(ref remainder, 0.3f);
        Assert.AreEqual(0, whole);
        Assert.AreEqual(0.3f, remainder, 0.0001f);
    }

    [Test]
    public void CrossingOneSecondFlushesExactlyOne()
    {
        float remainder = 0.8f;
        int whole = FlagCarryAccumulator.Tick(ref remainder, 0.3f);
        Assert.AreEqual(1, whole);
        Assert.AreEqual(0.1f, remainder, 0.0001f);
    }

    [Test]
    public void RepeatedTicksAccumulateAcrossCalls()
    {
        float remainder = 0f;
        int totalFlushed = 0;
        for (int i = 0; i < 10; i++) // 10 ticks of 0.11s = 1.1s
            totalFlushed += FlagCarryAccumulator.Tick(ref remainder, 0.11f);
        Assert.AreEqual(1, totalFlushed);
        Assert.AreEqual(0.1f, remainder, 0.001f);
    }

    [Test]
    public void ALargeDeltaCanFlushMoreThanOneSecond()
    {
        float remainder = 0f;
        int whole = FlagCarryAccumulator.Tick(ref remainder, 2.3f);
        Assert.AreEqual(2, whole);
        Assert.AreEqual(0.3f, remainder, 0.0001f);
    }
}
```

Create `Assets/Tests/EditMode/Stats/FlagCarryAccumulatorTests.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 4d96bb2be1864b3792c0beda7328fd39
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

Create `Assets/Tests/EditMode/Stats/RosterIndexTests.cs`:

```csharp
using NUnit.Framework;
using Game.Stats.Core;

public class RosterIndexTests
{
    [Test]
    public void ValidPlayerIdResolvesToItself()
    {
        Assert.IsTrue(RosterIndex.TryResolve(5, 20, out int index));
        Assert.AreEqual(5, index);
    }

    [Test]
    public void PlayerIdAtCapacityIsOutOfRange()
    {
        Assert.IsFalse(RosterIndex.TryResolve(20, 20, out _));
    }

    [Test]
    public void PlayerIdOneBelowCapacityIsInRange()
    {
        Assert.IsTrue(RosterIndex.TryResolve(19, 20, out int index));
        Assert.AreEqual(19, index);
    }

    [Test]
    public void NegativePlayerIdIsOutOfRange()
    {
        Assert.IsFalse(RosterIndex.TryResolve(-1, 20, out _));
    }

    [Test]
    public void ZeroCapacityRejectsEverything()
    {
        Assert.IsFalse(RosterIndex.TryResolve(0, 0, out _));
    }
}
```

Create `Assets/Tests/EditMode/Stats/RosterIndexTests.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 88d12cd1fdb7427bb55a80f90daf80bd
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

- [ ] **Step 4: Run the tests to verify they pass**

Run: Test Runner ▸ EditMode ▸ Run All (or the Roslyn harness from "How to run tests").
Expected: PASS — all 15 cases across `ScoreFormulaTests`, `FlagCarryAccumulatorTests`, `RosterIndexTests` green.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Stats" "Assets/Scripts/Stats.meta" "Assets/Tests/EditMode/Stats" "Assets/Tests/EditMode/Stats.meta"
git commit -m "feat(stats): pure score/roster math in a new Game.Stats.Core assembly"
```

---

## Task 2: `MatchStatsManager` — the central always-interested table

**Files:**
- Create: `Assets/Scripts/Stats/MatchStatsManager.cs` (+ `.meta`)

**Interfaces:**
- Consumes: `Game.Stats.Core.RosterIndex.TryResolve` (Task 1), `Game.Stats.Core.ScoreWeights` (Task 1).
- Produces:
  - `struct PlayerStatEntry : INetworkStruct { NetworkBool Active; byte Team; NetworkString<_64> DisplayName; NetworkBool IsDead; int Kills; int Deaths; int Captures; int CoinsDeposited; int FlagCarrySeconds; int FlagReturns; }`
  - `class MatchStatsManager : NetworkBehaviour` with `static MatchStatsManager Instance`, `const int RosterCapacity = 20`, `NetworkArray<PlayerStatEntry> Entries`, `ScoreWeights Weights`.
  - `void RegisterPlayer(int playerId, int team, string displayName)`
  - `void SetTeam(int playerId, int team)`
  - `void SetDead(int playerId, bool isDead)`
  - `void RecordKill(PlayerRef attacker)`
  - `void RecordDeath(PlayerRef player)`
  - `void RecordCapture(PlayerRef carrier)`
  - `void RecordDeposit(PlayerRef player, int points)`
  - `void RecordFlagCarrySeconds(PlayerRef carrier, int seconds)`
  - `void RecordFlagReturn(PlayerRef returner)`
  - `bool TryGetEntry(int playerId, out PlayerStatEntry entry)`

This is a Fusion `NetworkBehaviour`; it is not unit-testable in isolation (no EditMode test for this file — verified by compile + manual Play mode, per project convention).

- [ ] **Step 1: Write the class**

Create `Assets/Scripts/Stats/MatchStatsManager.cs`:

```csharp
using Fusion;
using UnityEngine;
using Game.Stats.Core;

/// <summary>
/// Single central, always-interested source of per-player match stats (kills, deaths, captures,
/// coins deposited, flag carry time, flag returns) plus the small subset of identity/state
/// (team, display name, alive/dead) the scoreboard needs for every player regardless of distance.
///
/// AoI applies per NetworkObject, not per component: a stats component living on each player's
/// own (AoI-culled) avatar would force that whole avatar object always-interested for every
/// viewer, defeating AoI at 20-player scale. This is a scene singleton instead -- mark its
/// GameObject with AlwaysInterestedMarker in the inspector, exactly like TeamScoreManager and
/// MatchManager.
///
/// See docs/superpowers/specs/2026-07-29-scoreboard-killfeed-design.md.
/// </summary>
public class MatchStatsManager : NetworkBehaviour
{
    public static MatchStatsManager Instance { get; private set; }

    /// <summary>Matches GameNetworkManager.maxPlayers (20); slots are indexed by PlayerId directly.</summary>
    public const int RosterCapacity = 20;

    [Networked, Capacity(RosterCapacity)]
    public NetworkArray<PlayerStatEntry> Entries => default;

    [Header("Overall score weights (tunable -- see the design spec's weight table)")]
    [SerializeField] private float killWeight = 10f;
    [SerializeField] private float deathWeight = -10f;
    [SerializeField] private float coinWeight = 0.75f;
    [SerializeField] private float flagCarrySecondWeight = 1f;
    [SerializeField] private float flagReturnWeight = 20f;

    public ScoreWeights Weights => new ScoreWeights
    {
        Kill = killWeight,
        Death = deathWeight,
        Coin = coinWeight,
        FlagCarrySecond = flagCarrySecondWeight,
        FlagReturn = flagReturnWeight
    };

    private void Awake()
    {
        // Never Destroy() a spawned NetworkObject locally (desyncs Fusion's object table on this
        // peer); disable the duplicate and leave it inert, matching TeamScoreManager's guard.
        if (Instance != null && Instance != this) { enabled = false; return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// SERVER: create/refresh a player's roster entry. Called once at spawn from
    /// NetworkedSpawnManager.TrySpawnPlayer, which already knows the resolved team -- so this sets
    /// Team directly and does not depend on PlayerTeamData.SetTeam's mirror having run first.
    /// </summary>
    public void RegisterPlayer(int playerId, int team, string displayName)
    {
        if (!HasStateAuthority) return;
        if (!RosterIndex.TryResolve(playerId, RosterCapacity, out int index)) return;

        Entries.Set(index, new PlayerStatEntry
        {
            Active = true,
            Team = (byte)team,
            DisplayName = displayName,
            IsDead = false,
            Kills = 0,
            Deaths = 0,
            Captures = 0,
            CoinsDeposited = 0,
            FlagCarrySeconds = 0,
            FlagReturns = 0
        });
    }

    /// <summary>SERVER: mirrors a team reassignment after the entry already exists (e.g. a team switch).</summary>
    public void SetTeam(int playerId, int team)
    {
        if (!HasStateAuthority) return;
        if (!TryGetMutable(playerId, out int index, out var entry)) return;
        entry.Team = (byte)team;
        Entries.Set(index, entry);
    }

    public void SetDead(int playerId, bool isDead)
    {
        if (!HasStateAuthority) return;
        if (!TryGetMutable(playerId, out int index, out var entry)) return;
        entry.IsDead = isDead;
        Entries.Set(index, entry);
    }

    public void RecordKill(PlayerRef attacker)
    {
        if (!HasStateAuthority || !attacker.IsRealPlayer) return;
        if (!TryGetMutable(attacker.PlayerId, out int index, out var entry)) return;
        entry.Kills++;
        Entries.Set(index, entry);
    }

    public void RecordDeath(PlayerRef player)
    {
        if (!HasStateAuthority || !player.IsRealPlayer) return;
        if (!TryGetMutable(player.PlayerId, out int index, out var entry)) return;
        entry.Deaths++;
        Entries.Set(index, entry);
    }

    public void RecordCapture(PlayerRef carrier)
    {
        if (!HasStateAuthority || !carrier.IsRealPlayer) return;
        if (!TryGetMutable(carrier.PlayerId, out int index, out var entry)) return;
        entry.Captures++;
        Entries.Set(index, entry);
    }

    public void RecordDeposit(PlayerRef player, int points)
    {
        if (!HasStateAuthority || !player.IsRealPlayer || points <= 0) return;
        if (!TryGetMutable(player.PlayerId, out int index, out var entry)) return;
        entry.CoinsDeposited += points;
        Entries.Set(index, entry);
    }

    public void RecordFlagCarrySeconds(PlayerRef carrier, int seconds)
    {
        if (!HasStateAuthority || !carrier.IsRealPlayer || seconds <= 0) return;
        if (!TryGetMutable(carrier.PlayerId, out int index, out var entry)) return;
        entry.FlagCarrySeconds += seconds;
        Entries.Set(index, entry);
    }

    public void RecordFlagReturn(PlayerRef returner)
    {
        if (!HasStateAuthority || !returner.IsRealPlayer) return;
        if (!TryGetMutable(returner.PlayerId, out int index, out var entry)) return;
        entry.FlagReturns++;
        Entries.Set(index, entry);
    }

    /// <summary>Read accessor for the scoreboard UI. False when the slot is unused or out of range.</summary>
    public bool TryGetEntry(int playerId, out PlayerStatEntry entry)
    {
        entry = default;
        if (!RosterIndex.TryResolve(playerId, RosterCapacity, out int index)) return false;
        entry = Entries.Get(index);
        return entry.Active;
    }

    private bool TryGetMutable(int playerId, out int index, out PlayerStatEntry entry)
    {
        entry = default;
        if (!RosterIndex.TryResolve(playerId, RosterCapacity, out index)) return false;
        entry = Entries.Get(index);
        return entry.Active; // ignore writes for a player with no registered entry yet
    }
}

/// <summary>
/// One player's replicated match stats, plus the identity/state slice the scoreboard needs
/// regardless of AoI distance. Indexed by PlayerId in MatchStatsManager.Entries.
/// </summary>
public struct PlayerStatEntry : INetworkStruct
{
    public NetworkBool Active;
    public byte Team;
    public NetworkString<_64> DisplayName;
    public NetworkBool IsDead;
    public int Kills;
    public int Deaths;
    public int Captures;
    public int CoinsDeposited;
    public int FlagCarrySeconds;
    public int FlagReturns;
}
```

Create `Assets/Scripts/Stats/MatchStatsManager.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 5605187109884835a0fbd762f83255d8
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

- [ ] **Step 2: Compile-check**

Run the bundled-Roslyn whole-surface compile gate from "How to run tests" (or let the Unity editor recompile if it is open and unlocked). Expected: no errors. `Game.Stats.Core` is a new core folder in this branch — compile it inline per the gate's instructions rather than relying on a stale `Library\ScriptAssemblies\Game.Stats.Core.dll`.

- [ ] **Step 3: Manual scene setup**

In the Unity Editor, open `Assets/Scenes/Gameplay.unity`:
1. Create an empty GameObject named `MatchStatsManager` (sibling to the existing `TeamScoreManager`/`MatchManager` GameObjects).
2. Add the `MatchStatsManager` component.
3. Add the `NetworkObject` component (Fusion requires this for any spawned/networked scene object — check whether it's auto-added; if not, add it explicitly).
4. Add the `AlwaysInterestedMarker` component (`Assets/Scripts/AreaOfInterest/AlwaysInterestedMarker.cs`) — this is the step that keeps the whole table visible to every player regardless of AoI distance.
5. Save the scene.

- [ ] **Step 4: Manual verification**

Enter Play mode as Host (single-player mode). Confirm no console errors on scene load (a missing `AlwaysInterestedMarker` or duplicate singleton would log via the existing guard patterns). This step only confirms the manager spawns cleanly — its actual data is exercised by Tasks 3–7.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Stats/MatchStatsManager.cs" "Assets/Scripts/Stats/MatchStatsManager.cs.meta" "Assets/Scenes/Gameplay.unity"
git commit -m "feat(stats): add MatchStatsManager, the central always-interested stat table"
```

---

## Task 3: Kill / death / dead-state hook (`PlayerStatsHandler`)

**Files:**
- Modify: `Assets/Scripts/Player/PlayerStatsHandler.cs`

**Interfaces:**
- Consumes: `MatchStatsManager.Instance.RecordKill/RecordDeath/SetDead` (Task 2).
- Produces: no new public API — `Die()`/`Respawn()`/`ServerApplyDamage` behavior only.

- [ ] **Step 1: Add the last-attacker field and record it in `ServerApplyDamage`**

In `Assets/Scripts/Player/PlayerStatsHandler.cs`, find the private field block near `hitLedger` (around line 58) and add a new field:

```csharp
    // Per-attacker rapid-hit guard (server-only; keyed by the attacking NetworkObject's id).
    // Replaces the old single global HitCooldownTimer, which ate a second attacker's hit
    // landing inside the window. Never networked; cleared on respawn.
    private readonly HitCooldownLedger hitLedger = new HitCooldownLedger();

    // Most recent attacker on this life, for kill attribution in Die(). Non-networked and safe:
    // it is written and consumed within the same synchronous server call (ServerApplyDamage ->
    // Die()), never read across a tick boundary, so it needs no resimulation safety.
    private NetworkId lastAttackerId;
```

In `ServerApplyDamage`, record the attacker right after the rapid-hit guard passes:

```csharp
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

        lastAttackerId = attackerId;

        CurrentHealth -= damage;
        CurrentHealth = Mathf.Max(0, CurrentHealth);

        if (CurrentHealth <= 0)
        {
            CurrentHealth = 0;
            Die();
        }
    }
```

- [ ] **Step 2: Report the death into `MatchStatsManager` from `Die()`**

Replace the start of `Die()`:

```csharp
    private void Die()
    {
        if (!HasStateAuthority) return;

        IsDead = true;

        ReportDeathToStats();

        // Decide where we will respawn NOW, so the camera transition and the respawn teleport
        // target the exact same point (see RespawnPosition).
        RespawnPosition = ResolveSpawnPosition();

        // Drop flag if carrying one
        DropFlagOnDeath();
```

(the rest of `Die()` is unchanged). Add the new private method anywhere in the class, e.g. right after `Die()`:

```csharp
    /// <summary>
    /// SERVER: mirrors this death into the scoreboard (self death, and the killer's kill if the
    /// last hit resolves to a real player who is not this same player). An attacker NetworkId
    /// that fails to resolve (environmental damage, the default path from RPC_TakeDamage) or that
    /// resolves to a non-player NetworkObject (an AI enemy) credits no kill to anyone --
    /// RecordKill's own IsRealPlayer guard on the manager side handles the AI case.
    /// </summary>
    private void ReportDeathToStats()
    {
        if (MatchStatsManager.Instance == null) return;

        PlayerRef self = Object.InputAuthority;
        MatchStatsManager.Instance.SetDead(self.PlayerId, true);
        MatchStatsManager.Instance.RecordDeath(self);

        if (Runner.TryFindObject(lastAttackerId, out NetworkObject attackerObj) && attackerObj != null)
        {
            PlayerRef attacker = attackerObj.InputAuthority;
            if (attacker != self)
                MatchStatsManager.Instance.RecordKill(attacker);
        }
    }
```

- [ ] **Step 3: Mirror `IsDead = false` from `Respawn()`**

In `Respawn()`, right after `IsDead = false;`:

```csharp
    private void Respawn()
    {
        if (!HasStateAuthority)
        {
            Debug.LogWarning("Respawn called on client - only server can respawn players!");
            return;
        }

        CurrentHealth = stats.maxHealth;
        IsDead = false;
        SpawnImmunityTimer = TickTimer.CreateFromSeconds(Runner, spawnImmunityDuration); // Reset spawn immunity
        hitLedger.Clear(); // fresh life, no stale attacker cooldowns

        if (MatchStatsManager.Instance != null)
            MatchStatsManager.Instance.SetDead(Object.InputAuthority.PlayerId, false);
```

(the rest of `Respawn()` — teleport, velocity reset, `RPC_EnablePlayerControls()` — is unchanged).

- [ ] **Step 4: Compile-check**

Run the bundled-Roslyn whole-surface compile gate (or let Unity recompile). Expected: no errors.

- [ ] **Step 5: Manual verification**

Enter Play mode as Host + one client (Multiplayer Play Mode). Have the client kill the host (or vice versa): confirm no console errors. Full observable verification (reading `MatchStatsManager.Entries` values) is deferred to Task 9's scoreboard UI — this step only confirms the hook doesn't throw.

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/Player/PlayerStatsHandler.cs"
git commit -m "feat(stats): attribute kills/deaths and mirror alive state into MatchStatsManager"
```

---

## Task 4: Capture hook (`CTFGameManager`)

**Files:**
- Modify: `Assets/Scripts/CTF Flag/CTFGameManager.cs`

**Interfaces:**
- Consumes: `MatchStatsManager.Instance.RecordCapture` (Task 2).
- Produces: no new public API.

- [ ] **Step 1: Record the capture in `OnCarrierEnteredBase`**

```csharp
    public void OnCarrierEnteredBase(PlayerRef carrier, Team baseTeam)
    {
        if (!HasStateAuthority) return;
        if (MatchManager.Instance == null || !MatchManager.Instance.IsPlayActive) return;
        if (team1Flag == null || team2Flag == null) return;

        if (baseTeam == Team.Team1 &&
            team2Flag.IsCarriedBy(carrier) && team1Flag.State == Flag.FlagState.AtHome)
        {
            if (MatchStatsManager.Instance != null) MatchStatsManager.Instance.RecordCapture(carrier);
            MatchManager.Instance.ReportCapture(Team.Team1);
        }
        else if (baseTeam == Team.Team2 &&
            team1Flag.IsCarriedBy(carrier) && team2Flag.State == Flag.FlagState.AtHome)
        {
            if (MatchStatsManager.Instance != null) MatchStatsManager.Instance.RecordCapture(carrier);
            MatchManager.Instance.ReportCapture(Team.Team2);
        }
    }
```

- [ ] **Step 2: Compile-check**

Run the bundled-Roslyn whole-surface compile gate (or let Unity recompile). Expected: no errors.

- [ ] **Step 3: Manual verification**

Enter Play mode as Host, carry the enemy flag into your base and confirm a capture still ends the match exactly as before (no regression to `MatchManager.ReportCapture`). Console shows no errors from the new call.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/CTF Flag/CTFGameManager.cs"
git commit -m "feat(stats): record flag captures into MatchStatsManager"
```

---

## Task 5: Coin-deposit hook (`HomeBase`)

**Files:**
- Modify: `Assets/Scripts/Coin Scripts/HomeBase.cs`

**Interfaces:**
- Consumes: `MatchStatsManager.Instance.RecordDeposit` (Task 2).
- Produces: no new public API.

- [ ] **Step 1: Record the deposit in `ServerDeposit`**

```csharp
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

        // Mirror into the central match-stats table for the scoreboard.
        if (MatchStatsManager.Instance != null)
            MatchStatsManager.Instance.RecordDeposit(playerNetObj.InputAuthority, points);
    }
```

- [ ] **Step 2: Compile-check**

Run the bundled-Roslyn whole-surface compile gate (or let Unity recompile). Expected: no errors.

- [ ] **Step 3: Manual verification**

Enter Play mode as Host, deposit coins at your base, confirm the existing team-score/buff behavior is unchanged and no console errors appear.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/Coin Scripts/HomeBase.cs"
git commit -m "feat(stats): record coin deposits into MatchStatsManager"
```

---

## Task 6: Flag carry-time + flag-return hooks (`Flag`)

**Files:**
- Modify: `Assets/Scripts/CTF Flag/Flag.cs`

**Interfaces:**
- Consumes: `Game.Stats.Core.FlagCarryAccumulator.Tick` (Task 1), `MatchStatsManager.Instance.RecordFlagCarrySeconds/RecordFlagReturn` (Task 2).
- Produces: `Flag.ReturnFlag(PlayerRef returner = default)` — existing callers with no argument are unaffected.

- [ ] **Step 1: Add the accumulator field and `using`**

At the top of `Assets/Scripts/CTF Flag/Flag.cs`:

```csharp
using Fusion;
using UnityEngine;
using Game.Stats.Core;
```

Add a private field near the other local references (after `markedCarrier`):

```csharp
    // Local references
    private GameObject carrierGameObject; // RENAMED from 'carrier' to avoid conflict
    private GameObject markedCarrier;      // who currently shows the flag icon on THIS peer

    // Non-networked, server-only: sub-second remainder for flag-carry-time reporting. Reset to 0
    // whenever the flag is not being carried, so a later pickup starts clean.
    private float carrySecondsRemainder;
```

- [ ] **Step 2: Accumulate carry time in `FixedUpdateNetwork`**

```csharp
    public override void FixedUpdateNetwork()
    {
        // SERVER: auto-return a dropped flag once its countdown elapses.
        if (!HasStateAuthority) return;

        // Carrier disconnect/crash: death drops the flag via PlayerStatsHandler.Die(), but a
        // player who vanishes without dying leaves the flag stuck in Carried forever (the
        // auto-return timer only arms on Drop). If the carrier's player object no longer
        // exists, drop the flag where it is so the auto-return countdown starts.
        if (CurrentState == FlagState.Carried &&
            !Runner.TryGetPlayerObject(CarrierPlayerRef, out _))
        {
            DropFlag();
        }

        if (CurrentState == FlagState.Carried && CarrierPlayerRef != PlayerRef.None)
        {
            int wholeSeconds = FlagCarryAccumulator.Tick(ref carrySecondsRemainder, Runner.DeltaTime);
            if (wholeSeconds > 0 && MatchStatsManager.Instance != null)
                MatchStatsManager.Instance.RecordFlagCarrySeconds(CarrierPlayerRef, wholeSeconds);
        }
        else
        {
            carrySecondsRemainder = 0f;
        }

        if (CurrentState == FlagState.Dropped && AutoReturnTimer.Expired(Runner))
        {
            AutoReturnTimer = default;
            ReturnFlag(); // no attribution: an auto-return is not a player action
        }
    }
```

- [ ] **Step 3: Attribute the return in `OnTriggerEnter2D`**

```csharp
            case FlagState.Dropped:
                // Own team returns a dropped flag straight home; the enemy steals it (carries it).
                if (playerTeam.Team == TeamUtil.Normalize(owningTeam))
                    ReturnFlag(playerNetworkObject.InputAuthority);
                else
                    PickupFlag(player, playerNetworkObject.InputAuthority);
                break;
```

- [ ] **Step 4: Add the optional parameter and the credit call to `ReturnFlag`**

```csharp
    /// <summary>
    /// SERVER: Return flag to home position. returner is the player who touched the dropped flag
    /// to return it; default (unattributed) for the auto-return timer and the raw RPC path, which
    /// have no verified player action behind them.
    /// </summary>
    public void ReturnFlag(PlayerRef returner = default)
    {
        if (!HasStateAuthority) return;

        // Cancel auto-return if running
        AutoReturnTimer = default;
        HasDropPosition = false;

        // Clear carrier
        if (carrierGameObject != null)
        {
            FlagCarrierMarker marker = carrierGameObject.GetComponent<FlagCarrierMarker>();
            if (marker != null)
            {
                marker.SetCarryingFlag(false);
            }
            NetworkObject carrierObj = carrierGameObject.GetComponent<NetworkObject>();
            if (carrierObj != null && AreaOfInterestRegistrar.Instance != null)
                AreaOfInterestRegistrar.Instance.RemoveAlwaysInterested(carrierObj);
            carrierGameObject = null;
        }

        // Reset state
        CurrentState = FlagState.AtHome;
        CarrierPlayerRef = PlayerRef.None;
        transform.position = HomePosition;

        if (MatchStatsManager.Instance != null)
            MatchStatsManager.Instance.RecordFlagReturn(returner);

        // Notify clients, and re-check captures for any carrier already parked in a base
        // (this flag returning home may complete a pending capture).
        if (CTFGameManager.Instance != null && HasStateAuthority)
        {
            CTFGameManager.Instance.RPC_ShowNotification($"{TeamUtil.DisplayName(TeamUtil.Normalize(owningTeam))} flag has been returned!");
            CTFGameManager.Instance.OnFlagReturnedHome();
        }
    }
```

Note: `RecordFlagReturn` on `MatchStatsManager` already guards `!returner.IsRealPlayer`, so passing `default` (the auto-return / raw-RPC path) is safe and credits nobody — no extra check needed here.

- [ ] **Step 5: Compile-check**

Run the bundled-Roslyn whole-surface compile gate (or let Unity recompile). Expected: no errors. Confirm no other caller of `ReturnFlag()` broke — `ReturnFlagRpc()` calls `ReturnFlag();` with no argument, which now resolves to `ReturnFlag(default)` and still compiles unchanged.

- [ ] **Step 6: Manual verification**

Enter Play mode as Host + one client. As the client: pick up the host's dropped-by-you flag scenario is awkward solo, so instead verify indirectly — pick up the enemy flag and carry it for several seconds, drop it, let a teammate (or yourself after respawn) return it by touching it. Confirm no console errors and the existing drop/return/notification behavior is visually unchanged. Numeric verification (carry seconds and return count actually incrementing) is deferred to Task 9's scoreboard UI.

- [ ] **Step 7: Commit**

```bash
git add "Assets/Scripts/CTF Flag/Flag.cs"
git commit -m "feat(stats): track flag carry time and attribute flag returns into MatchStatsManager"
```

---

## Task 7: Identity — team mirror, nickname handoff, spawn registration

**Files:**
- Modify: `Assets/Scripts/Player/PlayerTeamData.cs`
- Modify: `Assets/Scripts/GameNetworkManager.cs`
- Modify: `Assets/Scripts/NetworkedSpawnManager.cs`

**Interfaces:**
- Consumes: `MatchStatsManager.Instance.SetTeam/RegisterPlayer` (Task 2).
- Produces: `public static class LobbyNicknameChoices { void Set(PlayerRef, string); bool TryGet(PlayerRef, out string); void Remove(PlayerRef); void Clear(); }`, parallel to the existing `LobbyTeamChoices`.

- [ ] **Step 1: Mirror team assignment in `PlayerTeamData.SetTeam`**

```csharp
    /// <summary>Server-only: assign this player's team. Rejects None and the AI team.</summary>
    public void SetTeam(Team team)
    {
        if (!TeamUtil.IsPlayerTeam(team))
        {
            Debug.LogError($"Invalid player team assignment: {team}. Must be Team1 or Team2.");
            return;
        }

        if (!Object.HasStateAuthority)
        {
            Debug.LogWarning("Only the state authority can set team assignment!");
            return;
        }

        Team = team;

        // Apply immediately on the authority; remote clients get it via OnChangedRender.
        OnTeamChanged();

        // Mirror into the central stats table so the scoreboard can group by team regardless of
        // AoI distance. Harmless no-op on the very first call at spawn (before RegisterPlayer has
        // created the entry) -- RegisterPlayer sets the initial Team directly from the already-
        // resolved team int, so this mirror only matters for a LATER reassignment.
        if (MatchStatsManager.Instance != null)
            MatchStatsManager.Instance.SetTeam(Object.InputAuthority.PlayerId, TeamUtil.ToNumber(team));
    }
```

- [ ] **Step 2: Add `LobbyNicknameChoices`**

In `Assets/Scripts/GameNetworkManager.cs`, add the new static class after the existing `LobbyTeamChoices` class:

```csharp
/// <summary>
/// Per-player display name collected during the lobby (placeholder on join, updated on nickname
/// change), keyed by PlayerRef, parallel to LobbyTeamChoices. Survives the menu -> gameplay scene
/// load. NetworkedSpawnManager reads this to register each player's MatchStatsManager entry.
/// </summary>
public static class LobbyNicknameChoices
{
    private static readonly Dictionary<PlayerRef, string> choices = new Dictionary<PlayerRef, string>();

    public static void Set(PlayerRef player, string name) => choices[player] = name;
    public static bool TryGet(PlayerRef player, out string name) => choices.TryGetValue(player, out name);
    public static void Remove(PlayerRef player) => choices.Remove(player);
    public static void Clear() => choices.Clear();
}
```

- [ ] **Step 3: Wire it at the same four points as `LobbyTeamChoices`**

Seed a placeholder on join, in `ServerHandleJoin`:

```csharp
    private void ServerHandleJoin(PlayerRef player)
    {
        int team = serverLobby.PlayerJoined(player.PlayerId);
        LobbyTeamChoices.Set(player, team);
        LobbyNicknameChoices.Set(player, LobbyProtocol.PlaceholderName(player.PlayerId));
        if (!gameStarting) BroadcastLobby();
    }
```

Update it whenever a nickname is set, in the `NameKey` branch of `OnReliableDataReceived` (mirror unconditionally on a successful `SetNickname`, independent of the `!gameStarting` broadcast gate):

```csharp
            if (key == NameKey)
            {
                if (data.Array == null) return;
                if (!LobbyProtocol.TryDecodeNickname(data.Array, data.Offset, data.Count, out string name)) return;
                if (serverLobby.SetNickname(player.PlayerId, name))
                {
                    LobbyNicknameChoices.Set(player, name);
                    if (!gameStarting) BroadcastLobby();
                }
                return;
            }
```

Remove/clear it alongside `LobbyTeamChoices`, in `OnPlayerLeft`:

```csharp
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (runner.IsServer)
        {
            serverLobby.PlayerLeft(player.PlayerId);
            LobbyTeamChoices.Remove(player);
            LobbyNicknameChoices.Remove(player);
            LobbyLoadoutChoices.Remove(player);
            if (!gameStarting) BroadcastLobby();
        }
    }
```

and in `OnShutdown`:

```csharp
        LobbyTeamChoices.Clear();
        LobbyNicknameChoices.Clear();
        LobbyLoadoutChoices.Clear();
        serverLobby = new LobbyServerState();
        gameStarting = false;
```

- [ ] **Step 4: Register the roster entry at spawn**

In `Assets/Scripts/NetworkedSpawnManager.cs`, extend `TrySpawnPlayer`:

```csharp
    private void TrySpawnPlayer(PlayerRef player)
    {
        if (!Object.HasStateAuthority)
            return;

        if (spawnedPlayers.Contains(player))
            return;

        if (!Runner.ActivePlayers.Contains(player))
        {
            return;
        }

        if (!LobbyTeamChoices.TryGet(player, out int choice))
        {
            // The lobby gate normally guarantees a choice before gameplay loads; reaching here means
            // an unexpected late joiner with no recorded choice. AssignTeam auto-balances them.
            Debug.LogWarning($"⚠️ No lobby team choice for Player {player.PlayerId} - auto-balancing");
            choice = NoTeamChoice;
        }

        spawnedPlayers.Add(player);
        int team = AssignTeam(player, choice);

        Vector3 spawnPosition = GetSpawnPosition(team);
        SpawnPlayer(Runner, player, spawnPosition, team);

        if (MatchStatsManager.Instance != null)
        {
            if (!LobbyNicknameChoices.TryGet(player, out string name) || string.IsNullOrEmpty(name))
                name = LobbyProtocol.PlaceholderName(player.PlayerId);
            MatchStatsManager.Instance.RegisterPlayer(player.PlayerId, team, name);
        }
    }
```

- [ ] **Step 5: Compile-check**

Run the bundled-Roslyn whole-surface compile gate (or let Unity recompile). Expected: no errors.

- [ ] **Step 6: Manual verification**

Enter Play mode as Host + one client. Set a nickname in the lobby before starting the match (via the existing lobby nickname field), start the match, and confirm no console errors. Full visual confirmation that the name reaches the scoreboard is deferred to Task 9.

- [ ] **Step 7: Commit**

```bash
git add "Assets/Scripts/Player/PlayerTeamData.cs" "Assets/Scripts/GameNetworkManager.cs" "Assets/Scripts/NetworkedSpawnManager.cs"
git commit -m "feat(stats): mirror team + nickname into MatchStatsManager at spawn"
```

---

## Task 8: Pure scoreboard row sort (`Game.Hud.Core.ScoreboardSort`)

**Files:**
- Create: `Assets/Scripts/Hud/Core/ScoreboardSort.cs` (+ `.meta`)
- Test: `Assets/Tests/EditMode/Hud/ScoreboardSortTests.cs` (+ `.meta`)

**Interfaces:**
- Consumes: nothing (engine-free leaf; does not reference `Game.Stats.Core`, only plain fields).
- Produces:
  - `struct Game.Hud.Core.ScoreboardRow { int PlayerId; int Team; string DisplayName; bool IsDead; bool IsCarryingFlag; int Kills; int Deaths; int Captures; int CoinsDeposited; int FlagCarrySeconds; int FlagReturns; float OverallScore; }`
  - `static List<ScoreboardRow> Game.Hud.Core.ScoreboardSort.SortByScoreDescending(IReadOnlyList<ScoreboardRow> rows)`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/EditMode/Hud/ScoreboardSortTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Game.Hud.Core;

public class ScoreboardSortTests
{
    private static ScoreboardRow Row(int playerId, float score) =>
        new ScoreboardRow { PlayerId = playerId, OverallScore = score };

    [Test]
    public void EmptyListSortsToEmptyList()
    {
        var result = ScoreboardSort.SortByScoreDescending(new List<ScoreboardRow>());
        Assert.AreEqual(0, result.Count);
    }

    [Test]
    public void SingleRowSortsToItself()
    {
        var result = ScoreboardSort.SortByScoreDescending(new List<ScoreboardRow> { Row(1, 42f) });
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(1, result[0].PlayerId);
    }

    [Test]
    public void HighestScoreSortsFirst()
    {
        var input = new List<ScoreboardRow> { Row(1, 10f), Row(2, 90f), Row(3, 50f) };
        var result = ScoreboardSort.SortByScoreDescending(input);
        Assert.AreEqual(new[] { 2, 3, 1 }, new[] { result[0].PlayerId, result[1].PlayerId, result[2].PlayerId });
    }

    [Test]
    public void NegativeScoresSortBelowPositiveScores()
    {
        var input = new List<ScoreboardRow> { Row(1, -30f), Row(2, 5f) };
        var result = ScoreboardSort.SortByScoreDescending(input);
        Assert.AreEqual(2, result[0].PlayerId);
        Assert.AreEqual(1, result[1].PlayerId);
    }

    [Test]
    public void TiedScoresPreserveInputOrder()
    {
        // Stable sort: ties keep the order they arrived in, so a fresh repaint doesn't jitter rows.
        var input = new List<ScoreboardRow> { Row(1, 20f), Row(2, 20f), Row(3, 20f) };
        var result = ScoreboardSort.SortByScoreDescending(input);
        Assert.AreEqual(new[] { 1, 2, 3 }, new[] { result[0].PlayerId, result[1].PlayerId, result[2].PlayerId });
    }

    [Test]
    public void InputListIsNotMutated()
    {
        var input = new List<ScoreboardRow> { Row(1, 10f), Row(2, 90f) };
        ScoreboardSort.SortByScoreDescending(input);
        Assert.AreEqual(1, input[0].PlayerId, "original list order must be untouched");
    }
}
```

Create `Assets/Tests/EditMode/Hud/ScoreboardSortTests.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 5068df3915df47bcbed28c3e7d852a07
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

Run: Test Runner ▸ EditMode ▸ Run All (or the Roslyn harness).
Expected: FAIL / compile error — `ScoreboardRow`/`ScoreboardSort` do not exist.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Hud/Core/ScoreboardSort.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace Game.Hud.Core
{
    /// <summary>
    /// One player's row on the scoreboard panel -- plain data, no Fusion types, built by
    /// ScoreboardPanel from MatchStatsManager.Entries + ScoreFormula.Compute before being handed
    /// here. Team separation happens by which list a row is placed into before sorting, not by a
    /// filter in this file.
    /// </summary>
    public struct ScoreboardRow
    {
        public int PlayerId;
        public int Team;
        public string DisplayName;
        public bool IsDead;
        public bool IsCarryingFlag;
        public int Kills;
        public int Deaths;
        public int Captures;
        public int CoinsDeposited;
        public int FlagCarrySeconds;
        public int FlagReturns;
        public float OverallScore;
    }

    /// <summary>
    /// Pure group/sort math for the scoreboard: highest Overall Score first, ties stable (input
    /// order preserved) so a repaint with unchanged scores doesn't visibly jitter rows.
    /// Engine-free (Game.Hud.Core sets noEngineReferences) so it is testable outside Unity.
    /// See docs/superpowers/specs/2026-07-29-scoreboard-killfeed-design.md, "Scoreboard UI".
    /// </summary>
    public static class ScoreboardSort
    {
        public static List<ScoreboardRow> SortByScoreDescending(IReadOnlyList<ScoreboardRow> rows)
        {
            return rows.OrderByDescending(r => r.OverallScore).ToList();
        }
    }
}
```

Create `Assets/Scripts/Hud/Core/ScoreboardSort.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 90a4b1484cda42d28b1fe8c583f3c671
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

- [ ] **Step 4: Run the tests to verify they pass**

Run: Test Runner ▸ EditMode ▸ Run All (or the Roslyn harness — `Game.Hud.Core` changed, so compile it inline per the compile-gate instructions if using the Roslyn workaround).
Expected: PASS — all 6 `ScoreboardSortTests` cases green.

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/Hud/Core/ScoreboardSort.cs" "Assets/Scripts/Hud/Core/ScoreboardSort.cs.meta" "Assets/Tests/EditMode/Hud/ScoreboardSortTests.cs" "Assets/Tests/EditMode/Hud/ScoreboardSortTests.cs.meta"
git commit -m "feat(hud): pure scoreboard row sort in Game.Hud.Core"
```

---

## Task 9: Scoreboard UI components + one-click scene builder

**Files:**
- Create: `Assets/Scripts/Hud/ScoreboardRowView.cs` (+ `.meta`)
- Create: `Assets/Scripts/Hud/ScoreboardPanel.cs` (+ `.meta`)
- Create: `Assets/Scripts/Editor/ScoreboardHudBuilder.cs` (+ `.meta`)

**Interfaces:**
- Consumes: `Game.Hud.Core.ScoreboardRow`/`ScoreboardSort` (Task 8), `Game.Stats.Core.ScoreFormula` (Task 1), `MatchStatsManager.Instance.Entries/TryGetEntry/Weights` (Task 2), `CTFGameManager.Instance.IsCarrying(PlayerRef)` (existing).
- Produces:
  - `class ScoreboardRowView : MonoBehaviour { void Paint(ScoreboardRow row); }`
  - `class ScoreboardPanel : MonoBehaviour { void SetHeld(bool held); void SetForcedVisible(bool visible); }` — consumed by Task 10's `ScoreboardInputReader` and `MatchPhaseHud`.

Not unit-testable (MonoBehaviours reading Fusion singletons) — verified by compile + manual Play mode.

- [ ] **Step 1: Write `ScoreboardRowView`**

Create `Assets/Scripts/Hud/ScoreboardRowView.cs`:

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Game.Hud.Core;

/// <summary>One row of the scoreboard panel: one player's identity, state, and stats.</summary>
public class ScoreboardRowView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI kdText;
    [SerializeField] private TextMeshProUGUI capturesText;
    [SerializeField] private TextMeshProUGUI coinsText;
    [SerializeField] private TextMeshProUGUI carryTimeText;
    [SerializeField] private TextMeshProUGUI returnsText;
    [SerializeField] private Image deadIcon;
    [SerializeField] private Image carryIcon;

    public void Paint(ScoreboardRow row)
    {
        if (nameText != null) nameText.text = row.DisplayName;
        if (scoreText != null) scoreText.text = Mathf.RoundToInt(row.OverallScore).ToString();
        if (kdText != null) kdText.text = $"{row.Kills}/{row.Deaths}";
        if (capturesText != null) capturesText.text = row.Captures.ToString();
        if (coinsText != null) coinsText.text = row.CoinsDeposited.ToString();
        if (carryTimeText != null) carryTimeText.text = FormatSeconds(row.FlagCarrySeconds);
        if (returnsText != null) returnsText.text = row.FlagReturns.ToString();
        if (deadIcon != null) deadIcon.enabled = row.IsDead;
        if (carryIcon != null) carryIcon.enabled = row.IsCarryingFlag;
    }

    private static string FormatSeconds(int totalSeconds)
    {
        int m = totalSeconds / 60;
        int s = totalSeconds % 60;
        return $"{m}:{s:00}";
    }
}
```

Create `Assets/Scripts/Hud/ScoreboardRowView.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 2c49371932e849d3bf564333a4c15deb
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

- [ ] **Step 2: Write `ScoreboardPanel`**

Create `Assets/Scripts/Hud/ScoreboardPanel.cs`:

```csharp
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using Game.Hud.Core;
using Game.Stats.Core;

/// <summary>
/// Renders every active player's stats, grouped by team and sorted by Overall Score. Shown
/// on-demand (hold Tab, wired by ScoreboardInputReader) and auto-shown during MatchPhase.PostMatch
/// (wired by MatchPhaseHud). Reads MatchStatsManager.Entries directly on the render path while
/// visible -- no per-tick simulation work, no polling while hidden.
/// See docs/superpowers/specs/2026-07-29-scoreboard-killfeed-design.md.
/// </summary>
public class ScoreboardPanel : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Transform team1RowContainer;
    [SerializeField] private Transform team2RowContainer;
    [SerializeField] private ScoreboardRowView rowTemplate;

    private readonly List<ScoreboardRowView> team1Pool = new List<ScoreboardRowView>();
    private readonly List<ScoreboardRowView> team2Pool = new List<ScoreboardRowView>();

    private bool forcedVisible; // PostMatch auto-show
    private bool heldVisible;   // Tab held

    private void Awake()
    {
        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
        SetVisible(false);
    }

    /// <summary>Local input reader calls this on the Scoreboard action's performed/canceled.</summary>
    public void SetHeld(bool held)
    {
        heldVisible = held;
        Repaint();
    }

    /// <summary>MatchPhaseHud calls this to force the board open for the whole PostMatch phase.</summary>
    public void SetForcedVisible(bool visible)
    {
        forcedVisible = visible;
        Repaint();
    }

    private void Repaint()
    {
        bool visible = heldVisible || forcedVisible;
        SetVisible(visible);
        if (visible) PaintRows();
    }

    private void SetVisible(bool visible)
    {
        if (panelRoot != null) panelRoot.SetActive(visible);
    }

    private void PaintRows()
    {
        MatchStatsManager manager = MatchStatsManager.Instance;
        if (manager == null || manager.Runner == null) return;

        var team1Rows = new List<ScoreboardRow>();
        var team2Rows = new List<ScoreboardRow>();

        foreach (PlayerRef player in manager.Runner.ActivePlayers)
        {
            if (!manager.TryGetEntry(player.PlayerId, out PlayerStatEntry entry)) continue;

            var row = new ScoreboardRow
            {
                PlayerId = player.PlayerId,
                Team = entry.Team,
                DisplayName = entry.DisplayName.Value,
                IsDead = entry.IsDead,
                IsCarryingFlag = CTFGameManager.Instance != null && CTFGameManager.Instance.IsCarrying(player),
                Kills = entry.Kills,
                Deaths = entry.Deaths,
                Captures = entry.Captures,
                CoinsDeposited = entry.CoinsDeposited,
                FlagCarrySeconds = entry.FlagCarrySeconds,
                FlagReturns = entry.FlagReturns,
                OverallScore = ScoreFormula.Compute(entry.Kills, entry.Deaths, entry.CoinsDeposited,
                    entry.FlagCarrySeconds, entry.FlagReturns, manager.Weights)
            };

            if (entry.Team == (byte)Team.Team1) team1Rows.Add(row);
            else if (entry.Team == (byte)Team.Team2) team2Rows.Add(row);
        }

        PaintTeam(ScoreboardSort.SortByScoreDescending(team1Rows), team1Pool, team1RowContainer);
        PaintTeam(ScoreboardSort.SortByScoreDescending(team2Rows), team2Pool, team2RowContainer);
    }

    private void PaintTeam(List<ScoreboardRow> rows, List<ScoreboardRowView> pool, Transform container)
    {
        if (rowTemplate == null || container == null) return;

        while (pool.Count < rows.Count)
        {
            ScoreboardRowView view = Instantiate(rowTemplate, container);
            view.gameObject.SetActive(true);
            pool.Add(view);
        }

        for (int i = 0; i < pool.Count; i++)
        {
            bool active = i < rows.Count;
            pool[i].gameObject.SetActive(active);
            if (active) pool[i].Paint(rows[i]);
        }
    }
}
```

Create `Assets/Scripts/Hud/ScoreboardPanel.cs.meta`:

```yaml
fileFormatVersion: 2
guid: d5a690a970644c75a8fb3007f7d8eb5e
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

- [ ] **Step 3: Write the one-click Editor builder**

Create `Assets/Scripts/Editor/ScoreboardHudBuilder.cs`:

```csharp
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-click builder for the ScoreboardPanel: a dim backdrop, two team columns (each a
/// VerticalLayoutGroup with a header), and one hidden row template per column with all
/// ScoreboardRowView fields wired. Wires ScoreboardPanel's serialized references via
/// SerializedObject. Safe to re-run (rebuilds only its own "ScoreboardContent" child).
/// Mirrors the MatchHudBuilder / EconomyHudBuilder editor-tool pattern.
/// </summary>
public static class ScoreboardHudBuilder
{
    private const string UndoLabel = "Build Scoreboard Panel";

    [MenuItem("Tools/Match/Build Scoreboard Panel")]
    public static void Build()
    {
        var scoreboard = Object.FindFirstObjectByType<ScoreboardPanel>(FindObjectsInactive.Include);
        if (scoreboard == null)
        {
            EditorUtility.DisplayDialog("Scoreboard HUD Builder",
                "No ScoreboardPanel found in the open scene.\n\nAdd the ScoreboardPanel component to " +
                "your HUD canvas first, then run this again.", "OK");
            return;
        }

        var canvas = scoreboard.GetComponentInParent<Canvas>(true);
        if (canvas == null) canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Scoreboard HUD Builder",
                "No Canvas found in the open scene to parent the ScoreboardPanel under.", "OK");
            return;
        }

        var so = new SerializedObject(scoreboard);
        var rootProp = so.FindProperty("panelRoot");
        Undo.RecordObject(scoreboard, UndoLabel);

        var panel = rootProp.objectReferenceValue as GameObject;
        if (panel == null)
        {
            panel = new GameObject("ScoreboardPanel", typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panel, UndoLabel);
            panel.transform.SetParent(canvas.transform, false);
        }

        var prt = panel.GetComponent<RectTransform>();
        if (prt == null) prt = Undo.AddComponent<RectTransform>(panel);
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero;
        prt.offsetMax = Vector2.zero;
        var backdrop = panel.GetComponent<Image>();
        if (backdrop == null) backdrop = Undo.AddComponent<Image>(panel);
        backdrop.color = new Color(0f, 0f, 0f, 0.75f);
        backdrop.raycastTarget = true;

        var old = panel.transform.Find("ScoreboardContent");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var content = new GameObject("ScoreboardContent", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        Undo.RegisterCreatedObjectUndo(content, UndoLabel);
        content.transform.SetParent(panel.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.5f, 0.5f);
        crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(1000f, 640f);
        crt.anchoredPosition = Vector2.zero;

        var contentLayout = content.GetComponent<HorizontalLayoutGroup>();
        contentLayout.spacing = 24f;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = true;

        Transform team1Container = MakeColumn("Team1Column", content.transform, "BLUE");
        Transform team2Container = MakeColumn("Team2Column", content.transform, "RED");

        ScoreboardRowView team1Template = MakeRowTemplate("Team1RowTemplate", team1Container);
        ScoreboardRowView team2Template = MakeRowTemplate("Team2RowTemplate", team2Container);

        rootProp.objectReferenceValue = panel;
        so.FindProperty("team1RowContainer").objectReferenceValue = team1Container;
        so.FindProperty("team2RowContainer").objectReferenceValue = team2Container;
        // ScoreboardPanel has a single rowTemplate field shared by both columns (it Instantiates
        // into whichever container it's pooling for); keep the Team1 template and discard the
        // Team2 one that was built only to keep the two columns visually symmetric while editing.
        so.FindProperty("rowTemplate").objectReferenceValue = team1Template;
        Object.DestroyImmediate(team2Template.gameObject);
        so.ApplyModifiedProperties();

        panel.SetActive(true);
        Selection.activeGameObject = panel;
        EditorSceneManager.MarkSceneDirty(scoreboard.gameObject.scene);

        Debug.Log("[Match] ScoreboardPanel built and wired (two team columns + one row template). " +
                   "Save the scene (Ctrl+S). It auto-hides at runtime.");
    }

    private static Transform MakeColumn(string name, Transform parent, string headerLabel)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var layout = go.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var header = MakeText(name + "Header", go.transform, 28, Color.white, headerLabel);
        header.fontStyle = FontStyles.Bold;

        return go.transform;
    }

    private static ScoreboardRowView MakeRowTemplate(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childForceExpandWidth = false;

        var nameText = MakeText("NameText", go.transform, 20, Color.white, "PlayerName");
        var scoreText = MakeText("ScoreText", go.transform, 20, new Color(1f, 0.86f, 0.40f), "0");
        var kdText = MakeText("KdText", go.transform, 18, Color.white, "0/0");
        var capturesText = MakeText("CapturesText", go.transform, 18, Color.white, "0");
        var coinsText = MakeText("CoinsText", go.transform, 18, Color.white, "0");
        var carryText = MakeText("CarryTimeText", go.transform, 18, Color.white, "0:00");
        var returnsText = MakeText("ReturnsText", go.transform, 18, Color.white, "0");
        var deadIcon = MakeIcon("DeadIcon", go.transform, new Color(0.6f, 0.1f, 0.1f));
        var carryIcon = MakeIcon("CarryIcon", go.transform, new Color(0.9f, 0.8f, 0.2f));

        var view = go.AddComponent<ScoreboardRowView>();
        var rowSo = new SerializedObject(view);
        rowSo.FindProperty("nameText").objectReferenceValue = nameText;
        rowSo.FindProperty("scoreText").objectReferenceValue = scoreText;
        rowSo.FindProperty("kdText").objectReferenceValue = kdText;
        rowSo.FindProperty("capturesText").objectReferenceValue = capturesText;
        rowSo.FindProperty("coinsText").objectReferenceValue = coinsText;
        rowSo.FindProperty("carryTimeText").objectReferenceValue = carryText;
        rowSo.FindProperty("returnsText").objectReferenceValue = returnsText;
        rowSo.FindProperty("deadIcon").objectReferenceValue = deadIcon;
        rowSo.FindProperty("carryIcon").objectReferenceValue = carryIcon;
        rowSo.ApplyModifiedProperties();

        return view;
    }

    private static TextMeshProUGUI MakeText(string name, Transform parent, int fontSize, Color color, string sample)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = 90f;
        le.preferredHeight = 28f;

        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = fontSize;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.color = color;
        t.text = sample;
        t.raycastTarget = false;
        return t;
    }

    private static Image MakeIcon(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = 20f;
        le.preferredHeight = 20f;

        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        img.enabled = false; // toggled per-row by ScoreboardRowView.Paint
        return img;
    }
}
```

Create `Assets/Scripts/Editor/ScoreboardHudBuilder.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 31195e19d2164655a02b65754293f454
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

- [ ] **Step 4: Compile-check**

Run the bundled-Roslyn whole-surface compile gate (or let Unity recompile). Expected: no errors. Note `Assets/Scripts/Editor/**` is Unity's editor-only folder convention — it must be excluded from any runtime-only compile pass (the existing `EconomyHudBuilder.cs`/`MatchHudBuilder.cs` already establish this exclusion; mirror it).

- [ ] **Step 5: Manual scene setup**

In the Unity Editor, open `Assets/Scenes/Gameplay.unity`:
1. Find the HUD canvas (same canvas `MatchPhaseHud`'s `resultsPanel` lives under).
2. Create an empty GameObject named `ScoreboardPanel` under that canvas, add the `ScoreboardPanel` component.
3. Run **Tools ▸ Match ▸ Build Scoreboard Panel**. This builds the backdrop, two team columns, and one row template, and wires `ScoreboardPanel`'s serialized fields.
4. Save the scene (Ctrl+S).

- [ ] **Step 6: Manual verification**

Enter Play mode as Host + one client (Multiplayer Play Mode). Temporarily call `scoreboardPanel.SetHeld(true)` from a debug hook, or just proceed to Task 10 (which wires real input) before doing full visual verification — note in the task log that visual confirmation is deferred to Task 10's Play mode pass.

- [ ] **Step 7: Commit**

```bash
git add "Assets/Scripts/Hud/ScoreboardRowView.cs" "Assets/Scripts/Hud/ScoreboardRowView.cs.meta" "Assets/Scripts/Hud/ScoreboardPanel.cs" "Assets/Scripts/Hud/ScoreboardPanel.cs.meta" "Assets/Scripts/Editor/ScoreboardHudBuilder.cs" "Assets/Scripts/Editor/ScoreboardHudBuilder.cs.meta" "Assets/Scenes/Gameplay.unity"
git commit -m "feat(hud): scoreboard panel UI + one-click scene builder"
```

---

## Task 10: Input wiring (hold Tab) + PostMatch results-screen integration

**Files:**
- Modify: `Assets/InputSystem_Actions.inputactions`
- Create: `Assets/Scripts/Hud/ScoreboardInputReader.cs` (+ `.meta`)
- Modify: `Assets/Scripts/Hud/MatchPhaseHud.cs`

**Interfaces:**
- Consumes: `ScoreboardPanel.SetHeld`/`SetForcedVisible` (Task 9).
- Produces: no new public API beyond `ScoreboardInputReader` itself (a leaf input adapter).

- [ ] **Step 1: Add the `Scoreboard` action to the `UI` action map**

In `Assets/InputSystem_Actions.inputactions`, find the `UI` action map's `actions` array (it currently ends with the `TrackedDeviceOrientation` action, around line 626-634, immediately followed by `],` then `"bindings": [`). Insert a new action object right before that `],`:

```json
                {
                    "name": "TrackedDeviceOrientation",
                    "type": "PassThrough",
                    "id": "9caa3d8a-6b2f-4e8e-8bad-6ede561bd9be",
                    "expectedControlType": "Quaternion",
                    "processors": "",
                    "interactions": "",
                    "initialStateCheck": false
                },
                {
                    "name": "Scoreboard",
                    "type": "Button",
                    "id": "2983d768-fd74-4333-8a43-88616b9cf133",
                    "expectedControlType": "Button",
                    "processors": "",
                    "interactions": "",
                    "initialStateCheck": false
                }
            ],
```

(only the trailing `TrackedDeviceOrientation` entry and the new `Scoreboard` entry change; everything above them in the `actions` array is untouched — add a comma after `TrackedDeviceOrientation`'s closing `}` since it's no longer the last entry).

Then find the `UI` map's `bindings` array's closing entry (`TrackedDeviceOrientation`'s binding, around line 1044-1054, immediately followed by `]` then `}` then `],` then `"controlSchemes": [`). Insert a new binding object right before that `]`:

```json
                {
                    "name": "",
                    "id": "23e01e3a-f935-4948-8d8b-9bcac77714fb",
                    "path": "<XRController>/deviceRotation",
                    "interactions": "",
                    "processors": "",
                    "groups": "XR",
                    "action": "TrackedDeviceOrientation",
                    "isComposite": false,
                    "isPartOfComposite": false
                },
                {
                    "name": "",
                    "id": "10c6a264-7f02-4132-bc6e-320288e03a9a",
                    "path": "<Keyboard>/tab",
                    "interactions": "",
                    "processors": "",
                    "groups": "Keyboard&Mouse",
                    "action": "Scoreboard",
                    "isComposite": false,
                    "isPartOfComposite": false
                }
            ]
        }
    ],
```

No `Hold` interaction: leaving `interactions` empty means the action's default Button behavior applies — `performed` fires on press, `canceled` fires on release, which is exactly hold-to-view (immediate show, immediate hide), not a timed hold gesture.

- [ ] **Step 2: Write `ScoreboardInputReader`**

Create `Assets/Scripts/Hud/ScoreboardInputReader.cs`:

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Local-only: reads the UI/Scoreboard action (hold Tab) and forwards press/release to the bound
/// ScoreboardPanel. Not networked -- every client decides independently whether to show its own
/// already-replicated copy of the board. Matches the project convention that UI input reads are
/// local/non-simulation.
/// </summary>
public class ScoreboardInputReader : MonoBehaviour
{
    [SerializeField] private ScoreboardPanel panel;
    [SerializeField] private InputActionReference scoreboardAction;

    private void OnEnable()
    {
        if (scoreboardAction == null || scoreboardAction.action == null) return;
        scoreboardAction.action.performed += OnPerformed;
        scoreboardAction.action.canceled += OnCanceled;
        scoreboardAction.action.Enable();
    }

    private void OnDisable()
    {
        if (scoreboardAction == null || scoreboardAction.action == null) return;
        scoreboardAction.action.performed -= OnPerformed;
        scoreboardAction.action.canceled -= OnCanceled;
    }

    private void OnPerformed(InputAction.CallbackContext ctx)
    {
        if (panel != null) panel.SetHeld(true);
    }

    private void OnCanceled(InputAction.CallbackContext ctx)
    {
        if (panel != null) panel.SetHeld(false);
    }
}
```

Create `Assets/Scripts/Hud/ScoreboardInputReader.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 6e719f39a9254033a4eb7719de74aea0
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

- [ ] **Step 3: Force the board visible for the whole PostMatch phase**

In `Assets/Scripts/Hud/MatchPhaseHud.cs`, add a new serialized field near the other results-panel fields:

```csharp
    [Header("Results panel")]
    [SerializeField] private GameObject resultsPanel;
    [SerializeField] private TMP_Text winnerText;
    [SerializeField] private TMP_Text finalScoreText;
    [SerializeField] private TMP_Text returnCountdownText;
    [SerializeField] private Button returnToLobbyButton;
    [SerializeField] private ScoreboardPanel scoreboardPanel;
```

In `Render()`, force the board open exactly for `PostMatch` (not `Intermission` — the scene is already loading away by then):

```csharp
        bool results = phase == MatchPhase.PostMatch || phase == MatchPhase.Intermission;
        if (resultsPanel != null) resultsPanel.SetActive(results);
        if (results)
        {
            if (winnerText != null) winnerText.text = MatchResolver.WinnerLabel(bound.Winner);
            if (finalScoreText != null)
            {
                int t1 = TeamScoreManager.Instance != null ? TeamScoreManager.Instance.Team1Score : 0;
                int t2 = TeamScoreManager.Instance != null ? TeamScoreManager.Instance.Team2Score : 0;
                finalScoreText.text = $"Team 1  {t1}   —   {t2}  Team 2";
            }
            if (returnToLobbyButton != null)
                returnToLobbyButton.gameObject.SetActive(bound.LocalPlayerIsHost());
        }

        if (scoreboardPanel != null) scoreboardPanel.SetForcedVisible(phase == MatchPhase.PostMatch);
```

(only the final line is new; everything above it in `Render()` is unchanged).

- [ ] **Step 4: Compile-check**

Run the bundled-Roslyn whole-surface compile gate (or let Unity recompile). Expected: no errors.

- [ ] **Step 5: Manual scene setup**

In the Unity Editor, open `Assets/Scenes/Gameplay.unity`:
1. Add a `ScoreboardInputReader` component to the `ScoreboardPanel` GameObject (or any always-active HUD root object).
2. Assign its `Panel` field to the `ScoreboardPanel` component built in Task 9.
3. Assign its `Scoreboard Action` field: in the Project window, open `Assets/InputSystem_Actions.inputactions`, expand `UI ▸ Scoreboard`, and drag that action asset reference into the field (Unity's Input System asset editor exposes each action as a draggable sub-asset once the `.inputactions` file is saved and reimported).
4. On the `MatchPhaseHud` component (wherever it lives in the scene), assign the new `Scoreboard Panel` field to the same `ScoreboardPanel` component.
5. Save the scene (Ctrl+S).

- [ ] **Step 6: Manual verification (this is the first full end-to-end pass)**

Enter Play mode as Host + one client (Multiplayer Play Mode), both on different teams:
1. Deposit some coins, get a kill, carry and return/capture a flag on one peer.
2. Hold Tab on the other peer: confirm the board appears grouped by team (BLUE/RED), sorted by Overall Score descending within each team, showing the correct name, K/D, captures, coins, carry time (`m:ss`), returns, and a dead/alive + flag-carry indicator per row. Release Tab: board disappears.
3. Have one peer disconnect: confirm their row disappears from the other peer's board on the next hold.
4. End the match (capture or Sudden Death capture): confirm the board auto-shows during `PostMatch` without holding Tab, layered with the existing winner banner/final score/return countdown, and disappears once `Intermission`/scene load begins.
5. Confirm no per-frame console spam and no editor performance warnings while the board is held open.

- [ ] **Step 7: Commit**

```bash
git add "Assets/InputSystem_Actions.inputactions" "Assets/Scripts/Hud/ScoreboardInputReader.cs" "Assets/Scripts/Hud/ScoreboardInputReader.cs.meta" "Assets/Scripts/Hud/MatchPhaseHud.cs" "Assets/Scenes/Gameplay.unity"
git commit -m "feat(hud): hold-Tab scoreboard input and PostMatch auto-show"
```

---

## Self-Review Notes

**Spec coverage:** every section of `2026-07-29-scoreboard-killfeed-design.md` maps to a task — per-player stat model (Tasks 1–2), the six update hooks (Tasks 3–7), late-join/reset (inherent to the always-interested table + scene-reload contract, no extra task needed), scoreboard UI + input (Tasks 8–10), results-screen reuse (Task 10), and the derived-score/no-live-killfeed decisions are enforced throughout by Global Constraints. No spec section lacks a task.

**Placeholder scan:** no TBD/TODO markers; every step has complete, runnable code or an exact manual-step sequence; every `.meta` file has a real (generated, verified-unique-length) GUID.

**Type consistency, checked across tasks:** `RecordFlagCarrySeconds`/`RecordFlagReturn`/`RecordDeposit`/`RecordKill`/`RecordDeath`/`RecordCapture`/`SetTeam`/`SetDead`/`RegisterPlayer`/`TryGetEntry` signatures in Task 2 match every call site in Tasks 3, 4, 5, 6, 7, and 9 exactly (same names, same parameter order). `ScoreboardRow`/`ScoreboardSort.SortByScoreDescending` in Task 8 match `ScoreboardPanel`'s usage in Task 9 exactly. `ScoreboardRowView.Paint(ScoreboardRow)` in Task 9 matches its only caller in `ScoreboardPanel.PaintTeam`. `ScoreboardPanel.SetHeld`/`SetForcedVisible` in Task 9 match their only callers in Task 10 (`ScoreboardInputReader`, `MatchPhaseHud`) exactly, including the serialized-field name `scoreboardPanel` that `ScoreboardHudBuilder`/manual wiring and `MatchPhaseHud`'s `SerializedObject` lookups depend on.
