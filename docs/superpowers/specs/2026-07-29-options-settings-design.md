# Options / Settings Menu — Design

**Date:** 2026-07-29
**Status:** Approved (design), no implementation plan authored
**Game:** Unity 6.3 Photon Fusion 2 2D PvPvE arena, Host/Client + dedicated server, ~20 players

## Problem

There is no settings menu of any kind, and the settings that *are* declared live in the wrong
place and are mostly dead.

**No UI exists.** [`UI/MainMenuUI.cs`](../../../Assets/Scripts/UI/MainMenuUI.cs) is a nickname
field plus Join/Host plus a status line. The only persisted preference in the entire project is
the nickname (`lobby.nickname`, [`MainMenuUI.cs:26`](../../../Assets/Scripts/UI/MainMenuUI.cs:26)).
There is no volume control, no resolution or display-mode control, no vsync or framerate control,
no camera-shake control, and no damage-numbers toggle anywhere in the game.

**`GameSettingsManager` mixes two incompatible concerns and is mostly unreferenced.**
[`ScriptableObjects/Game Settings Manager.cs`](../../../Assets/Scripts/ScriptableObjects/Game%20Settings%20Manager.cs)
is a `MonoBehaviour` holding both client-local preferences and host/match rules. Actual consumer
counts, from the code:

| Field | Consumers |
|---|---|
| `combatConfig`, `difficultyRingConfig` | Live — [`Enemy.cs:123,286`](../../../Assets/Scripts/Enemy/Base/Enemy.cs:123), [`PlayerCombat.cs:297,316`](../../../Assets/Scripts/Player/PlayerCombat.cs:297) |
| `matchTimeLimit`, `suddenDeathHardCap` | Live — [`MatchManager.cs:115-123`](../../../Assets/Scripts/Match/MatchManager.cs:115), server-side |
| `respawnTimeMultiplier` | Only `GetRespawnTime()`, which **nothing calls** |
| `enemyHealthMultiplier`, `enemyDamageMultiplier`, `enemySpawnRateMultiplier` | **Zero** |
| `goldMultiplier`, `experienceMultiplier` | **Zero** (there is no XP system) |
| `autoRespawn`, `showMinimap` | **Zero** (there is no minimap) |
| `cameraShakeIntensity`, `showDamageNumbers` | **Zero** |

**The client-local fields are on an object a settings menu cannot reach.** `GameSettingsManager` is
instantiated in `Gameplay.unity` only — it does not exist in `MainMenu.unity`. A main-menu options
screen could not read or write `cameraShakeIntensity` or `showDamageNumbers` even if it wanted to.
That is not an incidental detail; it is the structural reason these two knobs were never wired to
anything.

**The damage-numbers toggle has no consumer at either declaration site.**
[`CombatConfig.showDamageNumbers`](../../../Assets/Scripts/ScriptableObjects/CombatConfig.cs:45) is
also declared-but-never-read. The real damage-number spawn is the unconditional
`damageNumberPrefab` branch in [`HitFeedback.Play`](../../../Assets/Scripts/Player/HitFeedback.cs:45)
— which is already client-local and attacker-only, so it is exactly the right gate point.

**Gameplay input never touches the Input Actions asset.**
[`Player/NetworkInputProvider.cs`](../../../Assets/Scripts/Player/NetworkInputProvider.cs) reads
`Keyboard.current` / `Mouse.current` / `Gamepad.current` directly with hardcoded controls
(`aKey`, `spaceKey`, `leftShiftKey`, `leftButton`, …). The `Player` action map authored in
[`InputSystem_Actions.inputactions`](../../../Assets/InputSystem_Actions.inputactions) is **unused
by gameplay**; only the `UI` map is consumed (by
[`ScoreboardInputReader`](../../../Assets/Scripts/Hud/ScoreboardInputReader.cs)). This governs the
Controls decision below.

