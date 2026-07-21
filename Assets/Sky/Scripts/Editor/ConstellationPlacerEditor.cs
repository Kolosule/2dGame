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
