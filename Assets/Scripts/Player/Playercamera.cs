using Fusion;
using UnityEngine;

/// <summary>
/// Individual camera controller for each player in a multiplayer game.
/// This camera follows the player smoothly, zooms based on speed, and handles camera shake.
/// 
/// SETUP INSTRUCTIONS:
/// 1. Attach this script to your Main Camera in the Gameplay scene
/// 2. The camera will automatically find and follow the local player
/// 3. Adjust the Inspector values to customize the camera behavior
/// </summary>
public class PlayerCamera : MonoBehaviour
{
    [Header("🎯 View Distance Settings")]
    [Tooltip("How far the camera can see horizontally (higher = more horizontal view)")]
    [SerializeField] private float baseOrthographicSize = 5f;

    [Tooltip("Additional horizontal view distance multiplier")]
    [SerializeField] private float horizontalViewMultiplier = 1.0f;

    [Tooltip("Additional vertical view distance multiplier")]
    [SerializeField] private float verticalViewMultiplier = 1.0f;

    [Header("📹 Camera Follow Settings")]
    [Tooltip("How quickly the camera follows the player (lower = more delay/smoothing)")]
    [SerializeField] private float followSmoothTime = 0.15f;

    [Tooltip("Z position of the camera (should be negative to see the game)")]
    [SerializeField] private float cameraZPosition = -10f;

    [Header("🏃 Speed-Based Zoom")]
    [Tooltip("Enable zoom out when player moves faster")]
    [SerializeField] private bool enableSpeedZoom = true;

    [Tooltip("How much to zoom OUT when moving at max speed (0.5 = zoom out by 50%)")]
    [SerializeField] private float maxZoomOutAmount = 2f;

    [Tooltip("Player speed that triggers maximum zoom out")]
    [SerializeField] private float maxSpeedForZoom = 15f;

    [Tooltip("How quickly the zoom responds to speed changes")]
    [SerializeField] private float zoomSmoothTime = 0.3f;

    [Header("📳 Camera Shake Settings")]
    [Tooltip("How intense the shake is when player takes damage")]
    [SerializeField] private float shakeIntensity = 0.3f;

    [Tooltip("How long the shake lasts (in seconds)")]
    [SerializeField] private float shakeDuration = 0.2f;

    [Tooltip("How quickly the shake fades out")]
    [SerializeField] private float shakeDecay = 2f;

    [Header("💀 Respawn Transition")]
    [Tooltip("How long the camera takes to move to respawn point (in seconds)")]
    [SerializeField] private float respawnTransitionTime = 1f;

    [Tooltip("Should the camera arrive before the player respawns?")]
    [SerializeField] private bool arriveBeforeRespawn = true;

    [Header("🔧 Debug")]
    [Tooltip("Show debug messages in the console")]
    [SerializeField] private bool showDebugMessages = false;

    // === INTERNAL VARIABLES (Don't modify these in Inspector) ===

    // The player this camera is following
    private Transform targetPlayer;
    private Rigidbody2D targetRigidbody;

    // Smooth following variables
    private Vector3 followVelocity;
    private Vector3 currentFollowPosition;

    // Speed-based zoom variables
    private float currentZoom;
    private float zoomVelocity;
    private float targetZoom;

    // Camera shake variables
    private float shakeTimer = 0f;
    private float currentShakeIntensity = 0f;
    private Vector3 shakeOffset;

    // Respawn transition variables
    private bool isTransitioningToRespawn = false;
    private Vector3 respawnStartPosition;
    private Vector3 respawnTargetPosition;
    private float respawnTransitionTimer = 0f;

    // Camera component reference
    private Camera cam;

    /// <summary>
    /// Called when the script is first loaded
    /// </summary>
    private void Awake()
    {
        // Get the Camera component attached to this GameObject
        cam = GetComponent<Camera>();

        if (cam == null)
        {
            Debug.LogError("❌ PlayerCamera: No Camera component found! Please attach this script to a Camera.");
            enabled = false;
            return;
        }

        // Set initial zoom
        currentZoom = baseOrthographicSize;
        targetZoom = baseOrthographicSize;
        cam.orthographicSize = currentZoom;

        // Initialize follow position
        currentFollowPosition = transform.position;

        if (showDebugMessages)
        {
            Debug.Log("✓ PlayerCamera initialized and ready to find local player");
        }
    }

