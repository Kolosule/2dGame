# 2D Sky Background Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a client-local, world-anchored 2D sky (sparse star mesh + hand-placed pulsing constellations) to the Gameplay arena at minimal CPU/GPU cost.

**Architecture:** Engine-free math (`Game.Sky.Core`: `StarfieldMath`, `PulseMath`) is TDD'd in EditMode. Thin MonoBehaviours (`StarfieldGenerator`, `ConstellationPulse`) consume that math and build a single star mesh / animate constellations. Editor tooling (`SkyTextureBaker`, `SkySceneBuilder`, `ConstellationPlacerEditor`) bakes procedural textures, assembles the scene, and lets the user place constellations — all via menu items, so no fragile hand-authored `.prefab`/`.mat`/`.spriteatlas` YAML is required. Nothing scrolls, nothing is networked, nothing is parented to the camera.

**Tech Stack:** Unity 6000.3.0f1, URP 17.3 (2D Renderer), C#, NUnit EditMode tests, Photon Fusion 2 (untouched).

## Global Constraints

- Unity **6000.3.0f1**, URP **17.3**, 2D Renderer (`Assets/Settings/Renderer2D.asset`). Copy no other pipeline assumptions.
- The sky is **cosmetic and client-local**: NO `NetworkObject`, NO `[Networked]`, NO Fusion `using`/callbacks, and it is NEVER parented under a networked object or the camera.
- **No parallax / no camera-relative movement.** World-anchored, static scenery.
- **No custom HLSL / Shader Graph.** Glow via stock additive sprite material; pulse via C#.
- Engine-free logic goes in a `*.Core` asmdef with `"noEngineReferences": true` — it may use only `System.*` (no `UnityEngine` types like `Vector2`/`Rect`/`Mathf`). Mirror the existing `Game.Hud.Core` / `Game.PlayerMovement.Core` pattern.
- Do NOT modify the camera, gameplay, existing scripts, or the two already-dirty working files (`DefaultPlayerStats.asset`, `PlayerPrefab.prefab`).
- `.meta` files: Unity generates these on next editor focus. Commit code without metas is fine on this feature branch; the README checklist covers committing generated metas after the user's first Unity session. Do NOT hand-author metas.
- Work happens on branch `feat/sky-background` (already created).

## File Structure

```
Assets/Sky/
  Scripts/
    Core/
      Game.Sky.Core.asmdef        # engine-free, noEngineReferences:true, autoReferenced:true
      StarfieldMath.cs            # StarPoint struct + Generate(...)
      PulseMath.cs                # Multiplier(...)
    StarfieldGenerator.cs         # MonoBehaviour (default assembly): builds star mesh
    ConstellationPulse.cs         # MonoBehaviour (default assembly): sine glow/scale
    Editor/                       # compiles into Assembly-CSharp-Editor (folder named Editor)
      SkyTextureBaker.cs          # menu: bakes star_dot/node_glow/nebula_cloud PNGs
      SkySceneBuilder.cs          # menu: materials + SkyRoot + starfield + 3 example constellations
      ConstellationPlacerEditor.cs# Scene-view tool: place nodes, build line, save prefab
  README.md                       # tweak guide + in-editor wiring/verify checklist
Assets/Tests/EditMode/Sky/
  Game.Sky.Tests.asmdef           # references Game.Sky.Core
  StarfieldMathTests.cs
  PulseMathTests.cs
```

Generated at editor time by the menu items (not committed as authored YAML): `Assets/Sky/Textures/*.png`, `Assets/Sky/Materials/*.mat`, `Assets/Sky/Prefabs/*.prefab`.

---

### Task 1: Engine-free StarfieldMath + Core/Tests asmdefs

**Files:**
- Create: `Assets/Sky/Scripts/Core/Game.Sky.Core.asmdef`
- Create: `Assets/Sky/Scripts/Core/StarfieldMath.cs`
- Create: `Assets/Tests/EditMode/Sky/Game.Sky.Tests.asmdef`
- Test: `Assets/Tests/EditMode/Sky/StarfieldMathTests.cs`

**Interfaces:**
- Produces: `Game.Sky.Core.StarPoint { float X, Y, Size, Brightness }` and
  `Game.Sky.Core.StarfieldMath.Generate(float minX, float minY, float width, float height, int count, int seed, float minSize, float maxSize, float maxBrightness) -> StarPoint[]`.

- [ ] **Step 1: Create the two asmdefs**

`Assets/Sky/Scripts/Core/Game.Sky.Core.asmdef`:
```json
{
    "name": "Game.Sky.Core",
    "rootNamespace": "Game.Sky.Core",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
```

