# Settings Menu — Unity Setup Guide

Wiring the client-local options window into `MainMenu.unity`. All the code is written, compiled,
and reviewed; **none of it has ever run inside Unity**, so this pass is the first real verification.

**Branch:** `feat/economy-feedback-surfaces` (the code does not exist on `main`).
**Spec:** `docs/superpowers/specs/2026-07-29-options-settings-design.md`

Budget ~20 minutes for the hierarchy and wiring, then playtest.

---

## The critical wiring constraint — read this first

`SettingsPanel` must sit on a GameObject that **stays active**, with its `panelRoot` field pointing
at a *child* window object. If the component sat on the object it deactivates, `Awake` would never
have run the first time the window opened — `Awake` is where every button and slider gets its
listener. The code logs an explicit error (`❌ SettingsPanel: panelRoot must be a CHILD window
object...`) if `panelRoot == gameObject`, so a bad wiring fails loudly the first time you open the
window rather than doing nothing silently.

**The same reasoning applies to `VideoSettingsSection`.** Its 10-second auto-revert countdown for an
unconfirmed resolution/display-mode change is driven by `Update()`, which stops firing the moment
its GameObject goes inactive — a countdown that can't tick can't expire, which would otherwise strand
the player mid-preview. A review caught this and it's now fixed defensively in code two ways:
`SettingsPanel.ShowTab` cancels any pending confirm when the player leaves the Video tab, and
`VideoSettingsSection.OnDisable` cancels as a catch-all for any other path that deactivates it. So a
bad wiring can no longer strand the player — but the **recommended** wiring is still to put both
`SettingsPanel` and `VideoSettingsSection` on the same always-active `SettingsRoot` object, and only
ever toggle their visual *children* (the tab contents, the confirm panel). Treat the `OnDisable`
guard as a safety net, not a wiring option.

---

## 1. Hierarchy to build

Under the existing `Canvas` in `MainMenu.unity`:

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

`SettingsWindow` starts inactive — `SettingsPanel.Awake()` calls `panelRoot.SetActive(false)` at the
end of setup, so it's safe to leave it active in the editor for layout work; Unity will hide it the
moment Play mode starts. `ConfirmPanel` similarly gets `SetActive(false)` from
`VideoSettingsSection.Awake()`.

Add both **`SettingsPanel`** and **`VideoSettingsSection`** components to `SettingsRoot`.

---

## 2. Field-by-field wiring — `SettingsPanel`

22 serialized fields, in declaration order. *Optional* means the panel runs fine with it unset (a
null check guards every use); nothing crashes, that piece of UI just won't update.

| Field | Wire to | Required? |
|---|---|---|
| `panelRoot` | `SettingsWindow` | **Required** |
| `closeButton` | `CloseButton` | Optional |
| `audioTabButton` | `TabBar/AudioTabButton` | Optional |
| `videoTabButton` | `TabBar/VideoTabButton` | Optional |
| `gameplayTabButton` | `TabBar/GameplayTabButton` | Optional |
| `audioTab` | `AudioTab` | Optional |
| `videoTab` | `VideoTab` | Optional |
| `gameplayTab` | `GameplayTab` | Optional |
| `masterSlider` | Master volume slider | Optional |
| `musicSlider` | Music volume slider | Optional |
| `sfxSlider` | SFX volume slider | Optional |
| `uiSlider` | UI volume slider | Optional |
| `masterValueLabel` | Master % label | Optional |
| `musicValueLabel` | Music % label | Optional |
| `sfxValueLabel` | SFX % label | Optional |
| `uiValueLabel` | UI % label | Optional |
| `audioResetButton` | `AudioResetButton` | Optional |
| `cameraShakeSlider` | Camera shake slider | Optional |
| `cameraShakeValueLabel` | Camera shake % label | Optional |
| `damageNumbersToggle` | Damage numbers toggle | Optional |
| `gameplayResetButton` | `GameplayResetButton` | Optional |
| `video` | the `VideoSettingsSection` component on `SettingsRoot` | **Required** |

Note: `panelRoot` and `video` are the only two fields the code treats as load-bearing. Everything
else degrades gracefully if left unassigned — useful if you want to build the window tab by tab
rather than all at once, but don't rely on that long-term; an unwired slider is a silently missing
feature, not an error.

---

## 3. Field-by-field wiring — `VideoSettingsSection`

9 object references plus one numeric value, in declaration order.

