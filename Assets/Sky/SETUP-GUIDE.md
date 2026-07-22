# Sky Background — Step-by-Step Setup Guide

This is the hands-on walkthrough for turning the code on `feat/sky-background` into a working sky in
your **Gameplay** scene. I wrote the scripts and verified they compile and that the math is correct,
but I can't open your Unity editor — so the parts that need the editor (baking textures, building the
scene, placing constellations, and looking at it) are the steps below. Follow them in order.

Everything here is **cosmetic and per-client** — no networking. You don't have to configure anything
for multiplayer; each player's camera just sees the same world-fixed sky.

---

## What you'll end up with

- A dim **nebula** and a sparse **star mesh** covering your map, sitting behind all gameplay.
- **3 example constellations** (Triangle, Dipper, Cross) you can move to any spot on the map.
- A tool to author **your own constellations** and pin them to specific map areas.

Estimated time: ~10 minutes the first time.

---

## Step 0 — One-time: create the "Background" sorting layer

The sky renders behind gameplay using a sorting layer. If it doesn't exist yet, the build tool will
warn you and fall back to the default layer (sky would draw *on top* of gameplay — wrong).

1. **Edit ▸ Project Settings ▸ Tags and Layers**.
2. Expand **Sorting Layers**.
3. Click **+** and name the new layer exactly `Background` (capital B).
4. **Drag it to the TOP of the list**, above `Default`. In Unity's list, the top layer draws *first*
   (i.e. furthest back), so `Background` must be above `Default`.

✅ **Check:** the Sorting Layers list reads `Background`, then `Default`, then any others.

---

## Step 1 — Open the scene

Open **`Assets/Scenes/Gameplay.unity`**. The sky is built into whatever scene is open, so make sure
it's this one (not the menu scene).

---

## Step 2 — Bake the textures

Menu: **Tools ▸ Sky ▸ 1. Bake Textures**

This procedurally generates three soft-gradient PNGs into `Assets/Sky/Textures/`:
`star_dot`, `node_glow`, `nebula_cloud`. It's safe to run repeatedly.

✅ **Check:** those three PNGs now exist in the Project window under `Assets/Sky/Textures`. The
Console prints `[Sky] Baked textures: …`.

---

## Step 3 — Build the sky

Menu: **Tools ▸ Sky ▸ 2. Build Sky In Open Scene**

This does everything at once:
- (re-)bakes the textures if needed,
- creates the materials `SkyAdditive`, `SkyAlpha`, `SkyStars` in `Assets/Sky/Materials/`,
- creates a **`SkyRoot`** object in the scene containing a **Nebula** and a **Starfield**,
- creates the 3 example constellation prefabs in `Assets/Sky/Prefabs/` and drops one of each under
  `SkyRoot`.

It's **re-runnable** — running it again deletes the old `SkyRoot` and rebuilds cleanly, so you can't
end up with duplicates.

✅ **Check:** the Hierarchy shows `SkyRoot` with children `Nebula`, `Starfield`, `Triangle`,
`Dipper`, `Cross`. In the Scene view you should see a faint bluish nebula, a field of small star
dots, and three little node-and-line figures near the origin.

> If the Console warns about a missing `Background` layer, go back to **Step 0**, then re-run this step.

---

## Step 4 — Size the starfield to your map

The starfield only scatters stars inside a rectangle you set. The default is a 100×100 area centered
on the origin, which may not match your arena.

1. Select **`SkyRoot ▸ Starfield`** in the Hierarchy.
2. In the Inspector, find the **Star Field Generator** component ▸ **World Bounds**.
3. Set it to your map's extents:
   - `X`, `Y` = the **bottom-left corner** of the play area (in world units),
   - `Width`, `Height` = the size of the play area.
   - Add ~10 units of margin on each side so the camera's zoom-out never reveals an empty edge.
   - *Tip for finding your extents:* click your arena/tilemap/ground object and read its bounds, or
     eyeball the corners in the Scene view with the coordinate gizmo.
4. Right-click the **Star Field Generator** component header ▸ **Regenerate**.

Optional tweaks on the same component: `Star Count` (keep 200–400 for a sparse look), `Seed` (change
to reshuffle the layout), `Min/Max Size`, `Max Brightness`, `Star Color`. Hit **Regenerate** after
any change.

✅ **Check:** stars now fill your whole play area with a little margin.

---

## Step 5 — Place constellations where you want them

