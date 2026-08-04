# Reconnection / Disconnection Handling — Design

**Date:** 2026-07-29
**Status:** Approved (design), no implementation plan authored
**Game:** Unity 6.3 Photon Fusion 2 2D PvPvE arena, Host/Client + dedicated server, ~20 players

## Problem

A dropped connection is terminal in both directions: the server throws the player's entire match
away, and the client bounces to the main menu with no way back into the match it just left. On a
weekend-only dedicated server where a match runs long enough to accumulate real progression, a
30-second router blip currently costs a player everything they earned.

Concretely, from the current code:

- **The server discards a leaver immediately and completely.**
  [`NetworkedSpawnManager.OnPlayerLeft`](../../../Assets/Scripts/NetworkedSpawnManager.cs:114)
  drops the carried flag, `runner.Despawn`s the avatar, and decrements the team count.
  [`GameNetworkManager.OnPlayerLeft`](../../../Assets/Scripts/GameNetworkManager.cs:361) removes
  them from `serverLobby` and from all three handoff dictionaries (`LobbyTeamChoices`,
  `LobbyNicknameChoices`, `LobbyLoadoutChoices`). There is no grace period and nothing is
  preserved.
- **Carried coins evaporate on a leave, unlike on a death.**
  [`PlayerStatsHandler.Die`](../../../Assets/Scripts/Player/PlayerStatsHandler.cs:186) drops the
  flag *and* scatters the player's coins back into the world via
  `NetworkedPlayerInventory.OnPlayerDeath(position)`. The leave path drops only the flag, so a
  disconnecting carrier deletes their coins from the economy entirely.
- **The client just bounces.**
  [`GameNetworkManager.OnDisconnectedFromServer`](../../../Assets/Scripts/GameNetworkManager.cs:400)
  is an empty stub;
  [`OnShutdown`](../../../Assets/Scripts/GameNetworkManager.cs:373) shows the menu with
  `"Disconnected: {reason}"`;
  [`OnConnectFailed`](../../../Assets/Scripts/GameNetworkManager.cs:409) shows
  `"Connection failed"`. No retry, no distinction between an intentional quit and a drop.
- **Rejoining creates a stranger.** Reconnect is a plain `StartClient()`, which yields a new
  `PlayerRef`, a fresh auto-assigned team from
  [`LobbyServerState.PlayerJoined`](../../../Assets/Scripts/Net/LobbyServerState.cs:29), zero
  deposited value, and a brand-new `MatchStatsManager` row. Nothing maps the returning human to
  their prior state.
- **The identity hook exists but is unused.**
  [`OnConnectRequest`](../../../Assets/Scripts/GameNetworkManager.cs:402) already receives a
  `byte[] token` and unconditionally calls `request.Accept()`; its comment earmarks the spot for
  future connection policy.
- **Stats are keyed by `PlayerId`, not by human.**
  [`MatchStatsManager`](../../../Assets/Scripts/Stats/MatchStatsManager.cs:25) stores a
  `NetworkArray<PlayerStatEntry>` indexed by `PlayerId`, and the scoreboard skips any `Active`
  slot whose id is no longer in `Runner.ActivePlayers`. A rejoiner arrives with a *different*
  `PlayerId`, so restoring stats means copying the saved row into the new slot — the old row
  cannot simply be "kept."

This spec makes a drop survivable: the server holds the player's earned state for the rest of the
match, keyed by a client-persisted identity token, and the client automatically tries to get back
in.

## Decisions (from brainstorming)

