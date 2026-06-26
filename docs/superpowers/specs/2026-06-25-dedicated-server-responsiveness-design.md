# Dedicated Server & 20-Player Responsiveness — Design

**Date:** 2026-06-25
**Status:** Approved (pending written-spec review)
**Scope:** Make the game feel responsive at the full 20-player cap by moving off
host-authoritative play to a **dedicated server**, cutting bandwidth with **Area of
Interest + projectile pooling**, and tightening combat feel with **client-side
cosmetic prediction** (lag compensation was dropped — see decision). Camera feel is explicitly
out of scope (handled separately).

## Problem

At 20 players the game stops feeling responsive. Three independent causes:

1. **Topology.** `GameMode.Host` ([GameNetworkManager.cs:95](../../../Assets/Scripts/GameNetworkManager.cs))
   makes one player the server: they feel zero-latency while the other 19 pay full RTT to
   that person's home connection, and that home uplink is a hard scaling ceiling — its
   saturation causes packet loss → jitter → "lag."
2. **Bandwidth.** Area of Interest is OFF, so every client receives every object. Player
   sync scales ~O(n²); at 20p this is the dominant bandwidth cost and the proximate cause
   of saturation-driven jitter.
3. **Combat feel.** Clients' authoritative combat outcomes (projectile spawn, melee
   damage, stun) cost a full round-trip. Only movement is predicted today.

## Decisions

The game is **friends invite-only with no anti-cheat requirement**, and **player-vs-player
physical collision is a strategic/core mechanic** (body-blocking, guarding the flag).

| Decision | Choice | Why |
| --- | --- | --- |
| Authority model | **Dedicated server, server-authoritative** | Single-truth physics is required for PvP collision; Shared Mode cannot arbitrate colliding bodies consistently. |
| Match start (no host-player) | **Designated host-client** | Room creator gets the Start button and messages the server to load gameplay; preserves current "wait for all, then a human starts" UX. |
| Scale | **Area of Interest + projectile object pooling** | AoI is the ~3× bandwidth win; pooling removes GC hitches that read as lag. |
| Combat feel | **Cosmetic client prediction only** (lag compensation dropped 2026-06-25 per user) | Prediction makes own actions feel instant. Lag comp was de-scoped: friends-only (no fairness/anti-cheat pressure), projectiles travel (mild corner problem), and the Hitbox conversion was the largest, riskiest change for little felt gain. |
| Camera | **Out of scope** | Being addressed separately. |

### Approaches considered (authority model)

- **A. Dedicated server, keep server authority (CHOSEN).** One machine resolves all
  physics against a single world snapshot — exactly what colliding player bodies need.
  Fixes latency fairness and removes the home-uplink bottleneck. Existing
  `HasStateAuthority`-gated gameplay code is preserved almost wholesale. Cost: headless
  build + hosting, and the lobby must be reworked because the server is not a player.
- **B. Shared Mode (client authority).** Best per-player feel (own movement/combat become
  locally authoritative) and combat hit-registration comes "for free," with no server to
  run. **Rejected:** with each client owning its own rigidbody, a collision between two
  player bodies is resolved against each peer's *interpolated* (stale) view of the other,
  so the two machines disagree on the separation → contact jitter and, for body-blocking,
  gameplay-affecting disagreement about whether a carrier got through. There is no clean
  in-model fix. Shared Mode is structurally worst at the one mechanic that is core here.
- **C. Stay host mode, optimize only.** Least work, but keeps the host's unfair zero-latency
  advantage and the home-uplink bottleneck. Rejected: doesn't address the root.

## Environment (confirmed)

- Photon **Fusion 2**, currently `GameMode.Host` (`PeerMode 0`), tick rate **64**,
  `PlayerCount 20` ([NetworkProjectConfig.fusion](../../../Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion)).
- Positions sync via **`NetworkRigidbody2D`** (no `NetworkTransform`); client-side physics
  prediction is ON (`RunnerSimulatePhysics2D.SimulateForward`, [GameNetworkManager.cs:62](../../../Assets/Scripts/GameNetworkManager.cs)).
- **Lag compensation OFF** (`LagCompensation.Enabled: false`); hit detection uses Unity
  `OnTriggerEnter2D` / `Physics2D.OverlapBoxAll` ([Projectile.cs:58](../../../Assets/Scripts/Player/Projectile.cs), [PlayerCombat.cs:176](../../../Assets/Scripts/Player/PlayerCombat.cs)).
- **Area of Interest OFF.** Flags + global managers must be marked always-interested or the
  flag HUD/score break (known footgun).
- Lobby team/loadout choices already arrive at the server via reliable-data
  ([GameNetworkManager.cs:388](../../../Assets/Scripts/GameNetworkManager.cs)); the host's
  direct-record branch is the only host-as-player assumption in the lobby path.

## Architecture — three independently shippable phases

Each phase is a separate implementation plan. Phase 1 gates the others; Phases 2 and 3 are
independent of each other once Phase 1 lands.

### Phase 1 — Dedicated server + lobby rework (the gate)

**Goal:** the match runs on a headless, non-player server; a designated host-client starts
it; gameplay authority code is unchanged.

- Server process starts with `GameMode.Server` and runs headless
  (`-batchmode -nographics`). Clients use `GameMode.Client`.
