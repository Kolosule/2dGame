using Fusion;
using UnityEngine;

/// <summary>
/// Enhanced NetworkPlayerWrapper with proper teammate collision prevention
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayerWrapper : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private MonoBehaviour yourPlayerControllerScript;
    [SerializeField] private Rigidbody2D rb;

    [Header("Team Settings")]
    [SerializeField] private int teamId = 0;

    // Networked properties - Fusion handles sync and interpolation automatically!
    [Networked] public Vector3 NetworkPosition { get; set; }
    [Networked] public Vector2 NetworkVelocity { get; set; }
    [Networked] public float NetworkScaleX { get; set; } = 1f;
    [Networked] public int NetworkTeamId { get; set; }

    private void Awake()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();
    }

    public override void Spawned()
    {
        Debug.Log($"Player spawned! HasInputAuthority: {HasInputAuthority}, Position: {transform.position}");

        // IMPORTANT: Team is set by NetworkedSpawnManager via PlayerTeamData
        // PlayerTeamData then updates PlayerTeamComponent
        // We sync from PlayerTeamComponent (which is always available)
        if (HasStateAuthority)
        {
            // Wait a frame for PlayerTeamData to update PlayerTeamComponent
            StartCoroutine(SyncTeamFromComponent());
        }

        // Assign team component immediately (both server and client)
        StartCoroutine(AssignTeamComponent());

        // Setup collision ignoring with teammates
        StartCoroutine(SetupTeammateCollisionIgnoring());

        if (HasInputAuthority)
        {
            // LOCAL PLAYER
            if (yourPlayerControllerScript != null)
            {
                yourPlayerControllerScript.enabled = true;
            }

            // Setup camera with a small delay to ensure scene is loaded
            StartCoroutine(SetupCameraDelayed());

            Debug.Log("✓ Local player controls enabled");
        }
        else
        {
            // REMOTE PLAYER
            if (yourPlayerControllerScript != null)
            {
                yourPlayerControllerScript.enabled = false;
            }

            Debug.Log("✓ Remote player - showing synced position");
        }
    }

    /// <summary>
    /// Sync NetworkTeamId from PlayerTeamComponent after PlayerTeamData updates it
    /// </summary>
    private System.Collections.IEnumerator SyncTeamFromComponent()
    {
        // Wait a frame for PlayerTeamData to set the team
        yield return null;

        PlayerTeamComponent teamComponent = GetComponent<PlayerTeamComponent>();
        if (teamComponent != null && !string.IsNullOrEmpty(teamComponent.teamID))
        {
            // Convert team name to team number (Team1 = 0, Team2 = 1)
            NetworkTeamId = teamComponent.teamID == "Team1" ? 0 : 1;
            Debug.Log($"✓ Synced NetworkTeamId from PlayerTeamComponent: {teamComponent.teamID} → {NetworkTeamId}");
        }
        else
        {
            Debug.LogWarning("⚠️ PlayerTeamComponent not found or teamID not set!");

            // Fallback: try to get from NetworkedSpawnManager
            if (NetworkedSpawnManager.Instance != null)
            {
                int assignedTeam = NetworkedSpawnManager.Instance.GetPlayerTeam(Object.InputAuthority);
                NetworkTeamId = assignedTeam - 1; // Convert from 1/2 to 0/1
                Debug.Log($"✓ Got team from NetworkedSpawnManager: Team {assignedTeam} → NetworkTeamId {NetworkTeamId}");
            }
        }
    }

    /// <summary>
    /// Setup collision ignoring with all teammates
    /// </summary>
    private System.Collections.IEnumerator SetupTeammateCollisionIgnoring()
    {
        // Wait for all players to spawn
        yield return new WaitForSeconds(0.5f);

        Collider2D myCollider = GetComponent<Collider2D>();
        if (myCollider == null)
        {
            Debug.LogWarning("NetworkPlayerWrapper: No collider found!");
            yield break;
        }

        // Find all other players
        NetworkPlayerWrapper[] allPlayers = FindObjectsByType<NetworkPlayerWrapper>(FindObjectsSortMode.None);

        foreach (NetworkPlayerWrapper otherPlayer in allPlayers)
        {
            if (otherPlayer == this) continue; // Skip self

            // Check if same team
            if (NetworkTeamId == otherPlayer.NetworkTeamId)
            {
                Collider2D otherCollider = otherPlayer.GetComponent<Collider2D>();
                if (otherCollider != null)
                {
                    // Ignore collision between teammates
                    Physics2D.IgnoreCollision(myCollider, otherCollider, true);
                    Debug.Log($"Ignoring collision between teammates: {gameObject.name} <-> {otherPlayer.gameObject.name}");
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
        }
    }

    public override void Render()
    {
        // Apply networked state to visuals for remote players
        if (!HasInputAuthority)
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
    }

    public void SetTeam(int team)
    {
        teamId = team;
        if (HasStateAuthority)
        {
            NetworkTeamId = team;
        }
    }

    public int GetTeam()
    {
        return NetworkTeamId;
    }

    /// <summary>
    /// Called when colliding with another object
    /// Ensures teammates don't collide
    /// </summary>
    private void OnCollisionEnter2D(Collision2D collision)
    {
        var otherPlayer = collision.gameObject.GetComponent<NetworkPlayerWrapper>();
        if (otherPlayer != null && NetworkTeamId == otherPlayer.NetworkTeamId)
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
                Debug.Log("✓ Camera setup complete");
                yield break;
            }

            attempts++;
            yield return new WaitForSeconds(0.1f);
        }

        Debug.LogWarning("⚠️ Could not find CameraFollow script after multiple attempts");
    }

    /// <summary>
    /// Assign team component with retry logic
    /// </summary>
    private System.Collections.IEnumerator AssignTeamComponent()
    {
        yield return null;

        int attempts = 0;
        int maxAttempts = 10;

        while (attempts < maxAttempts)
        {
            var teamComponent = GetComponent<PlayerTeamComponent>();
            if (teamComponent != null)
            {
                // Convert team ID (0 or 1) to team name ("Team1" or "Team2")
                string teamName = NetworkTeamId == 0 ? "Team1" : "Team2";

                Debug.Log($"✓ Team component assigned: {teamName}");
                yield break;
            }

            attempts++;
            yield return new WaitForSeconds(0.1f);
        }

        Debug.LogWarning("⚠️ Could not find PlayerTeamComponent after multiple attempts");
    }
}