| # | Decision |
|---|---|
| 1 | **Hold duration is the rest of the match** — no grace timer. The hold has exactly one expiry event: the match ending. |
| 2 | **A held slot reserves its seat** against the 20-player cap. Implemented as a stricter `OnConnectRequest` gate, not by raising Fusion's `PlayerCount` (see "Seat reservation"). |
| 3 | **Preserved:** team, `TotalDepositedValue` (and the buff tiers derived from it), buff loadout order, the `MatchStatsManager` row, display name. Team score contribution is already banked in `TeamScoreManager` and never rewinds. |
| 4 | **Reset:** health (full), position (team spawn), velocity/physics, active buff timers. Coins in hand are **scattered at the drop point**, exactly as on death. The carried flag is **always dropped immediately**. |
| 5 | **The avatar is despawned on disconnect, not frozen.** A drop is treated as a death you don't respawn from yet. No frozen zombie to shoot or body-block, no replication cost for an absent player. |
| 6 | **Rejoin spawns at the team spawn with full health and empty hands** — the existing respawn condition, with restored team and progression. No last-position restore, so dropping cannot be used to escape a fight. |
| 7 | **Client auto-retries 5 times** with 1/2/4/8/8 s backoff (23 s of waiting), then falls back to the main menu. A Cancel button exits the loop at any point. Each attempt also carries a 15 s wall-clock deadline (added during implementation — without it a `StartGame` that never completes parks the loop forever), so the worst case before the menu is ~98 s, not 23. |
| 8 | **A duplicate token on a live connection is ignored, not enforced.** The newcomer is seated as a brand-new player; nobody is kicked and nobody is refused. |
| 9 | **The token is an identity hint, not a credential.** It unlocks only state the holder already earned this match. Accepted risk on a private server; called out rather than papered over. |
| 10 | **The hold applies only while a match is actually running.** Lobby-phase and PostMatch-phase drops release fully, as today. |
| 11 | **Restoration reuses the three existing lobby handoff dictionaries** rather than adding a parallel spawn path, so `NetworkedSpawnManager`'s spawn logic is untouched. |

## Architecture

Three new pieces plus edits to two existing callbacks. The seam is deliberately narrow: all
server-side hold logic lives in one testable pure-C# class, and all client-side retry logic lives
in one component, so neither `GameNetworkManager` (already 584 lines) nor `NetworkedSpawnManager`
grows a second responsibility.

| Piece | Kind | Lives in | Job |
|---|---|---|---|
| `PlayerIdentity` | static class, pure C# + `PlayerPrefs` | `Assets/Scripts/Net/` | Mint, persist, and expose the local player's stable identity GUID as connection-token bytes. |
| `ReconnectRegistry` | pure C# class | `Assets/Scripts/Net/` | Server-side token → held-state map. No Unity or Fusion types in its API, so it is EditMode-testable like `LobbyServerState`. |
| `ReconnectController` | `MonoBehaviour` | `Assets/Scripts/Net/` | Client-side drop detection, retry loop with backoff, runner rebuild, and reconnecting-overlay state. |

### `PlayerIdentity` — the stable client id

```
key:   "reconnect.identity.v1"   (PlayerPrefs)
value: Guid.NewGuid().ToString("N")   // 32 hex chars, minted once on first run
wire:  the raw 16 bytes, as StartGameArgs.ConnectionToken
```

The GUID is minted on first access and never rotated. It is sent on **every** connect — host,
client, and reconnect attempt alike — so there is no "reconnect mode" on the wire; the server
simply notices that a token matches a held slot.

**Local-testing caveat (required, not optional):** `PlayerPrefs` is per-*product*, not per-process —
on Windows it is a single registry key derived from company + product name — so any two clients on
one machine present the *identical* GUID by default. That includes Unity Multiplayer Play Mode
virtual players and two copies of the same standalone build.

Two salts address it: `UNITY_EDITOR` appends a flat `.editor`, and a `-identitySuffix <value>`
command-line argument gives each build its own key (following the existing
`NetworkBootMode.Resolve` command-line pattern). The suffix must be **stable across relaunches of
the same peer** — a per-process salt would mint a new identity on every restart and break exactly
the reconnect-after-relaunch case worth testing.

**MPPM is therefore not a usable test route for this feature.** An earlier draft of this spec
claimed the editor key is salted with the virtual-player index; it is not, because that would
require depending on the MPPM package's API, which this project could not verify was resolvable.
The flat `.editor` salt only separates *the editor* from *a build* — every MPPM virtual player in
one editor still shares one identity and so exercises the duplicate-token path 100% of the time.
The working local route is **two standalone builds launched with different `-identitySuffix`
values**, or one build plus the editor.

