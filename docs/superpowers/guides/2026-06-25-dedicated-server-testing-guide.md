# Dedicated Server — Implementation & Verification Guide

> **Gameplay guide refreshed 2026-07-28; Azure endpoint and headless-presentation notes refreshed
> 2026-08-28.**

This is the **Unity-Editor-side** work the agent can't run (no Unity CLI in the authoring
environment): one-time setup, then the verification procedures for the dedicated-server topology
and the responsiveness phases — Phase 1 (dedicated server + lobby, §D), Phase 2a (Area of Interest,
§B + §E), Phase 2b (projectile pooling, §F), and Phase 3 (cosmetic shoot prediction, §G) — plus
current-game checks that must survive the server topology (§H).

**Where this fits:**
- **This guide** — verify the server topology and gameplay behave correctly (run locally with MPPM
  or against the real host).
- **[Azure runbook](../../azure-dedicated-server-runbook.md)** — build the Linux Dedicated Server,
  provision the VM, deploy, and run it on Azure for the monthly weekend session (static
  `<AZURE_PUBLIC_IP>`, systemd `gameserver`).

Do the setup before the matching verification. **Phase 2a will visibly break the game if its scene
wiring (§B) is skipped** — enabling AoI culls anything not in a player's region or marked
always-interested. **Phase 2b does nothing until you add `Poolable` to the projectile prefab (§F).**

---

## A. One-time setup

1. **Open the project in Unity** so it imports any new scripts and generates their `.meta` files.
   Watch the Console for compile errors — expected: **none**. If you see "two assembly definitions in
   the same folder," something was placed wrong: the AoI scripts must be in
   `Assets/Scripts/AreaOfInterest/` and pooling in `Assets/Scripts/Pooling/` (plain `Assembly-CSharp`),
   not under `Assets/Scripts/Net/` (the engine-free `Game.Net` asmdef).

2. **Run the full EditMode suite:** `Window → General → Test Runner → EditMode → Run All`.
   Expected: every test is green; the exact count grows as coverage is added. Examples include:
   - **Net** — the dedicated-server + lobby logic:
     - `DedicatedServerEndpointConfigTests` (27): defaults, command-line/environment precedence,
       IPv4/port validation, relay-only values, missing values, and unrelated arguments.
     - `NetworkBootModeTests` (5): batch-mode → DedicatedServer, `-dedicatedServer` arg →
       DedicatedServer, interactive → Client, unrelated/null args → Client.
     - `LobbyHostPolicyTests` (7): host = lowest id / empty → NoHost / re-designation; CanStart
       0 players=false, 1 player=true, 20 players=true.
     - `LobbyProtocolTests` (9): nickname sanitize + round-trip, snapshot round-trip
       (empty + full 20-player roster), truncated/bad-team/trailing-byte rejection.
     - `LobbyServerStateTests` (7): balanced auto-assign (tie → team 1), rejoin keeps team,
       switch validation, nickname sanitize, host re-resolution, snapshot contents.
   - **PlayerMovement (21)** — `MovementMathTests`: accel/decel, dash momentum, apex gravity.
   - **Combat (25)** — `SwingPhaseTests`, `HitCooldownLedgerTests`, `FlashCurveTests`,
     `DamageNumberMotionTests` (melee phase timing, per-target hit cooldown, hit-landed FX curves).
   - **PlayerAnimation (14)** — `PlayerLocomotionResolverTests`: locally-derived locomotion +
     hysteresis.
   - **Buffs (18)** — `BuffUnlockTests`: deposit-earned tier thresholds and unlock ordering.
   - **Hud (23)** — `AuraTiersTests`, `BuffTierVisualTests`, `CooldownFillTests`,
     `HealthSegmentsTests`: the rebuilt event-driven HUD's pure display math.
   - **EnemyAI (16)** — `DifficultyRingConfigTests`, `EnemyAILeashTests`, `EnemyAIMovementTests`:
     center-scaled difficulty rings + zone-leashed wander.
   - **Sky (7)** — `PulseMathTests`, `StarfieldMathTests`: client-local nebula pulse + starfield
     placement (no networking).

3. **Confirm Build Settings scenes:** `File → Build Settings` — `MainMenu.unity` at index **0**,
   `Gameplay.unity` at index **1** (`GameNetworkManager.gameplaySceneIndex` defaults to 1). The
   dedicated-server boot skips the menu automatically, but the indices still need to be correct.

4. **Confirm the Photon region pin.** `Fusion → Realtime Settings` → **Fixed Region** must read
   **`usw`** (already committed in `PhotonAppSettings.asset`). Server and client builds must share it,
   or cross-region players silently can't discover the session — see the runbook's "pin the Photon
   region" section. A region change only lands in builds made *after* it.

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