**Aim is absolute, not relative.**
[`NetworkInputProvider.cs:102-107`](../../../Assets/Scripts/Player/NetworkInputProvider.cs:102)
computes `AimWorldPoint` as `Camera.main.ScreenToWorldPoint(mouse.position)` — the crosshair *is*
the OS cursor. There is no mouse-delta aim path, and the gamepad has no aim stick at all (the left
stick drives movement; the right stick is unread). This also governs the Controls decision.

**There is no `AudioMixer` asset in the project.** Audio today is bare
`AudioSource.PlayClipAtPoint` ([`CoinPickup.cs:294`](../../../Assets/Scripts/Coin%20Scripts/CoinPickup.cs:294),
[`HomeBase.cs:256`](../../../Assets/Scripts/Coin%20Scripts/HomeBase.cs:256),
[`PlayerInventory.cs:113,159`](../../../Assets/Scripts/Coin%20Scripts/PlayerInventory.cs:113)) and
one `PlayOneShot` source on the player
([`PlayerAnimator.cs:235`](../../../Assets/Scripts/Player/PlayerAnimator.cs:235)) — none of which
any mixer currently routes. The audio-system spec this design was expected to pair with
(`2026-07-29-audio-system-design.md`) **has not been written**; it is a named forward dependency,
not an existing document.

**Nothing in the project touches display settings.** There is no call to `Screen.SetResolution`,
`Screen.fullScreenMode`, `QualitySettings.vSyncCount`, `QualitySettings.SetQualityLevel`, or
`Application.targetFrameRate` anywhere in `Assets/Scripts`. There is also no
`[RuntimeInitializeOnLoadMethod]` anywhere — boot-time application of settings is a new pattern
for this codebase.

## Decisions (from brainstorming)

| # | Decision |
|---|---|
| 1 | **Client-local preferences and host/match rules are separated by ownership**, not merely by menu section. The options menu owns only client-local preferences; match rules stay server-authoritative and host-authored. |
| 2 | **No Controls section in v1.** Key rebinding is out of scope because it requires first migrating `NetworkInputProvider` onto the action map; aim sensitivity is out of scope because absolute-cursor aim gives it nothing to scale. |
| 3 | **Prune the seven dead fields from `GameSettingsManager`; leave the two live match-rules fields where they are.** No lobby match-rules UI in this spec — only a statement of where one would belong. |
| 4 | **No in-match settings overlay in v1.** Settings are reachable from the main menu and the lobby only. The "a Fusion match cannot be paused" constraint is recorded as the *reason*, and as the contract any future overlay must honor. |
| 5 | **Video ships resolution, display mode, vsync, and framerate cap.** Quality level and brightness/gamma are cut — URP quality tiers gate shadows/LOD/AA that a flat 2D sprite scene barely uses, and brightness has no post-process to consume it. |
| 6 | **Persistence is `PlayerPrefs` behind a typed accessor layer**, matching the two existing `PlayerPrefs` users, with a `settings.version` int for migrations. No JSON file, no ScriptableObject-backed store. |
| 7 | **Four audio buses are defined now and are inert until an `AudioMixer` ships.** Sliders persist and apply to whatever mixer exists; with no mixer they are stored-but-silent. The exposed-parameter names are specified here as a contract on the future audio spec. |
| 8 | **Apply-on-change**, with per-tab Reset to Defaults. No Apply/Cancel buttons — except resolution and display mode, which get a confirm-or-auto-revert prompt because they are the only settings that can leave the window unusable. |
| 9 | **Settings apply before the first scene loads**, via `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`, so the first rendered frame is already at the chosen resolution/vsync/cap. |
| 10 | **The whole service no-ops on headless/dedicated-server builds.** A batch-mode server must never call `Screen.SetResolution` or touch audio. |

## Settings taxonomy & ownership

### CLIENT-LOCAL preferences — owned by this menu

Stored in `PlayerPrefs`, applied locally, never replicated, never read by simulation code:

- Audio: master, music, SFX, UI bus volumes
- Video: resolution, display mode, vsync, framerate cap
- Gameplay: camera-shake intensity, damage numbers
- (Pre-existing, unchanged: the lobby nickname, which stays owned by `MainMenuUI`)

