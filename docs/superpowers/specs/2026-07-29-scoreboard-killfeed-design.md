# Scoreboard / Roster / Killfeed — Design

**Date:** 2026-07-29
**Status:** Approved (design), no implementation plan authored
**Game:** Unity 6.3 Photon Fusion 2 2D PvPvE arena, Host/Client + dedicated server, ~20 players

## Problem

There is no in-match scoreboard, no per-player roster, and no per-player stat tracking of any
kind. Concretely, from the current code:

- **No per-player stats exist.** Nothing tracks kills, deaths, captures, coins deposited, flag
  carry time, or flag returns per player. The hooks are half-built:
  [`PlayerStatsHandler.ServerApplyDamage(damage, attackerId)`](../../../Assets/Scripts/Player/PlayerStatsHandler.cs:148)
  receives the attacker's `NetworkId` and discards it — `Die()`
  ([`PlayerStatsHandler.cs:179-205`](../../../Assets/Scripts/Player/PlayerStatsHandler.cs:179))
  has no idea who landed the finishing blow.
- **The only roster is in the lobby, pre-match.**
  [`LobbyServerState`](../../../Assets/Scripts/Net/LobbyServerState.cs) holds nicknames as
  plain server-side C# state; there is no analog to
  [`LobbyTeamChoices`](../../../Assets/Scripts/GameNetworkManager.cs:537) carrying a name into
  the Gameplay scene. A nickname a player types in the lobby currently never reaches their
  in-match player object at all.
- **The HUD that exists is all always-on, single-subject displays**: `Hud/TeamScoreDisplay.cs`
  (team totals + Vanguard + territory), `Hud/MatchPhaseHud.cs` (phase, timers, and a *minimal*
  PostMatch results panel — winner banner + final team scores only), `Hud/PlayerHud.cs` +
  `Hud/HealthSegmentDisplay.cs` + `Hud/CoinDisplay.cs` + `Hud/BuffIconDisplay.cs` (all
  local-player-only), and `Hud/HudToastFeed.cs` (a shared but purely local, client-side toast
  queue for buff unlock moments — explicitly **not** a killfeed; nothing broadcasts a kill event
  to other clients today).
- **The match-lifecycle spec explicitly deferred this.** Its results panel was scoped as
  "transition only," naming "the rich per-player scoreboard (K/D, captures, coins)... a separate
  item, out of scope" (see
  [2026-07-29-match-lifecycle-design.md](2026-07-29-match-lifecycle-design.md)). Since that spec
  was written, `SuddenDeath` has been merged into `MatchPhase` and `MatchManager`, and the
  PostMatch results panel described there is now live in
  [`MatchPhaseHud.cs`](../../../Assets/Scripts/Hud/MatchPhaseHud.cs). This spec is that deferred
  item.
- **Area-of-Interest culling is active** and applies per-`NetworkObject`, not per-component
  ([`AreaOfInterestRegistrar`](../../../Assets/Scripts/AreaOfInterest/AreaOfInterestRegistrar.cs)).
  A player's avatar `NetworkObject` (health, buffs, inventory, team color) is deliberately **not**
  always-interested — that is the entire point of AoI at 20 players. Any data the scoreboard needs
  to show for *every* player, regardless of distance, must live somewhere that is independently
  always-interested, or it will silently vanish for distant players exactly like the
  documented AoI footgun (flags/managers/carrier must be always-interested or the HUD blanks out).

## Decisions (from brainstorming)

