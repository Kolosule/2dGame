# Audio System — Design

**Date:** 2026-07-29
**Status:** Approved (design), no implementation plan authored
**Game:** Unity 6.3 Photon Fusion 2 2D PvPvE arena, Host/Client + dedicated server, ~20 players

## Problem

The game has no audio system. It has fifteen scattered audio call sites, one copyrighted mp3, and
a settings contract waiting for a consumer that has never shipped.

**There is no mixer, no service, and no routing.** Every existing sound plays through
`AudioSource.PlayClipAtPoint`, which has three properties that make it unusable at this game's
scale: it ignores every `AudioMixerGroup` (so no volume slider can ever affect it), it allocates a
`GameObject` + `AudioSource` per call and `Destroy`s it on clip end (per-hit GC churn at 20
players), and it offers no concurrency control whatsoever. The five call sites are
[`CoinPickup.cs:294`](../../../Assets/Scripts/Coin%20Scripts/CoinPickup.cs:294),
[`HomeBase.cs:256`](../../../Assets/Scripts/Coin%20Scripts/HomeBase.cs:256), and
[`PlayerInventory.cs:113`](../../../Assets/Scripts/Coin%20Scripts/PlayerInventory.cs:113) /
[`:159`](../../../Assets/Scripts/Coin%20Scripts/PlayerInventory.cs:159). Two more use a per-player
`AudioSource.PlayOneShot` — [`PlayerAnimator.cs:235`](../../../Assets/Scripts/Player/PlayerAnimator.cs:235)
(jump) and [`:240`](../../../Assets/Scripts/Player/PlayerAnimator.cs:240) (land) — which at least
respects a mixer group, but only if one is assigned on the prefab, and none is.
[`CombatConfig.hitSound`](../../../Assets/Scripts/ScriptableObjects/CombatConfig.cs:51) is declared
and never read by anything.

**The settings side is already built and already blocked on this.**
[`SettingsService.cs:22`](../../../Assets/Scripts/Settings/SettingsService.cs:22) declares
`public static AudioMixer Mixer { get; set; }` with the comment *"Assigned by the audio system when
it ships; null until then, which is why the four volume sliders currently persist but are
inaudible."* The four exposed-parameter names are fixed by contract at
[`SettingsService.cs:26-29`](../../../Assets/Scripts/Settings/SettingsService.cs:26):
`MasterVolume`, `MusicVolume`, `SfxVolume`, `UiVolume`.
[`SettingsStore`](../../../Assets/Scripts/Settings/SettingsStore.cs) already persists all four to
PlayerPrefs with clamping, migration, defaults, and a deferred-flush contract, and
[`SettingsPanel.cs:116`](../../../Assets/Scripts/UI/SettingsPanel.cs:116) already calls
`SettingsService.ApplyAudio()` on every slider change. **The four sliders become audible the
instant `SettingsService.Mixer` is non-null.** This spec adds no settings code.

**There is exactly one sound file on disk:**
`Assets/Sound/Music/Halo Theme Song Original.mp3`. It is copyrighted, it is referenced by no
script, and its disposition is settled in decision 22 below.

**Nothing needs new detection.** The codebase is already event-driven and already
server-authoritative, with player-facing feedback delivered through RPCs and C# events rather than
polling. Every sound this game needs has an existing hook: `HitFeedback.Play` (driven by
`RPC_HitFeedback`, already targeted at `RpcTargets.InputAuthority`), `MatchManager.PhaseChanged`,
`Flag.OnStateChanged`, `PlayerBuffs.BuffsChanged` / `StealthStateChanged`,
`TeamScoreManager.ScoresChanged` / `TeamBuffsChanged`, `PlayerStatsHandler.HealthChanged`, and the
existing coin/deposit/impact RPCs. Audio subscribes. It detects nothing.

## Decisions (from brainstorming)

