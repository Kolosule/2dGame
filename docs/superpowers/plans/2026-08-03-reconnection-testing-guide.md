# Reconnection — Manual Verification Guide

Covers [the design spec](../specs/2026-07-29-reconnection-design.md). All eight implementation
tasks are code-complete and reviewed, but **no implementer had a Unity editor** — this feature has
**zero in-editor verification**. This guide is how you find out whether it actually works.

Requirements: one server (host or dedicated build) + at least 2 clients (editor, standalone
builds, or Multiplayer Play Mode), with a console visible for the server and for whichever client
you're actively dropping. You are watching console output as much as the screen — several checks
below only fail silently on screen and loudly in the log.

---

## 0. Setup — do this first

1. **Add `ReconnectController` to the persistent `GameNetworkManager` GameObject in
   `MainMenu.unity`** (select it, **Add Component ▸ Reconnect Controller**). **This is required,
   not optional.** `ReconnectController` is what `GameNetworkManager.TryBeginReconnect` looks for
   on drop; without it, every drop falls straight through to the plain "Disconnected" menu state —
   the feature is entirely inert, and nothing logs an error to tell you that. If none of the
   Retry-loop checks below ever show an attempt counter, this is the first thing to check.
2. **Optional: wire `Reconnect Panel` and `Cancel Reconnect Button` on `MainMenuUI`.** The status
   text alone (`"Connection lost — reconnecting… (attempt N of 5)"`) works without them. But if you
   skip the Cancel button, you have no UI path to trigger the Cancel case in the Retry loop section
   (step 6) — wire it if you intend to run that step.
3. **Identity collision warning — read before running anything below.** `PlayerPrefs` is
   per-product, not per-process: on Windows it's one registry key derived from company + product
   name. The Unity editor salts its own key automatically (suffix `.editor`), so an editor peer
   never collides with a standalone build. Two **standalone builds** on the same machine share an
   identity unless you salt them yourself: launch each with a different `-identitySuffix`, e.g.
   `Game.exe -identitySuffix alpha` and `Game.exe -identitySuffix bravo`.
   **What collision looks like:** if two peers do share an identity, the second one to connect is
   seated as a brand-new player — no team, no stats, no deposited value carried over. This looks
   exactly like "reconnection is broken," but it's the designed duplicate-token behavior (edge case
   in the spec: a live duplicate token is ignored, not enforced). If a restore test fails and your
   two clients are both unsalted standalone builds, fix the launch args before you file it as a bug.

---

## 1. Core restore (1 server + 2 clients, mid-match)

**Keep the server console visible for every step below.** The server captures a leaver's deposited
value by reading it off their avatar; if `GameNetworkManager`'s disconnect handling ever ran after
`NetworkedSpawnManager` had already torn the avatar down, that read would fail. This ordering is
guaranteed by construction, not by anything documented in Fusion, so the code carries a deliberate
tripwire. Watch for this exact line:

```
❌ ServerCaptureForReconnect: no avatar for Player N
```

**If it appears, treat it as a hard failure**, even if everything else about the test looks fine.
Team, name, and stats restore through a separate path and will still look correct — only the
deposited value (and the buff tiers derived from it) silently fails to carry over. A passing test
with this line in the log is not a pass.

| # | Do | Expect | Fails if |
|---|---|---|---|
| 1 | Get client B into a live match. Have B pick up the enemy flag and collect some coins, then disconnect B (disable B's network adapter, or close B's process) | B's flag drops at B's last position; B's coins **scatter at that same position** (new behavior — confirm explicitly, don't assume); B's avatar despawns; B's scoreboard row disappears entirely | The flag or coins vanish instead of dropping/scattering; B's avatar lingers; B's scoreboard row stays visible with stale data |
| 2 | Reconnect B (retry loop or manual Join, same client) | Same team, same nickname, same deposited value and buff tiers, same buff loadout order, scoreboard row restored with its prior counters (kills/deaths/captures/etc. intact), spawned at the **team spawn** at **full health** with **no coins in hand**. No lobby team-pick screen appears at any point | B lands on a different or auto-assigned team; deposited value/tiers reset to 0; loadout order reverts to the config default; scoreboard row starts fresh at zero instead of resuming; B spawns at a stale/last-known position instead of team spawn; the console tripwire above fired |
| 3 | Kill B's process entirely (not just disconnect) and relaunch it, then reconnect | Identical result to step 2 | Anything differs from step 2 — this would mean the identity is living in memory instead of `PlayerPrefs`, so it doesn't survive a real process restart |

---

## 2. Retry loop

**Correction to the spec's headline number:** the backoff schedule itself is 1/2/4/8/8 seconds
(23s), but each attempt also carries its own 15-second wall-clock deadline so a `StartGame` call
that never completes can't stall the loop forever. **Worst case before falling back to the menu is
~98 seconds, not 23.** If you're timing step 5 below with a stopwatch expecting ~23s, you will
conclude a working feature is hung. Give it the full ~98s before deciding attempt exhaustion isn't
happening.