| # | Decision |
|---|---|
| 1 | **Overall score is the headline/sort stat**, computed from five inputs (below), not any single raw stat. |
| 2 | **Tracked stats: kills, deaths, flag captures, coins deposited, flag carry seconds, flag returns.** No assists — the extra damage-contribution ledger it requires isn't worth it for a nice-to-have. |
| 3 | **Captures are tracked and displayed but excluded from the overall-score formula.** Carry time and returns already carry the "objective play" signal at finer grain than a 0-1-per-match binary. |
| 4 | **Storage: one central always-interested `MatchStatsManager` singleton**, not a per-player `NetworkBehaviour`. AoI applies per-`NetworkObject`; a per-player stats component living on the (AoI-culled) avatar object would force that whole avatar object always-interested for everyone, reintroducing the O(n²) traffic problem AoI exists to prevent. |
| 5 | **Team and alive/dead state are mirrored into the same table**, alongside the stats, so the entire board is readable regardless of AoI — those two fields live on the avatar's culled `NetworkObject` today and are not otherwise visible for a distant player. |
| 6 | **Display name reaches the match via a new `LobbyNicknameChoices` handoff**, mirroring the existing `LobbyTeamChoices` pattern, written into the central table at spawn. Nicknames currently die in the lobby; this closes that gap. |
| 7 | **A leaving player's row is simply dropped**, not preserved as a "disconnected" ghost row. Matches existing despawn behavior and needs zero extra state. |
| 8 | **Input: hold Tab.** Board shows while held, hides on release — a glance, not a menu, with no explicit close step to fumble mid-fight. |
| 9 | **No dedicated live killfeed surface.** Kill/death/capture events are visible only as accumulating stats on the scoreboard, not as a real-time "X eliminated Y" feed. This is a scope cut from the task's working title — see "Resolved open questions." |
| 10 | **The scoreboard doubles as the results screen.** During `MatchPhase.PostMatch` the same panel used for hold-to-view Tab auto-shows (no hold required), layered with the existing winner banner in `MatchPhaseHud`. One component, two triggers. |
| 11 | **Overall score is derived, never stored** — computed client-side, on demand, from the networked stat inputs and authored weights. Matches the codebase's established pattern for Vanguard/buff tiers: nothing to reset, nothing to desync, nothing extra on the wire. |

## Architecture

### `MatchStatsManager` — one central, always-interested table

A new scene `NetworkBehaviour` singleton, placed in the Gameplay scene next to `MatchManager` and
`TeamScoreManager` and following the same pattern: `Instance` static accessor, guarded
double-spawn (disable the duplicate, never `Destroy()` a spawned `NetworkObject` locally), and
tagged with [`AlwaysInterestedMarker`](../../../Assets/Scripts/AreaOfInterest/AlwaysInterestedMarker.cs)
so [`AreaOfInterestRegistrar.ServerInitialize`](../../../Assets/Scripts/AreaOfInterest/AreaOfInterestRegistrar.cs:37)
discovers it at scene start and forces interest for every current and future player
(`AreaOfInterestRegistrar.OnPlayerJoined` already handles late joiners for any object in the
always-interested set).

Its networked state is one fixed-capacity array, indexed by `PlayerId`:

```csharp
[Networked, Capacity(20)]
public NetworkArray<PlayerStatEntry> Entries => default;
```

