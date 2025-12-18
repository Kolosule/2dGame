using UnityEngine;

/// <summary>
/// Simple static camera for the main menu scene.
/// No special movement or effects - just a fixed view of the menu.
/// 
/// SETUP INSTRUCTIONS:
/// 1. Attach this script to your Main Camera in the MainMenu scene
/// 2. Position the camera where you want it in the scene
/// 3. That's it! The camera will stay in that position
/// </summary>
public class MenuCamera : MonoBehaviour
{
    [Header("📹 Camera Settings")]
    [Tooltip("The orthographic size (controls zoom level). Higher = more zoomed out")]
    [SerializeField] private float orthographicSize = 5f;

    [Tooltip("Z position of the camera (should be negative to see the scene)")]
    [SerializeField] private float cameraZPosition = -10f;

    [Header("🎨 Optional: Camera Animation")]
    [Tooltip("Enable a subtle camera sway/movement for visual interest")]
    [SerializeField] private bool enableSubtleSway = false;

    [Tooltip("How much the camera sways (very small values recommended)")]
    [SerializeField] private float swayAmount = 0.1f;

    [Tooltip("How fast the camera sways")]
    [SerializeField] private float swaySpeed = 1f;

    // Internal variables
    private Camera cam;
    private Vector3 originalPosition;

    /// <summary>
    /// Called when the script starts
    /// </summary>
    private void Start()
    {
        // Get the Camera component
        cam = GetComponent<Camera>();

        if (cam == null)
        {
            Debug.LogError("❌ MenuCamera: No Camera component found! Please attach this script to a Camera.");
            enabled = false;
            return;
        }

        // Set camera to orthographic mode (for 2D)
        cam.orthographicSize = orthographicSize;

        // Store the original position
        originalPosition = transform.position;

        // Make sure Z position is set correctly
        Vector3 pos = transform.position;
        pos.z = cameraZPosition;
        transform.position = pos;

        Debug.Log("✓ MenuCamera initialized");
    }

    /// <summary>
    /// Called every frame - handles optional camera sway
    /// </summary>
    private void Update()
    {
        if (!enableSubtleSway)
            return;

        // Create a subtle swaying motion using sine waves
        float swayX = Mathf.Sin(Time.time * swaySpeed) * swayAmount;
        float swayY = Mathf.Cos(Time.time * swaySpeed * 0.8f) * swayAmount;

        // Apply sway to camera position
        Vector3 newPosition = originalPosition;
        newPosition.x += swayX;
        newPosition.y += swayY;

        transform.position = newPosition;
    }

    /// <summary>
    /// Resets the camera to its original position
    /// </summary>
    public void ResetPosition()
    {
        transform.position = originalPosition;
    }

    /// <summary>
    /// Changes the camera's zoom level
    /// </summary>
    public void SetZoom(float newSize)
    {
        orthographicSize = newSize;
        if (cam != null)
        {
            cam.orthographicSize = orthographicSize;
        }
    }
}