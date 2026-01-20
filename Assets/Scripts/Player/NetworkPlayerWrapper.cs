using UnityEngine;
using Fusion;

/// <summary>
/// FINAL FIX - Properly handles spawn position replication
/// The key insight: Server sets spawn position, clients receive it
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
    #endregion

    public override void Spawned()
    {
        rb = GetComponent<Rigidbody2D>();

        Debug.Log($"🌐 ========================================");
        Debug.Log($"🌐 PLAYER SPAWNED ON NETWORK");
        Debug.Log($"🌐 Time: {Time.time:F2}");
        Debug.Log($"🌐 Position: {transform.position}");
        Debug.Log($"🌐 Name: {gameObject.name}");
        Debug.Log($"🌐 HasInputAuthority: {HasInputAuthority}");
        Debug.Log($"🌐 HasStateAuthority: {HasStateAuthority}");
        Debug.Log($"🌐 GameMode: {Runner.GameMode}");
        Debug.Log($"🌐 ========================================");

        // CRITICAL: Initialize NetworkPosition immediately
        // This happens on both server and client when object spawns
        if (HasStateAuthority)
        {
            // Server: Set the initial position
            NetworkPosition = transform.position;
            Debug.Log($"✅ [SERVER] Set initial NetworkPosition: {NetworkPosition}");
        }
        else
        {
            // Client: Immediately apply the server's position
            transform.position = NetworkPosition;
            Debug.Log($"✅ [CLIENT] Applied NetworkPosition from server: {NetworkPosition}");
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
    /// Public method to force initialize position from spawn manager
    /// Called by NetworkedSpawnManager after spawning
    /// </summary>
    public void ForceInitializePosition(Vector3 position)
    {
        if (HasStateAuthority)
        {
            NetworkPosition = position;
            transform.position = position;
            Debug.Log($"🔒 [SERVER] Force initialized position to: {position}");
        }
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
        // CRITICAL FIX: Server always updates NetworkPosition
        // Client only updates if they have input authority (their own player)
        if (HasStateAuthority)
        {
            // Server: Always update NetworkPosition from transform
            NetworkPosition = transform.position;

            if (rb != null)
            {
                NetworkVelocity = rb.linearVelocity;
            }

            NetworkScaleX = transform.localScale.x;
        }
        else if (HasInputAuthority)
        {
            // Client with input authority: Update from their local transform
            // This handles client prediction
            NetworkPosition = transform.position;

            if (rb != null)
            {
                NetworkVelocity = rb.linearVelocity;
            }

            NetworkScaleX = transform.localScale.x;
        }
        // else: Remote player on client - do nothing, just receive updates
    }

    public override void Render()
    {
        // CRITICAL FIX: Apply networked position to remote players
        if (!HasInputAuthority)
        {
            // This is a remote player - apply the networked position
            transform.position = NetworkPosition;

            if (rb != null)
            {
                rb.linearVelocity = NetworkVelocity;
            }

            Vector3 scale = transform.localScale;
            scale.x = NetworkScaleX;
            transform.localScale = scale;
        }
        // else: Local player - don't override, they control their own position
    }

    private System.Collections.IEnumerator SetupCameraDelayed()
    {
        yield return new WaitForSeconds(0.1f);

        // Only setup camera for local player
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