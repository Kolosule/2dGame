using UnityEngine;
using Fusion;
using System.Collections;

/// <summary>
/// Networked coin pickup for Photon Fusion.
///
/// Pickup is HOST-AUTHORITATIVE: the state authority simulates every player, so it polls for an
/// overlapping player each tick and collects the coin directly. The old design relied on each
/// client's local trigger firing on a proxy coin + an RPC, which is unreliable for the joining
/// client - that is why coins were visible but un-collectable for player 2.
///
/// Pop + fall also run only on the state authority (simple manual integration + a downward
/// ground raycast); the resulting position is replicated through SyncedPosition because this
/// project uses no NetworkTransform.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class NetworkedCoinPickup : NetworkBehaviour
{
    [Header("Coin Properties")]
    [Tooltip("The data asset that defines this coin's values for each team")]
    [SerializeField] private CoinData coinData;

    [Header("Visual Feedback (Optional)")]
    [Tooltip("Sound to play when coin is picked up")]
    [SerializeField] private AudioClip pickupSound;

    [Header("Rotation (Optional)")]
    [Tooltip("Should the coin rotate for visual effect?")]
    [SerializeField] private bool rotateVisual = true;

    [Tooltip("Rotation speed in degrees per second")]
    [SerializeField] private float rotationSpeed = 90f;

    [Header("Pop + Fall (server simulated)")]
    [Tooltip("Downward acceleration applied while the coin is falling")]
    [SerializeField] private float gravity = 30f;

    [Tooltip("Upward launch speed when the coin pops out (randomized between min/max)")]
    [SerializeField] private float popUpSpeedMin = 4f;
    [SerializeField] private float popUpSpeedMax = 7f;

    [Tooltip("Maximum sideways launch speed when the coin pops out")]
    [SerializeField] private float popSideSpeed = 3f;

    [Header("Pickup")]
    [Tooltip("Radius around the coin used to detect an overlapping player")]
    [SerializeField] private float pickupRadius = 0.6f;

    [Header("Spawn Settings")]
    [Tooltip("How long to wait after spawning before allowing pickup (prevents instant pickup)")]
    [SerializeField] private float spawnDelay = 0.25f;

    // Network property to track if coin has been collected
    [Networked]
    private NetworkBool IsCollected { get; set; }

    // Networked position. Coins are spawned by the server (Runner.Spawn) at an enemy's death /
    // a player's drop location and then pop + fall on the server; a bare NetworkObject does not
    // replicate its transform (this project uses no NetworkTransform), so the authority writes
    // its position here every tick and remote peers follow it.
    [Networked]
    private Vector3 SyncedPosition { get; set; }

    // Server-only physics state.
    private Vector2 velocity;
    private bool grounded;
    private int groundMask;
    private int playerMask;
    private float restOffset;

    // Track if we're ready to be picked up
    private bool isReadyForPickup = false;
    private bool hasStartedInitialization = false;

    // Pickup query reuse: TryServerPickup runs every tick per live coin on the authority. The old
    // Physics2D.OverlapCircleAll allocated a fresh Collider2D[] each call (~1.3k allocs/s on the
    // host at 20 coins × 64 Hz). The ContactFilter2D + reusable List overload is allocation-free
    // after warmup; it runs authority-only, single-threaded, and consumes results immediately, so a
    // shared static buffer is safe.
    private static readonly System.Collections.Generic.List<Collider2D> PickupResults =
        new System.Collections.Generic.List<Collider2D>(8);
    private ContactFilter2D playerFilter;

    /// <summary>
    /// Public property to access coin data from other scripts
    /// </summary>
    public CoinData CoinDataProperty => coinData;

    public override void Spawned()
    {
        groundMask = LayerMask.GetMask("Ground");
        playerMask = LayerMask.GetMask("Player");

        // Build the pickup filter once (matches the old OverlapCircleAll: player layer mask,
        // triggers included per the default global query behavior).
        playerFilter = new ContactFilter2D { useTriggers = true };
        playerFilter.SetLayerMask(playerMask);

        // Distance from the coin centre to its bottom, so it rests on top of the ground.
        CircleCollider2D circle = GetComponent<CircleCollider2D>();
        restOffset = circle != null ? circle.radius * Mathf.Abs(transform.lossyScale.y) : 0.1f;

        if (HasStateAuthority)
        {
            // Capture the spawn position and give the coin its initial "pop".
            SyncedPosition = transform.position;
            velocity = new Vector2(
                Random.Range(-popSideSpeed, popSideSpeed),
                Random.Range(popUpSpeedMin, popUpSpeedMax));
        }
        else
        {
            // Remote peers snap to the replicated position immediately.
            transform.position = SyncedPosition;
        }

        // Start initialization when spawned on the network
        if (!hasStartedInitialization)
        {
            hasStartedInitialization = true;
            StartCoroutine(InitializeCoin());
        }
    }

    /// <summary>
    /// Initialize coin after a short delay to ensure everything is ready
    /// </summary>
    private IEnumerator InitializeCoin()
    {
        // Wait for spawn delay
        yield return new WaitForSeconds(spawnDelay);

        // Ensure the collider is set to trigger mode (the pickup query hits it either way; a
        // trigger keeps the coin from physically shoving players that walk into it).
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }

        // Warn if coin data is missing
        if (coinData == null)
        {
            Debug.LogError($"NetworkedCoinPickup on {gameObject.name} is missing CoinData! Please assign it in the Inspector.");
        }

        // Mark as ready for pickup
        isReadyForPickup = true;
    }

    /// <summary>
    /// SERVER: run pop/fall integration and poll for a collecting player. Runs in the fixed
    /// network tick so Physics2D queries see up-to-date positions (same path PlayerCombat melee
    /// uses successfully).
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || IsCollected) return;

        float dt = Runner.DeltaTime;

        if (!grounded)
        {
            velocity.y -= gravity * dt;
            Vector3 pos = transform.position;
            Vector2 step = velocity * dt;

            // While moving down, stop on the first ground collider we would pass through.
            if (velocity.y < 0f)
            {
                float castDist = Mathf.Abs(step.y) + restOffset + 0.05f;
                RaycastHit2D hit = Physics2D.Raycast(pos, Vector2.down, castDist, groundMask);
                if (hit.collider != null)
                {
                    pos.x += step.x;
                    pos.y = hit.point.y + restOffset;
                    transform.position = pos;
                    velocity = Vector2.zero;
                    grounded = true;
                }
                else
                {
                    transform.position = pos + (Vector3)step;
                }
            }
            else
            {
                transform.position = pos + (Vector3)step;
            }

            SyncedPosition = transform.position;
        }

        if (isReadyForPickup)
            TryServerPickup();
    }

    /// <summary>SERVER: collect the coin if a player overlaps it.</summary>
    private void TryServerPickup()
    {
        if (coinData == null) return;

        int count = Physics2D.OverlapCircle(transform.position, pickupRadius, playerFilter, PickupResults);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = PickupResults[i];
            NetworkedPlayerInventory inventory = hit.GetComponent<NetworkedPlayerInventory>();
            if (inventory == null) continue;

            // A dead player's frozen body should not vacuum up the coins it just dropped.
            PlayerStatsHandler health = hit.GetComponent<PlayerStatsHandler>();
            if (health != null && health.IsPlayerDead()) continue;

            if (inventory.ServerAddCoin(coinData))
            {
                IsCollected = true;
                RPC_OnCoinCollected(transform.position);
                Runner.Despawn(Object);
                return; // coin is gone
            }
            // This player's inventory is full (maxCoins cap) — let another overlapping
            // player collect it this tick instead of permanently blocking the coin.
        }
    }

    /// <summary>
    /// Rotation visual + remote position follow.
    /// </summary>
    private void Update()
    {
        // IMPORTANT: Only access networked properties if the object has been spawned
        if (Object == null || !Object.IsValid)
            return;

        // Keep remote coins pinned to the networked position (no NetworkTransform).
        if (!HasStateAuthority)
            transform.position = SyncedPosition;

        if (rotateVisual && !IsCollected)
        {
            // Rotate the coin around the Z axis (for 2D)
            transform.Rotate(0, 0, rotationSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// RPC to notify all clients that coin was collected.
    /// Used for visual/audio feedback.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnCoinCollected(Vector3 playerPosition)
    {
        // Play pickup sound if assigned
        if (pickupSound != null)
        {
            AudioSource.PlayClipAtPoint(pickupSound, transform.position);
        }

        // You can add particle effects here
        // Example: Instantiate(pickupEffect, transform.position, Quaternion.identity);
    }
}