### `ReconnectRegistry` — the server-side hold

A `Dictionary<string, HeldSlot>` keyed by the token's hex string, owned by `GameNetworkManager` on
the server only.

| `HeldSlot` field | Source at capture |
|---|---|
| `Team` | `LobbyTeamChoices` / `serverLobby.TeamOf(id)` |
| `DisplayName` | `LobbyNicknameChoices` |
| `LoadoutOrder` (`byte[]`) | `LobbyLoadoutChoices` |
| `TotalDepositedValue` | `PlayerBuffs.TotalDeposited` on the avatar |
| `Stats` (the `PlayerStatEntry` counters) | `MatchStatsManager.TryGetEntry(playerId)` |

`LoadoutOrder` is easy to forget and load-bearing: `LobbyLoadoutChoices.Remove` runs on leave, and
without the saved order a rejoiner silently comes back on `BuffLoadoutConfig`'s default priority
instead of the order they chose in the lobby.

The registry exposes `Capture`, `TryClaim` (which removes the slot), `HeldCount`, and `Clear`. It
holds no Fusion types — the caller passes plain values — so its rules are covered by EditMode
tests.

## Disconnect: capture, then release

`OnPlayerLeft` runs on the server across both `GameNetworkManager` and `NetworkedSpawnManager`.
The ordering below is the whole contract; every step already exists except capture and the coin
scatter.

1. **Drop the carried flag.** Unchanged, and still first — the flag must never be stranded on an
   absent player, whatever else happens. This is why it is step 1 rather than part of the teardown:
   it runs while the avatar still exists, so the flag lands at the player's last position and the
   carrier-marker cleanup can still run.
2. **Scatter carried coins** at the avatar's position via `NetworkedPlayerInventory.OnPlayerDeath`.
   New behavior, and the reason it belongs here: a drop should cost exactly what a death costs, and
   today a leaver's coins are deleted from the economy instead of returned to it.
3. **Capture the `HeldSlot`** into the registry, keyed by the leaver's connection token — but only
   if a match is actually running (see "Match-phase behavior").
4. **Despawn the avatar**, decrement the team count, clear the three handoff dictionaries, remove
   from `serverLobby`, re-broadcast the lobby. All unchanged.

After step 4 the server's live state is identical to today's. The registry is the only addition,
and it is invisible to every existing system.

### Hold duration and release

There is **no `TickTimer`**. The hold lasts the rest of the match, which gives it exactly one
expiry event:
[`GameNetworkManager.BeginReturnToLobby()`](../../../Assets/Scripts/GameNetworkManager.cs:332) —
the single server-only chokepoint that `MatchManager` already calls when entering Intermission —
calls `registry.Clear()`. `OnShutdown` clears it too, alongside the existing
`LobbyTeamChoices.Clear()` / `CoinRegistry.Clear()` block.

