# Integration State Audit — 2026-08-04

**Purpose:** establish, with evidence, what is actually built / wired / verified on `origin/main`
before committing to any further integration work.

**Method:** static analysis of committed state (`origin/main` @ `c53526d`), plus a batchmode
compile and a full EditMode NUnit run. **No fixes were applied** — this document is descriptive
only. Every claim below cites a file and line, or a command result.

**Scope note:** the capstone that commissioned this audit assumed ~15 unmerged feature branches
needing dependency-ordered integration. That premise does not hold (§1). The audit was rescoped
to "what is the real state" before choosing work.

---

## 1. Branch reconciliation — integration is a non-task

Local `main` was 40 commits behind `origin/main` at audit start, which is the likely source of the
stale premise. Measured against `origin/main`:

| Branch | Commits ahead | Verdict |
|---|---|---|
| `feat/win-condition-sudden-death` | 0 | Contained in main |
| `feat/territory-vanguard-rework` | 0 | Contained in main |
| `docs/coins-buffs-economy-spec` | 0 | Contained in main |
| `feat/enemy-shape-archetypes` | 0 | Contained in main |
| `feat/player-hud-rebuild` | 0 | Contained in main |
| `feat/dedicated-server-phase2a-aoi` | 0 | Contained in main |
| `feat/economy-feedback-surfaces` | 0 | Contained in main |
| `feat/hit-landed-feedback` | 0 | Contained in main |
| `feat/movement-combat-feel` | 0 | Contained in main |
| `feat/sky-background` | 0 | Contained in main |
| `fix/sky-visuals-and-placer-hang` | 0 | Contained in main |
| `fix/remove-player-hitmarker` | 0 | Contained in main |
| `fix/ctf-client-flag-carrier-sync` | 0 | Contained in main |
| `feat/match-lifecycle` | 3 | **Docs only, and older than main.** `git diff origin/main origin/feat/match-lifecycle -- docs/` is 13,822 deletions / 0 insertions — main is a strict superset. |
| `feat/individual-buff-layer` | 1 | **Empty merge commit** (`fef78da`), no unique content |
| `feat/menu-lobby-revamp` (local) | 0 | Contained in main |
| `feat/anim-locomotion-smoothing` (local) | 0 | Contained in main |

**Conclusion: there is nothing to merge.** No unique code exists off `origin/main`, so there are no
conflicts to resolve and no dependency ordering to compute. PRs #73 (reconnection) and #74 (options)
are **merged**, not open, contrary to prior notes.

---

## 2. Build and test evidence

| Gate | Result |
|---|---|
| Batchmode compile (Unity 6000.3.0f1) | **Clean.** `grep -cE "error CS"` over the build log = **0**. `*** Tundra build success`. |
| EditMode NUnit suite | **386 / 386 passed**, 0 failed, 0 skipped, 0 inconclusive. `result=Passed`, duration 0.199s. |

Command used (the first two attempts died during assembly reload; the third, without `-nographics`,
completed — worth remembering, as prior sessions recorded batchmode `-runTests` as unusable):

```bash
"C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Unity.exe" -runTests -batchmode -projectPath "C:\Users\1\Documents\GitHub\2dGame" -testPlatform EditMode -testResults r3.xml -logFile r3.log
```

**What this does and does not prove.** It proves the code compiles and all pure-logic cores are
correct. It proves *nothing* about whether systems are wired into scenes or behave correctly in
play — which is exactly where the defects in §3 live. Every unassigned reference below passed
through a green test suite.

---

## 3. Inert and unwired systems — the headline finding

A scan of every scene and prefab for `{fileID: 0}` on project-owned scripts found **58 unassigned
object references**. Most are benign; the following are not. The dominant failure mode is a
`[SerializeField]` guarded by `if (x == null) return;`, so a missing reference disables a feature
**silently** — no exception, no log, and a green test suite.

### 3.1 CRITICAL — the meta-damage layer is dead