`Assets/Tests/EditMode/Sky/Game.Sky.Tests.asmdef`:
```json
{
    "name": "Game.Sky.Tests",
    "rootNamespace": "",
    "references": [
        "Game.Sky.Core",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [ "Editor" ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [ "nunit.framework.dll" ],
    "autoReferenced": false,
    "defineConstraints": [ "UNITY_INCLUDE_TESTS" ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Write the failing test**

`Assets/Tests/EditMode/Sky/StarfieldMathTests.cs`:
```csharp
using NUnit.Framework;
using Game.Sky.Core;

public class StarfieldMathTests
{
    [Test]
    public void Generate_Produces_Requested_Count()
    {
        StarPoint[] s = StarfieldMath.Generate(0f, 0f, 10f, 10f, 300, 1, 0.03f, 0.09f, 0.7f);
        Assert.AreEqual(300, s.Length);
    }

    [Test]
    public void Same_Seed_Is_Deterministic()
    {
        StarPoint[] a = StarfieldMath.Generate(-5f, -5f, 20f, 20f, 50, 42, 0.03f, 0.09f, 0.7f);
        StarPoint[] b = StarfieldMath.Generate(-5f, -5f, 20f, 20f, 50, 42, 0.03f, 0.09f, 0.7f);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.AreEqual(a[i].X, b[i].X, 1e-6f);
            Assert.AreEqual(a[i].Y, b[i].Y, 1e-6f);
            Assert.AreEqual(a[i].Size, b[i].Size, 1e-6f);
            Assert.AreEqual(a[i].Brightness, b[i].Brightness, 1e-6f);
        }
    }

    [Test]
    public void All_Stars_Inside_Bounds_And_Ranges()
    {
        float minX = -8f, minY = 3f, w = 40f, h = 12f;
        StarPoint[] s = StarfieldMath.Generate(minX, minY, w, h, 400, 7, 0.02f, 0.10f, 0.6f);
        foreach (StarPoint p in s)
        {
            Assert.GreaterOrEqual(p.X, minX);
            Assert.LessOrEqual(p.X, minX + w);
            Assert.GreaterOrEqual(p.Y, minY);
            Assert.LessOrEqual(p.Y, minY + h);
            Assert.GreaterOrEqual(p.Size, 0.02f - 1e-6f);
            Assert.LessOrEqual(p.Size, 0.10f + 1e-6f);
            Assert.GreaterOrEqual(p.Brightness, 0f);
            Assert.LessOrEqual(p.Brightness, 0.6f + 1e-6f);
        }
    }

    [Test]
    public void Negative_Count_Returns_Empty()
    {
        Assert.AreEqual(0, StarfieldMath.Generate(0f, 0f, 1f, 1f, -5, 1, 0.1f, 0.2f, 1f).Length);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run in Unity Test Runner (EditMode) or via the out-of-editor harness in Step 6.
Expected: FAIL — `StarfieldMath` / `StarPoint` do not exist.

- [ ] **Step 4: Write minimal implementation**

`Assets/Sky/Scripts/Core/StarfieldMath.cs`:
```csharp
namespace Game.Sky.Core
{
    /// <summary>A single generated star in world space. Engine-free for EditMode tests.</summary>
    public struct StarPoint
    {
        public float X;
        public float Y;
        public float Size;       // world-unit edge length of the star quad
        public float Brightness; // 0..maxBrightness, applied as vertex alpha
    }

    /// <summary>
    /// Deterministic sparse-starfield generator. Pure math (no UnityEngine) so it runs in plain
    /// EditMode tests. Scatters <paramref name="count"/> stars uniformly inside the rectangle
    /// (minX, minY, width, height); size and brightness are seeded-random within their ranges.
    /// </summary>
    public static class StarfieldMath
    {
        public static StarPoint[] Generate(
            float minX, float minY, float width, float height,
            int count, int seed, float minSize, float maxSize, float maxBrightness)
        {
            if (count < 0) count = 0;
            if (width < 0f) width = 0f;
            if (height < 0f) height = 0f;
            if (maxSize < minSize) maxSize = minSize;
            if (maxBrightness < 0f) maxBrightness = 0f;

            var stars = new StarPoint[count];
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float fx = (float)rng.NextDouble();
                float fy = (float)rng.NextDouble();
                float fs = (float)rng.NextDouble();
                float fb = (float)rng.NextDouble();
                stars[i] = new StarPoint
                {
                    X = minX + fx * width,
                    Y = minY + fy * height,
                    Size = minSize + fs * (maxSize - minSize),
                    Brightness = fb * maxBrightness,
                };
            }
            return stars;
        }
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Expected: 4 tests PASS.

- [ ] **Step 6: (Editor-locked fallback) prove green out-of-editor**

Per `[[unity-locked-verification-workaround]]`: compile `StarfieldMath.cs` + a hand-written assert harness translating each `[Test]` above (float tol 1e-4) to an exe referencing only the netstandard ref, drop a `net8.0` runtimeconfig.json beside it, and run on `<Editor>\Data\NetCoreRuntime\dotnet.exe`. Expected: `PASS` count 4, `FAIL` count 0. Use the scratchpad dir for the throwaway harness.

- [ ] **Step 7: Commit**

```bash
git add Assets/Sky/Scripts/Core Assets/Tests/EditMode/Sky
git commit -m "feat(sky): deterministic engine-free StarfieldMath + tests"
```

---

### Task 2: Engine-free PulseMath

**Files:**
- Create: `Assets/Sky/Scripts/Core/PulseMath.cs`
- Test: `Assets/Tests/EditMode/Sky/PulseMathTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Game.Sky.Core.PulseMath.Multiplier(float time, float frequency, float amplitude, float phase) -> float`, oscillating in `[1 - amplitude, 1 + amplitude]`.

- [ ] **Step 1: Write the failing test**

`Assets/Tests/EditMode/Sky/PulseMathTests.cs`:
```csharp
using NUnit.Framework;
using Game.Sky.Core;

public class PulseMathTests
{
    [Test]
    public void Stays_Within_Amplitude_Band()
    {
        for (float t = 0f; t < 20f; t += 0.13f)
        {
            float m = PulseMath.Multiplier(t, 0.9f, 0.12f, 0f);
            Assert.GreaterOrEqual(m, 1f - 0.12f - 1e-4f);
            Assert.LessOrEqual(m, 1f + 0.12f + 1e-4f);
        }
    }

    [Test]
    public void Zero_Amplitude_Is_Flat_One()
    {
        Assert.AreEqual(1f, PulseMath.Multiplier(3.3f, 0.9f, 0f, 0f), 1e-6f);
    }

    [Test]
    public void Phase_Shifts_The_Curve()
    {
        float a = PulseMath.Multiplier(0f, 1f, 0.2f, 0f);
        float b = PulseMath.Multiplier(0f, 1f, 0.2f, 1.5707963f); // +pi/2
        Assert.AreEqual(1f, a, 1e-4f);            // sin(0) = 0
        Assert.AreEqual(1.2f, b, 1e-4f);          // sin(pi/2) = 1
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Expected: FAIL — `PulseMath` does not exist.

- [ ] **Step 3: Write minimal implementation**

`Assets/Sky/Scripts/Core/PulseMath.cs`:
```csharp
namespace Game.Sky.Core
{
    /// <summary>
    /// Pulse curve for constellation glow/scale. Returns a multiplier oscillating in
    /// [1 - amplitude, 1 + amplitude]. <paramref name="frequency"/> is the raw radian rate
    /// (matches sin(time * frequency + phase)); ~0.9 gives a slow, calm pulse. Engine-free.
    /// </summary>
    public static class PulseMath
    {
        public static float Multiplier(float time, float frequency, float amplitude, float phase)
        {
            return 1f + amplitude * (float)System.Math.Sin(time * frequency + phase);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Expected: 3 tests PASS (and re-run Task 1's suite: 7 total green). Editor-locked: extend the Step-6 harness with these cases.

- [ ] **Step 5: Commit**

```bash
git add Assets/Sky/Scripts/Core/PulseMath.cs Assets/Tests/EditMode/Sky/PulseMathTests.cs
git commit -m "feat(sky): engine-free PulseMath + tests"
```

---

### Task 3: StarfieldGenerator MonoBehaviour

**Files:**
- Create: `Assets/Sky/Scripts/StarfieldGenerator.cs`

**Interfaces:**
- Consumes: `Game.Sky.Core.StarfieldMath.Generate(...)`, `Game.Sky.Core.StarPoint`.
- Produces: `public void Rebuild()`; serialized `Rect worldBounds` (the map-bounds field the user fills in later). Component requires `MeshFilter` + `MeshRenderer`.

- [ ] **Step 1: Write the component**

`Assets/Sky/Scripts/StarfieldGenerator.cs`:
```csharp
using UnityEngine;
using Game.Sky.Core;

/// <summary>
/// Builds the sparse starfield as ONE mesh of camera-facing quads (no per-star GameObjects).
/// World-anchored and static: it never moves and is not parented to the camera. Assign an
/// additive material (see SkySceneBuilder) so vertex-color brightness reads as glow.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class StarfieldGenerator : MonoBehaviour
{
    [Header("Coverage")]
    [Tooltip("World-space rectangle the stars are scattered across. SET THIS to your map bounds " +
             "(x,y = bottom-left corner; width,height = size). Add margin for camera zoom-out.")]
    [SerializeField] private Rect worldBounds = new Rect(-50f, -50f, 100f, 100f);

    [Header("Density")]
    [Tooltip("Total number of stars. Keep low (200-400) for a sparse, cheap field.")]
    [SerializeField] private int starCount = 300;
    [Tooltip("Change to reshuffle the star layout.")]
    [SerializeField] private int seed = 12345;

    [Header("Appearance")]
    [SerializeField] private float minSize = 0.03f;
    [SerializeField] private float maxSize = 0.09f;
    [Tooltip("Upper bound on per-star alpha. Keep dim so stars don't compete with gameplay.")]
    [SerializeField, Range(0f, 1f)] private float maxBrightness = 0.7f;
    [Tooltip("Cool-white star tint.")]
    [SerializeField] private Color starColor = new Color(0.80f, 0.85f, 1.0f, 1f);

    private void Awake()
    {
        if (GetComponent<MeshFilter>().sharedMesh == null) Rebuild();
    }

    /// <summary>Regenerates the star mesh from the current inspector values.</summary>
    [ContextMenu("Regenerate")]
    public void Rebuild()
    {
        StarPoint[] stars = StarfieldMath.Generate(
            worldBounds.xMin, worldBounds.yMin, worldBounds.width, worldBounds.height,
            starCount, seed, minSize, maxSize, maxBrightness);

        int n = stars.Length;
        var verts = new Vector3[n * 4];
        var cols  = new Color[n * 4];
        var uvs   = new Vector2[n * 4];
        var tris  = new int[n * 6];

        for (int i = 0; i < n; i++)
        {
            StarPoint s = stars[i];
            float h = s.Size * 0.5f;
            int v = i * 4;
            verts[v + 0] = new Vector3(s.X - h, s.Y - h, 0f);
            verts[v + 1] = new Vector3(s.X - h, s.Y + h, 0f);
            verts[v + 2] = new Vector3(s.X + h, s.Y + h, 0f);
            verts[v + 3] = new Vector3(s.X + h, s.Y - h, 0f);

            Color c = starColor; c.a = s.Brightness;
            cols[v + 0] = cols[v + 1] = cols[v + 2] = cols[v + 3] = c;

            uvs[v + 0] = new Vector2(0f, 0f);
            uvs[v + 1] = new Vector2(0f, 1f);
            uvs[v + 2] = new Vector2(1f, 1f);
            uvs[v + 3] = new Vector2(1f, 0f);

            int t = i * 6;
            tris[t + 0] = v + 0; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
            tris[t + 3] = v + 0; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
        }

        var mesh = new Mesh { name = "Starfield" };
        mesh.indexFormat = (n * 4 > 65000)
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = verts;
        mesh.colors = cols;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;
    }
}
```

- [ ] **Step 2: Compile-verify**

Preferred: open Unity — the Console must show no compile errors (the user opens Unity anyway to run the menu items in Task 7). Editor-locked alternative: run the whole-surface Roslyn compile gate from `[[unity-locked-verification-workaround]]`, adding `Assets/Sky/Scripts/Core` to the inline-core folders (its `Library/ScriptAssemblies/Game.Sky.Core.dll` doesn't exist yet). Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Sky/Scripts/StarfieldGenerator.cs
git commit -m "feat(sky): StarfieldGenerator builds single star mesh"
```

---

### Task 4: ConstellationPulse MonoBehaviour

**Files:**
- Create: `Assets/Sky/Scripts/ConstellationPulse.cs`

**Interfaces:**
- Consumes: `Game.Sky.Core.PulseMath.Multiplier(...)`.
- Produces: a component that, in `Update`, scales this transform and fades all child `SpriteRenderer` alphas around their authored baseline.

- [ ] **Step 1: Write the component**

`Assets/Sky/Scripts/ConstellationPulse.cs`:
```csharp
using UnityEngine;
using Game.Sky.Core;

/// <summary>
/// Softly pulses a constellation's brightness and scale with a sine wave. Local cosmetic only —
/// no networking. Modulates every child SpriteRenderer's alpha around its authored value and
/// this transform's local scale. Attach to a constellation root that has node/line SpriteRenderers
/// as children.
/// </summary>
public class ConstellationPulse : MonoBehaviour
{
    [Tooltip("Raw radian pulse rate; ~0.9 is a slow, calm shimmer.")]
    [SerializeField] private float frequency = 0.9f;
    [Tooltip("Alpha swing as a fraction of the authored alpha (0.12 = +/-12%).")]
    [SerializeField, Range(0f, 0.5f)] private float alphaAmplitude = 0.12f;
    [Tooltip("Scale swing as a fraction of the authored scale.")]
    [SerializeField, Range(0f, 0.5f)] private float scaleAmplitude = 0.06f;
    [Tooltip("Phase offset. Left at 0, a random phase is chosen so identical constellations " +
             "don't pulse in lockstep.")]
    [SerializeField] private float phase = 0f;

    private SpriteRenderer[] renderers;
    private float[] baseAlphas;
    private Vector3 baseScale;

    private void Awake()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        baseAlphas = new float[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) baseAlphas[i] = renderers[i].color.a;
        baseScale = transform.localScale;
        if (Mathf.Approximately(phase, 0f)) phase = Random.value * 2f * Mathf.PI;
    }

    private void Update()
    {
        float am = PulseMath.Multiplier(Time.time, frequency, alphaAmplitude, phase);
        for (int i = 0; i < renderers.Length; i++)
        {
            Color c = renderers[i].color;
            c.a = Mathf.Clamp01(baseAlphas[i] * am);
            renderers[i].color = c;
        }
        float sm = PulseMath.Multiplier(Time.time, frequency, scaleAmplitude, phase);
        transform.localScale = baseScale * sm;
    }
}
```

- [ ] **Step 2: Compile-verify** — same as Task 3 Step 2. Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Sky/Scripts/ConstellationPulse.cs
git commit -m "feat(sky): ConstellationPulse sine glow + scale"
```

---

### Task 5: SkyTextureBaker (procedural PNGs)

**Files:**
- Create: `Assets/Sky/Scripts/Editor/SkyTextureBaker.cs`

**Interfaces:**
- Produces: menu `Tools/Sky/1. Bake Textures`; writes `Assets/Sky/Textures/{star_dot,node_glow,nebula_cloud}.png` and imports them as Sprites. Public `static string[] BakeAll()` returning the written asset paths (so SkySceneBuilder can depend on it).

- [ ] **Step 1: Write the editor utility**

`Assets/Sky/Scripts/Editor/SkyTextureBaker.cs`:
```csharp
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes the sky's soft-gradient textures procedurally so the project needs no external art.
/// Re-runnable and idempotent. Replace any PNG in Assets/Sky/Textures with your own art later.
/// </summary>
public static class SkyTextureBaker
{
    private const string TexDir = "Assets/Sky/Textures";

    [MenuItem("Tools/Sky/1. Bake Textures")]
    public static void BakeMenu()
    {
        string[] paths = BakeAll();
        Debug.Log("[Sky] Baked textures:\n" + string.Join("\n", paths));
    }

    /// <summary>Bakes all sky textures and returns their asset paths.</summary>
    public static string[] BakeAll()
    {
        Directory.CreateDirectory(TexDir);
        string star   = WriteRadial("star_dot",     64,  2.2f, Color.white, 128);
        string node   = WriteRadial("node_glow",    128, 1.6f, Color.white, 128);
        string nebula = WriteRadial("nebula_cloud", 256, 1.1f, Color.white, 32);
        AssetDatabase.Refresh();
        foreach (string p in new[] { star, node, nebula }) ImportAsSprite(p);
        AssetDatabase.SaveAssets();
        return new[] { star, node, nebula };
    }

    /// <summary>
    /// Writes a white radial-gradient PNG: alpha = (1 - r)^falloff, clamped. Higher falloff =
    /// tighter/softer core. ppu becomes the sprite's pixels-per-unit on import.
    /// </summary>
    private static string WriteRadial(string name, int size, float falloff, Color rgb, int ppu)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) * 0.5f;
        float maxR = c;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - c) / maxR;
            float dy = (y - c) / maxR;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01(1f - r);
            a = Mathf.Pow(a, falloff);
            px[y * size + x] = new Color(rgb.r, rgb.g, rgb.b, a);
        }
        tex.SetPixels(px);
        tex.Apply();

        string path = $"{TexDir}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        return path;
    }

    private static void ImportAsSprite(string path)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        if (imp == null) return;
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.SaveAndReimport();
    }
}
```

- [ ] **Step 2: Compile-verify** — editor scripts compile into Assembly-CSharp-Editor. Open Unity → no Console errors, then run `Tools/Sky/1. Bake Textures` and confirm 3 PNGs appear under `Assets/Sky/Textures`. (Editor-locked: this task cannot be functionally verified without Unity; compile gate only.)

- [ ] **Step 3: Commit**

```bash
git add Assets/Sky/Scripts/Editor/SkyTextureBaker.cs
git commit -m "feat(sky): SkyTextureBaker bakes soft-gradient star/node/nebula PNGs"
```

---

### Task 6: ConstellationPlacerEditor (Scene-view placement tool)

**Files:**
- Create: `Assets/Sky/Scripts/Editor/ConstellationPlacerEditor.cs`

**Interfaces:**
- Consumes: `node_glow` sprite (loaded from `Assets/Sky/Textures/node_glow.png`), the additive material named `Assets/Sky/Materials/SkyAdditive.mat` (created in Task 7; the tool loads it if present, else uses the sprite default), `ConstellationPulse`.
- Produces: menu `Tools/Sky/Constellation Placer`; public `static GameObject CreateConstellation(string name, Vector2[] nodePositions, Material additive, Sprite nodeSprite)` used by both the window and SkySceneBuilder (Task 7).

- [ ] **Step 1: Write the tool**

`Assets/Sky/Scripts/Editor/ConstellationPlacerEditor.cs`:
```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EditorWindow for placing constellation nodes by clicking in the Scene view. Builds a root with
/// node SpriteRenderers + a LineRenderer connecting them in order + a ConstellationPulse, and can
/// save the result as a prefab. Purely an authoring aid — produces normal cosmetic GameObjects.
/// </summary>
public class ConstellationPlacerEditor : EditorWindow
{
    private string constellationName = "Constellation";
    private GameObject current;
    private readonly List<Vector2> pending = new List<Vector2>();
    private bool placing;

