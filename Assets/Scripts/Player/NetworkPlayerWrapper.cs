using UnityEngine;
using Fusion;

/// <summary>
/// FIXED VERSION - Properly handles spawn position in Shared Mode
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class NetworkPlayerWrapper : NetworkBehaviour
{
    #region Networked Properties
    [Networked] public Vector3 NetworkPosition { get; set; }
    [Networked] public Vector2 NetworkVelocity { get; set; }
    [Networked] public float NetworkScaleX { get; set; }
    #endregion

    #region Private Fields
    private Rigidbody2D rb;
    private bool networkPositionInitialized = false;
    private bool spawnPositionLocked = false;
    private Vector3 initialSpawnPosition;
    #endregion

    public override void Spawned()
    {
        rb = GetComponent<Rigidbody2D>();

        // CRITICAL FIX: Lock the spawn position
        initialSpawnPosition = transform.position;
        spawnPositionLocked = true;

        Debug.Log($"🌐 ========================================");
        Debug.Log($"🌐 PLAYER SPAWNED ON NETWORK");
        Debug.Log($"🌐 Time: {Time.time:F2}");
        Debug.Log($"🌐 Initial Position: {initialSpawnPosition}");
        Debug.Log($"🌐 Name: {gameObject.name}");
        Debug.Log($"🌐 HasInputAuthority: {HasInputAuthority}");
        Debug.Log($"🌐 HasStateAuthority: {HasStateAuthority}");
        Debug.Log($"🌐 GameMode: {Runner.GameMode}");
        Debug.Log($"🌐 ========================================");

        // Check if spawning at origin (this would indicate a problem)
        if (initialSpawnPosition == Vector3.zero || initialSpawnPosition.magnitude < 1f)
        {
            Debug.LogError("⚠️ PLAYER SPAWNED AT ORIGIN OR NEAR ORIGIN!");
            Debug.LogError("This suggests spawn position wasn't set correctly!");
        }

        // Initialize networked position immediately
        if (HasInputAuthority)
        {
            NetworkPosition = initialSpawnPosition;
            networkPositionInitialized = true;
        }

        // Wait a frame before setting up camera and collisions
        StartCoroutine(SetupCameraDelayed());
        StartCoroutine(SetupTeammateCollisions());

        // Validate team assignment
        PlayerTeamData teamData = GetComponent<PlayerTeamData>();
        if (teamData != null)
        {
            Debug.Log($"PlayerTeamData.Team: {teamData.Team}");
            if (teamData.Team == 0)
            {
                Debug.LogError("⚠️ Team is 0! Team assignment hasn't happened yet!");
            }
        }
        else
        {
            Debug.LogError("❌ NO PlayerTeamData component!");
        }
    }

    /// <summary>
    /// CRITICAL FIX: Public method to force initialize position from spawn manager
    /// </summary>
    public void ForceInitializePosition(Vector3 position)
    {
        initialSpawnPosition = position;
        transform.position = position;

        if (HasInputAuthority)
        {
            NetworkPosition = position;
            networkPositionInitialized = true;
        }

        Debug.Log($"🔒 Force initialized position to: {position}");
    }

    private System.Collections.IEnumerator SetupTeammateCollisions()
    {
        yield return new WaitForSeconds(0.2f);

        Collider2D myCollider = GetComponent<Collider2D>();
        if (myCollider == null)
        {
            Debug.LogWarning("NetworkPlayerWrapper: No Collider2D found!");
            yield break;
        }

        PlayerTeamData myTeamData = GetComponent<PlayerTeamData>();
        if (myTeamData == null || myTeamData.Team == 0)
        {
            Debug.LogWarning("NetworkPlayerWrapper: Team not assigned yet!");
            yield break;
        }

        NetworkPlayerWrapper[] allPlayers = FindObjectsByType<NetworkPlayerWrapper>(FindObjectsSortMode.None);

        foreach (NetworkPlayerWrapper otherPlayer in allPlayers)
        {
            if (otherPlayer == this) continue;

            PlayerTeamData otherTeamData = otherPlayer.GetComponent<PlayerTeamData>();
            if (otherTeamData == null) continue;

            if (myTeamData.Team == otherTeamData.Team)
            {
                Collider2D otherCollider = otherPlayer.GetComponent<Collider2D>();
                if (otherCollider != null)
                {
                    Physics2D.IgnoreCollision(myCollider, otherCollider, true);
                    Debug.Log($"🤝 Ignoring collision between teammates");
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
            // CRITICAL FIX: Don't update position until spawn lock is released
            if (!spawnPositionLocked)
            {
                NetworkPosition = transform.position;
                NetworkVelocity = rb.linearVelocity;
                NetworkScaleX = transform.localScale.x;
            }
            else
            {
                // Release the spawn lock after a short delay
                if (Time.time > 0.1f)
                {
                    spawnPositionLocked = false;
                    networkPositionInitialized = true;
                    Debug.Log("🔓 Spawn position lock released");
                }
            }
        }
    }

    public override void Render()
    {
        // CRITICAL FIX: Only apply networked position to remote players AFTER initialization
        // AND respect the spawn position lock
        if (!HasInputAuthority && networkPositionInitialized && !spawnPositionLocked)
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
        // If spawn position is locked, maintain the spawn position
        else if (spawnPositionLocked)
        {
            transform.position = initialSpawnPosition;
        }
    }

    private System.Collections.IEnumerator SetupCameraDelayed()
    {
        yield return new WaitForSeconds(0.1f);

        if (!HasInputAuthority) yield break;

        CameraFollow cam = FindFirstObjectByType<CameraFollow>();
        if (cam != null)
        {
            cam.SetTarget(transform);
            Debug.Log("✅ Camera locked to player");
        }
        else
        {
            Debug.LogWarning("⚠️ No CameraFollow found!");
        }
    }
}