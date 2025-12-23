using Fusion;
using UnityEngine;
using System.Collections;

/// <summary>
/// Manages flag state and interactions for Capture the Flag mode
/// Attach this to each team's flag GameObject
/// CONVERTED TO PHOTON FUSION - FIXED VERSION
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

    // Network properties to sync flag state across all clients
    [Networked, OnChangedRender(nameof(OnStateChanged))]
    public FlagState CurrentState { get; set; }

    [Networked, OnChangedRender(nameof(OnCarrierPlayerRefChanged))]
    public PlayerRef CarrierPlayerRef { get; set; }

    [Networked]
    public Vector3 HomePosition { get; set; }

    // Local references
    private GameObject carrierGameObject; // RENAMED from 'carrier' to avoid conflict
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
    public FlagState State => CurrentState;
    public GameObject Carrier => carrierGameObject; // Use the renamed variable

    private void Awake()
    {
        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider2D>();

        if (triggerCollider != null)
            triggerCollider.isTrigger = true;

        if (flagSprite == null)
            flagSprite = GetComponent<SpriteRenderer>();
    }

    public override void Spawned()
    {
        // Server sets the home position when flag spawns
        if (HasStateAuthority)
        {
            HomePosition = transform.position;
            CurrentState = FlagState.AtHome;
            CarrierPlayerRef = PlayerRef.None;
            Debug.Log($"✓ {owningTeam} flag spawned at {HomePosition}");
        }

        // Initialize visuals on all clients based on current state
        OnStateChanged();
    }

    private void Update()
    {
        // IMPORTANT: Only access networked properties if the object has been spawned
        if (Object == null || !Object.IsValid)
            return;

        // If flag is being carried, follow the carrier
        if (CurrentState == FlagState.Carried && carrierGameObject != null)
        {
            transform.position = carrierGameObject.transform.position + Vector3.up * carrierOffset;
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // Only process on server
        if (!HasStateAuthority) return;

        // Get player components
        GameObject player = collision.gameObject;
        PlayerTeamComponent playerTeam = player.GetComponent<PlayerTeamComponent>();
        NetworkObject playerNetworkObject = player.GetComponent<NetworkObject>();

        if (playerTeam == null || playerNetworkObject == null)
            return;

        // Handle different flag states
        switch (CurrentState)
        {
            case FlagState.AtHome:
                // Enemy team can pick up flag from home
                if (playerTeam.teamID != owningTeam)
                {
                    PickupFlag(player, playerNetworkObject.InputAuthority);
                }
                break;

            case FlagState.Dropped:
                // Anyone can pick up dropped flag
                PickupFlag(player, playerNetworkObject.InputAuthority);
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
    private void PickupFlag(GameObject player, PlayerRef playerRef)
    {
        if (!HasStateAuthority) return;

        // Cancel auto-return if it was running
        if (autoReturnCoroutine != null)
        {
            StopCoroutine(autoReturnCoroutine);
            autoReturnCoroutine = null;
        }

        // Set flag state
        CurrentState = FlagState.Carried;
        CarrierPlayerRef = playerRef;
        carrierGameObject = player;

        // Disable player's dash (if you have this component)
        PlayerMovement movement = player.GetComponent<PlayerMovement>();
        if (movement != null)
        {
            movement.enabled = false; // Will re-enable when flag is dropped
            movement.enabled = true;
        }

        // Set flag carrier marker (if you have this component)
        FlagCarrierMarker marker = player.GetComponent<FlagCarrierMarker>();
        if (marker != null)
        {
            marker.SetCarryingFlag(true);
        }

        // Play pickup effect
        if (pickupEffect != null)
            pickupEffect.Play();

        // Notify clients
        PlayerTeamComponent playerTeam = player.GetComponent<PlayerTeamComponent>();
        string notification = playerTeam.teamID != owningTeam
            ? $"{GetTeamDisplayName(playerTeam.teamID)} team has captured {GetTeamDisplayName(owningTeam)}'s flag!"
            : $"{GetTeamDisplayName(owningTeam)} team is returning their flag!";

        // Notify clients via CTFGameManager
        if (CTFGameManager.Instance != null && HasStateAuthority)
        {
            CTFGameManager.Instance.RPC_ShowNotification(notification);
        }

        Debug.Log($"✓ {owningTeam} flag picked up by {player.name} (Player {playerRef})");
    }

    /// <summary>
    /// SERVER: Drop the flag at current position
    /// </summary>
    public void DropFlag()
    {
        if (!HasStateAuthority) return;
        if (CurrentState != FlagState.Carried) return;

        // Save drop position
        dropPosition = transform.position;

        // Clear carrier
        if (carrierGameObject != null)
        {
            // Re-enable dash
            FlagCarrierMarker marker = carrierGameObject.GetComponent<FlagCarrierMarker>();
            if (marker != null)
            {
                marker.SetCarryingFlag(false);
            }
            carrierGameObject = null;
        }

        // Update state
        CurrentState = FlagState.Dropped;
        CarrierPlayerRef = PlayerRef.None;

        // Play drop effect
        if (dropEffect != null)
            dropEffect.Play();

        // Start auto-return timer
        autoReturnCoroutine = StartCoroutine(AutoReturnTimer());

        // Notify clients
        if (CTFGameManager.Instance != null && HasStateAuthority)
        {
            CTFGameManager.Instance.RPC_ShowNotification($"{GetTeamDisplayName(owningTeam)} flag has been dropped!");
        }

        Debug.Log($"✓ {owningTeam} flag dropped at {dropPosition}");
    }

    /// <summary>
    /// SERVER: Return flag to home position
    /// </summary>
    public void ReturnFlag()
    {
        if (!HasStateAuthority) return;

        // Cancel auto-return if running
        if (autoReturnCoroutine != null)
        {
            StopCoroutine(autoReturnCoroutine);
            autoReturnCoroutine = null;
        }

        // Clear carrier
        if (carrierGameObject != null)
        {
            FlagCarrierMarker marker = carrierGameObject.GetComponent<FlagCarrierMarker>();
            if (marker != null)
            {
                marker.SetCarryingFlag(false);
            }
            carrierGameObject = null;
        }

        // Reset state
        CurrentState = FlagState.AtHome;
        CarrierPlayerRef = PlayerRef.None;
        transform.position = HomePosition;

        // Notify clients
        if (CTFGameManager.Instance != null && HasStateAuthority)
        {
            CTFGameManager.Instance.RPC_ShowNotification($"{GetTeamDisplayName(owningTeam)} flag has been returned!");
        }

        Debug.Log($"✓ {owningTeam} flag returned to home");
    }

    /// <summary>
    /// Auto-return timer coroutine
    /// </summary>
    private IEnumerator AutoReturnTimer()
    {
        yield return new WaitForSeconds(autoReturnTime);

        if (CurrentState == FlagState.Dropped)
        {
            ReturnFlag();
        }
    }

    /// <summary>
    /// Called when flag state changes (via OnChangedRender attribute)
    /// </summary>
    private void OnStateChanged()
    {
        UpdateVisuals();
    }

    /// <summary>
    /// Called when carrier PlayerRef changes (via OnChangedRender attribute)
    /// </summary>
    private void OnCarrierPlayerRefChanged()
    {
        // Find carrier GameObject on client
        if (CarrierPlayerRef != PlayerRef.None)
        {
            // Try to find the player with this PlayerRef
            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                if (player == CarrierPlayerRef)
                {
                    // Get the network object for this player
                    if (Runner.TryGetPlayerObject(player, out NetworkObject networkObject))
                    {
                        carrierGameObject = networkObject.gameObject;
                        Debug.Log($"Found carrier: {carrierGameObject.name}");
                        break;
                    }
                }
            }
        }
        else
        {
            carrierGameObject = null;
        }
    }

    /// <summary>
    /// Update visual feedback based on current state
    /// </summary>
    private void UpdateVisuals()
    {
        if (flagSprite == null) return;

        switch (CurrentState)
        {
            case FlagState.AtHome:
                flagSprite.color = Color.white;
                flagSprite.enabled = true;
                break;

            case FlagState.Carried:
                flagSprite.color = new Color(1f, 1f, 1f, 0.7f); // Slightly transparent
                flagSprite.enabled = true;
                break;

            case FlagState.Dropped:
                flagSprite.color = new Color(1f, 0.5f, 0.5f); // Reddish tint
                flagSprite.enabled = true;
                break;
        }
    }

    /// <summary>
    /// SERVER RPC: Request to drop the flag
    /// Called when carrier dies or manually drops
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void DropFlagRpc()
    {
        DropFlag();
    }

    /// <summary>
    /// SERVER RPC: Request to return flag
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void ReturnFlagRpc()
    {
        ReturnFlag();
    }

    /// <summary>
    /// Get display name for a team
    /// </summary>
    private string GetTeamDisplayName(string teamId)
    {
        switch (teamId)
        {
            case "Team1":
            case "Blue":
                return "Blue";
            case "Team2":
            case "Red":
                return "Red";
            default:
                return teamId;
        }
    }

    /// <summary>
    /// Check if a specific player is carrying this flag
    /// </summary>
    public bool IsCarriedBy(PlayerRef playerRef)
    {
        return CurrentState == FlagState.Carried && CarrierPlayerRef == playerRef;
    }

    /// <summary>
    /// Get the distance to home position
    /// </summary>
    public float GetDistanceToHome()
    {
        return Vector3.Distance(transform.position, HomePosition);
    }

    private void OnDrawGizmos()
    {
        // Draw home position in editor
        if (Application.isPlaying && HomePosition != Vector3.zero)
        {
            Gizmos.color = owningTeam == "Team1" ? Color.blue : Color.red;
            Gizmos.DrawWireSphere(HomePosition, 0.5f);
            Gizmos.DrawLine(transform.position, HomePosition);
        }
    }
}