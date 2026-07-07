# Testing Guide — Dedicated Server Responsiveness (Phases 1, 2a, 2b, 3)

This is the **Unity-Editor-side** work that the agent could not run (no Unity CLI in the
authoring environment): one-time setup, then the verification procedures for Phase 1 (dedicated
server + lobby, §D), Phase 2a (Area of Interest, §B + §E), Phase 2b (projectile pooling, §F), and
Phase 3 (cosmetic shoot prediction, §G).

Each phase ships on its own branch/PR — verify whichever you have merged or checked out. Do the
setup before the matching verification. **Phase 2a will visibly break the game if its scene wiring
(Section B) is skipped** — enabling AoI culls anything not in a player's region or marked
always-interested. **Phase 2b does nothing until you add `Poolable` to the projectile prefab (§F).**

---

## A. One-time setup

1. **Open the project in Unity** so it imports the new scripts and generates their `.meta`
   files:
   - `Assets/Scripts/AreaOfInterest/AlwaysInterestedMarker.cs`
   - `Assets/Scripts/AreaOfInterest/AreaOfInterestRegistrar.cs`
   - (Phase 1's `Assets/Scripts/Net/*` already have `.meta` from the merged PR #45.)
   Watch the Console for compile errors. Expected: **none**. If you see "two assembly
   definitions in the same folder," something was placed wrong — the AoI scripts must be in
   `Assets/Scripts/AreaOfInterest/` (plain `Assembly-CSharp`), not under `Assets/Scripts/Net/`.

2. **Run the lobby EditMode tests:** `Window → General → Test Runner → EditMode → Run All`.
   Expected: **28 green** in the Net suite —
   - `NetworkBootModeTests` (5): batch-mode → DedicatedServer, `-dedicatedServer` arg →
     DedicatedServer, interactive → Client, unrelated/null args → Client.
   - `LobbyHostPolicyTests` (7): host = lowest id / empty → NoHost / re-designation; CanStart
     0 players=false, 1 player=true, 20 players=true.
   - `LobbyProtocolTests` (9): nickname sanitize + round-trip, snapshot round-trip
     (empty + full 20-player roster), truncated/bad-team/trailing-byte rejection.
   - `LobbyServerStateTests` (7): balanced auto-assign (tie → team 1), rejoin keeps team,
     switch validation, nickname sanitize, host re-resolution, snapshot contents.

3. **Confirm Build Settings scenes:** `File → Build Settings` — MainMenu at index **0**,
   Gameplay at index **1** (the gameplay scene index `GameNetworkManager.gameplaySceneIndex`
   defaults to 1).

---

## B. Phase 2a scene wiring (REQUIRED before any AoI test)

In the **Gameplay scene**:

1. Create an empty GameObject named `AreaOfInterestRegistrar` and add the
   **`AreaOfInterestRegistrar`** component. Exactly one in the scene.

2. Add the **`AlwaysInterestedMarker`** component to every networked object whose state the
   HUD / objectives need at **any** distance:
   - Both flag GameObjects (the ones with `Flag`).
   - The `CTFGameManager` GameObject.
   - The score-manager GameObject (`TeamScoreManager`) — only if it is a `NetworkObject`
     (check for a `NetworkObject` component; if score is replicated through `CTFGameManager`
     instead, marking CTFGameManager is enough).
   - Both home-base GameObjects (`NetworkedHomeBase`) if base occupancy is shown in the HUD.

3. Save the scene.

> Rule of thumb: if a piece of HUD/objective state must be correct for a player standing on
> the far side of the map, the networked object that drives it needs an `AlwaysInterestedMarker`.
> Everything else (other players, enemies, coins, projectiles) is replicated *spatially* by the
> per-player region and needs no marker.

---

## C. How to run a dedicated server + clients

**Dedicated server (headless).** Build a player (`File → Build`), then from a terminal:

```bash
"<YourBuild>.exe" -batchmode -nographics -logFile ./server.log
```

The boot resolver treats `-batchmode` (or an explicit `-dedicatedServer` arg) as the dedicated
server. Expected in `server.log`: `✅ Dedicated server started — waiting for players.`

**Clients.** In the menu, type a nickname and click **Join**. Either:
- **Editor + builds:** Play in the Editor as one client, run extra player builds as more
  clients (all use the same `sessionName`, default `"PvPvERoom"`), **or**
- **Multiplayer Play Mode (MPPM):** `Window → Multiplayer Play Mode`, enable 2–3 virtual
  players. (Note: MPPM virtual players are all clients; you still need the headless server
  process for the dedicated topology. For a quick lobby-logic smoke test without a server you
  can use the solo-host path below.)

**Solo-dev smoke (no dedicated server):** click **Host** in the menu (`GameMode.Host`); the
host player lands in the lobby with the Start button directly. Useful for quick single-machine
checks; does **not** exercise the dedicated-server path.

To approach 20 players, launch ~20 client builds (or fewer + MPPM) against one headless server.

---

## D. Phase 1 verification — dedicated server + lobby

Run the headless server + at least **3** clients.

- [ ] **Server is not a player.** Only the headless process is the server; no client window
      acts as host. `server.log` shows players joining, no spawn before the match starts.
- [ ] **Roster is live.** Each joining client appears in a team column with its nickname
      within ~a second, on **every** client's screen; the header counts up ("Players: 3/20").
      A client with an empty nickname shows as "Player N".
- [ ] **Balanced auto-assign.** Joiners alternate columns (smaller team gets the newcomer);
      nobody has to pick anything.
- [ ] **Team switch.** Clicking "Join Team 2" moves your row to the other column on all
      screens; your switch buttons flip which side is enabled.
- [ ] **Host-client designation.** Exactly one client — the **lowest PlayerId** (first to
      join) — shows the ★ marker and the Start button. The others never show it.
- [ ] **Start gate.** The Start button is enabled from the moment ≥1 player is in the lobby —
      an AFK client cannot block the match.
- [ ] **Host re-designation.** With nobody having started yet, disconnect the host-client in
      the lobby. The **next-lowest** client's screen should now show ★ + Start.
- [ ] **Start → load.** Host-client clicks Start → the Gameplay scene loads on **all** clients
      and the server; every player spawns on their roster team.
- [ ] **Mid-match late join.** A client joining after Start spawns straight into gameplay on
      the smaller team.
- [ ] **Disconnect surface.** Kill the server while a client sits in the lobby → that client
      returns to the menu with a "Disconnected: ..." status line.
- [ ] **Server build hygiene.** `server.log` has no repeated `AudioListener` warnings and no
      camera errors (cameras/audio are disabled on the server after scene load).
- [ ] **Gameplay parity.** Player-vs-player **physical collision** (body-blocking) and the full
      **flag capture** flow behave exactly as in the old host build.
- [ ] **Latency feel.** No single client has a zero-latency advantage (the old host's unfair
      edge is gone); all clients feel comparable.

---

## E. Phase 2a verification — Area of Interest

Pre-req: Section B wiring done; `NetworkProjectConfig` `ReplicationFeatures = 2` (already
committed). Run the headless server + as many spread-out clients as you can.

**Measuring bandwidth.** Use Fusion's runtime stats overlay (add a `FusionStats` component / the
Fusion realtime-stats canvas) or call `Runner.TryGetFusionStatistics(...)`. Compare **inbound
traffic per client** in two runs: AoI off (`ReplicationFeatures = 1`) vs. on (`= 2`), with
players spread across the map.

