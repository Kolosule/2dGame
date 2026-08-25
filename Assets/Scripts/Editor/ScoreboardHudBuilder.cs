using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-click builder for the ScoreboardPanel: a dim backdrop, a team-score summary line, a column
/// header row, and one centred column of pooled rows -- a single merged individual leaderboard, not
/// two team columns. Builds one hidden row template with all ScoreboardRowView fields wired
/// (including the team stripe, the rank cell and the self outline) and wires ScoreboardPanel's
/// serialized references via SerializedObject. Safe to re-run (rebuilds only its own
/// "ScoreboardContent" child). Mirrors the MatchHudBuilder / EconomyHudBuilder editor-tool pattern.
/// </summary>
public static class ScoreboardHudBuilder
{
    private const string UndoLabel = "Build Scoreboard Panel";

    // Cell widths, shared by the header row and the data row so the two can never drift apart.
    // Row and header both total 760: 680 of cells plus 10 gaps of 8, inside an 800-wide column.
    private const float StripeWidth = 6f;
    private const float RankWidth = 44f;
    private const float NameWidth = 160f;
    private const float ScoreWidth = 80f;
    private const float KdWidth = 80f;
    private const float CapturesWidth = 70f;
    private const float CoinsWidth = 70f;
    private const float CarryWidth = 70f;
    private const float ReturnsWidth = 60f;
    private const float IconWidth = 20f;
    private const float CellHeight = 28f;
    private const float SummaryHeight = 36f;

    /// <summary>
    /// Turns on "control child size" for a layout group. This is NOT cosmetic and NOT the default:
    /// a layout group created from script serializes m_ChildControlWidth/Height as FALSE, and while
    /// they are false the group ignores every LayoutElement.preferredWidth/Height and lays children
    /// out at their raw RectTransform size instead -- 200x50 for a fresh TextMeshProUGUI, 100x100
    /// for a fresh Image. That made one row ~1980px wide inside a 100px-wide row rect, and an
    /// overflowing layout group ignores childAlignment and packs from its left edge, so the board
    /// started at screen centre and ran off the right. It also left the self outline (which stretches
    /// to the row rect) as a small box instead of a full-row highlight. Call this on every group
    /// this builder creates.
    /// </summary>
    private static void ControlChildSize(HorizontalOrVerticalLayoutGroup layout)
    {
        layout.childControlWidth = true;
        layout.childControlHeight = true;
    }

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
        // Purely informational -- never intercept pointer events (the PostMatch results panel's
        // Return-to-Lobby button lives on the same canvas and must stay clickable).
        backdrop.raycastTarget = false;

        var old = panel.transform.Find("ScoreboardContent");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var content = new GameObject("ScoreboardContent", typeof(RectTransform), typeof(VerticalLayoutGroup));
        Undo.RegisterCreatedObjectUndo(content, UndoLabel);
        content.transform.SetParent(panel.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.5f, 0.5f);
        crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        // Narrower and taller than the old two-column board: one row is 760px of cells, and a full
        // 20-player roster is 20 rows plus the summary line and the header row (~716px). Sized to fit
        // that without overflowing the 1080-tall reference canvas once the -150 offset below is
        // applied (top edge +230, bottom edge -530, against a +/-540 half-height).
        crt.sizeDelta = new Vector2(800f, 760f);
        // Offset down from center so the board doesn't collide with the PostMatch results banner,
        // which was reshaped into a top strip (see the Unity setup guide, "Step 5"). Baked in here
        // so re-running the builder reproduces that fix instead of resetting to (0, 0).
        crt.anchoredPosition = new Vector2(0f, -150f);

        var contentLayout = content.GetComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 8f;
        contentLayout.childAlignment = TextAnchor.UpperCenter;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        ControlChildSize(contentLayout);

        // Order matters: summary, then column headers, then the pooled rows.
        MakeTeamScoreLine(content.transform, out TextMeshProUGUI team1ScoreText, out TextMeshProUGUI team2ScoreText);
        MakeHeaderRow(content.transform);
        Transform rowContainer = MakeRowContainer(content.transform);