    [MenuItem("Tools/Sky/Constellation Placer")]
    public static void Open() => GetWindow<ConstellationPlacerEditor>("Constellation Placer");

    private void OnEnable()  => SceneView.duringSceneGui += OnScene;
    private void OnDisable() => SceneView.duringSceneGui -= OnScene;

    private void OnGUI()
    {
        constellationName = EditorGUILayout.TextField("Name", constellationName);
        EditorGUILayout.HelpBox(
            "1. Click 'Start Placing'.\n2. Ctrl+Click in the Scene view to drop nodes.\n" +
            "3. Click 'Build' to create the constellation, then 'Save Prefab'.", MessageType.Info);

        if (!placing && GUILayout.Button("Start Placing")) { pending.Clear(); placing = true; }
        if (placing && GUILayout.Button("Stop Placing"))   { placing = false; }
        EditorGUILayout.LabelField($"Nodes queued: {pending.Count}");

        using (new EditorGUI.DisabledScope(pending.Count < 2))
        {
            if (GUILayout.Button("Build Constellation"))
            {
                Material add = AssetDatabase.LoadAssetAtPath<Material>("Assets/Sky/Materials/SkyAdditive.mat");
                Sprite node  = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sky/Textures/node_glow.png");
                current = CreateConstellation(constellationName, pending.ToArray(), add, node);
                Selection.activeGameObject = current;
                placing = false;
            }
        }

        using (new EditorGUI.DisabledScope(current == null))
        {
            if (GUILayout.Button("Save Prefab"))
            {
                System.IO.Directory.CreateDirectory("Assets/Sky/Prefabs");
                string path = $"Assets/Sky/Prefabs/{current.name}.prefab";
                PrefabUtility.SaveAsPrefabAssetAndConnect(current, path, InteractionMode.UserAction);
                Debug.Log($"[Sky] Saved {path}");
            }
        }
    }

