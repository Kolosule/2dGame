using Unity.Netcode;
using UnityEngine;
using System.Collections;

/// <summary>
/// Manages flag state and interactions for Capture the Flag mode
/// Attach this to each team's flag GameObject
/// </summary>
public class Flag : NetworkBehaviour
{
    [Header("Flag Configuration")]
    [Tooltip("Which team owns this flag (Team1 or Team2)")]
    [SerializeField] private string owningTeam = "Team1";

    [Header("Flag Visuals")]
    [Tooltip("The sprite renderer for the flag")]
    [SerializeField] private SpriteRenderer flagSprite;

    [Tooltip("Particle effect when flag is picked up")]
    [SerializeField] private ParticleSystem pickupEffect;

    [Tooltip("Particle effect when flag is dropped")]
    [SerializeField] private ParticleSystem dropEffect;

    [Header("Flag Settings")]
    [Tooltip("Time in seconds before dropped flag auto-returns")]
    [SerializeField] private float autoReturnTime = 15f;

    [Tooltip("Height offset when flag is carried above player")]
    [SerializeField] private float carrierOffset = 1.5f;

    [Header("Collision")]
    [Tooltip("Trigger collider for flag pickup")]
    [SerializeField] private Collider2D triggerCollider;

    // Network variables to sync flag state across all clients
    private NetworkVariable<FlagState> currentState = new NetworkVariable<FlagState>(
        FlagState.AtHome,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<ulong> carrierClientId = new NetworkVariable<ulong>(
        ulong.MaxValue, // MaxValue = no carrier
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<Vector3> homePosition = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Local references
    private GameObject carrier;
    private Coroutine autoReturnCoroutine;
    private Vector3 dropPosition;

    // Flag states
    public enum FlagState
    {
        AtHome,     // Flag is at home base
        Carried,    // Flag is being carried by a player
        Dropped     // Flag is dropped on the ground
    }

    // Public properties
    public string OwningTeam => owningTeam;
    public FlagState State => currentState.Value;
    public GameObject Carrier => carrier;

    private void Awake()
    {
        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider2D>();

        if (triggerCollider != null)
            triggerCollider.isTrigger = true;

        if (flagSprite == null)
            flagSprite = GetComponent<SpriteRenderer>();
    }

    public override void OnNetworkSpawn()
    {
        // Server sets the home position when flag spawns
        if (IsServer)
        {
            homePosition.Value = transform.position;
            currentState.Value = FlagState.AtHome;
            Debug.Log($"✓ {owningTeam} flag spawned at {homePosition.Value}");
        }

        // Subscribe to state changes on all clients
        currentState.OnValueChanged += OnStateChanged;
        carrierClientId.OnValueChanged += OnCarrierChanged;
    }

    public override void OnNetworkDespawn()
    {
        currentState.OnValueChanged -= OnStateChanged;
        carrierClientId.OnValueChanged -= OnCarrierChanged;
    }

    private void Update()
    {
        // If flag is being carried, follow the carrier
        if (currentState.Value == FlagState.Carried && carrier != null)
        {
            transform.position = carrier.transform.position + Vector3.up * carrierOffset;
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // Only process on server
        if (!IsServer) return;

        // Get player components
        GameObject player = collision.gameObject;
        PlayerTeamComponent playerTeam = player.GetComponent<PlayerTeamComponent>();
        NetworkObject playerNetworkObject = player.GetComponent<NetworkObject>();

        if (playerTeam == null || playerNetworkObject == null)
            return;

        // Handle different flag states
        switch (currentState.Value)
        {
            case FlagState.AtHome:
                // Enemy team can pick up flag from home
                if (playerTeam.teamID != owningTeam)
                {
                    PickupFlag(player, playerNetworkObject.OwnerClientId);
                }
                break;

            case FlagState.Dropped:
                // Anyone can pick up dropped flag
                PickupFlag(player, playerNetworkObject.OwnerClientId);
                break;

            case FlagState.Carried:
                // Carried flag checks are handled by CTFGameManager
                // based on position, not triggers
                break;
        }
    }

    /// <summary>
    /// SERVER: Pick up the flag
    /// </summary>
    private void PickupFlag(GameObject player, ulong clientId)
    {
        if (!IsServer) return;

        // Cancel auto-return if it was running
        if (autoReturnCoroutine != null)
        {
            StopCoroutine(autoReturnCoroutine);
            autoReturnCoroutine = null;
        }

        // Set flag state
        currentState.Value = FlagState.Carried;
        carrierClientId.Value = clientId;
        carrier = player;

        // Disable player's dash
        PlayerMovement movement = player.GetComponent<PlayerMovement>();
        if (movement != null)
        {
            movement.enabled = false; // Will re-enable when flag is dropped
            movement.enabled = true;
            // Set a flag carrier marker
            player.GetComponent<FlagCarrierMarker>()?.SetCarryingFlag(true);
        }

        // Play pickup effect
        if (pickupEffect != null)
            pickupEffect.Play();

        // Notify clients
        PlayerTeamComponent playerTeam = player.GetComponent<PlayerTeamComponent>();
        string notification = playerTeam.teamID != owningTeam
            ? $"{GetTeamDisplayName(playerTeam.teamID)} team has captured {GetTeamDisplayName(owningTeam)}'s flag!"
            : $"{GetTeamDisplayName(owningTeam)} team is returning their flag!";

        CTFGameManager.Instance?.ShowNotificationClientRpc(notification);

        Debug.Log($"✓ {owningTeam} flag picked up by {player.name} (Client {clientId})");
    }

    /// <summary>
    /// SERVER: Drop the flag at current position
    /// </summary>
    public void DropFlag()
    {
        if (!IsServer) return;
        if (currentState.Value != FlagState.Carried) return;

        // Save drop position
        dropPosition = transform.position;

        // Clear carrier
        if (carrier != null)
        {
            // Re-enable dash
            carrier.GetComponent<FlagCarrierMarker>()?.SetCarryingFlag(false);
            carrier = null;
        }

        // Update state
        currentState.Value = FlagState.Dropped;
        carrierClientId.Value = ulong.MaxValue;

        // Play drop effect
        if (dropEffect != null)
            dropEffect.Play();

        // Start auto-return timer
        autoReturnCoroutine = StartCoroutine(AutoReturnTimer());

        // Notify clients
        CTFGameManager.Instance?.ShowNotificationClientRpc(
            $"{GetTeamDisplayName(owningTeam)} flag has been dropped!"
        );

        Debug.Log($"✓ {owningTeam} flag dropped at {dropPosition}");
    }

    /// <summary>
    /// SERVER: Return flag to home base
    /// </summary>
    public void ReturnFlag()
    {
        if (!IsServer) return;

        // Cancel auto-return timer
        if (autoReturnCoroutine != null)
        {
            StopCoroutine(autoReturnCoroutine);
            autoReturnCoroutine = null;
        }

        // Clear carrier if returning while carried
        if (carrier != null)
        {
            carrier.GetComponent<FlagCarrierMarker>()?.SetCarryingFlag(false);
            carrier = null;
        }

        // Reset position and state
        transform.position = homePosition.Value;
        currentState.Value = FlagState.AtHome;
        carrierClientId.Value = ulong.MaxValue;

        // Notify clients
        CTFGameManager.Instance?.ShowNotificationClientRpc(
            $"{GetTeamDisplayName(owningTeam)} flag has been returned!"
        );

        Debug.Log($"✓ {owningTeam} flag returned to home");
    }

    /// <summary>
    /// Auto-return timer for dropped flags
    /// </summary>
    private IEnumerator AutoReturnTimer()
    {
        yield return new WaitForSeconds(autoReturnTime);

        if (currentState.Value == FlagState.Dropped)
        {
            ReturnFlag();
            Debug.Log($"✓ {owningTeam} flag auto-returned after timeout");
        }

        autoReturnCoroutine = null;
    }

    /// <summary>
    /// Called when flag state changes (runs on all clients)
    /// </summary>
    private void OnStateChanged(FlagState oldState, FlagState newState)
    {
        Debug.Log($"{owningTeam} flag state changed: {oldState} → {newState}");

        // Update visuals based on state
        switch (newState)
        {
            case FlagState.AtHome:
                if (flagSprite != null)
                    flagSprite.color = Color.white;
                transform.position = homePosition.Value;
                break;

            case FlagState.Carried:
                if (flagSprite != null)
                    flagSprite.color = Color.yellow;
                break;

            case FlagState.Dropped:
                if (flagSprite != null)
                    flagSprite.color = Color.red;
                break;
        }
    }

    /// <summary>
    /// Called when carrier changes (runs on all clients)
    /// </summary>
    private void OnCarrierChanged(ulong oldCarrier, ulong newCarrier)
    {
        // Find carrier GameObject on client
        if (newCarrier != ulong.MaxValue)
        {
            // Find the player with this client ID
            foreach (var networkClient in NetworkManager.Singleton.ConnectedClients.Values)
            {
                if (networkClient.ClientId == newCarrier && networkClient.PlayerObject != null)
                {
                    carrier = networkClient.PlayerObject.gameObject;
                    break;
                }
            }
        }
        else
        {
            carrier = null;
        }
    }

    /// <summary>
    /// Get display name for team
    /// </summary>
    private string GetTeamDisplayName(string teamId)
    {
        if (teamId == "Team1") return "Blue";
        if (teamId == "Team2") return "Red";
        return teamId;
    }

    /// <summary>
    /// Public method for forcing flag drop (called when carrier dies)
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ForceDropServerRpc()
    {
        if (currentState.Value == FlagState.Carried)
        {
            DropFlag();
        }
    }
}