        ScoreboardRowView rowTemplate = MakeRowTemplate("RowTemplate", rowContainer);

        rootProp.objectReferenceValue = panel;
        so.FindProperty("rowContainer").objectReferenceValue = rowContainer;
        so.FindProperty("rowTemplate").objectReferenceValue = rowTemplate;
        so.FindProperty("team1ScoreText").objectReferenceValue = team1ScoreText;
        so.FindProperty("team2ScoreText").objectReferenceValue = team2ScoreText;
        so.ApplyModifiedProperties();

        panel.SetActive(true);
        Selection.activeGameObject = panel;
        EditorSceneManager.MarkSceneDirty(scoreboard.gameObject.scene);

        Debug.Log("[Match] ScoreboardPanel built and wired (one merged rank-ordered list, team " +
                  "stripes, team-score summary). Save the scene (Ctrl+S). It auto-hides at runtime.");
    }

    /// <summary>
    /// The "BLUE 2 - RED 1" line above the list. Removing the BLUE/RED column headers removed the
    /// only in-panel view of team standing, so it moves here. ScoreboardPanel writes both the text
    /// and each half's team colour, so the sample text/colour set here is only for editing comfort.
    /// </summary>
    private static void MakeTeamScoreLine(Transform parent, out TextMeshProUGUI team1ScoreText,
        out TextMeshProUGUI team2ScoreText)
    {
        var go = new GameObject("TeamScoreLine", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        ControlChildSize(layout);

        team1ScoreText = MakeText("Team1ScoreText", go.transform, 26, Color.white, "BLUE  0", 160f, SummaryHeight);
        team1ScoreText.fontStyle = FontStyles.Bold;
        team1ScoreText.alignment = TextAlignmentOptions.MidlineRight;

        var separator = MakeText("SeparatorText", go.transform, 26, new Color(0.62f, 0.64f, 0.7f), "-", 24f,
            SummaryHeight);
        separator.alignment = TextAlignmentOptions.Midline;

        team2ScoreText = MakeText("Team2ScoreText", go.transform, 26, Color.white, "RED  0", 160f, SummaryHeight);
        team2ScoreText.fontStyle = FontStyles.Bold;
        team2ScoreText.alignment = TextAlignmentOptions.MidlineLeft;
    }

    private static void MakeHeaderRow(Transform parent)
    {
        var go = new GameObject("ColumnHeaders", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        ControlChildSize(layout);

        // Blank cell standing in for the row's team stripe, so "Rank" starts where the rank cell does.
        MakeSpacer("StripeHeaderSpacer", go.transform, StripeWidth);

        Color labelColor = new Color(0.62f, 0.64f, 0.7f);
        var columns = new (string Label, float Width)[]
        {
            ("Rank", RankWidth),
            ("Name", NameWidth),
            ("Score", ScoreWidth),
            ("K/D", KdWidth),
            ("Cap", CapturesWidth),
            ("Coins", CoinsWidth),
            ("Carry", CarryWidth),
            ("Ret", ReturnsWidth)
        };

        foreach (var column in columns)
        {
            var text = MakeText(column.Label + "HeaderLabel", go.transform, 14, labelColor, column.Label,
                column.Width);
            text.fontStyle = FontStyles.Bold;
        }

        // Blank spacers matching the two icon cells' width, so the header row's total width
        // matches a data row's and nothing after "Ret" drifts out of alignment.
        MakeSpacer("DeadIconHeaderSpacer", go.transform, IconWidth);
        MakeSpacer("CarryIconHeaderSpacer", go.transform, IconWidth);
    }

    /// <summary>The single pooled-row container. ScoreboardPanel Instantiates the template into this
    /// one transform -- there is deliberately no second per-team container any more.</summary>
    private static Transform MakeRowContainer(Transform parent)
    {
        var go = new GameObject("Rows", typeof(RectTransform), typeof(VerticalLayoutGroup));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var layout = go.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        ControlChildSize(layout);

        // Deliberately NO ContentSizeFitter: ScoreboardContent controls this child's height now
        // (childControlHeight on, childForceExpandHeight off gives it exactly its preferred height),
        // and a fitter underneath a controlling layout group is the classic two-writers fight.
        return go.transform;
    }

    private static ScoreboardRowView MakeRowTemplate(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        ControlChildSize(layout);

        // Behind everything else (first sibling) and outside the layout, so it frames the whole row
        // instead of taking a cell. ScoreboardRowView.Paint toggles it for the local player only.
        var selfOutline = MakeSelfOutline("SelfOutline", go.transform);

        var teamStripe = MakeStripe("TeamStripe", go.transform);
        var rankText = MakeText("RankText", go.transform, 18, new Color(0.62f, 0.64f, 0.7f), "1.", RankWidth);
        var nameText = MakeText("NameText", go.transform, 20, Color.white, "PlayerName", NameWidth);
        var scoreText = MakeText("ScoreText", go.transform, 20, new Color(1f, 0.86f, 0.40f), "0", ScoreWidth);
        var kdText = MakeText("KdText", go.transform, 18, Color.white, "0/0", KdWidth);
        var capturesText = MakeText("CapturesText", go.transform, 18, Color.white, "0", CapturesWidth);
        var coinsText = MakeText("CoinsText", go.transform, 18, Color.white, "0", CoinsWidth);
        var carryText = MakeText("CarryTimeText", go.transform, 18, Color.white, "0:00", CarryWidth);
        var returnsText = MakeText("ReturnsText", go.transform, 18, Color.white, "0", ReturnsWidth);
        var deadIcon = MakeIcon("DeadIcon", go.transform, new Color(0.6f, 0.1f, 0.1f));
        var carryIcon = MakeIcon("CarryIcon", go.transform, new Color(0.9f, 0.8f, 0.2f));

        var view = go.AddComponent<ScoreboardRowView>();
        var rowSo = new SerializedObject(view);
        rowSo.FindProperty("teamStripe").objectReferenceValue = teamStripe;
        rowSo.FindProperty("selfOutline").objectReferenceValue = selfOutline;
        rowSo.FindProperty("rankText").objectReferenceValue = rankText;
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

    /// <summary>The thin full-height team colour bar at the far left of a row. Left neutral here;
    /// ScoreboardRowView.Paint tints it per player.</summary>
    private static Image MakeStripe(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = StripeWidth;
        le.preferredHeight = CellHeight;
        // Fills the row's height whatever the row ends up being, so the stripe reads as an edge
        // rather than a floating chip.
        le.flexibleHeight = 1f;

        var img = go.GetComponent<Image>();
        img.color = new Color(0.62f, 0.64f, 0.70f, 1f);
        img.raycastTarget = false;
        return img;
    }

    /// <summary>
    /// The local player's row highlight: a bright neutral plate behind the row, deliberately not a
    /// team colour so it can never read as a third team. Excluded from the layout and stretched over
    /// the whole row, and drawn first so the stat text sits on top of it. Drop a 9-sliced border
    /// sprite onto this Image in the inspector to turn it into a hollow outline -- no code change.
    /// </summary>
    private static Image MakeSelfOutline(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);
        go.transform.SetAsFirstSibling();

        go.GetComponent<LayoutElement>().ignoreLayout = true;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-4f, -2f);
        rt.offsetMax = new Vector2(4f, 2f);

        var img = go.GetComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.16f);
        img.raycastTarget = false;
        img.enabled = false; // toggled per-row by ScoreboardRowView.Paint
        return img;
    }

    private static TextMeshProUGUI MakeText(string name, Transform parent, int fontSize, Color color,
        string sample, float width, float height = CellHeight)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height;

        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = fontSize;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.color = color;
        t.text = sample;
        t.raycastTarget = false;
        // Cells are genuinely width-constrained now, so a long nickname would otherwise wrap onto a
        // second line and drag the whole row's height with it. Truncate instead.
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    private static Image MakeIcon(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = IconWidth;
        le.preferredHeight = IconWidth;

        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        img.enabled = false; // toggled per-row by ScoreboardRowView.Paint
        return img;
    }

    /// <summary>Invisible layout-only cell, used to keep the header row's width matching a data row's.</summary>
    private static void MakeSpacer(string name, Transform parent, float width)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = 20f;
    }
}