### HOST / MATCH RULES — not owned by this menu

Server-authoritative, authored by whoever runs the host or dedicated server, consumed inside
`FixedUpdateNetwork` on the state authority:

- `matchTimeLimit` (minutes; 0 = no timer, capture is then the only end condition)
- `suddenDeathHardCap` (minutes; 0 = off; operations safety valve)

These stay as `SerializeField`s on `GameSettingsManager` in `Gameplay.unity`, read by
[`MatchManager`](../../../Assets/Scripts/Match/MatchManager.cs:115). **A client must never be able
to write them** — they determine when the match ends for everyone, so a client-side control over
them would be a client writing simulation state. If a host-facing UI for these is built later, it
belongs in the lobby screen ([`LobbyScreenUI`](../../../Assets/Scripts/UI/LobbyScreenUI.cs)),
host-only, delivered to the server over the existing reliable-data lobby protocol and applied
server-side — structurally the same shape as the existing team-choice and loadout handoffs, and
explicitly *not* part of this spec.

### `GameSettingsManager` prune & migration plan

| Field | Fate | Rationale |
|---|---|---|
| `combatConfig` | **Keep** | Live refs from `Enemy` and `PlayerCombat` |
| `difficultyRingConfig` | **Keep** | Live ref from `Enemy` |
| `matchTimeLimit` | **Keep** | Live in `MatchManager`; host/match rule |
| `suddenDeathHardCap` | **Keep** | Live in `MatchManager`; host/match rule |
| `cameraShakeIntensity` | **Delete** → `SettingsStore` | Zero consumers; client-local, and unreachable from `MainMenu.unity` where the menu lives |
| `showDamageNumbers` | **Delete** → `SettingsStore` | Zero consumers; client-local; same scene problem |
| `respawnTimeMultiplier` **and** `GetRespawnTime(TeamData)` | **Delete both** | The multiplier's only reader is the method, and the method has zero callers — deleting the field alone would leave a dead method behind |
| `enemyHealthMultiplier` | **Delete** | Zero consumers |
| `enemyDamageMultiplier` | **Delete** | Zero consumers |
| `enemySpawnRateMultiplier` | **Delete** | Zero consumers |
| `goldMultiplier` | **Delete** | Zero consumers; coin value is authored per-coin in the coin data assets |
| `experienceMultiplier` | **Delete** | Zero consumers; there is no XP system |
| `autoRespawn` | **Delete** | Zero consumers; respawn is server-driven, not opt-in |
| `showMinimap` | **Delete** | Zero consumers; there is no minimap |
| [`CombatConfig.showDamageNumbers`](../../../Assets/Scripts/ScriptableObjects/CombatConfig.cs:45) | **Delete** | Also declared-but-never-read; the toggle's real home is `SettingsStore`, gating `HitFeedback` |

After the prune, `GameSettingsManager` holds four fields — two config asset references and two
match rules — and is honestly a host/match-config holder rather than a grab-bag. Renaming it is
*not* part of this spec: it is referenced by GUID from `Gameplay.unity` and by name from three
scripts, and a rename buys nothing this spec needs.

Because every deleted field has zero consumers, the prune is a pure deletion with no call-site
migration. The only serialized-data consequence is that Unity drops the removed keys from the
`GameSettingsManager` component's YAML in `Gameplay.unity` on next save — no values are lost that
anything read.

## Settings catalog

All keys are namespaced under `settings.*`. `settings.version` (int, currently `1`) sits alongside
them and is written on first save.

### Audio

Volumes are stored as linear `0..1` — the value the slider shows. Conversion to decibels happens at
apply time, not at store time, so the stored value stays human-meaningful and re-readable.