There are three run contexts. Pick by what you're verifying.

### C1. Against the real Azure host (closest to production)

The server runs under systemd on Azure (`<AZURE_PUBLIC_IP>`, session `PvPvERoom`, region `usw`).
If it's deallocated, bring it up first:

```bash
az vm start -g game-rg -n game-server
```

Then just launch **normal player builds** (or Play in the Editor as a client), enter a nickname,
and **Join** — clients find the session by name through Photon, no IP needed. Full build/deploy/
weekend-start details live in the **[Azure runbook](../../azure-dedicated-server-runbook.md)**. Use
this path to confirm real latency feel and cross-region discovery; use C2/C3 for fast local iteration.

### C2. Local headless server (dedicated topology, no cloud)

Build a player (`File → Build`), then from a terminal launch it headless as the server:

```bash
"<YourBuild>.exe" -batchmode -nographics -logFile ./server.log
```

The boot resolver treats `-batchmode` (or an explicit `-dedicatedServer` arg) as the dedicated
server. Expected in `server.log`: a `[Network] Dedicated server listening on UDP 27015` message.
Then join with Editor Play and/or extra player builds (all use the same `sessionName`, default
`"PvPvERoom"`).

> For an actual Linux Dedicated Server build (the artifact Azure runs), the project now has a
> **Dedicated Server / Linux** build target/profile committed. See the runbook's Part A — the same
> `-batchmode` boot path applies.

### C3. Multiplayer Play Mode (fastest, for lobby/logic smoke)

`Window → Multiplayer Play Mode`, enable 2–3 virtual players. MPPM virtual players are all
**clients**; you still need a headless server process (C1 or C2) for the real dedicated topology. For
a quick lobby-logic check without any server, click **Host** in the menu (`GameMode.Host`) — the host
player lands in the lobby with the Start button directly. This does **not** exercise the
dedicated-server path.

To approach 20 players, launch ~20 client builds (or fewer + MPPM) against one headless server.

### C4. Is *this* machine the server, or just a client? (how to tell)

The role is decided once at startup in `GameNetworkManager.Start()` →
[`NetworkBootMode.Resolve(Application.isBatchMode, commandLineArgs)`](../../../Assets/Scripts/Net/NetworkBootMode.cs):
**batch mode _or_ a `-dedicatedServer` arg → dedicated server** (`GameMode.Server`, no menu);
anything else → an interactive **client** build that shows the menu, where you then pick **Host**
(this machine becomes server + player) or **Join** (pure client). So a single local box can end up
in any of three roles — here's how to tell which:

