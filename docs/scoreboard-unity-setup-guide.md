# Scoreboard — Unity Setup Guide

Wiring the in-match scoreboard into `Gameplay.unity`. All the code is written, compiled, and
reviewed; **none of it has ever run inside Unity**, so this pass is the first real verification.

**Branch:** `feat/economy-feedback-surfaces` (the code does not exist on `main`).
**Spec:** [2026-07-29-scoreboard-killfeed-design.md](superpowers/specs/2026-07-29-scoreboard-killfeed-design.md)

Budget ~20 minutes for steps 1–4, then playtest.

---

## What you're wiring

| Piece | Where it lives | What it does |
|---|---|---|
| `MatchStatsManager` | Scene GameObject | The networked stat table. One row per player, indexed by `PlayerId`. |
| `ScoreboardPanel` | HUD canvas | Reads the table, sorts by Overall Score, paints rows. |
| `ScoreboardRowView` | Row prefab (built for you) | Paints one player's row. |
| `ScoreboardInputReader` | Any always-active HUD object | Hold-Tab → show/hide. |
| `MatchPhaseHud` | Existing HUD object | Auto-shows the board during PostMatch. |

Stats are recorded automatically — six server hooks already report kills, deaths, captures,
coins deposited, flag-carry seconds, and flag returns. **You don't wire any of those.**

---

## Step 1 — Verify `MatchStatsManager` (you've added it)

Select your `MatchStatsManager` GameObject and confirm **all three**:

- [ ] **`MatchStatsManager` component** — you'll see five weight fields (Kill 10, Death −10, Coin 0.75, Flag Carry Second 1, Flag Return 20). Leave them for now; they're the tuning surface later.
- [ ] **`NetworkObject` component** — Fusion requires this. Without it the component never spawns and every stat write silently no-ops.
- [ ] **`AlwaysInterestedMarker` component** — ⚠️ **this one is load-bearing and easy to miss.**