This is a genuine simplification rather than a deferral: a timed grace window would need a
server-side `TickTimer` per held slot, a tick-rate poll to notice expiry, and a decision about what
"expired" means for a seat that is simultaneously reserved. Rest-of-match hold has none of that,
and the match boundary is already a hard reset of every other piece of match state (the
match-lifecycle spec's scene-reload reset contract).

## Rejoin: the restoration handshake

The ordering requirement is that restoration completes **before the player is interactive and
before other clients ever see them**. That falls out of Fusion's spawn callback rather than needing
any new sequencing primitive.

1. **`OnConnectRequest(runner, request, token)`** — the accept/refuse decision, and the only place
   it is made:
   - Token matches a held slot → `Accept()` unconditionally. They are reclaiming a seat that is
     already reserved for them.
   - Otherwise → `Accept()` if `Runner.ActivePlayers.Count + registry.HeldCount < maxPlayers`, else
     `Refuse()`.
2. **`OnPlayerJoined(runner, player)`** (server) — the token is re-read for the now-existing
   `PlayerRef` via `Runner.GetPlayerConnectionToken(player)`, which is what bridges the token to a
   `PlayerRef` (`OnConnectRequest` fires before any `PlayerRef` exists). Then:
   - **Held slot found** → `TryClaim` it, and write the saved values straight into the three
     existing handoff dictionaries — `LobbyTeamChoices.Set(player, held.Team)`,
     `LobbyNicknameChoices.Set(player, held.DisplayName)`,
     `LobbyLoadoutChoices.Set(player, held.LoadoutOrder)` — then seat them in `serverLobby` on the
     *held* team rather than through `PlayerJoined`'s balanced auto-assign. This is the key
     structural decision: everything downstream reads those dictionaries already, so no spawn code
     needs to know reconnection exists.
   - **No held slot** → today's `ServerHandleJoin(player)` path, unchanged.
3. **`NetworkedSpawnManager.TrySpawnPlayer(player)`** runs from its own `OnPlayerJoined` and finds
   a `LobbyTeamChoices` entry, exactly as it would for a lobby-originated player. No changes.
4. **`Runner.Spawn(..., onBeforeSpawned)`** — the existing `OnPlayerSpawned` callback, which
   already does `SetPlayerObject`, `PlayerTeamData.SetTeam`, and `PlayerBuffs.ServerInitLoadout`,
   gains one line: `PlayerBuffs.ServerRestoreDeposited(held.TotalDepositedValue)`. **This callback
   runs before the object is replicated**, so the avatar's very first snapshot already carries the
   restored deposited value and its derived buff tiers. There is no frame in which a rejoiner is
   visible, interactive, and at tier 0 — and no RPC-ordering problem to reason about.
5. **`MatchStatsManager.RestoreEntry(newPlayerId, team, name, savedCounters)`** — a sibling of
   [`RegisterPlayer`](../../../Assets/Scripts/Stats/MatchStatsManager.cs:62) that writes the saved
   counters where `RegisterPlayer` writes zeros. Called from `TrySpawnPlayer` in place of
   `RegisterPlayer` when a claim happened. Necessary because the roster array is indexed by
   `PlayerId` and the rejoiner has a new one; the stale row under the old `PlayerId` is already
   invisible (the scoreboard filters on `Runner.ActivePlayers`) and is fully overwritten if a
   future player is ever assigned that id, since `RegisterPlayer` writes a complete struct.

**The lobby team-pick is skipped implicitly**, not by a special case: the restored player already
has a `LobbyTeamChoices` entry when they arrive, and a mid-match joiner is pulled into the running
Gameplay scene by Fusion's scene sync rather than through the lobby Start gate.

### An adjacent fix this spec claimed, retracted during implementation

An earlier draft of this section asserted that `OnSceneLoadDone` early-returning on a non-menu
scene leaves a mid-match joiner with the lobby panel drawn on top of gameplay, and scoped a fix
for it into this work. **That was wrong, and the fix was reverted.** Only `GameNetworkManager`,
`GameSettingsManager`, and `TeamManager` call `DontDestroyOnLoad`; the menu canvas does not, so
`LobbyScreenUI` is destroyed by the gameplay scene load and there is no panel left to hide. Any
`lobbyUI.Hide()` on that path is a no-op against a Unity fake-null reference.

Recorded here rather than deleted because the claim is the kind a future reader would otherwise
re-derive and re-fix.

## Preserved vs reset contract

| Preserved across a drop | Why |
|---|---|
| Team | Prevents drop-to-switch-teams and keeps balance stable; also what makes the lobby pick skippable. |
| `TotalDepositedValue` → buff tiers | The whole point. This is a match's worth of economy progress. |
| Buff loadout order | Otherwise a rejoiner silently reverts to the config default order. |
| `MatchStatsManager` row (kills, deaths, captures, coins deposited, carry seconds, returns) | Copied into the new `PlayerId` slot. |
| Display name | Keeps the scoreboard row identifiable instead of reverting to `Player N`. |
| Team score contribution | Nothing to do — already banked in `TeamScoreManager` and never rewound by a leave. |

| Reset on rejoin | Why |
|---|---|
| Health → full | Rejoin is a respawn. Preserving HP punishes an involuntary drop for no design gain. |
| Position → team spawn | No last-position restore, so a drop can never be an escape-and-flank. |
| Velocity and all transient physics | Nothing meaningful to carry across a despawn. |
| Active buff timers (stealth duration, stealth cooldown) | Wall-clock effects on an absent player are meaningless; tiers are the durable part, timers are not. |
| Coins in hand | **Scattered at the drop point**, recoverable by anyone — a drop costs what a death costs. |
| Carried flag | **Always dropped immediately**, before anything else. Never held, never restored. |

**While held, the player has no scoreboard row.** They are absent from `Runner.ActivePlayers`, so
the scoreboard's existing membership filter skips them and the row reappears intact on rejoin. This
matches the scoreboard spec's Decision 7 ("a leaving player's row is simply dropped") with no ghost
-row concept and no extra networked state.

## Client-side reconnection loop

`ReconnectController` on the persistent `DontDestroyOnLoad` object, next to `GameNetworkManager`.

**Intentional quit vs drop.** `GameNetworkManager` sets an `intentionalDisconnect` latch before any
deliberate `runner.Shutdown()` (`OnDestroy`, `OnApplicationQuit`, and any future quit/leave
button). `OnDisconnectedFromServer` and `OnShutdown` branch on it: set → today's behavior (menu +
status), clear → hand off to the retry loop.

