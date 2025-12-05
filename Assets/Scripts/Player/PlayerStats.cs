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

    [Header("Combat")]
    public float attackDamage = 1f;
    public float attackForce = 5f;
    public float attackCooldown = 0.3f;
    public float maxHealth = 100f;

}