```
Assets/Scenes/Gameplay.unity:17253:  combatConfig: {fileID: 0}
Assets/Scenes/Gameplay.unity:17254:  difficultyRingConfig: {fileID: 0}
```

These are worse than "unassigned": **neither ScriptableObject asset exists in the repo.** Only
`CombatConfig.cs` and `DifficultyRingConfig.cs` are present — there is no `.asset` to assign, so
this was never authorable in the editor. Consumers fail open:

```csharp
// Assets/Scripts/Player/PlayerCombat.cs:302
if (config == null) return Mathf.RoundToInt(stats.attackDamage);
```

All three `ResolveDamage` call sites — `PlayerCombat.cs:303` (melee), `PlayerCombat.cs:322`
(projectile), `Enemy.cs:292` — degrade to raw base damage. **Consequently inert in play:** crit
rolls, the territorial damage tax, and the entire team `VanguardTier` buff layer. The
`difficultyRingConfig` gap likewise makes enemy center-scaled difficulty inert — the same root
cause, not previously flagged.

This single defect makes systems #2 (team half), #3, and the difficulty half of #4 non-functional,
while all their unit tests pass.

### 3.2 HIGH — ground pound misfires while grounded

`PlayerPrefab.prefab` carries two independent ground checks. `PlayerMovement`'s is correct;
`PlayerCombat`'s is not:

| Component | `groundCheck` | `groundLayer` | `groundCheckRadius` |
|---|---|---|---|
| `PlayerMovement` | `{fileID: 2240091569963034458}` | `m_Bits: 8` | 2 |
| `PlayerCombat` | **`{fileID: 0}`** | **`m_Bits: 0`** | 0.2 |

`PlayerCombat` is doubly broken — no transform *and* an empty layer mask. So:

```csharp
// PlayerCombat.cs:158
bool isGrounded = groundCheck != null && Physics2D.OverlapCircle(...);   // always false
AttackIsPound = verticalAim < 0 && !isGrounded && downAttackPoint != null;
```

`isGrounded` is permanently `false`, so `AttackIsPound` is **true whenever the player aims down**,
including while standing on the ground (`useGroundPound: 1` in the prefab). Note that
`PlayerMovement.IsGrounded()` is documented at line 260 as the "single source of truth for grounded
state" — `PlayerCombat` duplicates the check instead of calling it. Fixing the duplication is
probably better than assigning the field.

### 3.3 MEDIUM — economy feedback HUD is unwired

The economy feedback surfaces merged in PR #71 are present in code but have no scene objects:

- `TeamScoreDisplay` — `vanguardProgressFill`, `vanguardMilestoneText`, `zoneIcon`, `zoneText`,
  `toastFeed` all unassigned (`Gameplay.unity:23470-23477`)
- `BuffIconDisplay` × 3 instances — `nextUnlockFill`, `cooldownRadial`, `toastFeed` unassigned
  (`Gameplay.unity:4052`, `7064`, `21987`)
- `HudToastFeed` — **exists as a script but is present in no scene at all**, which is consistent
  with every `toastFeed` reference above being empty

Net effect: tier pips, next-unlock progress, and unlock toasts do not render. Buff tier changes
would be invisible to the player even once §3.1 is fixed.

### 3.4 MEDIUM — reconnection retry UX is invisible

```
Assets/Scenes/MainMenu.unity:301:  reconnectPanel: {fileID: 0}
Assets/Scenes/MainMenu.unity:302:  cancelReconnectButton: {fileID: 0}
```

`ReconnectController` *is* attached to the `NetworkManager` object in `MainMenu.unity` (added in
the uncommitted editor work captured as `1c68f18`), but `MainMenuUI`'s two reconnect UI references
are empty. The reconnect logic can run; the player sees no retry panel and has no cancel affordance.

### 3.5 MEDIUM — Sudden Death banner never displays

```
Assets/Scenes/Gameplay.unity:8659:  suddenDeathRoot: {fileID: 0}
```