| Setting | Key | Type | Default | Consumer |
|---|---|---|---|---|
| Master | `settings.audio.master` | float 0–1 | 0.8 | `AudioMixer` exposed param `MasterVolume` |
| Music | `settings.audio.music` | float 0–1 | 0.7 | exposed param `MusicVolume` |
| SFX | `settings.audio.sfx` | float 0–1 | 1.0 | exposed param `SfxVolume` |
| UI | `settings.audio.ui` | float 0–1 | 1.0 | exposed param `UiVolume` |

**These four are inert until an `AudioMixer` exists.** No mixer asset is in the project today, and
the bare `PlayClipAtPoint` / `PlayOneShot` calls listed under "Problem" route through no mixer
group, so nothing can attenuate them. The sliders still render, persist, and survive a restart;
they simply have no audible effect until the audio system lands. `SettingsService` holds an
optional `AudioMixer` reference and skips the apply step when it is null — no error, no warning
spam.

**Contract on the future audio spec.** For these sliders to become live, the audio system must
expose exactly four mixer parameters named `MasterVolume`, `MusicVolume`, `SfxVolume`,
`UiVolume`, and route every sound through one of the corresponding groups. The linear→dB curve is
fixed here so both sides agree:

```
dB = (v <= 0) ? -80 : Mathf.Log10(Mathf.Max(v, 0.0001f)) * 20
```

The explicit `v == 0 → -80` case matters: `Log10(0.0001) * 20` is `-80` anyway, but routing zero
through the clamp rather than through the log makes "slider at zero means silent" a stated
property rather than a numeric coincidence.

### Video

| Setting | Key | Type | Default | Consumer |
|---|---|---|---|---|
| Resolution | `settings.video.width`, `settings.video.height` | int, int | the display's native resolution, captured on first boot | `Screen.SetResolution(w, h, mode)` |
| Display mode | `settings.video.displayMode` | int (`FullScreenMode`) | `FullScreenWindow` (borderless) | `Screen.fullScreenMode` |
| VSync | `settings.video.vsync` | int 0/1 | 1 | `QualitySettings.vSyncCount` |
| Framerate cap | `settings.video.fpsCap` | int (0 = uncapped) | 0 | `Application.targetFrameRate` |

The resolution dropdown is populated from `Screen.resolutions`, de-duplicated by width×height
(the array contains one entry per refresh-rate variant, so a raw listing shows the same resolution
several times). Refresh rate is **not** a user-facing setting; `Screen.SetResolution` is called
with the default refresh rate for the chosen mode.

VSync and the framerate cap interact: with `vSyncCount > 0`, `Application.targetFrameRate` is
ignored on desktop targets. The UI therefore disables the framerate-cap control while VSync is on,
rather than presenting a setting that silently does nothing. Both values are still persisted
independently, so turning VSync off restores the previously chosen cap.

Quality level and brightness/gamma are cut (Decision 5). Quality tiers gate shadow cascades, LOD
bias, and anti-aliasing that a flat 2D sprite scene barely exercises; brightness would need a
post-process or overlay that does not exist.

### Gameplay

| Setting | Key | Type | Default | Consumer |
|---|---|---|---|---|
| Camera shake | `settings.gameplay.cameraShake` | float 0–2 | 1.0 | [`PlayerCameraShakeHandler`](../../../Assets/Scripts/Player/PlayerCameraShakeHandler.cs) |
| Damage numbers | `settings.gameplay.damageNumbers` | int 0/1 | 1 | [`HitFeedback.Play`](../../../Assets/Scripts/Player/HitFeedback.cs:37) |

**Camera shake.** `PlayerCameraShakeHandler.TriggerShakeFromDamage`
([`:118-138`](../../../Assets/Scripts/Player/PlayerCameraShakeHandler.cs:118)) computes an
intensity, clamps it, then calls `playerCamera.TriggerShake(intensity, duration)`. The setting
multiplies the computed intensity **after** the existing clamp, so the authored `maxShakeIntensity`
still bounds the 1.0 case while a 2.0 setting can deliberately exceed it. At exactly 0 the handler
skips the `TriggerShake` call entirely rather than passing a zero-amplitude shake, so no shake
state machine is entered at all. The handler is already local-only — it early-outs unless
`Object.HasInputAuthority`
([`:68`](../../../Assets/Scripts/Player/PlayerCameraShakeHandler.cs:68)) — so this changes nothing
about who sees what.

