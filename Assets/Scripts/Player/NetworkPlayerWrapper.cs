// 12/4/2025 AI-Tag
// This was created with the help of Assistant, a Unity Artificial Intelligence product.

using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Network wrapper for your existing player controller
/// This syncs position/state without changing your existing code
/// Attach this alongside your existing player controller
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayerWrapper : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private MonoBehaviour yourPlayerControllerScript;
    [SerializeField] private Rigidbody2D rb;

    [Header("Team Settings")]
    [SerializeField] private int teamId = 0;

    // Network variables - automatically synced
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

    // Interpolation for smooth remote player movement
    private Vector3 positionVelocity;
    private const float positionSmoothTime = 0.1f;

    // Update frequency control
    private float lastUpdateTime;
    private const float updateInterval = 0.05f; // 20 updates per second

    private void Awake()
    {
        // Auto-find components if not assigned
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();
    }

    public override void OnNetworkSpawn()
    {
        // Determine if this is the local player or remote player
        if (IsOwner)
        {
            // LOCAL PLAYER: Enable controls
            if (yourPlayerControllerScript != null)
            {
                yourPlayerControllerScript.enabled = true;
            }

            // Setup camera to follow this player
            Camera.main?.GetComponent<CameraFollow>()?.SetTarget(transform);

            Debug.Log("Local player spawned - controls enabled");
        }
        else
        {
            // REMOTE PLAYER: Disable their controller (we'll sync their position)
            if (yourPlayerControllerScript != null)
            {
                yourPlayerControllerScript.enabled = false;
            }

            Debug.Log("Remote player spawned - showing synced position");
        }

        // Server sets team ID
        if (IsServer)
        {
            networkTeamId.Value = teamId;
        }

        // Update PlayerTeamComponent with the assigned team ID
        var playerTeamComponent = GetComponent<PlayerTeamComponent>();
        if (playerTeamComponent != null)
        {
            playerTeamComponent.teamID = TeamManager.Instance.GetTeamData(networkTeamId.Value.ToString())?.teamID;
            Debug.Log($"Player team assigned: {playerTeamComponent.teamID}");
        }
        else
        {
            Debug.LogWarning("PlayerTeamComponent not found on PlayerPrefab.");
        }

        // Subscribe to position changes for remote players
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
            // LOCAL PLAYER: Send position updates to network
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
            // REMOTE PLAYER: Smoothly interpolate to synced position
            InterpolateToNetworkState();
        }
    }

    private void InterpolateToNetworkState()
    {
        // Smooth position interpolation
        transform.position = Vector3.SmoothDamp(
            transform.position,
            networkPosition.Value,
            ref positionVelocity,
            positionSmoothTime
        );

        // Apply network velocity to rigidbody for physics interactions
        if (rb != null)
        {
            rb.linearVelocity = networkVelocity.Value;
        }

        // Sync sprite flip direction
        Vector3 scale = transform.localScale;
        scale.x = networkScaleX.Value;
        transform.localScale = scale;
    }

    private void OnPositionChanged(Vector3 previousValue, Vector3 newValue)
    {
        // This fires when remote player position updates
        // You can add prediction/interpolation logic here if needed
    }

    // Team management
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

    // Prevent team damage
    private void OnCollisionEnter2D(Collision2D collision)
    {
        var otherPlayer = collision.gameObject.GetComponent<NetworkPlayerWrapper>();
        if (otherPlayer != null && networkTeamId.Value == otherPlayer.networkTeamId.Value)
        {
            // Ignore collision between teammates
            Physics2D.IgnoreCollision(
                GetComponent<Collider2D>(),
                otherPlayer.GetComponent<Collider2D>()
            );
        }
    }

    // Optional: Show network status in inspector
    private void OnGUI()
    {
        if (!IsSpawned || Camera.main == null) return;

        // Display network info above player (for debugging)
        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2);
        if (screenPos.z > 0)
        {
            string label = IsOwner ? "YOU" : "REMOTE";
            GUI.Label(new Rect(screenPos.x - 30, Screen.height - screenPos.y, 60, 20), label);
        }
    }
}