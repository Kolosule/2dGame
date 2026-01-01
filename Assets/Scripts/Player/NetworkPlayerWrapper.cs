using Fusion;
using UnityEngine;

/// <summary>
/// FINAL FIX - NetworkPlayerWrapper that properly preserves spawn position
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayerWrapper : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private MonoBehaviour yourPlayerControllerScript;
    [SerializeField] private Rigidbody2D rb;

    // Networked properties - Fusion handles sync and interpolation automatically!
    [Networked] public Vector3 NetworkPosition { get; set; }
    [Networked] public Vector2 NetworkVelocity { get; set; }
    [Networked] public float NetworkScaleX { get; set; } = 1f;
    [Networked] public int NetworkTeamId { get; set; }

    // CRITICAL FIX: Track if we've initialized network position
    private bool networkPositionInitialized = false;

    private void Awake()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();
    }

    public override void Spawned()
    {
        Debug.Log($"🎮 Player spawned! HasInputAuthority: {HasInputAuthority}, Position: {transform.position}");

        // CRITICAL FIX: Initialize NetworkPosition to current spawn position
        if (HasStateAuthority)
        {
            NetworkPosition = transform.position;
            networkPositionInitialized = true;
            Debug.Log($"✅ [SERVER] Initialized NetworkPosition to spawn position: {NetworkPosition}");
        }

        if (HasInputAuthority)
        {
            // LOCAL PLAYER
            if (yourPlayerControllerScript != null)
            {
                yourPlayerControllerScript.enabled = true;
            }

            // Setup camera with a small delay to ensure scene is loaded
            StartCoroutine(SetupCameraDelayed());

            Debug.Log("✅ Local player controls enabled");
        }
        else
        {
            // REMOTE PLAYER
            if (yourPlayerControllerScript != null)
            {
                yourPlayerControllerScript.enabled = false;
            }

            Debug.Log("✅ Remote player - showing synced position");
        }

        // Setup collision ignoring with teammates (after a small delay)
        StartCoroutine(SetupTeammateCollisionIgnoring());

        // Sync NetworkTeamId from PlayerTeamData (server only, with delay)
        if (HasStateAuthority)
        {
            StartCoroutine(SyncNetworkTeamId());
        }
    }

    /// <summary>
    /// Sync NetworkTeamId from PlayerTeamData after it's been set by the server
    /// </summary>
    private System.Collections.IEnumerator SyncNetworkTeamId()
    {
        // Wait for PlayerTeamData to be set by NetworkedSpawnManager
        yield return new WaitForSeconds(0.1f);

        PlayerTeamData teamData = GetComponent<PlayerTeamData>();
        if (teamData != null && teamData.Team != 0)
        {
            // Convert from 1/2 to 0/1 for backwards compatibility
            NetworkTeamId = teamData.Team - 1;
            Debug.Log($"✅ Synced NetworkTeamId: Team {teamData.Team} → NetworkTeamId {NetworkTeamId}");
        }
        else
        {
            Debug.LogWarning("⚠️ Could not sync NetworkTeamId - PlayerTeamData not set yet");
        }
    }

    /// <summary>
    /// Setup collision ignoring with all teammates
    /// </summary>
    private System.Collections.IEnumerator SetupTeammateCollisionIgnoring()
    {
        // Wait for all players to spawn and teams to be assigned
        yield return new WaitForSeconds(0.5f);

        Collider2D myCollider = GetComponent<Collider2D>();
        if (myCollider == null)
        {
            Debug.LogWarning("NetworkPlayerWrapper: No collider found!");
            yield break;
        }

        // Get our team
        PlayerTeamData myTeamData = GetComponent<PlayerTeamData>();
        if (myTeamData == null || myTeamData.Team == 0)
        {
            Debug.LogWarning("NetworkPlayerWrapper: Team not assigned yet!");
            yield break;
        }

        // Find all other players
        NetworkPlayerWrapper[] allPlayers = FindObjectsByType<NetworkPlayerWrapper>(FindObjectsSortMode.None);

        foreach (NetworkPlayerWrapper otherPlayer in allPlayers)
        {
            if (otherPlayer == this) continue; // Skip self

            // Get other player's team
            PlayerTeamData otherTeamData = otherPlayer.GetComponent<PlayerTeamData>();
            if (otherTeamData == null) continue;

            // Check if same team
            if (myTeamData.Team == otherTeamData.Team)
            {
                Collider2D otherCollider = otherPlayer.GetComponent<Collider2D>();
                if (otherCollider != null)
                {
                    // Ignore collision between teammates
                    Physics2D.IgnoreCollision(myCollider, otherCollider, true);
                    Debug.Log($"🤝 Ignoring collision between teammates: {gameObject.name} <-> {otherPlayer.gameObject.name}");
                }
            }
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        // Cleanup if needed
    }

    public override void FixedUpdateNetwork()
    {
        // Physics-based movement input handling
        if (HasInputAuthority)
        {
            // Update networked properties - Fusion automatically syncs and interpolates!
            NetworkPosition = transform.position;
            NetworkVelocity = rb.linearVelocity;
            NetworkScaleX = transform.localScale.x;

            // Mark as initialized
            if (!networkPositionInitialized)
            {
                networkPositionInitialized = true;
            }
        }
    }

    public override void Render()
    {
        // CRITICAL FIX: Only apply networked position to remote players AFTER it's been initialized
        if (!HasInputAuthority && networkPositionInitialized)
        {
            transform.position = NetworkPosition;

            if (rb != null)
            {
                rb.linearVelocity = NetworkVelocity;
            }

            Vector3 scale = transform.localScale;
            scale.x = NetworkScaleX;
            transform.localScale = scale;
        }
        // If not initialized yet, keep the spawn position set by the server
    }

    /// <summary>
    /// Setup camera with retry logic
    /// </summary>
    private System.Collections.IEnumerator SetupCameraDelayed()
    {
        // Wait one frame to ensure everything is loaded
        yield return null;

        int attempts = 0;
        int maxAttempts = 10;

        while (attempts < maxAttempts)
        {
            var cameraFollow = FindFirstObjectByType<CameraFollow>();
            if (cameraFollow != null)
            {
                cameraFollow.SetTarget(transform);
                Debug.Log("✅ Camera setup complete");
                yield break;
            }

            attempts++;
            yield return new WaitForSeconds(0.1f);
        }

        Debug.LogWarning("⚠️ Could not find CameraFollow script after multiple attempts");
    }

    /// <summary>
    /// Get the team this player is on (1 or 2)
    /// </summary>
    public int GetTeam()
    {
        PlayerTeamData teamData = GetComponent<PlayerTeamData>();
        if (teamData != null)
        {
            return teamData.Team;
        }
        return 0; // No team
    }

    /// <summary>
    /// Called when colliding with another object
    /// Ensures teammates don't collide (backup safety check)
    /// </summary>
    private void OnCollisionEnter2D(Collision2D collision)
    {
        var otherPlayer = collision.gameObject.GetComponent<NetworkPlayerWrapper>();
        if (otherPlayer != null)
        {
            // Check if same team
            PlayerTeamData myTeamData = GetComponent<PlayerTeamData>();
            PlayerTeamData otherTeamData = otherPlayer.GetComponent<PlayerTeamData>();

            if (myTeamData != null && otherTeamData != null &&
                myTeamData.Team == otherTeamData.Team)
            {
                // Same team - ignore collision
                Collider2D myCollider = GetComponent<Collider2D>();
                Collider2D otherCollider = otherPlayer.GetComponent<Collider2D>();

                if (myCollider != null && otherCollider != null)
                {
                    Physics2D.IgnoreCollision(myCollider, otherCollider, true);
                    Debug.Log("OnCollisionEnter2D: Ignoring teammate collision");
                }
            }
        }
    }
}