**Damage numbers.** `HitFeedback.Play` gates only its `damageNumberPrefab` branch on the setting.
The particle burst and the target hit-flash are unaffected; this is a "hide the floating numbers"
preference, not a "disable hit feedback" preference. `HitFeedback` is a scene singleton invoked
from `InputAuthority`-targeted RPC handlers, i.e. already attacker-only and client-local, so the
gate cannot desync anything.

## Persistence

### Storage: `PlayerPrefs` behind a typed layer

`PlayerPrefs` matches the two existing users — the lobby nickname
([`MainMenuUI.cs:26`](../../../Assets/Scripts/UI/MainMenuUI.cs:26)) and the reconnection identity
token ([`PlayerIdentity.cs`](../../../Assets/Scripts/PlayerIdentity.cs)) — and needs no file I/O,
no atomic-write path, and no new serialization format. Raw `PlayerPrefs` calls are never made from
UI or consumer code; everything goes through one static store.

**Known wart, inherited not introduced:** `PlayerPrefs` is per-*product*, not per-process. Multiple
editor peers (MPPM) on one machine share a single settings store, exactly as they share a single
identity token — a limitation already documented on `PlayerIdentity`
([`:59`](../../../Assets/Scripts/PlayerIdentity.cs:59)). This is acceptable for settings in a way
it is not for identity: two local peers sharing a volume slider is harmless.

### Components

Four small pieces, following the project's existing `Core`-assembly convention (`Game.Match.Core`,
`Game.Combat.Core`, `Game.Hud.Core`, …) of putting pure logic in an engine-free assembly with a
matching EditMode test assembly:

**`Assets/Scripts/Settings/Core/` (`Game.Settings.Core.asmdef`, engine-free)** — the pure parts,
unit-testable outside Unity per the project's bundled-Roslyn workaround:
- the linear→dB curve
- clamping and defaulting of out-of-range or corrupt stored values
- the version-migration decision (given a stored version and the current version, migrate or
  re-default)
- resolution-list de-duplication

**`Assets/Scripts/Settings/SettingsStore.cs`** — a static typed accessor over `PlayerPrefs`. Owns
every key string, every default, and `settings.version`. Reads are served from an in-memory cache
populated once at boot, so a consumer polling per-frame costs a field read, not a registry hit.
Writes update the cache, write through to `PlayerPrefs`, call `PlayerPrefs.Save()`, and raise a
single `Changed` event. Consumers (`PlayerCameraShakeHandler`, `HitFeedback`) read the cached
property directly; they do not subscribe, because both read at the moment of use anyway.

**`Assets/Scripts/Settings/SettingsService.cs`** — pushes stored values into the engine. Holds the
optional `AudioMixer` reference. Exposes `ApplyAll()`, plus per-category applies used by the panel
on change.

**`Assets/Scripts/UI/SettingsPanel.cs`** — the view. Binds sliders/dropdowns/toggles to
`SettingsStore`, applies on change, owns the Reset-to-Defaults buttons and the resolution confirm
prompt.

### Defaults, versioning, corruption

- **Missing key** → the default from the catalog above is returned and written on first save. No
  separate "first run" branch.
- **Out-of-range value** (a hand-edited registry key, or a resolution no longer offered by the
  current display) → clamped, or replaced by the default when clamping is meaningless (an
  unavailable resolution falls back to native).
- **`settings.version` mismatch** → if a migration exists for the stored version, apply it and
  rewrite; otherwise clear the `settings.*` keys and re-default. Version starts at `1`; no
  migrations exist yet, so any other stored value re-defaults. Only `settings.*` keys are ever
  cleared — `lobby.nickname` and the identity token are untouched.

### Boot ordering

