# Playtest Readiness — Deferred Follow-Ups

Created from the code review on the `feat/friendly-fire-collision-self-marker` branch.

**Context that set these priorities:** the next milestone is a *closed playtest with ~20 known
people*. Anti-cheat work was therefore deliberately deprioritized, and the list below is ordered
by what makes a playtest produce usable data: the session has to survive, and combat has to feel
honest enough that feedback means something.

> ⚠️ **This ordering is only valid while the test is closed and invite-only.** The moment a build
> goes public or semi-public, the security items in the last section jump back to the top.

## Status

| # | Item | Priority | Status |
| --- | --- | --- | --- |
| 1 | Reconnect / session robustness under real drops | 🔴 High | ⬜ Open |
| 2 | Lag compensation on melee hit detection | 🔴 High | ⬜ Open |
| 3 | Flag drop/return RPC race | — | ✅ Done |
| 4 | `.gitignore` hygiene | — | ✅ No action needed (already correct) |
| 5 | Remove dead unvalidated damage RPC | — | ✅ Done |
| 6 | Unvalidated `RpcSources.All` RPCs | 🟡 Deferred | ⬜ Gated on public build |

---

## 1. Reconnect / session robustness

**Why this is first.** Twenty people on home internet for an hour means several *will* drop. If
rejoin is broken the session ends early and twenty calendars are wasted. This is the single
highest-leverage thing to verify before test day.

**Where the risk actually is.** The *pure logic* is already unit-tested and is probably fine:

- `Assets/Scripts/Net/ReconnectRegistry.cs` — covered by `ReconnectRegistryTests`
- `Assets/Scripts/Net/ReconnectPolicy.cs` — covered by `ReconnectPolicyTests`
- `Assets/Scripts/Net/ReconnectBackoff.cs` — covered by `ReconnectBackoffTests`

The **untested** part is the integration glue inside `GameNetworkManager.cs` (~1000 lines), which
has no test coverage because it is welded to `NetworkRunner`. Specific areas of concern, per the
comments already in that file:

- `pendingRestores` (the `Dictionary<PlayerRef, ReconnectHeldSlot>`) is parked between
  `OnPlayerJoined` and the spawn that consumes it — and for a mid-match rejoin those two points
  can be *a whole scene load apart*.
- `tokensByPlayer` captures the identity token at join specifically because
  `GetPlayerConnectionToken` will not resolve during an ungraceful drop.
- `BuildRunner()` must fully rebuild the stack per reconnect attempt, since a shut-down
  `NetworkRunner` cannot be restarted.

**Suggested manual test matrix (do this before test day):**

| Scenario | Expected |
| --- | --- |
| Client drops mid-match, rejoins | Same team, score/stats preserved, flag state sane |
| Client drops *during* a scene load | Rejoin still resolves the held slot |
| Two clients drop simultaneously | Both slots held independently, no cross-assignment |
| Client drops while **carrying a flag** | Flag drops correctly; rejoiner is not still marked carrier |
| Host drops | Documented, understood behaviour (even if "session ends") |
| Client force-quits (ungraceful) | Token path still works — this is the case the design targets |

**Stretch goal:** extract the lobby/roster half of `GameNetworkManager` into a testable plain
class in `Game.Net` (following how `ReconnectRegistry` was already extracted), leaving the
`MonoBehaviour` as thin Fusion glue. Not required for the playtest.

## 2. Lag compensation on melee hit detection

**Why this matters for a playtest specifically.** This is the thing you are literally trying to
evaluate. Without it, higher-ping testers will report *"combat feels unresponsive"*, and the
temptation will be to fix that by tuning swing timings in `PlayerStats`
(`attackStartupTicks` / `attackActiveTicks` / `attackRecoveryTicks`) — which would be tuning the
wrong variable and would bake a latency workaround into the game's feel.

**Where.** `PlayerCombat.ApplyMeleeHits()` uses a raw `Physics2D.OverlapBoxAll(...)` inside the
authoritative tick. Fusion ships `Runner.LagCompensation` precisely so that a client's hit is
evaluated against where the target *was on the attacker's screen*, rather than where the target
is on the server right now.

**Notes for whoever picks this up:**

- The swing itself is already modelled correctly — `SwingPhase.Resolve` derives the phase from
  the start tick, so it predicts and resimulates properly. Only the *query* needs changing.
- Lag compensation requires targets to have Fusion `Hitbox` components; today the query relies on
  `attackableLayer` + `Collider2D`. That is the bulk of the work.
- `Projectile.cs` performs its own overlap check and has the same issue; fix both together so
  melee and projectiles agree.
- Keep the existing per-attacker rapid-hit ledger (`hitLedger` in `PlayerStatsHandler`) — it is
  correct and orthogonal to this change.

**Cheaper interim option** if the `Hitbox` migration is too large before the test: slightly
enlarge the melee box and note it as a known limitation, so that feel feedback is at least not
dominated by ping.

---

## 6. Deferred: unvalidated `RpcSources.All` RPCs

These are **pure cheat vectors** — every one is guarded such that only the server mutates state,
so there is no accidental/non-malicious trigger path and nothing here can misfire during a
trusted playtest. Deliberately skipped for now.

| Location | Issue |
| --- | --- |
| `TeamScoreManager.cs:197` `RPC_AddPoints(string team, int points)` | Any client can request an arbitrary score delta. Should only ever be driven by `HomeBase`'s server-side deposit. Also: `string` params in RPCs allocate and are size-capped — send `Team` as a `byte`. |
| `Enemy.cs:228` `RPC_TakeDamage(int, Vector2, Vector2)` | Client-chosen damage *and* knockback on any enemy at any range. |
| `HomeBase.cs:164` `RPC_RequestDeposit(NetworkObject)` | Trusts a client-supplied object reference; should verify `info.Source == playerNetObj.InputAuthority`. |

**The general rule to apply when these are addressed:** `RpcSources.All` means *"a request from an
untrusted stranger."* A `HasStateAuthority` check inside the body only proves the code is running
on the server — it does **not** prove the caller was entitled to ask. Each of these should take
`RpcInfo info` as its final parameter and validate `info.Source`.

**Gate:** address all three before any public, open, or redistributable build. Consider adding
inline `// SECURITY:` comments at each site so this does not get lost if this doc goes stale.

---

## Record of what was changed

For traceability, the review items that *were* actioned:

- **Item 5 —** Deleted `PlayerStatsHandler.TakeDamage(float)` and its
  `[Rpc(RpcSources.All, ...)] RPC_TakeDamage(float)`. Verified dead: all three live damage paths
  (`PlayerCombat`, `Projectile`, `Enemy`) already call `ServerApplyDamage(damage, attackerId)`
  with a real attacker id, and no scene/prefab had it wired via `UnityEvent`.
- **Item 3 —** Deleted `Flag.DropFlagRpc()` and `Flag.ReturnFlagRpc()`. `ReturnFlagRpc` had no
  callers at all. `DropFlagRpc`'s only caller (`PlayerStatsHandler.TryDropFlag`) is already
  server-only and already performs the carrier check via `IsCarriedBy`, so it now calls
  `DropFlag()` directly — matching how `NetworkedSpawnManager` already did it. This removed a
  pointless server→self RPC round-trip from the death path.
- **Item 4 —** No action. `.gitignore` was already correct: `Library/`, `Temp/`, `Obj/`, `Logs/`,
  `UserSettings/`, `.superpowers/` and `.claude/worktrees/` are all ignored, and `git ls-files`
  confirms zero tracked files in any of them. The worktree copies exist on disk only, so they add
  local search noise but do **not** bloat the repository.