**The loop.** 5 attempts, backoff 1 / 2 / 4 / 8 / 8 seconds (23 s of waiting), plus a 15 s
wall-clock deadline per attempt so a `StartGame` that never returns cannot park the loop
indefinitely — worst case ~98 s before the menu. Each attempt is a
`StartClient()` against the same session name with the same identity token. Success ends the loop —
the server does the rest, and the client needs no special-case code. Exhaustion lands on the
existing terminal state: `menuUI.ShowStatus("Could not reconnect: {reason}")`, `SetBusy(false)`.
Because the hold lasts the rest of the match, a player who gives up (or cancels) can still
reconnect manually from the menu minutes later and get their state back.

**UX surface.** No new screen. `MainMenuUI` gains a "reconnecting" state: status text
(`"Connection lost — reconnecting… (attempt N of 5)"`), `SetBusy(true)` to disable Join/Host while
the loop runs, and a Cancel button that aborts to the idle menu. Reusing the menu is correct rather
than lazy — when the runner dies, every gameplay `NetworkObject` goes with it, so there is no live
gameplay left to overlay.

**Two Fusion realities the loop must respect:**

- **A shut-down `NetworkRunner` cannot be restarted.** `GameNetworkManager` currently adds its
  runner exactly once, in `Start()`. Each retry attempt must destroy the dead runner component and
  rebuild the full stack: `NetworkRunner`, `RunnerSimulatePhysics2D` (with
  `ClientPhysicsSimulation.SimulateForward` — the documented client-prediction requirement),
  `PooledNetworkObjectProvider`, `NetworkSceneManagerDefault`, `NetworkInputProvider`, and the
  callback registrations. The existing `OnShutdown` cleanup (`ClearPools()`, `CoinRegistry.Clear()`)
  is exactly the right teardown for this and needs no change.
- **Load `MainMenu` locally before the first attempt.** A plain, non-networked
  `SceneManager.LoadScene(menuSceneIndex)`, so the client is not reconnecting from a Gameplay scene
  full of despawned husks. Fusion's scene sync then drives the load back into gameplay on success.

## Match-phase behavior

The hold is only armed when there is a match to hold state for. The server gates on
`MatchManager.Instance` (which exists only in the Gameplay scene) and its networked `Phase`.

