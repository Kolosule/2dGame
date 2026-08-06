using UnityEngine;
using Fusion;

/// <summary>
/// Marks the locally-controlled player so its owner can pick their own body out of a pile of
/// same-colored teammates (teammates now pass through each other via FriendlyCollision, so
/// overlapping stacks are common). Purely local: markerRoot is enabled ONLY on the client that
/// has input authority over this player, so no other peer ever sees it. No networked state, no
/// per-frame logic -- the enabled flag is set once in Spawned() and never revisited, including
/// across death/respawn (the player object is teleported on respawn, never despawned --
/// PlayerStatsHandler.Respawn -- so this component and its marker stay put throughout).
/// </summary>
public class LocalPlayerMarker : NetworkBehaviour
{
    [Tooltip("Pre-authored marker child (e.g. a chevron sprite). Leave unassigned to use the " +
             "code-generated fallback triangle -- see markerHeight/markerColor below.")]
    [SerializeField] private GameObject markerRoot;

    [Tooltip("Fallback-only: height above the player root the generated triangle is placed at. " +
             "Ignored if markerRoot is assigned -- that object's own position is authored instead.")]
    [SerializeField] private float markerHeight = 2.6f;

    [Tooltip("Fallback-only: color of the generated triangle. Ignored if markerRoot is assigned.")]
    [SerializeField] private Color markerColor = Color.white;

    public override void Spawned()
    {
        if (markerRoot == null)
        {
            markerRoot = BuildFallbackMarker(markerColor);
            markerRoot.transform.SetParent(transform, false);
            markerRoot.transform.localPosition = Vector3.up * markerHeight;
        }

        markerRoot.SetActive(HasInputAuthority);
    }

    /// <summary>Code-generated downward-pointing triangle, no art dependency. Mirrors
    /// CosmeticTracer's "no art needed" pattern.</summary>
    private static GameObject BuildFallbackMarker(Color color)
    {
        var go = new GameObject("LocalPlayerMarker_Fallback");
        var meshFilter = go.AddComponent<MeshFilter>();
        var meshRenderer = go.AddComponent<MeshRenderer>();

        var mesh = new Mesh();
        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.2f, 0.3f, 0f),
            new Vector3(0.2f, 0.3f, 0f),
            new Vector3(0f, 0f, 0f),
        };
        mesh.triangles = new int[] { 0, 1, 2 };
        mesh.colors = new Color[] { color, color, color };
        mesh.uv = new Vector2[] { Vector2.zero, Vector2.zero, Vector2.zero };
        mesh.RecalculateBounds();
        meshFilter.mesh = mesh;

        var material = new Material(Shader.Find("Sprites/Default"));
        material.color = color;
        meshRenderer.material = material;
        meshRenderer.sortingOrder = 100;

        return go;
    }
}