This is the part you specifically wanted: constellations pinned to **specific areas of the map**.

**Move the examples:** select `Triangle` / `Dipper` / `Cross` under `SkyRoot` and drag them (or set
their Transform position) to the map areas you want them in. They stay put in the world — the camera
just reveals them as the player moves.

**Author your own:**
1. Menu: **Tools ▸ Sky ▸ Constellation Placer**.
2. Type a **Name**, click **Start Placing**.
3. **Ctrl+Click** in the Scene view to drop each node (do this over the map spot you want).
4. Click **Build Constellation** (needs at least 2 nodes). It creates the nodes + connecting line +
   a pulse component.
5. Click **Save Prefab** — it's saved to `Assets/Sky/Prefabs/`. Drag that prefab into the scene
   wherever you like, as many times as you want.

> Note: if you author constellations *before* creating the `Background` sorting layer (Step 0), their
> renderers will log a harmless "layer not found" warning. Do Step 0 first and you're fine.

✅ **Check:** your constellations sit at the intended map locations.

---

## Step 6 — See the pulse (enter Play mode)

The gentle glow/scale pulse is driven at runtime, so it **only animates in Play mode**, not in the
edit-mode Scene view. Press **Play** and watch a constellation — it should breathe slowly (about a
1-second cycle, ±12% brightness). Different constellations pulse out of sync on purpose.

To adjust: select a constellation, find the **Constellation Pulse** component, and tweak `Frequency`,
`Alpha Amplitude`, `Scale Amplitude`.

---

## Step 7 — Verify

**Automated tests (the math I wrote):**
- **Window ▸ General ▸ Test Runner ▸ EditMode**. You should see `Game.Sky.Tests` with 7 tests
  (StarfieldMath + PulseMath). Click **Run All** → all green.

**Draw-call / batching sanity:**
- **Window ▸ Analysis ▸ Frame Debugger**, click **Enable**, and step through. The sky should add only
  a handful of SRP batches (one for the star mesh, shared batches for the constellations, one for the
  nebula).

**Visual:** stars read as soft dots (not hard squares), the nebula is subtle, constellations are a
touch brighter/warmer than the background stars but not distracting.

---

## Step 8 — Save & commit

1. **File ▸ Save** (the scene).
2. Running the menu items generated new assets and `.meta` files. Commit them so they're tracked:

```bash
git add Assets/Sky Assets/Scenes/Gameplay.unity
git commit -m "chore(sky): baked textures, materials, prefabs + scene wiring"
```

(Your unrelated `DefaultPlayerStats.asset` / `PlayerPrefab.prefab` changes are left alone — don't
`git add -A`.)

---

## Troubleshooting

| Symptom | Cause / Fix |
|---|---|
| Sky draws **on top of** gameplay | `Background` sorting layer missing or below `Default`. Do Step 0 (put it at the top of the list), then re-run **Build Sky**. |
| Material is **bright pink/magenta** | The legacy additive shader isn't in the build. **Edit ▸ Project Settings ▸ Graphics ▸ Always Included Shaders** → add `Legacy Shaders/Particles/Additive`. |
| Stars look like **hard squares**, not dots | Re-run **Tools ▸ Sky ▸ 2. Build Sky In Open Scene** — it assigns the `star_dot` texture to the `SkyStars` material and saves it. |
| Stars **vanish in the edit-mode view** after reopening the scene / a script reload | The star mesh is generated, not saved into the scene. Select `Starfield` ▸ right-click **Star Field Generator** ▸ **Regenerate**, or just press Play. |
| Nebula **doesn't cover the map** | Select `SkyRoot ▸ Nebula` and increase its Transform **Scale** (default 40). |
| Constellations **don't pulse** | Pulse only runs in **Play** mode (Step 6). |
| Console warns about **"Background" layer** | Create it (Step 0), then re-run Build. |

---

## Where to change things later

- **Star density / size / brightness / layout** → `Starfield` ▸ Star Field Generator (Regenerate after).
- **Pulse speed / amount** → each constellation ▸ Constellation Pulse.
- **Nebula strength / tint** → `Nebula` ▸ Sprite Renderer color (alpha ~0.08–0.18) and Scale.
- **Add constellations** → Constellation Placer (Step 5).
- **Replace the art** → drop your own PNG over any file in `Assets/Sky/Textures/` (keep the name).

See `Assets/Sky/README.md` for the condensed version of all of this.
