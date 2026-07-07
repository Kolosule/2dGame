# Menu & Lobby Revamp — Design

**Date:** 2026-07-06
**Status:** Approved
**Goal:** Alpha-ready menu system whose only job is getting 20 players into a single lobby to stress-test the network. The first player to join (or the host in host mode) can start the match.

## Context

The current system works but is blind and brittle for a 20-player session:

- `GameNetworkManager` (~590 lines) mixes boot-mode resolution, UI wiring, lobby protocol, and scene loading.
- `TeamSelectionUI` shows hardcoded "0 Players" team counts; there is no roster, no player count, no connect-error surface.
- The start gate requires *every* connected player to submit a team choice — one AFK tester blocks the match.
- Host-mode and dedicated-mode drive the Start button through two different code paths.

What already works and is kept: `NetworkBootMode` (batch/`-dedicatedServer` → headless server), `LobbyHostPolicy` (lowest active PlayerId is the designated host-client), the reliable-data transport (no NetworkObjects exist in the menu scene), the fixed session name (`PvPvERoom`) and `maxPlayers = 20`, and the `LobbyTeamChoices` / `LobbyLoadoutChoices` handoff that `NetworkedSpawnManager` reads at spawn.

## Decisions (from brainstorming)

- **Topology:** both host mode and dedicated server, designed dedicated-first. One UI path driven by server snapshots; host mode feeds its own UI via local loopback.
- **Teams / start gate:** server auto-assigns a balanced team on join; switching is optional. Start is enabled for the designated host whenever ≥1 player is connected. Nobody can block the match.
- **Identity:** nickname input on the menu, persisted to PlayerPrefs, sent to the server over reliable data. Roster shows real names.
- **Loadout picker:** kept, moved into a collapsible sub-panel. Untouched = server default order.

## Architecture

Four pieces, splitting along the network/UI seam:

1. **`LobbyProtocol`** (new, pure C#, `Assets/Scripts/Net/LobbyProtocol.cs`) — serialization for all lobby messages over Fusion reliable data.
   - Client → server: nickname, team-switch request, loadout order (existing byte format), start-match.
   - Server → client: a single **`LobbyState` snapshot** — full roster (per player: PlayerId, nickname, team, is-designated-host) plus `canStart` and `maxPlayers`.
   - No UnityEngine types; unit-testable.
2. **`LobbyServerState`** (new, pure C#, `Assets/Scripts/Net/LobbyServerState.cs`) — the server's lobby brain: roster dictionary, balanced auto-assign on join (smaller team wins, tie → Team 1), team-switch handling, host designation via `LobbyHostPolicy`, start rule (≥1 player), snapshot production.
3. **`MainMenuUI` + `LobbyScreenUI`** (new MonoBehaviours, replacing `TeamSelectionUI`) —
   - Menu screen: nickname `TMP_InputField` (PlayerPrefs-persisted), Join, Host, status line.
   - Lobby screen: "Players: X/20" header, two team columns of name rows, Switch Team buttons, collapsible loadout sub-panel (existing picker logic moves over unchanged), Start button rendered only for the designated host, status line.
   - The lobby screen renders **purely from the received `LobbyState`**; it holds no authoritative state.
4. **`GameNetworkManager`** (slimmed) — boot-mode resolution, runner setup, the three StartGame paths, Fusion callbacks. Delegates lobby decisions to `LobbyServerState`, pixels to the UI components.

**Key unification:** in host mode the server builds the same snapshot and applies it to its own UI through a direct local call instead of the wire. One rendering path for both modes.

## Data flow

**Join:** nickname → Join/Host click → "Connecting…" → on connect, client sends nickname. Server `OnPlayerJoined` creates a roster entry with a placeholder name ("Player N"), auto-assigns the balanced team, records it in `LobbyTeamChoices`, broadcasts. Nickname arrival updates the entry and re-broadcasts.

**Broadcast discipline:** every lobby change (join, leave, nickname, team switch) sends one full snapshot to all players. Full-snapshot-every-time makes late joins and dropped messages self-healing. 20 players × ~20 bytes ≪ 1 KB.

**Team switch:** request → server updates roster + `LobbyTeamChoices` → broadcast. No local prediction; menu round-trip latency is invisible.

**Start:** designated host clicks Start → start-match message → server re-validates (sender is designated host, ≥1 player, `gameStarting` false) → `LoadScene(Gameplay)`.

**Late join after match start:** the server still auto-assigns into `LobbyTeamChoices` in `OnPlayerJoined` while the match runs, so `NetworkedSpawnManager` spawns mid-match joiners on a balanced team. Testers can trickle in without a restart.

**Loadout:** unchanged — submitted from the collapsible panel; missing entry = server default.

## Error handling

- **Connect failure:** menu stays up, status line shows the human-readable `ShutdownReason`, buttons re-enable.
- **Session full:** Fusion refuses connection #21 via `PlayerCount`; surfaces through the same failure path.
- **Disconnect / shutdown mid-lobby:** existing `OnShutdown` cleanup runs; lobby closes, menu returns with "Disconnected: <reason>". Nickname survives via PlayerPrefs.
- **Designated host leaves:** `LobbyHostPolicy` re-resolves to next-lowest id; the following broadcast moves the Start button automatically.
- **Start races:** server-side re-validation (`gameStarting` flag + sender check) ignores stale/double Starts.
- **Nickname hygiene:** trimmed, capped at 16 chars, empty → placeholder. Enforced on the input field *and* server-side on receipt.
- **Malformed payloads:** every `LobbyProtocol` parse is length-checked and rejects rather than throws.

## Testing

**EditMode (pure C#, runnable outside Unity with the bundled Roslyn workaround):**

- `LobbyProtocol` round-trip for every message type; truncated/garbage payload rejection.
- `LobbyServerState`: balanced auto-assign (alternating joins, tie → Team 1), team switch updates counts, host designation follows lowest id and re-resolves on leave, `canStart` true from the first player, snapshot contents.
- `LobbyHostPolicyTests` updated: `CanStart` becomes "≥1 active player" (the has-chosen predicate goes away).

**Scene work (`MainMenu.unity`, TMP throughout):** menu panel (title, nickname field, Join, Host, status) and lobby panel ("X/20" header, two team columns via `VerticalLayoutGroup` with up-to-20 name rows, Switch Team buttons, collapsible "Loadout" sub-panel, host-only Start, status). Scene YAML wired directly; visual polish left to the editor.

**Manual verify:** multi-peer in-editor (host + client), then dedicated server + 2 clients per the dedicated-server testing guide — roster updates, team switch, Start-button migration on host leave, match start from the designated client.

## Out of scope

Session browser, party codes, region selection, matchmaking, host migration mid-match, reconnect-to-match, FusionMenu package adoption. None of these serve the alpha stress-test goal; the FusionMenu option stays available post-alpha.