- [ ] **Bandwidth drops.** With players spread out, per-client inbound traffic is materially
      lower with AoI on (target ~3× reduction on player sync). If players are all clustered, the
      difference will be small — that's expected (everyone is in everyone's region).
- [ ] **No pop-in.** A nearby enemy player / enemy / coin is already visible **before** it
      reaches your screen edge. If objects pop in at the edge, raise
      `PlayerController.areaOfInterestRadius` (default **25**) on the player prefab and re-test.
- [ ] **HUD/score at distance.** A player on the far side of the map still sees correct flag
      state, score, and the flag-direction arrow.
- [ ] **Flag carrier at distance.** While an enemy carries **your** flag far away (outside your
      region): the flag-direction arrow tracks them, and the carried flag renders above them.
      After they drop it or it returns home, the now-distant former carrier is culled again (the
      head marker / their body disappears when far). This confirms the dynamic carrier-interest
      add/remove.
- [ ] **Capture flow intact.** Pickup → carry across the map → capture in base still scores
      correctly and ends the match.

**AoI debugging aids.**
- `Runner.GetObjectsInAreaOfInterestForPlayer(player)` (server-only) lists what a given player
  currently receives — handy to confirm a far flag/carrier is present and a far non-marked
  object is absent.
- `Runner.GetAreaOfInterestGizmoData(list)` exposes the AoI cell grid for a debug overlay.

---

## F. Phase 2b verification — projectile object pooling

**Setup (one-time):** open the project so Unity generates `.meta` for the new `Assets/Scripts/Pooling/`
scripts. Then open the **projectile prefab** (the `NetworkObject` assigned to
`PlayerCombat.projectilePrefab`) and add the **`Poolable`** component. No other prefab needs it —
players etc. stay un-pooled. (Pooling does nothing until the prefab is marked.)

- [ ] **Functional parity.** Fire repeatedly. Projectiles spawn, travel, hit (damage + stun apply),
      and despawn exactly as before.