| # | Decision |
|---|---|
| 1 | **Audio direction is arcadey and punchy** — short, bright, heavily-compressed impacts; chiptune-adjacent coin and pickup chimes; strong tonal separation between event families so a 20-player scrum stays legible. Chosen over a grounded/realistic direction because it reads instantly at scale, forgives cheap source material, and matches the existing visual language (flat team colors, chevron markers, floating damage numbers, particle bursts). |
| 2 | **Assets come from royalty-free / CC0 packs** — Kenney.nl (CC0, arcade-flavored, no attribution burden) as the primary source, freesound.org CC0 for gaps, CC0 or CC-BY loops for music. **Licensing rule: CC0 or explicit commercial license only.** Every acquired asset's source and license is recorded in `Assets/Sound/LICENSES.md`, one row per file. No AI-generated placeholders, no asset-store purchase, no commissioning in v1. |
| 3 | **World SFX are positional with tight rolloff and clamped pan.** Linear rolloff reaching silence just past the camera edge; stereo pan clamped to ±0.7 so nothing is ever stuck hard in one ear. Off-screen fights are naturally quiet — **this is the distance-culling mechanism**, not a separate feature. The local player's own actions and all UI stay flat. |
| 4 | **The audio service self-bootstraps and has zero scene wiring.** `AudioManager` creates itself via `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` and loads one `AudioConfig` ScriptableObject from `Resources/`, exactly mirroring [`SettingsService.ApplyAtBoot`](../../../Assets/Scripts/Settings/SettingsService.cs:33). Chosen over the project's usual scene-singleton pattern (`HitFeedback`, `GameSettingsManager`, `CTFGameManager`) specifically because unassigned scene references are this project's dominant failure mode — see `docs/scene-wiring-punch-list.md`. There is no scene reference to forget, and the service works identically in Menu, Lobby, and Gameplay with no per-scene setup. |
| 5 | **The service is a hard no-op when `!SettingsService.HasDisplay`.** The dedicated server never allocates a voice, never loads a clip, never touches the mixer. Client-only *by construction*, not by convention — the same property `SettingsService` already has. Call sites may call unconditionally; the service swallows. |
| 6 | **Mixer tree is `Master → { Music → { MusicBed }, SFX → { Combat, World, Enemy, Ambient }, Ui }`.** The four contracted exposed parameters live on Master / Music / SFX / Ui only — group names match `AudioBus` enum values exactly (`bus.ToString()`), which is why the UI group is named `Ui`, not `UI`. The `MusicBed` child of `Music` exists so snapshots can duck the looping bed without ever animating the `Music` group itself (see decision 7 and the Mixer section below) — `MusicDirector`'s two bed `AudioSource`s route there; one-shot stingers stay on `Music` directly. The four child groups under SFX exist purely for mix balance and as ducking targets, tuned in the mixer asset by the developer, never exposed to players. Chosen over adding a fifth settings slider because that would change shipped, tested settings code and bump its migration version — separate work, not a rider on this. |
| 7 | **Snapshots may never animate one of the four exposed parameters.** Snapshot transitions and `SettingsService.ApplyAudio()` both write mixer parameters; any overlap means a transition silently stomps the player's saved volume. Snapshots touch child-group volumes only. This is a correctness rule, not a style preference. |
| 8 | **One playback path replaces all fifteen call sites.** `Audio.PlayAt`, `Audio.Play2D`, `Audio.PlayUi`, `Audio.PlayMusic`. `AudioSource.PlayClipAtPoint` appears nowhere in the codebase after this lands. |
| 9 | **Voices are pooled and preallocated**, never instantiated per play. Fixed pool built at boot as children of the manager; the mixer group is assigned on acquire. |
| 10 | **Clips are referenced through an `AudioCueId` enum → `SoundBank` ScriptableObject**, never by direct `AudioClip` fields on gameplay components. Gameplay code names an event; it does not own an asset. |
| 11 | **Zero new network surface.** No new RPCs, no new `[Networked]` state, no new authority logic, no change to who receives what. Audio piggybacks on RPCs and `OnChangedRender` callbacks that already fan out correctly. |
| 12 | **No cue is ever both predicted and replicated.** The melee *swing* and the melee *impact* are two different cues on two different hooks, so there is nothing to reconcile and no double-play to suppress. Responsiveness comes from the swing being local; correctness comes from the impact being authoritative. |
| 13 | **Coin and deposit sounds are split by role, resolving an existing double-fire.** A single pickup fires both `CoinPickup.RPC_OnCoinCollected` *(All)* and `PlayerInventory.RPC_OnCoinAdded` *(All)*, and both call sites play a clip today — two sounds for one event whenever both fields are assigned. Same for deposit. After this spec: `CoinPickup` / `HomeBase` own the **world** cue (positional, everyone), `PlayerInventory` owns a **local-only** confirmation gated on `HasInputAuthority`. Two roles, one sound each. |
| 14 | **Three scale gates run in fixed order: cull → dedupe → budget.** Cheapest rejection first. A distant hit costs one squared-distance compare and never reaches the pool. |
| 15 | **De-duplication is keyed by cue id, not by instigator.** Twenty players landing hits in the same frame produce **one** impact sound, not twenty. This is a correctness requirement for the 20-player target, not a polish item. |
| 16 | **Voice stealing prefers the oldest voice at priority ≤ the incoming cue**, and drops the incoming cue if no such voice exists. Per-cue `maxConcurrent` prevents any single cue from consuming the pool. |
| 17 | **Every play gets pitch jitter and round-robin variant selection.** Without it, deduped-but-still-rapid repeats comb-filter into a buzz — the exact failure the arcade direction is most prone to. |
| 18 | **Music transitions crossfade between two ping-ponging music sources**, not via snapshot volume. Snapshot fades cannot overlap two tracks; two sources can. |
| 19 | **Music state is driven entirely by `MatchManager.PhaseChanged`**, which already fires on every peer via `OnChangedRender`. Victory vs defeat vs draw is `MatchManager.Winner` compared against the local player's team. No new state. |
| 20 | **No footsteps in v1.** No per-step event exists, and 20 players × footsteps is precisely the machine-gun failure this spec exists to prevent. Explicit non-goal, revisitable once the voice budget is proven under load. |
| 21 | **Stealth activation is audible to nearby opponents** — a quiet positional shimmer on a deliberately short `maxDistance` (default `0.35 ×` the world-cue default radius), plus a flat self-layer for the stealthed player. Gives opponents local counterplay without announcing a stealthed player across the arena. That multiplier is the balance lever; it lives in the cue definition and is retuned in the asset, never in code. |
| 22 | **`Halo Theme Song Original.mp3` stays on disk as a dev-only level-calibration reference** — used to check music-bus gain staging in the lobby. It is **never** entered into the `SoundBank`, **never** referenced by `AudioConfig`, and therefore never reachable from a build. A pre-ship checklist gate (below) requires its removal before any public distribution. Licensed CC0/CC-BY music is what actually ships on the Music bus. |
| 23 | **"Your team's flag was taken" deliberately breaks the positional rule** — flat, distance-independent, on the UI bus. It is the most match-relevant event in the game and by definition happens far away from you. Readability wins over consistency here, once, on purpose. |
| 24 | **`CTFGameManager.RPC_ShowNotification` drives only the neutral toast blip.** It carries a formatted string, and keying distinct sounds off string content would be fragile. Specific flag cues key off `Flag.OnStateChanged` instead, which carries typed state. |

