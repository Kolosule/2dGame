using UnityEngine;

[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerCombat))]
public class PlayerController : MonoBehaviour
{
    private PlayerMovement movement;
    private PlayerCombat combat;

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        combat = GetComponent<PlayerCombat>();
    }

    void Update()
    {
        // Delegate input handling
        movement.HandleInput();
        combat.HandleInput();
    }
}