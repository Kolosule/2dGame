using UnityEngine;
using System.Collections;

/// <summary>
/// FIXED VERSION - Connects the camera system to player respawn events.
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
/// 
/// WHAT CHANGED:
/// - Now uses PlayerTeamData (which stores team as an int: 1 or 2)
/// - Falls back to PlayerTeamComponent if PlayerTeamData is missing
/// - Compatible with the fixed NetworkedSpawnManager
/// - FIXED LINE 160: String-to-int conversion for GetSpawnPosition()
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


            // Start the death sequence
            StartCoroutine(HandleDeathCameraSequence());
        }

        // Check if player respawned
        if (isDead && statsHandler.GetCurrentHealth() > 0)
        {
            // Player respawned
            isDead = false;

        }
    }

    /// <summary>
    /// Finds the PlayerCamera in the scene
    /// </summary>
    private void FindPlayerCamera()
    {
        playerCamera = FindFirstObjectByType<PlayerCamera>();

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


        // Trigger camera transition
        if (playerCamera != null)
        {
            playerCamera.StartRespawnTransition(respawnPosition);
        }
    }

    /// <summary>
    /// FIXED: Gets the respawn position for this player using team number (int)
    /// NOW PROPERLY CONVERTS STRING → INT
    /// </summary>
    private Vector3 GetRespawnPosition()
    {
        if (NetworkedSpawnManager.Instance != null)
        {
            PlayerTeamData teamData = GetComponent<PlayerTeamData>();
            if (teamData != null && teamData.Team != Team.None)
            {
                Vector3 spawnPos = NetworkedSpawnManager.Instance.GetSpawnPosition(TeamUtil.ToNumber(teamData.Team));
                return spawnPos;
            }
        }

        if (showDebugMessages)
        {
            Debug.LogWarning("⚠️ Could not resolve respawn position - using current position");
        }
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