## Architecture

### Layout

```
Assets/Scripts/Audio/
    AudioManager.cs              MonoBehaviour, self-bootstrapped, DontDestroyOnLoad
    Audio.cs                     static facade — the only API gameplay code touches
    AudioConfig.cs               ScriptableObject (one instance, in Resources/)
    SoundBank.cs                 ScriptableObject
    SoundCue.cs                  [Serializable] cue definition
    MusicDirector.cs             phase → track/snapshot, owned by AudioManager
    Core/
        Game.Audio.Core.asmdef   engine-free pure logic
        AudioCueId.cs            enum
        AudioBus.cs              enum
        VoiceBudget.cs           pooling / stealing rules
        SoundDedupe.cs           recent-cue suppression
        MusicState.cs            MatchPhase + winner byte + local-team byte → music state
Assets/Resources/
    AudioConfig.asset
Assets/Sound/
    LICENSES.md                  one row per acquired asset: file, source, license, URL
    SFX/ ...
    Music/ ...
```

The `Game.Audio.Core` assembly follows the existing precedent of `Game.Settings.Core`,
`Game.Match.Core`, `Game.Hud.Core`, and `Game.Combat.Core`: the rules that are worth testing live
where they can be tested outside Unity, and the Unity layer above them is thin enough to not need
its own tests. It declares `noEngineReferences: true` and references only `Game.Match.Core` (for
`MatchPhase`), matching `Game.Hud.Core`'s single reference to `Game.Combat.Core`.

**Assembly constraint an implementer will hit immediately:**
[`Team`](../../../Assets/Scripts/Teams/Team.cs:6) lives in the default assembly
(`Assembly-CSharp`), which an asmdef cannot reference. `MusicState` therefore takes plain
primitives — `MusicState.Resolve(MatchPhase phase, byte winner, byte localTeam)` — and
`MusicDirector`, which lives in the default assembly, converts `Team` to a byte at the boundary.
`MatchManager.Winner` is already a `byte` with the same encoding, so only one side needs
converting. The same constraint is why `AudioCueId` and `AudioBus` are self-contained enums rather
than reusing any gameplay type.

### `AudioManager`

```
[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]
    → if (!SettingsService.HasDisplay) return          // dedicated server: nothing exists
    → load AudioConfig from Resources
    → create the manager GameObject, DontDestroyOnLoad
    → preallocate the voice pool as children
    → SettingsService.Mixer = config.mixer
    → SettingsService.ApplyAudio()                     // sliders become audible here
    → MusicDirector.Initialize()
```

Boot order matters and is guaranteed: `BeforeSceneLoad` runs before any scene object's `Awake`, and
`SettingsStore.EnsureLoaded()` is idempotent, so the mixer is assigned and volumes applied before
the first frame renders and before any gameplay component could request a sound.

The manager subscribes to `SceneManager.sceneLoaded` to re-target `MusicDirector` and to re-acquire
`MatchManager.Instance` when a match scene loads or the scene reloads on rematch. It holds no
reference to any scene object beyond that.

### `Audio` — the static facade

The only surface gameplay code touches. Deliberately narrow:

| Call | Use |
|---|---|
| `Audio.PlayAt(AudioCueId, Vector3 worldPos)` | Positional world SFX. Runs all three scale gates. |
| `Audio.Play2D(AudioCueId)` | Flat SFX — the local player's own actions. Skips the distance gate. |
| `Audio.PlayUi(AudioCueId)` | Flat, UI bus, always local. Skips the distance gate. |
| `Audio.PlayMusic(MusicTrackId)` / `Audio.StopMusic(fade)` | Music director use; also callable from menu code. |
| `Audio.PlayLoop(AudioCueId, Transform follow) → AudioHandle` | The only call that returns anything. |
| `Audio.StopLoop(AudioHandle)` | Releases a loop voice. |

