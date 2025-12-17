using UnityEngine;

/// <summary>
/// Attach this to player prefabs to mark when they're carrying a flag
/// Handles disabling dash and showing visual indicator
/// </summary>
public class FlagCarrierMarker : MonoBehaviour
{
    [Header("Visual Indicator")]
    [Tooltip("Icon to show above player's head when carrying flag")]
    [SerializeField] private GameObject flagIconPrefab;
    
    [Tooltip("Height above player to show icon")]
    [SerializeField] private float iconHeight = 2f;

    private GameObject flagIcon;
    private bool isCarryingFlag = false;
    private PlayerMovement playerMovement;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
    }

    /// <summary>
    /// Set whether this player is carrying a flag
    /// </summary>
    public void SetCarryingFlag(bool carrying)
    {
        isCarryingFlag = carrying;

        // Disable/enable dash
        if (playerMovement != null)
        {
            if (carrying)
            {
                // Store original canDash state might be useful, but for now just disable
                playerMovement.enabled = false;
                playerMovement.enabled = true;
                // The dash will be disabled by checking this component in PlayerMovement
            }
        }

        // Show/hide visual indicator
        if (carrying && flagIcon == null && flagIconPrefab != null)
        {
            flagIcon = Instantiate(flagIconPrefab, transform);
            flagIcon.transform.localPosition = Vector3.up * iconHeight;
        }
        else if (!carrying && flagIcon != null)
        {
            Destroy(flagIcon);
            flagIcon = null;
        }

        Debug.Log($"Player {gameObject.name} carrying flag: {carrying}");
    }

    /// <summary>
    /// Check if player is currently carrying a flag
    /// </summary>
    public bool IsCarryingFlag()
    {
        return isCarryingFlag;
    }

    private void OnDestroy()
    {
        if (flagIcon != null)
        {
            Destroy(flagIcon);
        }
    }
}
