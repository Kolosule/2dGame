# Sky Background

Client-local, world-anchored 2D sky: a sparse star mesh, a dim nebula, and hand-placed pulsing
constellations pinned to fixed map locations. No parallax, no networking, no custom shaders.

## First-time setup (in the Unity editor)

1. **Create the sorting layer:** Project Settings > Tags and Layers > Sorting Layers. Add
   `Background` and drag it ABOVE `Default` so the sky renders behind gameplay.
2. Open `Assets/Scenes/Gameplay.unity`.
3. Run **Tools > Sky > 1. Bake Textures** (bakes star/node/nebula PNGs into `Assets/Sky/Textures`).
4. Run **Tools > Sky > 2. Build Sky In Open Scene**. This creates `SkyRoot` (Nebula + Starfield +
   3 example constellations) and saves the constellation prefabs to `Assets/Sky/Prefabs`.
5. Select `SkyRoot/Starfield`, set **World Bounds** on the StarfieldGenerator to your map extents
   (x,y = bottom-left; width,height = size; add ~10 units margin for camera zoom-out), then
   right-click the component header > **Regenerate**.
6. Save the scene. Commit the newly-generated `.meta` files.

## Add your own constellation

- **Tools > Sky > Constellation Placer** > Start Placing > Ctrl+Click nodes in the Scene view >
  Build Constellation > Save Prefab. Drag the prefab wherever you want it on the map.
- Or duplicate an example prefab under `Assets/Sky/Prefabs` and move its nodes.

## Tweak the look

- **Star density / size / brightness:** `StarfieldGenerator` (starCount, min/maxSize, maxBrightness,
  seed). Re-run Regenerate after changes.
- **Pulse speed / amount:** `ConstellationPulse` (frequency, alphaAmplitude, scaleAmplitude) on each
  constellation.
- **Nebula strength:** the `Nebula` SpriteRenderer color alpha (keep it ~0.08–0.18).
- **Colors:** star tint on StarfieldGenerator; node/line colors are set per constellation.

## Performance

- The starfield is one mesh + one material (one batch). Constellations share `SkyAdditive.mat`.
- Layers never move, so mark `SkyRoot` children **Static** (Batching) if you want static batching.
- Optional bloom: add a Global Volume with a Bloom override (camera HDR is already on); low intensity.
- Verify in the Frame Debugger that the sky adds only a handful of SRP batches.
- If the additive material renders black in a build, add `Legacy Shaders/Particles/Additive` to
  Project Settings > Graphics > Always Included Shaders.
- The starfield mesh is generated at edit time / on Play. After a scene reopen or domain reload in
  the editor it may appear empty until you right-click the StarfieldGenerator > Regenerate (or
  enter Play).

## Guarantees

Everything here is cosmetic and local to each client. No `NetworkObject`, no `[Networked]` state,
no Fusion code. Safe to change freely without affecting multiplayer.