Everything except `PlayLoop` returns `void`. Callers cannot obtain, hold, or leak an `AudioSource`,
which is what makes the voice budget enforceable rather than advisory. All calls are safe when the
manager does not exist (server, or before boot) — they early-return.

### `SoundCue`

```
AudioCueId    id
AudioClip[]   variants        round-robin with no-immediate-repeat
AudioBus      bus             Combat | World | Enemy | Ambient | Ui | Music
bool          positional
float         volume
Vector2       pitchRange      default (0.92, 1.08)
int           priority        0 = first stolen … 100 = never stolen
float         dedupeWindow    seconds; 0 = no dedupe
int           maxConcurrent   0 = unlimited (still bounded by the pool)
float         maxDistance     0 = use the bus default
```

`variants` plus `pitchRange` are what keep the arcade direction from turning into a buzz under
repeat fire; they are per-cue, not global, because a coin chime wants far less variation than a
melee impact.

### `AudioConfig`

One asset in `Resources/`. Fields: the `AudioMixer` reference, the `SoundBank` reference, the music
playlist (`MusicTrackId → AudioClip`), pool sizes (32 SFX / 4 UI / 2 music), the default world
`maxDistance`, the pan clamp, and the crossfade duration. Nothing else in the project references
`Resources/` today; this is the one deliberate use, and it is what buys decision 4.

### Mixer

```
Master  [MasterVolume]
├── Music  [MusicVolume]
│   └── MusicBed        the two looping bed AudioSources route here — never Music directly
├── SFX    [SfxVolume]
│   ├── Combat      melee, projectiles, damage, death, respawn
│   ├── World       movement, coins, flags, deposits, ambient beds
│   ├── Enemy       all AI creature sounds
│   └── Ambient     looping arena beds
└── Ui     [UiVolume]   menus, HUD, self-confirmations, stingers-as-UI
```

Only the four bracketed parameters are exposed. Per decision 7, no snapshot animates any of them —
which is exactly why `MusicBed` exists: a snapshot that needs to duck the looping bed under a
stinger or Sudden Death animates `MusicBed`'s group volume, never `Music`'s (`Music` carries the
exposed `MusicVolume` parameter, and animating it would silently overwrite the player's saved
setting on every phase transition). One-shot stingers (`VictoryStinger`, `DefeatStinger`,
`DrawStinger`, and the countdown/match cues on the Music bus) stay routed to `Music` directly via
their `SoundCue.bus` — they are not beds, do not crossfade, and are unaffected by the `MusicBed`
duck.

**Snapshots**

| Snapshot | Effect | Transition |
|---|---|---|
| `Default` | Neutral. | 0.5 s |
| `Menu` | Combat / World / Enemy muted, Ambient down 12 dB. | 0.5 s |
| `SuddenDeath` | Enemy −6 dB, Ambient −9 dB, `MusicBed` +2 dB. | 1.5 s |
| `Stinger` | `MusicBed` −6 dB, Enemy / Ambient −12 dB for 2.5 s, then return to `Default`. | 0.2 s in, 1.0 s out |

All four move child-group volumes only — `MusicBed`, `Combat`, `World`, `Enemy`, `Ambient` — never
`Master`, `Music`, `SFX`, or `Ui`. `AudioMixer.TransitionToSnapshots` with explicit times.

## Networking

Audio adds nothing to the network layer. Every cue falls into exactly one of three categories.

**Local-only.** Never leaves the machine, never gated on anything but `HasInputAuthority` or "this
is a menu." All UI, the local player's own jump / land / dash, the self-confirmation chimes for
coins and deposits, buff tier-ups, scoreboard open/close.

**Replicated.** Piggybacks on an RPC or `OnChangedRender` that already fans out to the right peers.
`CoinPickup.RPC_OnCoinCollected`, `HomeBase.RPC_OnDeposit`, `Projectile.RPC_Impact`,
`PlayerStatsHandler.RPC_DisablePlayerControls` / `RPC_EnablePlayerControls`, `Flag.OnStateChanged`,
`MatchManager.PhaseChanged`, `PlayerBuffs.StealthStateChanged`.

**Attacker-only.** [`PlayerCombat.RPC_HitFeedback`](../../../Assets/Scripts/Player/PlayerCombat.cs:281)
and [`Projectile.RPC_HitFeedback`](../../../Assets/Scripts/Player/Projectile.cs:171) are already
declared `[Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]`, so the hit-confirm sound
reaches exactly the player who landed the hit and nobody else. The cue hangs off
[`HitFeedback.Play`](../../../Assets/Scripts/Player/HitFeedback.cs:37), alongside the particle
burst, damage number, and target flash that already live there.

### Responsiveness without double-play

The local player's melee must feel instant, and combat resolves on the server. This is normally
where a predicted sound and an authoritative sound collide. Here they cannot, because they are
different sounds:

```
Local input (input authority, this frame)
  → PlayerCombat.BeginSwing()  → swing whoosh cue        [Combat bus]

Server resolves the hit  →  RPC_HitFeedback (InputAuthority)
  → HitFeedback.Play()         → hit-confirm cue          [Combat bus]
```

