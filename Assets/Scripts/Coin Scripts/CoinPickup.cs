using UnityEngine;
using Fusion;
using System.Collections;

/// <summary>
/// FIXED VERSION - Now properly handles coin pickup by passing NetworkObject directly!
/// Networked coin pickup for Photon Fusion.
/// Handles coin collection and syncs across all clients.
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

    [Header("Spawn Settings")]
    [Tooltip("How long to wait after spawning before allowing pickup (prevents instant pickup)")]
    [SerializeField] private float spawnDelay = 0.1f;

    // Network property to track if coin has been collected
    [Networked]
    private NetworkBool IsCollected { get; set; }

    // Track if we're ready to be picked up
    private bool isReadyForPickup = false;
    private bool hasStartedInitialization = false;

    /// <summary>
    /// Public property to access coin data from other scripts
    /// </summary>
    public CoinData CoinDataProperty => coinData;

    public override void Spawned()
    {
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

        // Ensure the collider is set to trigger mode
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
        Debug.Log($"[CoinPickup] {gameObject.name} initialized and ready for pickup");
    }

    /// <summary>
    /// FIXED - Called when another object enters this coin's trigger collider
    /// Now properly passes NetworkObject reference to avoid lookup issues
    /// </summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        Debug.Log($"[CoinPickup] Trigger entered by: {collision.gameObject.name}");

        // IMPORTANT: Check if coin is ready for pickup
        if (!isReadyForPickup)
        {
            Debug.Log("[CoinPickup] Coin not ready for pickup yet (still initializing)");
            return;
        }

        // IMPORTANT: Only access networked properties if the object has been spawned
        if (Object == null || !Object.IsValid)
        {
            Debug.LogWarning("[CoinPickup] NetworkObject not valid yet");
            return;
        }

        // Only process if this coin hasn't been collected yet
        if (IsCollected)
        {
            Debug.Log("[CoinPickup] Coin already collected, ignoring");
            return;
        }

        // Check if the object that touched the coin is a player
        NetworkedPlayerInventory player = collision.GetComponent<NetworkedPlayerInventory>();

        if (player != null && coinData != null)
        {
            Debug.Log($"[CoinPickup] Player detected: {player.name}, HasInputAuthority: {player.HasInputAuthority}");

            // Only the local player should request pickup
            if (player.HasInputAuthority)
            {
                Debug.Log("[CoinPickup] Requesting pickup from server");

                // FIXED: Pass the player's NetworkObject directly instead of PlayerRef
                // This avoids the Runner.TryGetPlayerObject() lookup issue
                RPC_RequestPickup(player.Object);
            }
        }
        else
        {
            if (player == null)
            {
                Debug.Log($"[CoinPickup] No NetworkedPlayerInventory found on {collision.gameObject.name}");
            }
            if (coinData == null)
            {
                Debug.LogError("[CoinPickup] CoinData is NULL! Assign it in the Inspector!");
            }
        }
    }

    /// <summary>
    /// Optional: Makes coins slowly rotate for visual appeal
    /// </summary>
    private void Update()
    {
        // IMPORTANT: Only access networked properties if the object has been spawned
        if (Object == null || !Object.IsValid)
            return;

        if (rotateVisual && !IsCollected)
        {
            // Rotate the coin around the Z axis (for 2D)
            transform.Rotate(0, 0, rotationSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// FIXED - RPC to request coin pickup. Called by client, executed on server.
    /// Now receives NetworkObject directly instead of PlayerRef to avoid lookup issues.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestPickup(NetworkObject playerNetObj)
    {
        Debug.Log("[SERVER] RPC_RequestPickup called");

        // Double-check coin hasn't been collected (race condition protection)
        if (IsCollected)
        {
            Debug.Log("[SERVER] Coin already collected, ignoring pickup request");
            return;
        }

        // Validate the player NetworkObject
        if (playerNetObj == null || !playerNetObj.IsValid)
        {
            Debug.LogError("[SERVER] Invalid player NetworkObject passed to RPC_RequestPickup");
            return;
        }

        Debug.Log($"[SERVER] Processing pickup for {playerNetObj.name}");

        // Get the inventory component
        NetworkedPlayerInventory inventory = playerNetObj.GetComponent<NetworkedPlayerInventory>();

        if (inventory != null)
        {
            Debug.Log("[SERVER] Found NetworkedPlayerInventory component");

            // Try to add coin to player's inventory
            bool pickedUp = inventory.ServerAddCoin(coinData);

            if (pickedUp)
            {
                Debug.Log("[SERVER] Coin successfully added to inventory - despawning coin");

                // Mark as collected
                IsCollected = true;

                // Notify all clients to play effects and destroy
                RPC_OnCoinCollected(playerNetObj.transform.position);

                // Despawn the coin (server authority)
                Runner.Despawn(Object);
            }
            else
            {
                Debug.LogWarning("[SERVER] Failed to add coin to inventory (inventory might be full)");
            }
        }
        else
        {
            Debug.LogError($"[SERVER] No NetworkedPlayerInventory component found on {playerNetObj.name}!");
        }
    }

    /// <summary>
    /// RPC to notify all clients that coin was collected.
    /// Used for visual/audio feedback.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnCoinCollected(Vector3 playerPosition)
    {
        Debug.Log("[CLIENT] Coin collected - playing effects");

        // Play pickup sound if assigned
        if (pickupSound != null)
        {
            AudioSource.PlayClipAtPoint(pickupSound, transform.position);
        }

        // You can add particle effects here
        // Example: Instantiate(pickupEffect, transform.position, Quaternion.identity);
    }
}