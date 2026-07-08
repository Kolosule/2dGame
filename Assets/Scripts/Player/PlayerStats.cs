using UnityEngine;

[CreateAssetMenu(menuName = "Player/Stats", fileName = "PlayerStats")]
public class PlayerStats : ScriptableObject
{
    [Header("Movement")]
    public float walkSpeed = 5f;
    public float jumpForce = 10f;
    public int maxAirJumps = 1;
    public float dashSpeed = 15f;
    public float dashTime = 0.2f;
    public float dashCooldown = 1f;

    [Header("Acceleration (ticks to reach walkSpeed)")]
    public int groundAccelTicks = 4;
    public int groundDecelTicks = 3;
    public int airAccelTicks = 10;
    public int airDecelTicks = 18;

    [Header("Momentum (decay of speed above walkSpeed, units/s^2)")]
    public float momentumDecayAir = 8f;
    public float momentumDecayGround = 40f;

    [Header("Dash-Jump")]
    [Range(0f, 1f)] public float dashJumpCarryFactor = 0.65f;

    [Header("Jump Arc")]
    public float fallGravityMult = 1.7f;
    public float apexGravityMult = 0.5f;
    [Tooltip("Vertical speed band around the jump apex that gets the apex-hang gravity")]
    public float apexThreshold = 1.5f;
    public float fastFallSpeed = 18f;
    public float maxFallSpeed = 20f;

    [Header("Combat")]
    public float attackDamage = 1f;
    public float attackForce = 5f;
    public float attackCooldown = 0.3f;
    public float maxHealth = 100f;

    [Header("Melee Swing Phases (ticks)")]
    public int attackStartupTicks = 3;
    public int attackActiveTicks = 5;
    public int attackRecoveryTicks = 10;

}