    /// <summary>
    /// Called every frame - searches for the local player if we don't have one yet
    /// </summary>
    private void Update()
    {
        // If we don't have a target, try to find the local player
        if (targetPlayer == null)
        {
            FindLocalPlayer();
            return; // Wait until we find a player
        }

        // Handle camera shake decay
        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.deltaTime;
            currentShakeIntensity = Mathf.Lerp(currentShakeIntensity, 0f, Time.deltaTime * shakeDecay);

            if (shakeTimer <= 0f)
            {
                shakeTimer = 0f;
                currentShakeIntensity = 0f;
                shakeOffset = Vector3.zero;
            }
        }
    }

    /// <summary>
    /// Called at a fixed rate - handles camera movement and zoom
    /// </summary>
    private void LateUpdate()
    {
        // If we don't have a target player, don't do anything
        if (targetPlayer == null)
            return;

        // Handle respawn transition
        if (isTransitioningToRespawn)
        {
            HandleRespawnTransition();
            return;
        }

        // Calculate target position (where we want the camera to be)
        Vector3 targetPosition = targetPlayer.position;
        targetPosition.z = cameraZPosition;

        // Smoothly move camera to target position
        currentFollowPosition = Vector3.SmoothDamp(
            currentFollowPosition,
            targetPosition,
            ref followVelocity,
            followSmoothTime
        );

        // Apply camera shake if active
        Vector3 finalPosition = currentFollowPosition;
        if (shakeTimer > 0f)
        {
            // Generate random shake offset
            shakeOffset = Random.insideUnitSphere * currentShakeIntensity;
            shakeOffset.z = 0f; // Keep shake in 2D plane
            finalPosition += shakeOffset;
        }

        // Set camera position
        transform.position = finalPosition;

        // Handle speed-based zoom
        if (enableSpeedZoom)
        {
            HandleSpeedBasedZoom();
        }
    }

    /// <summary>
    /// Searches for the local player in the scene and sets it as our target
    /// Only follows players that are owned by this client (IsOwner = true)
    /// </summary>
    private void FindLocalPlayer()
    {
        // Find all NetworkPlayerWrapper objects in the scene
        NetworkPlayerWrapper[] allPlayers = FindObjectsOfType<NetworkPlayerWrapper>();

        if (allPlayers.Length == 0)
        {
            // No players spawned yet, just return and try again next frame
            return;
        }

        // Look through all players to find the one owned by this client
        foreach (NetworkPlayerWrapper player in allPlayers)
        {
            // Check if this player is owned by the local client
            if (player.HasInputAuthority)
            {
                // Found the local player!
                targetPlayer = player.transform;
                targetRigidbody = player.GetComponent<Rigidbody2D>();

                // Initialize the camera position to the player's position immediately
                currentFollowPosition = targetPlayer.position;
                currentFollowPosition.z = cameraZPosition;
                transform.position = currentFollowPosition;

                if (showDebugMessages)
                {
                    Debug.Log($"✓ PlayerCamera: Found and following local player '{targetPlayer.name}'");
                }

                return;
            }
        }

        // If we get here, we didn't find the local player yet
        // This is normal during the initial connection, so we'll just try again next frame
    }

    /// <summary>
    /// Adjusts camera zoom based on player's current speed
    /// Zooms out when moving fast, zooms in when slow/stationary
    /// </summary>
    private void HandleSpeedBasedZoom()
    {
        if (targetRigidbody == null)
            return;

        // Get player's current speed
        float currentSpeed = targetRigidbody.linearVelocity.magnitude;

        // Calculate zoom based on speed (0 = no zoom, 1 = max zoom out)
        float speedRatio = Mathf.Clamp01(currentSpeed / maxSpeedForZoom);

        // Calculate target zoom amount
        // baseOrthographicSize = normal zoom
        // baseOrthographicSize + maxZoomOutAmount = fully zoomed out
        targetZoom = baseOrthographicSize + (maxZoomOutAmount * speedRatio);

        // Smoothly transition to target zoom
        currentZoom = Mathf.SmoothDamp(
            currentZoom,
            targetZoom,
            ref zoomVelocity,
            zoomSmoothTime
        );

        // Apply zoom to camera (orthographic size controls zoom in 2D)
        cam.orthographicSize = currentZoom * verticalViewMultiplier;

        // Note: In Unity's 2D camera, orthographicSize controls the vertical view.
        // Horizontal view is automatically calculated based on aspect ratio.
        // To affect horizontal view, you'd need to change the camera's aspect ratio,
        // which is typically controlled by the game window size.
    }

    /// <summary>
    /// Triggers a camera shake effect
    /// Call this from other scripts when the player takes damage
    /// </summary>
    public void TriggerShake()
    {
        shakeTimer = shakeDuration;
        currentShakeIntensity = shakeIntensity;

        if (showDebugMessages)
        {
            Debug.Log("📳 Camera shake triggered!");
        }
    }

    /// <summary>
    /// Triggers a camera shake with custom intensity and duration
    /// </summary>
    /// <param name="intensity">How strong the shake is</param>
    /// <param name="duration">How long the shake lasts</param>
    public void TriggerShake(float intensity, float duration)
    {
        shakeTimer = duration;
        currentShakeIntensity = intensity;

        if (showDebugMessages)
        {
            Debug.Log($"📳 Camera shake triggered! Intensity: {intensity}, Duration: {duration}");
        }
    }

    /// <summary>
    /// Starts a smooth transition from current position to a respawn point
    /// Call this when the player dies and is about to respawn
    /// </summary>
    /// <param name="respawnPosition">The position where the player will respawn</param>
    public void StartRespawnTransition(Vector3 respawnPosition)
    {
        isTransitioningToRespawn = true;
        respawnStartPosition = transform.position;
        respawnTargetPosition = respawnPosition;
        respawnTargetPosition.z = cameraZPosition;
        respawnTransitionTimer = 0f;

        if (showDebugMessages)
        {
            Debug.Log($"💀 Starting respawn transition to {respawnPosition}");
        }
    }

    /// <summary>
    /// Handles the smooth camera transition during respawn
    /// </summary>
    private void HandleRespawnTransition()
    {
        respawnTransitionTimer += Time.deltaTime;

        // Calculate transition progress (0 to 1)
        float progress = respawnTransitionTimer / respawnTransitionTime;

        // If we want to arrive before respawn, use normal linear progress
        // Otherwise, we can adjust the curve here
        if (arriveBeforeRespawn)
        {
            // Arrive early by speeding up the transition
            progress = Mathf.Clamp01(progress * 1.2f);
        }

        // Use smooth ease-in-out curve for professional feel
        float easedProgress = EaseInOutCubic(progress);

        // Interpolate position
        Vector3 newPosition = Vector3.Lerp(
            respawnStartPosition,
            respawnTargetPosition,
            easedProgress
        );

        transform.position = newPosition;

        // End transition when complete
        if (progress >= 1f)
        {
            isTransitioningToRespawn = false;
            currentFollowPosition = respawnTargetPosition;

            if (showDebugMessages)
            {
                Debug.Log("✓ Respawn transition complete");
            }
        }
    }

    /// <summary>
    /// Smooth easing function for respawn transition
    /// </summary>
    private float EaseInOutCubic(float t)
    {
        if (t < 0.5f)
        {
            return 4f * t * t * t;
        }
        else
        {
            float f = (2f * t) - 2f;
            return 0.5f * f * f * f + 1f;
        }
    }

    /// <summary>
    /// Call this to immediately snap the camera to a new position
    /// Useful for scene transitions or teleports
    /// </summary>
    public void SnapToPosition(Vector3 position)
    {
        position.z = cameraZPosition;
        transform.position = position;
        currentFollowPosition = position;
        followVelocity = Vector3.zero;

        // Cancel any ongoing transitions
        isTransitioningToRespawn = false;

        if (showDebugMessages)
        {
            Debug.Log($"📸 Camera snapped to position: {position}");
        }
    }

    /// <summary>
    /// Forces the camera to re-find the local player
    /// Useful if the player object changes (like after respawn)
    /// </summary>
    public void RefreshTarget()
    {
        targetPlayer = null;
        targetRigidbody = null;
        FindLocalPlayer();

        if (showDebugMessages)
        {
            Debug.Log("🔄 Camera target refreshed");
        }
    }

    // === PUBLIC GETTERS (for other scripts to access) ===

    /// <summary>
    /// Returns the player this camera is currently following
    /// </summary>
    public Transform GetTargetPlayer()
    {
        return targetPlayer;
    }

    /// <summary>
    /// Returns true if the camera is currently in a respawn transition
    /// </summary>
    public bool IsTransitioning()
    {
        return isTransitioningToRespawn;
    }
}