| Phase at time of drop | Behavior |
|---|---|
| **Lobby** (MainMenu scene, `MatchManager.Instance == null`) | Full release, exactly today. Nothing is worth preserving pre-match, and reserving lobby seats would lock out real joiners before the match even starts. Rejoin is a normal fresh join with balanced auto-assign. |
| **Warmup / Countdown / Live / SuddenDeath** | Full hold as specified above. Rejoin restores and respawns at the team spawn. |
| **PostMatch / Intermission** | Full release. The match is resolved and the scene reload is about to reset everything anyway; holding state that is seconds from deletion buys nothing. A player who reconnects during this window arrives as a fresh player and rides the return-to-lobby scene load in with everyone else. |

## Seat reservation and scale

A held slot reserves its seat, but **Fusion's `PlayerCount` stays at 20**. Fusion frees its own
slot the moment a player disconnects, so reservation is enforced one level up, in
`OnConnectRequest`: an unknown token is refused when `ActivePlayers.Count + registry.HeldCount >=
maxPlayers`, while a known token is always accepted. Since every held slot was previously an active
one, the invariant `active + held ≤ 20` holds by construction — no `PlayerCount` headroom hack, and
no risk that a bug in our gate lets a 21st player onto the server, because Fusion's own cap is
still there as a backstop.

**Cost at 20 players:** the registry is a plain server-side dictionary of at most 20 small entries,
read and written only on join and leave. Zero per-tick work, zero bandwidth, no `[Networked]` state
added. The only new wire traffic is `RestoreEntry` writing a stats row that would have been written
(as zeros) anyway.

## Edge cases

- **Reconnect after release** (match ended, or the registry was cleared) — no held slot is found, so
  the player is seated as a brand-new player with a balanced team assignment. Today's behavior, and
  the correct one: their old match is over.
- **Duplicate token while the original is still connected** — the token is ignored and the newcomer
  is seated as a brand-new player. Nobody is kicked and nobody is refused. The alternatives are both
  worse here: refusing locks a real player out over a GUID collision, and replacing lets anyone with
  a copied GUID boot another player. Degrading to today's behavior is the safe failure mode for a
  hint that was never a credential. Note this is also what every MPPM virtual player hits — see the
  local-testing caveat above — so during in-editor multi-peer testing "nothing restored" is the
  expected result, not a defect.
- **Token missing or malformed** (fresh install, cleared prefs, a client that sends nothing) —
  treated as no token: normal join, no restore. The feature is strictly additive.
- **Server process dies** — every client's `OnShutdown` fires, the retry loop runs and fails against
  a dead endpoint, and everyone lands on the main menu with the shutdown reason. The registry dies
  with the process; the match is over. This is the documented behavior, not a defect to be fixed by
  migration (see Non-goals).
- **A player drops during the Gameplay → MainMenu scene load** — the registry is cleared by
  `BeginReturnToLobby` at the start of that transition, so the drop is a plain lobby-phase leave.
- **Two rejoins racing for one held slot** — `TryClaim` removes the slot, so the first claim wins
  and the second is seated as a new player. No locking needed; both callbacks run on the server's
  main thread.

## Session identity (discussion, not v1 scope)

Reconnecting "to the same session" is currently free: `sessionName` is the compile-time constant
`"PvPvERoom"` and there is exactly one server, so a retry naturally targets the right place. No
discovery, browser, or session list is built here.

The one forward-compatible thing worth doing now, at zero cost: `ReconnectController` should retry
against **the session name it actually joined**, recorded when the connection succeeded, rather than
re-reading `GameNetworkManager.sessionName` at retry time. If a server browser or a second region
ever appears, the constant stops being the right answer and the retry loop would silently reconnect
a player to the wrong server — a bug that would be confusing to diagnose and is avoided by one
stored field today. Region and any future session parameters would ride the same stored record.

## Testing / verification

Unity + Photon Fusion — the authoritative check is manual play, per project convention.