    private void OnScene(SceneView view)
    {
        if (!placing) return;
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && e.control)
        {
            Ray r = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Vector3 p = r.origin; p.z = 0f;
            pending.Add(new Vector2(p.x, p.y));
            e.Use();
            Repaint();
        }
        Handles.color = Color.cyan;
        for (int i = 0; i < pending.Count; i++)
        {
            Handles.DotHandleCap(0, pending[i], Quaternion.identity, 0.1f, EventType.Repaint);
            if (i > 0) Handles.DrawLine(pending[i - 1], pending[i]);
        }
    }

    /// <summary>
    /// Builds a constellation GameObject: one child SpriteRenderer per node + a LineRenderer through
    /// them + a ConstellationPulse. Used by the window and by SkySceneBuilder. Sorting layer is set
    /// to "Background"; caller may re-order.
    /// </summary>
    public static GameObject CreateConstellation(string name, Vector2[] nodePositions,
                                                 Material additive, Sprite nodeSprite)
    {
        var root = new GameObject(name);
        Vector2 centroid = Vector2.zero;
        foreach (Vector2 p in nodePositions) centroid += p;
        centroid /= Mathf.Max(1, nodePositions.Length);
        root.transform.position = centroid;

        // Line through nodes (drawn behind the nodes).
        var lineGo = new GameObject("Line");
        lineGo.transform.SetParent(root.transform, false);
        var line = lineGo.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = nodePositions.Length;
        line.widthMultiplier = 0.06f;
        line.numCapVertices = 2;
        line.sortingLayerName = "Background";
        line.sortingOrder = 10;
        if (additive != null) line.material = additive;
        var warm = new Color(1f, 0.85f, 0.55f, 0.35f);
        line.startColor = line.endColor = warm;
        for (int i = 0; i < nodePositions.Length; i++)
            line.SetPosition(i, (Vector3)(nodePositions[i] - centroid));

        // Nodes (drawn in front of the line).
        for (int i = 0; i < nodePositions.Length; i++)
        {
            var n = new GameObject($"Node{i}");
            n.transform.SetParent(root.transform, false);
            n.transform.localPosition = (Vector3)(nodePositions[i] - centroid);
            n.transform.localScale = Vector3.one * 0.5f;
            var sr = n.AddComponent<SpriteRenderer>();
            sr.sprite = nodeSprite;
            if (additive != null) sr.sharedMaterial = additive;
            sr.color = new Color(1f, 0.87f, 0.6f, 0.9f); // warm gold
            sr.sortingLayerName = "Background";
            sr.sortingOrder = 11;
        }

        root.AddComponent<ConstellationPulse>();
        return root;
    }
}
```

- [ ] **Step 2: Compile-verify** — compile gate only (needs Unity to exercise). Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Sky/Scripts/Editor/ConstellationPlacerEditor.cs
git commit -m "feat(sky): Scene-view constellation placer + prefab save"
```

