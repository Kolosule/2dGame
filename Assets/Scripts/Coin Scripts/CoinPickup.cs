using UnityEngine;
using Fusion;

/// <summary>
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

    // Network property to track if coin has been collected
    [Networked]
    private NetworkBool IsCollected { get; set; }

    /// <summary>
    /// Public property to access coin data from other scripts
    /// </summary>
    public CoinData CoinDataProperty => coinData;

    private void Start()
    {
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
    }

    /// <summary>
    /// Called when another object enters this coin's trigger collider
    /// Only processes on the client with input authority (local player)
    /// </summary>
    /// <summary>
    /// Called when another object enters this coin's trigger collider
    /// Only processes on the client with input authority (local player)
    /// </summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        Debug.Log($"[CoinPickup] Trigger entered by: {collision.gameObject.name}");

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
                // Request pickup from server
                RPC_RequestPickup(player.Object.InputAuthority);
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
    /// RPC to request coin pickup. Called by client, executed on server.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestPickup(PlayerRef player)
    {
        // Double-check coin hasn't been collected (race condition protection)
        if (IsCollected) return;

        // Find the player's network object
        NetworkObject playerNetObj;
        if (Runner.TryGetPlayerObject(player, out playerNetObj))
        {
            NetworkedPlayerInventory inventory = playerNetObj.GetComponent<NetworkedPlayerInventory>();

            if (inventory != null)
            {
                // Try to add coin to player's inventory
                bool pickedUp = inventory.ServerAddCoin(coinData);

                if (pickedUp)
                {
                    // Mark as collected
                    IsCollected = true;

                    // Notify all clients to play effects and destroy
                    RPC_OnCoinCollected(playerNetObj.transform.position);

                    // Despawn the coin (server authority)
                    Runner.Despawn(Object);
                }
            }
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