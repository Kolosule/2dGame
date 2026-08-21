using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Game.Buffs.Core;

/// <summary>
/// One-click builder for the Scope 4 economy HUD: the shared unlock-toast feed, the Team Power
/// strip's Vanguard pips / progress / zone indicator, per-icon tier pips and next-unlock fills on
/// every BuffIconDisplay, and the Sudden Death banner. Finds the existing HUD components in the
/// open scene and wires their private [SerializeField] references via SerializedObject.
///
/// Safe with the editor open (Unity API, not raw scene YAML), re-runnable (it rebuilds only the
/// containers it owns, by name) and undo-friendly. Mirrors MatchHudBuilder.
/// </summary>
public static class EconomyHudBuilder
{
    private const string UndoLabel = "Build Economy HUD";

    [MenuItem("Tools/Economy/Build Economy HUD")]
    public static void Build()
    {
        var canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Economy HUD Builder",
                "No Canvas found in the open scene.", "OK");
            return;
        }

        HudToastFeed feed = BuildToastFeed(canvas);
        var iconNames = new System.Collections.Generic.List<string>();
        int icons = BuildBuffIcons(feed, iconNames);
        bool strip = BuildTeamPowerStrip(feed);
        bool banner = BuildSuddenDeathBanner(canvas);

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        string names = iconNames.Count > 0 ? string.Join(", ", iconNames) : "none";
        Debug.Log($"[Economy] HUD built: toast feed ✔, {icons} buff icon(s) extended ({names}), " +
                  $"Team Power strip {(strip ? "✔" : "SKIPPED — no TeamScoreDisplay in scene")}, " +
                  $"Sudden Death banner {(banner ? "✔" : "SKIPPED — no MatchPhaseHud in scene")}. " +
                  $"Save the scene (Ctrl+S).");
    }

    private static HudToastFeed BuildToastFeed(Canvas canvas)
    {
        var existing = Object.FindFirstObjectByType<HudToastFeed>(FindObjectsInactive.Include);
        if (existing != null) return existing;

        var go = new GameObject("UnlockToast", typeof(RectTransform), typeof(CanvasGroup));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(canvas.transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(680f, 70f);
        rt.anchoredPosition = new Vector2(0f, -160f);

        var label = MakeText("UnlockToastLabel", go.transform, 34, new Color(1f, 0.86f, 0.40f),
            Vector2.zero, new Vector2(680f, 70f), "EXTRA JUMP  T2");
        label.fontStyle = FontStyles.Bold;

        var feed = Undo.AddComponent<HudToastFeed>(go);
        var so = new SerializedObject(feed);
        so.FindProperty("group").objectReferenceValue = go.GetComponent<CanvasGroup>();
        so.FindProperty("label").objectReferenceValue = label;
        so.ApplyModifiedProperties();

        go.GetComponent<CanvasGroup>().alpha = 0f;
        return feed;
    }

    private static int BuildBuffIcons(HudToastFeed feed, System.Collections.Generic.List<string> namesSet)
    {
        var icons = Object.FindObjectsByType<BuffIconDisplay>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var icon in icons)
        {
            Undo.RecordObject(icon, UndoLabel);
            var so = new SerializedObject(icon);

            var pips = BuildPipRow(icon.transform, 3, new Vector2(0f, -34f), 14f);
            var pipsProp = so.FindProperty("pips");
            pipsProp.arraySize = pips.Length;
            for (int i = 0; i < pips.Length; i++)
                pipsProp.GetArrayElementAtIndex(i).objectReferenceValue = pips[i];

            var fill = BuildBar("NextUnlockFill", icon.transform, new Vector2(0f, -48f),
                new Vector2(64f, 6f), new Color(0.35f, 0.65f, 1f));
            so.FindProperty("nextUnlockFill").objectReferenceValue = fill;
            so.FindProperty("toastFeed").objectReferenceValue = feed;

            string displayName = DisplayNameFor((BuffId)so.FindProperty("buffId").enumValueIndex);
            so.FindProperty("displayName").stringValue = displayName;
            namesSet?.Add(displayName);

            so.ApplyModifiedProperties();
        }

        return icons.Length;
    }

    private static bool BuildTeamPowerStrip(HudToastFeed feed)
    {
        var strip = Object.FindFirstObjectByType<TeamScoreDisplay>(FindObjectsInactive.Include);
        if (strip == null) return false;

        Undo.RecordObject(strip, UndoLabel);
        var so = new SerializedObject(strip);

        var pips = BuildPipRow(strip.transform, 2, new Vector2(0f, -30f), 18f);
        var pipsProp = so.FindProperty("vanguardPips");
        pipsProp.arraySize = pips.Length;
        for (int i = 0; i < pips.Length; i++)
            pipsProp.GetArrayElementAtIndex(i).objectReferenceValue = pips[i];

        var fill = BuildBar("VanguardProgressFill", strip.transform, new Vector2(0f, -46f),
            new Vector2(220f, 8f), new Color(1f, 0.86f, 0.40f));
        so.FindProperty("vanguardProgressFill").objectReferenceValue = fill;

        var milestone = MakeText("VanguardMilestoneText", strip.transform, 20,
            new Color(0.80f, 0.83f, 0.90f), new Vector2(0f, -66f), new Vector2(420f, 28f),
            "VANGUARD T0   0/12   +0% DAMAGE TAKEN");
        so.FindProperty("vanguardMilestoneText").objectReferenceValue = milestone;

        so.FindProperty("toastFeed").objectReferenceValue = feed;
        so.ApplyModifiedProperties();
        return true;
    }

    private static bool BuildSuddenDeathBanner(Canvas canvas)
    {
        var hud = Object.FindFirstObjectByType<MatchPhaseHud>(FindObjectsInactive.Include);
        if (hud == null) return false;

        Undo.RecordObject(hud, UndoLabel);
        var so = new SerializedObject(hud);

        var banner = Rebuild("SuddenDeathBanner", canvas.transform, new Vector2(0f, -70f),
            new Vector2(900f, 64f));
        var bg = Undo.AddComponent<Image>(banner);
        bg.color = new Color(0.55f, 0.06f, 0.10f, 0.92f);

        var rt = banner.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);

        var label = MakeText("SuddenDeathText", banner.transform, 30, Color.white,
            Vector2.zero, new Vector2(900f, 64f),
            "SUDDEN DEATH · all buffs unlocked · next capture wins");
        label.fontStyle = FontStyles.Bold;

        so.FindProperty("suddenDeathRoot").objectReferenceValue = banner;
        so.ApplyModifiedProperties();

        banner.SetActive(false); // MatchPhaseHud.Awake hides it anyway; keep the scene tidy.
        return true;
    }

    // ---- primitives ----

    /// <summary>
    /// Human-readable buff name for the unlock toast. Without this the toast reads "Buff T1" for
    /// every buff, which is the one thing it must not do — naming the buff IS the message.
    /// </summary>
    private static string DisplayNameFor(BuffId id)
    {
        switch (id)
        {
            case BuffId.ExtraJump: return "Extra Jump";
            case BuffId.Stealth: return "Stealth";
            case BuffId.QuickerDash: return "Quicker Dash";
            case BuffId.FlagRunner: return "Flag Runner";
            default: return id.ToString();
        }
    }

    private static GameObject Rebuild(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var old = parent.Find(name);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return go;
    }

    private static Image[] BuildPipRow(Transform parent, int count, Vector2 origin, float spacing)
    {
        var row = Rebuild("TierPips", parent, origin, new Vector2(spacing * count, spacing));
        var pips = new Image[count];

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"Pip{i + 1}", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, UndoLabel);
            go.transform.SetParent(row.transform, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(spacing * 0.6f, spacing * 0.6f);
            rt.anchoredPosition = new Vector2((i - (count - 1) * 0.5f) * spacing, 0f);

            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.18f);
            img.raycastTarget = false;
            pips[i] = img;
        }

        return pips;
    }

    private static Image BuildBar(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
    {
        var track = Rebuild(name + "Track", parent, pos, size);
        var trackImg = track.AddComponent<Image>();
        trackImg.color = new Color(1f, 1f, 1f, 0.12f);
        trackImg.raycastTarget = false;

        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
        go.transform.SetParent(track.transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillAmount = 0f;
        return img;
    }

    private static TextMeshProUGUI MakeText(string name, Transform parent, int fontSize,
        Color color, Vector2 anchoredPos, Vector2 size, string sample)
    {
        var old = parent.Find(name);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

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
}
