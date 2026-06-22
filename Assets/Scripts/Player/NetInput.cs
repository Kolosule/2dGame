using Fusion;
using UnityEngine;

/// <summary>Button indices used with NetInput.Buttons (NetworkButtons).</summary>
public enum PlayerButton
{
    Jump = 0,
    Dash = 1,
    Melee = 2,
    Shoot = 3,
}

/// <summary>All per-tick player input, collected in NetworkInputProvider.OnInput
/// and consumed in PlayerController.FixedUpdateNetwork.</summary>
public struct NetInput : INetworkInput
{
    public sbyte Horizontal;     // -1 / 0 / 1
    public sbyte VerticalAim;    // -1 / 0 / 1 (for up/down attacks)
    public NetworkButtons Buttons;
    public Vector2 AimWorldPoint; // mouse world position for projectile aim
}
