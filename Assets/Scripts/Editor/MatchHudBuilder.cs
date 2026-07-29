using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-click builder for the match ResultsPanel. Finds the MatchPhaseHud in the open scene,
/// (re)builds a basic centered results card — winner banner, final score, "returning in N…"
/// line, and a Return-to-Lobby button — under a dim full-screen backdrop, then wires all four
/// results fields (plus the panel) into the MatchPhaseHud via SerializedObject.
///
/// Safe to run with the editor open (it uses the Unity API, not raw scene YAML) and re-runnable:
/// it rebuilds only the "ResultsContent" child, leaving any other panel children alone. The
/// button's onClick is added in code by MatchPhaseHud.Awake, so wiring the serialized reference
/// is all that's required for the button to work. Undo-friendly.
///
/// Mirrors the SkySceneBuilder editor-tool pattern (Assets/Sky/Scripts/Editor).
/// </summary>
public static class MatchHudBuilder
{
    private const string UndoLabel = "Build Results Panel";

    [MenuItem("Tools/Match/Build Results Panel")]
    public static void Build()
    {
        var hud = Object.FindFirstObjectByType<MatchPhaseHud>(FindObjectsInactive.Include);
        if (hud == null)
        {
            EditorUtility.DisplayDialog("Match HUD Builder",
                "No MatchPhaseHud found in the open scene.\n\nAdd the MatchPhaseHud component to your " +
                "HUD canvas first, then run this again.", "OK");
            return;
        }

        var canvas = hud.GetComponentInParent<Canvas>(true);
        if (canvas == null) canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Match HUD Builder",
                "No Canvas found in the open scene to parent the ResultsPanel under.", "OK");
            return;
        }

        var so = new SerializedObject(hud);
        var panelProp = so.FindProperty("resultsPanel");
        Undo.RecordObject(hud, UndoLabel);

        // Reuse the already-assigned panel (e.g. the repurposed game-over panel) or make a new one.
        var panel = panelProp.objectReferenceValue as GameObject;
        if (panel == null)
        {
            panel = new GameObject("ResultsPanel", typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panel, UndoLabel);
            panel.transform.SetParent(canvas.transform, false);
        }

        // Full-screen dim backdrop.
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

        // Rebuild only our own content container so re-running is idempotent and never duplicates.
        var old = panel.transform.Find("ResultsContent");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var content = new GameObject("ResultsContent", typeof(RectTransform), typeof(Image));
        Undo.RegisterCreatedObjectUndo(content, UndoLabel);
        content.transform.SetParent(panel.transform, false);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(760f, 480f);
        crt.anchoredPosition = Vector2.zero;
        content.GetComponent<Image>().color = new Color(0.10f, 0.11f, 0.16f, 0.96f);

        var winner = MakeText("WinnerText", content.transform, 64,
            new Color(1f, 0.86f, 0.40f), new Vector2(0f, 150f), new Vector2(720f, 110f), "Team 1 Wins!");
        winner.fontStyle = FontStyles.Bold;

        var score = MakeText("FinalScoreText", content.transform, 40,
            Color.white, new Vector2(0f, 40f), new Vector2(720f, 60f), "Team 1  0   —   0  Team 2");

        var countdown = MakeText("ReturnCountdownText", content.transform, 26,
            new Color(0.75f, 0.78f, 0.85f), new Vector2(0f, -40f), new Vector2(720f, 50f),
            "Returning to lobby in 20…");

        var button = MakeButton("ReturnToLobbyButton", content.transform,
            new Vector2(0f, -150f), new Vector2(300f, 72f), "Return to Lobby");

        // Wire the private [SerializeField] references on MatchPhaseHud.
        panelProp.objectReferenceValue = panel;
        so.FindProperty("winnerText").objectReferenceValue = winner;
        so.FindProperty("finalScoreText").objectReferenceValue = score;
        so.FindProperty("returnCountdownText").objectReferenceValue = countdown;
        so.FindProperty("returnToLobbyButton").objectReferenceValue = button;
        so.ApplyModifiedProperties();

        // Left visible so you can preview/tweak in the editor; MatchPhaseHud.Awake hides it at runtime.
        panel.SetActive(true);
        Selection.activeGameObject = panel;
        EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);

        Debug.Log("[Match] ResultsPanel built and wired to MatchPhaseHud (winner / score / return " +
                  "countdown / Return-to-Lobby button). Save the scene (Ctrl+S). It auto-hides at runtime.");
    }

    private static TextMeshProUGUI MakeText(string name, Transform parent, int fontSize,
        Color color, Vector2 anchoredPos, Vector2 size, string sample)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = fontSize;
        t.alignment = TextAlignmentOptions.Center;
        t.color = color;
        t.text = sample;
        t.raycastTarget = false;

        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;
        return t;
    }

    private static Button MakeButton(string name, Transform parent, Vector2 anchoredPos,
        Vector2 size, string label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        var img = go.GetComponent<Image>();
        img.color = new Color(0.20f, 0.45f, 0.85f, 1f);

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;

        // Label stretched to fill the button.
        var lbl = MakeText(name + "Label", go.transform, 28, Color.white, Vector2.zero, size, label);
        var lrt = lbl.rectTransform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        return btn;
    }
}
