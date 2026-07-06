using Fusion;
using UnityEngine;

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

    // Networked drop location so a dropped flag appears in the same place on every peer.
    // A bare NetworkObject does not replicate its transform (this project uses no
    // NetworkTransform), so without this the dropped/returned flag desyncs on clients.
    [Networked] private Vector3 DropPosition { get; set; }

    // True while DropPosition holds a real drop location. Replaces the Vector3.zero sentinel
    // check, which would break for a flag legitimately dropped at the world origin.
    [Networked] private NetworkBool HasDropPosition { get; set; }

    // Server-evaluated auto-return countdown (simulation-path TickTimer, deterministic).
    [Networked] private TickTimer AutoReturnTimer { get; set; }

    // Local references
    private GameObject carrierGameObject; // RENAMED from 'carrier' to avoid conflict
    private GameObject markedCarrier;      // who currently shows the flag icon on THIS peer

    // Flag states
    public enum FlagState
    {
        AtHome,     // Flag is at home base
        Carried,    // Flag is being carried by a player
        Dropped     // Flag is dropped on the ground
    }

    // Public properties
    public string OwningTeam => owningTeam;

    /// <summary>Canonical Team for this flag (HUD/color lookups). Derived from the authored string.</summary>
    public Team OwningTeamEnum => TeamUtil.Normalize(owningTeam);
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
        }

        // Initialize visuals on all clients based on current state
        OnStateChanged();
    }

    private void Update()
    {
        // IMPORTANT: Only access networked properties if the object has been spawned
        if (Object == null || !Object.IsValid)
            return;

        // Resolve the carrier locally if needed. Doing this here (rather than relying only on
        // the OnChangedRender callback) makes it robust on every peer, including the client.
        if (CurrentState == FlagState.Carried && carrierGameObject == null &&
            CarrierPlayerRef != PlayerRef.None &&
            Runner.TryGetPlayerObject(CarrierPlayerRef, out NetworkObject carrierObj))
        {
            carrierGameObject = carrierObj.gameObject;
        }

        // Position the flag from networked state so every peer agrees on where it is
        // (this project has no NetworkTransform; the transform is driven locally here).
        switch (CurrentState)
        {
            case FlagState.Carried:
                if (carrierGameObject != null)
                    transform.position = carrierGameObject.transform.position + Vector3.up * carrierOffset;
                break;
            case FlagState.Dropped:
                if (HasDropPosition) transform.position = DropPosition;
                break;
            case FlagState.AtHome:
                // Guard against the brief window before HomePosition has replicated on a
                // freshly-joined client (scene flags already sit at home, so this is safe).
                if (HomePosition != Vector3.zero) transform.position = HomePosition;
                break;
        }

        // Reconcile the carrier marker on EVERY peer from the networked state, so all players
        // (not just the holder) see the flag icon above whoever is carrying it.
        GameObject desiredCarrier = CurrentState == FlagState.Carried ? carrierGameObject : null;
        if (desiredCarrier != markedCarrier)
        {
            if (markedCarrier != null) SetCarrierMarker(markedCarrier, false);
            if (desiredCarrier != null) SetCarrierMarker(desiredCarrier, true);
            markedCarrier = desiredCarrier;
        }
    }

    public override void FixedUpdateNetwork()
    {
        // SERVER: auto-return a dropped flag once its countdown elapses.
        if (!HasStateAuthority) return;

        // Carrier disconnect/crash: death drops the flag via PlayerStatsHandler.Die(), but a
        // player who vanishes without dying leaves the flag stuck in Carried forever (the
        // auto-return timer only arms on Drop). If the carrier's player object no longer
        // exists, drop the flag where it is so the auto-return countdown starts.
        if (CurrentState == FlagState.Carried &&
            !Runner.TryGetPlayerObject(CarrierPlayerRef, out _))
        {
            DropFlag();
        }

        if (CurrentState == FlagState.Dropped && AutoReturnTimer.Expired(Runner))
        {
            AutoReturnTimer = default;
            ReturnFlag();
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // Only process on server
        if (!HasStateAuthority) return;

        // Get player components
        GameObject player = collision.gameObject;
        PlayerTeamData playerTeam = player.GetComponent<PlayerTeamData>();
        NetworkObject playerNetworkObject = player.GetComponent<NetworkObject>();

        if (playerTeam == null || playerNetworkObject == null)
            return;

        // Handle different flag states
        switch (CurrentState)
        {
            case FlagState.AtHome:
                // Enemy team can pick up flag from home
                if (playerTeam.Team != TeamUtil.Normalize(owningTeam))
                {
                    PickupFlag(player, playerNetworkObject.InputAuthority);
                }
                break;

            case FlagState.Dropped:
                // Own team returns a dropped flag straight home; the enemy steals it (carries it).
                if (playerTeam.Team == TeamUtil.Normalize(owningTeam))
                    ReturnFlag();
                else
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
        AutoReturnTimer = default;
        HasDropPosition = false;

        // Set flag state
        CurrentState = FlagState.Carried;
        CarrierPlayerRef = playerRef;
        carrierGameObject = player;

        // Set flag carrier marker (if you have this component)
        FlagCarrierMarker marker = player.GetComponent<FlagCarrierMarker>();
        if (marker != null)
        {
            marker.SetCarryingFlag(true);
        }

        // Area of Interest: the carrier must replicate to EVERY player (even distant ones) or the
        // flag-direction HUD and carried-flag position desync for players outside the carrier's
        // region. NetworkRigidbody2D carries no position to a culling client otherwise.
        NetworkObject carrierObj = player.GetComponent<NetworkObject>();
        if (carrierObj != null && AreaOfInterestRegistrar.Instance != null)
            AreaOfInterestRegistrar.Instance.AddAlwaysInterested(carrierObj);

        // Play pickup effect
        if (pickupEffect != null)
            pickupEffect.Play();

        // Notify clients
        PlayerTeamData playerTeam = player.GetComponent<PlayerTeamData>();
        Team owner = TeamUtil.Normalize(owningTeam);
        string notification = playerTeam.Team != owner
            ? $"{TeamUtil.DisplayName(playerTeam.Team)} team has captured {TeamUtil.DisplayName(owner)}'s flag!"
            : $"{TeamUtil.DisplayName(owner)} team is returning their flag!";

        // Notify clients via CTFGameManager
        if (CTFGameManager.Instance != null && HasStateAuthority)
        {
            CTFGameManager.Instance.RPC_ShowNotification(notification);
        }

    }

    /// <summary>
    /// SERVER: Drop the flag at current position
    /// </summary>
    public void DropFlag()
    {
        if (!HasStateAuthority) return;
        if (CurrentState != FlagState.Carried) return;

        // Save drop position (networked so clients see the dropped flag in the right place)
        DropPosition = transform.position;
        HasDropPosition = true;

        // Clear carrier
        if (carrierGameObject != null)
        {
            // Re-enable dash
            FlagCarrierMarker marker = carrierGameObject.GetComponent<FlagCarrierMarker>();
            if (marker != null)
            {
                marker.SetCarryingFlag(false);
            }
            NetworkObject carrierObj = carrierGameObject.GetComponent<NetworkObject>();
            if (carrierObj != null && AreaOfInterestRegistrar.Instance != null)
                AreaOfInterestRegistrar.Instance.RemoveAlwaysInterested(carrierObj);
            carrierGameObject = null;
        }

        // Update state
        CurrentState = FlagState.Dropped;
        CarrierPlayerRef = PlayerRef.None;

        // Play drop effect
        if (dropEffect != null)
            dropEffect.Play();

        // Start auto-return countdown (evaluated server-side in FixedUpdateNetwork)
        AutoReturnTimer = TickTimer.CreateFromSeconds(Runner, autoReturnTime);

        // Notify clients
        if (CTFGameManager.Instance != null && HasStateAuthority)
        {
            CTFGameManager.Instance.RPC_ShowNotification($"{TeamUtil.DisplayName(TeamUtil.Normalize(owningTeam))} flag has been dropped!");
        }

    }

    /// <summary>
    /// SERVER: Return flag to home position
    /// </summary>
    public void ReturnFlag()
    {
        if (!HasStateAuthority) return;

        // Cancel auto-return if running
        AutoReturnTimer = default;
        HasDropPosition = false;

        // Clear carrier
        if (carrierGameObject != null)
        {
            FlagCarrierMarker marker = carrierGameObject.GetComponent<FlagCarrierMarker>();
            if (marker != null)
            {
                marker.SetCarryingFlag(false);
            }
            NetworkObject carrierObj = carrierGameObject.GetComponent<NetworkObject>();
            if (carrierObj != null && AreaOfInterestRegistrar.Instance != null)
                AreaOfInterestRegistrar.Instance.RemoveAlwaysInterested(carrierObj);
            carrierGameObject = null;
        }

        // Reset state
        CurrentState = FlagState.AtHome;
        CarrierPlayerRef = PlayerRef.None;
        transform.position = HomePosition;

        // Notify clients, and re-check captures for any carrier already parked in a base
        // (this flag returning home may complete a pending capture).
        if (CTFGameManager.Instance != null && HasStateAuthority)
        {
            CTFGameManager.Instance.RPC_ShowNotification($"{TeamUtil.DisplayName(TeamUtil.Normalize(owningTeam))} flag has been returned!");
            CTFGameManager.Instance.OnFlagReturnedHome();
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
        // Resolve (or clear) the carrier GameObject on this peer. The marker visual itself is
        // reconciled every frame in Update() from the networked state, so it shows on every
        // peer regardless of exactly when this callback fires.
        if (CarrierPlayerRef != PlayerRef.None)
        {
            if (Runner.TryGetPlayerObject(CarrierPlayerRef, out NetworkObject networkObject))
                carrierGameObject = networkObject.gameObject;
        }
        else
        {
            carrierGameObject = null;
        }
    }

    private static void SetCarrierMarker(GameObject carrier, bool carrying)
    {
        if (carrier == null) return; // carrier may have despawned (player left) mid-carry
        FlagCarrierMarker marker = carrier.GetComponent<FlagCarrierMarker>();
        if (marker != null) marker.SetCarryingFlag(carrying);
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