`SettingsService` runs `ApplyAll()` from a
`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` hook. This is the
project's first use of that attribute, and it is the point of the design: `BeforeSceneLoad` runs
after the engine is up but before the first scene's objects exist, so `Screen.SetResolution`,
`QualitySettings.vSyncCount`, and `Application.targetFrameRate` are all set before the first frame
is rendered. There is no visible flash of the wrong resolution, and no dependency on a scene
object's `Awake` order.

The audio apply runs in the same call and no-ops while the mixer reference is null (i.e. always,
until the audio system ships). The two gameplay settings need no boot-time apply at all — their
consumers read the store at the moment of use.

**Headless guard.** `ApplyAll()` returns immediately when the build has no graphics device
(`SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null`, i.e. `-batchmode -nographics`). The
dedicated-server build must never call `Screen.SetResolution` or touch audio; it also has no
meaningful `PlayerPrefs` store. This makes the entire settings subsystem a client-only concern by
construction, not by convention.

## Menu surfaces & navigation

### Where it opens from

One `SettingsPanel` GameObject in `MainMenu.unity`, hidden by default, reachable from two places
in that same scene:

1. An **Options** button on `MainMenuUI`'s `menuPanel`, next to Join/Host.
2. An **Options** button on `LobbyScreenUI`. Without this, a player who has already joined the
   lobby cannot reach settings without disconnecting — and both screens live in `MainMenu.unity`,
   so this costs one button and one listener, not a second panel.

Opening the panel hides the underlying panel (`menuPanel` or `lobbyPanel`); closing restores it.
The panel holds no networked state and never calls into `GameNetworkManager`, so opening it while
the lobby is live cannot disturb the connection or the roster.

### No in-match overlay in v1

`Gameplay.unity` gets no pause/settings overlay in this version. The reasoning is worth recording
because it also constrains any future attempt:

**A Fusion networked match cannot be paused.** The `NetworkRunner` keeps ticking on the state
authority regardless of what any client's UI is doing. Concretely, a hypothetical in-match overlay
would halt **none** of the following:

- the simulation — `FixedUpdateNetwork` keeps running on every object
- the match timer and phase machine in [`MatchManager`](../../../Assets/Scripts/Match/MatchManager.cs)
- other players, AI enemies, projectiles, and flags
- incoming damage — the local avatar stays fully killable while the menu is open
- flag carry timers, coin lifetimes, and respawn countdowns

The only thing it could change is local rendering and audio config. That means the honest framing
of an in-match settings menu here is "adjust your volume while standing in the open getting shot,"
which is a worse experience than not having it — so v1 does without, and the decision is revisited
alongside a real spectator/dead-state surface where standing still is already the situation.

If it is built later, the contract is: it is a **local overlay only**. It must not gate
`NetworkInputProvider.OnInput`, must not touch `MatchManager.InputEnabled` (which exists to
suppress input during non-play phases, and is server-driven state, not a UI concern), and must not
introduce any RPC. Suppressing local input while it is open is a *local* choice with a real cost —
the avatar goes idle and vulnerable — and would need to be decided then, not assumed now.

### Apply model

**Apply-on-change** for every setting: moving a slider or flipping a toggle takes effect and
persists immediately. No Apply button, no Cancel, no pending-changes state to keep in sync with the
UI. Each tab has a **Reset to Defaults** button that rewrites only that tab's keys.

**One exception — resolution and display mode.** These apply immediately, then show a
confirm-or-revert prompt with a ~10-second countdown; if the player does not confirm, the previous
resolution and mode are restored. This exists because they are the only settings that can leave the
window unusable, off-screen, or on an unsupported mode — a state the player cannot fix from inside
the game, and which would otherwise persist across restarts precisely because apply-on-change
already saved it. The confirm therefore delays the *write*, not the apply: the new value is only
persisted once confirmed.

## Networking safety

**Nothing in this menu writes networked or simulation state.** Explicitly:

