# Match Lifecycle — Design

**Date:** 2026-07-29
**Status:** Approved (design), pending implementation plan

## Problem

The match has no life cycle. It starts, and when a flag is captured it freezes
forever on a "Team N Wins!" panel with no way forward. Concretely, from the
current code:

- **`CTFGameManager.EndGame(int winningTeam)`** sets a `[Networked] bool
  GameIsOver` and fires `AnnounceWinnerRpc`, which flips on `gameOverPanel` and
  sets `winnerText`. There is no restart, rematch, return-to-lobby, cleanup, or
  next-match path. Win is triggered **only** by a flag capture
  (`OnCarrierEnteredBase`). Late joiners get the panel re-shown via
  `OnGameOverChanged` in `Spawned`, but the match is otherwise a dead end.
- **`GameSettingsManager`** declares `matchTimeLimit` and `scoreLimit`; **both
  are referenced in zero files.** There is no match timer and no score-limit end
  condition despite the settings existing.
- **`TeamScoreManager`** holds `[Networked] Team1Score/Team2Score` (the
  coin-economy score, separate from CTF flag captures). Nothing consumes these
  as an end condition.
- A working lobby already exists (`LobbyServerState`, `LobbyHostPolicy`,
  `LobbyProtocol`, `LobbyScreenUI`): snapshot roster, balanced auto-teams, the
  designated host (lowest active `PlayerId`) presses Start, and
  `GameNetworkManager.LoadGameplayScene()` does
  `runner.LoadScene(SceneRef.FromIndex(gameplaySceneIndex))`.

This spec adds the missing loop: **start → play → resolve → back to lobby →
repeat**, reusing the existing lobby rather than inventing a parallel one. It
does not resolve the still-open "CTF-vs-coins" scoring question; it assumes a
single authoritative "who won" resolver that both signals feed.

## Decisions (from brainstorming)

- **Post-match loop:** **return to lobby only.** After the results panel the
  server loads `MainMenu.unity`; the host presses Start to play again (which
  reloads `Gameplay.unity`). No rematch-in-place button in v1.
- **Win conditions:** **capture + timer fallback.** Flag capture wins instantly
  (unchanged). `matchTimeLimit` becomes a real end condition so a match can't
  run forever. `scoreLimit` stays **unwired** for v1 — its meaning is entangled
  with the unresolved CTF-vs-coins question.
- **Tiebreak on timer expiry:** higher `TeamScoreManager` coin score wins;
  exactly equal ⇒ **Draw**. No sudden-death/overtime.
- **Dedicated server:** **persist & recycle.** The server process and
  `NetworkRunner` stay up across matches (`GameNetworkManager` is already
  `DontDestroyOnLoad`); players stay connected between matches.
- **Advance from results:** **auto after a countdown**, with a host skip.
  A server `TickTimer` holds the results panel for **20 s**, then auto-loads the
  lobby. The designated host also gets a "Return to Lobby now" button to skip the
  wait. Auto-advance guarantees the loop progresses even if the host leaves or
  goes AFK — important on a headless dedicated server.
- **Results scope:** **transition only.** A minimal PostMatch results panel
  (winner/"Draw" banner + final team scores + return countdown). The rich
  per-player scoreboard (K/D, captures, coins) is a separate item, out of scope.

## Architecture

Two pieces, split along a clear seam:

1. **`MatchManager`** (new `NetworkBehaviour`, singleton, lives in the Gameplay
   scene) — owns the match life cycle: the phase enum, the phase timers, and the
   single "who won" resolver. Server-authoritative.
2. **`CTFGameManager`** (trimmed) — keeps flag/CTF-specific logic and *reports
   into* `MatchManager` instead of ending the game itself. The lone `GameIsOver`
   bool is removed; game-over queries derive from `MatchManager.Phase`.

Keeping lifecycle out of `CTFGameManager` means the resolver has one owner and
other game modes (or the coin economy) can feed the same resolver later without
touching CTF code.

### Match-state model

`MatchManager` replaces the lone `GameIsOver` bool with a single networked enum
plus a networked winner:

```csharp
enum MatchPhase : byte { Warmup, Countdown, Live, PostMatch, Intermission }

[Networked, OnChangedRender(nameof(OnPhaseChanged))]
public MatchPhase Phase { get; set; }

[Networked] public byte Winner { get; set; } // 0 = Draw, 1 = Team1, 2 = Team2

[Networked] TickTimer PhaseTimer { get; set; } // reused per timed phase
```

| Phase | Job | Timer | Input / combat |
|---|---|---|---|
| **Warmup** | Gameplay scene just loaded; grace window for spawn-in and late scene-loaders. No match timer yet. | `warmupTimer` (~3 s) | locked |
| **Countdown** | Pre-match "Starting in 3…2…1". | `countdownTimer` (~3 s) | locked |
| **Live** | Play. Ends on capture or timer expiry. | `matchTimer` = `matchTimeLimit` (0 ⇒ no timer) | active |
| **PostMatch** | Winner locked and announced; world frozen; results panel; auto-advance countdown + host skip. | `intermissionTimer` (**20 s**) | frozen |
| **Intermission** | Server loads `MainMenu`; covers the scene-load gap; lobby re-shows. | — | — |