**EditMode (pure C#, runnable outside Unity via the bundled-Roslyn workaround):**

- `ReconnectRegistry`: capture then claim returns the captured values; claim removes the slot so a
  second claim fails; `HeldCount` reflects captures and claims; `Clear` empties it.
- The admission rule as a pure function: known token always admitted; unknown token admitted iff
  `active + held < max`; verify the `active + held ≤ 20` invariant across a
  join/leave/rejoin sequence.
- Backoff schedule: attempt *n* yields the expected delay, and the loop terminates after 5.
- `PlayerIdentity`: a stored value is reused rather than re-minted; a missing value mints exactly
  once.

**Manual (Multiplayer Play Mode, ≥ 2 peers, with the editor identity salt in place):**

1. Client B drops mid-match while carrying a flag and coins → the flag drops at their last position
   and the coins scatter there; B's avatar despawns; B's scoreboard row disappears.
2. B reconnects → back on the same team, same nickname, same deposited value and buff tiers, same
   loadout order, scoreboard row restored with its counters intact, spawned at the team spawn at
   full health with no coins. No lobby team-pick appears, and no lobby panel is left drawn over
   gameplay.
3. Kill the client process entirely and relaunch → same result (proves the identity survives a
   process restart via `PlayerPrefs`, not just an in-memory field).
4. Pull the network cable / disable the adapter → the reconnecting overlay appears with the attempt
   counter; restore the connection mid-backoff → the loop succeeds and restores. Repeat and let it
   exhaust → main menu with a reason, then reconnect manually from the menu → state still restored.
5. Cancel during the loop → clean return to the idle menu, and a subsequent manual Join works
   (proves the runner rebuild is sound).
6. Drop in the lobby → seat is released immediately and rejoin is a plain fresh join.
7. Drop during PostMatch → released; the rejoiner rides the return-to-lobby load in.
8. Two peers presenting the same token (temporarily disable the editor salt) → the second is seated
   as a new player and the first is undisturbed.
9. Deliberate quit (`OnApplicationQuit`) → **no** reconnecting overlay, no retry loop.
10. Seat reservation, testable without 20 peers: temporarily set `maxPlayers = 2`, fill the
    session, drop one player, and confirm a third peer is refused while the slot is held and that
    the original can still get back in.

## Non-goals

- **Host migration.** Confirmed out. The dedicated server is the sole authority; if it dies the
  match is over and everyone returns to the menu through the existing `OnShutdown` path.
  `OnHostMigration` stays an empty stub.
- **Session discovery / server browser.** No session list, no region selection, no join-by-code.
  One fixed session name, with the forward-compatible retry-target note above.
- **Authentication.** The token identifies, it does not authenticate. No accounts, no server-side
  secret, no `OnCustomAuthenticationResponse` use.
- **Cross-match persistence.** Nothing survives the match boundary. The registry is cleared on
  return-to-lobby; progression is per-match by design.
- **A timed grace window.** Rest-of-match hold replaces it (Decision 1).
- **Frozen-avatar reconnection** (resuming in the same body at the same position/health).
- **Reconnecting into a *different* session or a restarted server process.**
- **Spectating while disconnected**, or any queue/lobby for held players.

## Resolved open questions

| Question | Resolution |
|---|---|
| Grace-window length? | None — the hold lasts the rest of the match, released by `BeginReturnToLobby`. |
| Does a held slot count against the 20-player cap? | Yes, via the stricter `OnConnectRequest` gate; `PlayerCount` stays 20. |
| Deposited value / buff tiers preserved? | Yes — the primary motivation for the feature. |
| Coins in hand preserved? | No — scattered at the drop point, exactly as on death. |
| Stats / score contribution preserved? | Yes; the stats row is copied into the rejoiner's new `PlayerId` slot, and team score was never rewound. |
| Health on rejoin? | Full. Rejoin is a respawn at the team spawn. |
| Avatar despawned or frozen? | Despawned. A drop is a death you don't respawn from yet. |
| Auto-retry attempts and backoff? | 5 attempts, 1/2/4/8/8 s of backoff plus a 15 s per-attempt deadline (~98 s worst case), Cancel available, then the main menu. |
| Duplicate token on a live connection? | Ignored — seated as a new player, nobody kicked. |
| Single fixed session? | Confirmed; the retry target is stored rather than re-read, for forward compatibility. |
| Host migration? | Confirmed out of scope; server death ends the match. |