- No RPCs are added or called.
- No `[Networked]` property is read for a decision or written at all.
- No value from `SettingsStore` reaches `NetInput`. `NetworkInputProvider` remains the sole
  gameplay device-read site and is not modified by this spec — no sensitivity multiplier, no
  rebinding indirection, no settings lookup on the input path.
- No value from `SettingsStore` is read inside `FixedUpdateNetwork` by any system.
- Match rules (`matchTimeLimit`, `suddenDeathHardCap`) are not exposed, not readable, and not
  writable from this menu.

The complete set of things the menu touches:

| Touch point | Scope |
|---|---|
| `AudioMixer` exposed bus volumes | Local audio rendering (inert until the mixer ships) |
| `Screen.SetResolution`, `Screen.fullScreenMode` | Local window state |
| `QualitySettings.vSyncCount`, `Application.targetFrameRate` | Local presentation timing |
| `PlayerCameraShakeHandler` shake intensity | Local camera, already `HasInputAuthority`-gated |
| `HitFeedback` damage-number spawn | Local cosmetic VFX, already attacker-only via an `InputAuthority`-targeted RPC handler |

Two players with opposite settings produce identical simulation outcomes from identical inputs. A
player who sets camera shake to 0 and damage numbers off has no competitive advantage or
disadvantage — neither affects hit detection, damage, movement, or timing, all of which are
resolved server-side.

## Non-goals

- **Key rebinding.** Requires migrating `NetworkInputProvider` off direct device reads and onto the
  `Player` action map first — a refactor of the single file that feeds Fusion's `OnInput`, with
  real correctness risk around the tap-latch and held-state logic. `PerformInteractiveRebinding`
  rebinds *actions*, and gameplay currently has none. Named as the prerequisite, not attempted.
- **Aim sensitivity.** Nothing to scale: aim is the absolute cursor world position, and there is no
  gamepad aim stick. A slider would multiply nothing.
- **Any Controls tab at all**, including a read-only bindings reference card.
- **An in-match pause/settings overlay** in `Gameplay.unity` — see the reasoning above. The
  local-only contract for a future one is recorded; the surface is not built.
- **A host match-rules UI.** `matchTimeLimit` and `suddenDeathHardCap` stay as host-authored
  inspector fields. Where a UI would belong (host-only, in the lobby, over the existing reliable-
  data protocol) is stated; no lobby or networked work is done.
- **Renaming `GameSettingsManager`.** It is referenced by GUID from `Gameplay.unity` and by name
  from three scripts; the rename buys nothing this spec needs.
- **Quality level and brightness/gamma settings.**
- **Refresh-rate selection.** Resolutions are de-duplicated by width×height; the mode's default
  refresh rate is used.
- **Building the `AudioMixer`, mixer groups, or any audio routing.** This spec defines the settings
  surface, keys, defaults, curve, and exposed-parameter names; the audio system that consumes them
  is a separate, currently unwritten spec.
- **Migrating existing sounds off `PlayClipAtPoint`** onto mixer groups — same reason.
- **A settings UI for the lobby nickname.** It stays owned by `MainMenuUI` and its existing
  `lobby.nickname` key.
- **Cloud-synced or per-profile settings.** One local store per install.
- **Accessibility options** (colorblind palettes, UI scale, text size, reduced motion beyond the
  camera-shake slider).

## Resolved open questions

