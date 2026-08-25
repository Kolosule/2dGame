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
| `ScoreboardPanel` | HUD canvas | Reads the table, sorts **everyone into one list**, paints rows. |
| `ScoreboardRowView` | Row prefab (built for you) | Paints one player's row: rank, team stripe, self outline, stats. |
| `ScoreboardInputReader` | Any always-active HUD object | Hold-Tab → show/hide. |
| `MatchPhaseHud` | Existing HUD object | Auto-shows the board during PostMatch. |
| `TeamManager` | Existing scene singleton | Supplies each team's `teamColor` for the row stripes and the summary line. |
| `TeamScoreManager` | Existing scene NetworkObject | Supplies the `BLUE 2 — RED 1` summary above the list. |

### The board is an individual leaderboard

It is **one merged, rank-ordered list of every player** — not two side-by-side team columns. That
was a deliberate rework: the board answers "where do I stand among everyone", and team membership
rides along as a visual accent rather than as structure.

| Element | What it is | Notes |
|---|---|---|
| **Team stripe** | A ~6px full-height colour bar at the far left of each row | Tinted from `TeamManager.Instance.GetTeamData(team).teamColor` at full alpha. The row background stays neutral so the stat text keeps its contrast. If `TeamManager` or a `TeamData` slot is unwired the stripe falls back to neutral grey — the board never blanks or errors. |
| **Rank column** | `1.` `2.` `3.` … as the row's first text cell | Position in the merged sorted list, assigned after sorting. |
| **Self outline** | A bright near-white highlight behind the local player's row | Deliberately *not* a team colour, so it can never read as a third team. It ships as a translucent white plate; drop a 9-sliced border sprite onto its `Image` in the inspector to turn it into a hollow outline — no code change needed. |
| **Team score line** | `BLUE 2 — RED 1` above the list, each half in that team's colour | Replaces the old `BLUE`/`RED` column headers as the in-panel view of team standing. Reads the same `TeamScoreManager` the in-match `TeamScoreDisplay` strip reads. |

**Sort order** is Overall Score descending → Kills descending → `PlayerId` ascending. The last key
is what makes the board *identical on every peer*: with two teams merged, opposing players tying is
routine, and each client builds its list from `Runner.ActivePlayers`, whose iteration order is
per-peer. `PlayerId` is unique, so no pair is ever left unordered.

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

The builder creates a dim backdrop and one centred column containing, top to bottom:

1. the **team score summary line** (`BLUE 0 — RED 0`),
2. the **column header row** — `Rank · Name · Score · K/D · Cap · Coins · Carry · Ret`, with blank
   spacers standing in for the stripe and the two status icons so headers stay aligned with data,
3. the **`Rows` container** holding one hidden row template.

The row template carries the team stripe (leftmost), the rank cell, the seven stat cells, the two
status icons, and the self outline. Every `ScoreboardRowView` field is wired for you, as are
`ScoreboardPanel`'s five serialized references:

`panelRoot` · `rowContainer` · `rowTemplate` · `team1ScoreText` · `team2ScoreText`

> ⚠️ **If your scene was built with the older two-column version, you must re-run the builder.**
> `team1RowContainer` / `team2RowContainer` no longer exist; the scene's saved values for them are
> dead, and `rowContainer` will be empty until the builder runs. A panel with a null `rowContainer`
> shows the backdrop and nothing else.

It's **re-runnable** — it rebuilds only its own `ScoreboardContent` child and leaves anything else
you've added alone. If it can't find the component or a Canvas it tells you in a dialog rather than
failing silently.

The `ScoreboardContent` rect is 800 × 760 — narrower and taller than the old two-column board,
because 20 players are now one column of 20 rows rather than two columns of 10. A row is 760px of
cells, so it sits inside that width with a 20px margin either side. It keeps the
`anchoredPosition = (0, −150)` offset that dodges the PostMatch banner (Step 5).

