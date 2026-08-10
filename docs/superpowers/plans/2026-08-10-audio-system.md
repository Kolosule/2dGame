# Audio System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the game a real audio system — one mixer-routed, voice-limited, volume-controlled playback path that replaces all fifteen scattered `PlayClipAtPoint`/`PlayOneShot` call sites, subscribes to the gameplay events that already exist, and makes the four already-shipped volume sliders audible.

**Architecture:** Three layers. An engine-free `Game.Audio.Core` assembly holds the only logic worth testing (`SoundDedupe`, `VoiceBudget`, `MusicState`) plus the `AudioCueId`/`AudioBus`/`MusicTrackId` enums. A `Game.Audio.Assets` assembly holds the ScriptableObjects (`SoundCue`, `SoundBank`, `AudioConfig`) so EditMode tests can assert bank completeness. The default assembly holds `AudioManager` (self-bootstrapping via `[RuntimeInitializeOnLoadMethod]`, no scene wiring), the `Audio` static facade, and `MusicDirector`. Gameplay code names an event; it never owns a clip, an `AudioSource`, or a mixer group.

**Tech Stack:** Unity 6.3 (6000.3.0f1), `UnityEngine.Audio` (`AudioMixer` + snapshots), Photon Fusion 2, NUnit (EditMode tests), C#.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-29-audio-system-design.md`. All 24 decisions apply. Deviations found during planning are called out below and flagged in the task that implements them.
- **Planning-time correction — assembly layout.** The spec's file layout puts `SoundCue`/`SoundBank`/`AudioConfig` under `Assets/Scripts/Audio/` (the default assembly, `Assembly-CSharp`). That makes the spec's asset-integrity tests **impossible**: an `.asmdef` cannot reference `Assembly-CSharp`, and every one of this project's eleven EditMode test asmdefs references only a `Game.*.Core` assembly (verified by reading all of them). Corrected layout: the three ScriptableObject types move into a second new assembly, `Game.Audio.Assets` (engine references ON, no Fusion), which `Game.Audio.Assets.Tests` can reference. `AudioManager`, `Audio`, and `MusicDirector` stay in the default assembly because they need `SettingsService`, `MatchManager`, and Fusion types. Behavior is unchanged; only the assembly boundary moves.
- **Planning-time correction — `int`, not `byte`, at the engine-free boundary.** The spec says `MusicState.Resolve(MatchPhase, byte winner, byte localTeam)`. This project's established convention for engine-free team/winner codes is plain `int` — see `Assets/Scripts/Hud/Core/ScoreboardSort.cs:15` (`public int Team;`) and `Assets/Scripts/Match/Core/MatchResolver.cs:5` (int winner codes). Use `int`; `MatchManager.Winner` (a `byte`) widens implicitly at the call site. Team-number convention is `TeamUtil.ToNumber`: 0 = `Team.None`, 1 = `Team1`, 2 = `Team2`, 3 = `Team3AI`.
- **Planning-time correction — manual 2D spatialization.** Spec decision 3 is implemented with `spatialBlend = 0` on every voice, computing attenuation and `panStereo` from the orthographic camera directly, rather than using Unity's 3D panner with `AudioRolloffMode.Linear`. Same observable behavior (linear rolloff to silence just past the camera edge, pan clamped to ±0.7), but it does not depend on an `AudioListener` being correctly placed at a correct z-depth — something this project has never configured, and a silent-audio failure mode if it is wrong.
- **Planning-time correction — `Projectile.RPC_Impact` is conditional today.** `Projectile.Hit()` (`Assets/Scripts/Player/Projectile.cs:153`) calls `RPC_Impact` **only when `impactEffect != null`**. The impact sound would therefore never fire on a projectile prefab with no impact VFX assigned. Task 8 makes the RPC call unconditional and moves the null-guard inside the RPC body, where it already exists for the VFX.
- **Planning-time correction — the coin double-fire is narrower than the spec states.** `PlayerInventory.RPC_OnCoinAdded` already guards on `HasInputAuthority` (`Assets/Scripts/Coin Scripts/PlayerInventory.cs:111`), so today only the **collector** hears two sounds; observers correctly hear one. The spec's role split (world cue vs. self chime) is still the right fix and still lands — it just resolves a collector-only double, not a global one.
- **No new `[Networked]` state, no new RPCs, no change to any existing RPC's `RpcSources`/`RpcTargets`, no change to any authority check.** Audio only ever *adds a call* inside an existing callback. If a task appears to require a new RPC, stop and re-read the spec — it does not. Adding an `OnChangedRender` callback to an already-`[Networked]` property is permitted: it changes nothing on the wire.
- **Play cues from render callbacks, never from server-only methods.** On a dedicated server, code inside `EnterPhase`, `Die()`, or any `HasStateAuthority` branch runs *only where there is no audio at all*, so a cue placed there is inaudible to every player. The correct hook is always the `OnChangedRender` callback of the networked value the server wrote, or an `RpcTargets.All` RPC body. This bit two of this plan's first-draft wirings; check every new call site against it.
- **Planning-time correction — three catalog cues are cut from v1.** `KillfeedEntry`, `KillConfirmSelf`, and `FlagCaptured` are removed from `AudioCueId` and from the bank. `MatchStatsManager.RecordKill` (`Assets/Scripts/Stats/MatchStatsManager.cs:142`) is server-only and there is **no per-kill broadcast to clients anywhere in the codebase** — wiring a kill sound would require a new RPC, which spec decision 11 forbids. `FlagCaptured` is redundant: a capture routes through `MatchManager.ReportCapture` → `EnterPhase(PostMatch)`, which already fires `MatchEnd` on every peer. Removing the enum values (rather than leaving unplayable entries) is what keeps the bank-completeness test meaningful. Catalog total for v1: **49 cues + 5 beds**.
- **Planning-time correction — no `PlayLoop`/`AudioHandle`.** The spec's facade includes `Audio.PlayLoop`/`StopLoop` returning an `AudioHandle`. The only looping sound in the catalog is the arena ambient bed, which `MusicDirector` owns on its own dedicated source. A general looping API with no second caller is speculative surface; it is dropped. Add it when a second looping cue actually exists.
- **Never call `AudioSource.PlayClipAtPoint` or `AudioSource.PlayOneShot`.** After Task 10, `grep -rn "PlayClipAtPoint\|PlayOneShot" Assets/Scripts` must return zero hits. This is verified in Task 13.
- Every `Audio.*` call is safe to make unconditionally: the facade null-guards on `AudioManager.Instance`, which is never created on the dedicated server (`!SettingsService.HasDisplay`) or before boot. Call sites must **not** add their own `HasDisplay` checks.
- Exposed mixer parameter names are fixed by an existing shipped contract and must match exactly: `MasterVolume`, `MusicVolume`, `SfxVolume`, `UiVolume` (`Assets/Scripts/Settings/SettingsService.cs:26-29`). **No snapshot may animate any of these four** (spec decision 7) — snapshots move child-group volumes only.
- Editor-closed test command (do **NOT** add `-nographics` — it kills the run silently on this machine):
  ```
  "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log
  ```
  Trust `Test run completed` in `r.log` over the shell's exit code. An `[Licensing::Module] Error: Access token is unavailable` line is a red herring — it appears in successful runs too.
- `.meta` files for new scripts/asmdefs are generated by Unity on next editor focus. If a `.meta` does not exist at commit time, commit the `.cs`/`.asmdef` alone and pick the `.meta` up in the next task's commit. Do **not** hand-author `.meta` files.
- Asset authoring (the `AudioMixer`, its snapshots, `AudioConfig.asset`, `SoundBank.asset`) is **Unity Editor work** (Task 12), never hand-authored YAML.

## Setup

- [ ] Before Task 1: create a new branch off `main`:
  ```bash
  git checkout main && git pull && git checkout -b feat/audio-system
  ```

---

### Task 1: `Game.Audio.Core` assembly, enums, and `SoundDedupe`

**Files:**
- Create: `Assets/Scripts/Audio/Core/Game.Audio.Core.asmdef`
- Create: `Assets/Scripts/Audio/Core/AudioBus.cs`
- Create: `Assets/Scripts/Audio/Core/AudioCueId.cs`
- Create: `Assets/Scripts/Audio/Core/MusicTrackId.cs`
- Create: `Assets/Scripts/Audio/Core/SoundDedupe.cs`
- Create: `Assets/Tests/EditMode/Audio/Game.Audio.Core.Tests.asmdef`
- Test: `Assets/Tests/EditMode/Audio/SoundDedupeTests.cs`

**Interfaces:**
- Produces: `Game.Audio.Core.AudioBus` (enum: `Combat`, `World`, `Enemy`, `Ambient`, `Ui`, `Music`), `Game.Audio.Core.AudioCueId` (52 values), `Game.Audio.Core.MusicTrackId` (5 beds + `None`), `Game.Audio.Core.MixerSnapshotId` (4 values), and `Game.Audio.Core.SoundDedupe` with instance method `bool ShouldPlay(int cueId, float now, float window)` and `void Clear()`. Consumed by Tasks 2–6.

- [ ] **Step 1: Create the runtime assembly definition**

Create `Assets/Scripts/Audio/Core/Game.Audio.Core.asmdef`:

```json
{
    "name": "Game.Audio.Core",
    "rootNamespace": "Game.Audio.Core",
    "references": [ "Game.Match.Core" ],
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

`Game.Match.Core` is referenced for `MatchPhase` (used by `MusicState` in Task 3). It is itself engine-free (`noEngineReferences: true`, `references: []`), so this assembly stays engine-free too.

- [ ] **Step 2: Create the enums**

Create `Assets/Scripts/Audio/Core/AudioBus.cs`:

```csharp
namespace Game.Audio.Core
{
    /// <summary>
    /// Mixer destination for a cue. Maps 1:1 onto AudioMixerGroup names in the mixer asset.
    /// Combat/World/Enemy/Ambient are CHILD groups of SFX -- they exist for mix balance and as
    /// snapshot ducking targets, and are never exposed to players. Only Master/Music/SFX/UI carry
    /// exposed parameters, and those names are fixed by SettingsService's shipped contract.
    /// </summary>
    public enum AudioBus : byte
    {
        Combat = 0,
        World = 1,
        Enemy = 2,
        Ambient = 3,
        Ui = 4,
        Music = 5,
    }
}
```

Create `Assets/Scripts/Audio/Core/AudioCueId.cs`:

```csharp
namespace Game.Audio.Core
{
    /// <summary>
    /// Every sound the game can play, named by the EVENT rather than by the asset. Gameplay code
    /// references these; it never holds an AudioClip. Values are explicit so a reordering of this
    /// file cannot silently remap an authored SoundBank entry.
    ///
    /// Adding a value here without adding a matching SoundBank entry FAILS the bank-completeness
    /// EditMode test (see Assets/Tests/EditMode/Audio/SoundBankIntegrityTests.cs) -- that is the
    /// point: this project's dominant failure mode is an unassigned reference that fails silently.
    /// </summary>
    public enum AudioCueId
    {
        None = 0,

        // --- Combat (Combat bus) ---
        MeleeSwing = 100,
        MeleeSwingHeavy = 101,
        HitConfirm = 102,
        HitConfirmHeavy = 103,
        TookDamage = 104,
        ProjectileFire = 105,
        ProjectileImpact = 106,
        PlayerDeath = 107,
        PlayerRespawn = 108,

        // --- Movement (World bus) ---
        Jump = 200,
        Land = 201,
        LandHeavy = 202,
        Dash = 203,
        WallOrLedgeScuff = 204,

        // --- Coins and economy ---
        CoinPickupWorld = 300,
        CoinPickupSelf = 301,
        DepositWorld = 302,
        DepositSelf = 303,
        ScoreTick = 304,

        // --- Flags ---
        FlagTaken = 400,
        FlagDropped = 401,
        FlagReturned = 402,
        FlagPickupSelf = 403,
        AlertOwnFlagTaken = 404,
        // 405 (FlagCaptured) intentionally unused — a capture already fires MatchEnd via
        // MatchPhase.PostMatch on every peer. See Global Constraints.

        // --- Buffs ---
        BuffTierUp = 500,
        TeamBuffUnlocked = 501,
        StealthEnter = 502,
        StealthExit = 503,

        // --- Enemies (Enemy bus) ---
        EnemyTelegraph = 600,
        EnemyAttack = 601,
        EnemyHurt = 602,
        EnemyDeath = 603,
        EnemySpawn = 604,

        // --- Match and stingers ---
        CountdownTick = 700,
        CountdownGo = 701,
        MatchStart = 702,
        SuddenDeathAlert = 703,
        MatchEnd = 704,
        VictoryStinger = 705,
        DefeatStinger = 706,
        DrawStinger = 707,

        // --- UI (Ui bus, always flat, always local) ---
        UiHover = 800,
        UiClick = 801,
        UiBack = 802,
        UiToggle = 803,
        UiSliderTick = 804,
        PanelOpen = 805,
        PanelClose = 806,
        ToastNotification = 807,
        // 808/809 (KillfeedEntry, KillConfirmSelf) intentionally unused — there is no per-kill
        // broadcast to clients, and adding one would need a new RPC. See Global Constraints.
    }
}
```

Create `Assets/Scripts/Audio/Core/MusicTrackId.cs`:

```csharp
namespace Game.Audio.Core
{
    /// <summary>Looping beds owned by MusicDirector. One-shot stingers are AudioCueId values on
    /// the Music bus, not entries here -- they are fired and forgotten, never crossfaded.</summary>
    public enum MusicTrackId : byte
    {
        None = 0,
        MenuLoop = 1,
        LobbyLoop = 2,
        GameplayLoop = 3,
        SuddenDeathLoop = 4,
        ArenaAmbientBed = 5,
    }

