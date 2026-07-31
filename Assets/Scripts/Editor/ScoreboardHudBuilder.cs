using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-click builder for the ScoreboardPanel: a dim backdrop, two team columns (each a
/// VerticalLayoutGroup with a header), and one hidden row template per column with all
/// ScoreboardRowView fields wired. Wires ScoreboardPanel's serialized references via
/// SerializedObject. Safe to re-run (rebuilds only its own "ScoreboardContent" child).
/// Mirrors the MatchHudBuilder / EconomyHudBuilder editor-tool pattern.
/// </summary>
public static class ScoreboardHudBuilder
{
    private const string UndoLabel = "Build Scoreboard Panel";

    [MenuItem("Tools/Match/Build Scoreboard Panel")]
    public static void Build()
    {
        var scoreboard = Object.FindFirstObjectByType<ScoreboardPanel>(FindObjectsInactive.Include);
        if (scoreboard == null)
        {
            EditorUtility.DisplayDialog("Scoreboard HUD Builder",
                "No ScoreboardPanel found in the open scene.\n\nAdd the ScoreboardPanel component to " +
                "your HUD canvas first, then run this again.", "OK");
            return;
        }

        var canvas = scoreboard.GetComponentInParent<Canvas>(true);
        if (canvas == null) canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Scoreboard HUD Builder",
                "No Canvas found in the open scene to parent the ScoreboardPanel under.", "OK");
            return;
        }

        var so = new SerializedObject(scoreboard);
        var rootProp = so.FindProperty("panelRoot");
        Undo.RecordObject(scoreboard, UndoLabel);

        var panel = rootProp.objectReferenceValue as GameObject;
        if (panel == null)
        {
            panel = new GameObject("ScoreboardPanel", typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panel, UndoLabel);
            panel.transform.SetParent(canvas.transform, false);
        }

        var prt = panel.GetComponent<RectTransform>();
        if (prt == null) prt = Undo.AddComponent<RectTransform>(panel);
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero;
        prt.offsetMax = Vector2.zero;
        var backdrop = panel.GetComponent<Image>();
        if (backdrop == null) backdrop = Undo.AddComponent<Image>(panel);
        backdrop.color = new Color(0f, 0f, 0f, 0.75f);
        backdrop.raycastTarget = true;

        var old = panel.transform.Find("ScoreboardContent");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var content = new GameObject("ScoreboardContent", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        Undo.RegisterCreatedObjectUndo(content, UndoLabel);
        content.transform.SetParent(panel.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.5f, 0.5f);
        crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(1000f, 640f);
        crt.anchoredPosition = Vector2.zero;

        var contentLayout = content.GetComponent<HorizontalLayoutGroup>();
        contentLayout.spacing = 24f;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = true;

        Transform team1Container = MakeColumn("Team1Column", content.transform, "BLUE");
        Transform team2Container = MakeColumn("Team2Column", content.transform, "RED");

        // ONE template, shared by both columns: ScoreboardPanel.PaintTeam Instantiates it into
        // whichever container it is pooling for, so a second per-column template would be dead
        // weight. It lives under Team1Column and stays inactive (ScoreboardPanel.Awake hides it).
        ScoreboardRowView rowTemplate = MakeRowTemplate("RowTemplate", team1Container);

        rootProp.objectReferenceValue = panel;
        so.FindProperty("team1RowContainer").objectReferenceValue = team1Container;
        so.FindProperty("team2RowContainer").objectReferenceValue = team2Container;
        so.FindProperty("rowTemplate").objectReferenceValue = rowTemplate;
        so.ApplyModifiedProperties();

        panel.SetActive(true);
        Selection.activeGameObject = panel;
        EditorSceneManager.MarkSceneDirty(scoreboard.gameObject.scene);

        Debug.Log("[Match] ScoreboardPanel built and wired (two team columns + one row template). " +
                   "Save the scene (Ctrl+S). It auto-hides at runtime.");
    }

    private static Transform MakeColumn(string name, Transform parent, string headerLabel)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var layout = go.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var header = MakeText(name + "Header", go.transform, 28, Color.white, headerLabel);
        header.fontStyle = FontStyles.Bold;

        return go.transform;
    }

    private static ScoreboardRowView MakeRowTemplate(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childForceExpandWidth = false;

        var nameText = MakeText("NameText", go.transform, 20, Color.white, "PlayerName");
        var scoreText = MakeText("ScoreText", go.transform, 20, new Color(1f, 0.86f, 0.40f), "0");
        var kdText = MakeText("KdText", go.transform, 18, Color.white, "0/0");
        var capturesText = MakeText("CapturesText", go.transform, 18, Color.white, "0");
        var coinsText = MakeText("CoinsText", go.transform, 18, Color.white, "0");
        var carryText = MakeText("CarryTimeText", go.transform, 18, Color.white, "0:00");
        var returnsText = MakeText("ReturnsText", go.transform, 18, Color.white, "0");
        var deadIcon = MakeIcon("DeadIcon", go.transform, new Color(0.6f, 0.1f, 0.1f));
        var carryIcon = MakeIcon("CarryIcon", go.transform, new Color(0.9f, 0.8f, 0.2f));

        var view = go.AddComponent<ScoreboardRowView>();
        var rowSo = new SerializedObject(view);
        rowSo.FindProperty("nameText").objectReferenceValue = nameText;
        rowSo.FindProperty("scoreText").objectReferenceValue = scoreText;
        rowSo.FindProperty("kdText").objectReferenceValue = kdText;
        rowSo.FindProperty("capturesText").objectReferenceValue = capturesText;
        rowSo.FindProperty("coinsText").objectReferenceValue = coinsText;
        rowSo.FindProperty("carryTimeText").objectReferenceValue = carryText;
        rowSo.FindProperty("returnsText").objectReferenceValue = returnsText;
        rowSo.FindProperty("deadIcon").objectReferenceValue = deadIcon;
        rowSo.FindProperty("carryIcon").objectReferenceValue = carryIcon;
        rowSo.ApplyModifiedProperties();

        return view;
    }

    private static TextMeshProUGUI MakeText(string name, Transform parent, int fontSize, Color color, string sample)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = 90f;
        le.preferredHeight = 28f;

        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = fontSize;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.color = color;
        t.text = sample;
        t.raycastTarget = false;
        return t;
    }

    private static Image MakeIcon(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = 20f;
        le.preferredHeight = 20f;

        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        img.enabled = false; // toggled per-row by ScoreboardRowView.Paint
        return img;
    }
}