> ⚠️ **`childControlWidth` / `childControlHeight` must stay on for every layout group the builder
> creates** (`ControlChildSize` does this). A layout group added from script serializes them as
> **false**, and while they are false UGUI ignores every `LayoutElement.preferredWidth/Height` and
> lays children out at their raw `RectTransform` size — 200×50 for a fresh `TextMeshProUGUI`, 100×100
> for a fresh `Image`. That inflates one row to ~1980px inside a 100px row rect, and an overflowing
> layout group ignores `childAlignment` and packs from its left edge, so the board starts at screen
> centre and runs off the right — taking the self-outline highlight with it (it stretches to the row
> rect, so it collapses to a small box at the far left). This was the cause of the first
> offset-to-the-right build; don't reintroduce it.

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
- [ ] **Hold Tab** → board appears; you are rank `1.`, your name is correct, your row carries your team's colour stripe **and** the self-outline highlight
- [ ] The `BLUE 0 — RED 0` line above the list shows both team names in their own colours
- [ ] **Release Tab** → board disappears
- [ ] Kill an AI enemy → your Kills does **not** increase *(correct — only player kills count)*
- [ ] Deposit coins → **Coins** column rises
- [ ] Pick up the enemy flag, hold ~10s → **Carry** rises roughly 1/second
- [ ] Capture → match ends, board **auto-shows** with the winner banner

### Then two peers (Multiplayer Play Mode)

- [ ] Both players appear in **one** list, each with their own team's stripe colour — and only *your* row is outlined on each peer
- [ ] Ranks read `1.` `2.` with no gaps, and both peers show the **same order** (this is the cross-peer tie-break; check it while the two are tied at 0)
- [ ] Deposit coins on one peer → the `BLUE — RED` summary line updates on both
- [ ] Player A kills Player B → A's **Kills** +1 **and** B's **Deaths** +1
- [ ] Dead player shows the dead indicator; clears on respawn
- [ ] Flag carrier shows the carry indicator on **both** peers
- [ ] **Walk far apart, then hold Tab** — ⚠️ *the single most important check.* Both rows must stay fully populated. If the distant player's row blanks, the `AlwaysInterestedMarker` from Step 1 is missing.
- [ ] Set a nickname in the lobby → it appears on the board (this path never existed before)
- [ ] One peer disconnects → their row disappears from the other's board
- [ ] At PostMatch the winner banner is readable **and** the host's Return-to-Lobby button is clickable

### Also worth running

- [ ] **Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All** — includes the 9 `ScoreboardSortTests` cases covering the score → kills → `PlayerId` tie-break chain.
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
| Board is empty but the backdrop shows | Scene still holds the pre-rework wiring, so `rowContainer` is null | Re-run the builder (Step 2) and re-save the scene |
| Every stripe is the same grey | `TeamManager` missing from the scene, or its `TeamData` slots unassigned | Expected fallback — wire `TeamManager`'s team assets |
| Summary line stuck at `BLUE 0 — RED 0` | `TeamScoreManager` not spawned yet | It fills in once the match starts; if it never does, check that object's `NetworkObject` |
| Kills not counting | Killed an AI, or environmental death | Working as designed — only player-attributed kills count |

---

## Known limitations (by design, not bugs)

- **Captures will read 0 for almost everyone.** A capture immediately ends the match, so at most one player per match can ever have 1. It's tracked and displayed but deliberately excluded from the Overall Score — carry time and returns carry the objective signal at finer grain.
- **The self highlight is a plate, not a hollow border.** One `Image` with no sprite can only be a filled rectangle, so the local player's row gets a translucent white backing plate. Assigning a 9-sliced border sprite to `SelfOutline` in the inspector upgrades it to a true outline with no code change.
- **Team standing lives on one line.** With the team columns gone, `BLUE n — RED n` above the list is the board's only team readout; per-team subtotals of individual stats no longer exist.
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