The whoosh is the responsiveness. The confirm is the truth. A swing that misses plays only the
whoosh, which is correct — that *is* the feedback for a miss. No suppression logic, no
reconciliation window, no correlation ids.

The swing whoosh is heard by other players too, but not through the local prediction path: every
peer independently detects the rising edge of `PlayerCombat.CurrentSwingPhase()`, which is derived
from the `[Networked] AttackStartTick` and therefore already evaluated identically on every peer.
The local player's instance plays it flat; every other peer's instance of the same player plays it
positional. One edge detector per player object per peer — no path exists for a peer to play the
same swing twice.

### Who hears what, summarized

| Category | Gate | Example |
|---|---|---|
| Local-only | `HasInputAuthority`, or no gate at all for menus | own jump, UI click, own deposit chime |
| Attacker-only | already enforced by `RpcTargets.InputAuthority` | hit confirm |
| Victim-only | `HasInputAuthority` on `HealthChanged` | took damage |
| Team-only | local `Team` compared to the event's team | own flag taken, team buff unlocked |
| Everyone in AoI | the existing `RpcTargets.All` / `OnChangedRender` reach | coin pickup, projectile impact, enemy death |
| Everyone, unconditionally | flat UI-bus cue, distance gate skipped | match start, Sudden Death, victory stinger |

### Interaction with Area of Interest

[`AreaOfInterestRegistrar`](../../../Assets/Scripts/AreaOfInterest/AreaOfInterestRegistrar.cs)
already prevents distant objects from replicating, so most far-away events never produce an RPC on
a given client at all — AoI is the first and cheapest audio cull, and it is free. The distance gate
in decision 14 covers the remaining band: objects that are inside a player's AoI region but outside
their camera view.

One consequence to be aware of rather than to fix: objects registered via
`AddAlwaysInterested` — the flag carrier, managers, flags — replicate to everyone regardless of
distance. Their cues will therefore arrive on every client and be rejected by the distance gate,
except for the cues deliberately marked distance-independent (decision 23). That is the intended
behavior, and it is why the own-flag-taken alert works at all.

## Scale — three gates, in order

Applied in `Audio.PlayAt`, cheapest first.

**1. Distance cull.** Squared-distance compare between the cue position and the local camera,
against the cue's `maxDistance` (default ≈ 1.3 × camera half-width). Runs **before** any pool
acquisition, so an off-screen hit costs one float comparison and zero allocation. Cues marked
non-positional skip this gate entirely.

**2. De-duplication.** `Game.Audio.Core.SoundDedupe` holds the last play time per `AudioCueId`. A
cue whose window has not elapsed is dropped. Windows are per-cue: ~60 ms on melee and projectile
impacts, ~200 ms on killfeed blips, ~250 ms on score ticks, 0 (disabled) on flag and match cues,
which are rare and individually meaningful. Keyed by cue id and not by instigator (decision 15) —
that is exactly what collapses a 20-player scrum into one legible impact per window instead of
twenty overlapping ones.

**3. Voice budget.** `Game.Audio.Core.VoiceBudget` enforces the 32-voice SFX pool and each cue's
`maxConcurrent`. On exhaustion: steal the oldest active voice whose priority is ≤ the incoming
cue's priority; if none qualifies, drop the incoming cue. Priorities are set so enemy spawn and
ambient loops are stolen first, and match / flag / stinger cues are effectively never stolen.

**Then** pitch jitter and variant round-robin are applied to whatever survived (decision 17).

Worst case is bounded by construction: 32 concurrent SFX voices regardless of player count, with a
fixed per-frame cost of one distance compare and one dictionary lookup per attempted play.

## Music and mixer states

`MusicDirector` subscribes to `MatchManager.PhaseChanged` and reads `MatchManager.Winner`. The
mapping is pure logic in `Game.Audio.Core.MusicState`, so it is unit-testable without a runner:

| Phase | Track | Snapshot |
|---|---|---|
| *(no match / main menu)* | `MenuLoop` | `Menu` |
| `Warmup` | `LobbyLoop` | `Menu` |
| `Countdown` | `GameplayLoop` (fades in) | `Default` |
| `Live` | `GameplayLoop` | `Default` |
| `SuddenDeath` | `SuddenDeathLoop` (1.5 s equal-power crossfade) | `SuddenDeath` |
| `PostMatch` | bed stops; `VictoryStinger` / `DefeatStinger` / `DrawStinger` | `Stinger` → `Default` |
| `Intermission` | `LobbyLoop` | `Menu` |

`PostMatch` track selection: `MatchManager.Winner` is `0 = draw, 1 = Team1, 2 = Team2`
([`MatchManager.cs:27`](../../../Assets/Scripts/Match/MatchManager.cs:27)); compare against the
local player's `PlayerTeamData.Team`. A local player with `Team.None` (spectator, or a team that
has not replicated) gets the draw stinger — fail-safe toward neutral rather than falsely
celebratory.

Crossfades use two music `AudioSource`s that ping-pong (decision 18): the outgoing source fades to
silence over the crossfade duration while the incoming source fades up on an equal-power curve, and
the outgoing source is released to the pool on completion. Snapshot transitions run independently
and concurrently — they shape the *rest* of the mix around the music, they do not move the music.

