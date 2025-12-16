using UnityEngine;

/// <summary>
/// Attached to coin GameObjects. Handles pickup detection and coin properties.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class CoinPickup : MonoBehaviour
{
    [Header("Coin Properties")]
    [Tooltip("The data asset that defines this coin's values for each team")]
    [SerializeField] private CoinData coinData;
    
    [Header("Visual Feedback (Optional)")]
    [Tooltip("Sound to play when coin is picked up")]
    [SerializeField] private AudioClip pickupSound;
    
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
            Debug.LogError($"CoinPickup on {gameObject.name} is missing CoinData! Please assign it in the Inspector.");
        }
    }
    
    /// <summary>
    /// Called when another object enters this coin's trigger collider
    /// </summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        // Check if the object that touched the coin is a player
        PlayerInventory player = collision.GetComponent<PlayerInventory>();
        
        if (player != null && coinData != null)
        {
            // Try to add coin to player's inventory
            bool pickedUp = player.AddCoin(this);
            
            if (pickedUp)
            {
                // Play pickup sound if assigned
                if (pickupSound != null)
                {
                    AudioSource.PlayClipAtPoint(pickupSound, transform.position);
                }
                
                // Remove the coin from the game world
                Destroy(gameObject);
            }
        }
    }
    
    /// <summary>
    /// Optional: Makes coins slowly rotate for visual appeal
    /// Remove this method if you don't want rotation
    /// </summary>
    private void Update()
    {
        // Rotate the coin around the Z axis (for 2D)
        transform.Rotate(0, 0, 90f * Time.deltaTime);
    }
}
