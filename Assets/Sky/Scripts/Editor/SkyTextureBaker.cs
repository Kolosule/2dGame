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
        ImportAsSprite(star, 128);
        ImportAsSprite(node, 128);
        ImportAsSprite(nebula, 32);
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

    /// <summary>Imports the PNG at <paramref name="path"/> as a sprite with the given pixels-per-unit.</summary>
    private static void ImportAsSprite(string path, int ppu)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        if (imp == null) return;
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.spritePixelsPerUnit = ppu;
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.SaveAndReimport();
    }
}