## Sound catalog

The acquisition checklist. Bus and spatial tag are the two things that must be decided before an
asset is auditioned, because they determine what "good" sounds like.

### Combat — `Combat` bus

| Cue | Spatial | Notes |
|---|---|---|
| `MeleeSwing` | flat (self) / positional (others) | Short whoosh. High variant count — heard constantly. |
| `MeleeSwingHeavy` | flat / positional | Ground-pound windup variant. |
| `HitConfirm` | flat | Attacker only. The single most important cue in the game. |
| `HitConfirmHeavy` | flat | Same hook, selected when `damage` exceeds a threshold stored on the cue. **Not a crit sound** — there is no crit system; the crit multiplier was deleted in the 2026-08-05 damage-model change. |
| `TookDamage` | flat | Victim only. Must be distinguishable from `HitConfirm` at a glance. |
| `ProjectileFire` | flat (own) / positional | |
| `ProjectileImpact` | positional | 60 ms dedupe. |
| `PlayerDeath` | positional (flat for own) | |
| `PlayerRespawn` | positional (flat for own) | |

### Movement — `World` bus

| Cue | Spatial | Notes |
|---|---|---|
| `Jump` | flat (self) / positional | Replaces `PlayerAnimator.PlayOneShot`. |
| `Land` | flat (self) / positional | Replaces `PlayerAnimator.PlayOneShot`. |
| `LandHeavy` | flat / positional | Ground-pound impact. |
| `Dash` | flat (self) / positional | |
| `WallOrLedgeScuff` | positional | **Stretch cue.** Cut from v1 if no suitable CC0 asset is found during acquisition; the enum value and bank entry are then removed rather than left empty, so the bank-completeness test stays meaningful. |

### Coins and economy

| Cue | Bus | Spatial | Notes |
|---|---|---|---|
| `CoinPickupWorld` | World | positional | Everyone. |
| `CoinPickupSelf` | UI | flat | Self only. Pitch rises with streak. |
| `DepositWorld` | World | positional | Everyone. |
| `DepositSelf` | UI | flat | Self only. Pitch scales with points deposited. |
| `ScoreTick` | UI | flat | 250 ms dedupe. |

### Flags

| Cue | Bus | Spatial | Notes |
|---|---|---|---|
| `FlagTaken` | World | positional | At the flag. |
| `FlagDropped` | World | positional | |
| `FlagReturned` | World | positional | |
| `FlagPickupSelf` | UI | flat | You are now the carrier. |
| `AlertOwnFlagTaken` | UI | **flat, distance-independent** | Decision 23. |
| `FlagCaptured` | UI | flat | Everyone; leads into the match-end stinger. |

### Buffs

| Cue | Bus | Spatial | Notes |
|---|---|---|---|
| `BuffTierUp` | UI | flat | Self only, via the existing `Game.Hud.Core.TierUpEdge` detector. |
| `TeamBuffUnlocked` | UI | flat | That team only. |
| `StealthEnter` | World | flat (self) + short-radius positional (others) | Decision 21. |
| `StealthExit` | World | flat (self) + short-radius positional (others) | Decision 21. |

### Enemies — `Enemy` bus, all positional

| Cue | Notes |
|---|---|
| `EnemyTelegraph` | **High priority** — never stolen. This is the counterplay window; if it is inaudible the enemy is unfair. |
| `EnemyAttack` | |
| `EnemyHurt` | 60 ms dedupe. |
| `EnemyDeath` | |
| `EnemySpawn` | **Lowest priority** — first stolen when the pool is contended. |

### Match and stingers

| Cue | Bus | Spatial | Notes |
|---|---|---|---|
| `CountdownTick` | UI | flat | Per second during `Countdown`. |
| `CountdownGo` | UI | flat | |
| `MatchStart` | UI | flat | |
| `SuddenDeathAlert` | UI | flat | Fires with the `SuddenDeath` snapshot transition. |
| `MatchEnd` | UI | flat | |
| `VictoryStinger` | Music | flat | |
| `DefeatStinger` | Music | flat | |
| `DrawStinger` | Music | flat | Also the `Team.None` fallback. |

### UI — `UI` bus, all flat, all local-only

| Cue | Notes |
|---|---|
| `UiHover` | Low volume; `maxConcurrent = 1`. |
| `UiClick` | |
| `UiBack` | |
| `UiToggle` | |
| `UiSliderTick` | `maxConcurrent = 1`, 40 ms dedupe — a slider drag must not machine-gun. |
| `PanelOpen` / `PanelClose` | Settings, scoreboard. |
| `ToastNotification` | Neutral blip from `CTFGameManager.RPC_ShowNotification` (decision 24). |
| `KillfeedEntry` | 200 ms dedupe. |
| `KillConfirmSelf` | Distinct from `KillfeedEntry` — your kill, not everyone's. |

### Music and ambient beds

