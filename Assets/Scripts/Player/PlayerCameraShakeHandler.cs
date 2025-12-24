using UnityEngine;

/// <summary>
/// Connects the camera system to player damage events.
/// This script triggers camera shake when the player takes damage.
/// 
/// SETUP INSTRUCTIONS:
/// 1. Attach this script to your Player prefab (the same GameObject that has PlayerStatsHandler)
/// 2. The script will automatically find the camera and trigger shake on damage
/// 3. That's it! No additional setup needed
/// 
/// HOW IT WORKS:
/// - This script listens for when the player's health changes
/// - When health decreases (damage taken), it triggers camera shake
/// - Shake intensity scales with damage amount (more damage = stronger shake)
/// </summary>
public class PlayerCameraShakeHandler : MonoBehaviour
{
    [Header("📳 Shake Settings")]
    [Tooltip("Base intensity for camera shake")]
    [SerializeField] private float baseShakeIntensity = 0.3f;

    [Tooltip("How much each point of damage increases shake intensity")]
    [SerializeField] private float damageToIntensityMultiplier = 0.05f;

    [Tooltip("Maximum shake intensity (prevents too much shake)")]
    [SerializeField] private float maxShakeIntensity = 1f;

    [Tooltip("Base duration for camera shake")]
    [SerializeField] private float baseShakeDuration = 0.2f;

    [Tooltip("Should shake get longer with more damage?")]
    [SerializeField] private bool scaleDurationWithDamage = false;

    [Header("🔧 Debug")]
    [Tooltip("Show debug messages in console")]
    [SerializeField] private bool showDebugMessages = false;

    // Internal variables
    private PlayerStatsHandler statsHandler;
    private PlayerCamera playerCamera;
    private float previousHealth;
    private bool isInitialized = false;

    /// <summary>
    /// Called when the script starts
    /// </summary>
    private void Start()
    {
        // Get the PlayerStatsHandler component
        statsHandler = GetComponent<PlayerStatsHandler>();

        if (statsHandler == null)
        {
            Debug.LogError("❌ PlayerCameraShakeHandler: No PlayerStatsHandler found on this GameObject!");
            enabled = false;
            return;
        }

        // Store initial health
        previousHealth = statsHandler.GetCurrentHealth();

        if (showDebugMessages)
        {
            Debug.Log("✓ PlayerCameraShakeHandler initialized");
        }
    }

    /// <summary>
    /// Called every frame - checks for health changes
    /// </summary>
    private void Update()
    {
        // Try to find camera if we don't have it yet
        if (playerCamera == null)
        {
            FindPlayerCamera();

            // If still null, wait for next frame
            if (playerCamera == null)
                return;
        }

        // Initialize previous health on first update with camera
        if (!isInitialized)
        {
            previousHealth = statsHandler.GetCurrentHealth();
            isInitialized = true;
            return;
        }

        // Check if health decreased (damage taken)
        float currentHealth = statsHandler.GetCurrentHealth();

        if (currentHealth < previousHealth)
        {
            // Calculate damage amount
            float damageAmount = previousHealth - currentHealth;

            // Trigger camera shake
            TriggerShakeFromDamage(damageAmount);
        }

        // Update previous health for next frame
        previousHealth = currentHealth;
    }

    /// <summary>
    /// Finds the PlayerCamera in the scene
    /// </summary>
    private void FindPlayerCamera()
    {
        // Find the PlayerCamera component in the scene
        playerCamera = FindFirstObjectByType<PlayerCamera>();

        if (playerCamera != null && showDebugMessages)
        {
            Debug.Log("✓ Found PlayerCamera");
        }
    }

    /// <summary>
    /// Triggers camera shake based on damage amount
    /// </summary>
    private void TriggerShakeFromDamage(float damageAmount)
    {
        if (playerCamera == null)
            return;

        // Calculate shake intensity based on damage
        float intensity = baseShakeIntensity + (damageAmount * damageToIntensityMultiplier);
        intensity = Mathf.Clamp(intensity, baseShakeIntensity, maxShakeIntensity);

        // Calculate shake duration
        float duration = baseShakeDuration;
        if (scaleDurationWithDamage)
        {
            duration += damageAmount * 0.02f; // +0.02s per damage point
            duration = Mathf.Clamp(duration, baseShakeDuration, baseShakeDuration * 2f);
        }

        // Trigger the shake
        playerCamera.TriggerShake(intensity, duration);

        if (showDebugMessages)
        {
            Debug.Log($"📳 Camera shake triggered! Damage: {damageAmount}, Intensity: {intensity}, Duration: {duration}");
        }
    }

    /// <summary>
    /// Manually trigger camera shake (can be called from other scripts)
    /// </summary>
    public void TriggerShakeManual(float intensity, float duration)
    {
        if (playerCamera == null)
        {
            FindPlayerCamera();
        }

        if (playerCamera != null)
        {
            playerCamera.TriggerShake(intensity, duration);
        }
    }
}