    /// <summary>Mixer snapshots. None of these may animate MasterVolume/MusicVolume/SfxVolume/
    /// UiVolume -- those are the player's, and a snapshot transition would stomp them.</summary>
    public enum MixerSnapshotId : byte
    {
        Default = 0,
        Menu = 1,
        SuddenDeath = 2,
        Stinger = 3,
    }
}
```

- [ ] **Step 3: Create the test assembly definition**

Create `Assets/Tests/EditMode/Audio/Game.Audio.Core.Tests.asmdef`:

```json
{
    "name": "Game.Audio.Core.Tests",
    "rootNamespace": "",
    "references": [
        "Game.Audio.Core",
        "Game.Match.Core",
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

- [ ] **Step 4: Write the failing tests**

Create `Assets/Tests/EditMode/Audio/SoundDedupeTests.cs`:

```csharp
using NUnit.Framework;
using Game.Audio.Core;

public class SoundDedupeTests
{
    [Test]
    public void FirstPlay_IsAlwaysAllowed()
    {
        var dedupe = new SoundDedupe();
        Assert.IsTrue(dedupe.ShouldPlay(1, now: 0f, window: 0.06f));
    }

    [Test]
    public void ZeroWindow_NeverSuppresses()
    {
        var dedupe = new SoundDedupe();
        Assert.IsTrue(dedupe.ShouldPlay(1, now: 0f, window: 0f));
        Assert.IsTrue(dedupe.ShouldPlay(1, now: 0f, window: 0f));
        Assert.IsTrue(dedupe.ShouldPlay(1, now: 0f, window: 0f));
    }

    [Test]
    public void SecondPlay_InsideWindow_IsSuppressed()
    {
        var dedupe = new SoundDedupe();
        dedupe.ShouldPlay(1, now: 0f, window: 0.06f);
        Assert.IsFalse(dedupe.ShouldPlay(1, now: 0.05f, window: 0.06f));
    }

    [Test]
    public void SecondPlay_AtOrAfterWindow_IsAllowed()
    {
        var dedupe = new SoundDedupe();
        dedupe.ShouldPlay(1, now: 0f, window: 0.06f);
        Assert.IsTrue(dedupe.ShouldPlay(1, now: 0.06f, window: 0.06f));
    }

    [Test]
    public void DifferentCueIds_HaveIndependentWindows()
    {
        var dedupe = new SoundDedupe();
        dedupe.ShouldPlay(1, now: 0f, window: 0.06f);
        Assert.IsTrue(dedupe.ShouldPlay(2, now: 0f, window: 0.06f));
    }

    // A 20-player scrum hammers the same cue continuously. If every SUPPRESSED attempt pushed the
    // window forward, the cue would go permanently silent instead of firing once per window.
    [Test]
    public void SuppressedAttempt_DoesNotExtendTheWindow()
    {
        var dedupe = new SoundDedupe();
        dedupe.ShouldPlay(1, now: 0f, window: 0.06f);
        Assert.IsFalse(dedupe.ShouldPlay(1, now: 0.05f, window: 0.06f));
        Assert.IsTrue(dedupe.ShouldPlay(1, now: 0.06f, window: 0.06f),
            "The denied attempt at 0.05 must not have moved the window to 0.11.");
    }

    [Test]
    public void Clear_ResetsAllWindows()
    {
        var dedupe = new SoundDedupe();
        dedupe.ShouldPlay(1, now: 0f, window: 0.06f);
        dedupe.Clear();
        Assert.IsTrue(dedupe.ShouldPlay(1, now: 0.01f, window: 0.06f));
    }
}
```

- [ ] **Step 5: Run tests to verify they fail (compile error — `SoundDedupe` doesn't exist yet)**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testFilter SoundDedupeTests -testResults r.xml -logFile r.log
```
Expected: `r.log` shows a compile error naming `SoundDedupe` (type not found). If the Unity editor holds the project lock (the command hangs or reports a lock), skip to Step 6 and verify with Step 7 once the editor is free.

- [ ] **Step 6: Write the implementation**

Create `Assets/Scripts/Audio/Core/SoundDedupe.cs`:

```csharp
using System.Collections.Generic;

namespace Game.Audio.Core
{
    /// <summary>
    /// Gate 2 of 3 in the playback path (cull -> DEDUPE -> budget). Suppresses a cue that already
    /// played within its window.
    ///
    /// Keyed by cue id and NOT by instigator, deliberately: twenty players landing hits in the same
    /// frame must produce ONE impact sound, not twenty overlapping copies. That is a correctness
    /// requirement at this game's player count, not a polish item.
    ///
    /// A SUPPRESSED attempt does not move the window. If it did, continuous fire would push the
    /// window forward forever and the cue would go permanently silent.
    /// </summary>
    public sealed class SoundDedupe
    {
        private readonly Dictionary<int, float> lastPlayTime = new Dictionary<int, float>();

        /// <summary>True if the cue may play now; records the play time as a side effect when it
        /// returns true. A window of 0 or less disables dedupe for that cue.</summary>
        public bool ShouldPlay(int cueId, float now, float window)
        {
            if (window <= 0f) return true;

            if (lastPlayTime.TryGetValue(cueId, out float last) && now - last < window)
                return false;

            lastPlayTime[cueId] = now;
            return true;
        }

        public void Clear() => lastPlayTime.Clear();
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testFilter SoundDedupeTests -testResults r.xml -logFile r.log
```
Expected: `r.log` contains `Test run completed`; `r.xml` shows all 7 `SoundDedupeTests` cases `result="Passed"`.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Audio Assets/Tests/EditMode/Audio
git commit -m "feat(audio): add Game.Audio.Core assembly, cue enums, and SoundDedupe"
```

---

### Task 2: `VoiceBudget`

**Files:**
- Create: `Assets/Scripts/Audio/Core/VoiceBudget.cs`
- Test: `Assets/Tests/EditMode/Audio/VoiceBudgetTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks (pure, self-contained).
- Produces: `Game.Audio.Core.VoiceBudget` with constructor `VoiceBudget(int capacity)`, `int Capacity { get; }`, `int TryAcquire(int cueId, int priority, int maxConcurrent, float now)` returning a slot index or `-1`, `void Release(int slot)`, `bool IsActive(int slot)`, `void ReleaseAll()`. Consumed by `AudioManager` (Task 5).

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/Audio/VoiceBudgetTests.cs`:

```csharp
using NUnit.Framework;
using Game.Audio.Core;

public class VoiceBudgetTests
{
    private const int AnyCue = 1;
    private const int OtherCue = 2;

    [Test]
    public void AcquireUnderCapacity_ReturnsDistinctSlots()
    {
        var budget = new VoiceBudget(3);
        int a = budget.TryAcquire(AnyCue, priority: 10, maxConcurrent: 0, now: 0f);
        int b = budget.TryAcquire(OtherCue, priority: 10, maxConcurrent: 0, now: 1f);
        Assert.GreaterOrEqual(a, 0);
        Assert.GreaterOrEqual(b, 0);
        Assert.AreNotEqual(a, b);
    }

    [Test]
    public void Release_FreesTheSlotForReuse()
    {
        var budget = new VoiceBudget(1);
        int a = budget.TryAcquire(AnyCue, priority: 10, maxConcurrent: 0, now: 0f);
        Assert.IsTrue(budget.IsActive(a));
        budget.Release(a);
        Assert.IsFalse(budget.IsActive(a));
        Assert.AreEqual(a, budget.TryAcquire(OtherCue, priority: 10, maxConcurrent: 0, now: 1f));
    }

    [Test]
    public void FullPool_StealsTheLowestPriorityVoice()
    {
        var budget = new VoiceBudget(2);
        int low = budget.TryAcquire(AnyCue, priority: 1, maxConcurrent: 0, now: 0f);
        budget.TryAcquire(OtherCue, priority: 90, maxConcurrent: 0, now: 1f);
        int stolen = budget.TryAcquire(3, priority: 50, maxConcurrent: 0, now: 2f);
        Assert.AreEqual(low, stolen, "The priority-1 voice should be the victim, not the priority-90 one.");
    }

    [Test]
    public void FullPool_AllVoicesHigherPriority_DropsTheIncomingCue()
    {
        var budget = new VoiceBudget(2);
        budget.TryAcquire(AnyCue, priority: 90, maxConcurrent: 0, now: 0f);
        budget.TryAcquire(OtherCue, priority: 90, maxConcurrent: 0, now: 1f);
        Assert.AreEqual(-1, budget.TryAcquire(3, priority: 10, maxConcurrent: 0, now: 2f));
    }

    [Test]
    public void FullPool_EqualPriority_StealsTheOldest()
    {
        var budget = new VoiceBudget(2);
        int oldest = budget.TryAcquire(AnyCue, priority: 50, maxConcurrent: 0, now: 0f);
        budget.TryAcquire(OtherCue, priority: 50, maxConcurrent: 0, now: 5f);
        Assert.AreEqual(oldest, budget.TryAcquire(3, priority: 50, maxConcurrent: 0, now: 9f));
    }

    // maxConcurrent = 1 is what keeps a slider drag from machine-gunning UiSliderTick.
    [Test]
    public void MaxConcurrentOne_ReusesTheSameSlotForTheSameCue()
    {
        var budget = new VoiceBudget(8);
        int first = budget.TryAcquire(AnyCue, priority: 50, maxConcurrent: 1, now: 0f);
        int second = budget.TryAcquire(AnyCue, priority: 50, maxConcurrent: 1, now: 1f);
        Assert.AreEqual(first, second);
    }

    [Test]
    public void MaxConcurrent_DoesNotConstrainADifferentCue()
    {
        var budget = new VoiceBudget(8);
        int first = budget.TryAcquire(AnyCue, priority: 50, maxConcurrent: 1, now: 0f);
        int other = budget.TryAcquire(OtherCue, priority: 50, maxConcurrent: 1, now: 1f);
        Assert.AreNotEqual(first, other);
    }

    [Test]
    public void MaxConcurrentZero_IsBoundedOnlyByCapacity()
    {
        var budget = new VoiceBudget(3);
        budget.TryAcquire(AnyCue, priority: 50, maxConcurrent: 0, now: 0f);
        budget.TryAcquire(AnyCue, priority: 50, maxConcurrent: 0, now: 1f);
        int third = budget.TryAcquire(AnyCue, priority: 50, maxConcurrent: 0, now: 2f);
        Assert.GreaterOrEqual(third, 0);
        Assert.AreEqual(3, budget.Capacity);
    }

    [Test]
    public void ReleaseAll_ClearsEverySlot()
    {
        var budget = new VoiceBudget(2);
        budget.TryAcquire(AnyCue, priority: 50, maxConcurrent: 0, now: 0f);
        budget.TryAcquire(OtherCue, priority: 50, maxConcurrent: 0, now: 1f);
        budget.ReleaseAll();
        Assert.IsFalse(budget.IsActive(0));
        Assert.IsFalse(budget.IsActive(1));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testFilter VoiceBudgetTests -testResults r.xml -logFile r.log
```
Expected: `r.log` shows a compile error naming `VoiceBudget`.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Audio/Core/VoiceBudget.cs`:

```csharp
namespace Game.Audio.Core
{
    /// <summary>
    /// Gate 3 of 3 in the playback path (cull -> dedupe -> BUDGET). Owns a fixed set of voice slots
    /// and decides which one an incoming cue gets, or that it gets none. Never grows, never
    /// allocates after construction: worst-case concurrent voices is a constant regardless of how
    /// many players are in the match.
    ///
    /// Slot indices map 1:1 onto the AudioSource array the Unity layer preallocates, so this type
    /// stays engine-free and fully unit-testable.
    /// </summary>
    public sealed class VoiceBudget
    {
        private struct Voice
        {
            public bool Active;
            public int CueId;
            public int Priority;
            public float StartTime;
        }

        private readonly Voice[] voices;

        public VoiceBudget(int capacity)
        {
            if (capacity < 1) capacity = 1;
            voices = new Voice[capacity];
        }

        public int Capacity => voices.Length;

        public bool IsActive(int slot) => slot >= 0 && slot < voices.Length && voices[slot].Active;

        /// <summary>
        /// Returns the slot the cue should play on, or -1 if it must be dropped.
        ///
        /// Order: (1) per-cue concurrency -- once a cue is at maxConcurrent, it recycles its OWN
        /// oldest voice rather than adding another, so the newest instance is always the audible
        /// one; (2) any free slot; (3) steal the lowest-priority active voice, oldest first among
        /// ties, but only if its priority is <= the incoming cue's. Nothing steals from a
        /// higher-priority voice -- that is what keeps EnemyTelegraph and match stingers audible
        /// while a scrum is saturating the pool.
        /// </summary>
        public int TryAcquire(int cueId, int priority, int maxConcurrent, float now)
        {
            if (maxConcurrent > 0)
            {
                int sameCue = 0;
                int oldestSame = -1;
                for (int i = 0; i < voices.Length; i++)
                {
                    if (!voices[i].Active || voices[i].CueId != cueId) continue;
                    sameCue++;
                    if (oldestSame < 0 || voices[i].StartTime < voices[oldestSame].StartTime)
                        oldestSame = i;
                }

                if (sameCue >= maxConcurrent && oldestSame >= 0)
                {
                    Occupy(oldestSame, cueId, priority, now);
                    return oldestSame;
                }
            }

            for (int i = 0; i < voices.Length; i++)
            {
                if (voices[i].Active) continue;
                Occupy(i, cueId, priority, now);
                return i;
            }

            int victim = -1;
            for (int i = 0; i < voices.Length; i++)
            {
                if (voices[i].Priority > priority) continue;

                if (victim < 0
                    || voices[i].Priority < voices[victim].Priority
                    || (voices[i].Priority == voices[victim].Priority
                        && voices[i].StartTime < voices[victim].StartTime))
                {
                    victim = i;
                }
            }

            if (victim < 0) return -1;

            Occupy(victim, cueId, priority, now);
            return victim;
        }

        public void Release(int slot)
        {
            if (slot < 0 || slot >= voices.Length) return;
            voices[slot].Active = false;
        }

        public void ReleaseAll()
        {
            for (int i = 0; i < voices.Length; i++) voices[i].Active = false;
        }

        private void Occupy(int slot, int cueId, int priority, float now)
        {
            voices[slot].Active = true;
            voices[slot].CueId = cueId;
            voices[slot].Priority = priority;
            voices[slot].StartTime = now;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testFilter VoiceBudgetTests -testResults r.xml -logFile r.log
```
Expected: `Test run completed`, all 9 `VoiceBudgetTests` cases `result="Passed"`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Audio/Core/VoiceBudget.cs Assets/Tests/EditMode/Audio/VoiceBudgetTests.cs
git commit -m "feat(audio): add VoiceBudget voice-stealing logic"
```

---

### Task 3: `MusicState`

**Files:**
- Create: `Assets/Scripts/Audio/Core/MusicState.cs`
- Test: `Assets/Tests/EditMode/Audio/MusicStateTests.cs`

**Interfaces:**
- Consumes: `Game.Audio.Core.MusicTrackId`, `MixerSnapshotId`, `AudioCueId` (Task 1); `Game.Match.Core.MatchPhase` (existing, values `Warmup, Countdown, Live, PostMatch, Intermission, SuddenDeath`).
- Produces: `Game.Audio.Core.MusicPlan` (readonly struct with fields `MusicTrackId Bed`, `bool Ambient`, `AudioCueId Stinger`, `MixerSnapshotId Snapshot`) and `Game.Audio.Core.MusicState.Resolve(bool hasMatch, MatchPhase phase, int winner, int localTeam) -> MusicPlan`. Consumed by `MusicDirector` (Task 6).

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/Audio/MusicStateTests.cs`:

```csharp
using NUnit.Framework;
using Game.Audio.Core;
using Game.Match.Core;

public class MusicStateTests
{
    private const int Draw = 0;
    private const int Team1 = 1;
    private const int Team2 = 2;
    private const int TeamNone = 0;

    [Test]
    public void NoMatch_PlaysMenuLoopUnderTheMenuSnapshot()
    {
        MusicPlan plan = MusicState.Resolve(hasMatch: false, MatchPhase.Live, Draw, Team1);
        Assert.AreEqual(MusicTrackId.MenuLoop, plan.Bed);
        Assert.AreEqual(MixerSnapshotId.Menu, plan.Snapshot);
        Assert.AreEqual(AudioCueId.None, plan.Stinger);
        Assert.IsFalse(plan.Ambient);
    }

    [TestCase(MatchPhase.Warmup, MusicTrackId.LobbyLoop, MixerSnapshotId.Menu)]
    [TestCase(MatchPhase.Intermission, MusicTrackId.LobbyLoop, MixerSnapshotId.Menu)]
    [TestCase(MatchPhase.Countdown, MusicTrackId.GameplayLoop, MixerSnapshotId.Default)]
    [TestCase(MatchPhase.Live, MusicTrackId.GameplayLoop, MixerSnapshotId.Default)]
    [TestCase(MatchPhase.SuddenDeath, MusicTrackId.SuddenDeathLoop, MixerSnapshotId.SuddenDeath)]
    public void PhaseSelectsBedAndSnapshot(MatchPhase phase, MusicTrackId bed, MixerSnapshotId snapshot)
    {
        MusicPlan plan = MusicState.Resolve(hasMatch: true, phase, Draw, Team1);
        Assert.AreEqual(bed, plan.Bed);
        Assert.AreEqual(snapshot, plan.Snapshot);
    }

    [TestCase(MatchPhase.Warmup, false)]
    [TestCase(MatchPhase.Countdown, true)]
    [TestCase(MatchPhase.Live, true)]
    [TestCase(MatchPhase.SuddenDeath, true)]
    [TestCase(MatchPhase.PostMatch, false)]
    [TestCase(MatchPhase.Intermission, false)]
    public void AmbientBedRunsOnlyWhileTheArenaIsInPlay(MatchPhase phase, bool ambient)
    {
        MusicPlan plan = MusicState.Resolve(hasMatch: true, phase, Draw, Team1);
        Assert.AreEqual(ambient, plan.Ambient);
    }

    [Test]
    public void PostMatch_StopsTheBedAndDucksUnderTheStinger()
    {
        MusicPlan plan = MusicState.Resolve(hasMatch: true, MatchPhase.PostMatch, Team1, Team1);
        Assert.AreEqual(MusicTrackId.None, plan.Bed);
        Assert.AreEqual(MixerSnapshotId.Stinger, plan.Snapshot);
    }

    [TestCase(Team1, Team1, AudioCueId.VictoryStinger)]
    [TestCase(Team2, Team2, AudioCueId.VictoryStinger)]
    [TestCase(Team1, Team2, AudioCueId.DefeatStinger)]
    [TestCase(Team2, Team1, AudioCueId.DefeatStinger)]
    [TestCase(Draw, Team1, AudioCueId.DrawStinger)]
    [TestCase(Draw, Team2, AudioCueId.DrawStinger)]
    public void PostMatchStingerFollowsWinnerVsLocalTeam(int winner, int localTeam, AudioCueId stinger)
    {
        MusicPlan plan = MusicState.Resolve(hasMatch: true, MatchPhase.PostMatch, winner, localTeam);
        Assert.AreEqual(stinger, plan.Stinger);
    }

    // A spectator, or a player whose team hasn't replicated yet, must never be told they won.
    [TestCase(Team1)]
    [TestCase(Team2)]
    [TestCase(Draw)]
    public void UnassignedLocalTeam_AlwaysGetsTheDrawStinger(int winner)
    {
        MusicPlan plan = MusicState.Resolve(hasMatch: true, MatchPhase.PostMatch, winner, TeamNone);
        Assert.AreEqual(AudioCueId.DrawStinger, plan.Stinger);
    }

    [TestCase(MatchPhase.Warmup)]
    [TestCase(MatchPhase.Countdown)]
    [TestCase(MatchPhase.Live)]
    [TestCase(MatchPhase.SuddenDeath)]
    [TestCase(MatchPhase.Intermission)]
    public void NonPostMatchPhases_HaveNoStinger(MatchPhase phase)
    {
        MusicPlan plan = MusicState.Resolve(hasMatch: true, phase, Team1, Team1);
        Assert.AreEqual(AudioCueId.None, plan.Stinger);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testFilter MusicStateTests -testResults r.xml -logFile r.log
```
Expected: `r.log` shows a compile error naming `MusicState` / `MusicPlan`.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Audio/Core/MusicState.cs`:

```csharp
using Game.Match.Core;

namespace Game.Audio.Core
{
    /// <summary>What the music layer should be doing right now. A pure value -- MusicDirector
    /// diffs it against what is currently playing and only acts on the difference.</summary>
    public readonly struct MusicPlan
    {
        /// <summary>Looping music bed, or None to stop music entirely.</summary>
        public readonly MusicTrackId Bed;

        /// <summary>Whether the looping arena ambience should be running.</summary>
        public readonly bool Ambient;

        /// <summary>One-shot to fire on entering this state, or None.</summary>
        public readonly AudioCueId Stinger;

        public readonly MixerSnapshotId Snapshot;

        public MusicPlan(MusicTrackId bed, bool ambient, AudioCueId stinger, MixerSnapshotId snapshot)
        {
            Bed = bed;
            Ambient = ambient;
            Stinger = stinger;
            Snapshot = snapshot;
        }
    }

    /// <summary>
    /// Maps match state onto music state. Pure, engine-free, and fully table-tested, because this
    /// is the one place where getting it wrong is loudly wrong: telling a losing player they won.
    ///
    /// Team and winner are plain ints (TeamUtil.ToNumber convention: 0 = None/draw). This assembly
    /// cannot reference the Team enum -- Team lives in Assembly-CSharp, which no asmdef can
    /// reference -- so the caller converts at the boundary. Same convention as
    /// Game.Hud.Core.ScoreboardSort and Game.Match.Core.MatchResolver.
    /// </summary>
    public static class MusicState
    {
        public const int TeamNone = 0;
        public const int WinnerDraw = 0;

        public static MusicPlan Resolve(bool hasMatch, MatchPhase phase, int winner, int localTeam)
        {
            if (!hasMatch)
                return new MusicPlan(MusicTrackId.MenuLoop, false, AudioCueId.None, MixerSnapshotId.Menu);

            switch (phase)
            {
                case MatchPhase.Warmup:
                    return new MusicPlan(MusicTrackId.LobbyLoop, false, AudioCueId.None, MixerSnapshotId.Menu);

                case MatchPhase.Countdown:
                    return new MusicPlan(MusicTrackId.GameplayLoop, true, AudioCueId.None, MixerSnapshotId.Default);

                case MatchPhase.Live:
                    return new MusicPlan(MusicTrackId.GameplayLoop, true, AudioCueId.None, MixerSnapshotId.Default);

                case MatchPhase.SuddenDeath:
                    return new MusicPlan(MusicTrackId.SuddenDeathLoop, true, AudioCueId.None, MixerSnapshotId.SuddenDeath);

                case MatchPhase.PostMatch:
                    return new MusicPlan(MusicTrackId.None, false, ResolveStinger(winner, localTeam), MixerSnapshotId.Stinger);

                case MatchPhase.Intermission:
                    return new MusicPlan(MusicTrackId.LobbyLoop, false, AudioCueId.None, MixerSnapshotId.Menu);

                default:
                    return new MusicPlan(MusicTrackId.MenuLoop, false, AudioCueId.None, MixerSnapshotId.Menu);
            }
        }

        /// <summary>An unassigned local team (spectator, or a team that hasn't replicated) gets the
        /// neutral stinger. Fail toward neutral, never toward falsely celebratory.</summary>
        private static AudioCueId ResolveStinger(int winner, int localTeam)
        {
            if (winner == WinnerDraw) return AudioCueId.DrawStinger;
            if (localTeam == TeamNone) return AudioCueId.DrawStinger;
            return winner == localTeam ? AudioCueId.VictoryStinger : AudioCueId.DefeatStinger;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testFilter MusicStateTests -testResults r.xml -logFile r.log
```
Expected: `Test run completed`, every `MusicStateTests` case `result="Passed"`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Audio/Core/MusicState.cs Assets/Tests/EditMode/Audio/MusicStateTests.cs
git commit -m "feat(audio): add MusicState phase-to-music mapping"
```

---

### Task 4: `Game.Audio.Assets` — `SoundCue`, `SoundBank`, `AudioConfig`

**Files:**
- Create: `Assets/Scripts/Audio/Assets/Game.Audio.Assets.asmdef`
- Create: `Assets/Scripts/Audio/Assets/SoundCue.cs`
- Create: `Assets/Scripts/Audio/Assets/SoundBank.cs`
- Create: `Assets/Scripts/Audio/Assets/AudioConfig.cs`
- Create: `Assets/Tests/EditMode/Audio/Assets/Game.Audio.Assets.Tests.asmdef`
- Test: `Assets/Tests/EditMode/Audio/Assets/SoundBankTests.cs`

**Interfaces:**
- Consumes: `Game.Audio.Core.AudioCueId`, `AudioBus`, `MusicTrackId` (Task 1).
- Produces: `SoundCue` (serializable class, global namespace, fields listed below), `SoundBank : ScriptableObject` with `bool TryGet(AudioCueId id, out SoundCue cue)`, `IReadOnlyList<SoundCue> Cues`, and `void RebuildIndex()`; `AudioConfig : ScriptableObject` with public properties `Mixer`, `Bank`, `SfxVoices`, `UiVoices`, `DefaultWorldMaxDistance`, `MaxPan`, `MusicCrossfadeSeconds`, and `AudioClip GetMusicClip(MusicTrackId id)`. Consumed by `AudioManager` and `MusicDirector` (Tasks 5–6) and by the integrity tests (Task 11).

- [ ] **Step 1: Create the assembly definition**

Create `Assets/Scripts/Audio/Assets/Game.Audio.Assets.asmdef`:

```json
{
    "name": "Game.Audio.Assets",
    "rootNamespace": "",
    "references": [ "Game.Audio.Core" ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`noEngineReferences` is `false` here (unlike `Game.Audio.Core`) because these are `ScriptableObject`s holding `AudioClip` and `AudioMixer` references. It deliberately does **not** reference Fusion — nothing in this assembly is networked.

- [ ] **Step 2: Create `SoundCue.cs`**

```csharp
using System;
using Game.Audio.Core;
using UnityEngine;

/// <summary>
/// One authored sound: what clips it can use, where it routes, and the three numbers that keep it
/// from flooding a 20-player match. Everything that makes a cue behave the way it does is data, so
/// retuning the mix is an asset edit, never a code change.
/// </summary>
[Serializable]
public class SoundCue
{
    [Tooltip("The event this cue answers. Must be unique within a SoundBank.")]
    public AudioCueId id = AudioCueId.None;

    [Tooltip("One or more clips. Picked round-robin so repeated plays don't comb-filter into a " +
             "buzz -- with a single clip, rapid repeats phase against each other.")]
    public AudioClip[] variants = Array.Empty<AudioClip>();

    [Tooltip("Mixer destination. Combat/World/Enemy/Ambient are child groups of SFX.")]
    public AudioBus bus = AudioBus.World;

    [Tooltip("If true, the cue is attenuated and panned by its distance from the camera, and is " +
             "dropped outright beyond maxDistance. UI and own-action cues should be false.")]
    public bool positional = true;

    [Range(0f, 1f)]
    public float volume = 1f;

    [Tooltip("Random pitch range per play. Small values (±0.08) are enough to break up repeats.")]
    public Vector2 pitchRange = new Vector2(0.92f, 1.08f);

    [Range(0, 100)]
    [Tooltip("Voice-stealing rank. 0 is stolen first (EnemySpawn, ambience); 100 is never stolen " +
             "(EnemyTelegraph, match stingers -- the cues whose absence is a gameplay bug).")]
    public int priority = 50;

    [Tooltip("Seconds during which a repeat of this SAME cue is suppressed, regardless of who " +
             "triggered it. 0 disables. This is what collapses a 20-player scrum into one impact.")]
    public float dedupeWindow;

    [Tooltip("Maximum simultaneous voices for this cue. 0 = unlimited (still bounded by the pool).")]
    public int maxConcurrent;

    [Tooltip("Distance at which this cue is fully silent and is culled before acquiring a voice. " +
             "0 = use AudioConfig.DefaultWorldMaxDistance. Ignored when positional is false.")]
    public float maxDistance;

    public bool HasClip => variants != null && variants.Length > 0 && variants[0] != null;
}
```

- [ ] **Step 3: Create `SoundBank.cs`**

```csharp
using System.Collections.Generic;
using Game.Audio.Core;
using UnityEngine;

/// <summary>
/// The AudioCueId -> SoundCue lookup. Built once into a dictionary rather than searched linearly:
/// PlayAt runs on every replicated combat event, and a linear scan of 52 entries per hit at 20
/// players is real cost for no reason.
/// </summary>
[CreateAssetMenu(fileName = "SoundBank", menuName = "Audio/Sound Bank")]
public class SoundBank : ScriptableObject
{
    [SerializeField] private SoundCue[] cues = new SoundCue[0];

    private Dictionary<AudioCueId, SoundCue> index;

    public IReadOnlyList<SoundCue> Cues => cues;

    public bool TryGet(AudioCueId id, out SoundCue cue)
    {
        if (index == null) RebuildIndex();
        return index.TryGetValue(id, out cue);
    }

    /// <summary>Rebuilds the lookup. Called lazily on first use, and by the integrity tests after
    /// they mutate a bank in memory. A duplicate id keeps the FIRST entry and warns -- silently
    /// picking the last one would make an accidental duplicate impossible to notice.</summary>
    public void RebuildIndex()
    {
        index = new Dictionary<AudioCueId, SoundCue>(cues.Length);
        foreach (SoundCue cue in cues)
        {
            if (cue == null || cue.id == AudioCueId.None) continue;
            if (index.ContainsKey(cue.id))
            {
                Debug.LogWarning($"[Audio] SoundBank '{name}' has duplicate entries for {cue.id}; keeping the first.");
                continue;
            }
            index[cue.id] = cue;
        }
    }

    private void OnValidate() => index = null;

#if UNITY_EDITOR
    /// <summary>Editor/test-only setter so integrity tests can build a bank in memory without an
    /// authored asset. Not available in a player build.</summary>
    public void SetCuesForTests(SoundCue[] value)
    {
        cues = value ?? new SoundCue[0];
        RebuildIndex();
    }
#endif
}
```

- [ ] **Step 4: Create `AudioConfig.cs`**

```csharp
using System;
using Game.Audio.Core;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The single asset the audio system loads at boot, from Resources. This is the whole reason the
/// audio system needs no scene wiring: there is one asset, found by name, and nothing to forget to
/// drag into an inspector slot in each scene.
/// </summary>
[CreateAssetMenu(fileName = "AudioConfig", menuName = "Audio/Audio Config")]
public class AudioConfig : ScriptableObject
{
    [Serializable]
    public struct MusicEntry
    {
        public MusicTrackId id;
        public AudioClip clip;
    }

    [Header("Assets")]
    [SerializeField] private AudioMixer mixer;
    [SerializeField] private SoundBank bank;
    [SerializeField] private MusicEntry[] musicTracks = new MusicEntry[0];

    [Header("Voice pools")]
    [Tooltip("Concurrent world/combat voices. Hard cap regardless of player count.")]
    [SerializeField] private int sfxVoices = 32;

    [Tooltip("Separate pool so a menu click is never starved by a combat scrum.")]
    [SerializeField] private int uiVoices = 4;

    [Header("2D spatialization")]
    [Tooltip("Distance at which a positional cue is silent and culled. Roughly 1.3x the camera " +
             "half-width, so off-screen fights are inaudible without a separate range check.")]
    [SerializeField] private float defaultWorldMaxDistance = 14f;

    [Tooltip("Hard pan limit. Full ±1 panning is fatiguing and can make a cue inaudible on one ear.")]
    [Range(0f, 1f)]
    [SerializeField] private float maxPan = 0.7f;

    [Header("Music")]
    [SerializeField] private float musicCrossfadeSeconds = 1.5f;

    public AudioMixer Mixer => mixer;
    public SoundBank Bank => bank;
    public int SfxVoices => Mathf.Max(1, sfxVoices);
    public int UiVoices => Mathf.Max(1, uiVoices);
    public float DefaultWorldMaxDistance => Mathf.Max(0.01f, defaultWorldMaxDistance);
    public float MaxPan => maxPan;
    public float MusicCrossfadeSeconds => Mathf.Max(0f, musicCrossfadeSeconds);

    public AudioClip GetMusicClip(MusicTrackId id)
    {
        if (id == MusicTrackId.None || musicTracks == null) return null;
        foreach (MusicEntry entry in musicTracks)
            if (entry.id == id) return entry.clip;
        return null;
    }
}
```

- [ ] **Step 5: Create the test assembly definition**

Create `Assets/Tests/EditMode/Audio/Assets/Game.Audio.Assets.Tests.asmdef`:

```json
{
    "name": "Game.Audio.Assets.Tests",
    "rootNamespace": "",
    "references": [
        "Game.Audio.Assets",
        "Game.Audio.Core",
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

- [ ] **Step 6: Write the failing tests**

Create `Assets/Tests/EditMode/Audio/Assets/SoundBankTests.cs`:

```csharp
using NUnit.Framework;
using Game.Audio.Core;
using UnityEngine;

public class SoundBankTests
{
    private static SoundBank BankWith(params SoundCue[] cues)
    {
        SoundBank bank = ScriptableObject.CreateInstance<SoundBank>();
        bank.SetCuesForTests(cues);
        return bank;
    }

    private static SoundCue Cue(AudioCueId id, AudioBus bus = AudioBus.World)
        => new SoundCue { id = id, bus = bus };

    [Test]
    public void TryGet_ReturnsTheAuthoredCue()
    {
        SoundBank bank = BankWith(Cue(AudioCueId.Jump), Cue(AudioCueId.Land));

        Assert.IsTrue(bank.TryGet(AudioCueId.Land, out SoundCue cue));
        Assert.AreEqual(AudioCueId.Land, cue.id);
    }

    [Test]
    public void TryGet_UnknownCue_ReturnsFalse()
    {
        SoundBank bank = BankWith(Cue(AudioCueId.Jump));

        Assert.IsFalse(bank.TryGet(AudioCueId.EnemyDeath, out SoundCue cue));
        Assert.IsNull(cue);
    }

    [Test]
    public void NoneEntries_AreNeverIndexed()
    {
        SoundBank bank = BankWith(Cue(AudioCueId.None), Cue(AudioCueId.Jump));

        Assert.IsFalse(bank.TryGet(AudioCueId.None, out _));
        Assert.IsTrue(bank.TryGet(AudioCueId.Jump, out _));
    }

    [Test]
    public void DuplicateIds_KeepTheFirstEntry()
    {
        SoundCue first = Cue(AudioCueId.Jump, AudioBus.World);
        SoundCue second = Cue(AudioCueId.Jump, AudioBus.Combat);
        SoundBank bank = BankWith(first, second);

        LogAssert.ignoreFailingMessages = true;
        Assert.IsTrue(bank.TryGet(AudioCueId.Jump, out SoundCue cue));
        Assert.AreEqual(AudioBus.World, cue.bus);
        LogAssert.ignoreFailingMessages = false;
    }

    [Test]
    public void HasClip_IsFalseForAnEmptyOrNullVariantList()
    {
        var empty = new SoundCue { id = AudioCueId.Jump };
        var nulled = new SoundCue { id = AudioCueId.Jump, variants = new AudioClip[] { null } };

        Assert.IsFalse(empty.HasClip);
        Assert.IsFalse(nulled.HasClip);
    }
}
```

Add `using UnityEngine.TestTools;` at the top for `LogAssert` — the full using block is:

```csharp
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Game.Audio.Core;
```

- [ ] **Step 7: Run tests to verify they fail**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testFilter SoundBankTests -testResults r.xml -logFile r.log
```
Expected: compile error naming `SoundBank` before Step 2–4 land; after them, `Test run completed` with all `SoundBankTests` `result="Passed"`.

- [ ] **Step 8: Run the full suite to confirm no regressions**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log
```
Expected: `Test run completed`, no compile errors, previously-green tests still green (386 existing + the new audio tests).

- [ ] **Step 9: Commit**

```bash
git add Assets/Scripts/Audio/Assets Assets/Tests/EditMode/Audio/Assets
git commit -m "feat(audio): add SoundCue, SoundBank, and AudioConfig scriptable objects"
```

---

### Task 5: `AudioManager` bootstrap, voice pools, and the `Audio` facade

**Files:**
- Create: `Assets/Scripts/Audio/AudioManager.cs`
- Create: `Assets/Scripts/Audio/Audio.cs`

**Interfaces:**
- Consumes: `AudioConfig`, `SoundBank`, `SoundCue` (Task 4); `VoiceBudget`, `SoundDedupe`, `AudioCueId`, `AudioBus` (Tasks 1–2); `SettingsService.HasDisplay`, `SettingsService.Mixer`, `SettingsService.ApplyAudio()` (existing, `Assets/Scripts/Settings/SettingsService.cs`).
- Produces: `AudioManager.Instance` (may be null), and the static facade `Audio` with `PlayAt(AudioCueId, Vector3)`, `Play2D(AudioCueId)`, `PlayUi(AudioCueId)`. All three are no-ops when no manager exists. Consumed by every call site in Tasks 7–10, and by `MusicDirector` (Task 6) via `AudioManager.Instance`.

- [ ] **Step 1: Create `AudioManager.cs`**

```csharp
using System.Collections.Generic;
using Game.Audio.Core;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The whole audio runtime. Creates itself before the first scene loads -- there is no scene
/// object to place and no inspector reference to forget, which is deliberate: unassigned scene
/// references are this project's dominant failure mode, and audio that silently does nothing is
/// exactly the kind of failure nobody notices until playtest.
///
/// CLIENT-ONLY BY CONSTRUCTION. On a build with no graphics device (the dedicated server) this
/// never instantiates, so every Audio.* call is a null-check and a return. Call sites must not
/// add their own server guards.
///
/// Positional cues are spatialized MANUALLY (spatialBlend stays 0; attenuation and pan are
/// computed from the orthographic camera). Unity's 3D panner would need a correctly placed
/// AudioListener at a correct z-depth, which this project has never configured -- and getting
/// that wrong produces silence, not an error.
///
/// See docs/superpowers/specs/2026-07-29-audio-system-design.md.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    private AudioConfig config;
    private SoundBank bank;

    private readonly Dictionary<AudioBus, AudioMixerGroup> groups = new Dictionary<AudioBus, AudioMixerGroup>();
    private readonly Dictionary<AudioCueId, int> variantCursor = new Dictionary<AudioCueId, int>();
    private readonly HashSet<AudioCueId> warnedMissing = new HashSet<AudioCueId>();

    private readonly SoundDedupe dedupe = new SoundDedupe();

    private AudioSource[] sfxSources;
    private VoiceBudget sfxBudget;
    private AudioSource[] uiSources;
    private VoiceBudget uiBudget;

    private Camera listenerCamera;

    public AudioConfig Config => config;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        // Same client-only gate SettingsService uses. A headless server allocates nothing.
        if (!SettingsService.HasDisplay) return;
        if (Instance != null) return;

        AudioConfig cfg = Resources.Load<AudioConfig>("AudioConfig");
        if (cfg == null)
        {
            Debug.LogError("[Audio] Resources/AudioConfig.asset not found — the game will run silently.");
            return;
        }

        var go = new GameObject("AudioManager");
        DontDestroyOnLoad(go);

        AudioManager manager = go.AddComponent<AudioManager>();
        Instance = manager;
        manager.Initialize(cfg);
    }

    private void Initialize(AudioConfig cfg)
    {
        config = cfg;
        bank = cfg.Bank;

        if (bank == null)
            Debug.LogError("[Audio] AudioConfig has no SoundBank assigned — no sound effect will play.");

        CacheMixerGroups();

        sfxSources = BuildPool("SfxVoice", cfg.SfxVoices);
        sfxBudget = new VoiceBudget(cfg.SfxVoices);
        uiSources = BuildPool("UiVoice", cfg.UiVoices);
        uiBudget = new VoiceBudget(cfg.UiVoices);

        // Hand the mixer to the already-shipped settings layer. The four persisted volume sliders
        // become audible on this line and not before — see SettingsService.Mixer's doc comment.
        SettingsService.Mixer = cfg.Mixer;
        SettingsService.ApplyAudio();
    }

    private void CacheMixerGroups()
    {
        groups.Clear();
        if (config.Mixer == null)
        {
            Debug.LogError("[Audio] AudioConfig has no AudioMixer assigned — volume sliders will do nothing.");
            return;
        }

        foreach (AudioBus bus in System.Enum.GetValues(typeof(AudioBus)))
        {
            AudioMixerGroup[] matches = config.Mixer.FindMatchingGroups(bus.ToString());
            if (matches != null && matches.Length > 0) groups[bus] = matches[0];
            else Debug.LogError($"[Audio] Mixer has no group named '{bus}'. Cues on that bus will be unrouted.");
        }
    }

    private AudioSource[] BuildPool(string namePrefix, int count)
    {
        var pool = new AudioSource[count];
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"{namePrefix}_{i}");
            go.transform.SetParent(transform, false);

            AudioSource src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;   // manual 2D spatialization; see the class doc comment
            src.loop = false;
            pool[i] = src;
        }
        return pool;
    }

    /// <summary>Returns finished voices to their budgets. One O(pool) sweep per frame, no
    /// allocation — cheaper and far simpler than a callback per voice.</summary>
    private void Update()
    {
        ReleaseFinished(sfxSources, sfxBudget);
        ReleaseFinished(uiSources, uiBudget);
    }

    private static void ReleaseFinished(AudioSource[] pool, VoiceBudget budget)
    {
        for (int i = 0; i < pool.Length; i++)
            if (budget.IsActive(i) && !pool[i].isPlaying) budget.Release(i);
    }

    // ---- Playback ----

    public void PlayAt(AudioCueId id, Vector3 worldPosition)
    {
        if (!Resolve(id, out SoundCue cue)) return;

        float attenuation = 1f;
        float pan = 0f;

        if (cue.positional && !ComputeSpatial(cue, worldPosition, out attenuation, out pan))
            return;   // gate 1: culled by distance, before any voice is acquired

        Play(id, cue, sfxSources, sfxBudget, attenuation, pan);
    }

    public void Play2D(AudioCueId id)
    {
        if (!Resolve(id, out SoundCue cue)) return;
        Play(id, cue, sfxSources, sfxBudget, attenuation: 1f, pan: 0f);
    }

    public void PlayUi(AudioCueId id)
    {
        if (!Resolve(id, out SoundCue cue)) return;
        Play(id, cue, uiSources, uiBudget, attenuation: 1f, pan: 0f);
    }

    private bool Resolve(AudioCueId id, out SoundCue cue)
    {
        cue = null;
        if (bank == null || id == AudioCueId.None) return false;

        if (!bank.TryGet(id, out cue) || !cue.HasClip)
        {
            // Warn once per cue per session: a missing cue in a scrum would otherwise spam the log
            // hard enough to be its own performance problem.
            if (warnedMissing.Add(id))
                Debug.LogWarning($"[Audio] No playable SoundBank entry for {id}.");
            return false;
        }
        return true;
    }

    /// <summary>Gate 1: linear distance attenuation and clamped pan against the local camera.
    /// Returns false when the cue is at or beyond silence, so the caller drops it without touching
    /// the voice pool — an off-screen hit costs one squared-distance compare.</summary>
    private bool ComputeSpatial(SoundCue cue, Vector3 worldPosition, out float attenuation, out float pan)
    {
        attenuation = 1f;
        pan = 0f;

        Camera cam = ResolveCamera();
        if (cam == null) return true;   // no camera yet (boot/menu): play flat rather than swallow

        float max = cue.maxDistance > 0f ? cue.maxDistance : config.DefaultWorldMaxDistance;

        Vector3 camPos = cam.transform.position;
        float dx = worldPosition.x - camPos.x;
        float dy = worldPosition.y - camPos.y;
        if (dx * dx + dy * dy >= max * max) return false;

        attenuation = 1f - Mathf.Sqrt(dx * dx + dy * dy) / max;

        float halfWidth = cam.orthographic ? cam.orthographicSize * cam.aspect : max;
        if (halfWidth > 0.01f)
            pan = Mathf.Clamp(dx / halfWidth, -1f, 1f) * config.MaxPan;

        return true;
    }

    private Camera ResolveCamera()
    {
        if (listenerCamera == null) listenerCamera = Camera.main;
        return listenerCamera;
    }

    private void Play(AudioCueId id, SoundCue cue, AudioSource[] pool, VoiceBudget budget,
                      float attenuation, float pan)
    {
        // Unscaled time: hit-stop and pause must not stretch a dedupe window.
        float now = Time.unscaledTime;

        if (!dedupe.ShouldPlay((int)id, now, cue.dedupeWindow)) return;              // gate 2

        int slot = budget.TryAcquire((int)id, cue.priority, cue.maxConcurrent, now); // gate 3
        if (slot < 0) return;

        AudioSource src = pool[slot];
        src.Stop();
        src.clip = NextVariant(id, cue);
        src.outputAudioMixerGroup = ResolveGroup(cue.bus);
        src.volume = cue.volume * attenuation;
        src.pitch = Random.Range(cue.pitchRange.x, cue.pitchRange.y);
        src.panStereo = pan;
        src.spatialBlend = 0f;
        src.loop = false;
        src.Play();
    }

    /// <summary>Round-robin rather than random: random repeats the same clip back-to-back roughly
    /// 1/n of the time, which is exactly the case the variants exist to prevent.</summary>
    private AudioClip NextVariant(AudioCueId id, SoundCue cue)
    {
        if (cue.variants.Length == 1) return cue.variants[0];

        variantCursor.TryGetValue(id, out int cursor);
        AudioClip clip = cue.variants[cursor % cue.variants.Length];
        variantCursor[id] = (cursor + 1) % cue.variants.Length;
        return clip != null ? clip : cue.variants[0];
    }

    public AudioMixerGroup ResolveGroup(AudioBus bus)
        => groups.TryGetValue(bus, out AudioMixerGroup group) ? group : null;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
```

- [ ] **Step 2: Create `Audio.cs` — the static facade**

```csharp
using Game.Audio.Core;
using UnityEngine;

/// <summary>
/// The only audio surface gameplay code touches. Deliberately narrow and void-returning: a caller
/// cannot obtain, hold, or leak an AudioSource, which is what makes the voice budget enforceable
/// rather than advisory.
///
/// Every method is safe to call unconditionally — before boot, in the menu, and on the dedicated
/// server, where no AudioManager exists at all. Do NOT wrap these in null or platform checks at
/// the call site.
/// </summary>
public static class Audio
{
    /// <summary>World event at a position: attenuated and panned by distance from the camera, and
    /// culled entirely when off-screen. Use for anything another player caused.</summary>
    public static void PlayAt(AudioCueId id, Vector3 worldPosition)
        => AudioManager.Instance?.PlayAt(id, worldPosition);

    /// <summary>Flat, full-volume, centred. Use for the local player's OWN actions — they should
    /// feel immediate and should not fade as the camera drifts.</summary>
    public static void Play2D(AudioCueId id)
        => AudioManager.Instance?.Play2D(id);

    /// <summary>Flat, on the UI bus, from a pool combat can never starve. Always local-only.</summary>
    public static void PlayUi(AudioCueId id)
        => AudioManager.Instance?.PlayUi(id);
}
```

- [ ] **Step 3: Compile-check**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log
```
Expected: `Test run completed`, no compile errors. There are no new unit tests here — `AudioManager` is a thin Unity wrapper over `VoiceBudget`/`SoundDedupe`, both already covered, and it cannot be instantiated outside a running player. It is verified in Task 13 instead.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Audio/AudioManager.cs Assets/Scripts/Audio/Audio.cs
git commit -m "feat(audio): add self-bootstrapping AudioManager and Audio facade"
```

---

### Task 6: `MusicDirector`

**Files:**
- Create: `Assets/Scripts/Audio/MusicDirector.cs`
- Modify: `Assets/Scripts/Audio/AudioManager.cs` (own and tick the director)

**Interfaces:**
- Consumes: `MusicState.Resolve` / `MusicPlan` (Task 3), `AudioConfig.GetMusicClip` / `MusicCrossfadeSeconds` (Task 4), `AudioManager.ResolveGroup` and `Audio.PlayUi` (Task 5), plus existing `MatchManager.Instance`, `MatchManager.Phase`, `MatchManager.Winner`, `MatchManager.PhaseChanged`, `PlayerTeamData.Team`, `TeamUtil.ToNumber`.
- Produces: `MusicDirector` — a plain class owned by `AudioManager`, with `Initialize(AudioManager, AudioConfig)`, `Tick(float unscaledDeltaTime)`, and `Shutdown()`. No public API consumed by later tasks.

- [ ] **Step 1: Create `MusicDirector.cs`**

```csharp
using Fusion;
using Game.Audio.Core;
using Game.Match.Core;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Drives music and mixer snapshots from match state. Owned by AudioManager, not a MonoBehaviour —
/// there is exactly one, its lifetime is the manager's, and it needs no inspector surface.
///
/// It re-resolves its MatchManager reference every frame by comparing against the live singleton
/// rather than subscribing once. MatchManager is a NetworkBehaviour that spawns, despawns, and
/// respawns across scene reloads on rematch; a one-shot subscription would silently go stale the
/// first time a match restarted.
///
/// Crossfades use two ping-ponging AudioSources rather than a mixer snapshot fade: a snapshot can
/// only move one group's volume, so it cannot overlap an outgoing and an incoming track.
/// </summary>
public class MusicDirector
{
    private AudioManager manager;
    private AudioConfig config;

    private AudioSource[] bedSources;      // 2, ping-ponged for crossfades
    private int activeBed;
    private AudioSource ambientSource;

    private MusicTrackId currentBed = MusicTrackId.None;
    private MixerSnapshotId currentSnapshot = (MixerSnapshotId)255;   // force the first apply
    private bool ambientRunning;

    private float fadeTimer;
    private float fadeDuration;
    private bool fading;

    private MatchManager lastMatch;
    private MatchPhase lastPhase;
    private bool hadMatch;

    private AudioMixerSnapshot[] snapshotCache;

    public void Initialize(AudioManager owner, AudioConfig cfg)
    {
        manager = owner;
        config = cfg;

        bedSources = new AudioSource[2];
        for (int i = 0; i < 2; i++) bedSources[i] = CreateSource($"MusicBed_{i}", AudioBus.Music);
        ambientSource = CreateSource("AmbientBed", AudioBus.Ambient);

        CacheSnapshots();
        Apply(MusicState.Resolve(hasMatch: false, MatchPhase.Warmup, 0, 0), instant: true);
    }

    private AudioSource CreateSource(string sourceName, AudioBus bus)
    {
        var go = new GameObject(sourceName);
        go.transform.SetParent(manager.transform, false);

        AudioSource src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = true;
        src.spatialBlend = 0f;
        src.volume = 0f;
        src.outputAudioMixerGroup = manager.ResolveGroup(bus);
        return src;
    }

    private void CacheSnapshots()
    {
        snapshotCache = new AudioMixerSnapshot[4];
        if (config.Mixer == null) return;

        foreach (MixerSnapshotId id in System.Enum.GetValues(typeof(MixerSnapshotId)))
        {
            AudioMixerSnapshot snapshot = config.Mixer.FindSnapshot(id.ToString());
            if (snapshot == null)
                Debug.LogError($"[Audio] Mixer has no snapshot named '{id}'. Its ducking will not apply.");
            snapshotCache[(int)id] = snapshot;
        }
    }

    public void Tick(float unscaledDeltaTime)
    {
        PollMatchState();
        AdvanceCrossfade(unscaledDeltaTime);
    }

    /// <summary>Re-derives the plan whenever the match object or its phase changes. One reference
    /// compare and one enum compare per frame; nothing is allocated unless the state actually
    /// moved.</summary>
    private void PollMatchState()
    {
        MatchManager live = MatchManager.Instance;
        bool hasMatch = live != null;

        bool changed = hasMatch != hadMatch
                       || live != lastMatch
                       || (hasMatch && live.Phase != lastPhase);
        if (!changed) return;

        lastMatch = live;
        hadMatch = hasMatch;
        lastPhase = hasMatch ? live.Phase : MatchPhase.Warmup;

        int winner = hasMatch ? live.Winner : MusicState.WinnerDraw;
        Apply(MusicState.Resolve(hasMatch, lastPhase, winner, ResolveLocalTeamNumber()), instant: false);
    }

    /// <summary>The local player's team as a plain number, or 0 when it can't be resolved
    /// (spectator, pre-spawn, or a team that hasn't replicated). 0 always yields the neutral
    /// stinger — see MusicState.ResolveStinger.</summary>
    private static int ResolveLocalTeamNumber()
    {
        NetworkRunner runner = MatchManager.Instance != null ? MatchManager.Instance.Runner : null;
        if (runner == null) return MusicState.TeamNone;
        if (!runner.TryGetPlayerObject(runner.LocalPlayer, out NetworkObject localObject)) return MusicState.TeamNone;
        if (localObject == null) return MusicState.TeamNone;

        PlayerTeamData teamData = localObject.GetComponent<PlayerTeamData>();
        return teamData != null ? TeamUtil.ToNumber(teamData.Team) : MusicState.TeamNone;
    }

    private void Apply(MusicPlan plan, bool instant)
    {
        ApplySnapshot(plan.Snapshot, instant);
        ApplyBed(plan.Bed, instant);
        ApplyAmbient(plan.Ambient);

        if (plan.Stinger != AudioCueId.None) Audio.PlayUi(plan.Stinger);
    }

    private void ApplySnapshot(MixerSnapshotId id, bool instant)
    {
        if (id == currentSnapshot) return;
        currentSnapshot = id;

        AudioMixerSnapshot snapshot = snapshotCache != null ? snapshotCache[(int)id] : null;
        if (snapshot == null) return;

        snapshot.TransitionTo(instant ? 0f : SnapshotTransitionSeconds(id));
    }

    private static float SnapshotTransitionSeconds(MixerSnapshotId id)
    {
        switch (id)
        {
            case MixerSnapshotId.Stinger: return 0.2f;
            case MixerSnapshotId.SuddenDeath: return 1.5f;
            default: return 0.5f;
        }
    }

    private void ApplyBed(MusicTrackId bed, bool instant)
    {
        if (bed == currentBed) return;
        currentBed = bed;

        AudioSource outgoing = bedSources[activeBed];
        activeBed = 1 - activeBed;
        AudioSource incoming = bedSources[activeBed];

        AudioClip clip = config.GetMusicClip(bed);
        incoming.clip = clip;
        incoming.volume = 0f;

        if (clip != null) incoming.Play();
        else incoming.Stop();

        fadeDuration = instant ? 0f : config.MusicCrossfadeSeconds;
        fadeTimer = 0f;
        fading = true;

        if (fadeDuration <= 0f)
        {
            outgoing.Stop();
            outgoing.volume = 0f;
            incoming.volume = clip != null ? 1f : 0f;
            fading = false;
        }
    }

    /// <summary>Equal-power crossfade: linear volume ramps would dip audibly through the middle of
    /// the transition, because two uncorrelated tracks sum in power, not amplitude.</summary>
    private void AdvanceCrossfade(float unscaledDeltaTime)
    {
        if (!fading) return;

        fadeTimer += unscaledDeltaTime;
        float t = fadeDuration <= 0f ? 1f : Mathf.Clamp01(fadeTimer / fadeDuration);

        AudioSource incoming = bedSources[activeBed];
        AudioSource outgoing = bedSources[1 - activeBed];

        incoming.volume = Mathf.Sin(t * Mathf.PI * 0.5f);
        outgoing.volume = Mathf.Cos(t * Mathf.PI * 0.5f);

        if (t < 1f) return;

        outgoing.Stop();
        outgoing.volume = 0f;
        incoming.volume = incoming.clip != null ? 1f : 0f;
        fading = false;
    }

    private void ApplyAmbient(bool shouldRun)
    {
        if (shouldRun == ambientRunning) return;
        ambientRunning = shouldRun;

        if (!shouldRun)
        {
            ambientSource.Stop();
            return;
        }

        AudioClip clip = config.GetMusicClip(MusicTrackId.ArenaAmbientBed);
        if (clip == null) return;

        ambientSource.clip = clip;
        ambientSource.volume = 1f;
        ambientSource.Play();
    }

    public void Shutdown()
    {
        if (bedSources != null)
            foreach (AudioSource src in bedSources) if (src != null) src.Stop();
        if (ambientSource != null) ambientSource.Stop();
    }
}
```

- [ ] **Step 2: Have `AudioManager` own and tick the director**

In `Assets/Scripts/Audio/AudioManager.cs`, add the field next to the other private fields (after `private Camera listenerCamera;`):

```csharp
    private MusicDirector music;
```

At the end of `Initialize`, after the two `SettingsService` lines, append:

```csharp
        music = new MusicDirector();
        music.Initialize(this, config);
```

Replace `Update` with:

```csharp
    /// <summary>Returns finished voices to their budgets and advances music. One O(pool) sweep per
    /// frame, no allocation — cheaper and far simpler than a callback per voice.</summary>
    private void Update()
    {
        ReleaseFinished(sfxSources, sfxBudget);
        ReleaseFinished(uiSources, uiBudget);
        music?.Tick(Time.unscaledDeltaTime);
    }
```

Replace `OnDestroy` with:

```csharp
    private void OnDestroy()
    {
        music?.Shutdown();
        if (Instance == this) Instance = null;
    }
```

- [ ] **Step 3: Compile-check**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log
```
Expected: `Test run completed`, no compile errors, full suite still green.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Audio/MusicDirector.cs Assets/Scripts/Audio/AudioManager.cs
git commit -m "feat(audio): add MusicDirector phase-driven music and snapshot transitions"
```

---

### Task 7: Migrate the coin and deposit call sites

Removes four `PlayClipAtPoint` calls and four serialized `AudioClip` fields, and splits the collector's doubled sound into a world cue plus a distinct self chime.

**Files:**
- Modify: `Assets/Scripts/Coin Scripts/CoinPickup.cs:24-26` (field), `:288-297` (RPC)
- Modify: `Assets/Scripts/Coin Scripts/HomeBase.cs:25-27` (field), `:249-257` (RPC)
- Modify: `Assets/Scripts/Coin Scripts/PlayerInventory.cs:15-20` (fields), `:108-115` (RPC), `:154-161` (RPC)

**Interfaces:**
- Consumes: `Audio.PlayAt` / `Audio.PlayUi` and `Game.Audio.Core.AudioCueId` (Tasks 1, 5).

- [ ] **Step 1: `CoinPickup.cs` — remove the clip field**

Delete these three lines (`:24-26`):

```csharp
    [Header("Visual Feedback (Optional)")]
    [Tooltip("Sound to play when coin is picked up")]
    [SerializeField] private AudioClip pickupSound;
```

- [ ] **Step 2: `CoinPickup.cs` — replace the RPC body**

Change:

```csharp
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnCoinCollected(Vector3 playerPosition)
    {
        // Play pickup sound if assigned
        if (pickupSound != null)
        {
            AudioSource.PlayClipAtPoint(pickupSound, transform.position);
        }

        // You can add particle effects here
        // Example: Instantiate(pickupEffect, transform.position, Quaternion.identity);
    }
```

to:

```csharp
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnCoinCollected(Vector3 playerPosition)
    {
        // The WORLD half of a pickup: positional, heard by everyone nearby. The collector's own
        // confirmation is a separate, flat cue owned by PlayerInventory — two roles, one sound
        // each, so the collector no longer hears the same clip twice.
        Audio.PlayAt(AudioCueId.CoinPickupWorld, transform.position);

        // You can add particle effects here
        // Example: Instantiate(pickupEffect, transform.position, Quaternion.identity);
    }
```

Add `using Game.Audio.Core;` to the file's using block if it is not already present.

- [ ] **Step 3: `HomeBase.cs` — remove the clip field**

Delete these three lines (`:25-27`):

```csharp
    [Header("Audio (Optional)")]
    [Tooltip("Sound to play when coins are deposited")]
    [SerializeField] private AudioClip depositSound;
```

- [ ] **Step 4: `HomeBase.cs` — replace the sound block inside `RPC_OnDeposit`**

Change:

```csharp
        // Play deposit sound if assigned
        if (depositSound != null)
        {
            AudioSource.PlayClipAtPoint(depositSound, transform.position);
        }
```

to:

```csharp
        // World half of a deposit: positional at the base, heard by everyone nearby. The
        // depositing player's own chime is a separate flat cue in PlayerInventory.
        Audio.PlayAt(AudioCueId.DepositWorld, transform.position);
```

Add `using Game.Audio.Core;` to the file's using block if not already present.

- [ ] **Step 5: `PlayerInventory.cs` — remove both clip fields**

Delete these six lines (`:15-20`):

```csharp
    [Header("Audio (Optional)")]
    [Tooltip("Sound to play when picking up a coin")]
    [SerializeField] private AudioClip coinPickupSound;

    [Tooltip("Sound to play when depositing coins")]
    [SerializeField] private AudioClip depositSound;
```

- [ ] **Step 6: `PlayerInventory.cs` — replace both RPC bodies**

Change:

```csharp
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnCoinAdded()
    {
        if (coinPickupSound != null && HasInputAuthority)
        {
            AudioSource.PlayClipAtPoint(coinPickupSound, transform.position);
        }
    }
```

to:

```csharp
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnCoinAdded()
    {
        // SELF half of a pickup: flat, on the UI bus, only for the player who collected it.
        // CoinPickup already plays the positional world cue for everyone, including this player.
        if (HasInputAuthority) Audio.PlayUi(AudioCueId.CoinPickupSelf);
    }
```

Change:

```csharp
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnCoinsDeposited()
    {
        if (depositSound != null && HasInputAuthority)
        {
            AudioSource.PlayClipAtPoint(depositSound, transform.position);
        }
    }
```

to:

```csharp
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnCoinsDeposited()
    {
        // SELF half of a deposit. HomeBase plays the positional world cue for everyone.
        if (HasInputAuthority) Audio.PlayUi(AudioCueId.DepositSelf);
    }
```

Add `using Game.Audio.Core;` to the file's using block if not already present.

- [ ] **Step 7: Compile-check**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log
```
Expected: `Test run completed`, no compile errors, full suite green.

- [ ] **Step 8: Commit**

```bash
git add "Assets/Scripts/Coin Scripts/CoinPickup.cs" "Assets/Scripts/Coin Scripts/HomeBase.cs" "Assets/Scripts/Coin Scripts/PlayerInventory.cs"
git commit -m "refactor(audio): route coin and deposit sounds through the audio service"
```

---

### Task 8: Migrate the movement and combat call sites

**Files:**
- Modify: `Assets/Scripts/Player/PlayerAnimator.cs:64-68` (fields), `:102` (Awake resolve), `:232-241` (sfx block)
- Modify: `Assets/Scripts/Player/PlayerCombat.cs:74` (`AttackStartTick` render callback)
- Modify: `Assets/Scripts/Player/HitFeedback.cs:37-59` (`Play`)
- Modify: `Assets/Scripts/Player/Projectile.cs` (`Spawned`, `Hit`, `RPC_Impact`)
- Modify: `Assets/Scripts/Player/PlayerStatsHandler.cs` (`RPC_DisablePlayerControls`, `RPC_EnablePlayerControls`)
- Modify: `Assets/Scripts/ScriptableObjects/CombatConfig.cs:49-53` (delete dead fields)

**Interfaces:**
- Consumes: `Audio.PlayAt` / `Audio.Play2D` and `AudioCueId` (Tasks 1, 5).

- [ ] **Step 1: `PlayerAnimator.cs` — remove the SFX fields**

Delete these five lines (`:64-68`):

```csharp
    [Header("SFX (optional — null-safe)")]
    [Tooltip("Plays jump/land one-shots. Auto-resolved from this GameObject if unset.")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip jumpClip;
    [SerializeField] private AudioClip landClip;
```

And delete the auto-resolve line at `:102`:

```csharp
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
```

- [ ] **Step 2: `PlayerAnimator.cs` — replace the SFX block**

Change:

```csharp
        if (audioSource != null)
        {
            if (state == AnimState.Jump && jumpClip != null)
                audioSource.PlayOneShot(jumpClip);

            bool wasAirborne = lastRenderedState == AnimState.Jump || lastRenderedState == AnimState.Fall;
            bool nowGrounded = state == AnimState.Idle || state == AnimState.Walk;
            if (wasAirborne && nowGrounded && landClip != null)
                audioSource.PlayOneShot(landClip);
        }
```

to:

```csharp
        // Your own movement is flat and full-volume so it always feels immediate; everyone else's
        // is positional, so a teammate landing beside you reads as "beside you" and one across the
        // map is culled before it costs a voice. Same cue id either way — only the path differs.
        if (state == AnimState.Jump)
            PlayMovementCue(AudioCueId.Jump);

        bool wasAirborne = lastRenderedState == AnimState.Jump || lastRenderedState == AnimState.Fall;
        bool nowGrounded = state == AnimState.Idle || state == AnimState.Walk;
        if (wasAirborne && nowGrounded)
            PlayMovementCue(AudioCueId.Land);
```

Add this helper as the last method of the class, immediately before the closing brace:

```csharp
    /// <summary>Flat for the player who owns this body, positional for everyone else's copy of
    /// it. Every peer runs this method for every player object it simulates, so exactly one peer
    /// takes the flat path per player — there is no way to double-play.</summary>
    private void PlayMovementCue(AudioCueId cue)
    {
        if (HasInputAuthority) Audio.Play2D(cue);
        else Audio.PlayAt(cue, transform.position);
    }
```

Add `using Game.Audio.Core;` to the file's using block.

- [ ] **Step 3: `PlayerAnimator.cs` — the local player's dash**

Add to the same method, immediately after the `Land` block from Step 2:

```csharp
        // Dash is LOCAL-ONLY on purpose. Your own dash is the feel-critical one; a remote player's
        // is cosmetic, and gating it on a proxy-correct read of PlayerMovement.IsDashing() would
        // make the cue depend on dash state replicating, which nothing else here needs.
        bool dashing = playerMovement != null && playerMovement.IsDashing();
        if (HasInputAuthority && dashing && !wasDashingForSfx) Audio.Play2D(AudioCueId.Dash);
        wasDashingForSfx = dashing;
```

Add the two supporting members: a `playerMovement` field resolved in `Awake` (only if the class does not already hold one — check first, and reuse the existing reference if it does), and the edge flag:

```csharp
    // Render-side only: previous dash state, so the cue fires on the rising edge rather than every
    // frame of the dash. Never networked.
    private bool wasDashingForSfx;
```

- [ ] **Step 4: `PlayerCombat.cs` — the melee swing whoosh**

This is the responsiveness half of the spec's "no double-play by construction" design: the swing is predicted and local, the impact is authoritative and separate. `AttackStartTick` is already `[Networked]`, so an `OnChangedRender` callback on it fires on the predicting input authority *and* on every proxy — one edge detector per player object per peer, with no way to double-play and no new wire traffic.

Change:

```csharp
    [Networked] private int AttackStartTick { get; set; }
```

to:

```csharp
    [Networked, OnChangedRender(nameof(OnAttackStartTickChanged))] private int AttackStartTick { get; set; }
```

and add this method immediately after `BeginSwing`:

```csharp
    /// <summary>
    /// Fires the swing whoosh on every peer the moment a new swing is latched — including the
    /// predicting input authority, which is what makes your own melee feel instant. This is a
    /// DIFFERENT cue from the hit confirm in HitFeedback (which arrives later, only on the
    /// attacker, only when the swing actually connected), so a landed hit plays two distinct
    /// sounds and a whiff plays one. There is nothing to reconcile and nothing to suppress.
    ///
    /// Adding OnChangedRender to an already-[Networked] property changes nothing on the wire.
    /// </summary>
    private void OnAttackStartTickChanged()
    {
        // Tick 0 is the never-swung default; a pooled or freshly spawned player must not whoosh.
        if (AttackStartTick <= 0) return;

        AudioCueId cue = AttackIsPound ? AudioCueId.MeleeSwingHeavy : AudioCueId.MeleeSwing;
        if (HasInputAuthority) Audio.Play2D(cue);
        else Audio.PlayAt(cue, transform.position);
    }
```

Add `using Game.Audio.Core;` to `PlayerCombat.cs` if not already present (it already has `using Game.Combat.Core;`, which is a different namespace).

- [ ] **Step 5: `HitFeedback.cs` — add the attacker-only confirm cue**

Change:

```csharp
    public void Play(GameObject target, Vector2 hitPoint, int damage)
    {
        if (particleBurstPrefab != null)
        {
```

to:

```csharp
    public void Play(GameObject target, Vector2 hitPoint, int damage)
    {
        // This method only ever runs on the ATTACKER's client — both callers are
        // [Rpc(..., RpcTargets.InputAuthority)] (PlayerCombat.RPC_HitFeedback,
        // Projectile.RPC_HitFeedback), so no gating is needed here. Flat and full volume: it is
        // the single most important cue in the game and must never fade with camera drift.
        Audio.Play2D(damage >= heavyHitDamageThreshold
            ? AudioCueId.HitConfirmHeavy
            : AudioCueId.HitConfirm);

        if (particleBurstPrefab != null)
        {
```

Add the threshold field next to the existing serialized fields (after `[SerializeField] private float particleLifetime = 2f;`):

```csharp
    [Tooltip("Damage at or above which the heavier hit-confirm cue plays instead. This is a " +
             "loudness tier, NOT a crit system — the crit multiplier was removed in the " +
             "2026-08-05 damage-model change.")]
    [SerializeField] private int heavyHitDamageThreshold = 25;
```

Add `using Game.Audio.Core;` to the file's using block.

- [ ] **Step 6: `Projectile.cs` — fire cue on spawn**

In `Spawned()`, change:

```csharp
        if (HasStateAuthority && rb != null)
            rb.linearVelocity = Direction * Speed;
    }
```

to:

```csharp
        if (HasStateAuthority && rb != null)
            rb.linearVelocity = Direction * Speed;

        // Runs on every peer that has this projectile — the shooter hears it flat, everyone else
        // positional. Pooled reuse re-runs Spawned(), so a recycled projectile still fires a cue.
        if (HasInputAuthority) Audio.Play2D(AudioCueId.ProjectileFire);
        else Audio.PlayAt(AudioCueId.ProjectileFire, transform.position);
    }
```

- [ ] **Step 7: `Projectile.cs` — make `RPC_Impact` unconditional**

The impact RPC is currently only sent when a VFX prefab is assigned, so the impact **sound** would never play on a projectile with no impact VFX. Change:

```csharp
        if (hasHit) return;
        hasHit = true;
        if (impactEffect != null) RPC_Impact(transform.position);
        Runner.Despawn(Object);
```

to:

```csharp
        if (hasHit) return;
        hasHit = true;
        // Unconditional: this RPC now carries the impact SOUND as well as the optional VFX, and
        // gating the send on impactEffect would silence every projectile whose prefab has no
        // particle assigned. The VFX null-guard lives inside the RPC body, where it belongs.
        RPC_Impact(transform.position);
        Runner.Despawn(Object);
```

And change:

```csharp
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_Impact(Vector3 position)
    {
        if (impactEffect != null)
        {
            GameObject fx = Instantiate(impactEffect, position, Quaternion.identity);
            Destroy(fx, 2f);
        }
    }
```

to:

```csharp
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_Impact(Vector3 position)
    {
        Audio.PlayAt(AudioCueId.ProjectileImpact, position);

        if (impactEffect != null)
        {
            GameObject fx = Instantiate(impactEffect, position, Quaternion.identity);
            Destroy(fx, 2f);
        }
    }
```

Add `using Game.Audio.Core;` to the file's using block.

- [ ] **Step 8: `PlayerStatsHandler.cs` — death, respawn, and took-damage cues**

Change:

```csharp
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_DisablePlayerControls()
    {
        SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
```

to:

```csharp
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_DisablePlayerControls()
    {
        if (HasInputAuthority) Audio.Play2D(AudioCueId.PlayerDeath);
        else Audio.PlayAt(AudioCueId.PlayerDeath, transform.position);

        SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
```

Change:

```csharp
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_EnablePlayerControls()
    {
        SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
```

to:

```csharp
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_EnablePlayerControls()
    {
        if (HasInputAuthority) Audio.Play2D(AudioCueId.PlayerRespawn);
        else Audio.PlayAt(AudioCueId.PlayerRespawn, transform.position);

        SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
```

Then find the existing `OnHealthChanged` render callback (the method named in `[Networked, OnChangedRender(nameof(OnHealthChanged))]` at `:40`) and add the took-damage cue as its first statement:

```csharp
        // Victim-only, flat: "I am being hit" must be instantly distinguishable from
        // "I am landing hits" (HitConfirm), which is why they are separate cues on separate paths.
        // Guarded on a health DECREASE so healing and the respawn reset never trigger it.
        if (HasInputAuthority && CurrentHealth < lastRenderedHealth)
            Audio.Play2D(AudioCueId.TookDamage);
        lastRenderedHealth = CurrentHealth;
```

and add the backing field next to the other private fields of the class:

```csharp
    // Render-side only: previous health, so OnHealthChanged can tell a hit from a heal or a
    // respawn reset. Never read by simulation, never networked.
    private float lastRenderedHealth = float.MaxValue;
```

Add `using Game.Audio.Core;` to the file's using block.

- [ ] **Step 9: `CombatConfig.cs` — delete the dead audio fields**

`hitSound` is read by nothing (verified by grep); the audio system supersedes it. Delete these three lines (`:51-53`):

```csharp
    public AudioClip hitSound;
    [Range(0f, 1f)]
    public float hitSoundVolume = 0.5f;
```

leaving the `[Header("Hit Effects")]` and `hitEffectPrefab` lines in place.

- [ ] **Step 10: Compile-check**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log
```
Expected: `Test run completed`, no compile errors, full suite green. If `lastRenderedHealth`, `wasDashingForSfx`, or `playerMovement` collides with an existing member name, rename yours and keep the same semantics.

- [ ] **Step 11: Commit**

```bash
git add Assets/Scripts/Player/PlayerAnimator.cs Assets/Scripts/Player/PlayerCombat.cs Assets/Scripts/Player/HitFeedback.cs Assets/Scripts/Player/Projectile.cs Assets/Scripts/Player/PlayerStatsHandler.cs Assets/Scripts/ScriptableObjects/CombatConfig.cs
git commit -m "refactor(audio): route movement and combat sounds through the audio service"
```

---

### Task 9: Subscribe the flag, buff, score, enemy, and match cues

All additions to existing render callbacks and events. Nothing new is detected, networked, or gated.

**Files:**
- Modify: `Assets/Scripts/CTF Flag/Flag.cs` (`OnStateChanged`)
- Modify: `Assets/Scripts/Buffs/PlayerBuffs.cs` (`OnStealthChanged`, `OnBuffsChanged`)
- Modify: `Assets/Scripts/Coin Scripts/TeamScoreManager.cs` (`ScoresChanged`, `TeamBuffsChanged` raise sites)
- Modify: `Assets/Scripts/Enemy/Base/Enemy.cs` (`Die`, `RPC_TakeDamage`)
- Modify: `Assets/Scripts/Match/MatchManager.cs` (`EnterPhase`)

**Interfaces:**
- Consumes: `Audio.PlayAt` / `Audio.Play2D` / `Audio.PlayUi`, `AudioCueId` (Tasks 1, 5), and existing `TeamUtil.Normalize` / `TeamUtil.ToNumber`.

- [ ] **Step 1: `Flag.cs` — flag state cues**

Change:

```csharp
    private void OnStateChanged()
    {
        UpdateVisuals();
    }
```

to:

```csharp
    private void OnStateChanged()
    {
        UpdateVisuals();
        PlayStateCue();
    }

    /// <summary>
    /// Fires on every peer (this is an OnChangedRender callback), so no RPC is involved and no
    /// authority check is needed. Two layers: a positional cue at the flag for anyone nearby, and
    /// — when it is YOUR team's flag being taken — a flat, distance-independent alert. That second
    /// layer deliberately breaks the positional rule: it is the most match-relevant event in the
    /// game and by definition happens far away from you.
    /// </summary>
    private void PlayStateCue()
    {
        switch (CurrentState)
        {
            case FlagState.Carried:
                Audio.PlayAt(AudioCueId.FlagTaken, transform.position);
                if (LocalPlayerOwnsThisFlag()) Audio.PlayUi(AudioCueId.AlertOwnFlagTaken);
                break;

            case FlagState.Dropped:
                Audio.PlayAt(AudioCueId.FlagDropped, transform.position);
                break;

            case FlagState.AtHome:
                Audio.PlayAt(AudioCueId.FlagReturned, transform.position);
                break;
        }
    }

    /// <summary>True when this flag belongs to the local player's own team. False for spectators
    /// and for any player whose team has not replicated yet — fail toward not alerting.</summary>
    private bool LocalPlayerOwnsThisFlag()
    {
        if (Runner == null) return false;
        if (!Runner.TryGetPlayerObject(Runner.LocalPlayer, out NetworkObject localObject)) return false;
        if (localObject == null) return false;

        PlayerTeamData teamData = localObject.GetComponent<PlayerTeamData>();
        if (teamData == null || teamData.Team == Team.None) return false;

        return teamData.Team == OwningTeamEnum;
    }
```

Add `using Game.Audio.Core;` to the file's using block.

- [ ] **Step 2: `Flag.cs` — carrier-only pickup cue**

In `OnCarrierPlayerRefChanged`, change:

```csharp
        if (CarrierPlayerRef != PlayerRef.None)
        {
            if (Runner.TryGetPlayerObject(CarrierPlayerRef, out NetworkObject networkObject))
                carrierGameObject = networkObject.gameObject;
        }
```

to:

```csharp
        if (CarrierPlayerRef != PlayerRef.None)
        {
            if (Runner.TryGetPlayerObject(CarrierPlayerRef, out NetworkObject networkObject))
                carrierGameObject = networkObject.gameObject;

            // "You are now the carrier" — only on the carrier's own client.
            if (Runner != null && CarrierPlayerRef == Runner.LocalPlayer)
                Audio.PlayUi(AudioCueId.FlagPickupSelf);
        }
```

- [ ] **Step 3: `PlayerBuffs.cs` — stealth and tier-up cues**

Find the `OnStealthChanged` render callback (named at `:18`) and add, as its first statements:

```csharp
        // Flat for the stealthed player. For everyone else, a quiet shimmer on a deliberately
        // SHORT radius (SoundCue.maxDistance on the cue asset, ~0.35x the normal world radius):
        // opponents standing right next to you get counterplay, but the buff is not announced
        // across the arena. That radius is the balance lever and lives in the asset, not here.
        AudioCueId cue = IsStealthed ? AudioCueId.StealthEnter : AudioCueId.StealthExit;
        if (HasInputAuthority) Audio.Play2D(cue);
        else Audio.PlayAt(cue, transform.position);
```

Find the `OnBuffsChanged` render callback (named at `:17`) and add, as its first statements:

```csharp
        // Self-only: your own progression, not a broadcast. Guarded on an INCREASE so the
        // scene-reload reset to 0 on rematch never plays a tier-up.
        if (HasInputAuthority && TotalDepositedValue > lastRenderedDeposit)
            Audio.PlayUi(AudioCueId.BuffTierUp);
        lastRenderedDeposit = TotalDepositedValue;
```

and add the backing field next to the other private fields:

```csharp
    // Render-side only: previous deposit total, so OnBuffsChanged can distinguish earning a tier
    // from a reset. Never read by simulation, never networked.
    private int lastRenderedDeposit;
```

Add `using Game.Audio.Core;` to the file's using block.

- [ ] **Step 4: `TeamScoreManager.cs` — score tick and team-buff unlock**

Both raise sites are already `OnChangedRender` callbacks (`TeamScoreManager.cs:19-20` and `:33-34`), so they fire on every peer — no authority handling needed.

Change (`:47-50`):

```csharp
    private void OnScoresChanged()
    {
        ScoresChanged?.Invoke();
        TeamBuffsChanged?.Invoke();
    }
```

to:

```csharp
    private void OnScoresChanged()
    {
        // Flat, everyone. The 250 ms dedupe window on the cue asset is what stops a burst deposit
        // (which writes the score several times in a few frames) from machine-gunning.
        Audio.PlayUi(AudioCueId.ScoreTick);

        ScoresChanged?.Invoke();
        TeamBuffsChanged?.Invoke();
    }
```

Change (`:53`):

```csharp
    private void OnTeamBuffsChanged() => TeamBuffsChanged?.Invoke();
```

to:

```csharp
    private void OnTeamBuffsChanged()
    {
        Audio.PlayUi(AudioCueId.TeamBuffUnlocked);
        TeamBuffsChanged?.Invoke();
    }
```

Note that `OnTeamBuffsChanged` is also subscribed to `MatchManager.PhaseChanged` (`:120`), so the unlock cue will additionally fire once on each phase change. Verify in Task 13 step 3.8 that this is not obtrusive; if it is, gate the cue on a roster-size increase using the same last-rendered-value pattern used in `PlayerBuffs` (Step 3 above).

Add `using Game.Audio.Core;` to the file's using block.

- [ ] **Step 5: `Enemy.cs` — hurt and death cues**

Both must be driven from render-side state, **not** from `RPC_TakeDamage` or `Die()`. Both of those are state-authority-only (`RPC_TakeDamage` is `RpcTargets.StateAuthority`; `Die()` early-returns unless `HasStateAuthority`), so a cue placed in either is inaudible to every player on a dedicated server. `CurrentHealth` is already `[Networked]` (`Enemy.cs:33-34`) and `Enemy` already overrides `Render()` (`:172`), which runs on every peer.

Add these fields to `Enemy`:

```csharp
    // Render-side only: previous health, so Render can fire the hurt cue on a damage edge without
    // depending on a server-only code path. Never read by simulation, never networked.
    private int lastRenderedHealth = int.MinValue;
    private bool renderedHealthPrimed;
```

At the end of the existing `Render()` override, add:

```csharp
        // Hurt is derived from the replicated health rather than from RPC_TakeDamage, which is
        // StateAuthority-targeted and therefore never runs on a client. The primed flag skips the
        // first frame, so a spawning enemy's initial health value is not heard as a hit.
        if (renderedHealthPrimed && CurrentHealth < lastRenderedHealth)
            Audio.PlayAt(AudioCueId.EnemyHurt, transform.position);
        lastRenderedHealth = CurrentHealth;
        renderedHealthPrimed = true;
```

Death goes in `Despawned`, which Fusion calls on **every** peer. Change:

```csharp
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
```

so its body begins with:

```csharp
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        Audio.PlayAt(AudioCueId.EnemyDeath, transform.position);
```

(keep the rest of the existing body unchanged).

Add `using Game.Audio.Core;` to the file's using block.

- [ ] **Step 6: `MatchManager.cs` — phase cues**

`EnterPhase` is server-only, so phase cues placed there would be silent for every player on a dedicated server. The correct hook is `OnPhaseChanged` — the `OnChangedRender` callback for the networked `Phase` (`MatchManager.cs:24`, `:193`), which fires on every peer. Music and snapshots are already handled separately by `MusicDirector`; this adds only the one-shot SFX layer.

Change:

```csharp
    private void OnPhaseChanged() => PhaseChanged?.Invoke();
```

to:

```csharp
    private void OnPhaseChanged()
    {
        PlayPhaseCue(Phase);
        PhaseChanged?.Invoke();
    }

    /// <summary>
    /// One-shot SFX layer for a phase change. Runs on EVERY peer, because this is the render
    /// callback for the networked Phase — putting it in EnterPhase (state authority only) would
    /// make it inaudible to every client on a dedicated server. Music and mixer snapshots are NOT
    /// driven from here; MusicDirector derives those from Phase independently, which is what makes
    /// them correct for a client that joined mid-phase.
    /// </summary>
    private void PlayPhaseCue(MatchPhase next)
    {
        switch (next)
        {
            case MatchPhase.Countdown:
                Audio.PlayUi(AudioCueId.CountdownGo);
                break;
            case MatchPhase.Live:
                Audio.PlayUi(AudioCueId.MatchStart);
                break;
            case MatchPhase.SuddenDeath:
                Audio.PlayUi(AudioCueId.SuddenDeathAlert);
                break;
            case MatchPhase.PostMatch:
                Audio.PlayUi(AudioCueId.MatchEnd);
                break;
        }
    }
```

Note that `Spawned()` already calls `OnPhaseChanged()` directly (`MatchManager.cs:73`) so a late joiner gets its initial phase — that path now also plays the phase cue, which is correct: a client joining into `Live` hears the match-start cue once as it arrives.

Add `using Game.Audio.Core;` to the file's using block.

- [ ] **Step 7: Compile-check**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log
```
Expected: `Test run completed`, no compile errors, full suite green.

- [ ] **Step 8: Commit**

```bash
git add "Assets/Scripts/CTF Flag/Flag.cs" Assets/Scripts/Buffs/PlayerBuffs.cs "Assets/Scripts/Coin Scripts/TeamScoreManager.cs" Assets/Scripts/Enemy/Base/Enemy.cs Assets/Scripts/Match/MatchManager.cs
git commit -m "feat(audio): subscribe flag, buff, score, enemy, and match cues"
```

---

### Task 10: UI, toast, countdown, and enemy-telegraph cues

**Files:**
- Modify: `Assets/Scripts/UI/SettingsPanel.cs` (open/close, slider tick)
- Modify: `Assets/Scripts/UI/MainMenuUI.cs` (button clicks)
- Modify: `Assets/Scripts/UI/LobbyScreenUI.cs` (button clicks)
- Modify: `Assets/Scripts/Hud/ScoreboardInputReader.cs` (panel open/close)
- Modify: `Assets/Scripts/Hud/HudToastFeed.cs:34` (`Show`)
- Modify: `Assets/Scripts/Enemy/Base/Enemy.cs` (telegraph cue)

**Interfaces:**
- Consumes: `Audio.PlayUi` / `Audio.PlayAt`, `AudioCueId` (Tasks 1, 5); existing `EnemyAI` state machine (`Assets/Scripts/Enemy/AI/`, `State.Telegraphing` at `EnemyAI.cs:30`).

- [ ] **Step 1: `HudToastFeed.cs` — toast blip**

Change:

```csharp
    public void Show(string message)
```

so its body begins with:

```csharp
    public void Show(string message)
    {
        Audio.PlayUi(AudioCueId.ToastNotification);
```

(keep the rest of the existing body unchanged). Add `using Game.Audio.Core;` to the using block.

Note: this is the neutral blip only. Specific flag sounds key off `Flag.OnStateChanged` (Task 9), never off the notification string — the string is formatted display text and matching on its content would be fragile.

- [ ] **Step 2: `SettingsPanel.cs` — panel and slider cues**

In the method that opens the panel (the one that activates the panel GameObject), add as its first statement:

```csharp
        Audio.PlayUi(AudioCueId.PanelOpen);
```

In `Close()`, add as its first statement:

```csharp
        Audio.PlayUi(AudioCueId.PanelClose);
```

At line 116, where `SettingsService.ApplyAudio()` is already called on a volume-slider change, add immediately after it:

```csharp
        // Audible reference while dragging a volume slider. maxConcurrent = 1 and a 40 ms dedupe
        // window on the cue asset are what stop a drag (which fires every frame) machine-gunning.
        Audio.PlayUi(AudioCueId.UiSliderTick);
```

Add `using Game.Audio.Core;` to the using block.

- [ ] **Step 3: `MainMenuUI.cs` and `LobbyScreenUI.cs` — button cues**

In each button-click handler that starts, hosts, joins, or confirms, add as the first statement:

```csharp
        Audio.PlayUi(AudioCueId.UiClick);
```

In each handler that cancels, backs out, or closes, add as the first statement:

```csharp
        Audio.PlayUi(AudioCueId.UiBack);
```

In each toggle/dropdown change handler, add as the first statement:

```csharp
        Audio.PlayUi(AudioCueId.UiToggle);
```

Add `using Game.Audio.Core;` to both files' using blocks. `UiHover` needs an `EventTrigger`/`IPointerEnterHandler` that these screens do not currently have; it stays in the catalog and in the bank but is left unwired in this pass — wiring it is a UI change, not an audio one, and is called out in Task 13's known-gaps note.

- [ ] **Step 4: `ScoreboardInputReader.cs` — scoreboard cues**

Where the scoreboard is shown, add:

```csharp
        Audio.PlayUi(AudioCueId.PanelOpen);
```

Where it is hidden, add:

```csharp
        Audio.PlayUi(AudioCueId.PanelClose);
```

Add `using Game.Audio.Core;` to the using block.

- [ ] **Step 5: `Enemy.cs` — telegraph and attack cues**

No change to `EnemyAI` is needed: `Enemy` already declares `[Networked] public NetworkBool IsTelegraphing { get; set; }` (`Enemy.cs:39`), explicitly so proxies can see the windup. That means the telegraph state is already correct on every peer — read it in `Render()` alongside the hurt cue from Task 9.

Add this field to `Enemy`:

```csharp
    // Render-side only: previous telegraph state, so the cue fires on the rising edge rather than
    // every frame the enemy spends winding up. Never networked.
    private bool wasTelegraphing;
```

In the existing `Render()` override (`:172`), immediately after the hurt-cue block added in Task 9 Step 5, add:

```csharp
        // The telegraph is the counterplay window — if it is inaudible the enemy is unfair, which
        // is why this cue carries priority 100 and is never stolen from the voice pool.
        bool telegraphing = IsTelegraphing;
        if (telegraphing && !wasTelegraphing)
            Audio.PlayAt(AudioCueId.EnemyTelegraph, transform.position);
        wasTelegraphing = telegraphing;
```

In `Enemy.AttackPlayer` (called from `EnemyAI.Attack()` at `EnemyAI.cs:425`), add as the first statement:

```csharp
        Audio.PlayAt(AudioCueId.EnemyAttack, transform.position);
```

`AttackPlayer` runs on the state authority only, so on a dedicated server this one cue is silent for everyone. Accepted for v1: the telegraph immediately preceding it is replicated and is the cue that actually carries the counterplay information. Recorded in Task 13's known-gaps step.

`using Game.Audio.Core;` was already added to `Enemy.cs` in Task 9.

- [ ] **Step 6: `MusicDirector.cs` — the per-second countdown tick**

`CountdownTick` needs a whole-second edge during `MatchPhase.Countdown`. `MatchManager` has no render loop, but `MusicDirector.Tick` already runs every frame and already holds a `MatchManager` reference, so the edge detector goes there rather than adding an `Update` to a `NetworkBehaviour`.

Add the field:

```csharp
    private int lastCountdownSecond = -1;
```

Add this call at the end of `Tick`:

```csharp
        AdvanceCountdownTick();
```

And add the method:

```csharp
    /// <summary>
    /// One tick per whole second remaining during Countdown. Derived from the networked phase
    /// timer, so every peer counts down together without a per-second RPC. The second counter
    /// resets whenever the phase is not Countdown, so re-entering it starts clean.
    /// </summary>
    private void AdvanceCountdownTick()
    {
        MatchManager match = MatchManager.Instance;
        if (match == null || match.Phase != MatchPhase.Countdown)
        {
            lastCountdownSecond = -1;
            return;
        }

        float? remaining = match.PhaseTimeRemaining;
        if (!remaining.HasValue) return;

        int second = Mathf.CeilToInt(remaining.Value);
        if (second <= 0 || second == lastCountdownSecond) return;

        lastCountdownSecond = second;
        Audio.PlayUi(AudioCueId.CountdownTick);
    }
```

- [ ] **Step 7: Verify no legacy audio calls remain**

Run:
```bash
grep -rn "PlayClipAtPoint\|PlayOneShot" Assets/Scripts
```
Expected: **no output**. Any hit is a missed migration — fix it before continuing.

- [ ] **Step 8: Compile-check**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log
```
Expected: `Test run completed`, no compile errors, full suite green.

- [ ] **Step 9: Commit**

```bash
git add Assets/Scripts/UI Assets/Scripts/Hud/HudToastFeed.cs Assets/Scripts/Hud/ScoreboardInputReader.cs Assets/Scripts/Enemy Assets/Scripts/Audio/MusicDirector.cs
git commit -m "feat(audio): wire UI, toast, countdown, and enemy telegraph cues"
```

---

### Task 11: Asset-integrity EditMode tests

These fail until Task 12 authors the assets. That is the point: they turn a manual authoring pass into one with an objective, machine-checked completion criterion, and they convert this project's dominant failure mode — an unassigned reference that fails silently — into a red test.

**Files:**
- Test: `Assets/Tests/EditMode/Audio/Assets/AudioAssetIntegrityTests.cs`
- Modify: `Assets/Tests/EditMode/Audio/Assets/Game.Audio.Assets.Tests.asmdef` (no change needed — `includePlatforms: ["Editor"]` already makes `UnityEditor` available)

**Interfaces:**
- Consumes: `AudioConfig`, `SoundBank`, `SoundCue` (Task 4), `AudioCueId`, `AudioBus`, `MixerSnapshotId` (Task 1); `SettingsService`'s four exposed-parameter name constants are **duplicated as literals** here on purpose — `SettingsService` lives in `Assembly-CSharp`, which this test assembly cannot reference, and a literal that must match is exactly what this test is for.

- [ ] **Step 1: Write the tests**

Create `Assets/Tests/EditMode/Audio/Assets/AudioAssetIntegrityTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Game.Audio.Core;
using UnityEngine;
using UnityEngine.Audio;

public class AudioAssetIntegrityTests
{
    // Duplicated from SettingsService.cs:26-29 as literals: SettingsService lives in
    // Assembly-CSharp, which no asmdef can reference. If these ever diverge, that IS the bug this
    // test exists to catch — the four volume sliders would silently stop working.
    private static readonly string[] RequiredExposedParams =
    {
        "MasterVolume", "MusicVolume", "SfxVolume", "UiVolume"
    };

    private static AudioConfig LoadConfig()
    {
        AudioConfig config = Resources.Load<AudioConfig>("AudioConfig");
        Assert.IsNotNull(config, "Resources/AudioConfig.asset is missing — the game boots silent.");
        return config;
    }

    [Test]
    public void AudioConfig_ExistsWithAMixerAndABank()
    {
        AudioConfig config = LoadConfig();
        Assert.IsNotNull(config.Mixer, "AudioConfig has no AudioMixer assigned.");
        Assert.IsNotNull(config.Bank, "AudioConfig has no SoundBank assigned.");
    }

    [Test]
    public void EveryCueId_HasABankEntryWithAPlayableClip()
    {
        SoundBank bank = LoadConfig().Bank;
        var missing = new List<string>();

        foreach (AudioCueId id in System.Enum.GetValues(typeof(AudioCueId)))
        {
            if (id == AudioCueId.None) continue;

            if (!bank.TryGet(id, out SoundCue cue)) missing.Add($"{id}: no bank entry");
            else if (!cue.HasClip) missing.Add($"{id}: entry has no clip");
        }

        Assert.IsEmpty(missing,
            "Every AudioCueId must resolve to a bank entry with at least one clip:\n"
            + string.Join("\n", missing));
    }

    [Test]
    public void EveryBankEntry_RoutesToAGroupThatExistsInTheMixer()
    {
        AudioConfig config = LoadConfig();
        var missing = new List<string>();

        foreach (SoundCue cue in config.Bank.Cues)
        {
            if (cue == null || cue.id == AudioCueId.None) continue;

            AudioMixerGroup[] groups = config.Mixer.FindMatchingGroups(cue.bus.ToString());
            if (groups == null || groups.Length == 0) missing.Add($"{cue.id} -> '{cue.bus}'");
        }

        Assert.IsEmpty(missing, "Cues routed to mixer groups that do not exist:\n" + string.Join("\n", missing));
    }

    [Test]
    public void Mixer_ExposesExactlyTheFourContractedVolumeParameters()
    {
        AudioMixer mixer = LoadConfig().Mixer;

        foreach (string param in RequiredExposedParams)
            Assert.IsTrue(mixer.GetFloat(param, out _),
                $"Mixer does not expose '{param}'. SettingsService.ApplyAudio would silently no-op for it.");
    }

    [Test]
    public void EverySnapshotNameInTheEnum_ExistsInTheMixer()
    {
        AudioMixer mixer = LoadConfig().Mixer;

        foreach (MixerSnapshotId id in System.Enum.GetValues(typeof(MixerSnapshotId)))
            Assert.IsNotNull(mixer.FindSnapshot(id.ToString()),
                $"Mixer has no snapshot named '{id}'.");
    }

    [Test]
    public void EveryMusicTrack_HasAClip()
    {
        AudioConfig config = LoadConfig();
        var missing = new List<string>();

        foreach (MusicTrackId id in System.Enum.GetValues(typeof(MusicTrackId)))
        {
            if (id == MusicTrackId.None) continue;
            if (config.GetMusicClip(id) == null) missing.Add(id.ToString());
        }

        Assert.IsEmpty(missing, "Music tracks with no clip assigned: " + string.Join(", ", missing));
    }

    // Licensing is a shipping requirement, not a nicety: an unlicensed file in Assets/Sound is a
    // legal problem, and "we'll remember to check" has already failed once in this project.
    [Test]
    public void EveryAudioFile_HasALicenseRow()
    {
        const string soundRoot = "Assets/Sound";
        const string licensePath = "Assets/Sound/LICENSES.md";

        Assert.IsTrue(File.Exists(licensePath), $"{licensePath} is missing.");
        string licenses = File.ReadAllText(licensePath);

        var undocumented = new List<string>();
        foreach (string path in Directory.GetFiles(soundRoot, "*.*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".wav" && extension != ".ogg" && extension != ".mp3") continue;

            string fileName = Path.GetFileName(path);
            if (!licenses.Contains(fileName)) undocumented.Add(fileName);
        }

        Assert.IsEmpty(undocumented,
            "Audio files with no row in LICENSES.md:\n" + string.Join("\n", undocumented));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testFilter AudioAssetIntegrityTests -testResults r.xml -logFile r.log
```
Expected: every test **fails** — `Resources/AudioConfig.asset` does not exist yet. This is the correct state at the end of this task.

- [ ] **Step 3: Commit**

```bash
git add Assets/Tests/EditMode/Audio/Assets/AudioAssetIntegrityTests.cs
git commit -m "test(audio): add asset integrity gates for bank, mixer, and licensing"
```

Note in the commit body that these tests are expected red until Task 12 lands, so a reviewer does not mistake them for a regression.

---

### Task 12: Acquire assets and author the mixer, bank, and config (Unity Editor)

This task has no `.cs` changes and **must** be done inside the Unity Editor — never by hand-authoring `.asset`/`.mixer` YAML. Its completion criterion is objective: Task 11's tests go green.

If the executor has no Unity Editor access, stop here and hand this checklist to the project owner.

**Files:**
- Create (in-editor): `Assets/Audio/GameMixer.mixer`
- Create (in-editor): `Assets/Resources/AudioConfig.asset`
- Create (in-editor): `Assets/Audio/MainSoundBank.asset`
- Create: `Assets/Sound/LICENSES.md`
- Create: audio files under `Assets/Sound/SFX/` and `Assets/Sound/Music/`

- [ ] **Step 1: Acquire the assets**

Sources, in order of preference (spec decision 2): **Kenney.nl** CC0 game-audio packs first (arcade character, consistent, no attribution burden); **freesound.org** filtered to CC0 for gaps; CC0 or CC-BY loops for the five music/ambient beds. **CC0 or an explicit commercial license only** — nothing else.

Acquire one file per catalog entry (**49 cues + 5 beds** — the spec's 52 minus `KillfeedEntry`, `KillConfirmSelf`, and `FlagCaptured`, cut per Global Constraints; 2–3 variants each for `MeleeSwing`, `HitConfirm`, `ProjectileImpact`, `Jump`, `Land`, `EnemyHurt` — the cues heard most often). Place them under `Assets/Sound/SFX/<category>/` and `Assets/Sound/Music/`. The `AudioCueId` enum from Task 1 is the authoritative checklist — Task 11's bank-completeness test is asserted against it, not against the spec's table.

- [ ] **Step 2: Create `Assets/Sound/LICENSES.md`**

One row per acquired file. Files with no row fail Task 11's licensing test.

```markdown
# Audio asset licenses

Every file under `Assets/Sound/` must have a row here. This is enforced by
`AudioAssetIntegrityTests.EveryAudioFile_HasALicenseRow`.

Policy: CC0 or an explicit commercial license only. For CC-BY assets, the attribution text in the
"Attribution" column must also appear in the in-game credits.

| File | Source | Author | License | Attribution | Acquired |
|---|---|---|---|---|---|
| melee_swing_01.wav | https://kenney.nl/assets/impact-sounds | Kenney | CC0 | — | 2026-08-10 |

## Not shipped

- `Music/Halo Theme Song Original.mp3` — copyrighted. Retained as a DEV-ONLY reference for
  calibrating music-bus gain staging in the lobby. It is never entered into the SoundBank and
  never referenced by AudioConfig, so no code path can reach it. **Delete this file and its
  `.meta` before any public build** — see the pre-ship gate in the design spec.
```

- [ ] **Step 3: Author the mixer**

Create `Assets/Audio/GameMixer.mixer` (**Assets → Create → Audio Mixer**) with this exact group tree — the names are matched by string at runtime (`AudioManager.CacheMixerGroups`) and asserted by Task 11:

```
Master
├── Music
├── SFX
│   ├── Combat
│   ├── World
│   ├── Enemy
│   └── Ambient
└── UI
```

Expose exactly four parameters, right-clicking each group's **Volume** in the inspector → *Expose … to script*, then renaming them in the **Exposed Parameters** dropdown to **exactly**:

| Group | Exposed parameter name |
|---|---|
| Master | `MasterVolume` |
| Music | `MusicVolume` |
| SFX | `SfxVolume` |
| UI | `UiVolume` |

Do **not** expose the volume of `Combat`, `World`, `Enemy`, or `Ambient`.

- [ ] **Step 4: Author the four snapshots**

Create snapshots named **exactly** `Default`, `Menu`, `SuddenDeath`, `Stinger`.

| Snapshot | Combat | World | Enemy | Ambient | Music |
|---|---|---|---|---|---|
| `Default` | 0 dB | 0 dB | 0 dB | 0 dB | 0 dB |
| `Menu` | −80 dB | −80 dB | −80 dB | −12 dB | 0 dB |
| `SuddenDeath` | 0 dB | 0 dB | −6 dB | −9 dB | +2 dB |
| `Stinger` | 0 dB | 0 dB | −12 dB | −12 dB | −6 dB |

**Critical (spec decision 7):** none of these snapshots may touch the Master, Music, SFX, or UI *group* volumes — those four carry the exposed parameters, and a snapshot transition would silently overwrite the player's saved setting on every phase change. The `Music` column above is the music **bed group's** own volume within the Music bus, not the exposed `MusicVolume` parameter. If the mixer layout makes that distinction impossible, add a `MusicBed` child group under `Music` and animate that instead.

- [ ] **Step 5: Author the sound bank**

Create `Assets/Audio/MainSoundBank.asset` (**Assets → Create → Audio → Sound Bank**) with one entry per `AudioCueId`. Starting values, retunable by ear afterwards:

| Cue group | bus | positional | priority | dedupeWindow | maxConcurrent | maxDistance |
|---|---|---|---|---|---|---|
| Combat swings/impacts | `Combat` | true | 50 | 0.06 | 0 | 0 (default) |
| `HitConfirm`, `HitConfirmHeavy`, `TookDamage` | `Combat` | **false** | 80 | 0.04 | 0 | — |
| Movement | `World` | true | 30 | 0.05 | 0 | 0 |
| `CoinPickupWorld`, `DepositWorld` | `World` | true | 40 | 0.05 | 0 | 0 |
| `CoinPickupSelf`, `DepositSelf` | `Ui` | false | 70 | 0 | 0 | — |
| `ScoreTick` | `Ui` | false | 60 | 0.25 | 1 | — |
| Flag world cues | `World` | true | 70 | 0 | 0 | 0 |
| `AlertOwnFlagTaken`, `FlagPickupSelf` | `Ui` | false | 95 | 0 | 1 | — |
| `BuffTierUp`, `TeamBuffUnlocked` | `Ui` | false | 70 | 0 | 1 | — |
| `StealthEnter`, `StealthExit` | `World` | true | 50 | 0 | 0 | **5** (≈0.35× default — spec decision 21) |
| `EnemyTelegraph` | `Enemy` | true | **100** | 0 | 0 | 0 |
| `EnemyAttack`, `EnemyHurt`, `EnemyDeath` | `Enemy` | true | 50 | 0.06 | 0 | 0 |
| `EnemySpawn` | `Enemy` | true | **0** | 0.1 | 0 | 0 |
| Match/stingers | `Ui` (stingers `Music`) | false | **100** | 0 | 1 | — |
| UI | `Ui` | false | 60 | 0 | 1 | — |
| `UiSliderTick` | `Ui` | false | 40 | **0.04** | **1** | — |

`WallOrLedgeScuff` is a stretch cue (spec catalog). If no suitable CC0 asset is found, **remove its value from `AudioCueId`** and rebuild rather than leaving an empty bank entry — an empty entry would defeat the bank-completeness test.

- [ ] **Step 6: Author the config**

Create `Assets/Resources/AudioConfig.asset` (**Assets → Create → Audio → Audio Config**; the `Resources` folder name is load-bearing — `AudioManager.Bootstrap` does `Resources.Load<AudioConfig>("AudioConfig")`, so the file must be named `AudioConfig` and sit directly in a folder named `Resources`).

Assign: `mixer` = `GameMixer`, `bank` = `MainSoundBank`, one `musicTracks` entry per `MusicTrackId` except `None`. Leave `sfxVoices` 32, `uiVoices` 4, `defaultWorldMaxDistance` 14, `maxPan` 0.7, `musicCrossfadeSeconds` 1.5.

- [ ] **Step 7: Run the integrity tests until green**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testFilter AudioAssetIntegrityTests -testResults r.xml -logFile r.log
```
Expected: all 7 `AudioAssetIntegrityTests` `result="Passed"`. Each failure message names the exact missing cue, group, parameter, snapshot, track, or license row — work the list until it is empty. Do not mark this task complete with any of them red.

- [ ] **Step 8: Commit**

```bash
git add Assets/Audio Assets/Resources Assets/Sound
git commit -m "feat(audio): author mixer, snapshots, sound bank, config, and CC0 asset set"
```

---

### Task 13: Full-suite, multi-peer, and dedicated-server verification

No `.cs` changes. Requires the Unity Editor and this project's existing multi-peer test flow.

- [ ] **Step 1: Full EditMode suite**

Run:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r.xml -logFile r.log
```
Expected: `Test run completed` in `r.log`, zero failures. The pre-existing baseline is 386 green; this plan adds roughly 40 cases across `SoundDedupeTests`, `VoiceBudgetTests`, `MusicStateTests`, `SoundBankTests`, and `AudioAssetIntegrityTests`.

- [ ] **Step 2: Confirm no legacy audio API remains**

Run:
```bash
grep -rn "PlayClipAtPoint\|PlayOneShot\|AudioClip" Assets/Scripts --include=*.cs
```
Expected: hits only inside `Assets/Scripts/Audio/` (`SoundCue.variants`, `AudioConfig.MusicEntry.clip`, `AudioManager.NextVariant`, `MusicDirector`). Any hit in a gameplay script is a missed migration.

- [ ] **Step 3: Multi-peer verification** (3 peers minimum: two on Team1, one on Team2)

  1. **Settings.** All four sliders audibly change their bus. Values survive a restart. *Reset to defaults* restores Master 0.8 / Music 0.7 / SFX 1.0 / UI 1.0 and is audible.
  2. **Snapshot safety.** Trigger a Sudden Death and a match end, then reopen settings: the four slider positions are unchanged and still match what you hear. (This is the decision-7 regression check — a snapshot animating an exposed parameter shows up here.)
  3. **Melee.** The swing sounds instantly on the swinging client. The hit-confirm plays **only** on the attacker; the victim hears only `TookDamage`. No client hears two sounds for one hit.
  4. **Coins.** One pickup = exactly one world sound for observers and exactly one distinct self chime for the collector. The collector no longer hears the same clip twice.
  5. **Distance.** A fight off-screen is inaudible. Walking toward it fades it in smoothly with no hard pan and no pop at the cull boundary.
  6. **Flag alert.** `AlertOwnFlagTaken` is clearly audible from the far side of the map; the positional `FlagTaken` at the same instant is not. Players on the *other* team hear only the positional cue.
  7. **Stealth.** Activating stealth is audible to an opponent standing adjacent, and inaudible to one across the arena.
  8. **Phases.** Countdown, match start, the Sudden Death crossfade plus its duck, and the correct victory/defeat stinger per team. A spectator (or a player with no team) gets the draw stinger, never victory.
  9. **Rematch.** Scene reload: music continues correctly, there is exactly one `AudioManager` in the hierarchy, and no doubled music.
  10. **Scrum.** All peers attacking in one area produce a legible impact rhythm, not a continuous buzz. Check the profiler for audio-driven frame spikes.
  11. **Late join.** A client joining mid-`Live` starts on the gameplay bed under the `Default` snapshot, not the menu bed.

- [ ] **Step 4: Dedicated-server verification**

  Run a full match on a headless build. Expected: zero `[Audio]` log lines, no `AudioManager` GameObject, no audio allocation in the profiler. `AudioManager.Bootstrap` returns at its first line because `SettingsService.HasDisplay` is false.

- [ ] **Step 5: Record known gaps**

  Append to the spec's *Out of Scope* section, or open follow-ups for:
  - **`KillfeedEntry` / `KillConfirmSelf` cut from v1** — `MatchStatsManager.RecordKill` is server-only and there is no per-kill client broadcast; wiring them needs a new RPC, which spec decision 11 forbids. Revisit when a killfeed broadcast exists.
  - **`FlagCaptured` cut from v1** — redundant with `MatchEnd`, which a capture already triggers via `MatchPhase.PostMatch`.
  - **`LandHeavy` unwired** — `PlayerAnimator` has no view of `PlayerCombat.AttackIsPound`, so a ground-pound landing plays the normal `Land` cue. `MeleeSwingHeavy` already carries the pound's audio identity on the way down.
  - **`EnemyAttack` is server-only** — `Enemy.AttackPlayer` runs on the state authority, so on a dedicated server it is silent. The replicated `EnemyTelegraph` immediately preceding it carries the counterplay information; revisit if playtest shows the attack itself needs to be heard.
  - `UiHover` is authored in the bank but unwired — the menu screens have no pointer-enter handlers (Task 10, Step 3).
  - `WallOrLedgeScuff`, if it was cut for lack of a CC0 source.
  - The pre-ship gate: `Assets/Sound/Music/Halo Theme Song Original.mp3` and its `.meta` must be deleted before any public build.

- [ ] **Step 6: Commit any tuning changes**

```bash
git add -A
git commit -m "chore(audio): mix tuning from multi-peer verification"
```

If any verification step fails, do not mark this task complete — fix the relevant task's code or asset values and re-run the full checklist.
