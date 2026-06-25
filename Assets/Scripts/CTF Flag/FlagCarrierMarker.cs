using UnityEngine;

/// <summary>
/// Attach to player prefabs. Shows a floating icon above the player's head while
/// they carry a flag, on every peer. Dash suppression is handled elsewhere via
/// IsCarryingFlag(); this component is purely the head-icon visual.
/// </summary>
public class FlagCarrierMarker : MonoBehaviour
{
    [Header("Visual Indicator")]
    [Tooltip("Icon to show above the player's head when carrying a flag")]
    [SerializeField] private GameObject flagIconPrefab;

    [Tooltip("Height above the player to show the icon")]
    [SerializeField] private float iconHeight = 2f;

    private GameObject flagIcon;
    private bool isCarryingFlag;

    /// <summary>Idempotent: repeated calls with the same value do nothing.</summary>
    public void SetCarryingFlag(bool carrying)
    {
        if (carrying == isCarryingFlag) return;
        isCarryingFlag = carrying;

        if (carrying)
        {
            if (flagIcon == null && flagIconPrefab != null)
            {
                flagIcon = Instantiate(flagIconPrefab, transform);
                flagIcon.transform.localPosition = Vector3.up * iconHeight;
            }
        }
        else if (flagIcon != null)
        {
            Destroy(flagIcon);
            flagIcon = null;
        }
    }

    public bool IsCarryingFlag() => isCarryingFlag;

    private void OnDestroy()
    {
        if (flagIcon != null) Destroy(flagIcon);
    }
}
