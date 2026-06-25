# Zone-Bound, Center-Scaled Enemy AI — Unity Setup Guide

This is the editor-side wiring for the new enemy AI (the code is done and merged on
branch `6/24`). It also documents the two bugs found on first play-test and how the
wiring prevents them.

## What the system is made of

| Piece | Type | Where | What it needs from you |
|---|---|---|---|
| `EnemyAI` | MonoBehaviour on each enemy prefab | `Assets/Scripts/Enemy/Base/EnemyAI.cs` | **Player Layer Mask** set (detection). Telegraph/wander-pause fields optional. |
| `EnemyStats` | ScriptableObject, one per enemy type | `Assets/Scripts/Enemy/Types/EnemyStats_1..7.asset` | The **AI Ranges** (detection / attack / leash / wander) + existing combat/move stats. |
| `Enemy` | NetworkBehaviour on each enemy prefab | `Assets/Scripts/Enemy/Base/Enemy.cs` | `stats` assigned (already is). Resolves difficulty at spawn. |
| `DifficultyRingConfig` | ScriptableObject, ONE shared asset | create under `Assets/Settings/` | Author the rings. Optional — without it, enemies use base stats. |
| `ArenaCenter` | MonoBehaviour, ONE per gameplay scene | `Assets/Scripts/Enemy/Base/ArenaCenter.cs` | Place at map center. Optional — without it, enemies use base stats. |
| `GameSettingsManager` | scene object | `Assets/Scripts/ScriptableObjects/Game Settings Manager.cs` | Drag the `DifficultyRingConfig` into its new field. |

The AI runs **authority-only** (host/server). Other clients only interpolate position and
mirror facing + the telegraph flash — that's expected.

---

## STEP 1 — Fix detection (REQUIRED — this was the "no lock-on" bug)

**Why:** the AI's detection field was renamed from `playerLayer` to `playerLayerMask`
during the rewrite. Unity binds serialized values by name, so every enemy prefab's mask
reset to **Nothing (0)** and detection matched no colliders. (You should see a console
warning: *"EnemyAI playerLayerMask is unassigned…"*.)

For **each** of the 7 enemy prefabs in `Assets/Scripts/Enemy/Prefabs/`
(Blue, Green, Indigo, Orange, Red, Violet, Yellow):