| Track | Bus | Notes |
|---|---|---|
| `MenuLoop` | Music | Main menu. |
| `LobbyLoop` | Music | Lobby, Warmup, Intermission. |
| `GameplayLoop` | Music | Countdown and Live. |
| `SuddenDeathLoop` | Music | Higher intensity; crossfaded in. |
| `ArenaAmbientBed` | Ambient | Looping, flat, starts on `Countdown`, stops on `PostMatch`. |

**Total: 52 cues + 5 music/ambient beds** — Combat 9, Movement 5 (one a stretch cue), Coins 5,
Flags 6, Buffs 4, Enemies 5, Match/stingers 8 (three of which route to the Music bus), UI 10.

## Asset sourcing and licensing

**Sources, in order of preference:** Kenney.nl CC0 game-audio packs (primary — arcade character,
consistent, zero attribution burden); freesound.org filtered to CC0 for gaps; CC0 or CC-BY music
loops for the five beds. Nothing else without an explicit commercial-use license.

**`Assets/Sound/LICENSES.md`** is created with the first acquired asset and holds one row per file:
filename, source URL, author, license, and date acquired. A file with no row does not ship. For
CC-BY assets the required attribution text is recorded there and surfaced in the credits.

**Pre-ship gate.** Before any public build:

1. `Assets/Sound/Music/Halo Theme Song Original.mp3` and its `.meta` are deleted from the working
   tree (decision 22).
2. Every file under `Assets/Sound/` has a row in `LICENSES.md`.
3. Every CC-BY attribution in `LICENSES.md` appears in the in-game credits.

Until then the Halo mp3 remains as a dev-only reference for calibrating music-bus gain staging in
the lobby. It is never entered into the `SoundBank` and never referenced by `AudioConfig`, so no
code path can reach it and no build can include it as a played asset.

## Data Flow

```
Boot (client only)
  RuntimeInitializeOnLoadMethod
    -> AudioManager created (DontDestroyOnLoad)
    -> voice pool preallocated
    -> SettingsService.Mixer = config.mixer
    -> SettingsService.ApplyAudio()      [four persisted volumes become audible]
```

```
Player drags a volume slider
  SettingsPanel -> SettingsStore.SfxVolume = v      [existing]
               -> SettingsService.ApplyAudio()      [existing, SettingsPanel.cs:116]
                    -> Mixer.SetFloat("SfxVolume", VolumeCurve.LinearToDecibels(v))
  (audio system adds nothing to this path)
```

```
Server resolves a melee hit
  PlayerCombat.ApplyMeleeHits (state authority)
    -> RPC_HitFeedback  [RpcTargets.InputAuthority]  (existing)
         -> HitFeedback.Play() on the attacker's client only
              -> particles + damage number + target flash   [existing]
              -> Audio.Play2D(HitConfirm)                   [new, one line]
```

```
A coin is collected
  CoinPickup.RPC_OnCoinCollected  [All]
    -> Audio.PlayAt(CoinPickupWorld, coinPos)     positional, everyone
  PlayerInventory.RPC_OnCoinAdded [All]
    -> if (HasInputAuthority) Audio.PlayUi(CoinPickupSelf)   flat, self only
```

```
Match enters Sudden Death
  MatchManager.EnterPhase(SuddenDeath)  ->  [Networked] Phase
    -> OnChangedRender on every peer -> PhaseChanged event      (existing)
         -> MusicDirector: crossfade GameplayLoop -> SuddenDeathLoop (1.5 s)
         -> Mixer.TransitionToSnapshots(SuddenDeath, 1.5 s)
         -> Audio.PlayUi(SuddenDeathAlert)
```

```
Any positional cue
  Audio.PlayAt(cue, pos)
    -> [1] sqrDistance(pos, localCamera) > cue.maxDistance^2 ?   -> drop
    -> [2] SoundDedupe.ShouldPlay(cue.id, now, cue.dedupeWindow)? -> drop
    -> [3] VoiceBudget.Acquire(cue.priority, cue.maxConcurrent)?  -> steal or drop
    -> apply variant round-robin + pitch jitter + clamped pan
    -> play on the acquired pooled voice, routed to cue.bus
```

## Failure Modes