| This process booted as… | How you can tell |
|---|---|
| **Dedicated server** (headless — local `-batchmode`/`-dedicatedServer`, or the Azure host) | No window, no menu, never spawns a player. `server.log` prints the `[Network]` endpoint message. At runtime `Runner.GameMode == Server` and, the decisive tell, `Runner.IsServer && Runner.LocalPlayer == PlayerRef.None` (the server owns **no** local player). |
| **Local machine as server + player** (solo-dev **Host** button) | You clicked **Host**; you have your own player and the **Start** button immediately. `Runner.GameMode == Host`; `Runner.IsServer` is **true** but `Runner.LocalPlayer != PlayerRef.None` (a Host owns a player, a dedicated server doesn't). |
| **Local machine as a pure client** (**Join** button) | You clicked **Join**; you see the lobby but only the ★ host-client gets Start. `Runner.GameMode == Client`; `Runner.IsServer` is **false**. The server is some *other* process (a local headless build or the Azure host). |

> **`Runner.IsServer` alone can't tell dedicated from Host** — it's true for both (both hold state
> authority). The clean discriminator is `LocalPlayer == PlayerRef.None` (dedicated) vs. a real
> `LocalPlayer` (Host), or just read `Runner.GameMode`.
>
> **The Editor is never in batch mode**, so Play-in-Editor always boots to the menu as a *client*
> — it can only "be the server" via the **Host** button (solo-dev), never the true dedicated path.
> To exercise the real dedicated topology from one machine, run the headless build (§C2) or point at
> Azure (§C1) and **Join**.

### C5. Why the dedicated server does no visual or audio work

A headless server has no screen and no local player. It still runs the authoritative Fusion
simulation, Physics2D, collision, spawning, damage, buffs, flags, scoring, match phases, reconnects,
and scene loading. It does **not** need to draw sprites, animate characters, update cameras/HUDs, or
play sound.

The optimization has three layers:

1. `Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion` sets
   `InvokeRenderInBatchMode` to `false`. Fusion therefore skips `Render()` and `OnChangedRender`
   callbacks in `-batchmode`. Those callbacks are for presentation only; never add gameplay,
   collider, score, health, spawn, or other authoritative changes to them.
2. `Project Settings → Multiplayer → Multiplayer Roles` enables **Strip Rendering Components**,
   **Strip UI Components**, and **Strip Audio Components** for server content. Safety checks remain
   enabled. Unity removes Cameras/Lights/Renderers, Unity UI/EventSystem components, AudioSources,
   AudioListeners, and audio filters from server scenes and prefabs. It does not remove transforms,
   `NetworkObject`/`NetworkBehaviour`, `Rigidbody2D`, `Collider2D`, or Fusion physics components.
3. `DedicatedServerPresentation` disables what automatic stripping does not cover: Animators,
   ParticleSystems, camera-follow/shake scripts, HUD/menu scripts, cosmetic effects, and sky
   animation. It scans once when a scene loads, and
   `PooledNetworkObjectProvider` applies the same rules to spawned or reused network prefabs.
   Every audited Fusion presentation callback also has a headless guard, which covers a local
   `-dedicatedServer` launch even when it is not started with `-batchmode`.

Expected once per process:

```text
[Server] Headless presentation disabled: render callbacks, cameras, audio, UI, and cosmetic animation are inactive.
```

Repeated `AudioListener`, missing-Camera, Animator, UI, shader, or particle warnings are not normal.
If they appear, first confirm the build used the **Dedicated Server / Linux** profile and was launched
with `-batchmode -nographics`; then check that the line above appears before the warning.

**Temporarily debug a Fusion render callback:** set `InvokeRenderInBatchMode` back to `true`, rebuild
the server, and add only the temporary diagnostic needed. Do not move gameplay into that callback.
Restore it to `false` and remove the diagnostic before shipping.

**Full rollback to the old server behavior:**

1. Set `InvokeRenderInBatchMode` to `true` in `NetworkProjectConfig.fusion`.
2. Set `m_StripRenderComponents`, `m_StripUIComponents`, and `m_StripAudioComponents` to `0` in
   `ProjectSettings/Packages/com.unity.dedicated-server/ContentSelectionSettings.asset`.
3. Revert the `DedicatedServerPresentation` integration (its startup activation, scene cleanup,
   network-prefab cleanup, and presentation-only guards).
4. Rebuild the **Dedicated Server / Linux** player into a clean output folder.
5. Start it with the previous command line, connect clients, complete one match, and confirm the
   previous server log and gameplay behavior.

**Functional check after changing presentation settings:** run a dedicated server plus at least two
clients and complete a match. Confirm initial and later team assignments update same-team collision;
then exercise movement, melee, projectiles, enemies, coins, buffs, flag pickup/carry/drop/return/
capture, scoring, death/respawn, return to lobby, and reconnect. Clients must retain their animation,
audio, particles, UI, and camera behavior.

Use the same map, player count, enemy count, and sustained combat duration for both performance runs.
Record results rather than inferring a gain from the code change:

| Build | Average server CPU | p95 tick | p99 tick | Managed memory | GC count | Log size |
|---|---:|---:|---:|---:|---:|---:|
| Before |  |  |  |  |  |  |
| After |  |  |  |  |  |  |

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
- [ ] **Server build hygiene.** The one `[Server] Headless presentation disabled` line appears;
      `server.log` has no repeated Camera, AudioListener, Animator, UI, shader, or particle warnings.
- [ ] **Gameplay parity.** Player-vs-player **physical collision** (body-blocking) and the full
      **flag capture** flow behave exactly as in the old host build.
- [ ] **Latency feel.** No single client has a zero-latency advantage (the old host's unfair
      edge is gone); all clients feel comparable.

---

## E. Phase 2a verification — Area of Interest

Pre-req: §B wiring done; `NetworkProjectConfig` `ReplicationFeatures = 2` (already committed). Run
the headless server + as many spread-out clients as you can.

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

## H. Current-game checks on the server topology

These systems shipped after the responsiveness phases. They mostly worked in the old solo-host
build; the point here is that they still behave correctly **when the server owns state and no client
is the host**. Run the headless server + ≥2 clients.

**Shape-based enemies (Box / Octagon / Circle / Flyer).** Four `EnemyStats` archetypes
(`Assets/Scripts/Enemy/Types/EnemyStats_*.asset`) applied to 7 color prefabs; one shared `EnemyAI`
with data-driven `canFly` flight; center-scaled difficulty + zone leashing.
- [ ] Enemies spawn and act **only on the server**; clients see them replicated (no client-side
      spawn). Positions track smoothly on non-host clients.
- [ ] **Flyer** archetypes actually fly (ignore ground) while Box/Circle/Octagon stay grounded —
      the `canFly` flag is honored the same on server and every client.
- [ ] Difficulty scales toward the center of the map and enemies leash to their zone; a distant
      enemy is AoI-culled and reappears as you approach (no pop-in mid-screen).
- [ ] Enemy attack damage/knockback (the timing-based combat pass) applies server-authoritatively
      and is felt identically on all clients.

**Deposit-earned buffs (`Assets/Scripts/Buffs`).** Tiered buffs unlocked by depositing coins.
- [ ] Depositing crosses a tier threshold → the buff unlocks and its effect applies, driven by the
      **server** (not the local depositing client only). Other clients see the buffed player's
      carrier aura tier update.
- [ ] Rejoining / late-joining players see correct buff/aura tiers (state comes from the server,
      not a local counter).

**Rebuilt HUD (`Assets/Scripts/Hud`, event-driven).** Health segments, coins, team score, buff
icons, cooldown fills.
- [ ] Health/Coins/TeamScore/BuffIcon displays update from network events on **every** client,
      including one that joined mid-match — no polling gaps, no values frozen at spawn defaults.
- [ ] Team score is correct on the far side of the map (depends on §B always-interested wiring).

**Client-local sky (`Assets/Sky`).** Nebula + starfield + placed constellations, no networking.
- [ ] The sky renders on **clients only** — it must NOT spawn/spam on the headless server
      (`server.log` shows no sky/shader/AudioListener churn from it).
- [ ] It needs no `AlwaysInterestedMarker` and no replication; each client renders its own. Confirm
      it looks identical across clients despite never being networked.

**Movement / combat feel.** Accel model, dash momentum, apex gravity, melee swing phases.
- [ ] On a non-host client, movement and dashes feel responsive (client physics prediction is on —
      see the responsiveness design). No rubber-banding that the old host build didn't have.

---

## I. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Distant flag state / score / arrow disappears | `AlwaysInterestedMarker` missing on that object, or no `AreaOfInterestRegistrar` in the scene | §B wiring |
| Carried flag/arrow desyncs only for far players | carrier not getting dynamic interest — registrar absent | ensure one `AreaOfInterestRegistrar` exists; it logs nothing, so confirm the component is present |
| Everything beyond the screen edge vanishes | `areaOfInterestRadius` too small | raise it on the player prefab (try 30–35) |
| Whole world vanishes for all clients | AoI enabled but no regions/markers registered (e.g. registrar never initialized) | confirm `NetworkedSpawnManager` runs on the server and the registrar is in the Gameplay scene |
| Clients can't find the match | Wrong Photon App ID, or **region mismatch** (a build made before the `usw` pin) | server and clients must share the same App ID **and** `FixedRegion = usw`; rebuild both (see §A.4 / runbook) |
| Only local (US) players find the match; AU/JP can't | one peer on an unpinned/Best-Region build | pin `FixedRegion = usw`, rebuild **both** server and client |
| "Two assembly definitions in the same folder" compile error | new scripts placed under `Assets/Scripts/Net/` (the engine-free `Game.Net` asmdef) | move them out (AoI → `AreaOfInterest/`, pooling → `Pooling/`) |
| Far enemy *players* never appear even when close | radius too small, or AoI region not being added | confirm `ReplicationFeatures = 2` and that `PlayerController.FixedUpdateNetwork` runs on the server |
| Enemies visible/moving on the server log, or spawned twice | enemy spawn not gated to server authority | enemies must spawn on the server only; clients receive them replicated |
| Flyer enemies fall / grounded enemies float | `canFly` not read consistently from the `EnemyStats` archetype | confirm the prefab points at the right `EnemyStats_*` asset; flight is data-driven, not per-prefab code |
| HUD values frozen at spawn defaults on a client | HUD not subscribed to the network events (polling holdover) | confirm the display components in `Assets/Scripts/Hud` bind to the event source, not a one-shot read |
| Sky churns the server log / spawns headless | sky components not client-gated | the sky is client-local — it must not run on the dedicated server |
| Recycled projectile deals no damage / dies on spawn | `Projectile.hasHit` not reset on reuse | confirm `Spawned()` starts with `hasHit = false;` |
| Players stop spawning after enabling pooling | provider wrongly pooling the player prefab | only the projectile prefab should have `Poolable`; non-poolable prefabs fall through to base |
| No muzzle/tracer on fire | testing on the wrong peer | use a real client (**Join** button); the cosmetic is skipped on the server/host by design |
| Two projectiles visible per shot | cosmetic lingering next to the real one | lower `tracerDuration`/`muzzleFlashLifetime`; confirm it's a client (host-as-player skips the cosmetic) |

For anything about **building / deploying / running on Azure** (build fails, `scp`/systemd, VM
capacity, weekend start-stop, cost), see the [Azure runbook](../../azure-dedicated-server-runbook.md)'s
own troubleshooting table.

---

## J. Known follow-ups (not blocking these tests)

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