1. Open the prefab.
2. Select the root object → find the **Enemy AI** component → **Detection** section.
3. Set **Player Layer Mask** to **`Player`** (this project's Player layer is layer 8).
4. In the **Jump** section, set:
   - **Ground Layer** → `Ground` (layer 3). *(If left empty, jumping is disabled and you'll see a console warning — the enemy can never detect ground.)*
   - **Obstacle Layer** → `Ground` only (terrain to hop, e.g. low walls/ledges), or leave
     **empty** to disable terrain-hopping. **Do NOT put `Enemy` here** — enemy-vs-enemy is
     handled by avoidance below, not jumping (two enemies can't hop over each other).
5. In the **Enemy Avoidance** section, set:
   - **Enemy Avoid Layer** → `Enemy` (layer 7). While wandering, an enemy that meets
     another enemy turns around instead of grinding into it. (Leave empty to disable.)
6. Save the prefab.

> Tip: you can multi-select all 7 prefabs in the Project window and set these masks
> once in the Inspector to apply to all.

The remaining **Jump** fields have sensible defaults (jump force 7, cooldown 0.6s, probe
distance 0.6, ground-check radius 0.15, chase-jump height 1.5). The enemy prefab uses
`gravityScale: 1` (floaty); if jumps arc too high, lower **Jump Force**. A green gizmo at
the feet shows the ground-check when the prefab is selected.

While you're there, the old fields `pointA`, `pointB`, `detectionRange`, `attackRange`,
`playerLayer` may still appear as leftover ("script has been changed") values — they're
harmless and disappear when the prefab is re-saved. Only **Player Layer Mask** matters.

---

## STEP 2 — Set the AI ranges on each EnemyStats asset (REQUIRED)

Detection/attack/leash/wander ranges now live on `EnemyStats`, not the AI component.

For **each** `Assets/Scripts/Enemy/Types/EnemyStats_*.asset`, open it and set the new
**AI Ranges** section. Suggested starting values:

| Field | Suggested | Meaning |
|---|---|---|
| Detection Range | 8–12 | how far it senses players |
| Attack Range | 1.5 | how close to start an attack |
| Leash Radius | 10–14 | hard max distance from spawn it will ever travel |
| Wander Radius | 4–6 | how far it roams while idle (keep ≤ Leash Radius) |

If these show as `0`, the enemy won't roam or detect — set them. Note `EnemyStats_5`
(Blue) has `moveSpeed: 15`, which is quite fast; lower it (e.g. 3–5) if wandering looks
frantic.

---

## STEP 3 — (Optional) Difficulty rings

Skip this and enemies just use their base stats everywhere (perfectly playable). To enable
center-scaled difficulty:

1. **Create the config:** Project window → right-click `Assets/Settings/` →
   **Create → Enemy → Difficulty Ring Config**. Name it `DifficultyRingConfig`.
2. **Author rings**, ordered INNER → OUTER (ascending Max Distance From Center). Example:

   | # | Max Distance | Health× | Damage× | Speed× |
   |---|---|---|---|---|
   | 0 | 10 | 3 | 2.5 | 1.4 |
   | 1 | 25 | 2 | 1.8 | 1.2 |
   | 2 | 50 | 1 | 1 | 1 |

   Near-center enemies hit ring 0 (toughest); far enemies hit ring 2 (baseline).
3. **Wire it:** select your `GameSettingsManager` object in the scene → drag the asset into
   the new **Difficulty Ring Config** field.

## STEP 4 — (Optional, needed only with Step 3) Place ArenaCenter

1. Create an empty GameObject named `ArenaCenter` at the map's center point.
2. Add the **Arena Center** component.
3. Exactly one per gameplay scene. (An orange gizmo marks it.)

Without both a `DifficultyRingConfig` AND an `ArenaCenter`, the AI logs one warning and
falls back to base stats (×1.0). That is a supported mode, not an error.

---

## STEP 5 — Verify

1. **EditMode tests:** Window → General → Test Runner → EditMode → Run All. Expect the
   `DifficultyRingConfig` and `EnemyAILeash` suites green.
2. **Play-test (as host):**
   - Idle enemies **walk back and forth** within Wander Radius of their spawn, pausing
     between moves. *(Was broken: the "not roaming" bug — now fixed.)*
   - Walk a player near an enemy → it **chases**. *(Needs Step 1 done.)*
   - Run the player far past the enemy → it **stops at its leash and walks home**, then
     resumes wandering.
   - Telegraph flash + dodge window still work; contact deals damage.
   - Dead / stealthed players are ignored.
   - Two enemies walking into each other **turn around** and head apart instead of
     grinding in place. *(Note: two enemies chasing the same player from the same side
     can still bunch up — that's an accepted trade-off of staying solid.)*
   - A player standing on a ledge above a chasing enemy gets **jumped toward**.
   - (If Step 3 done) enemies spawned near `ArenaCenter` are visibly tankier/harder.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Enemy never chases the player | `Player Layer Mask` unset on prefab (warning in console) | Step 1 |
| Enemy never chases, mask is set | Player GameObject isn't on the `Player` layer, or detection range is 0 | Put player on `Player` layer; set Detection Range (Step 2) |
| Enemy doesn't roam / moves once then freezes | (fixed in code) had been the 2D-target bug; if still seen, Wander Radius is 0 | Set Wander Radius > 0 (Step 2) |
| Enemy slides off forever | Leash Radius is 0 | Set Leash Radius (Step 2) |
| "no DifficultyRingConfig/ArenaCenter" warning | Difficulty not wired | Expected if skipping Step 3/4; otherwise do them |
| Enemies never jump / "groundLayer is unassigned" warning | Ground Layer mask unset on prefab | Set Jump → Ground Layer = `Ground` (Step 1) |
| Enemies grind into each other while wandering | Enemy Avoid Layer unset | Set Enemy Avoidance → Enemy Avoid Layer = `Enemy` (Step 1) |
| Enemies jump in place at each other | `Enemy` is in Obstacle Layer | Remove `Enemy` from Obstacle Layer; use Enemy Avoid Layer instead (Step 1) |
| Enemies jump too high / float | gravityScale 1 + high jump force | Lower Jump Force on the prefab |
| Enemy jump-spams against an un-climbable wall | obstacle ahead it can't clear | Cosmetic; the jump cooldown caps it. Lower Obstacle Probe Distance or exclude that wall layer |
| Only the host sees the AI move/jump | By design — AI is authority-only; clients interpolate | none |

---

## Code bugs found & fixed during this setup (for the record)

1. **No roaming** — `EnemyAI.PickWanderTarget` used `Random.insideUnitCircle` (a 2D offset
   with a Y component), but this is a gravity platformer where movement only sets X
   velocity. The 2D arrival check could never be reached, so the enemy walked to one X and
   froze. Fixed: wander now picks a horizontal offset at home height and arrival checks X
   distance only (commit `14afd32`).
2. **No lock-on** — prefab `Player Layer Mask` reset to 0 after the field rename; this is
   the wiring in Step 1, plus a startup warning was added so it fails loud (commit
   `5ff7b76`).
