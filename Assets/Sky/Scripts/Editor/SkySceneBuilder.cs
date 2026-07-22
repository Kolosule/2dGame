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
        Material stars    = GetOrCreateMaterial("SkyStars",   "Legacy Shaders/Particles/Additive");
        if (additive == null || alpha == null || stars == null)
        {
            Debug.LogError("[Sky] Required shader(s) missing; aborting Build.");
            return;
        }
        var starTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sky/Textures/star_dot.png");
        if (starTex != null)
        {
            stars.mainTexture = starTex;
            // Persist the texture reference so SkyStars.mat keeps it after a project reopen,
            // not just for this authoring session.
            EditorUtility.SetDirty(stars);
            AssetDatabase.SaveAssets();
        }

        var existing = GameObject.Find("SkyRoot");
        if (existing != null) Object.DestroyImmediate(existing);

        var root = new GameObject("SkyRoot");

        // Nebula: one large, very dim alpha sprite behind everything.
        var nebula = new GameObject("Nebula");
        nebula.transform.SetParent(root.transform, false);
        var nsr = nebula.AddComponent<SpriteRenderer>();
        nsr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sky/Textures/nebula_cloud.png");
        nsr.sharedMaterial = alpha;
        nsr.color = new Color(0.16f, 0.13f, 0.22f, 0.1f);
        nsr.sortingLayerName = SortingLayerNameOrDefault();
        nsr.sortingOrder = 0;
        nebula.transform.localScale = Vector3.one * 40f;

        // Starfield: single mesh, additive material.
        var star = new GameObject("Starfield", typeof(MeshFilter), typeof(MeshRenderer));
        star.transform.SetParent(root.transform, false);
        var smr = star.GetComponent<MeshRenderer>();
        smr.sharedMaterial = stars;
        smr.sortingLayerName = SortingLayerNameOrDefault();
        smr.sortingOrder = 5;
        var gen = star.AddComponent<StarfieldGenerator>();
        gen.Rebuild();

        // Three example constellations, saved as prefabs and instanced under SkyRoot.
        Sprite node = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sky/Textures/node_glow.png");
        Directory.CreateDirectory(PrefabDir);
        BuildExample(root, additive, node, "Triangle", new[]
            { new Vector2(-6, 6), new Vector2(-2, 9), new Vector2(-9, 10) });
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
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            r.sortingLayerName = SortingLayerNameOrDefault();
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
