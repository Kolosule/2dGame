using UnityEngine;
using Fusion;

[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerCombat))]
[RequireComponent(typeof(NetworkPlayerWrapper))]
public class PlayerController : NetworkBehaviour
{
    private PlayerMovement movement;
    private PlayerCombat combat;
    private NetworkPlayerWrapper networkWrapper;

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        combat = GetComponent<PlayerCombat>();
        networkWrapper = GetComponent<NetworkPlayerWrapper>();
    }

    void Update()
    {
        // ⭐ CRITICAL FIX: Only process input for the local player
        if (!networkWrapper.HasInputAuthority)
        {
            return; // Not our player - ignore
        }

        // This IS our player - process input
        movement.HandleInput();
        combat.HandleInput();
    }
}