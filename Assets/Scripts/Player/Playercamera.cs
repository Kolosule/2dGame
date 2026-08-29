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

    [Tooltip("Horizontal follow smoothing — keep very low so run/dash feel instant.")]
    [SerializeField] private float horizontalSmoothTime = 0.03f;

    [Tooltip("Vertical follow smoothing applied once the player leaves the deadzone band.")]
    [SerializeField] private float verticalSmoothTime = 0.16f;

    [Tooltip("Half-height (world units) of the vertical deadzone. The camera only moves in Y " +
             "when the player leaves this band, so jumps/hops don't jerk the view.")]
    [SerializeField] private float verticalDeadzone = 1.2f;

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

    [Header("🩹 Network Correction Absorb")]
    [Tooltip("Body movement faster than this (world units/sec) is treated as a reconciliation " +
             "snap, not real motion. Keep above dash speed so dashes are followed instantly.")]
    [SerializeField] private float maxFollowSpeed = 40f;

    [Tooltip("How quickly an absorbed correction eases out (higher = faster catch-up).")]
    [SerializeField] private float correctionRecoverRate = 9f;

    [Header("🎯 Aim Lean")]
    [Tooltip("Bias the camera toward the mouse aim direction.")]
    [SerializeField] private bool enableAimLean = true;

    [Tooltip("Max camera offset (world units) toward the aim direction. Keep small so the screen " +
             "edge stays within the player's Area-of-Interest radius.")]
    [SerializeField] private float aimLeanDistance = 2.0f;

    [Tooltip("Smoothing for the aim lean so fast cursor flicks don't jerk the view.")]
    [SerializeField] private float aimLeanSmoothTime = 0.2f;

    // === INTERNAL VARIABLES (Don't modify these in Inspector) ===

    // The player this camera is following
    private Transform targetPlayer;
    private Rigidbody2D targetRigidbody;

    // Aim lean
    private PlayerCombat targetCombat;
    private Vector3 currentAimLean;
    private Vector3 aimLeanVelocity;

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

    // Network-correction absorb
    private Vector3 lastBodyPosition;
    private Vector3 correctionOffset;
    private bool hasLastBodyPosition;

    // Impulse channel (dash kick, etc.) — additive, decaying.
    private struct CamImpulse { public Vector3 dir; public float magnitude; public float duration; public float elapsed; }
    private readonly System.Collections.Generic.List<CamImpulse> impulses = new System.Collections.Generic.List<CamImpulse>();

    // Camera component reference
    private Camera cam;

    /// <summary>
    /// Called when the script is first loaded
    /// </summary>
    private void Awake()
    {
        if (DedicatedServerPresentation.IsHeadless)
        {
            enabled = false;
            return;
        }

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

        currentFollowPosition = ComputeFollowPosition();

        // Aim lean (additive, capped, smoothed).
        Vector3 targetLean = Vector3.zero;
        if (enableAimLean && targetCombat != null)
        {
            Vector2 aimDir = targetCombat.GetAimDirection();
            targetLean = (Vector3)(aimDir * aimLeanDistance);
        }
        currentAimLean = Vector3.SmoothDamp(currentAimLean, targetLean,
                                            ref aimLeanVelocity, aimLeanSmoothTime);

        // Apply camera shake if active
        Vector3 finalPosition = currentFollowPosition + currentAimLean;
        finalPosition.z = cameraZPosition;
        if (shakeTimer > 0f)
        {
            // Generate random shake offset
            shakeOffset = Random.insideUnitSphere * currentShakeIntensity;
            shakeOffset.z = 0f; // Keep shake in 2D plane
            finalPosition += shakeOffset;
        }

        finalPosition += EvaluateImpulses();

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
        // Find all player objects in the scene
        PlayerController[] allPlayers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);

        if (allPlayers.Length == 0)
        {
            // No players spawned yet, just return and try again next frame
            return;
        }

        // Look through all players to find the one owned by this client
        foreach (PlayerController player in allPlayers)
        {
            // Check if this player is owned by the local client
            if (player.HasInputAuthority)
            {
                // Found the local player!
                targetPlayer = player.transform;
                targetRigidbody = player.GetComponent<Rigidbody2D>();
                targetCombat = player.GetComponent<PlayerCombat>();

                // Initialize the camera position to the player's position immediately
                currentFollowPosition = targetPlayer.position;
                currentFollowPosition.z = cameraZPosition;
                transform.position = currentFollowPosition;


                return;
            }
        }

        // If we get here, we didn't find the local player yet
        // This is normal during the initial connection, so we'll just try again next frame
    }

    /// <summary>
    /// Desired camera XY from the followed body: horizontal is near-instant; vertical uses a
    /// deadzone band so small hops don't move the camera, easing only once the player leaves it.
    /// Z is left at the current follow Z (applied by the caller).
    /// </summary>
    private Vector3 ComputeFollowPosition()
    {
        Vector3 body = AbsorbCorrection(targetPlayer.position);

        // Horizontal: tight follow.
        float newX = Mathf.SmoothDamp(currentFollowPosition.x, body.x,
                                      ref followVelocity.x, horizontalSmoothTime);

        // Vertical: deadzone. Only chase the part of the offset outside the band.
        float dy = body.y - currentFollowPosition.y;
        float targetY = currentFollowPosition.y;
        if (Mathf.Abs(dy) > verticalDeadzone)
        {
            float overshoot = dy - Mathf.Sign(dy) * verticalDeadzone;
            targetY = currentFollowPosition.y + overshoot;
        }
        float newY = Mathf.SmoothDamp(currentFollowPosition.y, targetY,
                                      ref followVelocity.y, verticalSmoothTime);

        return new Vector3(newX, newY, cameraZPosition);
    }

    /// <summary>
    /// Detects reconciliation snaps: if the body moved faster than maxFollowSpeed this frame, the
    /// excess is moved into correctionOffset (so the followed point stays put) and then eased out
    /// over the next frames. Normal motion (including dashes) passes through untouched.
    /// </summary>
    private Vector3 AbsorbCorrection(Vector3 bodyPos)
    {
        if (!hasLastBodyPosition)
        {
            lastBodyPosition = bodyPos;
            hasLastBodyPosition = true;
        }

        float dt = Time.deltaTime;
        float maxStep = maxFollowSpeed * Mathf.Max(dt, 0.0001f);
        Vector3 delta = bodyPos - lastBodyPosition;
        if (delta.magnitude > maxStep)
        {
            // Only the portion beyond maxStep is a reconciliation snap; absorb that excess so
            // legitimate-speed motion (up to maxFollowSpeed) passes through untouched.
            correctionOffset += delta.normalized * (delta.magnitude - maxStep);
        }
        lastBodyPosition = bodyPos;

        // Ease the absorbed offset out.
        correctionOffset = Vector3.Lerp(correctionOffset, Vector3.zero, 1f - Mathf.Exp(-correctionRecoverRate * dt));

        return bodyPos - correctionOffset;
    }

    /// <summary>Clears absorb state on a legitimate teleport (respawn/snap).</summary>
    private void ResetCorrection()
    {
        correctionOffset = Vector3.zero;
        hasLastBodyPosition = false;
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
    /// Push a directional camera impulse that decays to zero over <paramref name="duration"/>.
    /// Local cosmetic feedback only.
    /// </summary>
    public void AddImpulse(Vector2 direction, float magnitude, float duration)
    {
        if (duration <= 0f || magnitude <= 0f) return;
        Vector3 d = direction.sqrMagnitude > 0.0001f ? (Vector3)direction.normalized : Vector3.zero;
        impulses.Add(new CamImpulse { dir = d, magnitude = magnitude, duration = duration, elapsed = 0f });
    }

    /// <summary>Sums + advances all active impulses, dropping expired ones. Call once per frame.</summary>
    private Vector3 EvaluateImpulses()
    {
        Vector3 sum = Vector3.zero;
        for (int i = impulses.Count - 1; i >= 0; i--)
        {
            CamImpulse imp = impulses[i];
            imp.elapsed += Time.deltaTime;
            if (imp.elapsed >= imp.duration) { impulses.RemoveAt(i); continue; }
            float t = 1f - (imp.elapsed / imp.duration); // linear decay
            sum += imp.dir * (imp.magnitude * t);
            impulses[i] = imp;
        }
        return sum;
    }

    /// <summary>
    /// Triggers a camera shake effect
    /// Call this from other scripts when the player takes damage
    /// </summary>
    public void TriggerShake()
    {
        shakeTimer = shakeDuration;
        currentShakeIntensity = shakeIntensity;

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

        // When we reach the respawn point we HOLD here (Lerp stays clamped at the target) until
        // the player actually respawns - OnPlayerRespawned() releases the hold. This prevents the
        // camera from snapping back to the still-dead body during the respawn delay.
        if (progress >= 1f)
        {
            currentFollowPosition = respawnTargetPosition;
        }
    }

    /// <summary>
    /// Called by PlayerCameraRespawnHandler when the local player respawns. Releases the respawn
    /// hold and resumes normal following from the player's (now respawned) position.
    /// </summary>
    public void OnPlayerRespawned()
    {
        isTransitioningToRespawn = false;
        followVelocity = Vector3.zero;

        if (targetPlayer != null)
        {
            currentFollowPosition = targetPlayer.position;
            currentFollowPosition.z = cameraZPosition;
        }
        else
        {
            currentFollowPosition = respawnTargetPosition;
        }
        ResetCorrection();
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
        ResetCorrection();

        // Cancel any ongoing transitions
        isTransitioningToRespawn = false;

    }

    /// <summary>
    /// Forces the camera to re-find the local player
    /// Useful if the player object changes (like after respawn)
    /// </summary>
    public void RefreshTarget()
    {
        targetPlayer = null;
        targetRigidbody = null;
        targetCombat = null;
        FindLocalPlayer();

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