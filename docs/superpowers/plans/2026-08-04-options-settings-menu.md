# Options / Settings Menu — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a client-local options menu (Audio / Video / Gameplay) backed by `PlayerPrefs`, applied before the first rendered frame, and prune the seven dead fields out of `GameSettingsManager` so client preferences and host match rules stop sharing one object.

**Architecture:** A new engine-free `Game.Settings.Core` assembly holds all pure logic (key/default catalog, linear→dB curve, version-migration decision, resolution de-duplication) and is unit-tested outside Unity. A static `SettingsStore` caches values over `PlayerPrefs`; a static `SettingsService` pushes them into the engine from a `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` hook and no-ops entirely on headless builds. Two existing client-local consumers (`PlayerCameraShakeHandler`, `HitFeedback`) each grow a two-line read. The UI is two MonoBehaviours in `MainMenu.unity` — `SettingsPanel` (tabs, audio, gameplay) and `VideoSettingsSection` (resolution/display/vsync/fps plus the confirm-or-revert prompt) — reached from Options buttons on `MainMenuUI` and `LobbyScreenUI`.

**Tech Stack:** Unity 6.3 (6000.3.0f1), C#, NUnit EditMode tests, TextMeshPro, uGUI (`UnityEngine.UI`), `PlayerPrefs`.

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-07-29-options-settings-design.md` — read it first; this plan implements every decision in it verbatim.
- **Nothing in this feature may write networked or simulation state.** No RPCs, no `[Networked]` fields, no `NetworkBehaviour`s, no writes to `NetInput`. Two players with opposite settings must produce identical simulation outcomes.
- **Do not modify `Assets/Scripts/Player/NetworkInputProvider.cs`.** It stays the sole gameplay device-read site, untouched. No sensitivity multiplier, no action-map indirection, no settings lookup on the input path.
- **No Controls section, no key rebinding, no aim sensitivity** (spec Decision 2). Do not add a Controls tab, a bindings reference card, or a `PerformInteractiveRebinding` call.
- **No in-match overlay** (spec Decision 4). Nothing in this plan touches `Assets/Scenes/Gameplay.unity` UI, `MatchPhaseHud`, `MatchManager`, or `MatchManager.InputEnabled`. The only Gameplay-scene effect is that pruned `GameSettingsManager` fields disappear from its YAML when the user next saves the scene.
- **No `AudioMixer` asset, mixer groups, or audio routing is created** (spec: the audio system is a separate, unwritten spec). The four volume sliders persist and are applied to `SettingsService.Mixer` **if and only if** something has assigned it; that reference is null today, so they are stored-but-silent. This is expected, not a bug.
- **Pure, unit-tested logic lives in the engine-free `Game.Settings.Core` asmdef** (`noEngineReferences: true`). No `UnityEngine.Mathf`, no `Screen`, no `PlayerPrefs` in those files — use `System.Math` and plain comparisons. `SettingsStore`, `SettingsService`, and the UI MonoBehaviours live in the default assembly (no asmdef), matching `MainMenuUI`, `LobbyScreenUI`, `HitFeedback`, and `PlayerCameraShakeHandler` today.
- **Never call `PlayerPrefs.DeleteAll()`.** A settings reset deletes only the keys listed in `SettingsCatalog.AllKeys`. `lobby.nickname` (`MainMenuUI.cs:26`) and the reconnection identity token (`PlayerIdentity.cs`) live in the same store and must survive.
- **The whole service no-ops when there is no graphics device.** The dedicated-server build must never call `Screen.SetResolution` or touch audio.
- **Every deleted `GameSettingsManager` field has zero consumers** — verify with a repo-wide grep before deleting, and delete `GetRespawnTime` along with `respawnTimeMultiplier` (the method is the field's only reader and has no callers of its own).
- **Out of scope — do not build:** a host match-rules UI, quality-level or brightness settings, refresh-rate selection, renaming `GameSettingsManager`, a settings UI for the nickname, accessibility options, cloud/profile sync, or migrating existing sounds off `AudioSource.PlayClipAtPoint`.

### Numbers, verbatim from the spec

| Setting | Key | Type | Default | Range |
|---|---|---|---|---|
| Master volume | `settings.audio.master` | float | `0.8` | 0–1 |
| Music volume | `settings.audio.music` | float | `0.7` | 0–1 |
| SFX volume | `settings.audio.sfx` | float | `1.0` | 0–1 |
| UI volume | `settings.audio.ui` | float | `1.0` | 0–1 |
| Resolution width | `settings.video.width` | int | native at first boot | from `Screen.resolutions` |
| Resolution height | `settings.video.height` | int | native at first boot | from `Screen.resolutions` |
| Display mode | `settings.video.displayMode` | int | `1` (`FullScreenMode.FullScreenWindow`) | 0–3 |
| VSync | `settings.video.vsync` | int | `1` | 0 or 1 |
| Framerate cap | `settings.video.fpsCap` | int | `0` (uncapped) | 0, or 30–1000 |
| Camera shake | `settings.gameplay.cameraShake` | float | `1.0` | 0–2 |
| Damage numbers | `settings.gameplay.damageNumbers` | int | `1` | 0 or 1 |
| Settings version | `settings.version` | int | `1` | — |

| Thing | Value |
|---|---|
| Linear→dB curve | `v <= 0 ? -80 : Log10(Max(v, 0.0001)) * 20` |
| Mixer exposed params | `MasterVolume`, `MusicVolume`, `SfxVolume`, `UiVolume` |
| Resolution confirm timeout | 10 seconds, then auto-revert |
| Uncapped framerate | `Application.targetFrameRate = -1` |
| `FullScreenMode` values | `ExclusiveFullScreen=0`, `FullScreenWindow=1`, `MaximizedWindow=2`, `Windowed=3` |

### How to run tests

**Environment truth — read this before writing any "tests pass" claim:**

- **NUnit does NOT run outside the Unity editor here** (no reachable `nunit.framework.dll`). The committed NUnit `[Test]` files are the **user's** Test Runner gate. They are required deliverables — write them exactly as specified — but you cannot execute them.
- **Your execution evidence is a plain-`Main` harness**: compile the engine-free `Game.Settings.Core` sources plus a hand-written `class H { static int Main() }` assert harness against `netstandard 2.1` using
  `C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Data\DotNetSdkRoslyn\csc.dll`,
  write a `net6.0` `runtimeconfig.json` beside the exe, and run it on
  `C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Data\NetCoreRuntime\dotnet.exe` (it carries `Microsoft.NETCore.App 6.0.21`). Mirror every NUnit case as a harness assertion so the numbers you report correspond 1:1 to the committed tests.
- **Report the two separately.** "Harness: N/N assertions pass" is execution evidence. "Compile gate: exit 0" is not. Never write "tests pass" meaning only that it compiled.

EditMode tests run in Unity for the **user**: Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All. Note this as pending in your report; do not claim it.

For the whole-surface compile gate, build a `@response.rsp` for `csc.dll` referencing the netstandard 2.1 ref, `Editor\Data\Managed\UnityEngine\*.dll`, `Assets\Photon\Fusion\Assemblies\*.dll`, and `Library\ScriptAssemblies\*.dll` (skip `*Editor*` / `*CodeGen*` / `*Tests*`), compiling every `Assets/Scripts/**/*.cs` **except** the asmdef-owned folders (`Buffs/Core`, `Combat/Core`, `Enemy/AI`, `Hud/Core`, `Match/Core`, `Net`, `Player/Animation/Core`, `Player/Movement/Core`, `Stats/Core`, and `Settings/Core` once Task 1 lands). Quote every path inside the `.rsp` ("Program Files" has a space).

**Compile-gate gotchas, all previously hit in this repo:**
- **EXCLUDE `Assembly-CSharp.dll` from the reference list.** A stale copy produces bogus `CS1503`/`CS0117` errors against freshly compiled sources.
- **`Game.Settings.Core` is NEW.** There is no `Library\ScriptAssemblies\Game.Settings.Core.dll` until Unity imports it, so from Task 2 onward **compile `Assets/Scripts/Settings/Core/*.cs` inline** with the rest of the surface instead of referencing a DLL.
- **Reference paths must be Windows-format** (`cygpath -w`) when driving the gate from git-bash; source paths work as relative.
- **PowerShell gotcha:** `powershell -File script.ps1 -Sources "a","b","c"` mis-splits arrays through the Bash tool. Build `$Sources` natively via the PowerShell tool instead.
- Note the **baseline warning count** before your first change and attribute any increase to specific new fields. New `[SerializeField]` fields legitimately add `CS0649`.

**A clean compile is not verification.** Report separately what was executed and what was only compiled.

### What you CANNOT do (and must not claim)

You have no Unity Editor and no Play mode. Every step labeled **"Manual scene setup"** or **"Manual verification"** is the **user's** work:

- Do not create GameObjects, add components, or wire serialized fields in `Assets/Scenes/MainMenu.unity` or `Assets/Scenes/Gameplay.unity`.
- Do not `git add` either scene file (they are the user's working files; staging them would sweep up unrelated local edits — `git status` already shows `MainMenu.unity` modified).
- Do not enter Play mode, and never report a Play-mode result. In particular you cannot verify that resolution changes apply, that the auto-revert fires, or that volume sliders persist across a restart.

Write the code, run the harness and the compile gate, commit **code only**, and list every manual step you skipped under "Pending user verification" in your report.

---

## File Structure

**Created:**
- `Assets/Scripts/Settings/Core/Game.Settings.Core.asmdef` (+ `.meta`) — new engine-free assembly.
- `Assets/Scripts/Settings/Core/SettingsCatalog.cs` (+ `.meta`) — every key string, default, range, and clamp. One place, so store/UI/reset can never disagree.
- `Assets/Scripts/Settings/Core/VolumeCurve.cs` (+ `.meta`) — linear→dB.
- `Assets/Scripts/Settings/Core/SettingsMigration.cs` (+ `.meta`) — the stored-version decision.
- `Assets/Scripts/Settings/Core/ResolutionList.cs` (+ `.meta`) — `ResolutionOption` struct, de-duplication, stored-value resolution.
- `Assets/Tests/EditMode/Settings/Game.Settings.Core.Tests.asmdef` (+ `.meta`) — new test assembly.
- `Assets/Tests/EditMode/Settings/SettingsCatalogTests.cs`, `VolumeCurveTests.cs`, `SettingsMigrationTests.cs`, `ResolutionListTests.cs` (+ `.meta` each).
- `Assets/Scripts/Settings/SettingsStore.cs` (+ `.meta`) — cached `PlayerPrefs` accessors, the only place raw `PlayerPrefs` is touched for settings.
- `Assets/Scripts/Settings/SettingsService.cs` (+ `.meta`) — engine apply, boot hook, headless guard, mixer holder.
- `Assets/Scripts/UI/SettingsPanel.cs` (+ `.meta`) — panel lifecycle, tab switching, Audio + Gameplay controls, per-tab reset.
- `Assets/Scripts/UI/VideoSettingsSection.cs` (+ `.meta`) — resolution/display/vsync/fps controls and the confirm-or-revert prompt. Separate file because it is the only part with real state (pending value, countdown) and would otherwise double `SettingsPanel`'s size.
- `docs/settings-menu-unity-setup-guide.md` — the user's scene-wiring guide, mirroring `docs/scoreboard-unity-setup-guide.md`.

**Modified:**
- `Assets/Scripts/ScriptableObjects/Game Settings Manager.cs` — delete 7 dead fields, `GetRespawnTime`, and the 2 relocated client-local fields; keep `combatConfig`, `difficultyRingConfig`, `matchTimeLimit`, `suddenDeathHardCap`.
- `Assets/Scripts/ScriptableObjects/CombatConfig.cs` — delete the declared-but-never-read `showDamageNumbers`.
- `Assets/Scripts/Player/PlayerCameraShakeHandler.cs` — scale shake by the setting; skip entirely at 0.
- `Assets/Scripts/Player/HitFeedback.cs` — gate the damage-number spawn on the setting.
- `Assets/Scripts/UI/MainMenuUI.cs` — Options button + settings panel reference; `Show()` closes the panel.
- `Assets/Scripts/UI/LobbyScreenUI.cs` — Options button + settings panel reference; `Hide()` closes the panel.

**Modified by the USER, not by any implementer:**
- `Assets/Scenes/MainMenu.unity` — gains the `SettingsRoot` hierarchy and all field wiring (Task 9's guide).
- `Assets/Scenes/Gameplay.unity` — no deliberate change; the pruned `GameSettingsManager` fields simply drop out of its YAML on the user's next save. No values are lost that anything read.

---

## Task 1: Pure settings logic (`Game.Settings.Core`)

**Files:**
- Create: `Assets/Scripts/Settings.meta`, `Assets/Scripts/Settings/Core.meta` (folder metas)
- Create: `Assets/Scripts/Settings/Core/Game.Settings.Core.asmdef` (+ `.meta`)
- Create: `Assets/Scripts/Settings/Core/SettingsCatalog.cs` (+ `.meta`)
- Create: `Assets/Scripts/Settings/Core/VolumeCurve.cs` (+ `.meta`)
- Create: `Assets/Scripts/Settings/Core/SettingsMigration.cs` (+ `.meta`)
- Create: `Assets/Scripts/Settings/Core/ResolutionList.cs` (+ `.meta`)
- Create: `Assets/Tests/EditMode/Settings.meta` (folder meta)
- Create: `Assets/Tests/EditMode/Settings/Game.Settings.Core.Tests.asmdef` (+ `.meta`)
- Test: `Assets/Tests/EditMode/Settings/SettingsCatalogTests.cs` (+ `.meta`)
- Test: `Assets/Tests/EditMode/Settings/VolumeCurveTests.cs` (+ `.meta`)
- Test: `Assets/Tests/EditMode/Settings/SettingsMigrationTests.cs` (+ `.meta`)
- Test: `Assets/Tests/EditMode/Settings/ResolutionListTests.cs` (+ `.meta`)

Three folders do not exist yet and each needs its own folder `.meta`: `Assets/Scripts/Settings/`, `Assets/Scripts/Settings/Core/`, `Assets/Tests/EditMode/Settings/`.

**Interfaces:**
- Consumes: nothing (engine-free leaf).
- Produces:
  - `Game.Settings.Core.SettingsCatalog` — `const int CurrentVersion`; the 12 key `const string`s; the default `const`s; `static readonly string[] AllKeys`; `static float ClampVolume(float)`, `static float ClampCameraShake(float)`, `static int ClampVSync(int)`, `static int ClampFpsCap(int)`, `static int ClampDisplayMode(int)`, `static int ClampFlag(int)`.
  - `static float Game.Settings.Core.VolumeCurve.LinearToDecibels(float linear)`
  - `enum Game.Settings.Core.SettingsMigrationAction { None, ResetToDefaults }`
  - `static SettingsMigrationAction Game.Settings.Core.SettingsMigration.Resolve(int storedVersion, int currentVersion)`
  - `struct Game.Settings.Core.ResolutionOption` — `int Width`, `int Height`, ctor `(int, int)`.
  - `static List<ResolutionOption> Game.Settings.Core.ResolutionList.Deduplicate(IReadOnlyList<ResolutionOption> raw)`
  - `static int Game.Settings.Core.ResolutionList.IndexOf(IReadOnlyList<ResolutionOption> options, int width, int height)`
  - `static int Game.Settings.Core.ResolutionList.ResolveStoredIndex(IReadOnlyList<ResolutionOption> options, int storedWidth, int storedHeight, int nativeWidth, int nativeHeight)`

- [ ] **Step 1: Create the folder metas and the assembly definitions**

Create `Assets/Scripts/Settings.meta`:

```yaml
fileFormatVersion: 2
guid: 3226d7b7c99546598e4db74ed3a516d2
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

Create `Assets/Scripts/Settings/Core.meta`:

```yaml
fileFormatVersion: 2
guid: 7807889ec5ae4f459b814cb5a910f7c9
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

Create `Assets/Tests/EditMode/Settings.meta`:

```yaml
fileFormatVersion: 2
guid: c718f910aa434676a8ec4c935ec30353
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

Create `Assets/Scripts/Settings/Core/Game.Settings.Core.asmdef` (copied field-for-field from `Assets/Scripts/Match/Core/Game.Match.Core.asmdef`):

```json
{
    "name": "Game.Settings.Core",
    "rootNamespace": "Game.Settings.Core",
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

Create `Assets/Scripts/Settings/Core/Game.Settings.Core.asmdef.meta`:

```yaml
fileFormatVersion: 2
guid: 527b416b2f8a4cf6b151dd69688e66d7
AssemblyDefinitionImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

Create `Assets/Tests/EditMode/Settings/Game.Settings.Core.Tests.asmdef` (mirrors `Assets/Tests/EditMode/Hud/Game.Hud.Tests.asmdef`):

```json
{
    "name": "Game.Settings.Core.Tests",
    "rootNamespace": "",
    "references": [
        "Game.Settings.Core",
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

Create `Assets/Tests/EditMode/Settings/Game.Settings.Core.Tests.asmdef.meta`:

```yaml
fileFormatVersion: 2
guid: 16cca3180a034e5cb97f351a41182a5b
AssemblyDefinitionImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

- [ ] **Step 2: Write the failing tests**

Create `Assets/Tests/EditMode/Settings/VolumeCurveTests.cs`:

```csharp
using NUnit.Framework;
using Game.Settings.Core;

public class VolumeCurveTests
{
    [Test]
    public void FullVolumeIsZeroDecibels()
    {
        Assert.AreEqual(0f, VolumeCurve.LinearToDecibels(1f), 1e-4f);
    }

    [Test]
    public void HalfVolumeIsAboutMinusSixDecibels()
    {
        Assert.AreEqual(-6.0206f, VolumeCurve.LinearToDecibels(0.5f), 1e-3f);
    }

    [Test]
    public void FloorLinearIsMinusEighty()
    {
        Assert.AreEqual(-80f, VolumeCurve.LinearToDecibels(0.0001f), 1e-3f);
    }

    [Test]
    public void ZeroIsSilent()
    {
        Assert.AreEqual(-80f, VolumeCurve.LinearToDecibels(0f), 1e-4f);
    }

    [Test]
    public void NegativeIsSilentNotNaN()
    {
        Assert.AreEqual(-80f, VolumeCurve.LinearToDecibels(-0.5f), 1e-4f);
    }

    [Test]
    public void BelowFloorClampsToMinusEighty()
    {
        Assert.AreEqual(-80f, VolumeCurve.LinearToDecibels(0.00000001f), 1e-3f);
    }
}
```

Create `Assets/Tests/EditMode/Settings/SettingsMigrationTests.cs`:

```csharp
using NUnit.Framework;
using Game.Settings.Core;

public class SettingsMigrationTests
{
    [Test]
    public void MatchingVersionNeedsNoAction()
    {
        Assert.AreEqual(SettingsMigrationAction.None, SettingsMigration.Resolve(1, 1));
    }

    [Test]
    public void MissingVersionResetsToDefaults()
    {
        // A store written before settings existed reads back 0 from PlayerPrefs.
        Assert.AreEqual(SettingsMigrationAction.ResetToDefaults, SettingsMigration.Resolve(0, 1));
    }

    [Test]
    public void FutureVersionResetsToDefaults()
    {
        Assert.AreEqual(SettingsMigrationAction.ResetToDefaults, SettingsMigration.Resolve(99, 1));
    }

    [Test]
    public void CorruptNegativeVersionResetsToDefaults()
    {
        Assert.AreEqual(SettingsMigrationAction.ResetToDefaults, SettingsMigration.Resolve(-7, 1));
    }
}
```

Create `Assets/Tests/EditMode/Settings/SettingsCatalogTests.cs`:

```csharp
using NUnit.Framework;
using Game.Settings.Core;

public class SettingsCatalogTests
{
    [Test]
    public void VolumeClampsIntoZeroOne()
    {
        Assert.AreEqual(0f, SettingsCatalog.ClampVolume(-2f), 1e-4f);
        Assert.AreEqual(1f, SettingsCatalog.ClampVolume(5f), 1e-4f);
        Assert.AreEqual(0.42f, SettingsCatalog.ClampVolume(0.42f), 1e-4f);
    }

    [Test]
    public void CameraShakeClampsIntoZeroTwo()
    {
        Assert.AreEqual(0f, SettingsCatalog.ClampCameraShake(-1f), 1e-4f);
        Assert.AreEqual(2f, SettingsCatalog.ClampCameraShake(9f), 1e-4f);
        Assert.AreEqual(1f, SettingsCatalog.ClampCameraShake(1f), 1e-4f);
    }

    [Test]
    public void VSyncIsNormalisedToZeroOrOne()
    {
        Assert.AreEqual(0, SettingsCatalog.ClampVSync(0));
        Assert.AreEqual(1, SettingsCatalog.ClampVSync(1));
        Assert.AreEqual(1, SettingsCatalog.ClampVSync(4));
        Assert.AreEqual(1, SettingsCatalog.ClampVSync(-3));
    }

    [Test]
    public void FpsCapZeroMeansUncappedAndSurvivesClamping()
    {
        Assert.AreEqual(0, SettingsCatalog.ClampFpsCap(0));
        Assert.AreEqual(0, SettingsCatalog.ClampFpsCap(-120));
    }

    [Test]
    public void FpsCapBelowMinimumIsRaisedNotZeroed()
    {
        // A 3fps cap would be indistinguishable from a hang; the floor is a usability guard.
        Assert.AreEqual(30, SettingsCatalog.ClampFpsCap(3));
        Assert.AreEqual(30, SettingsCatalog.ClampFpsCap(29));
    }

    [Test]
    public void FpsCapIsBounded()
    {
        Assert.AreEqual(144, SettingsCatalog.ClampFpsCap(144));
        Assert.AreEqual(1000, SettingsCatalog.ClampFpsCap(99999));
    }

    [Test]
    public void DisplayModeOutOfEnumRangeFallsBackToDefault()
    {
        Assert.AreEqual(0, SettingsCatalog.ClampDisplayMode(0));
        Assert.AreEqual(3, SettingsCatalog.ClampDisplayMode(3));
        Assert.AreEqual(SettingsCatalog.DisplayModeDefault, SettingsCatalog.ClampDisplayMode(7));
        Assert.AreEqual(SettingsCatalog.DisplayModeDefault, SettingsCatalog.ClampDisplayMode(-1));
    }

    [Test]
    public void FlagIsNormalisedToZeroOrOne()
    {
        Assert.AreEqual(0, SettingsCatalog.ClampFlag(0));
        Assert.AreEqual(1, SettingsCatalog.ClampFlag(1));
        Assert.AreEqual(1, SettingsCatalog.ClampFlag(255));
    }

    [Test]
    public void AllKeysCoversEveryDeclaredKeyAndNothingElse()
    {
        // AllKeys drives the reset path. A key missing here survives a reset and silently
        // resurrects an old value; a stray key here could delete something else's data.
        CollectionAssert.AreEquivalent(
            new[]
            {
                SettingsCatalog.VersionKey,
                SettingsCatalog.MasterVolumeKey,
                SettingsCatalog.MusicVolumeKey,
                SettingsCatalog.SfxVolumeKey,
                SettingsCatalog.UiVolumeKey,
                SettingsCatalog.WidthKey,
                SettingsCatalog.HeightKey,
                SettingsCatalog.DisplayModeKey,
                SettingsCatalog.VSyncKey,
                SettingsCatalog.FpsCapKey,
                SettingsCatalog.CameraShakeKey,
                SettingsCatalog.DamageNumbersKey,
            },
            SettingsCatalog.AllKeys);
    }

    [Test]
    public void EveryKeyIsNamespacedUnderTheSettingsPrefix()
    {
        // Guards the reset path against ever deleting lobby.nickname or the identity token.
        foreach (string key in SettingsCatalog.AllKeys)
            Assert.IsTrue(key.StartsWith(SettingsCatalog.KeyPrefix), key + " is not namespaced");
    }
}
```

Create `Assets/Tests/EditMode/Settings/ResolutionListTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Game.Settings.Core;

public class ResolutionListTests
{
    private static List<ResolutionOption> Raw()
    {
        // Mirrors a real Screen.resolutions array: the same size repeated per refresh rate.
        return new List<ResolutionOption>
        {
            new ResolutionOption(1280, 720),
            new ResolutionOption(1920, 1080),
            new ResolutionOption(1920, 1080),
            new ResolutionOption(1920, 1080),
            new ResolutionOption(2560, 1440),
        };
    }

    [Test]
    public void DeduplicateCollapsesRefreshRateVariants()
    {
        List<ResolutionOption> result = ResolutionList.Deduplicate(Raw());
        Assert.AreEqual(3, result.Count);
    }

    [Test]
    public void DeduplicatePreservesFirstSeenOrder()
    {
        List<ResolutionOption> result = ResolutionList.Deduplicate(Raw());
        Assert.AreEqual(1280, result[0].Width);
        Assert.AreEqual(1920, result[1].Width);
        Assert.AreEqual(2560, result[2].Width);
    }

    [Test]
    public void DeduplicateDropsNonPositiveDimensions()
    {
        var raw = new List<ResolutionOption>
        {
            new ResolutionOption(0, 1080),
            new ResolutionOption(1920, 0),
            new ResolutionOption(-1, -1),
            new ResolutionOption(1920, 1080),
        };
        List<ResolutionOption> result = ResolutionList.Deduplicate(raw);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(1920, result[0].Width);
    }

    [Test]
    public void DeduplicateHandlesNull()
    {
        Assert.AreEqual(0, ResolutionList.Deduplicate(null).Count);
    }

    [Test]
    public void IndexOfFindsAMatchAndReportsAbsence()
    {
        List<ResolutionOption> options = ResolutionList.Deduplicate(Raw());
        Assert.AreEqual(1, ResolutionList.IndexOf(options, 1920, 1080));
        Assert.AreEqual(-1, ResolutionList.IndexOf(options, 800, 600));
    }

    [Test]
    public void StoredResolutionWinsWhenAvailable()
    {
        List<ResolutionOption> options = ResolutionList.Deduplicate(Raw());
        Assert.AreEqual(0, ResolutionList.ResolveStoredIndex(options, 1280, 720, 2560, 1440));
    }

    [Test]
    public void UnavailableStoredResolutionFallsBackToNative()
    {
        // The player unplugged the monitor their stored resolution belonged to.
        List<ResolutionOption> options = ResolutionList.Deduplicate(Raw());
        Assert.AreEqual(2, ResolutionList.ResolveStoredIndex(options, 3840, 2160, 2560, 1440));
    }

    [Test]
    public void NeitherStoredNorNativeAvailableFallsBackToLargestArea()
    {
        List<ResolutionOption> options = ResolutionList.Deduplicate(Raw());
        Assert.AreEqual(2, ResolutionList.ResolveStoredIndex(options, 3840, 2160, 5120, 2880));
    }

    [Test]
    public void EmptyOptionListYieldsNoIndex()
    {
        Assert.AreEqual(-1, ResolutionList.ResolveStoredIndex(new List<ResolutionOption>(), 1920, 1080, 1920, 1080));
        Assert.AreEqual(-1, ResolutionList.ResolveStoredIndex(null, 1920, 1080, 1920, 1080));
    }
}
```

Create a `.meta` for each of the four test files, using the `MonoImporter` template with these GUIDs:

| File | guid |
|---|---|
| `SettingsCatalogTests.cs.meta` | `de3fbac5f9b748f993c445d9f7c5e4fa` |
| `VolumeCurveTests.cs.meta` | `fe11c873b18144339ba5272a3da853f4` |
| `SettingsMigrationTests.cs.meta` | `8ee9763d9a6e42d8876dedbfcd919990` |
| `ResolutionListTests.cs.meta` | `98caff37f5734968a3633ef0dc3ac715` |

Template (substitute the guid):

```yaml
fileFormatVersion: 2
guid: PUT_GUID_HERE
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

- [ ] **Step 3: Run the harness to verify it fails**

Write a harness at `<scratchpad>/settings_harness/H.cs` mirroring every NUnit case above as a plain assertion (`if (!(cond)) { Console.WriteLine("FAIL: <name>"); f++; }`, `1e-4` float tolerance), returning `f == 0 ? 0 : 1` from `Main`. Compile it together with the four `Assets/Scripts/Settings/Core/*.cs` files against `netstandard 2.1` only:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.0f1/Editor/Data/NetCoreRuntime/dotnet.exe" exec "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Data\DotNetSdkRoslyn\csc.dll" @response.rsp
```

Expected at this point: **FAIL** — `error CS0246: The type or namespace name 'SettingsCatalog' could not be found` (and the same for `VolumeCurve`, `SettingsMigration`, `ResolutionList`), because the source files do not exist yet.

- [ ] **Step 4: Write `SettingsCatalog.cs`**

Create `Assets/Scripts/Settings/Core/SettingsCatalog.cs`:

```csharp
namespace Game.Settings.Core
{
    /// <summary>
    /// The single source of truth for every client-local setting: its PlayerPrefs key, its default,
    /// and its valid range. SettingsStore, the reset paths and the UI all read from here, so a
    /// default can never drift between "what boot applies" and "what Reset to Defaults writes".
    ///
    /// Engine-free on purpose (Game.Settings.Core has noEngineReferences) — no Mathf, no Screen.
    /// See docs/superpowers/specs/2026-07-29-options-settings-design.md.
    /// </summary>
    public static class SettingsCatalog
    {
        /// <summary>Bumped whenever a stored value's meaning changes. See SettingsMigration.</summary>
        public const int CurrentVersion = 1;

        /// <summary>
        /// Every settings key starts with this. The reset path deletes only keys under this prefix,
        /// so it can never touch lobby.nickname or the reconnection identity token, which live in
        /// the same per-product PlayerPrefs store.
        /// </summary>
        public const string KeyPrefix = "settings.";

        public const string VersionKey = "settings.version";

        public const string MasterVolumeKey = "settings.audio.master";
        public const string MusicVolumeKey = "settings.audio.music";
        public const string SfxVolumeKey = "settings.audio.sfx";
        public const string UiVolumeKey = "settings.audio.ui";

        public const string WidthKey = "settings.video.width";
        public const string HeightKey = "settings.video.height";
        public const string DisplayModeKey = "settings.video.displayMode";
        public const string VSyncKey = "settings.video.vsync";
        public const string FpsCapKey = "settings.video.fpsCap";

        public const string CameraShakeKey = "settings.gameplay.cameraShake";
        public const string DamageNumbersKey = "settings.gameplay.damageNumbers";

        public const float MasterVolumeDefault = 0.8f;
        public const float MusicVolumeDefault = 0.7f;
        public const float SfxVolumeDefault = 1.0f;
        public const float UiVolumeDefault = 1.0f;

        /// <summary>
        /// UnityEngine.FullScreenMode.FullScreenWindow == 1 (borderless). Written as a bare int
        /// because this assembly cannot reference UnityEngine; SettingsService casts it back.
        /// </summary>
        public const int DisplayModeDefault = 1;
        public const int DisplayModeMin = 0;
        public const int DisplayModeMax = 3;

        public const int VSyncDefault = 1;

        /// <summary>0 means uncapped (SettingsService translates it to targetFrameRate = -1).</summary>
        public const int FpsCapDefault = 0;
        public const int FpsCapFloor = 30;
        public const int FpsCapCeiling = 1000;

        public const float CameraShakeDefault = 1.0f;
        public const float CameraShakeMin = 0f;
        public const float CameraShakeMax = 2f;

        public const int DamageNumbersDefault = 1;

        /// <summary>
        /// Every key this feature owns. The reset path iterates exactly this array — a key missing
        /// from it survives a reset and silently resurrects a stale value.
        /// </summary>
        public static readonly string[] AllKeys =
        {
            VersionKey,
            MasterVolumeKey, MusicVolumeKey, SfxVolumeKey, UiVolumeKey,
            WidthKey, HeightKey, DisplayModeKey, VSyncKey, FpsCapKey,
            CameraShakeKey, DamageNumbersKey,
        };

        public static float ClampVolume(float value)
        {
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        public static float ClampCameraShake(float value)
        {
            if (value < CameraShakeMin) return CameraShakeMin;
            return value > CameraShakeMax ? CameraShakeMax : value;
        }

        public static int ClampVSync(int value)
        {
            return value == 0 ? 0 : 1;
        }

        /// <summary>
        /// 0 (uncapped) is preserved exactly; anything else is pulled into [30, 1000]. A 3fps cap
        /// would be indistinguishable from a hang, so the floor is a usability guard, not a
        /// hardware limit.
        /// </summary>
        public static int ClampFpsCap(int value)
        {
            if (value <= 0) return 0;
            if (value < FpsCapFloor) return FpsCapFloor;
            return value > FpsCapCeiling ? FpsCapCeiling : value;
        }

        public static int ClampDisplayMode(int value)
        {
            if (value < DisplayModeMin || value > DisplayModeMax) return DisplayModeDefault;
            return value;
        }

        public static int ClampFlag(int value)
        {
            return value == 0 ? 0 : 1;
        }
    }
}
```

- [ ] **Step 5: Write `VolumeCurve.cs`**

Create `Assets/Scripts/Settings/Core/VolumeCurve.cs`:

```csharp
using System;

namespace Game.Settings.Core
{
    /// <summary>
    /// Converts a linear 0-1 slider value into the decibel value an AudioMixer exposed parameter
    /// expects. Fixed here so the (not yet written) audio system and this menu cannot disagree.
    /// </summary>
    public static class VolumeCurve
    {
        /// <summary>Unity's AudioMixer treats -80 dB as silence.</summary>
        public const float MinDecibels = -80f;

        /// <summary>The linear value that maps exactly to MinDecibels.</summary>
        public const float MinLinear = 0.0001f;

        /// <summary>
        /// Zero and anything below the floor return MinDecibels directly rather than going through
        /// the log, so "slider at zero is silent" is a stated property, not a numeric coincidence.
        /// </summary>
        public static float LinearToDecibels(float linear)
        {
            if (linear <= MinLinear) return MinDecibels;

            float decibels = (float)(Math.Log10(linear) * 20.0);
            return decibels < MinDecibels ? MinDecibels : decibels;
        }
    }
}
```

- [ ] **Step 6: Write `SettingsMigration.cs`**

Create `Assets/Scripts/Settings/Core/SettingsMigration.cs`:

```csharp
namespace Game.Settings.Core
{
    public enum SettingsMigrationAction
    {
        /// <summary>The stored data matches the current version; read it as-is.</summary>
        None,

        /// <summary>Delete every settings key and re-write defaults.</summary>
        ResetToDefaults,
    }

    /// <summary>
    /// Decides what to do with a stored settings version. There are no migrations yet (version 1 is
    /// the first), so anything that is not an exact match re-defaults — including 0, which is what
    /// PlayerPrefs returns for a store written before settings existed.
    ///
    /// When a real migration is added later, this is the one place that branches on version.
    /// </summary>
    public static class SettingsMigration
    {
        public static SettingsMigrationAction Resolve(int storedVersion, int currentVersion)
        {
            return storedVersion == currentVersion
                ? SettingsMigrationAction.None
                : SettingsMigrationAction.ResetToDefaults;
        }
    }
}
```

- [ ] **Step 7: Write `ResolutionList.cs`**

Create `Assets/Scripts/Settings/Core/ResolutionList.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Game.Settings.Core
{
    /// <summary>A width x height pair, refresh rate deliberately excluded.</summary>
    public readonly struct ResolutionOption : IEquatable<ResolutionOption>
    {
        public readonly int Width;
        public readonly int Height;

        public ResolutionOption(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public bool Equals(ResolutionOption other) => Width == other.Width && Height == other.Height;
        public override bool Equals(object obj) => obj is ResolutionOption other && Equals(other);
        public override int GetHashCode() => (Width * 397) ^ Height;
        public override string ToString() => Width + " x " + Height;
    }

    /// <summary>
    /// Pure list handling for the resolution dropdown. Screen.resolutions contains one entry per
    /// refresh-rate variant, so a raw listing shows the same size several times; refresh rate is
    /// not a user-facing setting here, so the list is collapsed by size.
    /// </summary>
    public static class ResolutionList
    {
        public static List<ResolutionOption> Deduplicate(IReadOnlyList<ResolutionOption> raw)
        {
            var result = new List<ResolutionOption>();
            if (raw == null) return result;

            var seen = new HashSet<ResolutionOption>();
            for (int i = 0; i < raw.Count; i++)
            {
                ResolutionOption option = raw[i];
                if (option.Width <= 0 || option.Height <= 0) continue;
                if (!seen.Add(option)) continue;
                result.Add(option);
            }

            return result;
        }

        /// <summary>Index of the given size, or -1 when the list does not offer it.</summary>
        public static int IndexOf(IReadOnlyList<ResolutionOption> options, int width, int height)
        {
            if (options == null) return -1;

            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Width == width && options[i].Height == height) return i;
            }

            return -1;
        }

        /// <summary>
        /// Which entry the dropdown should select: the stored size when it is still offered,
        /// otherwise the display's native size, otherwise the largest available. The last fallback
        /// exists so an unrecognised display never lands the player on a postage-stamp window;
        /// in practice native is always enumerated. Returns -1 only for an empty list.
        /// </summary>
        public static int ResolveStoredIndex(
            IReadOnlyList<ResolutionOption> options,
            int storedWidth, int storedHeight,
            int nativeWidth, int nativeHeight)
        {
            if (options == null || options.Count == 0) return -1;

            int index = IndexOf(options, storedWidth, storedHeight);
            if (index >= 0) return index;

            index = IndexOf(options, nativeWidth, nativeHeight);
            if (index >= 0) return index;

            int best = 0;
            long bestArea = (long)options[0].Width * options[0].Height;
            for (int i = 1; i < options.Count; i++)
            {
                long area = (long)options[i].Width * options[i].Height;
                if (area <= bestArea) continue;
                bestArea = area;
                best = i;
            }

            return best;
        }
    }
}
```

Create a `.meta` for each of the four source files using the `MonoImporter` template from Step 2, with these GUIDs:

| File | guid |
|---|---|
| `SettingsCatalog.cs.meta` | `9c0048ff628048fb8a79930570749437` |
| `VolumeCurve.cs.meta` | `342d2a7ebd55433d9361a8cf1e6d609c` |
| `SettingsMigration.cs.meta` | `6632925c05724952bb43e81663ecacf5` |
| `ResolutionList.cs.meta` | `6763f9c6acd048f8932c165dc9b2a7ac` |

- [ ] **Step 8: Run the harness to verify it passes**

Re-run the Step 3 command.
Expected: exit code 0 and zero failed assertions. The count your harness prints must equal the number of NUnit assertions you mirrored (29 `[Test]` methods across the four files, containing more than 29 individual assertions — count them as you translate). Report the actual number; do not report a number you did not see printed.

- [ ] **Step 9: Commit**

```bash
git add "Assets/Scripts/Settings.meta" "Assets/Scripts/Settings/Core.meta" "Assets/Scripts/Settings/Core" "Assets/Tests/EditMode/Settings.meta" "Assets/Tests/EditMode/Settings"
git commit -m "feat(settings): pure settings logic in Game.Settings.Core

Key/default catalog, linear-to-dB volume curve, version-migration
decision, and resolution de-duplication, all engine-free and
unit-tested."
```

---

## Task 2: `SettingsStore` — cached PlayerPrefs accessors

**Files:**
- Create: `Assets/Scripts/Settings/SettingsStore.cs` (+ `.meta`, guid `3cd94c884e564c88900d18846280b31e`)

**Interfaces:**
- Consumes: `Game.Settings.Core.SettingsCatalog`, `SettingsMigration`, `SettingsMigrationAction`.
- Produces (all `static`, on `SettingsStore`):
  - `event System.Action Changed`
  - `float MasterVolume { get; set; }`, `MusicVolume`, `SfxVolume`, `UiVolume`
  - `int ResolutionWidth { get; }`, `int ResolutionHeight { get; }`, `void SetResolution(int width, int height)`
  - `int DisplayMode { get; set; }`, `int VSync { get; set; }`, `int FpsCap { get; set; }`
  - `float CameraShakeIntensity { get; set; }`
  - `bool ShowDamageNumbers { get; set; }`
  - `void EnsureLoaded()`
  - `void ResetAudioToDefaults()`, `ResetVideoToDefaults()`, `ResetGameplayToDefaults()`
  - `void NativeResolution(out int width, out int height)`

There is no NUnit test for this task: every method touches `PlayerPrefs`, which is process-global editor state — a test that wrote to it would corrupt the developer's real settings and the reconnection identity token. All the logic worth testing was extracted into `Game.Settings.Core` in Task 1, which is exactly why that assembly exists. Verification for this task is the compile gate plus the user's manual persistence check in Task 9.

- [ ] **Step 1: Write `SettingsStore.cs`**

Create `Assets/Scripts/Settings/SettingsStore.cs`:

```csharp
using System;
using Game.Settings.Core;
using UnityEngine;

/// <summary>
/// The one place raw PlayerPrefs is touched for client-local settings. Values are read once into
/// memory at first access, so a consumer reading a property every frame costs a field read rather
/// than a registry hit; writes update the cache, write through, and raise Changed.
///
/// CLIENT-LOCAL ONLY. Nothing here is networked, replicated, or read by simulation code — see the
/// networking-safety section of docs/superpowers/specs/2026-07-29-options-settings-design.md.
///
/// PlayerPrefs is per-PRODUCT, not per-process: multiple editor peers (MPPM) on one machine share
/// one settings store, exactly as they share one identity token (see PlayerIdentity.cs). Harmless
/// for a volume slider in a way it is not for identity.
/// </summary>
public static class SettingsStore
{
    /// <summary>Raised after any write. The settings UI uses it; gameplay consumers just read.</summary>
    public static event Action Changed;

    private static bool loaded;

    private static float masterVolume;
    private static float musicVolume;
    private static float sfxVolume;
    private static float uiVolume;

    private static int resolutionWidth;
    private static int resolutionHeight;
    private static int displayMode;
    private static int vsync;
    private static int fpsCap;

    private static float cameraShake;
    private static int damageNumbers;

    public static float MasterVolume
    {
        get { EnsureLoaded(); return masterVolume; }
        set { EnsureLoaded(); masterVolume = WriteFloat(SettingsCatalog.MasterVolumeKey, SettingsCatalog.ClampVolume(value)); }
    }

    public static float MusicVolume
    {
        get { EnsureLoaded(); return musicVolume; }
        set { EnsureLoaded(); musicVolume = WriteFloat(SettingsCatalog.MusicVolumeKey, SettingsCatalog.ClampVolume(value)); }
    }

    public static float SfxVolume
    {
        get { EnsureLoaded(); return sfxVolume; }
        set { EnsureLoaded(); sfxVolume = WriteFloat(SettingsCatalog.SfxVolumeKey, SettingsCatalog.ClampVolume(value)); }
    }

    public static float UiVolume
    {
        get { EnsureLoaded(); return uiVolume; }
        set { EnsureLoaded(); uiVolume = WriteFloat(SettingsCatalog.UiVolumeKey, SettingsCatalog.ClampVolume(value)); }
    }

    public static int ResolutionWidth { get { EnsureLoaded(); return resolutionWidth; } }

    public static int ResolutionHeight { get { EnsureLoaded(); return resolutionHeight; } }

    /// <summary>Width and height move together — a half-applied resolution is never valid.</summary>
    public static void SetResolution(int width, int height)
    {
        EnsureLoaded();
        if (width <= 0 || height <= 0) return;

        resolutionWidth = width;
        resolutionHeight = height;
        PlayerPrefs.SetInt(SettingsCatalog.WidthKey, width);
        PlayerPrefs.SetInt(SettingsCatalog.HeightKey, height);
        PlayerPrefs.Save();
        RaiseChanged();
    }

    public static int DisplayMode
    {
        get { EnsureLoaded(); return displayMode; }
        set { EnsureLoaded(); displayMode = WriteInt(SettingsCatalog.DisplayModeKey, SettingsCatalog.ClampDisplayMode(value)); }
    }

    public static int VSync
    {
        get { EnsureLoaded(); return vsync; }
        set { EnsureLoaded(); vsync = WriteInt(SettingsCatalog.VSyncKey, SettingsCatalog.ClampVSync(value)); }
    }

    public static int FpsCap
    {
        get { EnsureLoaded(); return fpsCap; }
        set { EnsureLoaded(); fpsCap = WriteInt(SettingsCatalog.FpsCapKey, SettingsCatalog.ClampFpsCap(value)); }
    }

    public static float CameraShakeIntensity
    {
        get { EnsureLoaded(); return cameraShake; }
        set { EnsureLoaded(); cameraShake = WriteFloat(SettingsCatalog.CameraShakeKey, SettingsCatalog.ClampCameraShake(value)); }
    }

    public static bool ShowDamageNumbers
    {
        get { EnsureLoaded(); return damageNumbers != 0; }
        set { EnsureLoaded(); damageNumbers = WriteInt(SettingsCatalog.DamageNumbersKey, value ? 1 : 0); }
    }

    /// <summary>
    /// The display's native size, with a fallback for a build that has no display at all (a
    /// headless server reports 0x0). SettingsService never applies video there, but the store must
    /// still return something sane if anything reads it.
    /// </summary>
    public static void NativeResolution(out int width, out int height)
    {
        Resolution current = Screen.currentResolution;
        width = current.width > 0 ? current.width : 1920;
        height = current.height > 0 ? current.height : 1080;
    }

    public static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;

        int storedVersion = PlayerPrefs.GetInt(SettingsCatalog.VersionKey, 0);
        if (SettingsMigration.Resolve(storedVersion, SettingsCatalog.CurrentVersion)
            == SettingsMigrationAction.ResetToDefaults)
        {
            DeleteAllSettingsKeys();
        }

        masterVolume = SettingsCatalog.ClampVolume(PlayerPrefs.GetFloat(SettingsCatalog.MasterVolumeKey, SettingsCatalog.MasterVolumeDefault));
        musicVolume = SettingsCatalog.ClampVolume(PlayerPrefs.GetFloat(SettingsCatalog.MusicVolumeKey, SettingsCatalog.MusicVolumeDefault));
        sfxVolume = SettingsCatalog.ClampVolume(PlayerPrefs.GetFloat(SettingsCatalog.SfxVolumeKey, SettingsCatalog.SfxVolumeDefault));
        uiVolume = SettingsCatalog.ClampVolume(PlayerPrefs.GetFloat(SettingsCatalog.UiVolumeKey, SettingsCatalog.UiVolumeDefault));

        NativeResolution(out int nativeWidth, out int nativeHeight);
        resolutionWidth = PlayerPrefs.GetInt(SettingsCatalog.WidthKey, nativeWidth);
        resolutionHeight = PlayerPrefs.GetInt(SettingsCatalog.HeightKey, nativeHeight);
        if (resolutionWidth <= 0 || resolutionHeight <= 0)
        {
            resolutionWidth = nativeWidth;
            resolutionHeight = nativeHeight;
        }

        displayMode = SettingsCatalog.ClampDisplayMode(PlayerPrefs.GetInt(SettingsCatalog.DisplayModeKey, SettingsCatalog.DisplayModeDefault));
        vsync = SettingsCatalog.ClampVSync(PlayerPrefs.GetInt(SettingsCatalog.VSyncKey, SettingsCatalog.VSyncDefault));
        fpsCap = SettingsCatalog.ClampFpsCap(PlayerPrefs.GetInt(SettingsCatalog.FpsCapKey, SettingsCatalog.FpsCapDefault));

        cameraShake = SettingsCatalog.ClampCameraShake(PlayerPrefs.GetFloat(SettingsCatalog.CameraShakeKey, SettingsCatalog.CameraShakeDefault));
        damageNumbers = SettingsCatalog.ClampFlag(PlayerPrefs.GetInt(SettingsCatalog.DamageNumbersKey, SettingsCatalog.DamageNumbersDefault));

        PlayerPrefs.SetInt(SettingsCatalog.VersionKey, SettingsCatalog.CurrentVersion);
        PlayerPrefs.Save();
    }

    public static void ResetAudioToDefaults()
    {
        EnsureLoaded();
        masterVolume = WriteFloat(SettingsCatalog.MasterVolumeKey, SettingsCatalog.MasterVolumeDefault);
        musicVolume = WriteFloat(SettingsCatalog.MusicVolumeKey, SettingsCatalog.MusicVolumeDefault);
        sfxVolume = WriteFloat(SettingsCatalog.SfxVolumeKey, SettingsCatalog.SfxVolumeDefault);
        uiVolume = WriteFloat(SettingsCatalog.UiVolumeKey, SettingsCatalog.UiVolumeDefault);
    }

    public static void ResetVideoToDefaults()
    {
        EnsureLoaded();
        NativeResolution(out int nativeWidth, out int nativeHeight);
        SetResolution(nativeWidth, nativeHeight);
        displayMode = WriteInt(SettingsCatalog.DisplayModeKey, SettingsCatalog.DisplayModeDefault);
        vsync = WriteInt(SettingsCatalog.VSyncKey, SettingsCatalog.VSyncDefault);
        fpsCap = WriteInt(SettingsCatalog.FpsCapKey, SettingsCatalog.FpsCapDefault);
    }

    public static void ResetGameplayToDefaults()
    {
        EnsureLoaded();
        cameraShake = WriteFloat(SettingsCatalog.CameraShakeKey, SettingsCatalog.CameraShakeDefault);
        damageNumbers = WriteInt(SettingsCatalog.DamageNumbersKey, SettingsCatalog.DamageNumbersDefault);
    }

    /// <summary>
    /// Deletes ONLY the keys this feature owns. Never PlayerPrefs.DeleteAll() — lobby.nickname and
    /// the reconnection identity token share this store and must survive a settings reset.
    /// </summary>
    private static void DeleteAllSettingsKeys()
    {
        foreach (string key in SettingsCatalog.AllKeys)
        {
            PlayerPrefs.DeleteKey(key);
        }

        PlayerPrefs.Save();
    }

    private static float WriteFloat(string key, float value)
    {
        PlayerPrefs.SetFloat(key, value);
        PlayerPrefs.Save();
        RaiseChanged();
        return value;
    }

    private static int WriteInt(string key, int value)
    {
        PlayerPrefs.SetInt(key, value);
        PlayerPrefs.Save();
        RaiseChanged();
        return value;
    }

    private static void RaiseChanged()
    {
        Action handler = Changed;
        if (handler != null) handler();
    }
}
```

Create `Assets/Scripts/Settings/SettingsStore.cs.meta` with guid `3cd94c884e564c88900d18846280b31e` using the `MonoImporter` template.

- [ ] **Step 2: Run the whole-surface compile gate**

Build the `@response.rsp` described in "How to run tests", **compiling `Assets/Scripts/Settings/Core/*.cs` inline** (there is no `Library\ScriptAssemblies\Game.Settings.Core.dll` yet) and excluding `Assembly-CSharp.dll` from the references.

Expected: exit code 0, warning count equal to your recorded baseline (no new `CS0649` — `SettingsStore` has no `[SerializeField]`s).

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Settings/SettingsStore.cs" "Assets/Scripts/Settings/SettingsStore.cs.meta"
git commit -m "feat(settings): SettingsStore, cached PlayerPrefs accessors

Loads once with version migration, clamps every read, writes through
on set. Reset paths delete only settings.* keys so lobby.nickname and
the identity token survive."
```

---

## Task 3: `SettingsService` — engine apply, boot hook, headless guard

**Files:**
- Create: `Assets/Scripts/Settings/SettingsService.cs` (+ `.meta`, guid `426bf68e29a5446ea95c89c4ee553669`)

**Interfaces:**
- Consumes: `SettingsStore` (Task 2), `Game.Settings.Core.VolumeCurve`.
- Produces (all `static`, on `SettingsService`):
  - `UnityEngine.Audio.AudioMixer Mixer { get; set; }` — null today; the future audio system assigns it.
  - `const string MasterVolumeParam = "MasterVolume"` (and `MusicVolumeParam`, `SfxVolumeParam`, `UiVolumeParam`)
  - `bool HasDisplay { get; }`
  - `void ApplyAll()`, `void ApplyAudio()`, `void ApplyVideo()`
  - `void ApplyDisplayPreview(int width, int height, int displayMode)` — applies without persisting; used by the confirm-or-revert flow in Task 7.

- [ ] **Step 1: Write `SettingsService.cs`**

Create `Assets/Scripts/Settings/SettingsService.cs`:

```csharp
using Game.Settings.Core;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;

/// <summary>
/// Pushes stored client-local settings into the engine. Runs once before the first scene loads, so
/// the first rendered frame is already at the chosen resolution / vsync / cap — no visible flash of
/// the wrong mode, and no dependency on any scene object's Awake order.
///
/// Every entry point is a no-op on a build with no graphics device (the dedicated server), which is
/// what makes this whole subsystem client-only by construction rather than by convention.
///
/// Nothing here is networked. See docs/superpowers/specs/2026-07-29-options-settings-design.md.
/// </summary>
public static class SettingsService
{
    /// <summary>
    /// Assigned by the audio system when it ships; null until then, which is why the four volume
    /// sliders currently persist but are inaudible. Not a bug — see the spec's audio section.
    /// </summary>
    public static AudioMixer Mixer { get; set; }

    // The exposed-parameter names the future AudioMixer must publish. Fixed by the spec so both
    // sides agree without either having shipped yet.
    public const string MasterVolumeParam = "MasterVolume";
    public const string MusicVolumeParam = "MusicVolume";
    public const string SfxVolumeParam = "SfxVolume";
    public const string UiVolumeParam = "UiVolume";

    public static bool HasDisplay => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyAtBoot()
    {
        ApplyAll();
    }

    public static void ApplyAll()
    {
        if (!HasDisplay) return;

        SettingsStore.EnsureLoaded();
        ApplyVideo();
        ApplyAudio();
    }

    public static void ApplyVideo()
    {
        if (!HasDisplay) return;

        SettingsStore.EnsureLoaded();
        Screen.SetResolution(
            SettingsStore.ResolutionWidth,
            SettingsStore.ResolutionHeight,
            (FullScreenMode)SettingsStore.DisplayMode);

        QualitySettings.vSyncCount = SettingsStore.VSync;

        // Unity's "uncapped" sentinel is -1, not 0.
        Application.targetFrameRate = SettingsStore.FpsCap <= 0 ? -1 : SettingsStore.FpsCap;
    }

    /// <summary>
    /// Applies a resolution/mode WITHOUT persisting it. The video settings UI uses this so a change
    /// that leaves the window unusable is never written to disk before the player confirms it.
    /// </summary>
    public static void ApplyDisplayPreview(int width, int height, int displayMode)
    {
        if (!HasDisplay) return;
        if (width <= 0 || height <= 0) return;

        Screen.SetResolution(width, height, (FullScreenMode)SettingsCatalog.ClampDisplayMode(displayMode));
    }

    public static void ApplyAudio()
    {
        if (!HasDisplay) return;
        if (Mixer == null) return;

        SettingsStore.EnsureLoaded();
        Mixer.SetFloat(MasterVolumeParam, VolumeCurve.LinearToDecibels(SettingsStore.MasterVolume));
        Mixer.SetFloat(MusicVolumeParam, VolumeCurve.LinearToDecibels(SettingsStore.MusicVolume));
        Mixer.SetFloat(SfxVolumeParam, VolumeCurve.LinearToDecibels(SettingsStore.SfxVolume));
        Mixer.SetFloat(UiVolumeParam, VolumeCurve.LinearToDecibels(SettingsStore.UiVolume));
    }
}
```

Create `Assets/Scripts/Settings/SettingsService.cs.meta` with guid `426bf68e29a5446ea95c89c4ee553669` using the `MonoImporter` template.

- [ ] **Step 2: Run the whole-surface compile gate**

Same command as Task 2 Step 2.
Expected: exit code 0, warning count unchanged from baseline.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/Settings/SettingsService.cs" "Assets/Scripts/Settings/SettingsService.cs.meta"
git commit -m "feat(settings): SettingsService applies settings before first scene load

RuntimeInitializeOnLoadMethod(BeforeSceneLoad) so resolution/vsync/cap
are set before the first frame renders. No-ops entirely when there is
no graphics device, keeping the dedicated-server build clean."
```

---

## Task 4: Prune `GameSettingsManager` and `CombatConfig`

**Files:**
- Modify: `Assets/Scripts/ScriptableObjects/Game Settings Manager.cs`
- Modify: `Assets/Scripts/ScriptableObjects/CombatConfig.cs:43-45`

**Interfaces:**
- Consumes: nothing.
- Produces: `GameSettingsManager` keeps exactly `combatConfig`, `difficultyRingConfig`, `matchTimeLimit`, `suddenDeathHardCap`, `Instance`, `GetCombatConfig()`, `GetDifficultyRingConfig()`. `CombatConfig.showDamageNumbers` no longer exists.

- [ ] **Step 1: Verify every field being deleted has zero consumers**

```bash
cd "C:/Users/1/Documents/GitHub/2dGame" && grep -rn "respawnTimeMultiplier\|GetRespawnTime\|enemyHealthMultiplier\|enemyDamageMultiplier\|enemySpawnRateMultiplier\|goldMultiplier\|experienceMultiplier\|autoRespawn\|showMinimap\|showDamageNumbers\|cameraShakeIntensity" --include=*.cs Assets/
```

Expected: the ONLY hits are the declaration lines inside `Assets/Scripts/ScriptableObjects/Game Settings Manager.cs` and `Assets/Scripts/ScriptableObjects/CombatConfig.cs`. If any other file appears, **stop** — the spec's premise that these are dead is wrong for that field, and the plan needs revisiting before deleting it.

Note `Assets/Scripts/Enemy/Base/Enemy.cs` and `Assets/Scripts/Player/PlayerCombat.cs` reference `GameSettingsManager.Instance.GetCombatConfig()` / `GetDifficultyRingConfig()` — those methods are **kept**, so those files must not need changes. `Assets/Scripts/Match/MatchManager.cs:115-123` reads `matchTimeLimit` / `suddenDeathHardCap` — also kept.

- [ ] **Step 2: Rewrite `Game Settings Manager.cs`**

Replace the entire contents of `Assets/Scripts/ScriptableObjects/Game Settings Manager.cs` with:

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

    [Header("Enemy Difficulty")]
    [Tooltip("Concentric difficulty rings applied to enemies by distance from center.")]
    [SerializeField] private DifficultyRingConfig difficultyRingConfig;

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

    /// <summary>
    /// Get the shared enemy difficulty ring configuration (may be null if unassigned).
    /// </summary>
    public DifficultyRingConfig GetDifficultyRingConfig()
    {
        return difficultyRingConfig;
    }
}
```

- [ ] **Step 3: Delete `showDamageNumbers` from `CombatConfig`**

In `Assets/Scripts/ScriptableObjects/CombatConfig.cs`, delete these three lines (currently lines 44-46, the `[Tooltip]`, the field, and the following blank line), leaving the `[Header("Visual Feedback")]` and `damageNumberPrefab` in place:

```csharp
    [Tooltip("Show damage numbers")]
    public bool showDamageNumbers = true;

```

The result reads:

```csharp
    [Header("Visual Feedback")]
    [Tooltip("Damage number prefab")]
    public GameObject damageNumberPrefab;
```

- [ ] **Step 4: Re-run the grep to confirm the fields are gone**

```bash
cd "C:/Users/1/Documents/GitHub/2dGame" && grep -rn "respawnTimeMultiplier\|GetRespawnTime\|enemyHealthMultiplier\|enemyDamageMultiplier\|enemySpawnRateMultiplier\|goldMultiplier\|experienceMultiplier\|autoRespawn\|showMinimap\|showDamageNumbers\|cameraShakeIntensity" --include=*.cs Assets/
```

Expected: **no output at all** (exit code 1 from grep).

- [ ] **Step 5: Run the whole-surface compile gate**

Same command as Task 2 Step 2.
Expected: exit code 0, **warning count unchanged**. Every deleted field was `public` with an initializer, so none of them was producing a `CS0649` ("never assigned") to begin with — deleting them removes source lines, not warnings. If the count moves in either direction, investigate before continuing rather than adopting the new number as a baseline.

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/ScriptableObjects/Game Settings Manager.cs" "Assets/Scripts/ScriptableObjects/CombatConfig.cs"
git commit -m "refactor(settings): prune dead knobs from GameSettingsManager

Deletes 7 zero-consumer fields (enemy/gold/xp multipliers,
respawnTimeMultiplier plus its uncalled GetRespawnTime, autoRespawn,
showMinimap) and moves the 2 client-local ones out. Also drops
CombatConfig.showDamageNumbers, likewise declared but never read.
Keeps combatConfig, difficultyRingConfig, matchTimeLimit and
suddenDeathHardCap, which are live."
```

---

## Task 5: Wire the two gameplay consumers

**Files:**
- Modify: `Assets/Scripts/Player/PlayerCameraShakeHandler.cs:118-154`
- Modify: `Assets/Scripts/Player/HitFeedback.cs:45`

**Interfaces:**
- Consumes: `SettingsStore.CameraShakeIntensity`, `SettingsStore.ShowDamageNumbers` (Task 2).
- Produces: nothing new.

Both call sites are already client-local: `PlayerCameraShakeHandler` early-outs unless `Object.HasInputAuthority` (`:68`), and `HitFeedback.Play` is invoked only from `InputAuthority`-targeted RPC handlers. Reading a local preference in either place cannot desync anything.

- [ ] **Step 1: Scale camera shake by the setting**

In `Assets/Scripts/Player/PlayerCameraShakeHandler.cs`, replace the body of `TriggerShakeFromDamage` (currently lines 118-138) with:

```csharp
    private void TriggerShakeFromDamage(float damageAmount)
    {
        if (playerCamera == null)
            return;

        // Client-local preference. At 0 we skip the call entirely rather than requesting a
        // zero-amplitude shake, so the camera never enters its shake state at all.
        float userScale = SettingsStore.CameraShakeIntensity;
        if (userScale <= 0f)
            return;

        // Calculate shake intensity based on damage
        float intensity = baseShakeIntensity + (damageAmount * damageToIntensityMultiplier);
        intensity = Mathf.Clamp(intensity, baseShakeIntensity, maxShakeIntensity);

        // The user scale is applied AFTER the authored clamp, so maxShakeIntensity still bounds the
        // default (1.0) case while a 2.0 preference can deliberately exceed it.
        intensity *= userScale;

        // Calculate shake duration
        float duration = baseShakeDuration;
        if (scaleDurationWithDamage)
        {
            duration += damageAmount * 0.02f; // +0.02s per damage point
            duration = Mathf.Clamp(duration, baseShakeDuration, baseShakeDuration * 2f);
        }

        // Trigger the shake
        playerCamera.TriggerShake(intensity, duration);

    }
```

**Leave `TriggerShakeManual` (lines 143-154) exactly as it is.** It has zero callers repo-wide, and the spec names only the damage path as this setting's consumer — wiring a preference into an entry point nothing calls is speculative. When something starts calling it, that change carries the scale with it.

- [ ] **Step 2: Gate damage numbers on the setting**

In `Assets/Scripts/Player/HitFeedback.cs`, change line 45 from:

```csharp
        if (damageNumberPrefab != null)
```

to:

```csharp
        // Client-local preference: hide the floating numbers only. The particle burst and the
        // target hit-flash below are unaffected -- this is not a "disable hit feedback" toggle.
        if (damageNumberPrefab != null && SettingsStore.ShowDamageNumbers)
```

- [ ] **Step 3: Run the whole-surface compile gate**

Same command as Task 2 Step 2.
Expected: exit code 0, warning count matching the baseline confirmed in Task 4.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/Player/PlayerCameraShakeHandler.cs" "Assets/Scripts/Player/HitFeedback.cs"
git commit -m "feat(settings): wire camera shake and damage numbers to SettingsStore

Both call sites were already local-only (HasInputAuthority-gated shake,
attacker-only HitFeedback), so reading a client preference there cannot
desync anything."
```

---

## Task 6: `SettingsPanel` — lifecycle, tabs, Audio and Gameplay

**Files:**
- Create: `Assets/Scripts/UI/SettingsPanel.cs` (+ `.meta`, guid `3a6819f254944881a4b13e360aceda7c`)

**Interfaces:**
- Consumes: `SettingsStore` (Task 2), `SettingsService.ApplyAudio()` (Task 3), `VideoSettingsSection` (Task 7 — declare the serialized field now; the type lands in Task 7, so **implement Task 7 before compiling this one**, or compile both together).
- Produces:
  - `SettingsPanel.Open(System.Action onClosed)` — shows the panel and calls `onClosed` when it closes.
  - `SettingsPanel.Close()` — safe to call when already closed.

**Ordering note:** `SettingsPanel` references `VideoSettingsSection` and vice versa is not true (the section does not know the panel), but this file will not compile until Task 7's file exists. Write Task 6 and Task 7 back to back and run the compile gate once, at the end of Task 7.

**Critical scene constraint** (repeated in the Task 9 guide): the `SettingsPanel` **component must live on a GameObject that stays active**, with `panelRoot` pointing at a *child* window object. If the component sat on the object it deactivates, `Awake` would never have run the first time the panel is shown.

- [ ] **Step 1: Write `SettingsPanel.cs`**

Create `Assets/Scripts/UI/SettingsPanel.cs`:

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The client-local options window: three tabs (Audio / Video / Gameplay), apply-on-change, and a
/// Reset to Defaults per tab. Video lives in its own component (VideoSettingsSection) because it is
/// the only tab with real state — a pending value and a confirm countdown.
///
/// NOTHING here writes networked or simulation state. It changes local audio, local window/present
/// settings, and two local cosmetic reads. See
/// docs/superpowers/specs/2026-07-29-options-settings-design.md.
///
/// SCENE REQUIREMENT: put this component on an object that stays ACTIVE and point panelRoot at a
/// child window object. If the component sat on the object it deactivates, Awake would never run.
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    [Header("Root")]
    [Tooltip("The window object toggled on/off. Must NOT be this component's own GameObject.")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button closeButton;

    [Header("Tabs")]
    [SerializeField] private Button audioTabButton;
    [SerializeField] private Button videoTabButton;
    [SerializeField] private Button gameplayTabButton;
    [SerializeField] private GameObject audioTab;
    [SerializeField] private GameObject videoTab;
    [SerializeField] private GameObject gameplayTab;

    [Header("Audio")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider uiSlider;
    [SerializeField] private TMP_Text masterValueLabel;
    [SerializeField] private TMP_Text musicValueLabel;
    [SerializeField] private TMP_Text sfxValueLabel;
    [SerializeField] private TMP_Text uiValueLabel;
    [SerializeField] private Button audioResetButton;

    [Header("Gameplay")]
    [SerializeField] private Slider cameraShakeSlider;
    [SerializeField] private TMP_Text cameraShakeValueLabel;
    [SerializeField] private Toggle damageNumbersToggle;
    [SerializeField] private Button gameplayResetButton;

    [Header("Video")]
    [SerializeField] private VideoSettingsSection video;

    private Action onClosed;

    // Set while the UI is writing its own controls, so onValueChanged callbacks triggered by
    // RefreshFromStore do not write straight back into the store.
    private bool suppressCallbacks;

    private void Awake()
    {
        if (panelRoot == null)
        {
            Debug.LogError("❌ SettingsPanel: panelRoot not assigned!");
            return;
        }

        if (panelRoot == gameObject)
        {
            Debug.LogError("❌ SettingsPanel: panelRoot must be a CHILD window object, not this " +
                           "component's own GameObject — deactivating self would stop Awake from " +
                           "ever having run.");
            return;
        }

        if (closeButton != null) closeButton.onClick.AddListener(Close);

        if (audioTabButton != null) audioTabButton.onClick.AddListener(() => ShowTab(0));
        if (videoTabButton != null) videoTabButton.onClick.AddListener(() => ShowTab(1));
        if (gameplayTabButton != null) gameplayTabButton.onClick.AddListener(() => ShowTab(2));

        WireVolumeSlider(masterSlider, masterValueLabel, v => SettingsStore.MasterVolume = v);
        WireVolumeSlider(musicSlider, musicValueLabel, v => SettingsStore.MusicVolume = v);
        WireVolumeSlider(sfxSlider, sfxValueLabel, v => SettingsStore.SfxVolume = v);
        WireVolumeSlider(uiSlider, uiValueLabel, v => SettingsStore.UiVolume = v);

        if (cameraShakeSlider != null)
        {
            cameraShakeSlider.minValue = Game.Settings.Core.SettingsCatalog.CameraShakeMin;
            cameraShakeSlider.maxValue = Game.Settings.Core.SettingsCatalog.CameraShakeMax;
            cameraShakeSlider.wholeNumbers = false;
            cameraShakeSlider.onValueChanged.AddListener(value =>
            {
                if (suppressCallbacks) return;
                SettingsStore.CameraShakeIntensity = value;
                SetPercentLabel(cameraShakeValueLabel, value);
            });
        }

        if (damageNumbersToggle != null)
        {
            damageNumbersToggle.onValueChanged.AddListener(value =>
            {
                if (suppressCallbacks) return;
                SettingsStore.ShowDamageNumbers = value;
            });
        }

        if (audioResetButton != null)
        {
            audioResetButton.onClick.AddListener(() =>
            {
                SettingsStore.ResetAudioToDefaults();
                SettingsService.ApplyAudio();
                RefreshFromStore();
            });
        }

        if (gameplayResetButton != null)
        {
            gameplayResetButton.onClick.AddListener(() =>
            {
                SettingsStore.ResetGameplayToDefaults();
                RefreshFromStore();
            });
        }

        panelRoot.SetActive(false);
    }

    /// <summary>Show the window. onClosed fires when it closes, so the caller can restore itself.</summary>
    public void Open(Action closedCallback)
    {
        if (panelRoot == null) return;

        onClosed = closedCallback;
        RefreshFromStore();
        if (video != null) video.RefreshFromStore();
        ShowTab(0);
        panelRoot.SetActive(true);
    }

    /// <summary>Hide the window. Safe to call when it is already closed.</summary>
    public void Close()
    {
        if (panelRoot == null || !panelRoot.activeSelf) return;

        // Leaving a pending resolution unconfirmed must revert it, not strand the player in it.
        if (video != null) video.CancelPendingConfirm();

        panelRoot.SetActive(false);

        Action callback = onClosed;
        onClosed = null;
        if (callback != null) callback();
    }

    private void ShowTab(int index)
    {
        if (audioTab != null) audioTab.SetActive(index == 0);
        if (videoTab != null) videoTab.SetActive(index == 1);
        if (gameplayTab != null) gameplayTab.SetActive(index == 2);
    }

    private void WireVolumeSlider(Slider slider, TMP_Text label, Action<float> write)
    {
        if (slider == null) return;

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.onValueChanged.AddListener(value =>
        {
            if (suppressCallbacks) return;
            write(value);
            SettingsService.ApplyAudio();
            SetPercentLabel(label, value);
        });
    }

    private void RefreshFromStore()
    {
        suppressCallbacks = true;

        SetSlider(masterSlider, masterValueLabel, SettingsStore.MasterVolume);
        SetSlider(musicSlider, musicValueLabel, SettingsStore.MusicVolume);
        SetSlider(sfxSlider, sfxValueLabel, SettingsStore.SfxVolume);
        SetSlider(uiSlider, uiValueLabel, SettingsStore.UiVolume);
        SetSlider(cameraShakeSlider, cameraShakeValueLabel, SettingsStore.CameraShakeIntensity);

        if (damageNumbersToggle != null)
            damageNumbersToggle.SetIsOnWithoutNotify(SettingsStore.ShowDamageNumbers);

        suppressCallbacks = false;
    }

    private static void SetSlider(Slider slider, TMP_Text label, float value)
    {
        if (slider == null) return;
        slider.SetValueWithoutNotify(value);
        SetPercentLabel(label, value);
    }

    private static void SetPercentLabel(TMP_Text label, float value)
    {
        if (label == null) return;
        label.text = Mathf.RoundToInt(value * 100f) + "%";
    }
}
```

Create `Assets/Scripts/UI/SettingsPanel.cs.meta` with guid `3a6819f254944881a4b13e360aceda7c` using the `MonoImporter` template.

- [ ] **Step 2: Do not compile or commit yet**

This file references `VideoSettingsSection`, which Task 7 creates. Proceed straight to Task 7 and run the compile gate there.

---

## Task 7: `VideoSettingsSection` — resolution, display mode, vsync, fps cap, confirm-or-revert

**Files:**
- Create: `Assets/Scripts/UI/VideoSettingsSection.cs` (+ `.meta`, guid `f6f33289b9c54afb9dac92082b92656e`)

**Interfaces:**
- Consumes: `SettingsStore` (Task 2), `SettingsService.ApplyVideo()` / `ApplyDisplayPreview(int, int, int)` (Task 3), `Game.Settings.Core.ResolutionOption` / `ResolutionList` / `SettingsCatalog` (Task 1).
- Produces:
  - `VideoSettingsSection.RefreshFromStore()` — repopulates every control from the store; called by `SettingsPanel.Open`.
  - `VideoSettingsSection.CancelPendingConfirm()` — reverts an unconfirmed display change; called by `SettingsPanel.Close`.

- [ ] **Step 1: Write `VideoSettingsSection.cs`**

Create `Assets/Scripts/UI/VideoSettingsSection.cs`:

```csharp
using System.Collections.Generic;
using Game.Settings.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Video tab. Resolution and display mode apply immediately but are only PERSISTED once the
/// player confirms — they are the only settings that can leave the window unusable, off-screen or
/// on an unsupported mode, a state the player cannot fix from inside the game and which
/// apply-on-change would otherwise have already written to disk. VSync and the framerate cap are
/// plain apply-on-change.
///
/// Purely local. Nothing here is networked.
/// </summary>
public class VideoSettingsSection : MonoBehaviour
{
    [Header("Controls")]
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private TMP_Dropdown displayModeDropdown;
    [SerializeField] private Toggle vsyncToggle;
    [SerializeField] private TMP_Dropdown fpsCapDropdown;
    [SerializeField] private Button videoResetButton;

    [Header("Confirm prompt")]
    [SerializeField] private GameObject confirmPanel;
    [SerializeField] private TMP_Text confirmCountdownLabel;
    [SerializeField] private Button confirmKeepButton;
    [SerializeField] private Button confirmRevertButton;
    [Tooltip("Seconds before an unconfirmed resolution/display change reverts itself.")]
    [SerializeField] private float confirmSeconds = 10f;

    // Offered display modes. MaximizedWindow is deliberately omitted — on Windows it behaves like
    // Windowed, so listing it would present a distinction that does not exist for our players.
    private static readonly FullScreenMode[] DisplayModes =
    {
        FullScreenMode.FullScreenWindow,
        FullScreenMode.ExclusiveFullScreen,
        FullScreenMode.Windowed,
    };

    private static readonly string[] DisplayModeLabels =
    {
        "Fullscreen (Borderless)",
        "Fullscreen (Exclusive)",
        "Windowed",
    };

    // 0 = uncapped, then the common desktop refresh rates.
    private static readonly int[] FpsCapOptions = { 0, 30, 60, 120, 144, 240 };

    private readonly List<ResolutionOption> resolutions = new List<ResolutionOption>();

    private bool suppressCallbacks;

    private bool awaitingConfirm;
    private float confirmRemaining;
    private int previousWidth, previousHeight, previousDisplayMode;

    private void Awake()
    {
        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);

        if (displayModeDropdown != null)
            displayModeDropdown.onValueChanged.AddListener(OnDisplayModeChanged);

        if (vsyncToggle != null)
            vsyncToggle.onValueChanged.AddListener(OnVSyncChanged);

        if (fpsCapDropdown != null)
            fpsCapDropdown.onValueChanged.AddListener(OnFpsCapChanged);

        if (videoResetButton != null)
        {
            videoResetButton.onClick.AddListener(() =>
            {
                CancelPendingConfirm();
                SettingsStore.ResetVideoToDefaults();
                SettingsService.ApplyVideo();
                RefreshFromStore();
            });
        }

        if (confirmKeepButton != null) confirmKeepButton.onClick.AddListener(KeepPendingDisplay);
        if (confirmRevertButton != null) confirmRevertButton.onClick.AddListener(CancelPendingConfirm);

        if (confirmPanel != null) confirmPanel.SetActive(false);
    }

    private void Update()
    {
        if (!awaitingConfirm) return;

        confirmRemaining -= Time.unscaledDeltaTime;

        if (confirmCountdownLabel != null)
        {
            int seconds = Mathf.Max(0, Mathf.CeilToInt(confirmRemaining));
            confirmCountdownLabel.text = "Keep these display settings? Reverting in " + seconds + "s";
        }

        if (confirmRemaining <= 0f) CancelPendingConfirm();
    }

    /// <summary>Repopulate every control from the store. Called whenever the panel opens.</summary>
    public void RefreshFromStore()
    {
        suppressCallbacks = true;

        BuildResolutionOptions();
        BuildDisplayModeOptions();
        BuildFpsCapOptions();

        if (vsyncToggle != null) vsyncToggle.SetIsOnWithoutNotify(SettingsStore.VSync != 0);
        ApplyVSyncInteractability();

        suppressCallbacks = false;
    }

    /// <summary>
    /// Revert an unconfirmed display change. Safe to call when nothing is pending — the panel calls
    /// it unconditionally on close so a player cannot escape the prompt by closing the window.
    /// </summary>
    public void CancelPendingConfirm()
    {
        if (!awaitingConfirm)
        {
            if (confirmPanel != null) confirmPanel.SetActive(false);
            return;
        }

        awaitingConfirm = false;
        if (confirmPanel != null) confirmPanel.SetActive(false);

        SettingsService.ApplyDisplayPreview(previousWidth, previousHeight, previousDisplayMode);
        RefreshFromStore();
    }

    private void KeepPendingDisplay()
    {
        if (!awaitingConfirm) return;

        awaitingConfirm = false;
        if (confirmPanel != null) confirmPanel.SetActive(false);

        // Only now does the new value reach disk.
        int index = resolutionDropdown != null ? resolutionDropdown.value : -1;
        if (index >= 0 && index < resolutions.Count)
            SettingsStore.SetResolution(resolutions[index].Width, resolutions[index].Height);

        int modeIndex = displayModeDropdown != null ? displayModeDropdown.value : -1;
        if (modeIndex >= 0 && modeIndex < DisplayModes.Length)
            SettingsStore.DisplayMode = (int)DisplayModes[modeIndex];
    }

    private void OnResolutionChanged(int index)
    {
        if (suppressCallbacks) return;
        if (index < 0 || index >= resolutions.Count) return;

        BeginConfirm(resolutions[index].Width, resolutions[index].Height, CurrentDropdownDisplayMode());
    }

    private void OnDisplayModeChanged(int index)
    {
        if (suppressCallbacks) return;
        if (index < 0 || index >= DisplayModes.Length) return;

        int resolutionIndex = resolutionDropdown != null ? resolutionDropdown.value : -1;
        int width = resolutionIndex >= 0 && resolutionIndex < resolutions.Count
            ? resolutions[resolutionIndex].Width
            : SettingsStore.ResolutionWidth;
        int height = resolutionIndex >= 0 && resolutionIndex < resolutions.Count
            ? resolutions[resolutionIndex].Height
            : SettingsStore.ResolutionHeight;

        BeginConfirm(width, height, (int)DisplayModes[index]);
    }

    /// <summary>
    /// Apply the new display state without persisting it, and start the revert countdown. If a
    /// confirm was already pending, the ORIGINAL previous value is kept as the revert target, so
    /// two changes in a row still fall back to a known-good state rather than to the intermediate
    /// one the player may never have been able to see.
    /// </summary>
    private void BeginConfirm(int width, int height, int displayMode)
    {
        if (!awaitingConfirm)
        {
            previousWidth = SettingsStore.ResolutionWidth;
            previousHeight = SettingsStore.ResolutionHeight;
            previousDisplayMode = SettingsStore.DisplayMode;
        }

        awaitingConfirm = true;
        confirmRemaining = confirmSeconds;

        SettingsService.ApplyDisplayPreview(width, height, displayMode);

        if (confirmPanel != null) confirmPanel.SetActive(true);
    }

    private void OnVSyncChanged(bool on)
    {
        if (suppressCallbacks) return;

        SettingsStore.VSync = on ? 1 : 0;
        SettingsService.ApplyVideo();
        ApplyVSyncInteractability();
    }

    private void OnFpsCapChanged(int index)
    {
        if (suppressCallbacks) return;
        if (index < 0 || index >= FpsCapOptions.Length) return;

        SettingsStore.FpsCap = FpsCapOptions[index];
        SettingsService.ApplyVideo();
    }

    /// <summary>
    /// With vSyncCount > 0 Unity ignores targetFrameRate on desktop, so the cap control is disabled
    /// rather than left presenting a setting that silently does nothing. The stored value is
    /// untouched and comes back when VSync is turned off.
    /// </summary>
    private void ApplyVSyncInteractability()
    {
        if (fpsCapDropdown != null) fpsCapDropdown.interactable = SettingsStore.VSync == 0;
    }

    private void BuildResolutionOptions()
    {
        if (resolutionDropdown == null) return;

        var raw = new List<ResolutionOption>();
        Resolution[] available = Screen.resolutions;
        for (int i = 0; i < available.Length; i++)
            raw.Add(new ResolutionOption(available[i].width, available[i].height));

        resolutions.Clear();
        resolutions.AddRange(ResolutionList.Deduplicate(raw));

        // A platform that enumerates nothing must still offer the current size.
        if (resolutions.Count == 0)
            resolutions.Add(new ResolutionOption(SettingsStore.ResolutionWidth, SettingsStore.ResolutionHeight));

        var labels = new List<string>(resolutions.Count);
        for (int i = 0; i < resolutions.Count; i++) labels.Add(resolutions[i].ToString());

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(labels);

        SettingsStore.NativeResolution(out int nativeWidth, out int nativeHeight);
        int selected = ResolutionList.ResolveStoredIndex(
            resolutions,
            SettingsStore.ResolutionWidth, SettingsStore.ResolutionHeight,
            nativeWidth, nativeHeight);

        resolutionDropdown.SetValueWithoutNotify(Mathf.Max(0, selected));
        resolutionDropdown.RefreshShownValue();
    }

    private void BuildDisplayModeOptions()
    {
        if (displayModeDropdown == null) return;

        displayModeDropdown.ClearOptions();
        displayModeDropdown.AddOptions(new List<string>(DisplayModeLabels));

        int stored = SettingsStore.DisplayMode;
        int selected = 0;
        for (int i = 0; i < DisplayModes.Length; i++)
        {
            if ((int)DisplayModes[i] != stored) continue;
            selected = i;
            break;
        }

        displayModeDropdown.SetValueWithoutNotify(selected);
        displayModeDropdown.RefreshShownValue();
    }

    private void BuildFpsCapOptions()
    {
        if (fpsCapDropdown == null) return;

        var labels = new List<string>(FpsCapOptions.Length);
        for (int i = 0; i < FpsCapOptions.Length; i++)
            labels.Add(FpsCapOptions[i] == 0 ? "Uncapped" : FpsCapOptions[i] + " FPS");

        fpsCapDropdown.ClearOptions();
        fpsCapDropdown.AddOptions(labels);

        int stored = SettingsStore.FpsCap;
        int selected = 0;
        for (int i = 0; i < FpsCapOptions.Length; i++)
        {
            if (FpsCapOptions[i] != stored) continue;
            selected = i;
            break;
        }

        fpsCapDropdown.SetValueWithoutNotify(selected);
        fpsCapDropdown.RefreshShownValue();
    }

    private int CurrentDropdownDisplayMode()
    {
        if (displayModeDropdown == null) return SettingsStore.DisplayMode;

        int index = displayModeDropdown.value;
        if (index < 0 || index >= DisplayModes.Length) return SettingsStore.DisplayMode;
        return (int)DisplayModes[index];
    }
}
```

Create `Assets/Scripts/UI/VideoSettingsSection.cs.meta` with guid `f6f33289b9c54afb9dac92082b92656e` using the `MonoImporter` template.

- [ ] **Step 2: Run the whole-surface compile gate**

Same command as Task 2 Step 2. This is the first gate that includes `SettingsPanel.cs` from Task 6.
Expected: exit code 0. New `CS0649` warnings are expected and legitimate — every `[SerializeField]` in `SettingsPanel` and `VideoSettingsSection` is assigned in the Unity Inspector, not in code. Count them and attribute each to a specific field; the total increase should equal the number of new serialized fields **without an initializer** — 22 on `SettingsPanel` plus 9 on `VideoSettingsSection` = **31**. `confirmSeconds` has an initializer (`= 10f`) and therefore raises no warning.

- [ ] **Step 3: Commit both UI files together**

```bash
git add "Assets/Scripts/UI/SettingsPanel.cs" "Assets/Scripts/UI/SettingsPanel.cs.meta" "Assets/Scripts/UI/VideoSettingsSection.cs" "Assets/Scripts/UI/VideoSettingsSection.cs.meta"
git commit -m "feat(settings): options window UI

SettingsPanel owns tabs, audio and gameplay controls, and per-tab reset;
VideoSettingsSection owns resolution/display/vsync/fps plus the
confirm-or-auto-revert prompt that keeps an unusable display mode from
ever reaching disk."
```

---

## Task 8: Entry points from the main menu and the lobby

**Files:**
- Modify: `Assets/Scripts/UI/MainMenuUI.cs`
- Modify: `Assets/Scripts/UI/LobbyScreenUI.cs`

**Interfaces:**
- Consumes: `SettingsPanel.Open(Action)`, `SettingsPanel.Close()` (Task 6).
- Produces: nothing new.

Both screens live in `MainMenu.unity` and share one `SettingsPanel` instance. Without the lobby entry point, a player who has already joined cannot reach settings without disconnecting.

- [ ] **Step 1: Add the Options button to `MainMenuUI`**

In `Assets/Scripts/UI/MainMenuUI.cs`, add to the `[Header("UI References")]` block, after `statusText` (line 17):

```csharp
    [SerializeField] private Button optionsButton;

    [Header("Settings")]
    [Tooltip("Shared with LobbyScreenUI — one SettingsPanel instance serves both screens.")]
    [SerializeField] private SettingsPanel settingsPanel;
```

In `Start()`, after the `hostButton` wiring (line 44), add:

```csharp
        if (optionsButton != null) optionsButton.onClick.AddListener(OpenSettings);
```

Add these methods after `Connect` (i.e. after line 73):

```csharp
    /// <summary>
    /// Client-local settings only — nothing here touches the runner or any networked state, so it
    /// is safe to open at any point on this screen.
    /// </summary>
    private void OpenSettings()
    {
        if (settingsPanel == null)
        {
            Debug.LogError("❌ MainMenuUI: settingsPanel not assigned!");
            return;
        }

        if (menuPanel != null) menuPanel.SetActive(false);
        settingsPanel.Open(() =>
        {
            if (menuPanel != null) menuPanel.SetActive(true);
        });
    }
```

Change `Show()` (lines 84-88) to close the settings window first, so a connect failure that re-shows the menu cannot leave both visible at once:

```csharp
    public void Show()
    {
        // A connect failure can call Show() while the options window is open; close it so the two
        // panels never stack.
        if (settingsPanel != null) settingsPanel.Close();
        if (menuPanel != null) menuPanel.SetActive(true);
        SetBusy(false);
    }
```

`SettingsPanel.Close()` fires the `onClosed` callback, which re-activates `menuPanel` — that runs before the `SetActive(true)` below it, so the end state is correct either way.

- [ ] **Step 2: Add the Options button to `LobbyScreenUI`**

In `Assets/Scripts/UI/LobbyScreenUI.cs`, add to the `[Header("Panel")]` block, after `statusText`:

```csharp
    [SerializeField] private Button optionsButton;

    [Tooltip("Shared with MainMenuUI — one SettingsPanel instance serves both screens.")]
    [SerializeField] private SettingsPanel settingsPanel;
```

In `Start()`, after the `loadoutToggleButton` wiring, add:

```csharp
        if (optionsButton != null) optionsButton.onClick.AddListener(OpenSettings);
```

Add this method after `Hide()`:

```csharp
    /// <summary>
    /// Client-local settings only. Opening this does not touch the runner, the roster, or any
    /// networked state — the lobby connection keeps running behind it.
    /// </summary>
    private void OpenSettings()
    {
        if (settingsPanel == null)
        {
            Debug.LogError("❌ LobbyScreenUI: settingsPanel not assigned!");
            return;
        }

        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        settingsPanel.Open(() =>
        {
            if (lobbyPanel != null) lobbyPanel.SetActive(true);
        });
    }
```

And change `Hide()` so leaving the lobby cannot strand the options window on screen:

```csharp
    public void Hide()
    {
        if (settingsPanel != null) settingsPanel.Close();
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
    }
```

Note the ordering: `Close()` re-activates `lobbyPanel` via its callback, and the line after immediately deactivates it — end state correct.

- [ ] **Step 3: Run the whole-surface compile gate**

Same command as Task 2 Step 2.
Expected: exit code 0, plus 4 more `CS0649` (the two new `[SerializeField]`s on each screen).

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/UI/MainMenuUI.cs" "Assets/Scripts/UI/LobbyScreenUI.cs"
git commit -m "feat(settings): Options entry points on the main menu and the lobby

Both screens live in MainMenu.unity and share one SettingsPanel. The
lobby entry matters because a joined player otherwise has to disconnect
to reach settings. Show()/Hide() close the window so panels never stack."
```

---

## Task 9: Scene-wiring guide for the user

**Files:**
- Create: `docs/settings-menu-unity-setup-guide.md`

**Interfaces:**
- Consumes: every serialized field name from Tasks 6-8.
- Produces: nothing in code.

**Why a guide and not a one-click `Editor` builder** (the project has both patterns — `MatchHudBuilder`/`ScoreboardHudBuilder` vs `docs/scoreboard-unity-setup-guide.md`): the settings window's layout is a design surface the user will want to arrange by hand, and an implementer cannot run or verify a builder without the editor. A guide fails visibly and cheaply; an unverifiable builder fails silently and expensively.

- [ ] **Step 1: Write the guide**

Create `docs/settings-menu-unity-setup-guide.md` containing, in this order:

1. **Hierarchy to build** under the existing `MainMenu.unity` Canvas:

```
Canvas
└── SettingsRoot            (ACTIVE — SettingsPanel + VideoSettingsSection components go HERE)
    └── SettingsWindow      (this is panelRoot; starts inactive, toggled by the panel)
        ├── TabBar          (AudioTabButton, VideoTabButton, GameplayTabButton)
        ├── AudioTab        (4 sliders + 4 value labels + AudioResetButton)
        ├── VideoTab        (ResolutionDropdown, DisplayModeDropdown, VSyncToggle,
        │                    FpsCapDropdown, VideoResetButton)
        ├── GameplayTab     (CameraShakeSlider + label, DamageNumbersToggle,
        │                    GameplayResetButton)
        ├── ConfirmPanel    (inactive by default; countdown label, Keep button, Revert button)
        └── CloseButton
```

State the critical constraint prominently: **`SettingsPanel` must be on `SettingsRoot`, which stays active — `panelRoot` points at the `SettingsWindow` child.** If the component is put on the object it deactivates, `Awake` never runs and the window can never open. The component logs an explicit error if `panelRoot == gameObject`.

2. **Field-by-field wiring table**, each row: field name → what to drag in → required or optional.

`SettingsPanel` (22 serialized fields), in declaration order:
`panelRoot` (→ SettingsWindow, **required**), `closeButton`, `audioTabButton`, `videoTabButton`,
`gameplayTabButton`, `audioTab`, `videoTab`, `gameplayTab`, `masterSlider`, `musicSlider`,
`sfxSlider`, `uiSlider`, `masterValueLabel` *(optional)*, `musicValueLabel` *(optional)*,
`sfxValueLabel` *(optional)*, `uiValueLabel` *(optional)*,
`audioResetButton`, `cameraShakeSlider`, `cameraShakeValueLabel` *(optional)*,
`damageNumbersToggle`, `gameplayResetButton`, `video` (→ the `VideoSettingsSection` component on
SettingsRoot, **required**).

`VideoSettingsSection` (9 object references + one value), in declaration order:
`resolutionDropdown`, `displayModeDropdown`, `vsyncToggle`, `fpsCapDropdown`, `videoResetButton`,
`confirmPanel` (**required** — without it a pending change has no visible prompt),
`confirmCountdownLabel` *(optional)*, `confirmKeepButton`, `confirmRevertButton`, and
`confirmSeconds` (a number, default `10`).

3. **`MainMenuUI` and `LobbyScreenUI` wiring:** assign `optionsButton` and `settingsPanel` on each. Both `settingsPanel` fields point at the **same** `SettingsRoot` object.

4. **Manual verification checklist**, one line each:
   - Settings survive quitting and relaunching the game.
   - The game launches directly at the stored resolution with no visible flash of a different one.
   - Changing resolution shows the confirm prompt; ignoring it for 10s reverts, and the reverted-from value is **not** persisted (relaunch to confirm).
   - Closing the window while a confirm is pending reverts rather than keeping.
   - VSync on greys out the FPS cap dropdown; turning VSync off restores the previously chosen cap.
   - Camera shake at 0 produces no shake on taking damage; at 2.0 it is visibly stronger than at 1.0.
   - Damage numbers off still shows the particle burst and the target hit-flash.
   - The audio sliders persist and restore their positions across a restart despite being silent (expected — no `AudioMixer` exists yet).
   - The Options button works from both the main menu and the lobby, and opening it from the lobby does not disturb the connection or the roster.
   - Reset to Defaults on each tab restores that tab only, and the lobby nickname survives it.
   - A headless dedicated-server build starts cleanly with no display or audio calls attempted.

5. **Note on `Gameplay.unity`:** no deliberate change is needed. The pruned `GameSettingsManager` fields disappear from the scene YAML the next time the scene is saved; `matchTimeLimit` and `suddenDeathHardCap` keep their authored values.

- [ ] **Step 2: Commit**

```bash
git add docs/settings-menu-unity-setup-guide.md
git commit -m "docs(settings): Unity scene-wiring guide for the options menu"
```

- [ ] **Step 3: Report pending user verification**

Your final report must list, explicitly and separately:
- **Executed:** the Task 1 harness result (N/N assertions), and each compile-gate exit code.
- **Not executed:** the NUnit EditMode suite (user runs Test Runner), all scene wiring, and every item in the Task 9 manual checklist. Do not claim any of it.

---

## Notes for the reviewer

- **The audio sliders do nothing audible on delivery.** That is the spec's Decision 7, not an incomplete implementation. `SettingsService.Mixer` is null until the (unwritten) audio-system spec ships an `AudioMixer` exposing `MasterVolume` / `MusicVolume` / `SfxVolume` / `UiVolume`. `Mixer` is the one deliberately-unconsumed piece of surface on this branch (the pre-flight review kept it and cut the other two): without it there is no seam for the sliders to apply through and the spec decision is unimplementable.
- **No test covers `SettingsStore` or `SettingsService` directly.** Both are thin adapters over process-global engine state (`PlayerPrefs`, `Screen`, `QualitySettings`); a test writing to `PlayerPrefs` would corrupt the developer's real settings and reconnection identity token. Everything with a decision in it was deliberately pushed into `Game.Settings.Core`, which is fully tested.
- **`Screen.SetResolution` is called even when the stored values equal the current ones** (at boot, every launch). This is harmless — Unity no-ops an identical request — and keeping it unconditional means there is exactly one code path that establishes display state.
- **One known edge case, deliberately left alone.** `SettingsPanel` holds a single `onClosed`
  callback, so if the options window was opened from the lobby and `MainMenuUI.Show()` then fires
  (a mid-lobby disconnect), `Close()` runs the *lobby's* callback and re-shows `lobbyPanel` while
  `Show()` re-shows `menuPanel` — briefly both. Whether this is reachable depends on whether
  `GameNetworkManager` already calls `LobbyScreenUI.Hide()` on that path; check during manual
  verification. Fixing it speculatively (a callback stack, or a shared screen-manager) would add
  machinery for a case that may not exist, so confirm it first.