`20` matches [`GameNetworkManager.maxPlayers`](../../../Assets/Scripts/GameNetworkManager.cs:29),
the project's existing session-size config; `PlayerId` is already used as the roster key
elsewhere (`LobbyServerState`'s `SortedDictionary<int, Entry>`), so indexing by it directly here
is consistent, not a new convention.

`PlayerStatEntry` (an `INetworkStruct`) holds:

| Field | Type | Meaning |
|---|---|---|
| `Active` | `NetworkBool` | Set once at spawn; distinguishes a real entry from an unused slot. Never cleared — see "Row lifecycle" below. |
| `Team` | `byte` | Mirrors `PlayerTeamData.Team`. |
| `DisplayName` | `NetworkString<_64>` | Mirrors the lobby nickname (`LobbyProtocol.MaxNicknameBytes` is 64, so this sizes exactly to the existing nickname wire format). |
| `IsDead` | `NetworkBool` | Mirrors `PlayerStatsHandler.IsDead`. |
| `Kills` | `int` | Credited to the attacker on a resolved kill. |
| `Deaths` | `int` | Credited to self on every death. |
| `Captures` | `int` | Credited to the carrier on a completed capture. |
| `CoinsDeposited` | `int` | Cumulative deposited value. |
| `FlagCarrySeconds` | `int` | Cumulative whole seconds spent carrying either flag. |
| `FlagReturns` | `int` | Count of player-attributed flag returns (own team's dropped flag, touched by a teammate). |

Why a central table and not a per-player `NetworkBehaviour` on the avatar (the initially obvious
choice, matching every other per-player system in this codebase —
[`PlayerTeamData`](../../../Assets/Scripts/Player/PlayerTeamData.cs),
[`PlayerStatsHandler`](../../../Assets/Scripts/Player/PlayerStatsHandler.cs),
`PlayerBuffs`, `NetworkedPlayerInventory`): all of those are intentionally *not*
always-interested, because AoI's entire purpose is to stop a distant player's transform/health/
buffs from replicating to everyone at 20-player scale. Marking one more component on that same
`NetworkObject` always-interested marks the **whole object** always-interested — there is no
per-component interest control in Fusion. A central table sidesteps this: it is one object, whose
always-interested cost is fixed regardless of player count, exactly like `TeamScoreManager` and
`MatchManager` already are.

### Row lifecycle: no leave-flag, `ActivePlayers` is the membership check

`MatchStatsManager` never clears a slot when a player disconnects. Per Decision 7, the scoreboard
UI reads `Runner.ActivePlayers` at render time and skips any `Active` slot whose `PlayerId` is no
longer in that set — the same membership source
[`NetworkedSpawnManager.OnPlayerLeft`](../../../Assets/Scripts/NetworkedSpawnManager.cs:114) and
[`TeamScoreManager.CaptureRosterSizes`](../../../Assets/Scripts/Coin%20Scripts/TeamScoreManager.cs:157)
already use for the same purpose. No `Connected` bool, no leave-time write, no extra networked
state.

## Update hooks

Every write below happens at an already-server-authoritative call site; each is one or two
additional lines, not a new code path.

### Identity: team, name, dead state

- **`RegisterPlayer(playerId, team, name)`** — called once from
  [`NetworkedSpawnManager.TrySpawnPlayer`](../../../Assets/Scripts/NetworkedSpawnManager.cs:151),
  right after `SpawnPlayer` resolves the new avatar. Reads the new `LobbyNicknameChoices.TryGet`
  (below) for the name.
- **Team** — mirrored from [`PlayerTeamData.SetTeam`](../../../Assets/Scripts/Player/PlayerTeamData.cs:28),
  the single existing authoritative team-assignment call.
- **`IsDead`** — mirrored from [`PlayerStatsHandler.Die()`](../../../Assets/Scripts/Player/PlayerStatsHandler.cs:179)
  (set true) and `Respawn()` (set false) — both already server-only, already the sole writers of
  the underlying `IsDead` field.

### Kills / deaths — closing the attribution gap

`ServerApplyDamage(float damage, NetworkId attackerId)`
([`PlayerStatsHandler.cs:148`](../../../Assets/Scripts/Player/PlayerStatsHandler.cs:148)) already
receives the attacker's `NetworkId` on every hit but never stores it. Add a private, **non-networked**
`lastAttackerId` field (server-only; resolved same-tick, so it does not need to replicate), written
at the top of `ServerApplyDamage`. `Die()` reads it once:

- Resolve `lastAttackerId` to a `NetworkObject`, then to a `PlayerRef` via its `InputAuthority`.
- If that `PlayerRef` has an `Active` entry in `MatchStatsManager`, credit `Kills` to them.
- If it doesn't resolve to a live roster entry — no last attacker (environmental/unknown damage,
  the `default` path used by `RPC_TakeDamage`), or the attacker was an AI enemy's `NetworkId`
  rather than a player's — **no kill is credited to anyone.** This is also what keeps AI kills out
  of the human K/D columns, matching the requirement that AI enemies are excluded from the roster.
- `Deaths` is always credited to the dying player, regardless of attacker resolution.

### Captures

[`CTFGameManager.OnCarrierEnteredBase(PlayerRef carrier, Team baseTeam)`](../../../Assets/Scripts/CTF%20Flag/CTFGameManager.cs:97)
already resolves the capturing `PlayerRef` directly before calling `MatchManager.ReportCapture`.
One additional call, same call site: `MatchStatsManager.Instance.RecordCapture(carrier)`.

### Coins deposited

[`HomeBase.ServerDeposit(NetworkObject playerNetObj, NetworkedPlayerInventory inventory)`](../../../Assets/Scripts/Coin%20Scripts/HomeBase.cs:196)
already dual-writes the same deposited value to `TeamScoreManager.RPC_AddPoints` and
`PlayerBuffs.ServerAddDepositedValue` (per the coins/buffs economy spec). A third parallel write
at the same call site: `MatchStatsManager.Instance.RecordDeposit(playerNetObj.InputAuthority, points)`.

### Flag carry time — new tracking, doesn't exist today

`Flag` has no time-carried tracking of any kind currently. Add a private, non-networked
`float carrySecondsAccumulator` on `Flag`. In
[`Flag.FixedUpdateNetwork`](../../../Assets/Scripts/CTF%20Flag/Flag.cs:148), while
`CurrentState == FlagState.Carried`, accumulate `Runner.DeltaTime`; once the accumulator reaches
≥1.0, flush the whole-second count to `MatchStatsManager.Instance.RecordFlagCarrySeconds(CarrierPlayerRef, seconds)`
and subtract the flushed amount, keeping the sub-second remainder. This bounds the networked write
rate to at most once per second **per carried flag** — there are only ever two flags in this mode,
so at most 2 writes/sec system-wide, nowhere near a 20-player-scale concern. The final fractional
second of a carry episode is simply dropped when the carry ends; sub-second precision doesn't
matter for a scoreboard column.

### Flag returns — attribution added to an existing method

[`Flag.ReturnFlag()`](../../../Assets/Scripts/CTF%20Flag/Flag.cs:307) is called from two places
today: the own-team-touches-a-dropped-flag trigger in
[`OnTriggerEnter2D`](../../../Assets/Scripts/CTF%20Flag/Flag.cs:170) (a real player action) and the
auto-return timer expiry in `FixedUpdateNetwork` (nobody did anything — the flag just times out).
Add an optional parameter: `ReturnFlag(PlayerRef returner = default)`. The trigger path passes
`playerNetworkObject.InputAuthority`; the timer-expiry path and `ReturnFlagRpc` pass nothing
(`default`). `MatchStatsManager.Instance.RecordFlagReturn(returner)` only credits when `returner`
resolves to an `Active` roster entry — an auto-return scores nothing to anyone.

### Name handoff: `LobbyNicknameChoices`

A new static class in `GameNetworkManager.cs`, structurally identical to
[`LobbyTeamChoices`](../../../Assets/Scripts/GameNetworkManager.cs:537) (a
`Dictionary<PlayerRef, string>` with `Set`/`TryGet`/`Remove`/`Clear`), populated at the same two
points `LobbyTeamChoices` already is:

- [`ServerHandleJoin`](../../../Assets/Scripts/GameNetworkManager.cs:291) — seeds a placeholder
  (`LobbyProtocol.PlaceholderName(id)`) immediately on join, so a player who never sets a nickname,
  or a mid-match late joiner, still has *something* by the time `NetworkedSpawnManager` reads it.
- The `NameKey` branch of
  [`OnReliableDataReceived`](../../../Assets/Scripts/GameNetworkManager.cs:437) — updates the
  choice whenever `serverLobby.SetNickname` succeeds, exactly parallel to how `TeamChoiceKey`
  updates `LobbyTeamChoices` on a team switch.
- `OnPlayerLeft` and `OnShutdown` remove/clear it, mirroring `LobbyTeamChoices`'s existing cleanup.

## Overall score — derived, not stored

The headline stat is a computed value, not a networked field. A pure, engine-free function
(alongside `MatchResolver`/`MatchRules` in style — unit-testable outside Unity per this project's
bundled-Roslyn workaround) takes the five raw stat inputs and a small `ScoreWeights` value:

```
OverallScore = kills · W_kill − deaths · W_death + coinsDeposited · W_coin
             + flagCarrySeconds · W_carry + flagReturns · W_return
```

Computing it client-side from already-replicated inputs means nothing new goes on the wire, there
is no separate value to keep in sync with its own inputs, and nothing to reset at match end —
the same "derive on query" shape already used for buff tiers and Vanguard's tier resolution.

Weights are authored `[SerializeField]` fields on `MatchStatsManager` (a config surface, like
`TeamScoreManager.vanguardThresholds` or `BuffLoadoutConfig`'s thresholds), tunable in playtest,
not derived from anything:

| Stat | Weight | Typical contribution (8-10 min match) |
|---|---|---|
| Flag carry seconds | 1 / sec | 60–180, for an objective player running multiple carries |
| Flag return | 20 | 40–80, for a defender with 2–4 returns |
| Kill | 10 | 100, for 10 kills |
| Death | −10 | −100, for 10 deaths |
| Coin deposited | 0.75 | 30–52.5 typical banker (40–70 coins, per the economy spec's target outcomes); up to ~195 for a runaway farmer (260 coins) |

Kills and deaths are weighted at parity — a death costs exactly what a kill earns — which pulls
combat up to a swing factor comparable to or exceeding flag carry time. This is a deliberate,
explicit choice (not the original "objective-first, combat as a minor modifier" framing floated
early in brainstorming); see "Resolved open questions."

Captures are **not** an input to this formula (Decision 3) — they remain a separately displayed
column.

## Late-join / reset contract

**Late join:** `MatchStatsManager` is always-interested, so a joiner receives the full `Entries`
array immediately on connect, same as `TeamScoreManager`'s scores and `MatchManager`'s phase. Their
own slot is populated by `RegisterPlayer` the moment `NetworkedSpawnManager` spawns them, following
the same "join mid-Live works, no special case" pattern the match-lifecycle and economy specs
already established for other systems.

**Reset at match end:** handled entirely by the existing scene-reload contract (see
[2026-07-29-match-lifecycle-design.md](2026-07-29-match-lifecycle-design.md), "State-reset
contract"). `MatchStatsManager` is a scene `NetworkBehaviour`, not `DontDestroyOnLoad` — it
despawns with the old Gameplay scene and respawns fresh (all entries zeroed / `Active = false`) on
the next `runner.LoadScene(Gameplay)`. Nothing to hand-write; this spec adds one more row to the
existing reset table, it doesn't need its own reset routine.

## Scoreboard UI

**Input:** hold Tab (Decision 8), read locally via a new action in the existing `UI` action map in
[`InputSystem_Actions.inputactions`](../../../Assets/InputSystem_Actions.inputactions) — a purely
local, render-path read (per project convention, UI reads are local/non-simulation), toggling the
panel's `CanvasGroup`/active state on `performed`/`canceled`. No RPC, no networked state; every
client independently decides whether to show its own copy of an already-replicated table.

**Layout:** players grouped by team (matching `TeamScoreDisplay`'s `team1Label`/`team2Label`
convention), each team's rows sorted by Overall Score descending. `Team3AI`/`None` entries are
filtered out — AI enemies never get a `MatchStatsManager` entry in the first place (only
`NetworkedSpawnManager.TrySpawnPlayer`, which only ever spawns human players, calls
`RegisterPlayer`), so no explicit AI-exclusion filter is even needed.

**Columns**, in display order: **Name · Overall Score (sort key) · K/D · Captures · Coins ·
Flag Carry Time · Flag Returns**, plus two per-row indicators: dead/alive (from the mirrored
`IsDead`) and currently-carrying-a-flag (via
[`CTFGameManager.IsCarrying(PlayerRef)`](../../../Assets/Scripts/CTF%20Flag/CTFGameManager.cs:168)
— already safe to read for any player, since the flags themselves are `AlwaysInterestedMarker`
objects; no duplication into `MatchStatsManager` needed for this one, unlike Team/IsDead).

**Repaint strategy:** while the panel is held open, values are read directly from the already-
replicated `Entries` array each frame the panel is visible — the same "per-frame numeric read
of networked state on the render path, gated on visibility" pattern `MatchPhaseHud.LateUpdate`
already uses for the countdown/timer text. This is not per-tick simulation polling; it costs
nothing when the panel is closed (the default state), and at 20 rows × 8 columns it is trivial
even every visible frame.

## Three-surface split

Two surfaces exist after this spec, not three — the task's working title's "killfeed" is resolved
as a scope cut (Decision 9, and see "Resolved open questions"):

1. **Always-on** — `TeamScoreDisplay` (team scores, Vanguard, territory zone) and `MatchPhaseHud`'s
   phase/timer readouts. Unchanged by this spec. Shows the current match state at a glance,
   permanently.
2. **On-demand full scoreboard** (new) — the hold-Tab panel described above. Shows full per-player
   detail, shown only when asked for, because 20 rows × 8 columns is too much to keep on screen
   permanently.

## Results screen

Per Decision 10, `MatchPhaseHud`'s existing PostMatch handling
([`MatchPhaseHud.cs:75-114`](../../../Assets/Scripts/Hud/MatchPhaseHud.cs:75)) gains one behavior
change: the same scoreboard panel used for hold-Tab is shown **automatically** (no hold required)
for the whole `PostMatch` phase, layered underneath/alongside the existing winner banner, final
team scores, and return-to-lobby countdown/button — all of which are unchanged. One component
serves both triggers (`Tab held` OR `Phase == PostMatch`); no second results-specific stats panel
is built. This directly fills the gap the match-lifecycle spec named and deferred.

## 20-player correctness

- **No per-frame polling of simulation state.** Every write to `MatchStatsManager` happens at an
  existing event (a death, a capture, a deposit, a flag pickup/return) or a bounded once-per-second
  flush (flag carry time, capped at 2 concurrent carriers). The scoreboard UI's per-frame reads are
  render-path reads of already-networked data, gated on panel visibility — the same shape as every
  other HUD component in this codebase.
- **Fixed networked footprint.** One `NetworkArray<PlayerStatEntry>` of capacity 20, each entry a
  small struct (bool + byte + `NetworkString<_64>` + bool + 6 ints ≈ 100 bytes) — one object, not
  20, and its always-interested cost does not scale with player count the way a per-player
  approach's would have.
- **Join/leave mid-match:** join is `RegisterPlayer` at spawn (works in any phase); leave is a
  silent row-drop driven by `Runner.ActivePlayers`, requiring no despawn-time write to
  `MatchStatsManager` at all.
- **Dead players:** shown via mirrored `IsDead`, correct regardless of the viewer's distance to
  that player.
- **AI enemies:** never registered, so never appear — no filter logic needed, just an absence.

## Non-goals

- **A dedicated live killfeed** ("X eliminated Y" transient event lines). Explicitly cut — see
  Decision 9 and "Resolved open questions." Kills are visible only as accumulating stats on the
  scoreboard.
- **Assists.** No damage-contribution ledger; would add real design and implementation weight for
  a stat that was ruled a nice-to-have, not core.
- **A "disconnected" ghost row** for players who leave mid-match. Their row is dropped, not frozen.
- **Preserving distant players' avatar `NetworkObject` interest.** This spec deliberately keeps
  avatars AoI-culled; only the small mirrored subset (team, name, dead state) plus the stat table
  are always-interested.
- **Per-player `NetworkBehaviour` stat storage.** Considered and rejected — see "Architecture" for
  why it would have defeated AoI.
- **Captures in the overall-score formula.** Tracked and displayed, deliberately excluded from the
  score itself (Decision 3).
- **Any UI for re-weighting or customizing the score formula in-game.** Weights are an
  authored/tunable config value, not a player-facing setting.
- **Changes to `HudToastFeed`, buff-unlock toasts, or any other existing HUD surface** beyond the
  one behavior change to `MatchPhaseHud`'s PostMatch panel described above.
- **In-place networked reset of `MatchStatsManager`.** Scene reload does it, per the existing
  match-lifecycle contract.

## Resolved open questions

| Question | Resolution |
|---|---|
| Headline/sort stat: combat, captures, coins, or a flat grid? | **A computed Overall Score**, combining kills, deaths, coins deposited, flag carry seconds, and flag returns — not any single raw stat. |
| Which stats does the networked block track? | Kills, deaths, captures, coins deposited, flag carry seconds, flag returns. No assists. |
| Hold-to-view vs toggle? | **Hold Tab.** |
| Does the always-on team score stay alongside the new board? | **Yes, unchanged** — `TeamScoreDisplay`/`MatchPhaseHud` remain the permanent always-on surface; the new board is on-demand. |
| Where does the in-match display name come from? | **A new `LobbyNicknameChoices` handoff**, mirroring `LobbyTeamChoices`, written into `MatchStatsManager` at spawn. |
| Should the scoreboard double as the results screen? | **Yes** — auto-shown (no hold needed) during `PostMatch`, layered with the existing minimal winner/score panel. |
| Per-player storage: component on the avatar, or a central table? | **Central table**, reversing the initial framing — AoI applies per-`NetworkObject`, and a per-player component on the (culled) avatar would have forced that whole object always-interested for every player, defeating AoI's purpose at 20-player scale. |
| Does Team/IsDead need mirroring into the table too? | **Yes** — both live on the AoI-culled avatar today and would otherwise be invisible for distant players, violating the "board must not depend on AoI" requirement. |
| What happens to a leaving player's row? | **Dropped**, not preserved — matches existing despawn behavior, no extra state. |
| Is a live killfeed in scope? | **No.** The task's working title named one, but it's cut in favor of accumulating stats on the scoreboard only — a live per-kill broadcast feed (which would need a new all-clients RPC broadcast path, unlike the purely local `HudToastFeed`) was judged not worth the added surface for this pass. |
| Are captures part of the overall-score formula? | **No** — tracked and displayed, but the formula only combines kills/deaths/coins/carry-time/returns, per explicit instruction. Carry time and returns already carry the objective-play signal at finer grain. |
| Overall score weighting philosophy? | Started as **objective-first** (flag carry time and returns weighted heaviest, kills/deaths as modifiers), then explicitly revised to weight kills and deaths at parity (**10 / −10**) and coins at **0.75** — pulling combat up to a swing factor comparable to or exceeding objective play. This was a deliberate late change, not an oversight; flagged explicitly in this doc since it departs from the framing used earlier in brainstorming. |
| Is the overall score networked state? | **No — derived client-side** from replicated inputs + authored weights, matching the buff-tier/Vanguard "derive on query" pattern. |

## Verification notes

Per project convention the authoritative check is manual play, but the arithmetic here is
deliberately engine-free and unit-testable outside Unity (bundled-Roslyn workaround):

- `OverallScore` formula against the weight table above, including negative contributions from
  deaths and the zero-captures-input property.
- Kill-credit resolution logic as a pure function of "does the attacker reference resolve to an
  `Active` roster entry" — table-test the four cases: valid player attacker, `default`/no attacker,
  an attacker `NetworkId` with no roster entry (AI), and a dead/despawned attacker.
- Flag-carry-seconds flush: accumulator crossing whole-second boundaries produces exactly one
  `RecordFlagCarrySeconds` call per second, with the correct sub-second remainder carried forward.
- Flag-return attribution: the trigger path credits the touching player; the auto-return timer path
  credits nobody.
- Row-membership filter: an `Active` entry whose `PlayerId` is not in `Runner.ActivePlayers` is
  excluded from the rendered board.

Manual play must confirm: the board reads correctly for a player who is far (AoI-distant) from
every other player on the map (the whole point of the central-table design); a mid-match late
joiner sees the full existing roster and stats immediately; a disconnecting player's row disappears
from every other client's board on their next repaint; the PostMatch auto-show doesn't visually
collide with the existing winner banner/return countdown; and holding Tab during combat doesn't
meaningfully affect frame time at a full 20-player lobby.