| # | Do | Expect | Fails if |
|---|---|---|---|
| 4 | With B in a live match, disable B's network adapter, then re-enable it partway through a backoff wait | The reconnecting overlay/status appears on B with an attempt counter (`"Connection lost (...) — reconnecting… (attempt N of 5)"`), Join/Host disabled while it runs; after re-enabling, the loop's next attempt succeeds and B is fully restored per section 1, step 2 | No overlay appears at all (check section 0, step 1 — `ReconnectController` is probably missing); the loop doesn't pick up the restored connection even after re-enabling |
| 5 | Same as step 4, but leave the adapter disabled for the full loop; once it exhausts, manually press Join | After **~98 seconds** (not 23 — see correction above), B lands on the main menu with a "could not reconnect" reason; the subsequent manual Join succeeds and B is still fully restored — the hold survives giving up, because it lasts the rest of the match, not a grace window | The menu appears much earlier or later than ~98s; a manual Join after exhaustion comes back as a fresh player instead of restored |
| 6 | Start the retry loop (disable adapter), then press **Cancel** partway through; once back at the idle menu, re-enable the adapter and manually press Join | Cancel gives a clean, immediate return to the idle menu — no lingering "reconnecting" state — and the subsequent manual Join succeeds normally, proving the runner was properly torn down and rebuilt, not left in a half-dead state | Cancel leaves the UI stuck in reconnecting state; the subsequent manual Join fails or hangs |
| 7 | With B in a live match, quit B deliberately (close the window, or Alt‑F4/task-kill the process) | **No** reconnecting overlay appears anywhere, and no retry loop runs — this is the ordinary "intentional disconnect" path, unchanged from before this feature | An overlay or attempt counter appears for a deliberate quit — this means the intentional-disconnect latch isn't being set before shutdown |

---

## 3. Phase behavior

| # | Do | Expect | Fails if |
|---|---|---|---|
| 8 | Disconnect a player while still in the **lobby** (pre-match, MainMenu scene) | Their seat is released immediately — no hold. Rejoining is a plain fresh join with an auto-assigned team, same as before this feature existed | The player's seat stays reserved (blocks a would-be joiner), or rejoining tries to restore stale/nonexistent state |
| 9 | Disconnect a player during **PostMatch** (results screen, after the match has resolved) | Released, same as step 8. If they rejoin before the scene reloads to the lobby, they ride the return-to-lobby scene load in with everyone else, as a fresh player | Their seat stays held into the next match, or they get a partial/broken restore during the resolve screen |
| 10 | Play a full match to completion with a player still held (disconnected mid-match, never reconnected) through the return-to-lobby transition | The hold is gone once the lobby scene loads — if that player connects again, they come back as a **brand-new** player (fresh team assignment, zero stats), not restored | They reconnect after the match ends and still get their old team/stats back — this means the registry isn't being cleared on the return-to-lobby transition |

---

## 4. Seat reservation

Testable without 20 peers.

| # | Do | Expect | Fails if |
|---|---|---|---|
| 11 | Temporarily set `GameNetworkManager.maxPlayers` to **2** in the Inspector (on the persistent `GameNetworkManager` GameObject in `MainMenu.unity`). Fill the session with 2 players, then disconnect one of them (don't let them reconnect yet). While their seat is held, attempt to connect a **third**, unrelated peer. Then reconnect the original disconnected player | The third peer is **refused** a connection while the seat is held (2 active + 1 held = at cap). The original player can still get back in — their token is recognized regardless of the cap | The third peer is let in anyway (held seats aren't counted against the cap); or the original player is refused on rejoin (their own held seat is blocking them, which shouldn't happen — a known token is always accepted) |

**Set `maxPlayers` back to 20 when you're done.** Leaving it at 2 will make every subsequent test in
this guide (or any other playtest) look like the server is full.

---

## 5. Server death

| # | Do | Expect | Fails if |
|---|---|---|---|
| 12 | With 2+ clients in a live match against a dedicated server, kill the server process | Every client's retry loop runs (per section 2), fails against the now-dead endpoint, and each lands on the main menu with the shutdown reason after the full ~98s retry budget. Nothing migrates — there is no new host, no reconnect target; the match is simply over for everyone | Any client appears to reconnect successfully (there's nothing to reconnect to — that would indicate a stale cached connection); a client hangs indefinitely instead of eventually reaching the menu |

---

## 6. EditMode suite

| # | Do | Expect | Fails if |
|---|---|---|---|
| 13 | **Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All** | All green, including `ReconnectRegistryTests`, `ReconnectPolicyTests`, `ReconnectBackoffTests`, `IdentityTokenCodecTests`, and the added cases in `MatchRulesTests` / `LobbyServerStateTests` | Any of the above fail or don't appear in the run at all (the latter means the test assembly didn't recompile — check the Console for compile errors first) |

Equivalent from the command line, if you'd rather not open the Test Runner window:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.3.0f1/Editor/Unity.exe" -batchmode -nographics -projectPath "C:/Users/1/Documents/GitHub/2dGame" -runTests -testPlatform EditMode -testResults results.xml -logFile unity-tests.log
```

---

## 7. Quick-diagnosis table

Three failure modes in this feature are non-obvious enough that a tester unfamiliar with the
implementation will misread a working feature as broken (or vice versa). Check this table before
concluding anything below is a bug.

| Symptom | Likely cause | Not a bug if... |
|---|---|---|
| Menu doesn't appear until ~90-100s after the network drops in section 2 | This is expected — see the retry-loop correction above | The time is in the ~90-100s range, not ~23s |
| A rejoining player comes back as a brand-new player with no state | Duplicate identity token — see section 0, step 3 | Your two test peers are unsalted standalone builds sharing one `PlayerPrefs` store |
| `❌ ServerCaptureForReconnect: no avatar for Player N` in the server console | The avatar-teardown-before-capture ordering broke | This should never be "not a bug" — if you see it, deposited value silently isn't being preserved for that drop even though everything else looks fine. Report it |