Each timed phase reuses one `[Networked] TickTimer PhaseTimer` (set on phase
entry from the state authority), rather than one field per phase, so remaining
time is replicated to every client and late joiner. The per-phase timer names
used below (`warmupTimer`, `countdownTimer`, `matchTimer`, `intermissionTimer`)
are the *logical role* of that shared `PhaseTimer` during each phase, not
separate networked fields.

## Transitions

All transitions are decided **only under `HasStateAuthority`**, inside
`MatchManager.FixedUpdateNetwork`, by checking `PhaseTimer.Expired(Runner)` or by
a server-side report. No client-local match state exists.

- **Warmup → Countdown** — `warmupTimer` expired. Gives late scene-loaders and
  player spawn-in a moment to settle before the countdown.
- **Countdown → Live** — `countdownTimer` expired. On entry to Live, read
  `GameSettingsManager.Instance.matchTimeLimit` (minutes → seconds) and arm
  `matchTimer`. If `matchTimeLimit == 0`, no timer is armed and the match can
  only end by capture.
- **Live → PostMatch (capture)** — `CTFGameManager.OnCarrierEnteredBase`
  resolves a valid capture and calls `MatchManager.ReportCapture(Team)` (this
  replaces `CTFGameManager.EndGame`). `MatchManager` sets `Winner` and moves to
  PostMatch. Captures are honored **only while `Phase == Live`** (this also
  prevents a capture landing during Countdown or after time expires).
- **Live → PostMatch (timer)** — `matchTimer` expired. `MatchManager` sets
  `Winner` = the team with the higher `TeamScoreManager` coin score, or `0`
  (Draw) if equal, then moves to PostMatch.
- **PostMatch → Intermission** — `intermissionTimer` (20 s) expired, **or** the
  designated host requests an early advance. The host's "Return to Lobby now"
  click sends a request; the server validates the sender is the current
  `LobbyHostPolicy` host and then advances. Auto-advance means a departed/AFK
  host never strands the match on the results screen.
- **Intermission → (lobby)** — server-only: reset the `gameStarting` latch on
  `GameNetworkManager`, then `runner.LoadScene(MainMenu)`. On load,
  `LobbyScreenUI` re-shows and the persisted `LobbyServerState` roster is intact,
  so the same players are ready to Start again.

## State-reset contract

**Reset happens by scene reload, not by an in-place networked reset routine.**

Because the loop is return-to-lobby-only, every new match is a fresh
`runner.LoadScene(Gameplay)` triggered by the host pressing Start in the lobby.
Fusion despawns the old scene's NetworkObjects and spawns the new scene's, so the
entire match slate is recreated from scratch:

| Must reset | How it resets |
|---|---|
| Both flags (state + position) | Scene-spawned → recreated fresh on Gameplay load |
| `TeamScoreManager` scores + team buffs | Scene `NetworkBehaviour` (not `DontDestroyOnLoad`) → despawned and re-spawned at zero |
| Per-player health / inventory / deposited coins / earned buffs | Player objects are **re-spawned** by `NetworkedSpawnManager` (reading `LobbyTeamChoices`) → fresh state |
| Enemies | Recreated by their spawner on Gameplay load |
| Player positions | Placed at team spawns by `NetworkedSpawnManager` |

In-place reset is **explicitly not built** — it would only be needed for
rematch-in-place, which we cut. The remaining reset work is therefore an
**audit**, not a routine: confirm no match-specific state leaks across the scene
reload via a `DontDestroyOnLoad` object or a `static` field.

- `GameSettingsManager` persists (`DontDestroyOnLoad`) **intentionally** — it
  holds config, not match state.
- `LobbyTeamChoices` / `LobbyLoadoutChoices` are the intended roster handoff and
  persist by design; they are re-read at the next spawn.
- `GameNetworkManager.gameStarting` is the one-way latch that today never resets
  after a match; the Intermission step must clear it so the next Start works.
- Anything else static or `DontDestroyOnLoad` that references match state (stale
  singleton back-references, cached lists) must be verified during
  implementation.

**Tradeoff, stated plainly:** scene-reload reset is simpler and correctness-free
(no field-by-field zeroing to keep in sync as new networked state is added), at
the cost of a brief scene-load hitch and a lobby round-trip between matches —
acceptable, and in fact the chosen loop already routes through the lobby.

## Networking / late-join