---

### Task 7: SkySceneBuilder (one-click scene assembly + example prefabs)

**Files:**
- Create: `Assets/Sky/Scripts/Editor/SkySceneBuilder.cs`

**Interfaces:**
- Consumes: `SkyTextureBaker.BakeAll()`, `ConstellationPlacerEditor.CreateConstellation(...)`, `StarfieldGenerator`.
- Produces: menu `Tools/Sky/2. Build Sky In Open Scene`; creates the `Background` sorting layer if missing (logs a manual step if it can't), materials under `Assets/Sky/Materials`, a `SkyRoot` with nebula + starfield, and 3 example constellation prefabs under `Assets/Sky/Prefabs`.

- [ ] **Step 1: Write the builder**

`Assets/Sky/Scripts/Editor/SkySceneBuilder.cs`:
```csharp
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click assembly of the sky into the currently open scene. Bakes textures (if missing),
/// creates additive/alpha materials, builds a SkyRoot (nebula + starfield), and drops three
/// example constellations, saving each as a prefab. Everything it creates is cosmetic and NOT
/// networked. Re-runnable: it removes a prior SkyRoot first.
/// </summary>
public static class SkySceneBuilder
{
    private const string MatDir = "Assets/Sky/Materials";
    private const string PrefabDir = "Assets/Sky/Prefabs";

    [MenuItem("Tools/Sky/2. Build Sky In Open Scene")]
    public static void Build()
    {
        SkyTextureBaker.BakeAll();
        WarnIfNoBackgroundLayer();

        Material additive = GetOrCreateMaterial("SkyAdditive", "Legacy Shaders/Particles/Additive");
        Material alpha    = GetOrCreateMaterial("SkyAlpha",    "Sprites/Default");

        var existing = GameObject.Find("SkyRoot");
        if (existing != null) Object.DestroyImmediate(existing);

        var root = new GameObject("SkyRoot");

        // Nebula: one large, very dim alpha sprite behind everything.
        var nebula = new GameObject("Nebula");
        nebula.transform.SetParent(root.transform, false);
        var nsr = nebula.AddComponent<SpriteRenderer>();
        nsr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sky/Textures/nebula_cloud.png");
        nsr.sharedMaterial = alpha;
        nsr.color = new Color(0.4f, 0.5f, 0.8f, 0.12f);
        nsr.sortingLayerName = SortingLayerNameOrDefault();
        nsr.sortingOrder = 0;
        nebula.transform.localScale = Vector3.one * 40f;

        // Starfield: single mesh, additive material.
        var star = new GameObject("Starfield", typeof(MeshFilter), typeof(MeshRenderer));
        star.transform.SetParent(root.transform, false);
        var smr = star.GetComponent<MeshRenderer>();
        smr.sharedMaterial = additive;
        smr.sortingLayerName = SortingLayerNameOrDefault();
        smr.sortingOrder = 5;
        var gen = star.AddComponent<StarfieldGenerator>();
        gen.Rebuild();

        // Three example constellations, saved as prefabs and instanced under SkyRoot.
        Sprite node = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sky/Textures/node_glow.png");
        Directory.CreateDirectory(PrefabDir);
        BuildExample(root, additive, node, "Triangle", new[]
            { new Vector2(-6, 6), new Vector2(-2, 9), new Vector2(-9, 10), new Vector2(-6, 6) });
        BuildExample(root, additive, node, "Dipper", new[]
            { new Vector2(8, 4), new Vector2(10, 5), new Vector2(12, 4.5f), new Vector2(13, 6),
              new Vector2(13, 8), new Vector2(11, 9) });
        BuildExample(root, additive, node, "Cross", new[]
            { new Vector2(2, -8), new Vector2(2, -3), new Vector2(0, -5.5f), new Vector2(4, -5.5f) });

        Selection.activeGameObject = root;
        Debug.Log("[Sky] Sky built. Set the Starfield's 'World Bounds' to your map extents, then " +
                  "right-click the StarfieldGenerator > Regenerate.");
    }

    private static void BuildExample(GameObject root, Material add, Sprite node,
                                     string name, Vector2[] pts)
    {
        GameObject c = ConstellationPlacerEditor.CreateConstellation(name, pts, add, node);
        string path = $"{PrefabDir}/{name}.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(c, path);
        Object.DestroyImmediate(c);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.transform.SetParent(root.transform, true);
    }

    private static Material GetOrCreateMaterial(string name, string shader)
    {
        Directory.CreateDirectory(MatDir);
        string path = $"{MatDir}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var sh = Shader.Find(shader);
        if (sh == null) { Debug.LogError($"[Sky] Shader not found: {shader}"); return null; }
        var mat = new Material(sh) { name = name };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static bool HasBackgroundLayer()
    {
        foreach (var l in SortingLayer.layers) if (l.name == "Background") return true;
        return false;
    }

    private static string SortingLayerNameOrDefault() => HasBackgroundLayer() ? "Background" : "Default";

    private static void WarnIfNoBackgroundLayer()
    {
        if (!HasBackgroundLayer())
            Debug.LogWarning("[Sky] No 'Background' sorting layer found. Create it " +
                "(Project Settings > Tags and Layers > Sorting Layers), place it ABOVE 'Default', " +
                "then re-run so sky layers sit behind gameplay.");
    }
}
```

- [ ] **Step 2: Compile-verify + (in Unity) smoke test** — compile gate must pass. When the user opens Unity: run `Tools/Sky/2. Build Sky In Open Scene` in Gameplay.unity; confirm SkyRoot with Nebula, Starfield, and 3 constellations appears, and 3 prefabs exist under `Assets/Sky/Prefabs`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Sky/Scripts/Editor/SkySceneBuilder.cs
git commit -m "feat(sky): one-click SkySceneBuilder + example constellation prefabs"
```

---

### Task 8: README, wiring checklist, and final gate

**Files:**
- Create: `Assets/Sky/README.md`

**Interfaces:** none (docs).

- [ ] **Step 1: Write the README**

`Assets/Sky/README.md` (complete content):
```markdown
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

## Guarantees

Everything here is cosmetic and local to each client. No `NetworkObject`, no `[Networked]` state,
no Fusion code. Safe to change freely without affecting multiplayer.
```

- [ ] **Step 2: Final compile gate + core test suite**

Open Unity: Console clean, Test Runner EditMode green (Task 1+2 = 7 tests). Editor-locked: run the whole-surface Roslyn compile gate (inline-core folder: `Assets/Sky/Scripts/Core`) → 0 errors, and the pure-math dotnet harness → 7 PASS / 0 FAIL.

- [ ] **Step 3: Commit**

```bash
git add Assets/Sky/README.md
git commit -m "docs(sky): README setup, tweak, and performance checklist"
```

---

## Self-Review

**Spec coverage:**
- Nebula / sparse star mesh / constellations / sorting layer → Tasks 3, 7 (SkySceneBuilder builds all three on `Background`). ✓
- Single star mesh, no per-star GameObjects → Task 3. ✓
- Hand-placed constellations pinned to world → Task 6 placer + Task 7 examples. ✓
- Additive glow + C# pulse → Tasks 4, 7 (`SkyAdditive.mat` + `ConstellationPulse`). ✓
- Procedurally-baked textures → Task 5. ✓
- `worldBounds` as an Inspector field to fill later → Task 3. ✓
- No parallax / no networking / no custom shader → enforced across all tasks + README guarantees. ✓
- EditMode tests for starfield + pulse math → Tasks 1, 2. ✓
- README tweak guide + in-editor checklist → Task 8. ✓
- Sprite atlas: intentionally deferred — the starfield is one mesh and constellations share one
  material, so the ≤-few-batches goal is met without an atlas. README notes batching. (Spec listed
  the atlas as an optimization aid, not a hard requirement; adding one is a trivial later step.)

**Placeholder scan:** No TBD/TODO; all code blocks complete. ✓

**Type consistency:** `StarfieldMath.Generate` signature, `StarPoint` fields, `PulseMath.Multiplier`
signature, and `ConstellationPlacerEditor.CreateConstellation(string, Vector2[], Material, Sprite)`
are used identically wherever referenced (Tasks 3, 4, 7). ✓
```