- [ ] **Reuse correctness (the crux).** Fire a shot that hits something, then fire again: the second
      projectile must still deal damage. A recycled projectile that does nothing / dies instantly
      means `Projectile.hasHit` isn't being reset on reuse (`Projectile.Spawned` change missing).
- [ ] **GC.** With the Unity Profiler (GC Alloc track), sustained rapid fire should no longer show
      the per-shot allocation/`Destroy` spikes — allocations from projectile spawn/despawn drop
      toward zero once the pool warms up.
- [ ] **Non-pooled unaffected.** Players still spawn on join and despawn on leave normally (no
      `Poolable` → default Instantiate/Destroy path).

---

## G. Phase 3 verification — cosmetic shoot prediction

**Setup:** Unity generates `.meta` for `Assets/Scripts/Player/CosmeticTracer.cs`. Optionally assign a
`muzzleFlashPrefab` on the player prefab's `PlayerCombat` (leaving it null is fine — the
code-generated tracer still fires). Tune `tracerColor/Length/Width/Duration` to taste.

Run the headless server + a real client (joined via the **Join** button). On the client:

- [ ] **Instant feedback.** Firing shows the muzzle/tracer the moment you press shoot — no
      round-trip delay (before this, the projectile only appeared after ~½ RTT).
- [ ] **Real projectile still governs gameplay.** The networked projectile still travels and deals
      damage/stun exactly as before; the cosmetic tracer is brief and doesn't linger beside it.
- [ ] **No duplicates.** Rapid fire / lag does not spawn multiple tracers per shot (the
      `Runner.IsForward` guard holds under resimulation).
- [ ] **Host-as-player unaffected.** In solo-dev host mode (**Host** button) there is no
      double projectile — the cosmetic is skipped (`!HasStateAuthority`) and the real projectile is
      already instant.
- [ ] **Tracer renders in a build.** If the tracer is invisible in a player build, add
      `Sprites/Default` to Project Settings → Graphics → Always Included Shaders (build stripping).

---

## H. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Distant flag state / score / arrow disappears | `AlwaysInterestedMarker` missing on that object, or no `AreaOfInterestRegistrar` in the scene | Section B wiring |
| Carried flag/arrow desyncs only for far players | carrier not getting dynamic interest — registrar absent | ensure one `AreaOfInterestRegistrar` exists; it logs nothing, so confirm the component is present |
| Everything beyond the screen edge vanishes | `areaOfInterestRadius` too small | raise it on the player prefab (try 30–35) |
| Whole world vanishes for all clients | AoI enabled but no regions/markers registered (e.g. registrar never initialized) | confirm `NetworkedSpawnManager` runs on the server and the registrar is in the Gameplay scene |
| "Two assembly definitions in the same folder" compile error | new scripts placed under `Assets/Scripts/Net/` (the engine-free `Game.Net` asmdef) | move them out (AoI → `AreaOfInterest/`, pooling → `Pooling/`) |
| Far enemy *players* never appear even when close | radius too small, or AoI region not being added | confirm `ReplicationFeatures = 2` and that `PlayerController.FixedUpdateNetwork` runs on the server |
| Recycled projectile deals no damage / dies on spawn | `Projectile.hasHit` not reset on reuse | confirm `Spawned()` starts with `hasHit = false;` |
| Players stop spawning after enabling pooling | provider wrongly pooling the player prefab | only the projectile prefab should have `Poolable`; non-poolable prefabs fall through to base |
| No muzzle/tracer on fire | testing on the wrong peer | use a real client (**Join** button); the cosmetic is skipped on the server/host by design |
| Two projectiles visible per shot | cosmetic lingering next to the real one | lower `tracerDuration`/`muzzleFlashLifetime`; confirm it's a client (host-as-player skips the cosmetic) |

---

## I. Known follow-ups (not blocking these tests)

- **Capture leaves the captor always-interested.** Capture ends the match without going through
  drop/return, so the captor stays in the always-interested set. Harmless for a single-capture
  match (the match is over). If you later add a **score-and-continue / multi-capture** mode, add
  a `RemoveAlwaysInterested(captor)` at the capture point.
- **Pooled instances linger on runner shutdown** (inactive GameObjects freed by scene teardown);
  and **`CosmeticTracer` allocates a `Material` per shot** (GC reclaims it). Both are harmless at
  this game's scale; pool/shared-material them only if the profiler ever flags it.
- **Traveling ghost projectile not built.** Phase 3 gives an instant muzzle/tracer but the real
  projectile still appears ~½ RTT later. If that travel delay still feels bad after testing, a full
  cosmetic ghost projectile is the next lever (deliberately skipped — it risks double-vision).
- ~~Three cosmetic stale doc-comments from Phase 1~~ — resolved: the menu/lobby revamp
  (2026-07-06) removed `RefreshStartGate`/`SetStartAvailable` entirely (snapshot-driven UI).
