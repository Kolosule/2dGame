using UnityEngine;
using UnityEngine.InputSystem;
using Fusion;

/// <summary>
/// FIXED VERSION - Now properly handles coin deposits by passing NetworkObject directly!
/// Networked home base for Photon Fusion.
/// Players deposit coins here to score points for their team.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class NetworkedHomeBase : NetworkBehaviour
{
    [Header("Base Settings")]
    [Tooltip("Which team this base belongs to: 'Team1' or 'Team2'")]
    [SerializeField] private string baseTeam;

    [Header("Deposit Settings")]
    [Tooltip("Should deposit happen automatically on enter, or require a button press?")]
    [SerializeField] private bool autoDeposit = true;

    [Tooltip("If not auto-deposit, which key to press (default: E)")]
    [SerializeField] private Key depositKey = Key.E;

    [Header("Audio (Optional)")]
    [Tooltip("Sound to play when coins are deposited")]
    [SerializeField] private AudioClip depositSound;

    [Header("Visual Feedback (Optional)")]
    [Tooltip("Particle effect to spawn when depositing")]
    [SerializeField] private GameObject depositEffect;

    // Track which player is currently in the base zone (per client)
    private NetworkedPlayerInventory playerInZone = null;

    private void Start()
    {
        // Ensure the collider is set to trigger mode
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }

        if (string.IsNullOrEmpty(baseTeam))
        {
            Debug.LogError($"NetworkedHomeBase on {gameObject.name} has no team assigned!");
        }
    }

    /// <summary>
    /// Called when a player enters the base trigger zone
    /// </summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        NetworkedPlayerInventory player = collision.GetComponent<NetworkedPlayerInventory>();

        if (player != null)
        {
            // Only process for local player
            if (player.HasInputAuthority)
            {
                // Check if player is on the same team as this base
                if (IsPlayerOnCorrectTeam(player))
                {
                    playerInZone = player;

                    // If auto-deposit is enabled, deposit immediately
                    if (autoDeposit)
                    {
                        RequestDeposit(player);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Called when a player leaves the base trigger zone
    /// </summary>
    private void OnTriggerExit2D(Collider2D collision)
    {
        NetworkedPlayerInventory player = collision.GetComponent<NetworkedPlayerInventory>();

        if (player != null && player == playerInZone)
        {
            playerInZone = null;
        }
    }

    /// <summary>
    /// Check for deposit key press if manual deposit is enabled
    /// </summary>
    private void Update()
    {
        // Local, non-networked convenience read: playerInZone is only ever set for the
        // input-authority (local) player, and this only fires an RPC — it never feeds the
        // Fusion simulation, so reading the device directly here is safe.
        if (!autoDeposit && playerInZone != null &&
            Keyboard.current != null && Keyboard.current[depositKey].wasPressedThisFrame)
        {
            RequestDeposit(playerInZone);
        }
    }

    /// <summary>
    /// Requests a deposit from the client (sends RPC to server)
    /// </summary>
    private void RequestDeposit(NetworkedPlayerInventory player)
    {
        if (player.CoinCount == 0)
        {
            return;
        }


        // FIXED: Send the NetworkObject directly instead of PlayerRef
        RPC_RequestDeposit(player.Object);
    }

    /// <summary>
    /// FIXED - RPC to request coin deposit. Called by client, executed on server.
    /// Now receives NetworkObject directly instead of PlayerRef to avoid lookup issues.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDeposit(NetworkObject playerNetObj)
    {

        // Validate the player NetworkObject
        if (playerNetObj == null || !playerNetObj.IsValid)
        {
            Debug.LogError("[SERVER] Invalid player NetworkObject passed to RPC_RequestDeposit");
            return;
        }


        // Get the inventory component
        NetworkedPlayerInventory inventory = playerNetObj.GetComponent<NetworkedPlayerInventory>();

        if (inventory != null)
        {

            // Verify player is on correct team (server-side check)
            if (!IsPlayerOnCorrectTeam(inventory))
            {
                Debug.LogWarning($"[SERVER] Player tried to deposit at wrong base!");
                return;
            }

            // Get points from player's inventory
            int points = inventory.ServerDepositCoins();

            if (points > 0)
            {

                // Add points to team score through the TeamScoreManager
                TeamScoreManager scoreManager = TeamScoreManager.Instance;
                if (scoreManager != null)
                {
                    scoreManager.RPC_AddPoints(baseTeam, points);


                    // Notify all clients to play effects
                    RPC_OnDeposit(playerNetObj.transform.position, points);
                }
                else
                {
                    Debug.LogError("[SERVER] TeamScoreManager not found in scene!");
                }
            }
        }
        else
        {
            Debug.LogError($"[SERVER] No NetworkedPlayerInventory component found on {playerNetObj.name}!");
        }
    }

    /// <summary>
    /// RPC to notify all clients that a deposit occurred (for visual/audio feedback)
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnDeposit(Vector3 playerPosition, int points)
    {

        // Play deposit sound if assigned
        if (depositSound != null)
        {
            AudioSource.PlayClipAtPoint(depositSound, transform.position);
        }

        // Spawn deposit effect if assigned
        if (depositEffect != null)
        {
            GameObject effect = Instantiate(depositEffect, playerPosition, Quaternion.identity);
            Destroy(effect, 2f);
        }

    }

    /// <summary>
    /// Checks if player is on the correct team for this base
    /// Handles multiple team naming conventions
    /// </summary>
    private bool IsPlayerOnCorrectTeam(NetworkedPlayerInventory player)
    {
        Team playerTeam = player.PlayerTeam;
        Team baseTeamEnum = TeamUtil.Normalize(baseTeam);
        return playerTeam != Team.None && playerTeam == baseTeamEnum;
    }
}