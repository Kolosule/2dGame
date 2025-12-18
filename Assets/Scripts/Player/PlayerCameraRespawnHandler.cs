using UnityEngine;
using System.Collections;

/// <summary>
/// Connects the camera system to player respawn events.
/// This script triggers smooth camera transitions when the player dies and respawns.
/// 
/// SETUP INSTRUCTIONS:
/// 1. Attach this script to your Player prefab (same GameObject with PlayerStatsHandler)
/// 2. The script will automatically handle camera transitions during respawn
/// 3. No additional setup needed!
/// 
/// HOW IT WORKS:
/// - Detects when the player dies (health reaches 0)
/// - Triggers a smooth camera transition to the respawn point
/// - Camera arrives at respawn point before/as player respawns
/// </summary>
public class PlayerCameraRespawnHandler : MonoBehaviour
{
    [Header("⚙️ Respawn Settings")]
    [Tooltip("Delay before starting camera transition after death (in seconds)")]
    [SerializeField] private float deathToCameraTransitionDelay = 0.5f;

    [Tooltip("Should camera follow player's falling body briefly before transitioning?")]
    [SerializeField] private bool followDuringDeathDelay = true;

    [Header("🔧 Debug")]
    [Tooltip("Show debug messages in console")]
    [SerializeField] private bool showDebugMessages = false;

    // Internal variables
    private PlayerStatsHandler statsHandler;
    private PlayerCamera playerCamera;
    private bool isDead = false;
    private bool hasTriggeredRespawnTransition = false;

    /// <summary>
    /// Called when the script starts
    /// </summary>
    private void Start()
    {
        // Get the PlayerStatsHandler component
        statsHandler = GetComponent<PlayerStatsHandler>();

        if (statsHandler == null)
        {
            Debug.LogError("❌ PlayerCameraRespawnHandler: No PlayerStatsHandler found!");
            enabled = false;
            return;
        }

        if (showDebugMessages)
        {
            Debug.Log("✓ PlayerCameraRespawnHandler initialized");
        }
    }

    /// <summary>
    /// Called every frame - checks for death and respawn
    /// </summary>
    private void Update()
    {
        // Try to find camera if we don't have it yet
        if (playerCamera == null)
        {
            FindPlayerCamera();
            if (playerCamera == null)
                return;
        }

        // Check if player just died
        if (!isDead && statsHandler.GetCurrentHealth() <= 0)
        {
            // Player just died
            isDead = true;
            hasTriggeredRespawnTransition = false;

            if (showDebugMessages)
            {
                Debug.Log("💀 Player died - preparing camera transition");
            }

            // Start the death sequence
            StartCoroutine(HandleDeathCameraSequence());
        }

        // Check if player respawned
        if (isDead && statsHandler.GetCurrentHealth() > 0)
        {
            // Player respawned
            isDead = false;

            if (showDebugMessages)
            {
                Debug.Log("✨ Player respawned - camera transition complete");
            }
        }
    }

    /// <summary>
    /// Finds the PlayerCamera in the scene
    /// </summary>
    private void FindPlayerCamera()
    {
        playerCamera = FindObjectOfType<PlayerCamera>();

        if (playerCamera != null && showDebugMessages)
        {
            Debug.Log("✓ Found PlayerCamera");
        }
    }

    /// <summary>
    /// Handles the camera sequence when player dies
    /// </summary>
    private IEnumerator HandleDeathCameraSequence()
    {
        // Optional: Follow player during death delay (falling animation, etc.)
        if (followDuringDeathDelay && deathToCameraTransitionDelay > 0)
        {
            yield return new WaitForSeconds(deathToCameraTransitionDelay);
        }

        // Check if we already triggered transition (in case of multiple death events)
        if (hasTriggeredRespawnTransition)
            yield break;

        hasTriggeredRespawnTransition = true;

        // Get respawn position from NetworkedSpawnManager
        Vector3 respawnPosition = GetRespawnPosition();

        if (showDebugMessages)
        {
            Debug.Log($"🎬 Starting camera transition to respawn point: {respawnPosition}");
        }

        // Trigger camera transition
        if (playerCamera != null)
        {
            playerCamera.StartRespawnTransition(respawnPosition);
        }
    }

    /// <summary>
    /// Gets the respawn position for this player
    /// </summary>
    private Vector3 GetRespawnPosition()
    {
        // Try to get respawn position from NetworkedSpawnManager
        if (NetworkedSpawnManager.Instance != null)
        {
            // Refresh spawn points to ensure we have latest data
            NetworkedSpawnManager.Instance.RefreshSpawnPoints();

            // Get player's team from their team component
            PlayerTeamComponent teamComponent = GetComponent<PlayerTeamComponent>();

            if (teamComponent != null)
            {
                string teamId = teamComponent.teamID; // Access the public teamID field directly
                Vector3 spawnPos = NetworkedSpawnManager.Instance.GetSpawnPosition(teamId);

                if (showDebugMessages)
                {
                    Debug.Log($"✓ Got respawn position from NetworkedSpawnManager: {spawnPos}");
                }

                return spawnPos;
            }
        }

        // Fallback: return current position if we can't get spawn point
        Debug.LogWarning("⚠️ Could not get respawn position from NetworkedSpawnManager, using current position");
        return transform.position;
    }

    /// <summary>
    /// Manually trigger respawn transition (can be called from other scripts)
    /// </summary>
    public void TriggerRespawnTransition(Vector3 respawnPosition)
    {
        if (playerCamera == null)
        {
            FindPlayerCamera();
        }

        if (playerCamera != null)
        {
            playerCamera.StartRespawnTransition(respawnPosition);

            if (showDebugMessages)
            {
                Debug.Log($"🎬 Manual respawn transition triggered to {respawnPosition}");
            }
        }
    }

    /// <summary>
    /// Get reference to the player camera (for other scripts)
    /// </summary>
    public PlayerCamera GetPlayerCamera()
    {
        if (playerCamera == null)
        {
            FindPlayerCamera();
        }
        return playerCamera;
    }
}