| Field | Wire to | Required? |
|---|---|---|
| `resolutionDropdown` | Resolution `TMP_Dropdown` | Optional |
| `displayModeDropdown` | Display mode `TMP_Dropdown` | Optional |
| `vsyncToggle` | VSync `Toggle` | Optional |
| `fpsCapDropdown` | FPS cap `TMP_Dropdown` | Optional |
| `videoResetButton` | `VideoResetButton` | Optional |
| `confirmPanel` | `ConfirmPanel` | **Required** — without it a pending change has no visible prompt, though the countdown and auto-revert still function invisibly |
| `confirmCountdownLabel` | Countdown text inside `ConfirmPanel` | Optional |
| `confirmKeepButton` | Keep button inside `ConfirmPanel` | Optional |
| `confirmRevertButton` | Revert button inside `ConfirmPanel` | Optional |
| `confirmSeconds` | *(not a reference — a float)* | Default `10`; leave as-is unless you want a longer/shorter grace period |

---

## 4. `MainMenuUI` and `LobbyScreenUI` wiring

Both scripts gained two fields for this feature:

- `optionsButton` (a `Button`) — wire it to whatever "Options" button lives on that screen.
- `settingsPanel` (a `SettingsPanel`) — wire it on **both** scripts to the **same** `SettingsRoot`
  object. One `SettingsPanel` instance serves both the main menu and the lobby screen; there is
  only ever one options window in the scene.

`MainMenuUI.OpenSettings()` hides `menuPanel` and reopens it via the `onClosed` callback;
`LobbyScreenUI.OpenSettings()` does the same with `lobbyPanel`. Because it's a shared instance,
`MainMenuUI.Show()` also calls `settingsPanel.Close()` defensively (so a connect failure can't leave
the options window and the main menu stacked); `LobbyScreenUI.Hide()` does the same for the lobby
side.

---

## 5. Manual verification checklist

- [ ] Settings survive quitting and relaunching the game.
- [ ] The game launches directly at the stored resolution with no visible flash of a different one.
- [ ] Changing resolution shows the confirm prompt; ignoring it for 10s reverts, and the
      reverted-from value is **not** persisted (relaunch to confirm).
- [ ] Closing the window while a confirm is pending reverts rather than keeping.
- [ ] VSync on greys out the FPS cap dropdown; turning VSync off restores the previously chosen cap.
- [ ] Camera shake at 0 produces no shake on taking damage; at 2.0 it is visibly stronger than at 1.0.
- [ ] Damage numbers off still shows the particle burst and the target hit-flash.
- [ ] The audio sliders persist and restore their positions across a restart despite being silent
      (expected — no `AudioMixer` exists yet; see note below).
- [ ] The Options button works from both the main menu and the lobby, and opening it from the lobby
      does not disturb the connection or the roster.
- [ ] Reset to Defaults on each tab restores that tab only, and the lobby nickname survives it.
- [ ] A headless dedicated-server build starts cleanly with no display or audio calls attempted.
- [ ] `Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All` — 29 settings cases across
      `Assets/Tests/EditMode/Settings/` (`ResolutionListTests`, `SettingsCatalogTests`,
      `SettingsMigrationTests`, `VolumeCurveTests`). These have never been executed through NUnit —
      running them is your gate, not something already confirmed.

Not on this list: any check of what happens if the options window is still open when the lobby
screen is hidden by a mid-lobby disconnect. That path was traced through all three call sites of
`menuUI.Show()` in `GameNetworkManager.cs` (`ShowReconnectingUI`, `HideReconnectingUI`, and the
generic disconnect handler) — every one of them is preceded by `lobbyUI.Hide()`, which itself closes
the shared `settingsPanel`, and the separate reconnect-success path reloads the MainMenu scene
outright. There's nothing to strand.

---

## 6. Note on `Gameplay.unity`

No deliberate change is needed here. `Gameplay.unity`'s `GameSettingsManager` component still lists
**10** now-deleted field names in its scene YAML — `respawnTimeMultiplier`, `enemyHealthMultiplier`,
`enemyDamageMultiplier`, `enemySpawnRateMultiplier`, `goldMultiplier`, `experienceMultiplier`,
`autoRespawn`, `showMinimap`, `showDamageNumbers`, `cameraShakeIntensity` — left over from before
those settings moved to the client-local `SettingsStore`. Unity drops them silently the next time
you save that scene; it's expected housekeeping, not corruption. `combatConfig`,
`difficultyRingConfig`, `matchTimeLimit`, and `suddenDeathHardCap` are the four fields still read by
`GameSettingsManager` today and keep their authored values.

---

## Known limitations (by design, not bugs)

- **The four audio sliders persist but are silent.** No `AudioMixer` exists in the project yet, so
  `SettingsService.Mixer` is `null` and `SettingsService.ApplyAudio()` no-ops on the mixer write
  until the (unwritten) audio system ships one exposing `MasterVolume` / `MusicVolume` /
  `SfxVolume` / `UiVolume` parameters. Expected on delivery, not a bug to report.
- **The EditMode test suite has never been executed.** NUnit does not run outside the Unity Editor
  in this environment — the 29 tests under `Assets/Tests/EditMode/Settings/` are correct-by-review
  and by an external harness only. Running them via Test Runner is the first real gate.
