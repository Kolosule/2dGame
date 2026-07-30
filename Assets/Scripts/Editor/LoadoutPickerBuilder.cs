using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-click builder that grows the lobby loadout picker to match the buff catalog. Finds the
/// LobbyScreenUI in the open scene and, for every buff beyond the rows that already exist, clones
/// the last row's three objects (label / up / down), offsets them by the existing row pitch, and
/// appends them to the three serialized slot arrays.
///
/// Re-runnable: it adds only the missing rows and leaves existing ones alone. Uses the Unity API
/// rather than raw scene YAML, so it cannot collide with existing fileIDs. Undo-friendly.
///
/// Mirrors the MatchHudBuilder editor-tool pattern in this folder.
/// </summary>
public static class LoadoutPickerBuilder
{
    private const string UndoLabel = "Extend Loadout Picker";

    [MenuItem("Tools/Lobby/Extend Loadout Picker")]
    public static void Build()
    {
        var lobby = Object.FindFirstObjectByType<LobbyScreenUI>(FindObjectsInactive.Include);
        if (lobby == null)
        {
            EditorUtility.DisplayDialog("Loadout Picker Builder",
                "No LobbyScreenUI found in the open scene.\n\nOpen Assets/Scenes/MainMenu.unity and run this again.",
                "OK");
            return;
        }

        var so = new SerializedObject(lobby);
        var configProp = so.FindProperty("buffConfig");
        var labels = so.FindProperty("slotLabels");
        var ups = so.FindProperty("slotUpButtons");
        var downs = so.FindProperty("slotDownButtons");

        var config = configProp.objectReferenceValue as BuffLoadoutConfig;
        if (config == null)
        {
            EditorUtility.DisplayDialog("Loadout Picker Builder",
                "LobbyScreenUI has no BuffLoadoutConfig assigned. Assign it and run this again.", "OK");
            return;
        }

        int want = config.BuffCount;
        int have = labels.arraySize;
        if (have == 0)
        {
            EditorUtility.DisplayDialog("Loadout Picker Builder",
                "The picker has no rows to clone from. Build at least one row by hand first.", "OK");
            return;
        }
        if (want <= have)
        {
            EditorUtility.DisplayDialog("Loadout Picker Builder",
                $"Nothing to do: the picker already has {have} rows for {want} buffs.", "OK");
            return;
        }

        // Row pitch from the two existing rows, so cloned rows land on the same grid.
        float pitch = -40f;
        if (have >= 2)
        {
            var r0 = ((TMP_Text)labels.GetArrayElementAtIndex(0).objectReferenceValue).rectTransform;
            var r1 = ((TMP_Text)labels.GetArrayElementAtIndex(1).objectReferenceValue).rectTransform;
            pitch = r1.anchoredPosition.y - r0.anchoredPosition.y;
        }

        for (int slot = have; slot < want; slot++)
        {
            CloneRow(labels, slot, pitch, $"Slot{slot}Label");
            CloneRow(ups, slot, pitch, $"Slot{slot}Up");
            CloneRow(downs, slot, pitch, $"Slot{slot}Down");
        }

        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(lobby.gameObject.scene);
        Debug.Log($"LoadoutPickerBuilder: picker now has {want} rows. Save the scene to keep them.");
    }

    /// <summary>
    /// Clones the last element of a serialized object-reference array into a new row at
    /// <paramref name="slot"/>, shifted one pitch down, and appends it to the array.
    /// </summary>
    private static void CloneRow(SerializedProperty array, int slot, float pitch, string name)
    {
        if (array.arraySize == 0) return;
        var last = array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue as Component;
        if (last == null) return;

        var clone = Object.Instantiate(last.gameObject, last.transform.parent);
        Undo.RegisterCreatedObjectUndo(clone, UndoLabel);
        clone.name = name;

        var rt = clone.GetComponent<RectTransform>();
        var srcRt = last.GetComponent<RectTransform>();
        if (rt != null && srcRt != null)
            rt.anchoredPosition = srcRt.anchoredPosition + new Vector2(0f, pitch);

        // Cloned buttons carry the source row's persistent listeners; MoveSlot is wired in code
        // by LobbyScreenUI.WireLoadoutButtons, so strip anything the clone inherited.
        var button = clone.GetComponent<Button>();
        if (button != null) button.onClick = new Button.ButtonClickedEvent();

        Component added = clone.GetComponent(last.GetType());
        array.arraySize++;
        array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue = added;
    }
}