| Situation | Behavior |
|---|---|
| Dedicated server build | `SettingsService.HasDisplay` is false; `AudioManager` is never created. Every `Audio.*` call early-returns. No clips load, no voices allocate. |
| `AudioConfig.asset` missing from `Resources/` | One `Debug.LogError` at boot; the manager does not create itself; every `Audio.*` call early-returns. Game is silent but fully playable. Caught by an EditMode test. |
| A cue has no bank entry, or an entry with all-null clips | The play call is dropped with a single throttled warning. **Caught at build time by the bank-completeness EditMode test**, which is the point — this project's dominant failure mode is an unassigned reference that fails silently, and this converts it into a red test. |
| Voice pool exhausted | Oldest voice at priority ≤ incoming is stolen; if none, the incoming cue is dropped. Never grows the pool, never allocates. |
| 20 players landing hits in one frame | Dedupe collapses them to one `HitConfirm` / `ProjectileImpact` per 60 ms window. |
| Player drags a volume slider rapidly | Unchanged from today: `SettingsStore` writes in-memory and defers the `PlayerPrefs.Save()` to panel close. `UiSliderTick` is dedupe-limited to 40 ms and `maxConcurrent = 1`. |
| Scene reload on rematch | `AudioManager` survives (`DontDestroyOnLoad`); `MusicDirector` re-acquires `MatchManager.Instance` on `sceneLoaded`. Music continues across the reload rather than restarting. |
| `MatchManager.Instance` null (menu, teardown) | `MusicState` returns the menu state. `MusicDirector` caches its subscription target the way [`PlayerBuffs`](../../../Assets/Scripts/Buffs/PlayerBuffs.cs:30) already does, so teardown does not throw. |
| Local player's `Team` is `Team.None` at match end | Draw stinger. Fail-safe toward neutral. |
| Player reconnects mid-match | A fresh client boot: the manager bootstraps, reads the current `MatchManager.Phase`, and starts the correct bed at the correct snapshot. No catch-up, no replay of missed cues. |
| A snapshot is authored to animate an exposed param | Player volume settings are silently stomped on every transition. Prevented by decision 7 and asserted by an EditMode test that reads the mixer asset's exposed-parameter list. |

## Testing

**EditMode — `Game.Audio.Core`**, following the `TerritorialCombatTests` / `SwingPhaseTests` /
`MatchRules` precedent for pure logic:

- `VoiceBudget` — acquire under capacity; steal the oldest at priority ≤ incoming; refuse to steal
  a higher-priority voice; drop when nothing qualifies; `maxConcurrent` enforced per cue
  independently of total capacity.
- `SoundDedupe` — a cue inside its window is dropped, outside is allowed; windows are independent
  per cue id; `dedupeWindow = 0` never drops.
- `MusicState.Resolve` — full mapping table over `MatchPhase` × winner byte × local-team byte,
  including local team `0` (`Team.None`) → draw stinger and the no-`MatchManager` → menu case.

**EditMode — asset integrity** (the tests that catch this project's actual failure mode):

- Every `AudioCueId` enum value resolves to a `SoundBank` entry with at least one non-null clip.
- Every `SoundCue` names a bus that exists in the mixer asset.
- The mixer exposes exactly `MasterVolume`, `MusicVolume`, `SfxVolume`, `UiVolume` — matching
  [`SettingsService.cs:26-29`](../../../Assets/Scripts/Settings/SettingsService.cs:26) — and no
  snapshot animates any of the four (decision 7).
- `Resources/AudioConfig.asset` exists and has a non-null mixer and bank.
- No file under `Assets/Sound/` lacks a row in `LICENSES.md`.

**Multi-peer verify** (3 peers minimum: two on Team1, one on Team2):

1. All four volume sliders audibly affect their bus, persist across a restart, and reset correctly.
2. A melee swing sounds instantly on the swinging client; the hit-confirm plays only on the
   attacker; the victim hears only the took-damage cue. No client hears two sounds for one hit.
3. One coin pickup produces exactly one world sound for the observers and exactly one self chime
   for the collector — the double-fire in decision 13 is gone.
4. An off-screen fight is inaudible or near-inaudible; the same fight walked into becomes audible
   with a smooth rolloff and no hard pan.
5. `AlertOwnFlagTaken` is clearly audible from the far side of the map; `FlagTaken` at the same
   moment is not.
6. Phase transitions: countdown ticks, match-start cue, Sudden Death crossfade plus snapshot duck,
   and the correct victory/defeat stinger per team.
7. Scene reload on rematch: music continues correctly, no duplicate `AudioManager`, no doubled
   music.
8. A staged scrum (all peers attacking in the same area) produces a legible impact rhythm rather
   than a continuous buzz, and the frame time shows no audio-driven spikes.

**Dedicated-server verify:** headless build runs a full match with zero audio log output and no
audio allocation.

## Out of Scope

- **Footsteps** (decision 20) — no per-step event exists and it is the worst case for the voice
  budget. Revisit once the budget is proven under load.
- **Any settings code.** The four sliders, their persistence, migration, clamping, defaults, and
  the panel that drives them all already ship. This spec assigns `SettingsService.Mixer` and stops.
- **A fifth volume slider** for ambient/enemy (decision 6) — that is a settings-contract change
  with its own migration bump.
- **Voice chat** of any kind.
- **Reverb zones, occlusion, and audio raycasting** — meaningless in a flat 2D arena.
- **Adaptive / layered music** that reacts to combat intensity. Music is phase-driven only
  (decision 19).
- **Accessibility features** — subtitles, visual sound indicators, mono downmix, per-cue mute.
  Worth a future spec; not this one.
- **Audio that affects gameplay.** Nothing here is simulated, networked, or read by the server.
- **Per-weapon or per-enemy-archetype sound variation.** One cue family serves all seven enemy
  colour prefabs and all four shape archetypes in v1.
- **Commissioned or original assets** (decision 2) — CC0/royalty-free only for now.
- **Any new RPC, `[Networked]` field, or change to existing authority** (decision 11).