| Question | Resolution |
|---|---|
| Is key rebinding in scope for v1? | **No.** Gameplay input never touches the Input Actions asset — `NetworkInputProvider` reads devices directly with hardcoded controls, so there are no actions to rebind. Migrating it onto the action map is the named prerequisite. |
| Aim sensitivity, then? | **Also no**, and for an independent reason: aim is `ScreenToWorldPoint(mouse.position)` — absolute, not relative — and the gamepad has no aim stick. Nothing for a sensitivity value to multiply. |
| So what is in the Controls section? | **There is no Controls section in v1.** Menu ships Audio, Video, and Gameplay. |
| Do `matchTimeLimit` / `suddenDeathHardCap` / the enemy multipliers move out of the client menu? | They were never *in* a client menu (none existed). The two live match-rules fields stay as host-authored server-side config; the enemy/gold/XP multipliers are **deleted outright** — all have zero consumers. |
| Is host match-rules configuration in scope? | **Deferred.** This spec covers client settings only and states where a host rules UI belongs (lobby, host-only, over the existing reliable-data protocol). |
| Which `GameSettingsManager` fields get deleted? | Seven: `respawnTimeMultiplier` (plus its unused `GetRespawnTime` method), the three enemy multipliers, `goldMultiplier`, `experienceMultiplier`, `autoRespawn`, `showMinimap` — and the two client-local fields move to `SettingsStore`. Plus `CombatConfig.showDamageNumbers`, also declared-but-never-read. |
| In-match entry point — Esc pause overlay? | **No in-match menu in v1.** The "a Fusion match cannot be paused" constraint is the reason, not a caveat: the sim, timers, enemies, and incoming damage all keep running, so the overlay would mean adjusting volume while standing helpless. |
| Which video options matter for a 2D game? | **Resolution, display mode, vsync, framerate cap.** Quality level is cut — URP tiers gate shadows/LOD/AA a flat sprite scene barely uses. Brightness is cut — no post-process consumes it. |
| `PlayerPrefs` or a dedicated settings file/asset? | **`PlayerPrefs` behind a typed static store**, matching the two existing users (`lobby.nickname`, the identity token). No file I/O, no new serialization. Accepted wart: MPPM peers share one store. |
| How do audio sliders work with no `AudioMixer` in the project? | **Four buses defined now, inert until the mixer lands.** Keys, defaults, and the linear→dB curve are fixed here; the exposed-parameter names (`MasterVolume`, `MusicVolume`, `SfxVolume`, `UiVolume`) are a contract on the unwritten audio spec. `SettingsService` skips the apply when the mixer ref is null. |
| Was there an existing audio spec to pair with? | **No** — `2026-07-29-audio-system-design.md` does not exist. It is a forward dependency named here, not a document referenced. |
| Apply-on-change vs Apply/Cancel? | **Apply-on-change** plus per-tab Reset to Defaults, with one exception: resolution/display mode get a confirm-or-auto-revert prompt, since they are the only settings that can leave the window unusable and would otherwise persist that state across restarts. |
| When do settings apply at boot? | **`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`** — before the first scene loads, so the first rendered frame is already correct. First use of that attribute in this codebase. |
| What about the dedicated-server build? | **The whole service no-ops** when there is no graphics device. A headless server never calls `Screen.SetResolution` and never touches audio. |

## Verification notes

Per project convention the authoritative check is manual play, but the pure parts live in
`Game.Settings.Core` and are unit-testable outside Unity (bundled-Roslyn workaround):

- Linear→dB curve: `1.0 → 0 dB`, `0.5 → ~-6 dB`, `0.0001 → -80 dB`, and the explicit `0 → -80`
  case.
- Clamping/defaulting: out-of-range floats clamp to their declared range; a stored resolution not
  present in the current display's list falls back to native.
- Version migration: a stored version equal to current passes through untouched; any other value
  re-defaults, and only `settings.*` keys are cleared (`lobby.nickname` survives).
- Resolution de-duplication: an input list containing the same width×height at several refresh
  rates yields one entry per width×height.
- VSync/cap interaction: with vsync on, the cap control is disabled but its stored value is
  preserved and restored when vsync is turned off.

Manual play must confirm: settings survive an application restart; the game launches directly at
the stored resolution with no visible flash of a different one; the resolution confirm prompt
auto-reverts when ignored and the *unconfirmed* value is not persisted; camera shake at 0 produces
no shake at all and at 2.0 is visibly stronger than at 1.0; damage numbers off leaves the particle
burst and hit-flash intact; the audio sliders persist and restore their positions across a restart
despite being silent; the Options button works from both the main menu and the lobby without
disturbing an active lobby connection; and a headless dedicated-server build starts cleanly with no
display or audio calls attempted.