- **Lobby:** remove the host-as-player assumption. Every team/loadout choice flows through
  `OnReliableDataReceived` (already implemented); the `runner.IsServer` direct-record branch
  becomes dead for human players and can be left for the server's own bookkeeping or removed.
- **Start trigger:** the room creator is the **designated host-client**. They see the Start
  button; clicking it sends a reliable start message (or RPC) to the server, which validates
  every active client has chosen (`CanStartMatch` semantics) and then `LoadScene`s gameplay.
  The server never auto-starts.
- **Spawning unchanged:** `NetworkedSpawnManager` already gates on `HasStateAuthority`
  (= the dedicated server) and calls `SetPlayerObject`; no authority change needed.
- **Server build hygiene:** disable/strip rendering, camera, and audio on the server build
  (e.g. PlayerCamera, shake, HUD) — they idle harmlessly otherwise but waste server cycles.
- **Dev workflow:** support launching a local headless server + connecting editor/build
  clients; retain a single-player smoke path for solo testing.
- **Out of design, into deployment:** hosting provider (Photon Fusion hosting vs.
  self-hosted VM) and room allocation — does not affect the code design.

**Done when:** 2+ clients connect to a headless server, choose teams, the host-client
starts the match, all players spawn on the correct teams, and PvP collision + flag capture
behave as today — with no player acting as server.

### Phase 2 — Area of Interest + projectile pooling (scale)

**Goal:** cut player-sync bandwidth so the link doesn't saturate at 20p.

- **Enable Area of Interest.** Turn on interest management in the network config and add a
  per-player interest provider (interest region around each player).
- **Always-interested set (the footgun):** explicitly mark the flags, `CTFGameManager`,
  `TeamScoreManager`, and any HUD/objective-driving networked objects as globally
  interested so flag arrows, carrier markers, and score keep working at any distance.
  Enemies are interest-scoped by range.
- **Object-pool projectiles.** Replace `Runner.Spawn`/`Despawn`
  ([PlayerCombat.cs:269](../../../Assets/Scripts/Player/PlayerCombat.cs), [Projectile.cs:103](../../../Assets/Scripts/Player/Projectile.cs))
  with a Fusion `INetworkObjectProvider`-backed pool to remove allocation/GC hitches.
- **Send-rate: measure then tune.** Profile bandwidth with AoI on before changing send
  rates; tune per-interest send rates only if a deficit remains. Do not blindly raise send
  rate — it fights the 20p budget.

**Done when:** with 20 simulated players, per-client inbound bandwidth is materially lower
(target the documented ~3× reduction on player sync), the flag HUD/score remain correct for
distant players, and projectile spawn/despawn no longer produces GC spikes.

### Phase 3 — Combat prediction (cosmetic, client-side)

**Goal:** own combat actions feel instant on the firing client, with the server still
authoritative over actual hits/damage. **Lag compensation is dropped** (see decision above) —
the server keeps its existing `OnTriggerEnter2D` / `OverlapBoxAll` hit detection unchanged.

- **Cosmetic local prediction of shooting.** The shoot cooldown is a predicted `TickTimer`
  on input authority ([PlayerCombat.cs:93](../../../Assets/Scripts/Player/PlayerCombat.cs)),
  so the client reliably knows locally whether a shot is allowed. On input, the input-authority
  client immediately plays cosmetic, non-networked feedback (muzzle flash + a brief tracer);
  the server's authoritative networked projectile arrives ~½ RTT later and is the real one.
  The cosmetic feedback is short-lived so it never lingers alongside the real projectile.
- **Predicted melee feedback.** Melee swing animation is already predicted
  ([PlayerCombat.cs:149](../../../Assets/Scripts/Player/PlayerCombat.cs)); no change needed.
  A predicted melee *hit marker* is explicitly NOT done — without lag comp it would produce
  false-positive markers that feel worse than the current server-driven marker.
- **No authority/hit-detection change.** Damage, stun, projectile spawn, and hit detection
  stay exactly as today on the server. This phase only adds client-local visuals.

**Done when:** firing produces immediate local muzzle/tracer feedback on the shooter's client
regardless of RTT, the real networked projectile still governs actual travel/damage, and there
is no lingering double-projectile or phantom-damage artifact.

## Risks & mitigations

- **Lobby rework regresses team assignment.** Mitigation: the choice-collection path is
  already reliable-data based; change is concentrated in the start trigger. Test the
  host-client start gate with late joiners and leavers (existing `RefreshStartGate` logic).
- **AoI hides objects that must stay visible (flag HUD/score).** Mitigation: the
  always-interested set is an explicit, reviewed list; add a smoke test that a distant
  player still sees flag state and score.
- **Cosmetic prediction lingers as a double-projectile.** Mitigation: the predicted muzzle/
  tracer is short-lived and non-networked; it never carries damage, so worst case is a brief
  visual, not a gameplay artifact.
- **Server build accidentally runs client-only systems.** Mitigation: gate rendering/camera
  /audio behind a server-build check; verify headless boot has no missing-Camera errors.

## Out of scope

- Camera responsiveness (look-ahead, dead-zone, follow smoothing) — handled separately.
- Shared Mode / client authority — rejected (see Approaches).
- Anti-cheat / input validation — not required (friends invite-only).
- Host migration — irrelevant on a dedicated server.
