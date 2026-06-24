using UnityEngine;

/// <summary>
/// Marks the map center used to scale enemy difficulty. Place exactly one in each
/// gameplay scene at the contested center point.
/// </summary>
public class ArenaCenter : MonoBehaviour
{
    public static ArenaCenter Instance { get; private set; }

    /// <summary>World-space center position (XY).</summary>
    public Vector2 Position => transform.position;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"Multiple ArenaCenter instances; keeping {Instance.name}, ignoring {name}.");
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 1f);
    }
}
