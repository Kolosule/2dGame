using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayerWrapper : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private MonoBehaviour yourPlayerControllerScript;
    [SerializeField] private Rigidbody2D rb;

    [Header("Team Settings")]
    [SerializeField] private int teamId = 0;

    // Network variables
    private NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private NetworkVariable<Vector2> networkVelocity = new NetworkVariable<Vector2>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private NetworkVariable<float> networkScaleX = new NetworkVariable<float>(
        1f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private NetworkVariable<int> networkTeamId = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Vector3 positionVelocity;
    private const float positionSmoothTime = 0.1f;
    private float lastUpdateTime;
    private const float updateInterval = 0.05f;

    private void Awake()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log($"Player spawned! IsOwner: {IsOwner}, Position: {transform.position}");

        // SERVER: Assign team immediately based on what was decided in ApprovalCheck
        if (IsServer)
        {
            if (NetworkedSpawnManager.Instance != null)
            {
                // Get the team that was assigned during connection approval
                int assignedTeam = NetworkedSpawnManager.Instance.GetTeamForClient(OwnerClientId);
                networkTeamId.Value = assignedTeam;
                Debug.Log($"✓ Server assigned team ID {assignedTeam} to client {OwnerClientId}");
            }
            else
            {
                Debug.LogError("⚠️ NetworkedSpawnManager.Instance is null on server!");
                networkTeamId.Value = 0;
            }
        }

        // Assign team component immediately (both server and client)
        StartCoroutine(AssignTeamComponent());

        if (IsOwner)
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

        if (!IsOwner)
        {
            networkPosition.OnValueChanged += OnPositionChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner)
        {
            networkPosition.OnValueChanged -= OnPositionChanged;
        }
    }

    private void Update()
    {
        if (IsOwner)
        {
            // Send position updates
            if (Time.time - lastUpdateTime >= updateInterval)
            {
                networkPosition.Value = transform.position;
                networkVelocity.Value = rb.linearVelocity;
                networkScaleX.Value = transform.localScale.x;
                lastUpdateTime = Time.time;
            }
        }
        else
        {
            // Interpolate to synced position
            InterpolateToNetworkState();
        }
    }

    private void InterpolateToNetworkState()
    {
        transform.position = Vector3.SmoothDamp(
            transform.position,
            networkPosition.Value,
            ref positionVelocity,
            positionSmoothTime
        );

        if (rb != null)
        {
            rb.linearVelocity = networkVelocity.Value;
        }

        Vector3 scale = transform.localScale;
        scale.x = networkScaleX.Value;
        transform.localScale = scale;
    }

    private void OnPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        // Position updated
    }

    public void SetTeam(int team)
    {
        teamId = team;
        if (IsServer)
        {
            networkTeamId.Value = team;
        }
    }

    public int GetTeam()
    {
        return networkTeamId.Value;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        var otherPlayer = collision.gameObject.GetComponent<NetworkPlayerWrapper>();
        if (otherPlayer != null && networkTeamId.Value == otherPlayer.networkTeamId.Value)
        {
            Physics2D.IgnoreCollision(
                GetComponent<Collider2D>(),
                otherPlayer.GetComponent<Collider2D>()
            );
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
        bool cameraSet = false;

        while (!cameraSet && attempts < maxAttempts)
        {
            attempts++;

            // Method 1: Use spawn manager
            if (NetworkedSpawnManager.Instance != null && NetworkedSpawnManager.Instance.cameraFollow != null)
            {
                NetworkedSpawnManager.Instance.SetupPlayerCamera(gameObject);
                cameraSet = true;
                Debug.Log("✓ Camera setup via SpawnManager");
                break;
            }

            // Method 2: Find camera directly
            CameraFollow cam = FindObjectOfType<CameraFollow>();
            if (cam != null)
            {
                cam.SetTarget(transform);
                Debug.Log("✓ Camera setup via FindObjectOfType");
                cameraSet = true;
                break;
            }

            // Method 3: Find by tag
            GameObject mainCam = GameObject.FindGameObjectWithTag("MainCamera");
            if (mainCam != null)
            {
                cam = mainCam.GetComponent<CameraFollow>();
                if (cam != null)
                {
                    cam.SetTarget(transform);
                    Debug.Log("✓ Camera setup via tag search");
                    cameraSet = true;
                    break;
                }
            }

            // Wait a bit before retrying
            Debug.LogWarning($"Camera not found, retry attempt {attempts}/{maxAttempts}");
            yield return new WaitForSeconds(0.1f);
        }

        if (!cameraSet)
        {
            Debug.LogError("⚠️ FAILED TO SETUP CAMERA after " + maxAttempts + " attempts!");
            Debug.LogError("Make sure your Gameplay scene has a Main Camera with the CameraFollow script!");
        }
    }

    /// <summary>
    /// Assign team to PlayerTeamComponent
    /// </summary>
    private System.Collections.IEnumerator AssignTeamComponent()
    {
        // Wait for network team ID to be set (clients need to wait for server)
        int waitCount = 0;
        while (networkTeamId.Value == teamId && !IsServer && waitCount < 20)
        {
            yield return new WaitForSeconds(0.1f);
            waitCount++;
        }

        var playerTeamComponent = GetComponent<PlayerTeamComponent>();
        if (playerTeamComponent == null)
        {
            Debug.LogError("⚠️ PlayerTeamComponent not found on player!");
            yield break;
        }

        if (TeamManager.Instance == null)
        {
            Debug.LogError("⚠️ TeamManager not found in scene!");
            yield break;
        }

        // Convert team ID (0 or 1) to team name ("Team1" or "Team2")
        string teamName = networkTeamId.Value == 0 ? "Team1" : "Team2";

        TeamData teamData = TeamManager.Instance.GetTeamData(teamName);
        if (teamData != null)
        {
            playerTeamComponent.teamID = teamData.teamID;
            Debug.Log($"✓ Player at {transform.position} assigned to {teamData.teamName} (ID: {playerTeamComponent.teamID})");
        }
        else
        {
            Debug.LogError($"⚠️ Failed to find team data for {teamName}");
        }
    }
}