- `Phase`, `Winner`, and `PhaseTimer` are `[Networked]`, so clients and late
  joiners render the correct phase and remaining time. `OnPhaseChanged` is also
  invoked from `Spawned` (same pattern as today's `OnGameOverChanged`) so a
  joiner reconciles immediately.
- **Always-interested requirement:** `MatchManager`, like the flags and
  `TeamScoreManager`, must be in the always-replicated set under interest
  management. If it falls under a per-player AoI region, the phase/timer/results
  HUD would vanish for distant players in a 20-player match — the known AoI
  footgun (managers/flags/carrier must be always-interested).
- **Dedicated server** (no local player) is the state authority: it runs every
  transition headless, and all UI is client-side, driven purely off the
  networked `Phase`. The "host" for the skip button is the lobby's designated
  host (lowest `PlayerId`, `LobbyHostPolicy`); the skip is only a *request* — the
  server owns the transition.
- **Mid-match join per phase:**
  - *Warmup / Countdown* — joiner spawns and sees the countdown from the
    networked timer.
  - *Live* — joiner drops into the ongoing match with the correct remaining time
    and current scores.
  - *PostMatch* — joiner sees the results panel and the return countdown.
  - *Intermission* — joiner follows the in-progress scene load into the lobby;
    Fusion keeps scene state in sync.
- **20-player cost:** no per-tick polling is introduced. Capture is already
  event-driven; the timer is a single `TickTimer.Expired` check per tick on the
  authority. Phase changes are rare networked writes.

## UI surface (per phase)

Event-driven, under `Assets/Scripts/Hud/` — no polling. `MatchManager` exposes a
C# event (`PhaseChanged`, and the timer value) mirroring
`TeamScoreManager.ScoresChanged`; a new `MatchPhaseHud` subscribes.

- **Warmup** — "Waiting for players…" / spawn-in.
- **Countdown** — large center "3… 2… 1… GO".
- **Live** — match timer (mm:ss) and team scores along the top; reuse
  `TeamScoreDisplay`. The timer is hidden when `matchTimeLimit == 0`.
- **PostMatch** — the repurposed `gameOverPanel` becomes the results panel:
  winner banner ("Team 1 Wins!" / "Team 2 Wins!" / "Draw"), final team scores,
  "Returning to lobby in N…", and a host-only "Return to Lobby now" button.
- **Intermission** — brief "Loading lobby…" while the scene loads.

The existing `gameOverPanel` / `winnerText` wiring is reused for the results
panel; `AnnounceWinnerRpc` is replaced by the `OnPhaseChanged` render reading
`Winner`, so late joiners get the correct panel without a missed RPC.

## Migration from the current code

- Remove `[Networked] bool GameIsOver` and `AnnounceWinnerRpc` from
  `CTFGameManager`.
- `CTFGameManager.EndGame(int)` → `MatchManager.ReportCapture(Team)`; the
  `OnCarrierEnteredBase` / `OnFlagReturnedHome` guards change from
  `GameIsOver` to `Phase != MatchPhase.Live`.
- `CTFGameManager.IsGameOver()` → derives from
  `Phase is PostMatch or Intermission`.
- `RPC_ShowNotification` and the flag references stay on `CTFGameManager`.
- `GameNetworkManager` gains a reset of `gameStarting` on the return-to-lobby
  path; `LoadGameplayScene` is unchanged.

## Testing / verification

Unity + Photon Fusion — the authoritative check is manual play, per project
convention.

**EditMode (pure C#, runnable outside Unity with the bundled-Roslyn workaround):**

- Winner resolution: capture sets the capturing team; timer expiry picks the
  higher coin score; equal coin scores ⇒ Draw (`0`). Pure logic, extracted so it
  takes scores/capturer as inputs.
- Phase-transition guard: captures are ignored unless `Phase == Live`.

**Manual (single-player `singlePlayerMode=true` Host, then Multiplayer Play Mode
1 host + 1 client):**

1. Start match → Warmup → Countdown "3…2…1" → Live (input locked until Live).
2. Capture a flag → PostMatch results panel with the correct winner and scores;
   world frozen; countdown from 20 s; auto-returns to the lobby at 0.
3. Host "Return to Lobby now" skips the wait.
4. With `matchTimeLimit > 0` and no capture, the timer expires → results show the
   higher-coin-score team, or "Draw" when equal.
5. Back in the lobby, press Start again → a **fresh** match (flags home, scores
   zero, full health/inventory, enemies respawned). Confirm no leaked state.
6. Late-join into each phase (Warmup, Live, PostMatch) → the joiner sees the
   correct phase, remaining time, and scores.
7. Host leaves during PostMatch → auto-advance still returns everyone to the
   lobby (no dead end).

## Non-goals

- Rematch-in-place (return-to-lobby is the only loop in v1).
- `scoreLimit` as a win condition (deferred with the CTF-vs-coins question).
- Sudden-death / overtime.
- Full per-player stats scoreboard (separate item).
- Host-triggered-only advance (auto-advance is the safety net).
- In-place networked match reset (scene reload does the reset).
- Per-match session teardown / re-host (server persists and recycles).
- Resolving the CTF-vs-coins scoring authority — this spec assumes the single
  "who won" resolver that both signals feed.
