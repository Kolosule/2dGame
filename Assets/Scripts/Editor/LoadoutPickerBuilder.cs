using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Game.Buffs.Core;

/// <summary>
/// Refreshes the lobby's read-only upgrade path, adding missing labels and hiding legacy
/// reorder controls. Re-runnable and undo-friendly, using the Unity API rather than scene YAML.
/// </summary>
public static class LoadoutPickerBuilder
{
    private const string UndoLabel = "Refresh Upgrade Path";

    [MenuItem("Tools/Lobby/Refresh Upgrade Path")]
    public static void Build()
    {
        var lobby = Object.FindFirstObjectByType<LobbyScreenUI>(FindObjectsInactive.Include);
        if (lobby == null)
        {
            EditorUtility.DisplayDialog("Upgrade Path Builder",
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
            EditorUtility.DisplayDialog("Upgrade Path Builder",
                "LobbyScreenUI has no BuffLoadoutConfig assigned. Assign it and run this again.", "OK");
            return;
        }

        int want = BuffUpgradePath.Order.Count;
        int have = labels.arraySize;
        if (have == 0)
        {
            EditorUtility.DisplayDialog("Upgrade Path Builder",
                "The upgrade path has no labels to clone from. Build at least one label by hand first.", "OK");
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
            CloneRow(labels, slot, pitch, $"Slot{slot}Label");

        for (int slot = 0; slot < labels.arraySize; slot++)
        {
            var label = labels.GetArrayElementAtIndex(slot).objectReferenceValue as TMP_Text;
            if (label == null) continue;
            Undo.RecordObject(label, UndoLabel);
            if (slot < want)
            {
                BuffId id = BuffUpgradePath.Order[slot];
                var definition = config.GetById(id);
                label.text = $"{slot + 1}. {(definition != null ? definition.DisplayName : id.ToString())}";
            }
            else label.text = "";
        }
        HideReorderButtons(ups);
        HideReorderButtons(downs);

        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(lobby.gameObject.scene);
        Debug.Log($"LoadoutPickerBuilder: fixed upgrade path now has {want} rows. Save the scene to keep them.");
    }

    private static void HideReorderButtons(SerializedProperty buttons)
    {
        for (int i = 0; i < buttons.arraySize; i++)
        {
            var button = buttons.GetArrayElementAtIndex(i).objectReferenceValue as Button;
            if (button == null) continue;
            Undo.RecordObject(button, UndoLabel);
            Undo.RecordObject(button.gameObject, UndoLabel);
            button.interactable = false;
            button.gameObject.SetActive(false);
        }
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

        Component added = clone.GetComponent(last.GetType());
        array.arraySize++;
        array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue = added;
    }
}