`MatchPhaseHud.cs:100` gates the banner on `suddenDeathRoot != null`. Sudden Death can be entered
(the phase logic is tested and passing) but is never announced on screen.

### 3.6 LOW — enemies and players never receive team colors

`EnemyTeamComponent.spriteRenderer` is unassigned on **all seven** enemy prefabs, and
`PlayerTeamData.spriteRenderer` is unassigned on `PlayerPrefab.prefab` (`colorizePlayer` defaults
to `true`). Both guard with `if (spriteRenderer == null ... ) return;`, so team tinting silently
no-ops everywhere.

### 3.7 Benign — verified as self-healing or by-design

Recorded so they are not re-investigated:

- `Flag.flagSprite` / `Flag.triggerCollider` — resolved via `GetComponent` in `Awake`
  (`Flag.cs:85-92`). Safe.
- `SettingsService` absent from all scenes — **correct by design**: it is a `static class` using
  `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` (`SettingsService.cs:33`).
- All effect-prefab fields (`depositEffect`, `pickupEffect`, `dropEffect`, `impactEffect`,
  `muzzleFlashPrefab`, `groundPoundImpactEffect`) — cosmetic, null-guarded.

---

## 4. Per-system status

Rated on three independent axes. "Verified" means verified *in play* — the EditMode suite does not
count, per §2.

