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
/// - Reads the player's team from the networked PlayerTeamData
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
        // Only the LOCAL player's handler may drive the (single) gameplay camera. Without this,
        // every player's handler instance on this client would hijack the camera when ANY player
        // died.
        if (statsHandler == null || statsHandler.Object == null || !statsHandler.Object.HasInputAuthority)
            return;

        // Try to find camera if we don't have it yet
        if (playerCamera == null)
        {
            FindPlayerCamera();
            if (playerCamera == null)
                return;
        }

        bool dead = statsHandler.IsPlayerDead();

        // Check if player just died
        if (!isDead && dead)
        {
            isDead = true;
            hasTriggeredRespawnTransition = false;

            // Start the death sequence (transitions the camera to the chosen respawn point).
            StartCoroutine(HandleDeathCameraSequence());
        }
        // Check if player respawned
        else if (isDead && !dead)
        {
            isDead = false;

            // The body is now at RespawnPosition (where the camera already transitioned); release
            // the hold and resume following so it stays put rather than snapping.
            playerCamera.OnPlayerRespawned();
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
        // Prefer the authoritative point chosen at death, so the camera lands exactly where the
        // player will respawn (instead of an independent random spawn pick).
        if (statsHandler != null && statsHandler.RespawnPosition != Vector3.zero)
            return statsHandler.RespawnPosition;

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