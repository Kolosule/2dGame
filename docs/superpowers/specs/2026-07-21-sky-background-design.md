# 2D Sky Background — Design

## Context

The `Gameplay.unity` arena currently has no dedicated backdrop. We want a lightweight, low-cost
2D sky: a very sparse starfield plus a small number of hand-placed, softly-glowing constellation
figures pinned to specific areas of the map. The user is a beginner at art; the deliverable must
be self-contained (no external art files) and easy to tweak in the Inspector.

Verified environment (do not re-guess):

- Unity **6000.3.0f1**, **URP 17.3** with the **2D Renderer** (`Assets/Settings/Renderer2D.asset`).
- Main Camera is **orthographic, HDR on**, and runs `Assets/Scripts/Player/Playercamera.cs`
  (`PlayerCamera`) — it does speed-zoom (ortho ~5→7), aim-lean, shake, and decaying impulses.
- Networking is **Photon Fusion 2** (v2.0.12). There is no global `Light2D` in Gameplay.

## Goals

- A world-anchored sky backdrop (nebula + sparse stars) covering the playable map bounds.
- A small number (3–8) of hand-placed constellations pinned to **fixed world locations** on the
  map, each softly pulsing.
- Minimal CPU/GPU cost; beginner-friendly editor workflow; no external art dependencies.

## Non-Goals

- **No parallax / no camera-relative movement.** The background is flat world scenery; the camera
  simply moves over it. Constellations stay pinned to their map location. (A future opt-in nebula
  drift is explicitly out of scope for this pass.)
- No networking. The sky is **100% cosmetic and client-local**: no `NetworkObject`, no
  `[Networked]` state, no Fusion callbacks, and it is **never parented under a networked object**.
- No custom HLSL / Shader Graph. No "bake constellation to texture" feature.
- No changes to the camera, gameplay, or any existing script.

## Key Decisions

- **Glow + pulse:** stock Unity `Sprites/Default` material configured for **additive blending
  (Blend One One)**, driven by a small C# `ConstellationPulse` component (sine on alpha + scale).
  Chosen over Shader Graph for zero custom-shader risk and beginner editability. Because there is
  no global `Light2D`, an unlit/default sprite material renders at full brightness.
- **Textures:** generated procedurally by an editor utility (`SkyTextureBaker`) that bakes soft
  radial-gradient PNGs into `Assets/Sky/Textures/`. Self-contained; any PNG can be replaced with
  the user's own art later.
- **World-anchored, static:** since nothing scrolls, the star mesh and nebula sprites never move
  and **may be marked static** for best-case batching. (This reverses the "don't mark static"
  advice that only applied to a scrolling sky.)

## Architecture

All assets live under `Assets/Sky/`. Layering is by **sorting layer + order**, not Z depth. A new
`Background` sorting layer is added behind all gameplay sorting layers. Layers back→front:

1. **Nebula** — one or more large world sprites (`nebula_cloud`), very low alpha (~0.12), unlit,
   static. Placed to cover the map with margin.
2. **Sparse starfield** — a single procedural `Mesh` of camera-facing quads + one material
   (`star_dot`), ~200–300 stars, dim cool-white, static.
3. **Constellations** — hand-placed prefab instances at fixed world locations. Each is a few node
   sprites (`node_glow`, warm additive) + one connecting line sprite sharing one material, plus a
   `ConstellationPulse` component.

There is a `SkyRoot` empty GameObject in `Gameplay.unity` that parents the nebula, the starfield
mesh object, and the constellation instances, purely for scene organization (it does **not** move
and is **not** under the camera).

### Components

**`StarfieldGenerator.cs`** (runtime, on the starfield object)
- Builds one `Mesh` of quads + one `MeshRenderer`/`MeshFilter` (no per-star GameObjects).
- Parameters: `worldBounds` (a `Rect` the user sets to their map extents — **left as an Inspector
  field to fill in later**), `starCount` (default 300), `seed`, `minSize`, `maxSize`,
  `maxBrightness`.
- Deterministic from `seed`. Regenerates via `[ContextMenu("Regenerate")]` and on `Awake` if the
  mesh is missing. Stars scattered uniformly within `worldBounds`.

**`ConstellationPulse.cs`** (runtime, on each constellation)
- Sine-modulates `SpriteRenderer.color.a` and local scale: `Mathf.Sin(Time.time * frequency +
  phase)`. Defaults: `frequency ≈ 0.9`, `amplitude ≈ 0.12`. Applies to all child
  `SpriteRenderer`s. Runs only on the few constellation objects.

**`SkyTextureBaker.cs`** (editor utility)
- Menu item (`Tools/Sky/Bake Textures`) that writes soft radial-gradient PNGs into
  `Assets/Sky/Textures/`: `star_dot`, `node_glow`, `nebula_cloud`. Idempotent; safe to re-run.
  Sets import settings (Sprite, correct PPU, no compression artifacts).

**`ConstellationPlacerEditor.cs`** (editor tool)
- Custom editor / EditorWindow: click in the Scene view to add node positions to the selected
  constellation, optional grid snap, auto-generates/updates the connecting line renderer between
  nodes, and a **Save as Prefab** action. No render-to-texture baking.

### Materials & atlas

- `Assets/Sky/Materials/`: `SkyAdditive.mat` (Sprites/Default, Blend One One) for stars, nodes,
  lines; `SkyAlpha.mat` (Sprites/Default, alpha blend) for the nebula.
- `Assets/Sky/Sky.spriteatlas` packs `star_dot`, `node_glow`, `nebula_cloud` (+ line if a texture
  is used) so shared-material layers stay in few SRP batches.

## Data Flow

Fully local, no events required at runtime. `StarfieldGenerator` builds its mesh once (edit time
or `Awake`). `ConstellationPulse` self-animates in `Update`. Nothing reads networked state.

## Testing

EditMode tests (pure logic only, no scene/Unity-editor dependency):

- `StarfieldGenerator`: given `worldBounds`, `starCount`, `seed` → mesh has `starCount * 4`
  vertices, all within bounds, deterministic across two runs with the same seed.
- `ConstellationPulse`: the pulse curve stays within `[base - amp, base + amp]` and is periodic at
  the configured frequency (test the pure math via an extracted helper).

Scene wiring (sorting layer, SkyRoot placement, dropping constellation instances, running the
texture baker) is delivered as prefabs + a **README in-editor checklist**, because the
implementation cannot open the Unity editor to verify rendering while the project is locked.

## Deliverables

```
Assets/Sky/
  Scripts/
    StarfieldGenerator.cs
    ConstellationPulse.cs
    Editor/
      SkyTextureBaker.cs
      ConstellationPlacerEditor.cs
  Textures/            (baked PNGs, generated via menu item)
  Materials/           (SkyAdditive.mat, SkyAlpha.mat)
  Prefabs/             (SkyRoot, 3 example constellation prefabs)
  Sky.spriteatlas
  README.md            (tweak guide + in-editor wiring/verify checklist)
Assets/Sky/Tests/      (EditMode asmdef + tests)
```

## Performance Notes

- One mesh + one material for the whole starfield → single batch.
- Constellations share one additive material and are atlased → few batches.
- Static (non-moving) layers are eligible for static batching.
- Verification target: the sky adds only a handful of SRP batches in the Frame Debugger.
- Optional URP **Bloom** via a Global Volume + Bloom override (HDR already on); keep intensity low.