| # | System | Built | Wired | Verified | Notes |
|---|---|---|---|---|---|
| 1 | Match lifecycle | Yes | Partial | No | `MatchManager` in scene; Sudden Death banner unwired (§3.5) |
| 2 | Coins→buffs economy | Yes | **No** | No | Individual layer wired (`PlayerBuffs.config` assigned on prefab); team layer inert (§3.1); HUD unwired (§3.3) |
| 3 | Unified damage pipeline | Yes | **No** | No | All 3 call sites fall back to base damage (§3.1) |
| 4 | Enemy AI | Yes | Partial | No | Authority model built; difficulty rings inert (§3.1); team color unwired (§3.6) |
| 5 | Audio | **No** | — | — | See below |
| 6 | Scoreboard + killfeed | **Yes** | Partial | No | See below |
| 7 | Options / settings | Yes | Yes | No | Bootstraps correctly (§3.7); 4 test files green. Video needs a standalone build |
| 8 | Reconnection | Yes | Partial | No | Merged (PR #73); retry UI unwired (§3.4) |
| 9 | HUD / lobby / AoI | Yes | Partial | No | AoI wired: 9 `AlwaysInterestedMarker` instances + 1 registrar + 2 flags in `Gameplay.unity` |

**Two corrections to the commissioning brief:**

- **Audio (#5) has no spec and no implementation.** `docs/superpowers/specs/2026-07-29-audio-system-design.md`
  does not exist; there is no audio spec on main at all. Its "hooks" exist only as unassigned
  `[SerializeField]` slots — `HomeBase.depositSound` ×2, `CoinPickup.pickupSound` ×7,
  `PlayerInventory.coinPickupSound`/`depositSound`, `PlayerAnimator.audioSource`/`jumpClip`/`landClip`.
  Nothing to regress; the system is simply absent.
- **Scoreboard (#6) is built**, not unbuilt: `ScoreboardPanel`, `ScoreboardRowView`,
  `ScoreboardInputReader`, `Core/ScoreboardSort.cs`, a 2,315-line plan, and
  `docs/scoreboard-unity-setup-guide.md`. `ScoreboardPanel` is present in `Gameplay.unity`. The
  **killfeed half is not wired** — `HudToastFeed` is in no scene (§3.3).

---

## 5. Dead-code sweep

Largely already done by prior work:

- **`GameIsOver`** — **zero references** anywhere in the codebase. The old win path is fully gone;
  `MatchPhase` is the sole authority.
- **`UIManager`** — the script does not exist. Only two stale doc-comments mention it
  (`PlayerStealthVisual.cs:76`, `PlayerHud.cs:31`). Cosmetic only.
- **`GameSettingsManager`** — no longer holds client-preference knobs; `minDamageMultiplier` /
  `maxDamageMultiplier` were pruned in `0ad144b` and dropped from `MainMenu.unity` on
  re-serialization. Its two remaining match-rule fields are legitimately server-authoritative.

One genuine orphan:

- **`Assets/Scripts/Coin Scripts/CoinCarrierAura.cs`** — attached to no prefab or scene and
  referenced by no other script, despite having a written wiring guide
  (`docs/coin-carrier-aura-wiring.md`). Either wire it or delete it (script + `.meta`).

Not orphans (false positives worth recording): `LoadoutPickerBuilder.cs` and `ScoreboardHudBuilder.cs`
are `[MenuItem]` editor entry points, and all `Assets/Tests/**` files are NUnit entry points.

---

## 6. Wiring: what is one-click vs. hand work

Four editor builders already exist, which substantially reduces the manual burden:

| Menu item | Source | Covers |
|---|---|---|
| `Tools/Economy/Build Economy HUD` | `EconomyHudBuilder.cs` | §3.3 |
| `Tools/Match/Build Results Panel` | `MatchHudBuilder.cs` | §3.5 (likely) |
| `Tools/Match/Build Scoreboard Panel` | `ScoreboardHudBuilder.cs` | §6 killfeed |
| `Tools/Lobby/Extend Loadout Picker` | `LoadoutPickerBuilder.cs` | lobby |

These must be invoked from an open editor. Supporting guides: `docs/buffs-unity-setup-guide.md`,
`docs/scoreboard-unity-setup-guide.md`, `docs/settings-menu-unity-setup-guide.md`,
`docs/coin-carrier-aura-wiring.md`, `docs/player-animation-guide.md`.

**Doable without the editor** (text-editable, deterministic): authoring the two missing
ScriptableObject `.asset` files + `.meta` for §3.1 and assigning their GUIDs in `Gameplay.unity`;
the §3.2 `PlayerCombat` ground-check fix (a code change); assigning `spriteRenderer` GUIDs for §3.6.

**Needs the editor / a human**: running the four builders, and any layout or visual judgment.

**Needs a standalone build** (cannot be checked in Play mode): Video settings, because
`Screen.SetResolution` is ignored in the editor. Reconnection needs **two** standalone builds —
MPPM peers share one `PlayerPrefs` identity, so they collide on the identity token.

---

## 7. Recommended next work, by severity

1. **Author `CombatConfig.asset` + `DifficultyRingConfig.asset` and assign them** (§3.1). Highest
   value by a wide margin: it revives three systems at once. Consider whether the null-fallbacks
   should instead log an error, so this class of defect cannot recur silently.
2. **Fix `PlayerCombat`'s ground check** (§3.2) — preferably by delegating to
   `PlayerMovement.IsGrounded()` rather than assigning a second field.
3. **Run the four editor builders and wire the HUD surfaces** (§3.3, §3.5, killfeed).
4. **Wire the reconnect panel refs** (§3.4).
5. **Decide `CoinCarrierAura`'s fate** — wire or delete (§5).
6. **Assign team-color sprite renderers** (§3.6).
7. **Then, and only then, run the end-to-end verification matrix.** Running it before §1–§4 would
   mostly re-discover these defects at much higher cost.

### Open question raised by the coin retune

Commit `1c68f18` captured an intentional economy retune: coin supply down (`coinsToDrop` 2/3/4 → 1)
and the high side of each coin's team-value pair doubled (5→10, 6→12, 7→14), steepening the reward
for pushing into enemy territory. The buff unlock thresholds were tuned against a "12/45 per-player
average" assumption in the economy spec. **That assumption should be re-derived against the new
supply curve** — the design intent is sound, but the thresholds may no longer land where intended.
This is a tuning question to verify, not a defect.
