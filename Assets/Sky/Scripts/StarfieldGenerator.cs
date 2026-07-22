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