**Why the marker matters.** Area-of-Interest culling means a distant player's data doesn't
replicate to you. Without this marker the scoreboard shows rows only for players near you and
blanks out for everyone else — which looks like a flaky bug, not a missing component.
`AreaOfInterestRegistrar` finds the marker at startup (it's invoked from
[`NetworkedSpawnManager.cs:82`](../Assets/Scripts/NetworkedSpawnManager.cs#L82)) and forces the
object to replicate to every player, including late joiners.

Put it next to your existing `TeamScoreManager` / `MatchManager` objects — they use the same pattern.

---

## Step 2 — Build the scoreboard panel

1. Open `Assets/Scenes/Gameplay.unity`.
2. Find your HUD **Canvas** — the same one holding `MatchPhaseHud`'s results panel.
3. Create an empty child GameObject named **`ScoreboardPanel`**.
4. Add the **`ScoreboardPanel`** component to it.
5. Run **`Tools ▸ Match ▸ Build Scoreboard Panel`**.
6. **Save the scene** (Ctrl+S).

The builder creates a dim backdrop, two team columns (BLUE / RED), and one row template, then
wires `ScoreboardPanel`'s four serialized fields for you:

`panelRoot` · `team1RowContainer` · `team2RowContainer` · `rowTemplate`

It's **re-runnable** — it rebuilds only its own `ScoreboardContent` child and leaves anything else
you've added alone. If it can't find the component or a Canvas it tells you in a dialog rather than
failing silently.

The panel stays visible in the editor so you can style it; `Awake` hides it at runtime.

---

## Step 3 — Wire the input reader (hold Tab)

1. Add a **`ScoreboardInputReader`** component — put it on the `ScoreboardPanel` object, or any HUD root that is **always active** (a disabled object can't read input).
2. **`Panel`** → drag your `ScoreboardPanel` component in.
3. **`Scoreboard Action`** → in the Project window expand `Assets/InputSystem_Actions.inputactions` ▸ **`UI`** ▸ **`Scoreboard`**, and drag that action in.

The action already exists with a `<Keyboard>/tab` binding and **no interaction modifier** — that's
deliberate, so the board appears the instant you press rather than after a hold delay.

Input is read locally and never networked; each client independently decides whether to show its
own copy of already-replicated data.

---

## Step 4 — Wire the PostMatch auto-show

1. Select the GameObject with **`MatchPhaseHud`**.
2. Set its new **`Scoreboard Panel`** field to your `ScoreboardPanel`.

During `PostMatch` the board now shows automatically — no Tab hold needed — alongside the existing
winner banner, final score, and return countdown.

---

## Step 5 — Resolve the PostMatch overlap ⚠️

**Read this before playtesting; it's a known open item, not a bug you're discovering.**

The results panel and the scoreboard were each authored as a **full-screen modal card**:

- `ResultsPanel` — full-screen backdrop at 75% black + a 760×480 centred card at 96% opacity
- `ScoreboardContent` — 1000×640, also centred

So at PostMatch they sit on top of each other and whichever draws last hides the other. Ordering
alone can't fix it. The click-blocking half **is** already fixed (the scoreboard's backdrop has
`raycastTarget = false`, so the host's Return-to-Lobby button still works either way).

**Recommended fix — pure inspector work, no code:**

1. Select `ResultsPanel`, and on its `Image` component either set alpha to **0** or uncheck the component. That kills the full-screen dim while keeping its children.
2. Select `ResultsContent` and reshape it into a **top banner strip** — e.g. anchor to top-centre, height ~200, `anchoredPosition.y` around −120.
3. Move `ScoreboardContent` down slightly so the two don't collide.

**Alternatives** if you'd rather not reshape: show only the winner line at PostMatch and let players
hold Tab for detail, or drop the auto-show entirely and make the board Tab-only (that reverses a
spec decision, so decide deliberately).

---

## Step 6 — Test

### Single player first (fastest signal)

`GameNetworkManager` defaults to single-player Host mode. Enter Play mode and check:

- [ ] No console errors on scene load
- [ ] **Hold Tab** → board appears, showing you on your team, name correct
- [ ] **Release Tab** → board disappears
- [ ] Kill an AI enemy → your Kills does **not** increase *(correct — only player kills count)*
- [ ] Deposit coins → **Coins** column rises
- [ ] Pick up the enemy flag, hold ~10s → **Carry** rises roughly 1/second
- [ ] Capture → match ends, board **auto-shows** with the winner banner

### Then two peers (Multiplayer Play Mode)

- [ ] Both players appear, grouped under the right team headers
- [ ] Player A kills Player B → A's **Kills** +1 **and** B's **Deaths** +1
- [ ] Dead player shows the dead indicator; clears on respawn
- [ ] Flag carrier shows the carry indicator on **both** peers
- [ ] **Walk far apart, then hold Tab** — ⚠️ *the single most important check.* Both rows must stay fully populated. If the distant player's row blanks, the `AlwaysInterestedMarker` from Step 1 is missing.
- [ ] Set a nickname in the lobby → it appears on the board (this path never existed before)
- [ ] One peer disconnects → their row disappears from the other's board
- [ ] At PostMatch the winner banner is readable **and** the host's Return-to-Lobby button is clickable

### Also worth running

- [ ] **Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All** — 22 scoreboard cases. These have been executed outside Unity via a harness but never through NUnit.
- [ ] Hold Tab during the Warmup countdown right after the scene loads — should show an empty board, never an error storm.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Board never appears on Tab | Action not assigned, or reader on an inactive object | Step 3 — check both fields, and that the host object is active |
| Board appears but is empty | `MatchStatsManager` missing `NetworkObject`, or not in the scene | Step 1 |
| **Distant players' rows blank out** | **Missing `AlwaysInterestedMarker`** | Step 1 — this is the classic AoI footgun |
| Rows show but names are "Player 3" | Nickname never set in the lobby | Expected fallback — set one in the lobby screen |
| Board frozen while held | Shouldn't happen — it repaints every visible frame | Report it; this was fixed during review |
| Winner banner hidden at PostMatch | The layout overlap | Step 5 |
| A phantom blank row | Row template left active | Re-run the builder; `Awake` should hide it |
| Kills not counting | Killed an AI, or environmental death | Working as designed — only player-attributed kills count |

---

## Known limitations (by design, not bugs)

- **Captures will read 0 for almost everyone.** A capture immediately ends the match, so at most one player per match can ever have 1. It's tracked and displayed but deliberately excluded from the Overall Score — carry time and returns carry the objective signal at finer grain.
- **No column headers.** Rows are a name followed by six unlabeled numbers: Score · K/D · Captures · Coins · Carry · Returns. Adding headers is a small builder change if you want them.
- **No local-player highlight.** Out of scope; usually the first thing playtesters ask for.
- **"Coins" is deposited *value***, the same unit fed to `TeamScoreManager` and `PlayerBuffs` — not a raw coin count. Worth confirming they're 1:1 when you tune weights.
- **No killfeed.** Explicitly cut from the spec — kills appear only as accumulating stats.
- **A leaver's row vanishes** rather than persisting greyed-out.

## Tuning the weights

All five live on `MatchStatsManager` and can be changed in the inspector without touching code:

| Stat | Default | Typical contribution over 8–10 min |
|---|---|---|
| Kill | +10 | 100 for 10 kills |
| Death | −10 | −100 for 10 deaths |
| Coin deposited | +0.75 | ~30–50 typical, ~195 for a runaway farmer |
| Flag carry second | +1 | 60–180 for an objective player |
| Flag return | +20 | 40–80 for a defender with 2–4 |

Kills and deaths sit at parity, so combat swings the board at least as hard as objective play — a
deliberate choice. If the board ends up feeling too combat-driven in practice, lower `killWeight`
and `deathWeight` together rather than inflating the objective weights.
